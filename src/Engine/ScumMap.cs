using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ZiloreMiniMap {
    public sealed class ScumMapCategory {
        public int Id;
        public string Section;
        public string Name;
        public int ColorBackground;
        public int ColorIcon;
        public bool DefaultEnabled;
        public int MarkerCount;

        public override string ToString() {
            return string.Format(CultureInfo.InvariantCulture, "{0} ({1})", Name, MarkerCount);
        }
    }

    public sealed class ScumMapMarker {
        public int Id;
        public int CategoryId;
        public float X; // 0..1 normalized map coordinate
        public float Y; // 0..1 normalized map coordinate
        public string Title;
        internal string CachedDisplayName;

        public PointF Point { get { return new PointF(X, Y); } }
    }

    public sealed class ScumMapHabitat {
        public string Name,Summary;
        public int Color;
        public int[] CategoryIds;
        public PointF[][] Rings;
        internal float MinX, MaxX, MinY, MaxY;
        internal bool BoundsReady;
        public void ComputeBounds() {
            if(Rings==null || Rings.Length==0) { MinX=MaxX=MinY=MaxY=0; BoundsReady=true; return; }
            MinX=float.MaxValue; MaxX=float.MinValue;
            MinY=float.MaxValue; MaxY=float.MinValue;
            foreach(PointF[] ring in Rings) {
                if(ring==null) continue;
                for(int i=0; i<ring.Length; i++) {
                    float x=ring[i].X, y=ring[i].Y;
                    if(x<MinX) MinX=x; if(x>MaxX) MaxX=x;
                    if(y<MinY) MinY=y; if(y>MaxY) MaxY=y;
                }
            }
            BoundsReady=true;
        }
        public bool Contains(PointF point) {
            if(Rings==null || Rings.Length==0) return false;
            if(!BoundsReady) ComputeBounds();
            if(point.X < MinX || point.X > MaxX || point.Y < MinY || point.Y > MaxY) return false;
            int winding=0;
            for(int ri=0; ri<Rings.Length; ri++) {
                PointF[] ring=Rings[ri];
                if(ring==null || ring.Length<3) continue;
                for(int i=0,j=ring.Length-1;i<ring.Length;j=i++) {
                    PointF a=ring[j],b=ring[i];
                    double cross=((double)b.X-a.X)*(point.Y-a.Y)-((double)point.X-a.X)*(b.Y-a.Y);
                    if(Math.Abs(cross)<1e-12 && point.X>=Math.Min(a.X,b.X) && point.X<=Math.Max(a.X,b.X) && point.Y>=Math.Min(a.Y,b.Y) && point.Y<=Math.Max(a.Y,b.Y)) return true;
                    if(a.Y<=point.Y) { if(b.Y>point.Y && cross>0) winding++; }
                    else if(b.Y<=point.Y && cross<0) winding--;
                }
            }
            return winding!=0;
        }
        public GraphicsPath CreatePath() {
            GraphicsPath path=new GraphicsPath(FillMode.Winding);
            foreach(PointF[] ring in Rings) if(ring.Length>=3) path.AddPolygon(ring);
            return path;
        }
    }

    public sealed class ScumMapStore {
        public readonly List<ScumMapHabitat> Habitats=new List<ScumMapHabitat>();
        public readonly List<ScumMapCategory> Categories = new List<ScumMapCategory>();
        public readonly Dictionary<int, ScumMapCategory> CategoryById = new Dictionary<int, ScumMapCategory>();
        public readonly List<string> Sections = new List<string>();
        public readonly Dictionary<string, List<ScumMapCategory>> CategoriesBySection = new Dictionary<string, List<ScumMapCategory>>(StringComparer.OrdinalIgnoreCase);
        public readonly List<ScumMapMarker> Markers = new List<ScumMapMarker>();

        // 32x32 spatial index covering 0..1 map coordinate space
        const int GridSize = 32;
        readonly List<int>[,] _grid = new List<int>[GridSize, GridSize];

        // Filter state
        readonly HashSet<int> _disabledCategories = new HashSet<int>();
        string cachedDisabledCategoriesKey;
        string cachedFilterHashKey;
        bool cachedFilterMasterEnabled;
        public bool MasterEnabled = true;

        public struct SettlementAnchor {
            public string Name;
            public int CategoryId;
            public float X;
            public float Y;
            public float Radius;
        }
        readonly List<SettlementAnchor> _settlements = new List<SettlementAnchor>();
        public List<SettlementAnchor> Settlements { get { return _settlements; } }

        public void IndexSettlements() {
            _settlements.Clear();
            for (int i = 0; i < Markers.Count; i++) Markers[i].CachedDisplayName = null;
            for (int i = 0; i < Markers.Count; i++) {
                ScumMapMarker sm = Markers[i];
                if (sm.CategoryId == 27 && !string.IsNullOrEmpty(sm.Title)) {
                    _settlements.Add(new SettlementAnchor { Name = sm.Title.Trim(), CategoryId=sm.CategoryId, X = sm.X, Y = sm.Y, Radius = 0.020f });
                } else if (sm.CategoryId == 26 && !string.IsNullOrEmpty(sm.Title) && (sm.Title.IndexOf("City", StringComparison.OrdinalIgnoreCase) >= 0 || sm.Title.Equals("Novigrad", StringComparison.OrdinalIgnoreCase))) {
                    string sname = sm.Title.Replace(" City", "").Trim();
                    _settlements.Add(new SettlementAnchor { Name = sname, CategoryId=sm.CategoryId, X = sm.X, Y = sm.Y, Radius = 0.040f });
                } else if (sm.CategoryId == 80 && !string.IsNullOrEmpty(sm.Title)) {
                    _settlements.Add(new SettlementAnchor { Name = sm.Title.Trim(), CategoryId=sm.CategoryId, X = sm.X, Y = sm.Y, Radius = 0.015f });
                }
            }
        }

        public static string ActivitySection(int categoryId) {
            switch(categoryId) {
                case 858: case 81: return "Fishing";
                case 869: case 859: case 17: case 776: case 24: return "Hunting";
                default:
                    if (categoryId <= -1000 && categoryId > -2000) return "Fishing";
                    if (categoryId <= -2000 && categoryId > -3000) return "Hunting";
                    return null;
            }
        }

        public ScumMapStore() {
            for (int x = 0; x < GridSize; x++) {
                for (int y = 0; y < GridSize; y++) {
                    _grid[x, y] = new List<int>();
                }
            }
        }

        public static ScumMapStore Load(string folder) {
            ScumMapStore store = new ScumMapStore();
            string binPath = Path.Combine(folder ?? "", "scummap.bin");
            if (File.Exists(binPath)) {
                try {
                    using (Stream s = File.OpenRead(binPath)) { store.ReadBinary(s); return store; }
                } catch(IOException) { store=new ScumMapStore(); }
                  catch(InvalidDataException) { store=new ScumMapStore(); }
                  catch(UnauthorizedAccessException) { store=new ScumMapStore(); }
            }
            using (Stream s = typeof(ScumMapStore).Assembly.GetManifestResourceStream("scummap.bin")) {
                if (s != null) {
                    store.ReadBinary(s);
                    return store;
                }
            }
            return store;
        }

        public void ReadBinary(Stream stream) {
            if (stream == null) return;
            using (BinaryReader r = new BinaryReader(stream, Encoding.UTF8)) {
                byte[] magic = r.ReadBytes(4);
                if (magic.Length < 4 || magic[0] != 'S' || magic[1] != 'C' || magic[2] != 'M' || magic[3] != 'P') {
                    throw new InvalidDataException("Invalid scummap.bin magic header.");
                }
                ushort version = r.ReadUInt16();
                if(version!=1 && version!=2) throw new InvalidDataException("Unsupported map data version.");
                ushort catCount = r.ReadUInt16();
                if(catCount>4096) throw new InvalidDataException("Too many map categories.");
                long textBytes=0, geometryPoints=0, ringCount=0;

                Categories.Clear(); Categories.EnsureCapacity(catCount);
                CategoryById.Clear();
                Sections.Clear();
                CategoriesBySection.Clear();
                _disabledCategories.Clear();
                Habitats.Clear();

                for (int i = 0; i < catCount; i++) {
                    int id = r.ReadInt32();
                    byte secLen = r.ReadByte();
                    string sec = ReadExactText(r,secLen);
                    sec = ActivitySection(id) ?? sec;
                    byte nameLen = r.ReadByte();
                    string name = ReadExactText(r,nameLen);
                    int bg = r.ReadInt32();
                    int fg = r.ReadInt32();
                    r.ReadByte(); // Legacy pack defaults; the app owns its current default policy.
                    bool def = !string.Equals(sec,"Hunting",StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(sec,"Fishing",StringComparison.OrdinalIgnoreCase);

                    ScumMapCategory cat = new ScumMapCategory {
                        Id = id,
                        Section = sec,
                        Name = name,
                        ColorBackground = bg,
                        ColorIcon = fg,
                        DefaultEnabled = def
                    };
                    Categories.Add(cat);
                    if(CategoryById.ContainsKey(id)) throw new InvalidDataException("Duplicate map category.");
                    CategoryById[id] = cat;

                    List<ScumMapCategory> secList;
                    if (!CategoriesBySection.TryGetValue(sec, out secList)) {
                        Sections.Add(sec);
                        secList = new List<ScumMapCategory>();
                        CategoriesBySection[sec] = secList;
                    }
                    secList.Add(cat);

                    if (!def) {
                        _disabledCategories.Add(id);
                    }
                }

                int markerCount = ReadCount(r,200000);
                Markers.Clear(); Markers.EnsureCapacity(markerCount);
                for (int x = 0; x < GridSize; x++) {
                    for (int y = 0; y < GridSize; y++) {
                        _grid[x, y].Clear();
                    }
                }

                for (int i = 0; i < markerCount; i++) {
                    int id = r.ReadInt32();
                    int catId = r.ReadInt32();
                    float x = r.ReadSingle();
                    float y = r.ReadSingle();
                    ScumMapCategory markerCat;
                    if(float.IsNaN(x)||float.IsInfinity(x)||float.IsNaN(y)||float.IsInfinity(y)||!CategoryById.TryGetValue(catId, out markerCat))
                        throw new InvalidDataException("Invalid map marker.");
                    ushort titleLen = r.ReadUInt16();
                    textBytes+=titleLen;
                    if(textBytes>16*1024*1024) throw new InvalidDataException("Map text exceeds the size limit.");
                    string title = titleLen > 0 ? ReadExactText(r,titleLen) : null;

                    ScumMapMarker m = new ScumMapMarker {
                        Id = id,
                        CategoryId = catId,
                        X = Math.Max(0f, Math.Min(1f, x)),
                        Y = Math.Max(0f, Math.Min(1f, y)),
                        Title = title
                    };
                    int markerIdx = Markers.Count;
                    Markers.Add(m);

                    markerCat.MarkerCount++;

                    int gx = Math.Max(0, Math.Min(GridSize - 1, (int)(m.X * GridSize)));
                    int gy = Math.Max(0, Math.Min(GridSize - 1, (int)(m.Y * GridSize)));
                    _grid[gx, gy].Add(markerIdx);
                }
                if(version>=2) {
                    int count=ReadCount(r,10000); Habitats.EnsureCapacity(count);
                    for(int i=0;i<count;i++) {
                        ScumMapHabitat habitat=new ScumMapHabitat { Name=ReadText(r),Summary=ReadText(r),Color=r.ReadInt32() };
                        textBytes+=habitat.Name.Length+habitat.Summary.Length;
                        if(textBytes>16*1024*1024) throw new InvalidDataException("Habitat text exceeds the size limit.");
                        habitat.CategoryIds=new int[ReadCount(r,100)];
                        for(int j=0;j<habitat.CategoryIds.Length;j++) {
                            int id=r.ReadInt32();
                            ScumMapCategory habCat;
                            if(!CategoryById.TryGetValue(id, out habCat)) throw new InvalidDataException("Unknown habitat category.");
                            habitat.CategoryIds[j]=id; habCat.MarkerCount++;
                        }
                        int rings=ReadCount(r,10000); ringCount+=rings;
                        if(ringCount>20000) throw new InvalidDataException("Too many habitat rings.");
                        habitat.Rings=new PointF[rings][];
                        for(int j=0;j<habitat.Rings.Length;j++) {
                            int points=ReadCount(r,100000);
                            geometryPoints+=points;
                            if(geometryPoints>2000000) throw new InvalidDataException("Habitat geometry exceeds supported limits.");
                            if(points<3) throw new InvalidDataException("Invalid habitat ring.");
                            habitat.Rings[j]=new PointF[points];
                            for(int k=0;k<points;k++) {
                                float x=r.ReadSingle(),y=r.ReadSingle();
                                if(float.IsNaN(x)||float.IsInfinity(x)||float.IsNaN(y)||float.IsInfinity(y)||Math.Abs(x)>10||Math.Abs(y)>10)
                                    throw new InvalidDataException("Invalid habitat coordinate.");
                                habitat.Rings[j][k]=new PointF(x,y);
                            }
                        }
                        Habitats.Add(habitat);
                    }
                }
            }
            IndexSettlements();
        }
        static string ReadExactText(BinaryReader reader,int count) {
            if(count==0) return "";
            byte[] buf=ArrayPool<byte>.Shared.Rent(count);
            try {
                reader.BaseStream.ReadExactly(buf,0,count);
                return Encoding.UTF8.GetString(buf,0,count);
            } finally {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }
        static int ReadCount(BinaryReader r,int maximum) {
            int n=r.ReadInt32(); if(n<0 || n>maximum) throw new InvalidDataException("Invalid habitat data length."); return n;
        }
        static string ReadText(BinaryReader r) {
            int count=ReadCount(r,100000);
            return ReadExactText(r,count);
        }
        public bool IsHabitatEnabled(ScumMapHabitat h) {
            if(!MasterEnabled || h==null || h.CategoryIds==null) return false;
            var ids=h.CategoryIds;
            for(int i=0;i<ids.Length;i++) { if(!_disabledCategories.Contains(ids[i])) return true; }
            return false;
        }
        public string HabitatDisplayName(ScumMapHabitat h) {
            var ids=h.CategoryIds;
            var species=new List<string>(ids==null?0:ids.Length);
            if(ids!=null) {
                for(int i=0;i<ids.Length;i++) {
                    int id=ids[i];
                    if(!IsCategoryEnabled(id)) continue;
                    ScumMapCategory cat;
                    if(CategoryById.TryGetValue(id,out cat)) species.Add(Localization.GetCategoryName(cat.Name));
                }
            }
            if(species.Count>1) species.Sort(StringComparer.CurrentCulture);
            int shown=Math.Min(3,species.Count);
            string label=shown==0?"":string.Join(", ",species.GetRange(0,shown));
            if(species.Count>3) label+=" +"+(species.Count-3);
            return Localization.GetMapObjectName(h.Name)+" — "+label;
        }
        public ScumMapHabitat HabitatAt(PointF point) {
            // Prefer specific fishing volumes over island-wide biomes, and the smallest overlapping volume.
            // Uses cached AABB + winding check with zero GDI+ GraphicsPath allocations.
            ScumMapHabitat best=null; float area=float.MaxValue;
            for(int hi=0;hi<Habitats.Count;hi++) {
                ScumMapHabitat h=Habitats[hi];
                if(!IsHabitatEnabled(h)) continue;
                if(!h.BoundsReady) h.ComputeBounds();
                if(point.X<h.MinX || point.X>h.MaxX || point.Y<h.MinY || point.Y>h.MaxY) continue;
                float size=(h.MaxX-h.MinX)*(h.MaxY-h.MinY);
                if(size<area && h.Contains(point)) { best=h; area=size; }
            }
            return best;
        }
        void DrawHabitats(Graphics g,RectangleF mapBounds) {
            GraphicsState state=g.Save();
            try {
                g.SetClip(mapBounds,CombineMode.Intersect);
                RectangleF clip = g.VisibleClipBounds;
                for(int hi=Habitats.Count-1; hi>=0; hi--) {
                    ScumMapHabitat h = Habitats[hi];
                    if(!IsHabitatEnabled(h)) continue;
                    if(!h.BoundsReady) h.ComputeBounds();
                    float screenMinX = mapBounds.Left + h.MinX * mapBounds.Width;
                    float screenMaxX = mapBounds.Left + h.MaxX * mapBounds.Width;
                    float screenMinY = mapBounds.Top + h.MinY * mapBounds.Height;
                    float screenMaxY = mapBounds.Top + h.MaxY * mapBounds.Height;
                    if(screenMaxX < clip.Left || screenMinX > clip.Right || screenMaxY < clip.Top || screenMinY > clip.Bottom) continue;
                    using(GraphicsPath p=h.CreatePath())
                    using(Matrix transform=new Matrix(mapBounds.Width,0,0,mapBounds.Height,mapBounds.Left,mapBounds.Top)) {
                        p.Transform(transform);
                        using(Brush fill=new SolidBrush(System.Drawing.Color.FromArgb(35,System.Drawing.Color.FromArgb(h.Color)))) g.FillPath(fill,p);
                        using(Pen outline=new Pen(System.Drawing.Color.FromArgb(175,System.Drawing.Color.FromArgb(h.Color)),1.1f)) g.DrawPath(outline,p);
                    }
                }
            } finally { g.Restore(state); }
        }

        public bool IsCategoryEnabled(int catId) {
            return MasterEnabled && !_disabledCategories.Contains(catId);
        }

        public void SetCategoryEnabled(int catId, bool enabled) {
            bool changed=enabled ? _disabledCategories.Remove(catId) : _disabledCategories.Add(catId);
            if(changed) InvalidateFilterKey();
        }

        public bool IsSectionEnabled(string section) {
            if (!MasterEnabled) return false;
            List<ScumMapCategory> cats;
            if (!CategoriesBySection.TryGetValue(section, out cats) || cats.Count == 0) return false;
            foreach (var c in cats) {
                if (_disabledCategories.Contains(c.Id)) return false;
            }
            return true;
        }

        public bool IsSectionPartiallyEnabled(string section) {
            if (!MasterEnabled) return false;
            List<ScumMapCategory> cats;
            if (!CategoriesBySection.TryGetValue(section, out cats) || cats.Count == 0) return false;
            int enabledCount = 0;
            foreach (var c in cats) {
                if (!_disabledCategories.Contains(c.Id)) enabledCount++;
            }
            return enabledCount > 0 && enabledCount < cats.Count;
        }

        public void SetSectionEnabled(string section, bool enabled) {
            List<ScumMapCategory> cats;
            if (CategoriesBySection.TryGetValue(section, out cats)) {
                bool changed=false;
                foreach (var c in cats) {
                    changed|=enabled ? _disabledCategories.Remove(c.Id) : _disabledCategories.Add(c.Id);
                }
                if(changed) InvalidateFilterKey();
            }
        }

        public void SetAllEnabled(bool enabled) {
            _disabledCategories.Clear();
            if (!enabled) {
                foreach (var c in Categories) _disabledCategories.Add(c.Id);
            }
            InvalidateFilterKey();
        }

        public void ResetToDefaults() {
            _disabledCategories.Clear();
            foreach (var c in Categories) {
                if (!c.DefaultEnabled) _disabledCategories.Add(c.Id);
            }
            InvalidateFilterKey();
        }

        void InvalidateFilterKey() {
            cachedDisabledCategoriesKey=null;
            cachedFilterHashKey=null;
        }

        string DisabledCategoriesKey() {
            if(cachedDisabledCategoriesKey==null) {
                if(_disabledCategories.Count==0) { cachedDisabledCategoriesKey=""; }
                else {
                    int[] ids=new int[_disabledCategories.Count];
                    _disabledCategories.CopyTo(ids);
                    Array.Sort(ids);
                    var sb=new StringBuilder(ids.Length*4);
                    for(int i=0;i<ids.Length;i++) { if(i>0) sb.Append(','); sb.Append(ids[i].ToString(CultureInfo.InvariantCulture)); }
                    cachedDisabledCategoriesKey=sb.ToString();
                }
            }
            return cachedDisabledCategoriesKey;
        }

        public string GetDisabledCategoriesString() {
            return "v2;"+DisabledCategoriesKey();
        }

        public void ApplyDisabledCategoriesString(string value) {
            _disabledCategories.Clear();
            InvalidateFilterKey();
            // Existing preferences predate habitats: leave these new, dense layers opt-in.
            if(value==null || !value.StartsWith("v2;",StringComparison.Ordinal))
                foreach(var c in Categories) if(c.Id<0) _disabledCategories.Add(c.Id);
            if (string.IsNullOrEmpty(value)) return;
            string[] parts = value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string p in parts) {
                int id;
                if (int.TryParse(p.Trim(), out id)) {
                    _disabledCategories.Add(id);
                }
            }
        }

        public string GetFilterHashKey() {
            if(cachedFilterHashKey==null || cachedFilterMasterEnabled!=MasterEnabled) {
                cachedFilterMasterEnabled=MasterEnabled;
                cachedFilterHashKey=MasterEnabled+":"+DisabledCategoriesKey();
            }
            return cachedFilterHashKey;
        }

        static readonly string[,] SectorNames = {
            { "D4", "D3", "D2", "D1", "D0" },
            { "C4", "C3", "C2", "C1", "C0" },
            { "B4", "B3", "B2", "B1", "B0" },
            { "A4", "A3", "A2", "A1", "A0" },
            { "Z4", "Z3", "Z2", "Z1", "Z0" }
        };

        public static string SectorOf(float x, float y) {
            int col = Math.Max(0, Math.Min(4, (int)Math.Floor(x * 5f)));
            int row = Math.Max(0, Math.Min(4, (int)Math.Floor(y * 5f)));
            return SectorNames[row, col];
        }

        static readonly FrozenDictionary<string, string> SingularMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["police stations"] = "Police Station", ["police station"] = "Police Station",
            ["gas stations"] = "Gas Station", ["gas station"] = "Gas Station",
            ["food shop"] = "Food Shop", ["food shops"] = "Food Shop",
            ["pharmacies"] = "Pharmacy", ["pharmacy"] = "Pharmacy",
            ["bars"] = "Bar", ["bar"] = "Bar",
            ["pubs"] = "Pub", ["pub"] = "Pub",
            ["clubs"] = "Club", ["club"] = "Club",
            ["restaurants"] = "Restaurant", ["restaurant"] = "Restaurant",
            ["gun shops"] = "Gun Shop", ["gun shop"] = "Gun Shop",
            ["hunting shops"] = "Hunting Shop", ["hunting shop"] = "Hunting Shop",
            ["vehicle repair shops"] = "Vehicle Repair Shop", ["vehicle repair shop"] = "Vehicle Repair Shop",
            ["car garages"] = "Car Garage", ["car garage"] = "Car Garage",
            ["workshops"] = "Workshop", ["workshop"] = "Workshop",
            ["schools"] = "School", ["school"] = "School",
            ["churches"] = "Church", ["church"] = "Church",
            ["hospitals"] = "Hospital", ["hospital"] = "Hospital",
            ["lighthouses"] = "Lighthouse", ["lighthouse"] = "Lighthouse",
            ["military warehouses"] = "Military Warehouse", ["military warehouse"] = "Military Warehouse",
            ["military hangars"] = "Military Hangar", ["military hangar"] = "Military Hangar",
            ["bunkers"] = "Bunker", ["bunker"] = "Bunker",
            ["abandoned bunkers"] = "Abandoned Bunker", ["abandoned bunker"] = "Abandoned Bunker",
            ["secret bunkers"] = "Secret Bunker", ["secret bunker"] = "Secret Bunker",
            ["ww2 bunkers"] = "WW2 Bunker", ["ww2 bunker"] = "WW2 Bunker",
            ["bunker hatch doors"] = "Bunker Hatch", ["bunker hatch"] = "Bunker Hatch",
            ["research facilities"] = "Research Facility", ["research facility"] = "Research Facility",
            ["general goods"] = "General Goods Store",
            ["armory"] = "Armory", ["mechanic"] = "Mechanic", ["saloon"] = "Saloon", ["hairdresser"] = "Hairdresser",
            ["killboxes"] = "Killbox", ["killbox"] = "Killbox",
            ["mine entrances"] = "Mine Entrance", ["mine entrance"] = "Mine Entrance",
            ["industrial storage silos"] = "Storage Silo", ["log cabin"] = "Log Cabin", ["storage shed"] = "Storage Shed",
            ["warehouses"] = "Warehouse", ["warehouse"] = "Warehouse",
            ["small docks"] = "Dock", ["small dock"] = "Dock", ["dock"] = "Dock",
            ["quest boards"] = "Quest Board", ["quest board"] = "Quest Board",
            ["hunting camps"] = "Hunting Camp", ["hunting camp"] = "Hunting Camp",
            ["hunting towers"] = "Hunting Tower", ["hunting tower"] = "Hunting Tower",
            ["drill press / lathe"] = "Workshop Lathe",
            ["red toolboxes"] = "Toolbox", ["toolbox"] = "Toolbox",
            ["medical containers"] = "Medical Container",
            ["lockers"] = "Locker", ["locker"] = "Locker",
            ["hazmat suit lockers"] = "Hazmat Locker", ["radiation equipment lockers"] = "Radiation Locker",
            ["depleted uranium crates"] = "Uranium Crate", ["propane tank"] = "Propane Tank", ["atm"] = "ATM"
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        public static string Singularize(string name) {
            if (string.IsNullOrWhiteSpace(name)) return "POI";
            string clean = name.Trim();
            string mapped;
            return SingularMap.TryGetValue(clean, out mapped) ? mapped : clean;
        }

        public string GetMarkerDisplayName(ScumMapMarker m) {
            if (m == null) return "POI";
            if (m.CachedDisplayName != null) return m.CachedDisplayName;
            string sec = SectorOf(m.X, m.Y);
            int cid = m.CategoryId;
            string resolved;

            if (cid == 1) {
                if (!string.IsNullOrEmpty(m.Title) && !m.Title.Equals("Bunkers", StringComparison.OrdinalIgnoreCase) && !m.Title.Equals("Bunker", StringComparison.OrdinalIgnoreCase))
                    resolved = m.Title.Contains(sec, StringComparison.OrdinalIgnoreCase) ? m.Title : sec + " " + m.Title;
                else
                    resolved = sec + " Bunker";
            } else if (cid == 456) resolved = sec + " Abandoned Bunker";
            else if (cid == 763) resolved = sec + " Secret Bunker";
            else if (cid == 14) resolved = sec + " WW2 Bunker";
            else if (cid == 874) resolved = sec + " Research Facility";
            else if ((cid == 27 || cid == 26 || cid == 80) && !string.IsNullOrEmpty(m.Title)) resolved = m.Title;
            else {
                ScumMapCategory cat;
                CategoryById.TryGetValue(cid, out cat);
                string baseName = !string.IsNullOrEmpty(m.Title) ? m.Title : (cat != null ? cat.Name : "POI");
                string cleanType = Singularize(baseName);
                if (_settlements.Count == 0 && Markers.Count > 0) IndexSettlements();
                string nearestSettlement = null;
                double minDSq = double.MaxValue;
                for (int i = 0; i < _settlements.Count; i++) {
                    SettlementAnchor si = _settlements[i];
                    double dx = m.X - si.X, dy = m.Y - si.Y;
                    double dSq = dx * dx + dy * dy;
                    double rSq = (double)si.Radius * si.Radius;
                    if (dSq <= rSq && dSq < minDSq) {
                        minDSq = dSq;
                        nearestSettlement = si.Name;
                    }
                }
                if (!string.IsNullOrEmpty(nearestSettlement))
                    resolved = cleanType.StartsWith(nearestSettlement, StringComparison.OrdinalIgnoreCase) ? cleanType : nearestSettlement + " " + cleanType;
                else
                    resolved = cleanType.StartsWith(sec, StringComparison.OrdinalIgnoreCase) ? cleanType : sec + " " + cleanType;
            }
            m.CachedDisplayName = resolved;
            return resolved;
        }

        static SolidBrush CachedBrush(Dictionary<int,SolidBrush> cache,int argb) {
            SolidBrush brush;
            if(!cache.TryGetValue(argb,out brush)) {
                brush=new SolidBrush(Color.FromArgb(argb));
                cache.Add(argb,brush);
            }
            return brush;
        }

        static Pen CachedPen(Dictionary<int,Pen> cache,int argb) {
            Pen pen;
            if(!cache.TryGetValue(argb,out pen)) {
                pen=new Pen(Color.FromArgb(argb),1.2f);
                cache.Add(argb,pen);
            }
            return pen;
        }

        public void Draw(Graphics g, RectangleF mapScreenBounds, float zoom, bool showLabels, float fontSize, bool smartLod) {
            if (!MasterEnabled || mapScreenBounds.Width<=0 || mapScreenBounds.Height<=0) return;
            DrawHabitats(g,mapScreenBounds);
            if(Markers.Count==0) return;

            // Compute visible map normalized rectangle [0..1]
            float leftNorm = Math.Max(0f, Math.Min(1f, -mapScreenBounds.Left / mapScreenBounds.Width));
            float rightNorm = Math.Max(0f, Math.Min(1f, (g.VisibleClipBounds.Width - mapScreenBounds.Left) / mapScreenBounds.Width));
            float topNorm = Math.Max(0f, Math.Min(1f, -mapScreenBounds.Top / mapScreenBounds.Height));
            float bottomNorm = Math.Max(0f, Math.Min(1f, (g.VisibleClipBounds.Height - mapScreenBounds.Top) / mapScreenBounds.Height));

            if (leftNorm > rightNorm || topNorm > bottomNorm) return;

            // Expand bounds slightly to avoid popping at edges (5% margin)
            float marginX = 25f / mapScreenBounds.Width;
            float marginY = 25f / mapScreenBounds.Height;
            float queryMinX = Math.Max(0f, leftNorm - marginX);
            float queryMaxX = Math.Min(1f, rightNorm + marginX);
            float queryMinY = Math.Max(0f, topNorm - marginY);
            float queryMaxY = Math.Min(1f, bottomNorm + marginY);

            int minCellX = Math.Max(0, Math.Min(GridSize - 1, (int)(queryMinX * GridSize)));
            int maxCellX = Math.Max(0, Math.Min(GridSize - 1, (int)(queryMaxX * GridSize)));
            int minCellY = Math.Max(0, Math.Min(GridSize - 1, (int)(queryMinY * GridSize)));
            int maxCellY = Math.Max(0, Math.Min(GridSize - 1, (int)(queryMaxY * GridSize)));

            bool zoomedIn = zoom >= 2.8f;
            float markerRadius = zoomedIn ? 5.5f : (zoom >= 1.6f ? 4f : 3f);

            List<KeyValuePair<string, RectangleF>> labelBoxes = new List<KeyValuePair<string, RectangleF>>();
            Font font = null;
            if (showLabels && (zoomedIn || !smartLod)) {
                SystemTypography.ApplyGdiClearType(g); font = SystemTypography.CreateOwnedGdiFont(Math.Max(6f, Math.Min(18f, fontSize - 1f)), FontStyle.Bold);
            }
            Dictionary<int,SolidBrush> brushCache=new Dictionary<int,SolidBrush>();
            Dictionary<int,Pen> iconPenCache=new Dictionary<int,Pen>();
            SolidBrush shadowBrush=new SolidBrush(Color.FromArgb(90,0,0,0));
            Pen markerBorderPen=new Pen(Color.FromArgb(200,20,25,30),1.2f);
            SolidBrush labelBackgroundBrush=new SolidBrush(Color.FromArgb(185,12,17,22));
            Pen labelBorderPen=new Pen(Color.FromArgb(130,255,255,255),1f);

            try {
                for (int gx = minCellX; gx <= maxCellX; gx++) {
                    for (int gy = minCellY; gy <= maxCellY; gy++) {
                        List<int> cell = _grid[gx, gy];
                        int count = cell.Count;
                        for (int i = 0; i < count; i++) {
                            int markerIdx = cell[i];
                            ScumMapMarker m = Markers[markerIdx];

                            if (_disabledCategories.Contains(m.CategoryId)) continue;
                            if (m.X < queryMinX || m.X > queryMaxX || m.Y < queryMinY || m.Y > queryMaxY) continue;

                            float screenX = mapScreenBounds.Left + m.X * mapScreenBounds.Width;
                            float screenY = mapScreenBounds.Top + m.Y * mapScreenBounds.Height;

                            ScumMapCategory cat;
                            CategoryById.TryGetValue(m.CategoryId, out cat);
                            int bgArgb = cat != null ? cat.ColorBackground : unchecked((int)0xFF4488CC);
                            int fgArgb = cat != null ? cat.ColorIcon : unchecked((int)0xFFFFFFFF);

                            // Draw shadow & pin circle using resources shared by this terrain build.
                            g.FillEllipse(shadowBrush, screenX - markerRadius, screenY - markerRadius + 1.2f, markerRadius * 2, markerRadius * 2);
                            g.FillEllipse(CachedBrush(brushCache,bgArgb), screenX - markerRadius, screenY - markerRadius, markerRadius * 2, markerRadius * 2);
                            g.DrawEllipse(markerBorderPen, screenX - markerRadius, screenY - markerRadius, markerRadius * 2, markerRadius * 2);
                            string activity=ActivitySection(m.CategoryId);
                            if(activity!=null) {
                                float r=markerRadius;
                                Pen symbol=CachedPen(iconPenCache,fgArgb);
                                if(activity=="Fishing") {
                                    g.DrawEllipse(symbol,screenX-r*.55f,screenY-r*.3f,r*.8f,r*.6f);
                                    g.DrawLine(symbol,screenX+r*.2f,screenY,screenX+r*.65f,screenY-r*.4f);
                                    g.DrawLine(symbol,screenX+r*.65f,screenY-r*.4f,screenX+r*.65f,screenY+r*.4f);
                                    g.DrawLine(symbol,screenX+r*.65f,screenY+r*.4f,screenX+r*.2f,screenY);
                                } else {
                                    g.DrawEllipse(symbol,screenX-r*.45f,screenY-r*.45f,r*.9f,r*.9f);
                                    g.DrawLine(symbol,screenX-r*.8f,screenY,screenX+r*.8f,screenY);
                                    g.DrawLine(symbol,screenX,screenY-r*.8f,screenX,screenY+r*.8f);
                                }
                            } else if (markerRadius >= 4f) {
                                float dotR = markerRadius * 0.45f;
                                g.FillEllipse(CachedBrush(brushCache,fgArgb), screenX - dotR, screenY - dotR, dotR * 2, dotR * 2);
                            }

                            // Optional label for visible markers
                            if (font != null && (!smartLod || zoomedIn || (cat != null && (cat.Section == "Outposts" || cat.Section == "Bunkers")))) {
                                string name = GetMarkerDisplayName(m);
                                if (!string.IsNullOrEmpty(name)) {
                                    string displayName = Localization.GetMapObjectName(name);
                                    SizeF sz = g.MeasureString(displayName, font);
                                    RectangleF box = new RectangleF(screenX - sz.Width / 2f - 3f, screenY + markerRadius + 2f, sz.Width + 6f, sz.Height + 2f);
                                    labelBoxes.Add(new KeyValuePair<string, RectangleF>(displayName, box));
                                }
                            }
                        }
                    }
                }

                // Draw labels in batch
                if (labelBoxes.Count > 0 && font != null) {
                    using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) {
                        foreach (var item in labelBoxes) {
                            g.FillRectangle(labelBackgroundBrush, item.Value);
                            g.DrawRectangle(labelBorderPen, item.Value.X, item.Value.Y, item.Value.Width, item.Value.Height);
                            g.DrawString(item.Key, font, Brushes.Black, item.Value.X + item.Value.Width / 2f + 1f, item.Value.Y + item.Value.Height / 2f + 1f, sf);
                            g.DrawString(item.Key, font, Brushes.White, item.Value.X + item.Value.Width / 2f, item.Value.Y + item.Value.Height / 2f, sf);
                        }
                    }
                }
            } finally {
                if (font != null) font.Dispose();
                shadowBrush.Dispose(); markerBorderPen.Dispose();
                labelBackgroundBrush.Dispose(); labelBorderPen.Dispose();
                foreach(SolidBrush brush in brushCache.Values)brush.Dispose();
                foreach(Pen pen in iconPenCache.Values)pen.Dispose();
            }
        }

        public ScumMapMarker HitTest(PointF screenPt, RectangleF mapScreenBounds, float maxDistancePixels) {
            if (!MasterEnabled || Markers.Count == 0) return null;

            float mapX = (screenPt.X - mapScreenBounds.Left) / mapScreenBounds.Width;
            float mapY = (screenPt.Y - mapScreenBounds.Top) / mapScreenBounds.Height;
            if (mapX < 0f || mapX > 1f || mapY < 0f || mapY > 1f) return null;

            float maxDistNorm = maxDistancePixels / Math.Min(mapScreenBounds.Width, mapScreenBounds.Height);
            float maxDistNormSq = maxDistNorm * maxDistNorm;

            int cellX = Math.Max(0, Math.Min(GridSize - 1, (int)(mapX * GridSize)));
            int cellY = Math.Max(0, Math.Min(GridSize - 1, (int)(mapY * GridSize)));

            ScumMapMarker closest = null;
            float closestDistSq = maxDistNormSq;

            int rMinX = Math.Max(0, cellX - 1), rMaxX = Math.Min(GridSize - 1, cellX + 1);
            int rMinY = Math.Max(0, cellY - 1), rMaxY = Math.Min(GridSize - 1, cellY + 1);

            for (int gx = rMinX; gx <= rMaxX; gx++) {
                for (int gy = rMinY; gy <= rMaxY; gy++) {
                    foreach (int idx in _grid[gx, gy]) {
                        ScumMapMarker m = Markers[idx];
                        if (_disabledCategories.Contains(m.CategoryId)) continue;
                        float dx = m.X - mapX;
                        float dy = m.Y - mapY;
                        float distSq = dx * dx + dy * dy;
                        if (distSq < closestDistSq) {
                            closestDistSq = distSq;
                            closest = m;
                        }
                    }
                }
            }
            return closest;
        }

        public static void SelfTest() {
            ScumMapStore store = new ScumMapStore();
            if (store.Markers.Count > 0) throw new Exception("Expected empty store initially.");
            if(ActivitySection(858)!="Fishing" || ActivitySection(869)!="Hunting" || ActivitySection(24)!="Hunting" || ActivitySection(8)!=null)
                throw new Exception("Fishing/hunting classification failed.");
            for(int id=1;id<=20;id++) store.SetCategoryEnabled(id,false);
            string originalHash=store.GetFilterHashKey();
            store.SetCategoryEnabled(20,true); store.SetCategoryEnabled(21,false);
            if(originalHash==store.GetFilterHashKey()) throw new Exception("Filter change did not invalidate map cache.");
            ScumMapHabitat hole=new ScumMapHabitat { Rings=new[]{
                new[]{new PointF(0,0),new PointF(1,0),new PointF(1,1),new PointF(0,1)},
                new[]{new PointF(.25f,.25f),new PointF(.25f,.75f),new PointF(.75f,.75f),new PointF(.75f,.25f)} } };
            if(!hole.Contains(new PointF(.1f,.1f)) || hole.Contains(new PointF(.5f,.5f)) || hole.Contains(new PointF(2,2)))
                throw new Exception("Habitat winding/hole test failed.");
            // Test grid bounds
            int gx = Math.Max(0, Math.Min(GridSize - 1, (int)(0.5f * GridSize)));
            if (gx < 0 || gx >= GridSize) throw new Exception("Grid clamping failed.");
        }
    }
}
