using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DrawingPoint=System.Drawing.PointF;

namespace ZiloreMiniMap {
    internal sealed partial class WpfMapSurface:FrameworkElement {
        readonly System.Diagnostics.Stopwatch profClock=System.Diagnostics.Stopwatch.StartNew();
        readonly double[] profFrameRing=new double[64];
        int profRingIdx;
        internal static readonly Brush MapMarkerBg=WpfFieldTheme.ColorBrush("#E60B1017");
        static readonly object profLogGate=new object();
        long profLastTick,profLastExportMs,profLastFmtMs=-1000;double profAvgMs=16.6;int profLastGc0;
        string profCachedTxt="";double profCachedDpi;FormattedText profCachedFt;
        void DrawDebugProfiler(DrawingContext dc,double w,double h) {
            if(!DebugProfilerEnabled)return;
            long now=profClock.ElapsedMilliseconds;
            if(profLastTick>0) {
                double dt=Math.Max(1.0,Math.Min(250.0,now-profLastTick));
                profAvgMs=profAvgMs*0.85+dt*0.15;
                profFrameRing[profRingIdx]=dt;
                profRingIdx=(profRingIdx+1)&63;
            }
            profLastTick=now;
            double dpi=CurrentDpiScaleX;
            if(profCachedFt==null||now-profLastFmtMs>=250||Math.Abs(profCachedDpi-dpi)>0.001) {
                profLastFmtMs=now;profCachedDpi=dpi;
                double p99=profAvgMs;
                for(int i=0;i<64;i++) if(profFrameRing[i]>p99) p99=profFrameRing[i];
                int fps=(int)Math.Round(1000.0/Math.Max(1.0,profAvgMs));
                int gc0=GC.CollectionCount(0), gc1=GC.CollectionCount(1);
                int gcDelta=gc0-profLastGc0;profLastGc0=gc0;
                profCachedTxt="FPS "+fps+" · "+profAvgMs.ToString("0.0",CultureInfo.InvariantCulture)+"/"+p99.ToString("0.0",CultureInfo.InvariantCulture)+"ms · GC:"+gc0+"/"+gc1+(gcDelta>0?" (+"+gcDelta+")":"");
                profCachedFt=new FormattedText(profCachedTxt,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,labelTypeface,11.5,WpfFieldTheme.Cyan,dpi);
            }
            var pt=Compact?new Point(10,h-24):new Point(16,h-34);
            dc.DrawRoundedRectangle(MapMarkerBg,null,new Rect(pt.X-4,pt.Y-1,profCachedFt.Width+8,profCachedFt.Height+2),4,4);
            dc.DrawText(profCachedFt,pt);
            if(now-profLastExportMs>=30000) {
                profLastExportMs=now;
                string snap=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture)+" | "+(Compact?"HUD":"MAP")+" | "+profCachedTxt+" | RAM:"+((GC.GetTotalMemory(false))/(1024*1024))+"MB";
                System.Threading.Tasks.Task.Run(()=> {
                    try {
                        lock(profLogGate) {
                            string logDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ZiloreMiniMap","logs");
                            Directory.CreateDirectory(logDir);
                            string logFile=Path.Combine(logDir,"perf-telemetry.log");
                            if(File.Exists(logFile)&&new FileInfo(logFile).Length>512*1024) File.Delete(logFile);
                            File.AppendAllText(logFile,snap+Environment.NewLine);
                        }
                    } catch { }
                });
            }
        }
        static readonly double[] ScaleBarSteps=new double[]{25,50,100,200,250,500,1000,2000,5000};
        void DrawScaleBar(DrawingContext dc,double side,double w,double h) {
            if(side<=0)return;
            double metresPerPx=ScumMapWidthMetres/side;
            double targetPx=Compact?56.0:110.0;
            double targetM=targetPx*metresPerPx;
            double chosenM=ScaleBarSteps[ScaleBarSteps.Length-1];
            for(int i=0;i<ScaleBarSteps.Length;i++) {
                if(ScaleBarSteps[i]>=targetM*0.65) { chosenM=ScaleBarSteps[i];break; }
            }
            double barPx=chosenM/metresPerPx;
            if(barPx<24||barPx>w*0.42)return;
            double rightInset=Compact?0.0:RightOverlayInset;
            double x=Compact?(w-barPx)/2.0:Math.Max(16.0,w-rightInset-barPx-24.0);
            double y=Compact?(State!=null&&State.Compass?h-29.0:h-12.0):h-22.0;
            var sbInk=CachedBrush(255,241,245,249);
            var pen=CachedPen(sbInk,1.6);
            dc.DrawLine(pen,new Point(x,y),new Point(x+barPx,y));
            dc.DrawLine(pen,new Point(x,y-4),new Point(x,y+2));
            dc.DrawLine(pen,new Point(x+barPx,y-4),new Point(x+barPx,y+2));
            string lbl=chosenM>=1000?(chosenM/1000.0).ToString("0.#",CultureInfo.InvariantCulture)+" km":((int)chosenM).ToString(CultureInfo.InvariantCulture)+" m";
            var ft=LabelText(lbl,Compact?9.5:11.0,sbInk,CurrentDpiScaleX);
            dc.DrawRoundedRectangle(CachedBrush(210,11,14,18),null,new Rect(x+(barPx-ft.Width)/2.0-3,y-ft.Height-4,ft.Width+6,ft.Height+2),3,3);
            dc.DrawText(ft,new Point(x+(barPx-ft.Width)/2.0,y-ft.Height-3));
        }
        string lastRulerTxt; double lastRulerDpi; FormattedText lastRulerFt;
        void DrawRulerOverlay(DrawingContext dc,double left,double top,double side) {
            if(Compact||measurePoints.Count==0)return;
            bool hasLive=!measureClosed&&cursorMapPoint.HasValue;
            int totalPts=measurePoints.Count+(hasLive?1:0);
            if(totalPts<2) {
                var p0=measurePoints[0];
                dc.DrawEllipse(MapMarkerBg,CachedPen(WpfFieldTheme.Amber,2),new Point(left+p0.X*side,top+p0.Y*side),5,5);
                return;
            }
            var pen=CachedPen(WpfFieldTheme.Amber,2.2,true,false,true);
            var nodePen=CachedPen(WpfFieldTheme.Amber,2);
            double totalM=0;
            for(int i=0;i<totalPts-1;i++) {
                DrawingPoint a=measurePoints[i];
                DrawingPoint b=(i+1<measurePoints.Count)?measurePoints[i+1]:cursorMapPoint.Value;
                var sa=new Point(left+a.X*side,top+a.Y*side); var sb=new Point(left+b.X*side,top+b.Y*side);
                dc.DrawLine(pen,sa,sb);
                dc.DrawEllipse(MapMarkerBg,nodePen,sa,4.5,4.5);
                totalM+=DestinationSearch.Metres(a,b);
            }
            DrawingPoint prev=(totalPts-2<measurePoints.Count)?measurePoints[totalPts-2]:cursorMapPoint.Value;
            DrawingPoint last=hasLive?cursorMapPoint.Value:measurePoints[measurePoints.Count-1];
            var sLast=new Point(left+last.X*side,top+last.Y*side);
            dc.DrawEllipse(WpfFieldTheme.Amber,null,sLast,4.5,4.5);
            double bearing=(Math.Atan2(last.X-prev.X,-(last.Y-prev.Y))*180.0/Math.PI+360.0)%360.0;
            string info="📏 "+DestinationSearch.Distance(totalM)+" · "+((int)Math.Round(bearing))+"°"+(totalPts>2?" ("+totalPts+")":"");
            double dpi=CurrentDpiScaleX;
            if(lastRulerFt==null||lastRulerTxt!=info||Math.Abs(lastRulerDpi-dpi)>0.001) {
                lastRulerTxt=info; lastRulerDpi=dpi;
                lastRulerFt=new FormattedText(info,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,labelTypeface,12.5,WpfFieldTheme.Amber,dpi);
            }
            var at=new Point(sLast.X+10,sLast.Y-20);
            dc.DrawRoundedRectangle(MapMarkerBg,null,new Rect(at.X-4,at.Y-1,lastRulerFt.Width+8,lastRulerFt.Height+2),4,4);
            dc.DrawText(lastRulerFt,at);
        }
        void DrawPlayerPulseAndVector(DrawingContext dc,Point p,double yaw,double side) {
            long elapsed=pulseClock.ElapsedMilliseconds-playerPulseStartMs;
            if(elapsed>=0&&elapsed<1200) {
                double t=elapsed/1200.0;
                double radius=10.0+t*(Compact?38.0:58.0);
                double opacity=Math.Max(0.05,1.0-t);
                dc.PushOpacity(opacity);
                dc.DrawEllipse(null,CachedPen(WpfFieldTheme.Cyan,2.4),p,radius,radius);
                dc.Pop();
            }
            double spd=CurrentGroundSpeedMps;
            if(!AviationModeEnabled&&spd>=7.0&&side>0) {
                // 6-second velocity projection vector (e.g. driving / boat)
                double projM=Math.Min(300.0,spd*6.0);
                double px=projM*(side/ScumMapWidthMetres);
                if(px>=14.0) {
                    double rad=(yaw-90.0)*Math.PI/180.0;
                    var end=new Point(p.X+Math.Cos(rad)*px,p.Y+Math.Sin(rad)*px);
                    dc.PushOpacity(0.65);
                    dc.DrawLine(CachedPen(WpfFieldTheme.Cyan,1.6,true,false,true),p,end);
                    dc.Pop();
                }
            }
        }
        static double PointToSegmentDistSq(DrawingPoint p,DrawingPoint a,DrawingPoint b) {
            double dx=b.X-a.X, dy=b.Y-a.Y, l2=dx*dx+dy*dy;
            double t=l2>1e-12?Math.Max(0.0,Math.Min(1.0,((p.X-a.X)*dx+(p.Y-a.Y)*dy)/l2)):0.0;
            double px=a.X+t*dx-p.X, py=a.Y+t*dy-p.Y;
            return px*px+py*py;
        }
        sealed class DangerZoneAabb {
            internal MapZone Zone;
            internal DrawingPoint[] Points;
            internal double MinX,MaxX,MinY,MaxY;
        }
        static readonly object dangerZoneCacheGate=new object();
        static MapZone[] cachedDangerSource;
        static int cachedDangerRev=-1;
        static DangerZoneAabb[] cachedDangerZones=Array.Empty<DangerZoneAabb>();
        static DangerZoneAabb[] GetDangerZones(MapZone[] saved,int rev) {
            lock(dangerZoneCacheGate) {
                if(object.ReferenceEquals(cachedDangerSource,saved)&&cachedDangerRev==rev) return cachedDangerZones;
                var list=new List<DangerZoneAabb>(8);
                for(int i=0;i<saved.Length;i++) {
                    var z=saved[i];
                    if(z==null||z.Points==null||z.Points.Length<3||z.IsDeathMarker)continue;
                    int r=(z.Argb>>16)&0xFF,g=(z.Argb>>8)&0xFF,b=z.Argb&0xFF;
                    if(!(r>=170&&g<=110&&b<=110))continue;
                    var pts=z.Points;
                    double minX=pts[0].X,maxX=minX,minY=pts[0].Y,maxY=minY;
                    for(int p=1;p<pts.Length;p++) {
                        if(pts[p].X<minX)minX=pts[p].X; else if(pts[p].X>maxX)maxX=pts[p].X;
                        if(pts[p].Y<minY)minY=pts[p].Y; else if(pts[p].Y>maxY)maxY=pts[p].Y;
                    }
                    list.Add(new DangerZoneAabb { Zone=z,Points=pts,MinX=minX,MaxX=maxX,MinY=minY,MaxY=maxY });
                }
                cachedDangerSource=saved; cachedDangerRev=rev;
                cachedDangerZones=list.Count==0?Array.Empty<DangerZoneAabb>():list.ToArray();
                return cachedDangerZones;
            }
        }
        internal static bool IsNearDangerZone(WpfMapState state,double radiusMetres=150.0) {
            if(state==null||state.Position==null||state.Saved==null||state.Saved.Length==0)return false;
            var dangerZones=GetDangerZones(state.Saved,state.ZoneRevision);
            if(dangerZones.Length==0)return false;
            var pos=MapWindow.ToMap(state.Position);
            double radNorm=radiusMetres/ScumMapWidthMetres;
            double radNormSq=radNorm*radNorm;
            for(int i=0;i<dangerZones.Length;i++) {
                var dz=dangerZones[i];
                if(pos.X<dz.MinX-radNorm||pos.X>dz.MaxX+radNorm||pos.Y<dz.MinY-radNorm||pos.Y>dz.MaxY+radNorm)continue;
                if(dz.Zone.Contains(pos))return true;
                var pts=dz.Points;
                for(int p=0;p<pts.Length;p++) {
                    if(PointToSegmentDistSq(pos,pts[p],pts[(p+1)%pts.Length])<=radNormSq)return true;
                }
            }
            return false;
        }
        string lastCursorReadoutTxt; double lastCursorReadoutDpi; FormattedText lastCursorReadoutFt;
        void DrawCursorReadout(DrawingContext dc,double w,double h) {
            if(Compact||!cursorMapPoint.HasValue)return;
            var pt=cursorMapPoint.Value;
            double wx,wy;MapToWorld(pt,out wx,out wy);
            string txt=DestinationSearch.Sector(pt)+" · X:"+((int)Math.Round(wx))+" Y:"+((int)Math.Round(wy));
            if(State!=null&&State.Position!=null) {
                double d=DestinationSearch.Metres(MapWindow.ToMap(State.Position),pt);
                txt+=" · "+DestinationSearch.Distance(d);
            }
            double dpi=CurrentDpiScaleX;
            if(lastCursorReadoutFt==null||lastCursorReadoutTxt!=txt||Math.Abs(lastCursorReadoutDpi-dpi)>0.001) {
                lastCursorReadoutTxt=txt; lastCursorReadoutDpi=dpi;
                lastCursorReadoutFt=new FormattedText(txt,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,labelTypeface,11.5,CachedBrush(255,226,232,240),dpi);
            }
            var ft=lastCursorReadoutFt;
            double x=Math.Max(16,w-RightOverlayInset-ft.Width-24);
            double y=h-62;
            dc.DrawRoundedRectangle(CachedBrush(210,11,16,23),CachedPen(CachedBrush(95,56,189,248),0.8),new Rect(x-6,y-2,ft.Width+12,ft.Height+4),6,6);
            dc.DrawText(ft,new Point(x,y));
        }
        static readonly StreamGeometry AircraftGeometry=CreateAircraftGeometry();
        static StreamGeometry CreateAircraftGeometry() {
            var g=new StreamGeometry();
            using(var ctx=g.Open()) {
                ctx.BeginFigure(new Point(0,-10),true,true);
                ctx.LineTo(new Point(1.8,-3),true,false);
                ctx.LineTo(new Point(9.5,1),true,false);
                ctx.LineTo(new Point(9.5,3.2),true,false);
                ctx.LineTo(new Point(1.8,1.8),true,false);
                ctx.LineTo(new Point(1.4,6.5),true,false);
                ctx.LineTo(new Point(4.2,8.5),true,false);
                ctx.LineTo(new Point(4.2,10),true,false);
                ctx.LineTo(new Point(0,8.8),true,false);
                ctx.LineTo(new Point(-4.2,10),true,false);
                ctx.LineTo(new Point(-4.2,8.5),true,false);
                ctx.LineTo(new Point(-1.4,6.5),true,false);
                ctx.LineTo(new Point(-1.8,1.8),true,false);
                ctx.LineTo(new Point(-9.5,3.2),true,false);
                ctx.LineTo(new Point(-9.5,1),true,false);
                ctx.LineTo(new Point(-1.8,-3),true,false);
            }
            g.Freeze();
            return g;
        }
        static readonly Brush DangerBrush=WpfFieldTheme.ColorBrush("#FF654F");
        static readonly StreamGeometry SharedDiamondGeometry=CreateSharedDiamondGeometry();
        static StreamGeometry CreateSharedDiamondGeometry() {
            var g=new StreamGeometry();
            using(var ctx=g.Open()) {
                ctx.BeginFigure(new Point(0,-8),true,true);
                ctx.LineTo(new Point(7,0),true,false);
                ctx.LineTo(new Point(0,8),true,false);
                ctx.LineTo(new Point(-7,0),true,false);
            }
            g.Freeze();
            return g;
        }
        static readonly StreamGeometry EdgeArrowGeometry=CreateEdgeArrowGeometry();
        static StreamGeometry CreateEdgeArrowGeometry() {
            var g=new StreamGeometry();
            using(var ctx=g.Open()) {
                ctx.BeginFigure(new Point(0,0),true,true);
                ctx.LineTo(new Point(-13,6),true,false);
                ctx.LineTo(new Point(-13,-6),true,false);
            }
            g.Freeze();
            return g;
        }
        static readonly Dictionary<int,RadialGradientBrush> coneBrushCache=new Dictionary<int,RadialGradientBrush>();
        static readonly Dictionary<int,SolidColorBrush> solidBrushCache=new Dictionary<int,SolidColorBrush>();
        static readonly Dictionary<long,Pen> penCache=new Dictionary<long,Pen>();
        static readonly Dictionary<string,Brush> memberInkCache=new Dictionary<string,Brush>(StringComparer.Ordinal);
        internal static SolidColorBrush CachedBrush(int argb) {
            SolidColorBrush b;
            if(!solidBrushCache.TryGetValue(argb,out b)) {
                if(solidBrushCache.Count>=512) solidBrushCache.Clear();
                b=new SolidColorBrush(Color.FromArgb((byte)(argb>>24),(byte)(argb>>16),(byte)(argb>>8),(byte)argb));
                b.Freeze();
                solidBrushCache[argb]=b;
            }
            return b;
        }
        static SolidColorBrush CachedBrush(byte a,byte r,byte g,byte b) {
            return CachedBrush((a<<24)|(r<<16)|(g<<8)|b);
        }
        internal static int PenCacheCount { get { return penCache.Count; } }
        internal static Pen CachedPen(Brush brush,double thickness,bool roundCaps=false,bool dotted=false,bool dashed=false) {
            var solid=brush as SolidColorBrush;
            if(solid==null) return new Pen(brush,thickness);
            int argb=(solid.Color.A<<24)|(solid.Color.R<<16)|(solid.Color.G<<8)|solid.Color.B;
            double safeT=Math.Max(0.00001,thickness);
            // Quantize tiny world-scaled thicknesses logarithmically (~4% step) so smooth auto-zoom never thrashes penCache.
            uint tKey;
            double penThick;
            if(safeT<0.25) {
                int bucket=(int)Math.Round(Math.Log(safeT,2.0)*16.0);
                tKey=(uint)(0x8000 | ((bucket+512)&0x7FFF));
                penThick=Math.Pow(2.0,bucket/16.0);
            } else {
                int q=(int)Math.Max(1,Math.Min(32767,Math.Round(safeT*20.0)));
                tKey=(uint)q;
                penThick=q/20.0;
            }
            long key=((long)argb<<19)|((long)tKey<<3)|(roundCaps?1L:0L)|(dotted?2L:0L)|(dashed?4L:0L);
            Pen p;
            if(!penCache.TryGetValue(key,out p)) {
                if(penCache.Count>=512) penCache.Clear();
                p=new Pen(CachedBrush(argb),penThick);
                if(roundCaps) { p.StartLineCap=PenLineCap.Round; p.EndLineCap=PenLineCap.Round; p.LineJoin=PenLineJoin.Round; }
                if(dotted) p.DashStyle=DashStyles.Dot;
                else if(dashed) p.DashStyle=DashStyles.Dash;
                p.Freeze();
                penCache[key]=p;
            }
            return p;
        }
        static RadialGradientBrush CachedConeBrush(Color c) {
            int rgb=(c.R<<16)|(c.G<<8)|c.B;
            RadialGradientBrush coneBrush;
            if(!coneBrushCache.TryGetValue(rgb,out coneBrush)) {
                if(coneBrushCache.Count>=64) coneBrushCache.Clear();
                coneBrush=new RadialGradientBrush(Color.FromArgb(135,c.R,c.G,c.B),Color.FromArgb(0,c.R,c.G,c.B)) {
                    MappingMode=BrushMappingMode.Absolute,
                    Center=new Point(0,0),
                    GradientOrigin=new Point(0,0),
                    RadiusX=58,
                    RadiusY=58
                };
                coneBrush.Freeze();
                coneBrushCache[rgb]=coneBrush;
            }
            return coneBrush;
        }
        internal static void DrawPlayer(DrawingContext dc,Point p,double yaw,Brush ink,bool heading,bool drawCone=false) {
            var m=Matrix.Identity;
            m.Rotate(yaw-90);
            m.Translate(p.X,p.Y);
            dc.PushTransform(FrozenMatrix(m));
            if(heading && drawCone) {
                var solid=ink as SolidColorBrush;
                Color c=solid!=null?solid.Color:Colors.Orange;
                dc.DrawGeometry(CachedConeBrush(c),null,VisionConeGeometry);
            }
            if(heading && AviationModeEnabled && drawCone) {
                dc.DrawLine(CachedPen(AviationVectorBrush,1.6,true,false,true),new Point(0,-12),new Point(0,-220));
            }
            var outline=CachedPen(Brushes.White,1.5);
            if(heading) dc.DrawGeometry(ink,outline,AviationModeEnabled&&drawCone?AircraftGeometry:PlayerArrowGeometry);
            else dc.DrawEllipse(ink,outline,new Point(0,0),5,5);
            dc.Pop();
        }
        void DrawDestinationEdge(DrawingContext dc,double width,double height,double left,double top,double side,double rotation,Brush ink) {
            if(State.Target==null||State.Position==null)return;
            var c=State.Target.Centroid;
            DrawEdgeArrow(dc,width,height,RotatePoint(new Point(left+c.X*side,top+c.Y*side),rotation,width,height),ink,null);
        }
        void DrawPeerEdges(DrawingContext dc,double width,double height,double left,double top,double side,double rotation) {
            if(!Compact)return;
            DrawingPoint? selfPt=State.Position==null?(DrawingPoint?)null:MapWindow.ToMap(State.Position);
            if(State.Peers!=null)for(int pi=0;pi<State.Peers.Length;pi++) { var peer=State.Peers[pi]; if(peer.Position==null)continue;
                var peerPt=MapWindow.ToMap(peer.Position);
                string lbl=selfPt.HasValue?(peer.Name+" · "+DestinationSearch.Distance(DestinationSearch.Metres(selfPt.Value,peerPt))):peer.Name;
                DrawEdgeArrow(dc,width,height,RotatePoint(new Point(left+peerPt.X*side,top+peerPt.Y*side),rotation,width,height),MemberInk(peer.Id),lbl,peer.Id);
            }
            if(State.SharedMarkers!=null)for(int si=0;si<State.SharedMarkers.Length;si++) { var marker=State.SharedMarkers[si];if(marker.Id==SharedMapMarker.GpsId)DrawEdgeArrow(dc,width,height,RotatePoint(new Point(left+marker.X*side,top+marker.Y*side),rotation,width,height),OwnerInk(marker.OwnerId),null); }
        }
        void DrawEdgeArrow(DrawingContext dc,double width,double height,Point destination,Brush ink,string name,string edgeKey=null) {
            bool circle=Compact&&State.Shape=="Circle";
            double radius=Math.Min(width,height)/2;
            bool visible=circle?(destination-new Point(width/2,height/2)).Length<=radius:destination.X>=0&&destination.X<=width&&destination.Y>=0&&destination.Y<=height;
            if(visible)return;
            var bounds=circle?new System.Drawing.RectangleF((float)(width/2-radius),(float)(height/2-radius),(float)(radius*2),(float)(radius*2)):new System.Drawing.RectangleF(0,0,(float)width,(float)height);
            DrawingPoint tip,direction;
            if(!MapWindow.TryDestinationEdge(bounds,circle,new DrawingPoint((float)(width/2),(float)(height/2)),new DrawingPoint((float)destination.X,(float)destination.Y),out tip,out direction))return;
            double angle=Math.Atan2(direction.Y,direction.X)*180.0/Math.PI;
            var em=Matrix.Identity;
            em.Rotate(angle);
            em.Translate(tip.X,tip.Y);
            dc.PushTransform(FrozenMatrix(em));
            dc.DrawGeometry(ink,CachedPen(MapMarkerBg,2),EdgeArrowGeometry);
            dc.Pop();
            if(name!=null) {
                var text=DynamicPeerLabelText("edge:"+(edgeKey??name),name,12,ink,CurrentDpiScaleX);
                double x=direction.X>.3?tip.X-22-text.Width:direction.X<-.3?tip.X+22:tip.X-text.Width/2;
                double y=direction.Y>.3?tip.Y-22-text.Height:direction.Y<-.3?tip.Y+22:tip.Y-text.Height/2;
                var at=new Point(Math.Max(10,Math.Min(width-text.Width-10,x)),Math.Max(10,Math.Min(height-text.Height-10,y)));
                dc.DrawRoundedRectangle(MapMarkerBg,null,new Rect(at.X-4,at.Y-1,text.Width+8,text.Height+2),3,3);dc.DrawText(text,at);
            }
        }
        internal static WpfMapLabel[] SelectLabels(WpfMapState state,double width,double height,bool compact,double pixelsPerDip=1.0) {
            if(state==null||!state.Labels||width<=0||height<=0)return Array.Empty<WpfMapLabel>();
            double dpi=pixelsPerDip>0?pixelsPerDip:1.0;
            double side=MapSide(state,width,height,compact);DrawingPoint centre=CameraCentre(state,width,height,compact);
            double rot=Rotation(state,compact), halfW=width*0.5, halfH=height*0.5;
            var candidates=new List<WpfMapLabel>(32);
            if(state.Saved!=null) {
                for(int zi=0;zi<state.Saved.Length;zi++) {
                    var zone=state.Saved[zi];
                    if(zone==null||zone.IsDeathMarker)continue; // Age labels belong to the retained shared/death layer.
                    if(zone.Points==null||zone.Points.Length==0||string.IsNullOrWhiteSpace(zone.Name))continue;
                    if(MapWindow.WpfCustomZone(zone)?!state.Zones:zone.Points.Length==1&&!state.Waypoints)continue;
                    var zc=MapWindow.WpfZoneCentre(zone);
                    var at=RotatePoint(new Point(halfW+(zc.X-centre.X)*side,halfH+(zc.Y-centre.Y)*side),rot,width,height);
                    if(at.X>=0&&at.Y>=0&&at.X<=width&&at.Y<=height)
                        candidates.Add(new WpfMapLabel { Text=zone.DisplayName,At=at,Major=zone.Points.Length>1 });
                }
            }
            if(state.Store!=null&&state.Store.MasterEnabled)foreach(var cluster in VisiblePoiClusters(state,width,height,compact)) {
                ScumMapMarker marker=cluster.Members[0];
                bool isMajor=marker.CategoryId==27||marker.CategoryId==26||marker.CategoryId==80;
                if(!isMajor) {
                    for(int mi=1;mi<cluster.Members.Count;mi++) {
                        var cand=cluster.Members[mi];
                        if(cand.CategoryId==27||cand.CategoryId==26||cand.CategoryId==80) { marker=cand; isMajor=true; break; }
                    }
                }
                var at=RotatePoint(new Point(halfW+(marker.Point.X-centre.X)*side,halfH+(marker.Point.Y-centre.Y)*side),rot,width,height);
                if(at.X<0||at.Y<0||at.X>width||at.Y>height)continue;
                string name=PoiLabel(state,marker,compact);if(string.IsNullOrWhiteSpace(name))continue;
                candidates.Add(new WpfMapLabel { Text=name,At=at,Major=isMajor });
            }
            var visibleCandidates=candidates;
            if(visibleCandidates.Count>1) {
                visibleCandidates.Sort((a,b)=> {
                    int mc=b.Major.CompareTo(a.Major); if(mc!=0) return mc;
                    double da=(a.At.X-halfW)*(a.At.X-halfW)+(a.At.Y-halfH)*(a.At.Y-halfH);
                    double db=(b.At.X-halfW)*(b.At.X-halfW)+(b.At.Y-halfH)*(b.At.Y-halfH);
                    return da.CompareTo(db);
                });
            }
            var result=new List<WpfMapLabel>();var occupied=new List<Rect>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for(int vi=0;vi<visibleCandidates.Count;vi++) {
                var item=visibleCandidates[vi];
                if(!item.Major&&names.Contains(item.Text))continue;
                double size=compact?Math.Max(9.0,Math.Min(11.5,(state.LabelSize>0?state.LabelSize:12)-3)):(state.LabelSize>0?state.LabelSize:12);
                var text=LabelText(item.Text,item.Major?size+2:size,WpfFieldTheme.Ink,dpi);
                double labelWidth=Math.Min(220,text.Width)+8,labelHeight=text.Height+4;
                for(int oi=0;oi<4;oi++) {
                    double ox=(oi<2)?9:(-labelWidth-9); double oy=((oi&1)==0)?(-labelHeight):8;
                    var at=new Point(item.At.X+ox,item.At.Y+oy); var box=new Rect(at.X-4,at.Y-1,labelWidth,labelHeight);
                    if(box.Left<0||box.Top<0||box.Right>width||box.Bottom>height)continue;
                    bool overlap=false; for(int bi=0;bi<occupied.Count;bi++) { if(occupied[bi].IntersectsWith(box)) { overlap=true; break; } }
                    if(overlap)continue;
                    result.Add(new WpfMapLabel { Text=item.Text,At=at,Major=item.Major });occupied.Add(box);names.Add(item.Text);break;
                }
                if(result.Count>=Math.Max(2,Math.Min(compact?3:60,(int)(width*height/(compact?24000:14000)))))break;
            }
            return result.ToArray();
        }
        static void DrawLineCount(DrawingContext dc,Point[] list,int count,Pen pen) {
            if(count<2)return;
            if(!pen.IsFrozen)pen.StartLineCap=pen.EndLineCap=PenLineCap.Round;
            if(count==2) { dc.DrawLine(pen,list[0],list[1]);return; }
            var geometry=new StreamGeometry();
            using(var c=geometry.Open()) { c.BeginFigure(list[0],false,false);for(int i=1;i<count;i++)c.LineTo(list[i],true,false); }
            geometry.Freeze();dc.DrawGeometry(null,pen,geometry);
        }
        static void DrawLine(DrawingContext dc,IEnumerable<Point> points,Pen pen) { var list=points as IList<Point>??points.ToArray();int count=list.Count;if(count<2)return;if(!pen.IsFrozen)pen.StartLineCap=pen.EndLineCap=PenLineCap.Round;if(count==2) { dc.DrawLine(pen,list[0],list[1]);return; }var geometry=new StreamGeometry();using(var c=geometry.Open()) { c.BeginFigure(list[0],false,false);for(int i=1;i<count;i++)c.LineTo(list[i],true,false); }geometry.Freeze();dc.DrawGeometry(null,pen,geometry); }
        static string PoiLabel(WpfMapState state,ScumMapMarker marker,bool compact) {
            var cache=poiCaches.GetValue(state.Store,store=>new[]{new WpfPoiCache(),new WpfPoiCache()})[compact?0:1];
            if(cache.Labels==null||cache.LabelLanguage!=Localization.CurrentCode) { cache.Labels=new Dictionary<ScumMapMarker,string>();cache.LabelLanguage=Localization.CurrentCode; }
            string result;if(!cache.Labels.TryGetValue(marker,out result)) { result=Localization.GetMapObjectName(state.Store.GetMarkerDisplayName(marker));cache.Labels.Add(marker,result); }return result;
        }
        struct LabelKey:IEquatable<LabelKey> {
            internal string Text;internal double Size;internal Color Color;internal int DpiKey;
            public bool Equals(LabelKey other) { return DpiKey==other.DpiKey&&Size==other.Size&&Color==other.Color&&Text==other.Text; }
            public override bool Equals(object other) { return other is LabelKey&&Equals((LabelKey)other); }
            public override int GetHashCode() { unchecked { return ((((Text==null?0:Text.GetHashCode())*397^Size.GetHashCode())*397^Color.GetHashCode())*397)^DpiKey; } }
        }
        static Typeface labelTypeface=new Typeface(SystemTypography.WpfUiFontFamily,FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal);
        static readonly Typeface poiTypeface=new Typeface(SystemTypography.WpfUiFontFamily,FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal);
        internal static void SetLabelFontFamily(FontFamily family) {
            if(family==null) return;
            labelTypeface=new Typeface(family,FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal);
            ClearLabelCache();
        }
        sealed class DynamicLabelEntry { internal string Text; internal double Size; internal Color Color; internal int DpiKey; internal FormattedText Formatted; }
        static readonly Dictionary<string,DynamicLabelEntry> dynamicPeerLabels=new Dictionary<string,DynamicLabelEntry>(StringComparer.Ordinal);
        internal static FormattedText DynamicPeerLabelText(string peerKey,string text,double size,Brush ink,double pixelsPerDip=1.0) {
            text=text??"";
            double dpi=pixelsPerDip>0?pixelsPerDip:1.0;
            int dpiKey=(int)Math.Round(dpi*100.0);
            var solidInk=ink as SolidColorBrush;
            Color col=solidInk!=null?solidInk.Color:Colors.White;
            DynamicLabelEntry entry;
            if(dynamicPeerLabels.TryGetValue(peerKey,out entry)&&entry.Text==text&&entry.Size==size&&entry.Color==col&&entry.DpiKey==dpiKey&&entry.Formatted!=null) return entry.Formatted;
            var ft=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,labelTypeface,size,ink,dpi) { MaxTextWidth=220,MaxLineCount=1,Trimming=TextTrimming.CharacterEllipsis };
            if(dynamicPeerLabels.Count>=64&&!dynamicPeerLabels.ContainsKey(peerKey)) dynamicPeerLabels.Clear();
            dynamicPeerLabels[peerKey]=new DynamicLabelEntry { Text=text,Size=size,Color=col,DpiKey=dpiKey,Formatted=ft };
            return ft;
        }
        void DrawDynamicPeerLabel(DrawingContext dc,string peerKey,string text,Point at,Brush ink,double size) {
            var screen=RotatePoint(at,renderRotation,ActualWidth,ActualHeight);
            if(screen.X<-228||screen.X>ActualWidth+4||screen.Y<-size*3||screen.Y>ActualHeight+1)return;
            var f=DynamicPeerLabelText(peerKey,text,size,ink,CurrentDpiScaleX);
            var bounds=new Rect(screen.X-4,screen.Y-1,Math.Min(220,f.Width)+8,f.Height+2);
            if(!bounds.IntersectsWith(new Rect(0,0,ActualWidth,ActualHeight)))return;
            if(renderRotation!=0)dc.PushTransform(FrozenRotate(-renderRotation,at.X,at.Y));
            dc.DrawRoundedRectangle(CachedBrush(210,11,14,18),null,new Rect(at.X-4,at.Y-1,Math.Min(220,f.Width)+8,f.Height+2),3,3);
            dc.DrawText(f,at);
            if(renderRotation!=0)dc.Pop();
        }
        static readonly Dictionary<LabelKey,FormattedText> labelTexts=new Dictionary<LabelKey,FormattedText>();
        static readonly Queue<LabelKey> labelTextOrder=new Queue<LabelKey>();
        static string labelCulture;
        internal static FormattedText LabelText(string text,double size,Brush ink,double pixelsPerDip=1.0,bool cacheable=true) {
            text=text??"";
            double dpi=pixelsPerDip>0?pixelsPerDip:1.0;
            if(!cacheable) {
                return new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,labelTypeface,size,ink,dpi) { MaxTextWidth=220,MaxLineCount=1,Trimming=TextTrimming.CharacterEllipsis };
            }
            if(labelCulture!=CultureInfo.CurrentCulture.Name) { labelCulture=CultureInfo.CurrentCulture.Name;labelTexts.Clear();labelTextOrder.Clear(); }
            var solidInk=ink as SolidColorBrush;
            var key=new LabelKey { Text=text,Size=size,Color=solidInk!=null?solidInk.Color:Colors.White,DpiKey=(int)Math.Round(dpi*100.0) };FormattedText result;
            if(labelTexts.TryGetValue(key,out result))return result;
            result=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,labelTypeface,size,ink,dpi) { MaxTextWidth=220,MaxLineCount=1,Trimming=TextTrimming.CharacterEllipsis };
            if(labelTexts.Count>=1024)labelTexts.Remove(labelTextOrder.Dequeue());labelTexts.Add(key,result);labelTextOrder.Enqueue(key);return result;
        }
        void LabelScreen(DrawingContext dc,string text,Point screenAt,Brush ink,double size) {
            if(screenAt.X<-228||screenAt.X>ActualWidth+4||screenAt.Y<-size*3||screenAt.Y>ActualHeight+1)return;
            var f=LabelText(text,size,ink,CurrentDpiScaleX,true);
            var bounds=new Rect(screenAt.X-4,screenAt.Y-1,Math.Min(220,f.Width)+8,f.Height+2);
            if(!bounds.IntersectsWith(new Rect(0,0,ActualWidth,ActualHeight)))return;
            dc.DrawRoundedRectangle(MapMarkerBg,null,bounds,3,3);dc.DrawText(f,screenAt);
        }
        void Label(DrawingContext dc,string text,Point at,Brush ink,double size,bool cacheable=true) {
            var screen=RotatePoint(at,renderRotation,ActualWidth,ActualHeight);
            // Text backgrounds are capped at 228 DIPs. Reject distant labels
            // before formatting; retain labels that can cross a viewport edge.
            if(screen.X<-228||screen.X>ActualWidth+4||screen.Y<-size*3||screen.Y>ActualHeight+1)return;
            var f=LabelText(text,size,ink,CurrentDpiScaleX,cacheable);var bounds=new Rect(screen.X-4,screen.Y-1,Math.Min(220,f.Width)+8,f.Height+2);
            if(!bounds.IntersectsWith(new Rect(0,0,ActualWidth,ActualHeight)))return;
            if(renderRotation!=0)dc.PushTransform(FrozenRotate(-renderRotation,at.X,at.Y));dc.DrawRoundedRectangle(CachedBrush(210,11,14,18),null,new Rect(at.X-4,at.Y-1,Math.Min(220,f.Width)+8,f.Height+2),3,3);dc.DrawText(f,at);if(renderRotation!=0)dc.Pop();
        }
        internal static string PoiSymbol(ScumMapCategory category) {
            string name=category==null?"":(category.Section+" "+category.Name).ToLowerInvariant();
            if(name.Contains("fish"))return "≈";if(name.Contains("hunt"))return "⌖";
            if(name.Contains("gas")||name.Contains("fuel"))return "F";
            if(name.Contains("bunker"))return "B";if(name.Contains("outpost")||name.Contains("trader"))return "$";
            if(name.Contains("hospital")||name.Contains("medic"))return "+";
            if(name.Contains("police"))return "P";if(name.Contains("military"))return "★";
            if(name.Contains("town")||name.Contains("city"))return "⌂";
            if(name.Contains("vehicle"))return "V";return "◆";
        }
        static readonly Dictionary<string,DrawingGroup> poiIcons=new Dictionary<string,DrawingGroup>();
        static readonly Queue<string> poiIconOrder=new Queue<string>();
        static readonly Dictionary<long,Pen> poiOutlinePens=new Dictionary<long,Pen>();
        void DrawPoi(DrawingContext dc,Point at,ScumMapCategory category,WpfPoiCluster cluster) {
            EnsurePoiAssets(category,cluster);
            double radius=cluster.Radius;
            dc.DrawEllipse(MapMarkerBg,cluster.Outline,at,radius,radius);
            double scale=(cluster.Members.Count>1?(Compact?9:11):radius*1.45)/12;
            if(renderRotation==0) {
                dc.PushTransform(FrozenMatrix(scale,0,0,scale,at.X,at.Y));
            } else {
                var transform=new Matrix(scale,0,0,scale,0,0);transform.Rotate(-renderRotation);transform.Translate(at.X,at.Y);
                dc.PushTransform(FrozenMatrix(transform));
            }
            dc.DrawDrawing(cluster.Icon);dc.Pop();
        }
        void EnsurePoiAssets(ScumMapCategory category,WpfPoiCluster cluster) {
            int color=category==null?unchecked((int)0xFFA4B1BC):category.ColorBackground;
            if(cluster.Icon==null) {
                string label=cluster.Members.Count>1?(cluster.Members.Count>99?"99+":cluster.Members.Count.ToString(CultureInfo.InvariantCulture)):PoiSymbol(category);
                int dpiKey=(int)Math.Round(CurrentDpiScaleX*100.0);
                string key=dpiKey.ToString(CultureInfo.InvariantCulture)+"|"+label;
                DrawingGroup icon;
                if(!poiIcons.TryGetValue(key,out icon)) {
                icon=new DrawingGroup();using(var drawing=icon.Open()) {
                    // One frozen vector glyph per symbol/count. Zoom changes its
                    // transform, never its font layout or drawing asset.
                    var text=new FormattedText(label,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,poiTypeface,11.5,Brushes.White,CurrentDpiScaleX);
                    if(label.Length>1) {
                        var bg=CachedBrush(220,12,16,22);
                        drawing.DrawRoundedRectangle(bg,null,new Rect(-text.Width/2-2.5,-text.Height/2-1,text.Width+5,text.Height+2),4,4);
                    }
                    drawing.DrawText(text,new Point(-text.Width/2,-text.Height/2));
                }icon.Freeze();
                if(poiIcons.Count>=512)poiIcons.Remove(poiIconOrder.Dequeue());
                poiIcons.Add(key,icon);poiIconOrder.Enqueue(key);
                }
                cluster.Icon=icon;
            }
            if(cluster.Outline==null||cluster.IconColor!=color) {
            long penKey=((long)(uint)color<<1)|(cluster.Mixed?1L:0L);Pen outline;
            if(!poiOutlinePens.TryGetValue(penKey,out outline)) {
                var ink=cluster.Mixed?WpfFieldTheme.Amber:CachedBrush(255,(byte)(color>>16),(byte)(color>>8),(byte)color);
                outline=new Pen(ink,2);outline.Freeze();if(poiOutlinePens.Count>=256)poiOutlinePens.Clear();poiOutlinePens.Add(penKey,outline);
            }
            cluster.Outline=outline;cluster.IconColor=color;
            }
        }
    }
}
