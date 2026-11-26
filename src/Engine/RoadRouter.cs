using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Reflection;

namespace ZiloreMiniMap {
    // Fixed coverage derived from the bundled map; independent of custom user textures.
    internal sealed class RoutingWaterMask {
        byte[] bits;
        int width,height;
        internal bool IsLoaded { get { return bits!=null; } }
        internal void Load() {
            Stream raw=Assembly.GetExecutingAssembly().GetManifestResourceStream("water-mask.bin");
            if(raw==null) {
                foreach(string path in new[]{Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"water-mask.bin"),Path.Combine(Environment.CurrentDirectory,"resources","water-mask.bin")})
                    if(File.Exists(path)) { raw=File.OpenRead(path); break; }
            }
            if(raw==null) return;
            using(raw) using(var zip=new GZipStream(raw,CompressionMode.Decompress)) using(var reader=new BinaryReader(zip)) {
                if(new string(reader.ReadChars(4))!="WATR") throw new InvalidDataException("Invalid water mask.");
                width=reader.ReadInt32(); height=reader.ReadInt32();
                if(width<1 || height<1 || width>8192 || height>8192) throw new InvalidDataException("Invalid water mask size.");
                int count=(width*height+7)/8;
                byte[] data=reader.ReadBytes(count);
                if(data.Length!=count) throw new InvalidDataException("Truncated water mask.");
                bits=data;
            }
        }
        internal bool IsWaterForPresentation(float x,float y) { return Water(x,y); }
        bool Water(float x,float y) {
            if(float.IsNaN(x)||float.IsNaN(y)||x<0||y<0||x>=1||y>=1) return true;
            int index=(int)(y*height)*width+(int)(x*width);
            return (bits[index>>3]&(1<<(index&7)))!=0;
        }
        internal bool AllowsConnector(PointF a,PointF b) {
            if(!IsLoaded) return false;
            double dx=b.X-a.X,dy=b.Y-a.Y;
            if(double.IsNaN(dx)||double.IsNaN(dy)||double.IsInfinity(dx)||double.IsInfinity(dy)) return false;
            // Half-cell spacing preserves small streams and narrow lake inlets.
            int samples=(int)Math.Ceiling(Math.Max(Math.Abs(dx)*width,Math.Abs(dy)*height)*2);
            if(samples>32768) return false;
            samples=Math.Max(1,samples);
            for(int i=0;i<=samples;i++) if(Water(a.X+(float)(dx*i/samples),a.Y+(float)(dy*i/samples))) return false;
            return true;
        }
    }
    public enum RoadRouteMode { Quickest, Shortest, PavedOnly }
    public enum RoadSurface { Unknown, Paved, Dirt }

    public class RoadRoute {
        public RoadRouteMode Mode;
        public double EstimatedSeconds;
        public double PavedDistanceMeters;
        public double DirtDistanceMeters;
        public double UnknownDistanceMeters;
        public bool Success;
        public PointF PlayerPoint;
        public PointF RoadEntryPoint;
        public PointF RoadExitPoint;
        public PointF TargetPoint;
        public PointF[] Polyline; // Full route including entry/exit or just road path
        public int[] JunctionIndices=Array.Empty<int>(); // Actual branch nodes along Polyline, not geometry bends.
        public double TotalDistanceMeters;
        public double RoadDistanceMeters;
        public double EntryDistanceMeters;
        public double ExitDistanceMeters;
        public double RoutingCost;
        internal int LastMatchedSegment = -1;
        internal double[] CumulativeMeters;
        internal double[] EnsureCumulativeMeters() {
            var line = Polyline;
            if (line == null || line.Length < 2) return Array.Empty<double>();
            var existing = CumulativeMeters;
            if (existing != null && existing.Length == line.Length) return existing;
            var cum = new double[line.Length];
            for (int i = 1; i < line.Length; i++) {
                cum[i] = cum[i - 1] + RoadRouter.DistanceMeters(line[i - 1], line[i]);
            }
            CumulativeMeters = cum;
            return cum;
        }
    }

    public class RoadRouter {
        private static readonly RoadRouter instance = new RoadRouter();
        public static RoadRouter Instance {
            get {
                return instance;
            }
        }

        private volatile bool isLoaded;
        private readonly object loadLock = new object();
        public bool IsLoaded { get { return isLoaded; } private set { isLoaded=value; } }

        public struct RoadNode {
            public float U;
            public float V;
        }

        public struct RoadEdge {
            public int U;
            public int V;
            public float LengthMeters;
            public PointF[] Points;
            public bool IsBridge;
            public RoadSurface Surface;
        }

        public struct HalfEdge {
            public int TargetNode;
            public float Weight;
            public int EdgeIndex;
            public bool Forward;
        }

        private List<RoadNode> nodesList;
        private List<RoadEdge> edgesList;
        private List<HalfEdge>[] adj;
        private int gridRes;
        private double heuristicDistanceFactor=1;
        private List<ushort>[] spatialGridList;

        // Fast lookup arrays
        private RoadNode[] nodes;
        private RoadEdge[] edges;
        private ushort[][] spatialGrid;

        // Connected component cache
        private int[] nodeComponent;
        private int componentCount;
        private List<int>[] componentNodes;
        private int mainComponentId;
        private readonly RoutingWaterMask waterMask=new RoutingWaterMask();
        private volatile PointF[][] avoidPolygons=Array.Empty<PointF[]>();
        private volatile bool[] avoidedEdgeFlags;
        private double avoidPenalty=4.0;
        internal static Action OnAvoidZonesChanged;
        public void SetAvoidZones(IEnumerable<PointF[]> polygons,double penaltyMultiplier=4.0) {
            System.Threading.Volatile.Write(ref avoidPenalty,double.IsNaN(penaltyMultiplier)||double.IsInfinity(penaltyMultiplier)?4.0:Math.Max(1.0,Math.Min(20.0,penaltyMultiplier)));
            var nextPolys=polygons==null?Array.Empty<PointF[]>():polygons.Where(p=>p!=null&&p.Length>=3).ToArray();
            avoidPolygons=nextPolys;
            RebuildAvoidedEdgeFlags(nextPolys);
            var cb=OnAvoidZonesChanged; if(cb!=null) cb();
        }
        void RebuildAvoidedEdgeFlags(PointF[][] polys) {
            var localEdges=edges;
            if(localEdges==null||localEdges.Length==0||polys==null||polys.Length==0) {
                avoidedEdgeFlags=null;
                return;
            }
            var boxes=new RectangleF[polys.Length];
            for(int p=0;p<polys.Length;p++) {
                var poly=polys[p];
                float minX=poly[0].X,maxX=minX,minY=poly[0].Y,maxY=minY;
                for(int k=1;k<poly.Length;k++) {
                    if(poly[k].X<minX)minX=poly[k].X; else if(poly[k].X>maxX)maxX=poly[k].X;
                    if(poly[k].Y<minY)minY=poly[k].Y; else if(poly[k].Y>maxY)maxY=poly[k].Y;
                }
                boxes[p]=RectangleF.FromLTRB(minX,minY,maxX,maxY);
            }
            var flags=new bool[localEdges.Length];
            for(int e=0;e<localEdges.Length;e++) {
                var pts=localEdges[e].Points;
                if(pts==null||pts.Length==0) continue;
                float eMinX=pts[0].X,eMaxX=eMinX,eMinY=pts[0].Y,eMaxY=eMinY;
                for(int k=1;k<pts.Length;k++) {
                    float px=pts[k].X,py=pts[k].Y;
                    if(px<eMinX)eMinX=px; else if(px>eMaxX)eMaxX=px;
                    if(py<eMinY)eMinY=py; else if(py>eMaxY)eMaxY=py;
                }
                var eBox=RectangleF.FromLTRB(eMinX,eMinY,eMaxX,eMaxY);
                bool hit=false;
                for(int p=0;p<polys.Length&&!hit;p++) {
                    var b=boxes[p];
                    if(!b.IntersectsWith(eBox)) continue;
                    var poly=polys[p];
                    for(int k=0;k<pts.Length;k++) {
                        PointF cur=pts[k];
                        if(b.Contains(cur)&&PointInPolygon(cur,poly)) { hit=true; break; }
                        if(k+1<pts.Length&&DistanceMeters(cur,pts[k+1])>100.0) {
                            PointF mid=new PointF((cur.X+pts[k+1].X)*0.5f,(cur.Y+pts[k+1].Y)*0.5f);
                            if(b.Contains(mid)&&PointInPolygon(mid,poly)) { hit=true; break; }
                        }
                    }
                }
                if(hit) flags[e]=true;
            }
            avoidedEdgeFlags=flags;
        }
        static bool PointInPolygon(PointF pt,PointF[] poly) {
            bool inside=false;
            for(int i=0,j=poly.Length-1;i<poly.Length;j=i++) {
                if(((poly[i].Y>pt.Y)!=(poly[j].Y>pt.Y))&&(pt.X<(poly[j].X-poly[i].X)*(pt.Y-poly[i].Y)/(poly[j].Y-poly[i].Y)+poly[i].X)) inside=!inside;
            }
            return inside;
        }
        bool IsEdgeAvoided(int edgeIndex) {
            var flags=avoidedEdgeFlags;
            return flags!=null&&(uint)edgeIndex<(uint)flags.Length&&flags[edgeIndex];
        }

        // Reusable zero-allocation A* search state
        private readonly object searchLock = new object();
        private double[] gScores;
        private HalfEdge[] cameFrom;
        private uint[] visitedEpoch;
        private uint[] closedEpoch;
        private uint currentEpoch;
        private MinHeap pq;
        private readonly List<PointF> routeLineBuffer = new List<PointF>(256);
        private readonly List<int> routeJunctionBuffer = new List<int>(64);
        private readonly List<HalfEdge> routePathBuffer = new List<HalfEdge>(128);

        private const double MapScaleX = 1521618.0 / 100.0; // 15216.18 meters across map
        private const double MapScaleY = 1523618.0 / 100.0; // 15236.18 meters across map

        public static double DistanceMeters(PointF a, PointF b) {
            double dx = (b.X - a.X) * MapScaleX;
            double dy = (b.Y - a.Y) * MapScaleY;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static double DistanceMeters(float u1, float v1, float u2, float v2) {
            double dx = (u2 - u1) * MapScaleX;
            double dy = (v2 - v1) * MapScaleY;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        internal PointF[][] PresentationGeometry() { lock(loadLock) { return edgesList==null?Array.Empty<PointF[]>():edgesList.Select(edge=>(PointF[])edge.Points.Clone()).ToArray(); } }
        public void InitializeFromResource() {
            lock(loadLock) { InitializeCore(); }
        }
        private void InitializeCore() {
            if (IsLoaded) return;
            try {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream raw = InstallationAssets.Open("roads.bin")) {
                    if (raw != null) {
                        using (GZipStream gz = new GZipStream(raw, CompressionMode.Decompress))
                        using (BinaryReader br = new BinaryReader(gz)) {
                            LoadBinary(br);
                            return;
                        }
                    }
                }
                // Fallback: check file on disk if running unbundled
                string[] candidates = new string[] {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "roads.bin"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "roads.bin"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScumMiniMap", "resources", "roads.bin"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScumMiniMap", "roads.bin"),
                    Path.Combine(Environment.CurrentDirectory, "resources", "roads.bin"),
                    Path.Combine(Environment.CurrentDirectory, "ScumMiniMap", "resources", "roads.bin"),
                    Path.Combine(Environment.CurrentDirectory, "ScumMiniMap", "roads.bin"),
                    Path.Combine(Environment.CurrentDirectory, "roads.bin")
                };
                foreach (string path in candidates) {
                    if (File.Exists(path)) {
                        using (FileStream fs = File.OpenRead(path))
                        using (GZipStream gz = new GZipStream(fs, CompressionMode.Decompress))
                        using (BinaryReader br = new BinaryReader(gz)) {
                            LoadBinary(br);
                            return;
                        }
                    }
                }
            } catch {
                IsLoaded = false;
            }
        }

        static float ReadCoordinate(BinaryReader reader) {
            float value=reader.ReadSingle();
            if(float.IsNaN(value)||float.IsInfinity(value)||value<-.1f||value>1.1f) throw new InvalidDataException("Invalid road coordinate.");
            return value;
        }
        public void LoadBinary(BinaryReader br) {
            byte[] magic = br.ReadBytes(4);
            IsLoaded=false;
            if (magic.Length!=4) throw new InvalidDataException("Truncated road header.");
            if (magic[0] != 'R' || magic[1] != 'O' || magic[2] != 'A' || magic[3] != 'D') throw new InvalidDataException("Invalid road header.");
            uint version = br.ReadUInt32();
            if (version != 2 && version != 3 && version != 4) throw new InvalidDataException("Unsupported road version.");

            int nodeCount = br.ReadInt32();
            int edgeCount = br.ReadInt32();
            gridRes = br.ReadInt32();
            if(nodeCount<1 || nodeCount>100000 || edgeCount<1 || edgeCount>60000 || gridRes<1 || gridRes>512)
                throw new InvalidDataException("Road counts exceed supported limits.");
            long totalPoints=0, totalReferences=0;
            heuristicDistanceFactor=1;

            nodesList = new List<RoadNode>(nodeCount + 64);
            for (int i = 0; i < nodeCount; i++) {
                nodesList.Add(new RoadNode { U = ReadCoordinate(br), V = ReadCoordinate(br) });
            }

            edgesList = new List<RoadEdge>(edgeCount + 2048);
            adj = new List<HalfEdge>[nodeCount + 2048];
            for (int i = 0; i < adj.Length; i++) adj[i] = new List<HalfEdge>(4);

            for (int i = 0; i < edgeCount; i++) {
                int u = br.ReadInt32();
                int v = br.ReadInt32();
                float lengthM = br.ReadSingle();
                if(u<0 || v<0 || u>=nodeCount || v>=nodeCount || float.IsNaN(lengthM) || float.IsInfinity(lengthM) || lengthM<=0 || lengthM>100000)
                    throw new InvalidDataException("Invalid road edge.");
                byte flags = version >= 3 ? br.ReadByte() : (byte)0;
                if((version < 4 && flags > 1) || (version == 4 && flags > 7)) throw new InvalidDataException("Invalid road surface flags.");
                ushort ptCount = br.ReadUInt16();
                totalPoints+=ptCount;
                if(ptCount<2 || totalPoints>4000000) throw new InvalidDataException("Road geometry exceeds supported limits.");
                PointF[] pts = new PointF[ptCount];
                for (int p = 0; p < ptCount; p++) {
                    pts[p] = new PointF(ReadCoordinate(br), ReadCoordinate(br));
                }
                double geometryLength=0;
                for(int p=1;p<pts.Length;p++)geometryLength+=DistanceMeters(pts[p-1],pts[p]);
                if(geometryLength>0)heuristicDistanceFactor=Math.Min(heuristicDistanceFactor,lengthM/geometryLength);
                edgesList.Add(new RoadEdge { U = u, V = v, LengthMeters = lengthM, Points = pts, IsBridge = (flags & 1) != 0, Surface = version < 4 || (flags & 4) != 0 ? RoadSurface.Unknown : (flags & 2) != 0 ? RoadSurface.Dirt : RoadSurface.Paved });
                adj[u].Add(new HalfEdge { TargetNode = v, Weight = lengthM, EdgeIndex = i, Forward = true });
                adj[v].Add(new HalfEdge { TargetNode = u, Weight = lengthM, EdgeIndex = i, Forward = false });
            }

            int totalCells = gridRes * gridRes;
            spatialGridList = new List<ushort>[totalCells];
            for (int i = 0; i < totalCells; i++) {
                ushort count = br.ReadUInt16();
                totalReferences+=count;
                if(totalReferences>4000000) throw new InvalidDataException("Road index exceeds supported limits.");
                List<ushort> list = new List<ushort>(count + 8);
                for (int c = 0; c < count; c++) { ushort edge=br.ReadUInt16(); if(edge>=edgeCount) throw new InvalidDataException("Invalid road index."); list.Add(edge); }
                spatialGridList[i] = list;
            }

            // Topology is compiled from classified roads, audited dry/3D seams and
            // named surveyed highway bridges. Never synthesize links at runtime.
            waterMask.Load();

            // Commit final arrays for high-performance direct indexing
            nodes = nodesList.ToArray();
            edges = edgesList.ToArray();
            spatialGrid = new ushort[totalCells][];
            for (int i = 0; i < totalCells; i++) {
                spatialGrid[i] = spatialGridList[i].ToArray();
            }

            // Analyze and index connected components
            ComputeConnectedComponents();

            // Allocate reusable zero-allocation A* search buffers
            int totalNodes = nodes.Length;
            gScores = new double[totalNodes];
            cameFrom = new HalfEdge[totalNodes];
            visitedEpoch = new uint[totalNodes];
            closedEpoch = new uint[totalNodes];
            currentEpoch = 1;
            pq = new MinHeap(1024);

            RebuildAvoidedEdgeFlags(avoidPolygons);
            IsLoaded = true;
        }

        private void ComputeConnectedComponents() {
            int count = nodes.Length;
            nodeComponent = new int[count];
            for (int i = 0; i < count; i++) nodeComponent[i] = -1;

            List<List<int>> comps = new List<List<int>>();
            Queue<int> q = new Queue<int>();

            for (int i = 0; i < count; i++) {
                if (nodeComponent[i] >= 0) continue;
                int cIdx = comps.Count;
                List<int> comp = new List<int>();
                q.Enqueue(i);
                nodeComponent[i] = cIdx;

                while (q.Count > 0) {
                    int curr = q.Dequeue();
                    comp.Add(curr);
                    List<HalfEdge> outgoing = adj[curr];
                    for (int k = 0; k < outgoing.Count; k++) {
                        int nbr = outgoing[k].TargetNode;
                        if (nodeComponent[nbr] < 0) {
                            nodeComponent[nbr] = cIdx;
                            q.Enqueue(nbr);
                        }
                    }
                }
                comps.Add(comp);
            }

            componentCount = comps.Count;
            componentNodes = comps.ToArray();

            int maxCount = -1;
            mainComponentId = 0;
            for (int c = 0; c < comps.Count; c++) {
                if (comps[c].Count > maxCount) {
                    maxCount = comps[c].Count;
                    mainComponentId = c;
                }
            }
        }

        [ThreadStatic] static HashSet<int> tlsInspected;
        static HashSet<int> BorrowInspectedSet() {
            var set = tlsInspected;
            if (set == null) tlsInspected = set = new HashSet<int>();
            else set.Clear();
            return set;
        }

        public struct SnapResult {
            public int EdgeIndex;
            public PointF Projected;
            public double DistanceMeters;
            public int SegmentIndex;
            public double SegmentT;
        }

        private bool AllowsRoadApproach(PointF point, PointF projected, int edgeIndex, double distance) {
            // A map water pixel beneath a known bridge must not reject a player
            // already on its deck. The exemption is confined to the deck width.
            return waterMask.IsLoaded && (waterMask.AllowsConnector(point,projected) ||
                (edges[edgeIndex].IsBridge && distance <= 8.0));
        }

        public SnapResult SnapToComponent(PointF pt, int targetComponent, double maxSearchMeters = 3500.0, RoadRouteMode mode = RoadRouteMode.Shortest) {
            if (!IsLoaded || edges == null || edges.Length == 0)
                return new SnapResult { EdgeIndex = -1, DistanceMeters = double.MaxValue };

            int gu = (int)Math.Floor(pt.X * gridRes);
            int gv = (int)Math.Floor(pt.Y * gridRes);
            gu = Math.Max(0, Math.Min(gridRes - 1, gu));
            gv = Math.Max(0, Math.Min(gridRes - 1, gv));

            HashSet<int> inspected = BorrowInspectedSet();
            int maxRadius = Math.Max(1, (int)Math.Ceiling(maxSearchMeters / (15220.0 / gridRes)));

            double bestDist = double.MaxValue;
            SnapResult bestSnap = new SnapResult { EdgeIndex = -1, DistanceMeters = double.MaxValue };

            for (int r = 0; r <= maxRadius; r++) {
                for (int dx = -r; dx <= r; dx++) {
                    for (int dy = -r; dy <= r; dy++) {
                        if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                        int cx = gu + dx;
                        int cy = gv + dy;
                        if (cx < 0 || cx >= gridRes || cy < 0 || cy >= gridRes) continue;

                        ushort[] cellEdges = spatialGrid[cy * gridRes + cx];
                        if (cellEdges == null) continue;

                        for (int e = 0; e < cellEdges.Length; e++) {
                            int eid = cellEdges[e];
                            if (inspected.Contains(eid)) continue;
                            inspected.Add(eid);

                            if (nodeComponent[edges[eid].U] != targetComponent || !AllowsSurface(edges[eid],mode)) continue;

                            PointF[] pts = edges[eid].Points;
                            for (int i = 0; i < pts.Length - 1; i++) {
                                PointF a = pts[i];
                                PointF b = pts[i + 1];
                                double ldx = b.X - a.X;
                                double ldy = b.Y - a.Y;
                                double l2 = ldx * ldx + ldy * ldy;
                                double t = 0;
                                if (l2 > 1e-12) {
                                    t = Math.Max(0.0, Math.Min(1.0, ((pt.X - a.X) * ldx + (pt.Y - a.Y) * ldy) / l2));
                                }
                                PointF proj = new PointF((float)(a.X + t * ldx), (float)(a.Y + t * ldy));
                                double d = DistanceMeters(pt, proj);
                                if (d <= maxSearchMeters && d < bestDist && AllowsRoadApproach(pt,proj,eid,d)) {
                                    bestDist = d;
                                    bestSnap = new SnapResult {
                                        EdgeIndex = eid,
                                        Projected = proj,
                                        DistanceMeters = d,
                                        SegmentIndex = i,
                                        SegmentT = t
                                    };
                                }
                            }
                        }
                    }
                }
                double ringMinMeters = Math.Max(0, (r - 1) * (15220.0 / gridRes));
                if (bestDist < ringMinMeters) break;
            }

            return bestSnap;
        }

        public List<SnapResult> SnapToRoadCandidates(PointF pt, double maxSearchMeters = 3000.0, int maxCandidates = 8, RoadRouteMode mode = RoadRouteMode.Shortest) {
            List<SnapResult> results = new List<SnapResult>(maxCandidates);
            if (!IsLoaded || edges == null || edges.Length == 0) return results;

            int gu = (int)Math.Floor(pt.X * gridRes);
            int gv = (int)Math.Floor(pt.Y * gridRes);
            gu = Math.Max(0, Math.Min(gridRes - 1, gu));
            gv = Math.Max(0, Math.Min(gridRes - 1, gv));

            HashSet<int> inspected = BorrowInspectedSet();
            int maxRadius = Math.Max(1, (int)Math.Ceiling(maxSearchMeters / (15220.0 / gridRes)));

            for (int r = 0; r <= maxRadius; r++) {
                bool foundInRing = false;
                for (int dx = -r; dx <= r; dx++) {
                    for (int dy = -r; dy <= r; dy++) {
                        if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                        int cx = gu + dx;
                        int cy = gv + dy;
                        if (cx < 0 || cx >= gridRes || cy < 0 || cy >= gridRes) continue;

                        ushort[] cellEdges = spatialGrid[cy * gridRes + cx];
                        if (cellEdges == null) continue;

                        for (int e = 0; e < cellEdges.Length; e++) {
                            int eid = cellEdges[e];
                            if (inspected.Contains(eid)) continue;
                            inspected.Add(eid);

                            if(!AllowsSurface(edges[eid],mode)) continue;
                            PointF[] pts = edges[eid].Points;
                            double bestEdgeDist = double.MaxValue;
                            SnapResult bestEdgeSnap = default(SnapResult);

                            for (int i = 0; i < pts.Length - 1; i++) {
                                PointF a = pts[i];
                                PointF b = pts[i + 1];
                                double ldx = b.X - a.X;
                                double ldy = b.Y - a.Y;
                                double l2 = ldx * ldx + ldy * ldy;
                                double t = 0;
                                if (l2 > 1e-12) {
                                    t = Math.Max(0.0, Math.Min(1.0, ((pt.X - a.X) * ldx + (pt.Y - a.Y) * ldy) / l2));
                                }
                                PointF proj = new PointF((float)(a.X + t * ldx), (float)(a.Y + t * ldy));
                                double d = DistanceMeters(pt, proj);
                                if (d < bestEdgeDist && AllowsRoadApproach(pt,proj,eid,d)) {
                                    bestEdgeDist = d;
                                    bestEdgeSnap = new SnapResult {
                                        EdgeIndex = eid,
                                        Projected = proj,
                                        DistanceMeters = d,
                                        SegmentIndex = i,
                                        SegmentT = t
                                    };
                                }
                            }

                            if (bestEdgeDist <= maxSearchMeters) {
                                foundInRing = true;
                                int ins = 0;
                                while (ins < results.Count && results[ins].DistanceMeters <= bestEdgeDist) ins++;
                                if (ins < maxCandidates) {
                                    results.Insert(ins, bestEdgeSnap);
                                    if (results.Count > maxCandidates) results.RemoveAt(results.Count - 1);
                                }
                            }
                        }
                    }
                }
                if (foundInRing && results.Count >= maxCandidates && r >= 2 &&
                    results[results.Count-1].DistanceMeters <= (r-1)*(15216.18/gridRes)) break;
            }

            // Stay on the nearest road when already on it. From open ground,
            // allow nearby alternatives on the same bank to reach a connected road.
            if (results.Count > 0) {
                double nearest = results[0].DistanceMeters;
                double tolerance = nearest <= 5.0 ? 2.0 : nearest <= 50.0 ? 5.0 : 100.0;
                results.RemoveAll(candidate => candidate.DistanceMeters > nearest + tolerance);
                // Dense isolated service roads can fill every candidate slot.
                // From open ground, retain a dry approach to the main road too.
                if(maxCandidates > 1 && nearest > 15.0 && !results.Any(candidate => nodeComponent[edges[candidate.EdgeIndex].U] == mainComponentId)) {
                    SnapResult main = SnapToComponent(pt,mainComponentId,maxSearchMeters,mode);
                    if(main.EdgeIndex >= 0) results.Add(main);
                }
            }

            return results;
        }

        public SnapResult SnapToRoad(PointF pt, double maxSearchMeters = 2000.0) {
            List<SnapResult> cands = SnapToRoadCandidates(pt, maxSearchMeters, 1);
            if (cands.Count > 0) return cands[0];
            return new SnapResult { EdgeIndex = -1, DistanceMeters = double.MaxValue };
        }

        private class MinHeap {
            private struct Entry {
                public double Priority;
                public int Node;
            }
            private Entry[] heap;
            public int Count { get; private set; }

            public MinHeap(int initialCapacity = 512) {
                heap = new Entry[initialCapacity];
            }

            public void Clear() {
                Count = 0;
            }

            public void Push(double priority, int node) {
                if (Count == heap.Length) {
                    Array.Resize(ref heap, heap.Length * 2);
                }
                int i = Count++;
                heap[i] = new Entry { Priority = priority, Node = node };
                while (i > 0) {
                    int p = (i - 1) >> 1;
                    if (heap[p].Priority <= heap[i].Priority) break;
                    Entry tmp = heap[p]; heap[p] = heap[i]; heap[i] = tmp;
                    i = p;
                }
            }

            public double Minimum { get { return Count==0?double.PositiveInfinity:heap[0].Priority; } }
            public int Pop() {
                int res = heap[0].Node;
                Count--;
                if (Count > 0) {
                    heap[0] = heap[Count];
                    int i = 0;
                    while (true) {
                        int left = (i << 1) + 1;
                        if (left >= Count) break;
                        int right = left + 1;
                        int best = (right < Count && heap[right].Priority < heap[left].Priority) ? right : left;
                        if (heap[i].Priority <= heap[best].Priority) break;
                        Entry tmp = heap[i]; heap[i] = heap[best]; heap[best] = tmp;
                        i = best;
                    }
                }
                return res;
            }
        }

        public RoadRoute FindRoute(PointF playerPt, PointF targetPt) { return FindRoute(playerPt,targetPt,RoadRouteMode.Shortest); }
        public RoadRoute FindRoute(PointF playerPt, PointF targetPt, RoadRouteMode mode) {
            if(!Enum.IsDefined<RoadRouteMode>(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            RoadRoute result = new RoadRoute {
                Success = false, Mode = mode,
                PlayerPoint = playerPt,
                TargetPoint = targetPt,
                TotalDistanceMeters = DistanceMeters(playerPt, targetPt)
            };

            if (!IsLoaded || !waterMask.IsLoaded) return result;

            List<SnapResult> startCands = SnapToRoadCandidates(playerPt, 1000.0, 4, mode);
            if (startCands.Count == 0) startCands = SnapToRoadCandidates(playerPt, 2500.0, 4, mode);
            List<SnapResult> endCands = SnapToRoadCandidates(targetPt, 1000.0, 4, mode);
            if (endCands.Count == 0) endCands = SnapToRoadCandidates(targetPt, 2500.0, 4, mode);

            if (startCands.Count == 0 || endCands.Count == 0) {
                return result;
            }

            // 1. Try all candidate combinations where compStart == compEnd (pure land/bridge route)
            RoadRoute bestConnectedRoute = null;
            double bestTotalDist = double.MaxValue;

            double approachDiv = mode == RoadRouteMode.Quickest ? (5.0 / 3.6) : 1.0;
            double heurDiv = mode == RoadRouteMode.Quickest ? (60.0 / 3.6) : 1.0;
            for (int s = 0; s < startCands.Count; s++) {
                SnapResult snapStart = startCands[s];
                int compStart = nodeComponent[edges[snapStart.EdgeIndex].U];

                for (int e = 0; e < endCands.Count; e++) {
                    SnapResult snapEnd = endCands[e];
                    int compEnd = nodeComponent[edges[snapEnd.EdgeIndex].U];

                    if (compStart == compEnd) {
                        double approachCost = (snapStart.DistanceMeters + snapEnd.DistanceMeters) / approachDiv;
                        double minRoadCost = DistanceMeters(snapStart.Projected, snapEnd.Projected) * heuristicDistanceFactor / heurDiv;
                        if (bestTotalDist < double.MaxValue && approachCost + minRoadCost >= bestTotalDist) continue;
                        double maxRoadCost = bestTotalDist < double.MaxValue ? (bestTotalDist - approachCost) : double.PositiveInfinity;
                        RoadRoute candidateRoute = ComputeConnectedRoute(playerPt, targetPt, snapStart, snapEnd, mode, maxRoadCost);
                        if (candidateRoute != null && candidateRoute.Success) {
                            double cost=candidateRoute.RoutingCost>0?candidateRoute.RoutingCost:(mode==RoadRouteMode.Quickest?candidateRoute.EstimatedSeconds:candidateRoute.TotalDistanceMeters);
                            if (cost < bestTotalDist) {
                                bestTotalDist = cost;
                                bestConnectedRoute = candidateRoute;
                            }
                        }
                    }
                }
            }

            if (bestConnectedRoute != null) {
                return bestConnectedRoute;
            }

            // Road navigation must never invent a swimming/boat crossing.
            return result;
        }

        static bool AllowsSurface(RoadEdge edge,RoadRouteMode mode) { return mode!=RoadRouteMode.PavedOnly || edge.Surface==RoadSurface.Paved; }
        // Planning assumptions in metres/second: 60 km/h paved, 30 dirt, 20 unknown.
        // Dry entry/exit approaches use 5 km/h. These are estimates, not live traffic.
        static double Speed(RoadEdge edge) { return edge.Surface==RoadSurface.Paved?60/3.6:edge.Surface==RoadSurface.Dirt?30/3.6:20/3.6; }
        double Cost(int edgeIndex,RoadEdge edge,double length,RoadRouteMode mode) { double c=mode==RoadRouteMode.Quickest?length/Speed(edge):length; return IsEdgeAvoided(edgeIndex)?c*System.Threading.Volatile.Read(ref avoidPenalty):c; }
        static double Along(RoadEdge edge,SnapResult snap) {
            double total=0,before=0;
            for(int i=0;i<edge.Points.Length-1;i++) {
                double length=DistanceMeters(edge.Points[i],edge.Points[i+1]);total+=length;
                if(i<snap.SegmentIndex)before+=length;
                else if(i==snap.SegmentIndex)before+=length*snap.SegmentT;
            }
            return total>0?edge.LengthMeters*Math.Max(0,Math.Min(1,before/total)):0;
        }
        static void Measure(RoadRoute result,RoadEdge edge,double length) {
            result.RoadDistanceMeters+=length;result.EstimatedSeconds+=length/Speed(edge);
            if(edge.Surface==RoadSurface.Paved)result.PavedDistanceMeters+=length;
            else if(edge.Surface==RoadSurface.Dirt)result.DirtDistanceMeters+=length;
            else result.UnknownDistanceMeters+=length;
        }
        static void AppendSection(List<PointF> line,RoadEdge edge,SnapResult snap,bool toNode,bool atU) {
            if(toNode) {
                line.Add(snap.Projected);
                if(atU)for(int i=snap.SegmentIndex;i>=0;i--)line.Add(edge.Points[i]);
                else for(int i=snap.SegmentIndex+1;i<edge.Points.Length;i++)line.Add(edge.Points[i]);
            } else {
                if(atU)for(int i=1;i<=snap.SegmentIndex;i++)line.Add(edge.Points[i]);
                else for(int i=edge.Points.Length-2;i>snap.SegmentIndex;i--)line.Add(edge.Points[i]);
                line.Add(snap.Projected);
            }
        }
        private RoadRoute ComputeConnectedRoute(PointF playerPt, PointF targetPt, SnapResult snapStart, SnapResult snapEnd,RoadRouteMode mode,double maxRoadCost=double.PositiveInfinity) {
            var result=new RoadRoute { Mode=mode,PlayerPoint=playerPt,TargetPoint=targetPt,
                RoadEntryPoint=snapStart.Projected,RoadExitPoint=snapEnd.Projected,
                EntryDistanceMeters=snapStart.DistanceMeters,ExitDistanceMeters=snapEnd.DistanceMeters };
            RoadEdge first=edges[snapStart.EdgeIndex],last=edges[snapEnd.EdgeIndex];
            if(!AllowsSurface(first,mode)||!AllowsSurface(last,mode))return result;
            double startU=Along(first,snapStart),startV=first.LengthMeters-startU;
            double endU=Along(last,snapEnd),endV=last.LengthMeters-endU;
            bool direct=snapStart.EdgeIndex==snapEnd.EdgeIndex;
            double directCost=direct?Cost(snapStart.EdgeIndex,first,Math.Abs(endU-startU),mode):double.PositiveInfinity;
            if(direct&&directCost>=maxRoadCost) direct=false;
            double best=Math.Min(directCost,maxRoadCost);
            int reached=-1;bool endAtU=true;
            PointF[] polylineOut;
            int[] junctionsOut;
            lock(searchLock) {
                routeLineBuffer.Clear();
                routeJunctionBuffer.Clear();
                var line=routeLineBuffer;var junctions=routeJunctionBuffer;
                currentEpoch++;if(currentEpoch==0) { Array.Clear(visitedEpoch,0,visitedEpoch.Length);Array.Clear(closedEpoch,0,closedEpoch.Length);currentEpoch=1; }
                pq.Clear();
                for(int i=0;i<2;i++) {
                    int n=(i==0)?first.U:first.V;double cost=Cost(snapStart.EdgeIndex,first,(i==0)?startU:startV,mode);
                    if(visitedEpoch[n]==currentEpoch&&gScores[n]<=cost)continue;
                    visitedEpoch[n]=currentEpoch;gScores[n]=cost;cameFrom[n]=new HalfEdge { EdgeIndex=-1,Forward=i==0 };
                    pq.Push(cost+Heuristic(n,snapEnd.Projected,mode),n);
                }
                // The destination is a virtual point inside its edge. Include its
                // remaining edge cost before choosing which endpoint wins.
                while(pq.Count>0&&pq.Minimum<best) {
                    int current=pq.Pop();
                    if(closedEpoch[current]==currentEpoch)continue;
                    closedEpoch[current]=currentEpoch;
                    double currentCost=gScores[current];
                    if(current==last.U||current==last.V) {
                        bool atU=current==last.U&&(last.U!=last.V||endU<=endV);
                        double cost=currentCost+Cost(snapEnd.EdgeIndex,last,atU?endU:endV,mode);
                        if(cost<best) { best=cost;reached=current;endAtU=atU;direct=false; }
                    }
                    var outgoing=adj[current];
                    for(int hi=0;hi<outgoing.Count;hi++) {
                        var half=outgoing[hi];
                        var edge=edges[half.EdgeIndex];if(!AllowsSurface(edge,mode))continue;
                        int next=half.TargetNode;double cost=currentCost+Cost(half.EdgeIndex,edge,edge.LengthMeters,mode);
                        if(visitedEpoch[next]==currentEpoch&&gScores[next]<=cost)continue;
                        visitedEpoch[next]=currentEpoch;gScores[next]=cost;cameFrom[next]=half;
                        pq.Push(cost+Heuristic(next,snapEnd.Projected,mode),next);
                    }
                }
                if(direct) {
                    line.Add(snapStart.Projected);
                    if(startU<=endU)for(int i=snapStart.SegmentIndex+1;i<=snapEnd.SegmentIndex;i++)line.Add(first.Points[i]);
                    else for(int i=snapStart.SegmentIndex;i>snapEnd.SegmentIndex;i--)line.Add(first.Points[i]);
                    line.Add(snapEnd.Projected);Measure(result,first,Math.Abs(endU-startU));
                } else {
                    if(reached<0)return result;
                    routePathBuffer.Clear();var path=routePathBuffer;int trace=reached;
                    while(cameFrom[trace].EdgeIndex>=0) {
                        var half=cameFrom[trace];path.Add(half);trace=half.Forward?edges[half.EdgeIndex].U:edges[half.EdgeIndex].V;
                        if(path.Count>nodes.Length)throw new InvalidDataException("Road predecessor cycle.");
                    }
                    path.Reverse();AppendSection(line,first,snapStart,true,cameFrom[trace].Forward);
                    Measure(result,first,cameFrom[trace].Forward?startU:startV);
                    if(adj[trace].Count>=3)junctions.Add(line.Count-1);
                    for(int pi=0;pi<path.Count;pi++) {
                        var half=path[pi];
                        var edge=edges[half.EdgeIndex];var points=edge.Points;
                        if(half.Forward)for(int i=1;i<points.Length;i++)line.Add(points[i]);
                        else for(int i=points.Length-2;i>=0;i--)line.Add(points[i]);
                        Measure(result,edge,edge.LengthMeters);
                        if(adj[half.TargetNode].Count>=3)junctions.Add(line.Count-1);
                    }
                    AppendSection(line,last,snapEnd,false,endAtU);Measure(result,last,endAtU?endU:endV);
                }
                polylineOut=line.ToArray();
                junctionsOut=junctions.Count>0?junctions.ToArray():Array.Empty<int>();
            }
            if(result.RoadDistanceMeters<1&&DistanceMeters(playerPt,targetPt)>35)return result;
            result.TotalDistanceMeters=result.EntryDistanceMeters+result.RoadDistanceMeters+result.ExitDistanceMeters;
            result.EstimatedSeconds+=(result.EntryDistanceMeters+result.ExitDistanceMeters)/(5/3.6);
            result.RoutingCost=best+(result.EntryDistanceMeters+result.ExitDistanceMeters)/(mode==RoadRouteMode.Quickest?(5.0/3.6):1.0);
            result.Polyline=polylineOut;result.JunctionIndices=junctionsOut;result.Success=true;return result;
        }
        public struct TurnInstruction {
            public bool Valid;
            public double DistanceMeters;
            public double TurnAngleDeg; // Negative = left, Positive = right
            public int PolylineIndex;
        }

        public static double DistanceToPolylineMeters(PointF pt, PointF[] polyline, out int nearestSegmentIndex) {
            return DistanceToPolylineMeters(pt, polyline, -1, out nearestSegmentIndex);
        }

        public static double DistanceToPolylineMeters(PointF pt, PointF[] polyline, int hintSegment, out int nearestSegmentIndex) {
            nearestSegmentIndex = -1;
            if (polyline == null || polyline.Length < 2) return double.MaxValue;
            int lastSeg = polyline.Length - 2;
            double px = pt.X, py = pt.Y;
            if (hintSegment >= 0 && hintSegment <= lastSeg && polyline.Length > 96) {
                int lo = Math.Max(0, hintSegment - 24);
                int hi = Math.Min(lastSeg, hintSegment + 64);
                double winBestSq = ScanPolylineWindow(px, py, polyline, lo, hi, out int winSeg);
                if (winBestSq <= 65.0 * 65.0) {
                    nearestSegmentIndex = winSeg;
                    return Math.Sqrt(winBestSq);
                }
            }
            double bestDistSq = ScanPolylineWindow(px, py, polyline, 0, lastSeg, out nearestSegmentIndex);
            return bestDistSq < double.MaxValue ? Math.Sqrt(bestDistSq) : double.MaxValue;
        }

        static double ScanPolylineWindow(double px, double py, PointF[] polyline, int startSeg, int endSeg, out int bestSeg) {
            bestSeg = -1;
            double bestDistSq = double.MaxValue;
            for (int i = startSeg; i <= endSeg; i++) {
                double ax = polyline[i].X, ay = polyline[i].Y;
                double dx = polyline[i + 1].X - ax, dy = polyline[i + 1].Y - ay;
                double l2 = dx * dx + dy * dy;
                double t = l2 > 1e-12 ? Math.Max(0.0, Math.Min(1.0, ((px - ax) * dx + (py - ay) * dy) / l2)) : 0.0;
                double ex = (px - (ax + t * dx)) * MapScaleX;
                double ey = (py - (ay + t * dy)) * MapScaleY;
                double dSq = ex * ex + ey * ey;
                if (dSq < bestDistSq) { bestDistSq = dSq; bestSeg = i; }
            }
            return bestDistSq;
        }

        public bool IsWaterAt(PointF pt) {
            return waterMask.IsLoaded && waterMask.IsWaterForPresentation(pt.X, pt.Y);
        }

        public bool IsDirectWaterClear(PointF a, PointF b) {
            if (!waterMask.IsLoaded) return false;
            double dx = b.X - a.X, dy = b.Y - a.Y;
            if (double.IsNaN(dx) || double.IsNaN(dy) || double.IsInfinity(dx) || double.IsInfinity(dy)) return false;
            int samples = Math.Max(4, Math.Min(512, (int)Math.Ceiling(DistanceMeters(a, b) / 25.0)));
            for (int i = 1; i < samples; i++) {
                float x = a.X + (float)(dx * i / samples);
                float y = a.Y + (float)(dy * i / samples);
                if (!waterMask.IsWaterForPresentation(x, y)) return false;
            }
            return true;
        }

        public static double AdaptiveOffRouteThreshold(double speedMps) {
            if (speedMps < 5.5) return 35.0;   // Foot: tight precision
            if (speedMps < 18.0) return 55.0;  // Standard car
            return 80.0;                       // Fast vehicle
        }

        public static bool IsOffRoute(PointF playerPt, RoadRoute route, double thresholdMeters = 80.0) {
            if (route == null || !route.Success || route.Polyline == null || route.Polyline.Length < 2) return false;
            int seg;
            double dist = DistanceToPolylineMeters(playerPt, route.Polyline, route.LastMatchedSegment, out seg);
            if (seg >= 0) route.LastMatchedSegment = seg;
            return dist > thresholdMeters;
        }

        static PointF SampleAlongPolyline(PointF[] line, int startIdx, int stepDir, double targetMeters) {
            double acc = 0.0;
            int idx = startIdx;
            while (idx + stepDir >= 0 && idx + stepDir < line.Length) {
                int nextIdx = idx + stepDir;
                double seg = DistanceMeters(line[idx], line[nextIdx]);
                if (acc + seg >= targetMeters && seg > 0.001) {
                    double t = (targetMeters - acc) / seg;
                    return new PointF(
                        (float)(line[idx].X + (line[nextIdx].X - line[idx].X) * t),
                        (float)(line[idx].Y + (line[nextIdx].Y - line[idx].Y) * t));
                }
                acc += seg;
                idx = nextIdx;
            }
            return line[idx];
        }

        public static double RemainingDistanceMeters(PointF playerPt, RoadRoute route) {
            if (route == null || !route.Success) return 0.0;
            PointF[] line = route.Polyline;
            if (line == null || line.Length < 2) return Math.Max(0.0, route.TotalDistanceMeters);
            int curSeg;
            double offDist = DistanceToPolylineMeters(playerPt, line, route.LastMatchedSegment, out curSeg);
            if (curSeg >= 0) route.LastMatchedSegment = curSeg;
            if (curSeg < 0 || curSeg >= line.Length - 1) return Math.Max(0.0, route.TotalDistanceMeters);
            double[] cum = route.EnsureCumulativeMeters();
            PointF sa = line[curSeg], sb = line[curSeg + 1];
            double sdx = sb.X - sa.X, sdy = sb.Y - sa.Y, sl2 = sdx * sdx + sdy * sdy;
            double st = sl2 > 1e-12 ? Math.Max(0.0, Math.Min(1.0, ((playerPt.X - sa.X) * sdx + (playerPt.Y - sa.Y) * sdy) / sl2)) : 0.0;
            PointF proj = new PointF((float)(sa.X + st * sdx), (float)(sa.Y + st * sdy));
            double alongRemaining = DistanceMeters(proj, sb) + (cum[line.Length - 1] - cum[curSeg + 1]);
            double totalRemaining = alongRemaining + (offDist > 15.0 ? offDist : 0.0) + Math.Max(0.0, route.ExitDistanceMeters);
            return Math.Max(0.0, totalRemaining);
        }

        public static TurnInstruction GetNextTurn(PointF playerPt, RoadRoute route) {
            var none = new TurnInstruction { Valid = false };
            if (route == null || !route.Success || route.Polyline == null || route.Polyline.Length < 3 || route.JunctionIndices == null || route.JunctionIndices.Length == 0)
                return none;
            int curSeg;
            double offDist = DistanceToPolylineMeters(playerPt, route.Polyline, route.LastMatchedSegment, out curSeg);
            if (curSeg >= 0) route.LastMatchedSegment = curSeg;
            if (curSeg < 0 || offDist > 60.0) return none;
            PointF[] line = route.Polyline;
            double[] cum = route.EnsureCumulativeMeters();
            PointF sa = line[curSeg], sb = line[curSeg + 1];
            double sdx = sb.X - sa.X, sdy = sb.Y - sa.Y, sl2 = sdx * sdx + sdy * sdy;
            double st = sl2 > 1e-12 ? Math.Max(0.0, Math.Min(1.0, ((playerPt.X - sa.X) * sdx + (playerPt.Y - sa.Y) * sdy) / sl2)) : 0.0;
            PointF proj = new PointF((float)(sa.X + st * sdx), (float)(sa.Y + st * sdy));
            double projToNext = DistanceMeters(proj, sb);
            double baseCum = cum[curSeg + 1];
            for (int ji = 0; ji < route.JunctionIndices.Length; ji++) {
                int idx = route.JunctionIndices[ji];
                if (idx <= curSeg || idx <= 0 || idx >= line.Length - 1) continue;
                double dist = projToNext + (cum[idx] - baseCum);
                PointF curr = line[idx];
                PointF prev = SampleAlongPolyline(line, idx, -1, 25.0);
                PointF next = SampleAlongPolyline(line, idx, 1, 25.0);
                double inAng = Math.Atan2((curr.Y - prev.Y) * MapScaleY, (curr.X - prev.X) * MapScaleX);
                double outAng = Math.Atan2((next.Y - curr.Y) * MapScaleY, (next.X - curr.X) * MapScaleX);
                double delta = (outAng - inAng) * (180.0 / Math.PI);
                while (delta > 180.0) delta -= 360.0;
                while (delta < -180.0) delta += 360.0;
                if (Math.Abs(delta) < 22.0) continue; // Ignore gentle road bends
                return new TurnInstruction { Valid = true, DistanceMeters = dist, TurnAngleDeg = delta, PolylineIndex = idx };
            }
            return none;
        }

        double Heuristic(int node,PointF target,RoadRouteMode mode) {
            double distance=DistanceMeters(nodes[node].U,nodes[node].V,target.X,target.Y)*heuristicDistanceFactor;
            return mode==RoadRouteMode.Quickest?distance/(60/3.6):distance;
        }
    }
}
