using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Drawing.Drawing2D;
using System.Security.Cryptography;

namespace ZiloreMiniMap {
    public static class SafeMapImage {
        public static Bitmap Decode(Stream stream) {
            if(stream==null) throw new FileNotFoundException("The default map is missing.");
            if(stream.CanSeek && stream.Length>128L*1024*1024) throw new InvalidDataException("Map file exceeds 128 MB.");
            using(Image image=Image.FromStream(stream,false,false)) {
                if(image.Width<1 || image.Height<1 || image.Width>16384 || image.Height>16384 || (long)image.Width*image.Height>256L*1024*1024)
                    throw new InvalidDataException("Map dimensions exceed the supported memory budget.");
                return new Bitmap(image);
            }
        }
        public static Bitmap Load(string custom,Func<Stream> embedded,out string warning) {
            warning=null;
            if(File.Exists(custom)) {
                try { using(var file=File.OpenRead(custom)) return Decode(file); }
                catch(Exception ex) {
                    if(!(ex is IOException) && !(ex is InvalidDataException) && !(ex is UnauthorizedAccessException) && !(ex is ArgumentException) && !(ex is OutOfMemoryException) && !(ex is System.Runtime.InteropServices.ExternalException)) throw;
                    warning=Localization.Get("CustomMapRecovery");
                }
            }
            using(Stream resource=embedded()) return Decode(resource);
        }
    }
    public enum ZoneCategory { City, Town, Farm, Trader, Faction, Military, Bunker, GasStation, Custom, Unknown }
    public sealed class MapZone {
        public const string DeathMarkerLayer="Death markers";
        public bool IsDeathMarker { get { return Category==ZoneCategory.Custom && Layer==DeathMarkerLayer && Points!=null && Points.Length==1; } }
        public DateTime? DeathCreatedUtc;
        internal DateTime? DeathTimeUtc {
            get {
                if(DeathCreatedUtc.HasValue)return DeathCreatedUtc;
                // Earlier versions stored a local timestamp at the end of the name.
                DateTime legacy;
                if(IsDeathMarker && Name!=null && Name.Length>=19 && DateTime.TryParseExact(Name.Substring(Name.Length-19),
                    "yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture,DateTimeStyles.AssumeLocal,out legacy))return legacy.ToUniversalTime();
                return null;
            }
        }
        public string DisplayName { get { return DisplayNameAt(DateTime.UtcNow); } }
        internal string DisplayNameAt(DateTime now) {
            if(!IsDeathMarker)return IsMapObject ? Localization.GetMapObjectName(Name) : Localization.GetZoneName(Name);
            DateTime? created=DeathTimeUtc;
            if(!created.HasValue)return Localization.Get("DeathName");
            TimeSpan age=now-created.Value;
            string elapsed=age.TotalMinutes<1?Localization.T("HistorySecondsAgo",Math.Max(0,(int)age.TotalSeconds)):
                age.TotalHours<1?Localization.T("HistoryMinutesAgo",(int)age.TotalMinutes):
                age.TotalDays<1?Localization.T("HistoryHoursAgo",(int)age.TotalHours):Localization.T("HistoryDaysAgo",(int)age.TotalDays);
            return Localization.Get("DeathName")+" "+elapsed;
        }
        public string Name;
        public bool IsMapObject;
        public string Subtitle;
        public int Argb;
        public int? FillArgb;
        public PointF[] Points;
        public ZoneCategory Category;
        static readonly FrozenSet<string> FactionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "D4 Clock house", "D4 Stable ruins", "D4 City - Pharmacy", "D4 City - Police Station",
            "D3 City Warehouse", "D3 - General Store", "D3 Gas Station", "D3 Little town - House",
            "D2 Little bridge", "D2 Lumbermill cabin", "D2 Little town - House",
            "D1 Graveyard", "D1 Food store", "D1 Farm",
            "C4 Wine farm", "C4 WWII Bunker",
            "C3 Little cabin", "C3 Workshop", "C3 Garage",
            "C1 ww2 bunker", "C1 Little farm",
            "B3 WWII Bunker", "B3 Little farm",
            "B2 Little Farm", "B2 WWII Bunker", "B2 Farm",
            "B1 Barn", "B1 WWII Bunker", "B1 Little Farm",
            "A4 Street between 2 houses", "A4 Lighthouse", "A4 House ruins",
            "A3 Farm", "A3 WWII bunker", "A3 Island WWII bunker",
            "A2 Store with ATM", "A2 Stables",
            "A1 Stables", "A1 School",
            "A0 Offices", "A0 Camping",
            "Z4 Lighthouse", "Z4 WWII Bunker", "Z4 Boatyard", "Z4 Graveyard",
            "Z3 WWII Bunker", "Z3 Boat yard", "Z3 Island Lighthouse",
            "Z2 light house",
            "Z0 WWII bunker", "Z0 Lighthouse", "Z0 WWII Bunker South"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        public static bool IsFactionName(string name) { return !string.IsNullOrEmpty(name) && FactionNames.Contains(name); }
        public bool IsFaction { get { return Argb == -1 || Argb == -657931 || (Argb & 0xFFFFFF) == 0xFFFFFF || IsFactionName(Name); } }
        public bool IsFuelStation { get { return Argb == -30720 || (Points != null && Points.Length < 3 && Name != null && Name.Contains("Gas", StringComparison.OrdinalIgnoreCase)); } }
        public string Layer = "Default";
        public MapZone Clone() { return new MapZone { Name=Name,Subtitle=Subtitle,Argb=Argb,FillArgb=FillArgb,Points=Points==null?Array.Empty<PointF>():(PointF[])Points.Clone(),Category=Category,Layer=Layer,DeathCreatedUtc=DeathCreatedUtc }; }
        PointF[] cachedCentroidSource;
        PointF cachedCentroid;
        public PointF Centroid {
            get {
                var pts = Points;
                if(pts==null || pts.Length==0) return new PointF(0,0);
                if(object.ReferenceEquals(cachedCentroidSource, pts)) return cachedCentroid;
                float x=0,y=0;
                for(int i=0;i<pts.Length;i++) { x+=pts[i].X; y+=pts[i].Y; }
                cachedCentroid = new PointF(x/pts.Length,y/pts.Length);
                cachedCentroidSource = pts;
                return cachedCentroid;
            }
        }
        public double DistanceTo(PointF p) {
            if(Points==null || Points.Length==0) return double.MaxValue;
            if(Contains(p)) return 0;
            double minDistSq=double.MaxValue;
            for(int i=0,j=Points.Length-1; i<Points.Length; j=i++) {
                PointF a=Points[j], b=Points[i];
                double dx=b.X-a.X, dy=b.Y-a.Y;
                double lenSq=dx*dx+dy*dy;
                double u = lenSq>0 ? ((p.X-a.X)*dx + (p.Y-a.Y)*dy)/lenSq : 0;
                u = Math.Max(0,Math.Min(1,u));
                double px = a.X + u*dx, py = a.Y + u*dy;
                double dSq = (p.X-px)*(p.X-px) + (p.Y-py)*(p.Y-py);
                if(dSq<minDistSq) minDistSq=dSq;
            }
            return Math.Sqrt(minDistSq);
        }
        internal float minX, maxX, minY, maxY;
        internal bool boundsReady;
        public void ComputeBounds() {
            if(Points==null || Points.Length==0) { minX=maxX=minY=maxY=0; boundsReady=true; return; }
            minX=maxX=Points[0].X; minY=maxY=Points[0].Y;
            for(int i=1;i<Points.Length;i++) {
                float px=Points[i].X, py=Points[i].Y;
                if(px<minX) minX=px; if(px>maxX) maxX=px;
                if(py<minY) minY=py; if(py>maxY) maxY=py;
            }
            boundsReady=true;
        }
        public bool Contains(PointF p) {
            if(Points==null || Points.Length<3) return false;
            if(!boundsReady) ComputeBounds();
            if(p.X < minX || p.X > maxX || p.Y < minY || p.Y > maxY) return false;
            bool inside=false;
            for(int i=0,j=Points.Length-1; i<Points.Length; j=i++) {
                if(((Points[i].Y>p.Y)!=(Points[j].Y>p.Y)) &&
                    (p.X < (Points[j].X-Points[i].X)*(p.Y-Points[i].Y)/(Points[j].Y-Points[i].Y) + Points[i].X)) {
                    inside=!inside;
                }
            }
            return inside;
        }
    }
    public static class ZoneStore {
        public const int MaxZones=10000, MaxPoints=512, MaxLineLength=65536;
        const long MaxTextChars=32*1024*1024;
        static readonly string[] KnownCities = { "SAMOBOR", "KLENOVNIK", "PRIBOJ" };
        static string ReadBoundedLine(TextReader reader,System.Text.StringBuilder line,ref long total) {
            line.Length=0; int c;
            while((c=reader.Read())!=-1) {
                if(++total>MaxTextChars) throw new InvalidDataException("Zone file exceeds the size limit.");
                if(c=='\n') {
                    if(line.Length>0 && line[line.Length-1]=='\r') line.Length--;
                    return line.ToString();
                }
                if(line.Length>=MaxLineLength) throw new InvalidDataException("Zone line exceeds the size limit.");
                line.Append((char)c);
            }
            if(line.Length==0) return null;
            if(line[line.Length-1]=='\r') line.Length--;
            return line.ToString();
        }
        static string SafeUnescape(string value) {
            try { return Uri.UnescapeDataString(value); }
            catch(UriFormatException) { return value; }
        }
        public static ZoneCategory Classify(MapZone z) {
            if(z==null) return ZoneCategory.Unknown;
            if(z.IsFuelStation) return ZoneCategory.GasStation;
            if(z.IsFaction) return ZoneCategory.Faction;
            if(z.Argb==-16737281) return ZoneCategory.Custom; // cyan 0xFF00C8FF
            string n=z.Name??"";
            if(n.Contains("BUNKER", StringComparison.OrdinalIgnoreCase)) return ZoneCategory.Bunker;
            if(z.Argb==-993486 || z.Argb==-1358291) return ZoneCategory.Military;
            if(n.Contains("FARM", StringComparison.OrdinalIgnoreCase) || n.Contains("RANCH", StringComparison.OrdinalIgnoreCase) || n.Contains("VINEYARD", StringComparison.OrdinalIgnoreCase) || n.Contains("ORCHARD", StringComparison.OrdinalIgnoreCase)) return ZoneCategory.Farm;
            if(n.Contains("OUTPOST", StringComparison.OrdinalIgnoreCase) || n.Contains("TRADER", StringComparison.OrdinalIgnoreCase) || z.Argb==-9843406) return ZoneCategory.Trader;
            for(int ci=0;ci<KnownCities.Length;ci++) if(n.Contains(KnownCities[ci], StringComparison.OrdinalIgnoreCase)) return ZoneCategory.City;
            if(z.Points!=null && z.Points.Length>=3 && z.Argb==-6895376) return ZoneCategory.Town;
            return ZoneCategory.Unknown;
        }
        public static List<MapZone> Load(string path) {
            if(File.Exists(path)) {
                using(Stream stream=File.OpenRead(path)) return Load(stream);
            }
            using(Stream stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("zones.tsv")) {
                if(stream!=null) return Load(stream);
            }
            return new List<MapZone>();
        }
        public static List<MapZone> Load(Stream stream) {
            List<MapZone> result=new List<MapZone>();
            if(stream==null)return result;
            using(StreamReader reader=new StreamReader(stream)) {
                var lineBuf=new System.Text.StringBuilder(256);
                string line; long total=0;
                while((line=ReadBoundedLine(reader,lineBuf,ref total))!=null) {
                    if(string.IsNullOrWhiteSpace(line)) continue;
                    string[] parts=line.Split('\t'); int color;
                    if(parts.Length<3 || !int.TryParse(parts[1],out color)) throw new InvalidDataException("Invalid zone record.");
                    List<PointF> points=new List<PointF>(); bool valid=true;
                    foreach(string point in parts[2].Split(';')) {
                        string[] xy=point.Split(','); float x,y;
                        if(xy.Length!=2 || !float.TryParse(xy[0],NumberStyles.Float,CultureInfo.InvariantCulture,out x) || !float.TryParse(xy[1],NumberStyles.Float,CultureInfo.InvariantCulture,out y) || float.IsNaN(x) || float.IsNaN(y) || x<-.1f || x>1.1f || y<-.1f || y>1.1f) { valid=false;break; }
                        if(points.Count>=MaxPoints) throw new InvalidDataException("Zone has too many points.");
                        points.Add(new PointF(Math.Max(0,Math.Min(1,x)),Math.Max(0,Math.Min(1,y))));
                    }
                    if(!valid || points.Count==0) throw new InvalidDataException("Invalid zone coordinates.");
                    if(result.Count>=MaxZones) throw new InvalidDataException("Zone file exceeds the supported zone count.");
                    {
                        MapZone zone = new MapZone { Name=SafeUnescape(parts[0]),Argb=color,Points=points.ToArray() };
                        if(parts.Length>3) {
                            ZoneCategory cat;
                            if(Enum.TryParse(parts[3], out cat) && Enum.IsDefined<ZoneCategory>(cat)) zone.Category = cat;
                            else zone.Category = Classify(zone);
                        } else {
                            zone.Category = Classify(zone);
                        }
                        if(parts.Length>4 && !string.IsNullOrWhiteSpace(parts[4])) {
                            zone.Layer = SafeUnescape(parts[4]);
                        } else {
                            zone.Layer = "Default";
                        }
                        if(parts.Length<=3 && (zone.Name??"").Contains("BUNKER", StringComparison.OrdinalIgnoreCase) && !zone.IsFuelStation && !zone.IsFaction && zone.Category != ZoneCategory.Faction) zone.Category = ZoneCategory.Bunker;
                        DateTime created;
                        if(zone.IsDeathMarker && parts.Length>5 && DateTime.TryParseExact(parts[5],"o",CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind,out created))zone.DeathCreatedUtc=created.ToUniversalTime();
                        else if(zone.IsDeathMarker)zone.DeathCreatedUtc=zone.DeathTimeUtc;
                        if(parts.Length>6&&!string.IsNullOrWhiteSpace(parts[6])) { int fill;if(!int.TryParse(parts[6],out fill))throw new InvalidDataException("Invalid zone fill colour.");zone.FillArgb=fill; }
                        result.Add(zone);
                    }
                }
            }
            return result;
        }
        public static void Save(string path,List<MapZone> zones) {
            if(zones==null || zones.Count>MaxZones) throw new InvalidDataException("Too many zones to save.");
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            var lines=new List<string>(zones.Count); long total=0;
            var sb=new System.Text.StringBuilder(256);
            for(int zi=0;zi<zones.Count;zi++) {
                MapZone z=zones[zi];
                if(z==null || z.Name==null || z.Points==null || z.Points.Length==0 || z.Points.Length>MaxPoints || !Enum.IsDefined<ZoneCategory>(z.Category))
                    throw new InvalidDataException("Invalid zone; nothing was saved.");
                sb.Length=0;
                sb.Append(Uri.EscapeDataString(z.Name)).Append('\t').Append(z.Argb.ToString(CultureInfo.InvariantCulture)).Append('\t');
                for(int pi=0;pi<z.Points.Length;pi++) {
                    PointF point=z.Points[pi];
                    if(float.IsNaN(point.X)||float.IsInfinity(point.X)||float.IsNaN(point.Y)||float.IsInfinity(point.Y)||point.X<0||point.X>1||point.Y<0||point.Y>1)
                        throw new InvalidDataException("Invalid zone coordinates; nothing was saved.");
                    if(pi>0) sb.Append(';');
                    sb.Append(point.X.ToString("R",CultureInfo.InvariantCulture)).Append(',').Append(point.Y.ToString("R",CultureInfo.InvariantCulture));
                }
                string layer=string.IsNullOrWhiteSpace(z.Layer)?"Default":z.Layer;
                sb.Append('\t').Append(z.Category).Append('\t').Append(Uri.EscapeDataString(layer));
                DateTime? deathUtc=z.IsDeathMarker?z.DeathTimeUtc:null;
                if(deathUtc.HasValue||z.FillArgb.HasValue) {
                    sb.Append('\t');
                    if(deathUtc.HasValue) sb.Append(deathUtc.Value.ToString("o",CultureInfo.InvariantCulture));
                }
                if(z.FillArgb.HasValue) sb.Append('\t').Append(z.FillArgb.Value.ToString(CultureInfo.InvariantCulture));
                string line=sb.ToString();
                total+=line.Length+2;
                if(line.Length>MaxLineLength || total>MaxTextChars) throw new InvalidDataException("Zone data exceeds the size limit.");
                lines.Add(line);
            }
            try {
                File.WriteAllLines(temp,lines);
                if(File.Exists(path))File.Replace(temp,path,null); else File.Move(temp,path);
            } finally { if(File.Exists(temp)) File.Delete(temp); }
        }

        public static void DrawFuelIcon(Graphics g,float cx,float cy,float r=10f) {
            using(Brush shadow=new SolidBrush(Color.FromArgb(90,0,0,0))) g.FillEllipse(shadow,cx-r,cy-r+1.5f,r*2,r*2);
            using(Brush badge=new SolidBrush(Color.FromArgb(255,140,0))) g.FillEllipse(badge,cx-r,cy-r,r*2,r*2);
            using(Pen border=new Pen(Color.FromArgb(220,20,25,30),1.4f)) g.DrawEllipse(border,cx-r,cy-r,r*2,r*2);
            using(Brush white=new SolidBrush(Color.White)) g.FillRectangle(white,cx-4.5f,cy-4f,5f,8.5f);
            using(Brush cutout=new SolidBrush(Color.FromArgb(255,140,0))) g.FillRectangle(cutout,cx-3.5f,cy-2.5f,3f,2.2f);
            using(Pen whitePen=new Pen(Color.White,1.4f)) {
                whitePen.StartCap=LineCap.Round; whitePen.EndCap=LineCap.Round;
                g.DrawLine(whitePen,cx+0.5f,cy-2f,cx+4f,cy-1f);
                g.DrawLine(whitePen,cx+4f,cy-1f,cx+4f,cy+4f);
                g.DrawLine(whitePen,cx+4f,cy+4f,cx+2.5f,cy+2.5f);
            }
        }
        public static void Draw(Graphics g,IEnumerable<MapZone> zones,RectangleF rect,bool showPolygons,float fontSize=9f,bool showGasStations=true,HashSet<ZoneCategory> hidden=null,bool showLabels=true,bool smartLod=true,float zoom=1.0f) {
            SystemTypography.ApplyGdiClearType(g); using(Font font=SystemTypography.CreateOwnedGdiFont(Math.Max(6f,Math.Min(24f,fontSize)),FontStyle.Bold))
            using(StringFormat sf=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center }) {
                List<RectangleF> placed=new List<RectangleF>();
                List<KeyValuePair<string,RectangleF>> labelItems=new List<KeyValuePair<string,RectangleF>>();
                bool zoomedOut = smartLod && zoom < 2.5f;
                RectangleF clip = g.VisibleClipBounds;
                if(clip.Width <= 0 || clip.Height <= 0) clip = new RectangleF(0, 0, 4000, 4000);
                foreach(MapZone zone in zones??Enumerable.Empty<MapZone>()) {
                    if(zone==null || zone.Points==null || zone.Points.Length==0) continue;
                    if(hidden!=null && hidden.Contains(zone.Category)) continue;
                    if(zone.Points.Length>=3) {
                        if(!showPolygons) continue;
                        if(!zone.boundsReady) zone.ComputeBounds();
                        float polyMinX = rect.Left + zone.minX * rect.Width;
                        float polyMaxX = rect.Left + zone.maxX * rect.Width;
                        float polyMinY = rect.Top + zone.minY * rect.Height;
                        float polyMaxY = rect.Top + zone.maxY * rect.Height;
                        if(polyMaxX < clip.Left - 10 || polyMinX > clip.Right + 10 || polyMaxY < clip.Top - 10 || polyMinY > clip.Bottom + 10) continue;

                        PointF[] points = new PointF[zone.Points.Length];
                        float cx = 0, cy = 0;
                        for(int pi = 0; pi < zone.Points.Length; pi++) {
                            float px = rect.Left + zone.Points[pi].X * rect.Width;
                            float py = rect.Top + zone.Points[pi].Y * rect.Height;
                            points[pi] = new PointF(px, py);
                            cx += px; cy += py;
                        }
                        cx /= zone.Points.Length; cy /= zone.Points.Length;

                        Color color=Color.FromArgb(zone.Argb);
                        int fillAlpha = (zone.IsFaction || zone.Category == ZoneCategory.Faction) ? 75 : 40;
                        float penWidth = (zone.IsFaction || zone.Category == ZoneCategory.Faction) ? 2.5f : 1.8f;
                        using(Brush fill=new SolidBrush(zone.FillArgb.HasValue?Color.FromArgb(zone.FillArgb.Value):Color.FromArgb(fillAlpha,color)))g.FillPolygon(fill,points);
                        using(Pen pen=new Pen(Color.FromArgb(220,color),penWidth))g.DrawPolygon(pen,points);
                        bool isMajor = zone.Category==ZoneCategory.City || zone.Category==ZoneCategory.Trader || zone.Category==ZoneCategory.Custom || zone.Category==ZoneCategory.Faction || zone.IsFaction;
                        if(showLabels && (!zoomedOut || isMajor) && !string.IsNullOrEmpty(zone.Name)) {
                            string displayName = zone.DisplayName;
                            SizeF sz=g.MeasureString(displayName,font);
                            RectangleF box=new RectangleF(cx-sz.Width/2f-4f,cy-sz.Height/2f-2f,sz.Width+8f,sz.Height+4f);
                            for(int pass=0;pass<10;pass++) {
                                bool clash=false;
                                for(int j=0;j<placed.Count;j++) {
                                    if(box.IntersectsWith(placed[j])) {
                                        clash=true;
                                        box.Y=placed[j].Bottom+2f;
                                    }
                                }
                                if(!clash)break;
                            }
                            placed.Add(box);
                            labelItems.Add(new KeyValuePair<string,RectangleF>(displayName,box));
                        }
                    } else {
                        float cx=rect.Left+zone.Points[0].X*rect.Width, cy=rect.Top+zone.Points[0].Y*rect.Height;
                        if(cx>=clip.Left-20 && cx<=clip.Right+20 && cy>=clip.Top-20 && cy<=clip.Bottom+20) {
                            if(zone.IsFuelStation) {
                                if(showGasStations) DrawFuelIcon(g,cx,cy);
                            } else {
                                Color c=Color.FromArgb(zone.Argb);
                                if(zone.IsDeathMarker) {
                                    using(var tombstone=new GraphicsPath()) {
                                        tombstone.AddArc(cx-7,cy-10,14,14,180,180);
                                        tombstone.AddLine(cx+7,cy-3,cx+7,cy+8);
                                        tombstone.AddLine(cx+7,cy+8,cx-7,cy+8);
                                        tombstone.CloseFigure();
                                        using(Brush fill=new SolidBrush(c)) g.FillPath(fill,tombstone);
                                        using(Pen outline=new Pen(Color.FromArgb(240,20,25,30),2)) {
                                            g.DrawPath(outline,tombstone);
                                            g.DrawLine(outline,cx,cy-5,cx,cy+4);
                                            g.DrawLine(outline,cx-3,cy-2,cx+3,cy-2);
                                            g.DrawLine(outline,cx-9,cy+9,cx+9,cy+9);
                                        }
                                    }
                                } else {
                                using(Brush shadow=new SolidBrush(Color.FromArgb(90,0,0,0))) g.FillEllipse(shadow,cx-7,cy-7+1.5f,14,14);
                                using(Brush badge=new SolidBrush(c)) g.FillEllipse(badge,cx-7,cy-7,14,14);
                                using(Pen border=new Pen(Color.FromArgb(220,20,25,30),1.4f)) g.DrawEllipse(border,cx-7,cy-7,14,14);
                                using(Brush dot=new SolidBrush(Color.White)) g.FillEllipse(dot,cx-2.5f,cy-2.5f,5,5);
                                }
                                if(showLabels && !string.IsNullOrEmpty(zone.Name)) {
                                    string displayName = zone.DisplayName;
                                    SizeF sz=g.MeasureString(displayName,font);
                                    RectangleF box=new RectangleF(cx-sz.Width/2f-4f,cy+9f,sz.Width+8f,sz.Height+4f);
                                    placed.Add(box);
                                    labelItems.Add(new KeyValuePair<string,RectangleF>(displayName,box));
                                }
                            }
                        }
                    }
                }
                if(labelItems.Count>0) {
                    using(Brush bg=new SolidBrush(Color.FromArgb(180,12,17,22)))
                    using(Pen border=new Pen(Color.FromArgb(130,255,255,255),1f)) {
                        foreach(KeyValuePair<string,RectangleF> item in labelItems) {
                            g.FillRectangle(bg,item.Value);
                            g.DrawRectangle(border,item.Value.X,item.Value.Y,item.Value.Width,item.Value.Height);
                            g.DrawString(item.Key,font,Brushes.Black,item.Value.X+item.Value.Width/2f+1f,item.Value.Y+item.Value.Height/2f+1f,sf);
                            g.DrawString(item.Key,font,Brushes.White,item.Value.X+item.Value.Width/2f,item.Value.Y+item.Value.Height/2f,sf);
                        }
                    }
                }
            }
        }
        public static PointF Align(PointF point,PointF s1,PointF s2,PointF m1,PointF m2) {
            if(Math.Abs(s2.X-s1.X)<.05 || Math.Abs(s2.Y-s1.Y)<.05 || Math.Abs(m2.X-m1.X)<.02 || Math.Abs(m2.Y-m1.Y)<.02)throw new ArgumentException("Choose two landmarks separated both horizontally and vertically.");
            float sx=(m2.X-m1.X)/(s2.X-s1.X),sy=(m2.Y-m1.Y)/(s2.Y-s1.Y);
            if(sx<=0 || sy<=0)throw new ArgumentException("Use matching landmarks in the same order on a north-up map.");
            return new PointF(m1.X+(point.X-s1.X)*sx,m1.Y+(point.Y-s1.Y)*sy);
        }
        public static void SelfTest() {
            AppZoneFolder.SelfTest();
            ZoneFileOpen.SelfTest();
            using(MemoryStream stream=new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Edge\t1\t-0.02,0.2;0.1,0.2;0.1,0.3"))) {
                List<MapZone> edge=Load(stream);
                if(edge.Count!=1 || edge[0].Points[0].X!=0)throw new Exception("Map-edge zone validation failed.");
            }
            PointF p=Align(new PointF(.5f,.5f),new PointF(.1f,.1f),new PointF(.9f,.9f),new PointF(.2f,.3f),new PointF(.6f,.7f));
            if(Math.Abs(p.X-.4f)>.0001 || Math.Abs(p.Y-.5f)>.0001)throw new Exception("Screenshot alignment failed.");
            bool rejected=false;try { Align(p,p,p,p,p); } catch(ArgumentException) { rejected=true; }
            if(!rejected)throw new Exception("Degenerate alignment accepted.");
            string path=Path.GetTempFileName();
            try {
                List<MapZone> sample=new List<MapZone>{
                    new MapZone { Name="Test zone / 1",Argb=Color.Red.ToArgb(),Points=new[]{new PointF(.2f,.2f),new PointF(.5f,.2f),new PointF(.5f,.5f)} },
                    new MapZone { Name="Gas Station - Test",Argb=-30720,Points=new[]{new PointF(.3f,.4f)} },
                    new MapZone { Name="Death location test",Argb=Color.Beige.ToArgb(),Category=ZoneCategory.Custom,Layer=MapZone.DeathMarkerLayer,Points=new[]{new PointF(.6f,.7f)} }
                };
                Save(path,sample);List<MapZone> loaded=Load(path);
                if(loaded.Count!=3 || loaded[0].Name!=sample[0].Name || loaded[1].Name!=sample[1].Name || loaded[1].Points[0]!=sample[1].Points[0])throw new Exception("Zone persistence failed.");
                if(!loaded[2].IsDeathMarker || loaded[2].Points[0]!=sample[2].Points[0] || !loaded[2].Clone().IsDeathMarker)
                    throw new Exception("Death marker persistence failed.");
                using(var image=new Bitmap(100,100)) using(var graphics=Graphics.FromImage(image))
                    Draw(graphics,new List<MapZone>{loaded[2]},new RectangleF(0,0,100,100),false,9,false,null,false);
            } finally { File.Delete(path);if(File.Exists(path+".tmp"))File.Delete(path+".tmp"); }
        }
    }
    // Folder packs stay separate from profile annotations: refresh/removal must never
    // overwrite saved waypoints, deaths, or zones created in the editor.
    internal sealed class AppZoneFolder {
        readonly string root;
        string applied,observed;
        DateTime next;
        Task<Pack> pending;
        internal sealed class Pack { internal string Signature;internal List<MapZone> Zones; }
        internal AppZoneFolder(string appFolder) { root=Path.Combine(appFolder,"zones"); }
        static string[] Files(string root) {
            var files=new List<string>();var directories=new Queue<Tuple<string,int>>();
            if(Directory.Exists(root)&&(File.GetAttributes(root)&FileAttributes.ReparsePoint)==0)directories.Enqueue(Tuple.Create(root,0));
            int visited=0;
            while(directories.Count>0) {
                var dir=directories.Dequeue();if(++visited>256)throw new InvalidDataException("Too many zone folders.");
                foreach(string file in Directory.EnumerateFiles(dir.Item1,"*.tsv")) {
                    if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)continue;
                    // source-zones.tsv is an editor calibration file, not a map pack.
                    if(!string.Equals(Path.GetFileName(file),"zones.tsv",StringComparison.OrdinalIgnoreCase))continue;
                    files.Add(file);if(files.Count>128)throw new InvalidDataException("Too many zone packs.");
                }
                foreach(string child in Directory.EnumerateDirectories(dir.Item1)) {
                    if((File.GetAttributes(child)&FileAttributes.ReparsePoint)!=0)continue;
                    if(dir.Item2>=8)throw new InvalidDataException("Zone folders are too deeply nested.");
                    directories.Enqueue(Tuple.Create(child,dir.Item2+1));
                }
            }
            return files.OrderBy(f=>f,StringComparer.OrdinalIgnoreCase).ToArray();
        }
        static string Signature(string[] files) {
            return string.Join("\n",files.Select(f=> { var info=new FileInfo(f);return f+"|"+info.Length+"|"+info.LastWriteTimeUtc.Ticks; }));
        }
        internal static Pack Read(string root,string previous,string stable) {
            string[] files=Files(root);string signature=Signature(files);
            if(signature==previous||signature!=stable)return new Pack { Signature=signature };
            var zones=new List<MapZone>();long bytes=0;
            foreach(string file in files) {
                bytes+=new FileInfo(file).Length;if(bytes>32L*1024*1024)throw new InvalidDataException("Zone packs exceed the size limit.");
                zones.AddRange(ZoneStore.Load(file));if(zones.Count>ZoneStore.MaxZones)throw new InvalidDataException("Too many packed zones.");
            }
            if(Signature(Files(root))!=signature)throw new IOException("Zone folder is still changing.");
            return new Pack { Signature=signature,Zones=zones.Where(z=>!z.IsDeathMarker).ToList() };
        }
        internal bool Poll(out List<MapZone> zones) {
            zones=null;
            if(pending!=null&&pending.IsCompleted) {
                try {
                    var result=pending.GetAwaiter().GetResult();observed=result.Signature;
                    if(result.Zones!=null) { applied=result.Signature;zones=result.Zones; }
                } catch(IOException) { } catch(UnauthorizedAccessException) { } catch(InvalidDataException) { }
                finally { pending=null; }
            }
            if(pending==null&&DateTime.UtcNow>=next) {
                next=DateTime.UtcNow.AddSeconds(2);string previous=applied,stable=observed;
                pending=Task.Run(()=>Read(root,previous,stable));
            }
            return zones!=null;
        }
        internal static void SelfTest() {
            string root=Path.Combine(Path.GetTempPath(),"minimap-zone-pack-"+Guid.NewGuid().ToString("N"));
            string nested=Path.Combine(root,"zones","skynett"),file=Path.Combine(nested,"zones.tsv");
            try {
                var absent=Read(Path.Combine(root,"zones"),null,null);if(absent.Zones!=null)throw new Exception("Zone pack debounce failed.");
                Directory.CreateDirectory(nested);File.WriteAllText(file,"Pack zone\t-65536\t0.1,0.1;0.2,0.1;0.2,0.2\tCustom\tSkynett\n");
                var first=Read(Path.Combine(root,"zones"),null,null);var pack=Read(Path.Combine(root,"zones"),null,first.Signature);
                if(pack.Zones.Count!=1||pack.Zones[0].Layer!="Skynett")throw new Exception("Nested zone pack did not load.");
                if(Read(Path.Combine(root,"zones"),pack.Signature,pack.Signature).Zones!=null)throw new Exception("Unchanged zone pack was reloaded.");
                File.WriteAllText(file,"Updated pack zone\t-16711936\t0.1,0.1;0.3,0.1;0.3,0.3\tCustom\tSkynett\n");
                var update=Read(Path.Combine(root,"zones"),pack.Signature,null);var updated=Read(Path.Combine(root,"zones"),pack.Signature,update.Signature);
                if(updated.Zones.Count!=1||updated.Zones[0].Name!="Updated pack zone"||updated.Zones[0].Argb!=-16711936)throw new Exception("Changed zone pack was not refreshed.");
                pack=updated;
                File.WriteAllText(file,"invalid");var changed=Read(Path.Combine(root,"zones"),pack.Signature,null);
                bool rejected=false;try { Read(Path.Combine(root,"zones"),pack.Signature,changed.Signature); }catch(InvalidDataException) { rejected=true; }
                if(!rejected)throw new Exception("Incomplete zone pack was accepted.");
                File.Delete(file);var removed=Read(Path.Combine(root,"zones"),pack.Signature,"");
                if(removed.Zones==null||removed.Zones.Count!=0)throw new Exception("Removed zone pack was retained.");
            } finally { if(File.Exists(file))File.Delete(file);if(Directory.Exists(nested))Directory.Delete(nested);if(Directory.Exists(Path.Combine(root,"zones")))Directory.Delete(Path.Combine(root,"zones"));if(Directory.Exists(root))Directory.Delete(root); }
        }
    }
    internal static class ZoneFileOpen {
        internal static string ParseArguments(string[] args) {
            if(args==null)return null;
            string path=args.Length==2&&args[0].Equals("--import-zones",StringComparison.OrdinalIgnoreCase)?args[1]:args.Length==1&&Path.GetExtension(args[0]).Equals(".tsv",StringComparison.OrdinalIgnoreCase)?args[0]:null;
            if(path==null)return null;
            if(!Path.GetExtension(path).Equals(".tsv",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("A zone TSV file is required.");
            return Path.GetFullPath(path);
        }
        internal static string QueueFolder(string profile) { return Path.Combine(profile,"zone-open-requests"); }
        internal static void Enqueue(string profile,string path) {
            path=Path.GetFullPath(path);if(!File.Exists(path))throw new FileNotFoundException("Zone file was not found.",path);
            if(!Path.GetExtension(path).Equals(".tsv",StringComparison.OrdinalIgnoreCase)||path.Length>4096||new FileInfo(path).Length>32L*1024*1024)throw new InvalidDataException("Invalid zone file.");
            string folder=QueueFolder(profile);Directory.CreateDirectory(folder);string key;
            key=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
            string target=Path.Combine(folder,key+".request"),temp=Path.Combine(folder,Guid.NewGuid().ToString("N")+".tmp");
            try { File.WriteAllText(temp,path);if(!File.Exists(target))try { File.Move(temp,target); }catch(IOException) { if(!File.Exists(target))throw; } }
            finally { if(File.Exists(temp))File.Delete(temp); }
        }
        internal static string Take(string profile) {
            string folder=QueueFolder(profile);if(!Directory.Exists(folder))return null;
            foreach(string file in Directory.EnumerateFiles(folder,"*.request").Take(64)) {
                if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)continue;
                string path=new FileInfo(file).Length<=16384?File.ReadAllText(file):null;
                File.Delete(file);
                if(!string.IsNullOrWhiteSpace(path)&&path.Length<=4096&&Path.IsPathRooted(path)&&Path.GetExtension(path).Equals(".tsv",StringComparison.OrdinalIgnoreCase))return path;
            }
            return null;
        }
        internal static string OpenCommand(string executable) { return "\""+Path.GetFullPath(executable)+"\" --import-zones \"%1\""; }
        internal static void Register(string executable) {
            // Advertise an Open With handler. Windows owns UserChoice/default selection.
            const string id="ZiloreMiniMap.ZoneFile";
            using(var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\\Classes\\"+id)) {
                key.SetValue("","ZiloreMiniMap");
                using(var icon=key.CreateSubKey("DefaultIcon"))icon.SetValue("","\""+Path.GetFullPath(executable)+"\",0");
                using(var open=key.CreateSubKey("shell\\open")) { open.SetValue("MultiSelectModel","Single");using(var command=open.CreateSubKey("command"))command.SetValue("",OpenCommand(executable)); }
            }
            using(var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\\Classes\\.tsv\\OpenWithProgids"))key.SetValue(id,Array.Empty<byte>(),Microsoft.Win32.RegistryValueKind.None);
            using(var app=Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\\Classes\\Applications\\"+Path.GetFileName(executable))) {
                app.SetValue("FriendlyAppName","ZiloreMiniMap");
                using(var types=app.CreateSubKey("SupportedTypes"))types.SetValue(".tsv","");
                using(var command=app.CreateSubKey("shell\\open\\command"))command.SetValue("",OpenCommand(executable));
            }
            NotifyAssociationChanged(0x08000000,0,IntPtr.Zero,IntPtr.Zero);
        }
        [System.Runtime.InteropServices.DllImport("shell32.dll",EntryPoint="SHChangeNotify")]static extern void NotifyAssociationChanged(uint action,uint flags,IntPtr first,IntPtr second);
        internal static void SelfTest() {
            string root=Path.Combine(Path.GetTempPath(),"minimap-zone-open-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string file=Path.Combine(root,"Zones with spaces 日本語.tsv");
            try {
                File.WriteAllText(file,"Synthetic\t-65536\t0.1,0.1;0.2,0.1;0.2,0.2\tCustom\tTest");
                if(ParseArguments(new[]{"--import-zones",file})!=file||ParseArguments(new[]{file})!=file||ParseArguments(new[]{"--preview"})!=null)throw new Exception("Zone opening arguments failed.");
                if(OpenCommand(Path.Combine(root,"MiniMap app.exe"))!="\""+Path.Combine(root,"MiniMap app.exe")+"\" --import-zones \"%1\"")throw new Exception("Zone association quoting failed.");
                Enqueue(root,file);Enqueue(root,file);
                if(Directory.GetFiles(QueueFolder(root),"*.request").Length!=1||Take(root)!=file||Take(root)!=null)throw new Exception("Zone opening handoff failed.");
                bool rejected=false;try { ParseArguments(new[]{"--import-zones",Path.Combine(root,"not-zones.exe")}); }catch(InvalidDataException) { rejected=true; }
                if(!rejected)throw new Exception("Non-TSV opening request was accepted.");
            }finally { if(File.Exists(file))File.Delete(file);if(Directory.Exists(QueueFolder(root))) { foreach(string request in Directory.GetFiles(QueueFolder(root)))File.Delete(request);Directory.Delete(QueueFolder(root)); }Directory.Delete(root); }
        }
    }
    internal sealed class MapSidecarPaths {
        internal readonly string DataFolder,Folder,ZonesPath,WaypointsPath,ImagePath,MapId;
        MapSidecarPaths(string dataFolder,string id) {
            DataFolder=dataFolder;
            Folder=Path.Combine(dataFolder,"maps",id);
            ZonesPath=Path.Combine(Folder,"zones.tsv");
            WaypointsPath=Path.Combine(Folder,"customwaypoints.tsv");
            ImagePath=Path.Combine(Folder,"map.png");
            MapId=id;
            Directory.CreateDirectory(Folder);
        }
        internal static MapSidecarPaths ForMap(string dataFolder,string customMapPath) {
            string id="default";
            if(!string.IsNullOrWhiteSpace(customMapPath) && File.Exists(customMapPath)) {
                using(FileStream stream=File.OpenRead(customMapPath)) {
                    id="custom-"+Convert.ToHexStringLower(SHA256.HashData(stream));
                }
            }
            MapSidecarPaths paths=new MapSidecarPaths(dataFolder,id);
            if(id!="default" && !File.Exists(paths.ImagePath)) File.Copy(customMapPath,paths.ImagePath);
            return paths;
        }
        internal void EnsureImage(Image image) {
            if(File.Exists(ImagePath)) return;
            string temporary=ImagePath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                image.Save(temporary,System.Drawing.Imaging.ImageFormat.Png);
                File.Move(temporary,ImagePath);
            } finally { if(File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    internal static class MapSidecarStore {
        internal static string SaveImport(string mapFolder,string sourcePath,List<MapZone> zones,List<MapZone> sourceZones,string report) {
            // Each import has its own directory, so equal filenames and layer
            // names cannot overwrite another image or its coordinate TSV.
            string imports=Path.Combine(mapFolder,"imports");
            Directory.CreateDirectory(imports);
            string id=Guid.NewGuid().ToString("N");
            string stage=Path.Combine(imports,".pending-"+id),destination=Path.Combine(imports,id);
            Directory.CreateDirectory(stage);
            try {
                string extension=Path.GetExtension(sourcePath).ToLowerInvariant();
                if(extension!=".png" && extension!=".jpg" && extension!=".jpeg" && extension!=".bmp" && extension!=".tsv" && extension!=".json")
                    extension=".bin";
                File.Copy(sourcePath,Path.Combine(stage,"source"+extension));
                ZoneStore.Save(Path.Combine(stage,"zones.tsv"),zones);
                if(sourceZones!=null) ZoneStore.Save(Path.Combine(stage,"source-zones.tsv"),sourceZones);
                if(report!=null) File.WriteAllText(Path.Combine(stage,"detection.json"),report);
                File.WriteAllText(Path.Combine(stage,"source-name.txt"),Path.GetFileName(sourcePath));
                Directory.Move(stage,destination);
                return destination;
            } finally { if(Directory.Exists(stage)) Directory.Delete(stage,true); }
        }
        internal static bool IsWaypoint(MapZone zone) { return zone!=null && zone.Points!=null && zone.Points.Length==1; }
        static List<MapZone> LoadFile(string path) { return File.Exists(path)?ZoneStore.Load(path):new List<MapZone>(); }
        internal static List<MapZone> Load(MapSidecarPaths paths,string legacyPath,bool includeBundledZones,out bool migrated) {
            migrated=false;
            bool hasScoped=File.Exists(paths.ZonesPath)||File.Exists(paths.WaypointsPath);
            List<MapZone> polygons,waypoints;
            string migrationMarker=Path.Combine(paths.DataFolder,"zones-migration-v1.txt");
            string migratedMapId=null;
            try { if(File.Exists(migrationMarker)) migratedMapId=File.ReadAllText(migrationMarker).Trim(); }
            catch(IOException) {} catch(UnauthorizedAccessException) {}
            bool legacyBelongsToThisMap=string.IsNullOrEmpty(migratedMapId) || string.Equals(migratedMapId,paths.MapId,StringComparison.Ordinal);
            if(!hasScoped && File.Exists(legacyPath) && legacyBelongsToThisMap) {
                List<MapZone> legacy=ZoneStore.Load(legacyPath);
                polygons=legacy.Where(zone=>!IsWaypoint(zone)).ToList();
                waypoints=legacy.Where(IsWaypoint).ToList();
                Save(paths,polygons.Concat(waypoints).ToList());
                File.WriteAllText(migrationMarker,paths.MapId);
                migrated=true;
            } else {
                polygons=File.Exists(paths.ZonesPath)?ZoneStore.Load(paths.ZonesPath):
                    (!hasScoped && includeBundledZones?ZoneStore.Load(paths.ZonesPath):new List<MapZone>());
                waypoints=LoadFile(paths.WaypointsPath);
                if(waypoints.Any(zone=>!IsWaypoint(zone))) throw new InvalidDataException("Custom waypoint file contains a non-waypoint entry.");
            }
            return polygons.Concat(waypoints).ToList();
        }
        internal static List<MapZone> Reload(MapSidecarPaths paths) {
            List<MapZone> polygons=File.Exists(paths.ZonesPath)?ZoneStore.Load(paths.ZonesPath):new List<MapZone>();
            List<MapZone> waypoints=LoadFile(paths.WaypointsPath);
            if(waypoints.Any(zone=>!IsWaypoint(zone))) throw new InvalidDataException("Custom waypoint file contains a non-waypoint entry.");
            return polygons.Concat(waypoints).ToList();
        }
        internal static void Save(MapSidecarPaths paths,List<MapZone> all) {
            SaveFiles(paths.ZonesPath,paths.WaypointsPath,all);
        }
        internal static void SaveFiles(string zonesPath,string waypointsPath,List<MapZone> all) {
            if(all==null) throw new InvalidDataException("No map data was provided.");
            if(all.Count>ZoneStore.MaxZones) throw new InvalidDataException("Too many map annotations to save.");
            List<MapZone> polygons=all.Where(zone=>!IsWaypoint(zone)).ToList();
            List<MapZone> waypoints=all.Where(IsWaypoint).ToList();
            ZoneStore.Save(zonesPath,polygons);
            ZoneStore.Save(waypointsPath,waypoints);
        }
    }

}
