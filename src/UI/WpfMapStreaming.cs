using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZiloreMiniMap {
    internal sealed partial class WpfMapSurface {
        // Spatial index for fast POI queries without per-frame LINQ allocations.
        sealed class PoiIndex {
            readonly struct IndexedCluster {
                internal readonly int Order;
                internal readonly WpfPoiCluster Cluster;
                internal IndexedCluster(int order, WpfPoiCluster cluster) { Order = order; Cluster = cluster; }
            }
            static readonly Comparison<IndexedCluster> OrderComparison = (a, b) => a.Order.CompareTo(b.Order);
            static int PackCell(int x, int y) { return ((y + 8) << 8) | ((x + 8) & 0xFF); }
            readonly Dictionary<int, List<IndexedCluster>> cells = new Dictionary<int, List<IndexedCluster>>(64);
            internal PoiIndex(WpfPoiCluster[] layout) {
                for(int i=0;i<layout.Length;i++) {
                    var cluster=layout[i];
                    int cx=(int)Math.Floor(cluster.Point.X*64);
                    int cy=(int)Math.Floor(cluster.Point.Y*64);
                    int key=PackCell(cx,cy);
                    List<IndexedCluster> cell;
                    if(!cells.TryGetValue(key,out cell)) cells.Add(key,cell=new List<IndexedCluster>(8));
                    cell.Add(new IndexedCluster(i,cluster));
                }
            }
            internal List<WpfPoiCluster> Query(Rect bounds) {
                var scratch=new List<IndexedCluster>(32);
                int x1=Math.Max(-1,(int)Math.Floor(bounds.Left*64)),y1=Math.Max(-1,(int)Math.Floor(bounds.Top*64));
                int x2=Math.Min(64,(int)Math.Floor(bounds.Right*64)),y2=Math.Min(64,(int)Math.Floor(bounds.Bottom*64));
                for(int y=y1;y<=y2;y++) {
                    for(int x=x1;x<=x2;x++) {
                        List<IndexedCluster> cell;
                        if(!cells.TryGetValue(PackCell(x,y),out cell)) continue;
                        for(int i=0;i<cell.Count;i++) {
                            var entry=cell[i];
                            var pt=entry.Cluster.Point;
                            if(pt.X>=bounds.Left&&pt.X<=bounds.Right&&pt.Y>=bounds.Top&&pt.Y<=bounds.Bottom)
                                scratch.Add(entry);
                        }
                    }
                }
                if(scratch.Count>1) scratch.Sort(OrderComparison);
                var result=new List<WpfPoiCluster>(scratch.Count);
                for(int i=0;i<scratch.Count;i++) result.Add(scratch[i].Cluster);
                return result;
            }
        }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WpfPoiCluster[],PoiIndex> poiIndexes=new System.Runtime.CompilerServices.ConditionalWeakTable<WpfPoiCluster[],PoiIndex>();
        static PoiIndex Index(WpfPoiCluster[] layout) { return poiIndexes.GetValue(layout,items=>new PoiIndex(items)); }
        static Rect ViewBounds(WpfMapState state,double width,double height,bool compact,double margin) {
            double side=MapSide(state,width,height,compact);var centre=CameraCentre(state,width,height,compact);
            double angle=Rotation(state,compact)*Math.PI/180,c=Math.Abs(Math.Cos(angle)),s=Math.Abs(Math.Sin(angle));
            double halfWidth=(width*c+height*s)/2+margin,halfHeight=(width*s+height*c)/2+margin;
            return new Rect(centre.X-halfWidth/side,centre.Y-halfHeight/side,halfWidth*2/side,halfHeight*2/side);
        }
        internal static IEnumerable<WpfPoiCluster> VisiblePoiClusters(WpfMapState state,double width,double height,bool compact) {
            if(width<=0||height<=0)return Array.Empty<WpfPoiCluster>();
            return Index(PoiClusters(state,width,height,compact)).Query(ViewBounds(state,width,height,compact,24));
        }
        static bool VisibleMarker(Point at,double width,double height,double rotation) {
            var screen=RotatePoint(at,rotation,width,height);
            return screen.X>=-252&&screen.X<=width+24&&screen.Y>=-40&&screen.Y<=height+48;
        }
        sealed class StreamedPoiTile {
            internal Point Key;internal Rect Bounds;internal DrawingGroup Drawing;internal int Pixels;internal LinkedListNode<StreamedPoiTile> LruNode;
        }
        readonly Dictionary<Point,StreamedPoiTile> streamedPoiTiles=new Dictionary<Point,StreamedPoiTile>(poiCellComparer);
        readonly LinkedList<StreamedPoiTile> poiLruList=new LinkedList<StreamedPoiTile>();
        void TouchPoiTileLru(StreamedPoiTile tile) {
            if(tile.LruNode==null) { tile.LruNode=poiLruList.AddFirst(tile); return; }
            if(tile.LruNode!=poiLruList.First) { poiLruList.Remove(tile.LruNode); poiLruList.AddFirst(tile.LruNode); }
        }
        readonly List<Point> missingTileBuffer=new List<Point>(32);
        readonly System.Text.StringBuilder sharedKeyBuilder=new System.Text.StringBuilder(256);
        int poiCachedPixels;
        StreamedPoiTile[] fallbackPoiTiles;
        double fallbackPoiSide;
        string fallbackPoiFilter,requestedPoiFilter;
        bool poiBatchRunning;
        double streamedPoiDpi,streamedPoiSpan;
        internal int PoiTilesBuilt,PoiStreamResets;
        internal bool PoiViewportReady;
        internal int PoiCachedTiles { get { return streamedPoiTiles.Count; } }
        internal int PoiCachedPixels { get { return poiCachedPixels; } }
        internal static void PrewarmPoiCache(WpfMapState state,double width,double height) {
            if(state==null||state.Store==null||!state.Store.MasterEnabled||width<=0||height<=0)return;
            var layout=PoiClusters(state,width,height,false);
            if(layout!=null&&layout.Length>0)Index(layout);
        }
        internal void ResetPoiStream(bool keepFallback=false) {
            if(poiBuild!=null) { poiBuild.Cancel();poiBuild=null; }
            System.Threading.Interlocked.Increment(ref poiGeneration);poiBatchRunning=false;
            string currentFilter=State!=null&&State.Store!=null?State.Store.GetFilterHashKey():"";
            if(keepFallback&&streamedPoiTiles.Count>0&&requestedPoiSide>0) {
                var list=new List<StreamedPoiTile>(streamedPoiTiles.Count);
                foreach(var t in streamedPoiTiles.Values) if(t.Drawing!=null) list.Add(t);
                fallbackPoiTiles=list.Count>0?list.ToArray():null;
                fallbackPoiSide=requestedPoiSide;
                fallbackPoiFilter=currentFilter;
            } else if(!keepFallback) {
                fallbackPoiTiles=null;
                fallbackPoiSide=0;
                fallbackPoiFilter=null;
            }
            requestedPoiLayout=drawnPoiLayout=null;streamedPoiTiles.Clear();poiLruList.Clear();poiCachedPixels=0;poiSceneTiles=null;PoiViewportReady=false;PoiStreamResets++;
        }
        void StreamPois(DrawingContext dc,double side,double left,double top,double width,double height) {
            Dispatcher.VerifyAccess();PoiTilesDrawn=0;
            var layout=PoiClusters(State,width,height,false);
            if(layout.Length==0) { PoiViewportReady=true; return; }
            double dpi=PoiRasterDensity(width,height,CurrentDpiScaleX);
            double span=Math.Max(512,Math.Ceiling(Math.Max(width,height)/2048)*512);
            string activeFilter=State!=null&&State.Store!=null?State.Store.GetFilterHashKey():"";
            if(!object.ReferenceEquals(layout,requestedPoiLayout)||requestedPoiSide!=side||streamedPoiDpi!=dpi||streamedPoiSpan!=span||requestedPoiFilter!=activeFilter) {
                bool sameFilter=requestedPoiFilter!=null&&requestedPoiFilter==activeFilter;
                ResetPoiStream(sameFilter);requestedPoiLayout=layout;requestedPoiSide=drawnPoiSide=side;streamedPoiDpi=dpi;streamedPoiSpan=span;requestedPoiFilter=activeFilter;
            }
            // Compute visible tile coordinates directly in O(tiles) instead of scanning all POIs every frame.
            int tx1=Math.Max(0,(int)Math.Floor((-left-24)/span));
            int ty1=Math.Max(0,(int)Math.Floor((-top-24)/span));
            int tx2=Math.Min((int)Math.Floor(side/span),(int)Math.Floor((-left+width+24)/span));
            int ty2=Math.Min((int)Math.Floor(side/span),(int)Math.Floor((-top+height+24)/span));
            if(tx2<tx1||ty2<ty1) { PoiViewportReady=true; return; }

            var missing=missingTileBuffer; missing.Clear();
            int totalWanted=0,cachedHit=0;
            for(int ty=ty1;ty<=ty2;ty++) {
                for(int tx=tx1;tx<=tx2;tx++) {
                    totalWanted++;
                    var key=new Point(tx,ty);
                    StreamedPoiTile tile;
                    if(streamedPoiTiles.TryGetValue(key,out tile)) {
                        TouchPoiTileLru(tile);
                        cachedHit++;
                    } else {
                        missing.Add(key);
                    }
                }
            }

            // Keep previous zoom/pan tiles visible while new tiles stream in so markers never vanish or flicker.
            bool drawFallback=fallbackPoiTiles!=null&&fallbackPoiSide>0&&fallbackPoiFilter==activeFilter&&cachedHit<totalWanted;
            if(drawFallback) {
                double ratio=side/fallbackPoiSide;
                var fbView=new Rect((-left-48)/ratio,(-top-48)/ratio,(width+96)/ratio,(height+96)/ratio);
                dc.PushTransform(FrozenMatrix(ratio,0,0,ratio,left,top));
                for(int fi=0;fi<fallbackPoiTiles.Length;fi++) {
                    var ft=fallbackPoiTiles[fi];
                    if(ft.Bounds.IntersectsWith(fbView)) dc.DrawDrawing(ft.Drawing);
                }
                dc.Pop();
            }

            if(cachedHit>0) {
                dc.PushTransform(FrozenTranslate(left,top));
                for(int ty=ty1;ty<=ty2;ty++) {
                    for(int tx=tx1;tx<=tx2;tx++) {
                        StreamedPoiTile tile;
                        if(streamedPoiTiles.TryGetValue(new Point(tx,ty),out tile)&&tile.Drawing!=null) {
                            dc.DrawDrawing(tile.Drawing);
                            PoiTilesDrawn++;
                        }
                    }
                }
                dc.Pop();
            }

            PoiViewportReady=missing.Count==0;
            if(PoiViewportReady) { drawnPoiLayout=layout;fallbackPoiTiles=null;fallbackPoiSide=0; }

            if(!poiBatchRunning&&missing.Count>0) {
                // Sort missing tiles by distance to viewport center
                double cx=width*0.5,cy=height*0.5;
                missing.Sort((a,b)=> {
                    double da=Math.Abs((a.X+0.5)*span+left-cx)+Math.Abs((a.Y+0.5)*span+top-cy);
                    double db=Math.Abs((b.X+0.5)*span+left-cx)+Math.Abs((b.Y+0.5)*span+top-cy);
                    return da.CompareTo(db);
                });
                var idx=Index(layout);
                var batch=new List<Tuple<StreamedPoiTile,PoiStamp[]>>(8);
                int limit=Math.Min(8,missing.Count);
                for(int i=0;i<limit;i++) {
                    var key=missing[i];
                    var tile=new StreamedPoiTile { Key=key,Bounds=new Rect(key.X*span-24,key.Y*span-24,span+48,span+48) };
                    var cellBounds=new Rect(key.X*span/side,key.Y*span/side,span/side,span/side);cellBounds.Inflate(1e-7,1e-7);
                    var clusters=idx.Query(cellBounds);
                    var stamps=new List<PoiStamp>(clusters.Count);
                    for(int ci=0;ci<clusters.Count;ci++) {
                        var cluster=clusters[ci];
                        if(Math.Floor(cluster.Point.X*side/span)!=key.X||Math.Floor(cluster.Point.Y*side/span)!=key.Y)continue;
                        ScumMapCategory category;State.Store.CategoryById.TryGetValue(cluster.Members[0].CategoryId,out category);EnsurePoiAssets(category,cluster);
                        stamps.Add(new PoiStamp { At=new Point(cluster.Point.X*side,cluster.Point.Y*side),Radius=cluster.Radius,Scale=(cluster.Members.Count>1?11:cluster.Radius*1.45)/12,Icon=cluster.Icon,Outline=cluster.Outline });
                    }
                    if(stamps.Count==0) {
                        // Cache empty tile immediately so we never re-query empty ocean/terrain cells.
                        streamedPoiTiles[key]=tile;
                        TouchPoiTileLru(tile);
                    } else {
                        batch.Add(Tuple.Create(tile,stamps.ToArray()));
                    }
                }
                if(batch.Count>0) QueuePoiBatch(batch,layout,side,dpi);
            }
        }
        void QueuePoiBatch(List<Tuple<StreamedPoiTile,PoiStamp[]>> batch,WpfPoiCluster[] layout,double side,double dpi) {
            poiBatchRunning=true;var cancel=new System.Threading.CancellationTokenSource();poiBuild=cancel;long generation=poiGeneration;var dispatcher=Dispatcher;
            System.Threading.Tasks.Task.Run(async()=> {
                bool entered=false;
                try {
                    await poiBuildSlots.WaitAsync(cancel.Token);entered=true;
                    foreach(var item in batch) {
                        cancel.Token.ThrowIfCancellationRequested();var tile=item.Item1;var drawing=RecordPois(item.Item2,cancel.Token);
                        if(side*dpi<=4096) {
                            int pixels=(int)Math.Ceiling(tile.Bounds.Width*dpi);var visual=new DrawingVisual();
                            double scale=pixels/tile.Bounds.Width;
                            using(var context=visual.RenderOpen()) { context.PushTransform(FrozenMatrix(scale,0,0,scale,-tile.Bounds.X*scale,-tile.Bounds.Y*scale));context.DrawDrawing(drawing);context.Pop(); }
                            var image=new RenderTargetBitmap(pixels,pixels,96,96,PixelFormats.Pbgra32);image.Render(visual);image.Freeze();
                            drawing=new DrawingGroup();using(var context=drawing.Open())context.DrawImage(image,tile.Bounds);drawing.Freeze();tile.Pixels=pixels*pixels;
                        }
                        tile.Drawing=drawing;
                    }
                    cancel.Token.ThrowIfCancellationRequested();
                    PostPoiScene(dispatcher,()=> {
                        if(generation!=poiGeneration)return;
                        foreach(var item in batch) {
                            var tile=item.Item1;
                            StreamedPoiTile existing;
                            if(streamedPoiTiles.TryGetValue(tile.Key,out existing)) {
                                poiCachedPixels-=existing.Pixels;
                                if(existing.LruNode!=null) { poiLruList.Remove(existing.LruNode); existing.LruNode=null; }
                            }
                            streamedPoiTiles[tile.Key]=tile;
                            TouchPoiTileLru(tile);
                            poiCachedPixels+=tile.Pixels;
                            PoiTilesBuilt++;
                        }
                        while((streamedPoiTiles.Count>32||poiCachedPixels>12*1024*1024)&&poiLruList.Last!=null) {
                            var oldestNode=poiLruList.Last;
                            var oldest=oldestNode.Value;
                            poiLruList.RemoveLast();
                            oldest.LruNode=null;
                            streamedPoiTiles.Remove(oldest.Key);
                            poiCachedPixels-=oldest.Pixels;
                        }
                        var sceneList=new List<PoiSceneTile>(streamedPoiTiles.Count);
                        foreach(var t in streamedPoiTiles.Values) if(t.Drawing!=null) sceneList.Add(new PoiSceneTile { Bounds=t.Bounds,Drawing=t.Drawing });
                        poiSceneTiles=sceneList.ToArray();
                        PoiSceneRasterized=side*dpi<=4096;drawnPoiLayout=layout;drawnPoiSide=side;PoiSceneBuilds++;poiBatchRunning=false;poiBuild=null;RequestRedraw();
                    });
                }catch(OperationCanceledException) { PostPoiScene(dispatcher,()=> { if(generation==poiGeneration) { poiBatchRunning=false;poiBuild=null; } }); }
                catch(Exception error) { Program.LogException("PoiStream",error);PostPoiScene(dispatcher,()=> { if(generation==poiGeneration) { poiBatchRunning=false;poiBuild=null; } }); }
                finally { if(entered)poiBuildSlots.Release();PostPoiScene(dispatcher,()=> { if(object.ReferenceEquals(poiBuild,cancel))poiBuild=null;cancel.Dispose(); }); }
            });
        }
        internal static double PoiRasterDensity(double width,double height,double dpi) { return Math.Max(1.0,Math.Min(dpi,1.5)); }
        sealed class RouteChunk {
            internal int Start,End;internal Rect Bounds;internal StreamGeometry WorldGeometry;
        }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Drawing.PointF[],RouteChunk[]> routeChunks=new System.Runtime.CompilerServices.ConditionalWeakTable<System.Drawing.PointF[],RouteChunk[]>();
        static RouteChunk[] IndexRoute(System.Drawing.PointF[] points) {
            var chunks=new List<RouteChunk>();
            for(int start=0;start<points.Length-1;start+=128) {
                int end=Math.Min(points.Length-1,start+128);double x1=points[start].X,x2=x1,y1=points[start].Y,y2=y1;
                var geo=new StreamGeometry();
                using(var ctx=geo.Open()) {
                    ctx.BeginFigure(new Point(points[start].X,points[start].Y),false,false);
                    for(int i=start+1;i<=end;i++) {
                        double px=points[i].X,py=points[i].Y;
                        x1=Math.Min(x1,px);x2=Math.Max(x2,px);y1=Math.Min(y1,py);y2=Math.Max(y2,py);
                        ctx.LineTo(new Point(px,py),true,false);
                    }
                }
                geo.Freeze();
                chunks.Add(new RouteChunk { Start=start,End=end,Bounds=new Rect(x1,y1,x2-x1,y2-y1),WorldGeometry=geo });
            }
            return chunks.ToArray();
        }
        internal int RouteVerticesDrawn;
        void DrawStreamedRoute(DrawingContext dc,System.Drawing.PointF[] points,Brush ink,bool verified) {
            if(points==null||points.Length<2)return;
            double side=MapSide(State,ActualWidth,ActualHeight,Compact);
            if(side<=0)return;
            var centre=CameraCentre(State,ActualWidth,ActualHeight,Compact);
            double left=ActualWidth/2-side*centre.X,top=ActualHeight/2-side*centre.Y;
            var viewport=ViewBounds(State,ActualWidth,ActualHeight,Compact,8);
            var chunks=routeChunks.GetValue(points,IndexRoute);
            bool pushed=false;
            Pen worldPen=null;
            for(int i=0;i<chunks.Length;i++) {
                var chunk=chunks[i];
                if(!chunk.Bounds.IntersectsWith(viewport))continue;
                if(!pushed) {
                    worldPen=CachedPen(ink,RouteThickness/side,true,false,!verified);
                    if(cachedTrailXform==null||cachedTrailSide!=side||cachedTrailLeft!=left||cachedTrailTop!=top) {
                        cachedTrailSide=side;cachedTrailLeft=left;cachedTrailTop=top;
                        cachedTrailXform=new MatrixTransform(side,0,0,side,left,top);cachedTrailXform.Freeze();
                    }
                    dc.PushTransform(cachedTrailXform);
                    pushed=true;
                }
                RouteVerticesDrawn+=chunk.End-chunk.Start+1;
                dc.DrawGeometry(null,worldPen,chunk.WorldGeometry);
            }
            if(pushed)dc.Pop();
        }
        static ulong MixSharedHash(ulong h, ulong v) { unchecked { h ^= v; return h * 1099511628211UL; } }
        static ulong MixSharedStr(ulong h, string s) {
            if(s==null) return MixSharedHash(h, 0x9E3779B97F4A7C15UL);
            unchecked { h=MixSharedHash(h,(ulong)(uint)s.Length); for(int i=0;i<s.Length;i++) { h^=s[i]; h*=1099511628211UL; } return h; }
        }
        void UpdateSharedLayer(string camera) {
            unchecked {
                ulong h=14695981039346656037UL;
                h=MixSharedStr(h,camera);h=MixSharedStr(h,Localization.CurrentCode);h=MixSharedStr(h,State.LocalPeerId);
                h=MixSharedHash(h,(ulong)(uint)State.RouteColor);h=MixSharedStr(h,State.ActiveSquadRoute);
                h=MixSharedHash(h,(ulong)(uint)State.ZoneRevision);h=MixSharedHash(h,(ulong)(uint)Identity(State.Saved));
                h=MixSharedHash(h,AviationModeEnabled?1UL:0UL);h=MixSharedHash(h,(ulong)(uint)ActiveRouteStopIndex);
                if(State.SharedMarkers!=null) {
                    h=MixSharedHash(h,(ulong)(uint)State.SharedMarkers.Length);
                    bool hasActivePing=false;
                    for(int i=0;i<State.SharedMarkers.Length;i++) {
                        var m=State.SharedMarkers[i];
                        if(SquadState.IsPing(m)) hasActivePing=true;
                        h=MixSharedStr(h,m.OwnerId);h=MixSharedStr(h,m.Id);h=MixSharedStr(h,m.Name);
                        h=MixSharedHash(h,m.Death?1UL:0UL);
                        h=MixSharedHash(h,(ulong)BitConverter.DoubleToInt64Bits(m.X));
                        h=MixSharedHash(h,(ulong)BitConverter.DoubleToInt64Bits(m.Y));
                    }
                    if(hasActivePing && !Compact) h=MixSharedHash(h,(ulong)(DateTime.UtcNow.Ticks/TimeSpan.TicksPerSecond));
                }
                h=MixSharedHash(h,(ulong)(uint)Identity(State.OwnRoutePlan));
                h=MixSharedHash(h,(ulong)(uint)Identity(State.SquadPlan));
                h=MixSharedHash(h,(ulong)(uint)Identity(State.Route));
                h=MixSharedHash(h,(ulong)(uint)Identity(State.Target));
                h=MixSharedHash(h,State.Position!=null?1UL:0UL);
                if(State.OtherRoutePlans!=null) {
                    for(int i=0;i<State.OtherRoutePlans.Length;i++) {
                        h=MixSharedStr(h,State.OtherRoutePlans[i].Key);
                        h=MixSharedHash(h,(ulong)(uint)Identity(State.OtherRoutePlans[i].Plan));
                    }
                }
                if(State.LocalRoute!=null) {
                    for(int i=0;i<State.LocalRoute.Length;i++) h=MixSharedStr(h,SquadState.RouteKey(State.LocalRoute[i]));
                }
                if(State.Saved!=null) {
                    for(int di=0;di<State.Saved.Length;di++) {
                        var death=State.Saved[di];
                        if(death!=null&&death.IsDeathMarker) h=MixSharedStr(h,death.DisplayName);
                    }
                }
                if(sharedLayerHashValid&&sharedLayerHash==h)return;
                using(var context=sharedLayer.RenderOpen())DrawMapPass(context,MapPass.Shared);
                sharedLayerHash=h;sharedLayerHashValid=true;SharedLayerBuilds++;
            }
        }
    }
}
