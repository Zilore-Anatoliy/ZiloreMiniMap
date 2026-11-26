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
        ToolTip hoverToolTip; TextBlock hoverToolTipText; Point lastCursorRedrawAt; long lastCursorRedrawMs;
        void RedrawLiveLayerOnly() {
            if(Compact||State==null||ActualWidth<1||ActualHeight<1)return;
            using(var dc=liveLayer.RenderOpen())DrawMapPass(dc,MapPass.Live);
            LiveLayerBuilds++;
        }
        void UpdateHover(Point at) {
            if(CameraInteracting)return;
            long now=Environment.TickCount&int.MaxValue;if(now-lastHover<100&&now>=lastHover)return;lastHover=now;
            DrawingPoint point;
            bool validPt=TryMapPoint(at,out point);
            if(!Compact) {
                DrawingPoint? nextCursor=validPt?(DrawingPoint?)point:null;
                if(cursorMapPoint!=nextCursor) {
                    cursorMapPoint=nextCursor;
                    bool measuring=measurePoints.Count>0&&!measureClosed;
                    if(measuring) { lastCursorRedrawAt=at; lastCursorRedrawMs=now; RedrawLiveLayerOnly(); }
                    else if(!drag.HasValue&&openContext==null) {
                        double cdx=at.X-lastCursorRedrawAt.X, cdy=at.Y-lastCursorRedrawAt.Y;
                        if((cdx*cdx+cdy*cdy>=36.0&&(now-lastCursorRedrawMs>=180||now<lastCursorRedrawMs))||!validPt) {
                            lastCursorRedrawAt=at; lastCursorRedrawMs=now; RedrawLiveLayerOnly();
                        }
                    }
                }
            }
            string text=drag.HasValue||contextPointer.HasValue||openContext!=null||!validPt?null:HoverDetails(point);if(text==hoverText)return;hoverText=text;
            if(text==null) { ToolTip=null; }
            else {
                if(hoverToolTip==null) {
                    hoverToolTipText=new TextBlock { TextWrapping=TextWrapping.Wrap,MaxWidth=380 };
                    hoverToolTip=new ToolTip { Content=hoverToolTipText,Padding=new Thickness(11,7,11,7) };
                    hoverToolTip.SetResourceReference(Control.BackgroundProperty,"ThemeRaised");
                    hoverToolTip.SetResourceReference(Control.ForegroundProperty,"ThemeInk");
                    hoverToolTip.SetResourceReference(Control.BorderBrushProperty,"ThemeLine");
                }
                hoverToolTipText.Text=text; ToolTip=hoverToolTip;
            }
            if(HoverChanged!=null)HoverChanged(text);
        }
        static double MarkerDistanceSq(DrawingPoint a,DrawingPoint b) { double dx=a.X-b.X,dy=a.Y-b.Y;return dx*dx+dy*dy; }
        internal bool HitsDestination(DrawingPoint point) { if(State==null||State.Target==null)return false;double r=12/MapSide(State,ActualWidth,ActualHeight,Compact);return MarkerDistanceSq(point,State.Target.Centroid)<=r*r; }
        static double MarkerDistance(DrawingPoint a,DrawingPoint b) { return Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y)); }
        internal static void MapToWorld(DrawingPoint mapPt,out double worldX,out double worldY) {
            worldX=617718.0-mapPt.X*1521618.0;
            worldY=618618.0-mapPt.Y*1523618.0;
        }
        internal ContextMenu BuildContextMenu(DrawingPoint target) {
            double ctxSide=State!=null?MapSide(State,ActualWidth,ActualHeight,Compact):0;
            double ctxHitTol=ctxSide>1.0?14.0/ctxSide:0.0;
            var menu=new ContextMenu();menu.SetResourceReference(Control.BackgroundProperty,"ThemePanel");menu.SetResourceReference(Control.ForegroundProperty,"ThemeInk");
            double hwx,hwy;MapToWorld(target,out hwx,out hwy);
            string hdrTop=DestinationSearch.Sector(target);
            if(State!=null&&State.Position!=null) {
                var pPt=MapWindow.ToMap(State.Position);
                double dM=DestinationSearch.Metres(pPt,target);
                int brg=(int)Math.Round((Math.Atan2(target.X-pPt.X,-(target.Y-pPt.Y))*180.0/Math.PI+360.0)%360.0)%360;
                hdrTop+=" · "+DestinationSearch.Distance(dM)+" ("+brg.ToString("000",CultureInfo.InvariantCulture)+"°)";
            }
            string hdrInfo=hdrTop+"\nX: "+Math.Round(hwx).ToString(CultureInfo.InvariantCulture)+"   Y: "+Math.Round(hwy).ToString(CultureInfo.InvariantCulture);
            menu.Items.Add(new MenuItem { Header=hdrInfo,IsEnabled=false,Tag="ctx-header" });
            var navigate=new MenuItem { Header=Localization.Get("MapGpsDestination"),ToolTip=Localization.Get("MapGpsSavedHelp"),Tag="ctx-primary" };navigate.Click+=(s,e)=>NavigateTo(target);menu.Items.Add(navigate);
            if(RouteStopAt!=null&&State!=null&&State.LocalRoute!=null) {
                var stop=new MenuItem { Header=Localization.Get("SquadAddStop"),IsEnabled=State.LocalRoute.Length<SquadState.MaximumStops,ToolTip=Localization.Get("MapGpsSavedHelp") };stop.Click+=(s,e)=> { var destination=SharedDestination(target);if(destination!=null&&RouteDestination!=null)RouteDestination(destination);else RouteStopAt(target); };menu.Items.Add(stop);
                if(RouteInsertStopAt!=null&&State.LocalRoute.Length>=1&&State.LocalRoute.Length<SquadState.MaximumStops) {
                    var ins=new MenuItem { Header=WpfFieldTheme.UiText("MapInsertStopHere") };ins.Click+=(s,e)=>RouteInsertStopAt(target);menu.Items.Add(ins);
                }
            }
            var save=new MenuItem { Header=Localization.Get("MapSaveLocation"),ToolTip=Localization.Get("MapGpsSavedHelp") };save.Click+=(s,e)=> { if(WaypointAt!=null)WaypointAt(target); };menu.Items.Add(save);
            if(RouteStopAt!=null&&State!=null&&State.LocalRoute!=null) {
                menu.Items.Add(new Separator());
                var routeMenu=new MenuItem { Header=Localization.Get("RouteList") };
                var clicked=State.SharedMarkers==null?null:State.SharedMarkers.Where(m=>m!=null&&SquadState.IsStop(m)&&MarkerDistance(new DrawingPoint((float)m.X,(float)m.Y),target)<=ctxHitTol).FirstOrDefault();var chosen=clicked!=null?SquadState.Routes(State.SharedMarkers).FirstOrDefault(r=>r.Length>0&&SquadState.RouteKey(r[0])==SquadState.RouteKey(clicked)):State.LocalRoute;
                var start=new MenuItem { Header=Localization.Get("SquadStartRoute"),IsEnabled=chosen!=null&&chosen.Length>0 };start.Click+=(s,e)=> { if(RouteNavigate!=null)RouteNavigate(chosen); };routeMenu.Items.Add(start);
                if(clicked!=null&&clicked.OwnerId==null&&State.LocalRoute.Any(m=>m.Id==clicked.Id)) { var remove=new MenuItem { Header=Localization.Get("SquadDelete")+": "+clicked.Name };remove.Click+=(s,e)=> { if(RouteRemove!=null)RouteRemove(clicked.Id); };routeMenu.Items.Add(remove); }
                if(RouteSkipStop!=null&&State.SquadNavigating) { var skip=new MenuItem { Header=WpfFieldTheme.UiText("RouteSkipStop") };skip.Click+=(s,e)=>RouteSkipStop();routeMenu.Items.Add(skip); }
                if(RouteOptimize!=null&&State.LocalRoute.Length>2) { var opt=new MenuItem { Header=WpfFieldTheme.UiText("RouteOptimize") };opt.Click+=(s,e)=>RouteOptimize();routeMenu.Items.Add(opt); }
                if(RouteReverse!=null&&State.LocalRoute.Length>1) { var rev=new MenuItem { Header=WpfFieldTheme.UiText("RouteReverse") };rev.Click+=(s,e)=>RouteReverse();routeMenu.Items.Add(rev); }
                if(RouteLoop!=null&&State.LocalRoute.Length>=2&&State.LocalRoute.Length<SquadState.MaximumStops) { var lp=new MenuItem { Header=WpfFieldTheme.UiText("RouteLoop") };lp.Click+=(s,e)=>RouteLoop();routeMenu.Items.Add(lp); }
                if(RouteCopyCode!=null&&State.LocalRoute.Length>0) { var cp=new MenuItem { Header=WpfFieldTheme.UiText("RouteCopyCode") };cp.Click+=(s,e)=>RouteCopyCode();routeMenu.Items.Add(cp); }
                var ownMenu=new MenuItem { Header=Localization.Get("OwnRoute") };foreach(bool shared in new[]{false,true}) { bool kind=shared;var mode=new MenuItem { Header=Localization.Get(shared?"SquadRoute":"SoloRoute"),IsCheckable=true,IsChecked=State.LocalRoute.Length>0&&SquadState.IsShared(State.LocalRoute[0])==shared };mode.Click+=(s,e)=> { if(RouteKind!=null)RouteKind(kind); };ownMenu.Items.Add(mode); }routeMenu.Items.Add(ownMenu);
                var undo=new MenuItem { Header=Localization.Get("SquadUndoStop"),IsEnabled=State.LocalRoute.Length>0 };undo.Click+=(s,e)=> { if(RouteUndo!=null)RouteUndo(); };routeMenu.Items.Add(undo);
                var clearLocal=new MenuItem { Header=Localization.Get("WpfClearRoute"),IsEnabled=State.LocalRoute.Length>0 };clearLocal.Click+=(s,e)=> { if(RouteClear!=null)RouteClear(); };routeMenu.Items.Add(clearLocal);
                var end=new MenuItem { Header=Localization.Get("SquadStopNav"),IsEnabled=State.SquadNavigating||State.Target!=null };end.Click+=(s,e)=> { if(StopNavigationRequested!=null)StopNavigationRequested(); };routeMenu.Items.Add(end);
                var manage=new MenuItem { Header=Localization.Get("RouteList")+"…" };manage.Click+=(s,e)=> { if(RoutesRequested!=null)RoutesRequested(); };routeMenu.Items.Add(manage);menu.Items.Add(routeMenu);
            }
            var toolsMenu=new MenuItem { Header=WpfFieldTheme.UiText("MapToolsAndRunways") };
            var measureFrom=new MenuItem { Header=WpfFieldTheme.UiText("MapMeasureFrom") };measureFrom.Click+=(s,e)=> { measurePoints.Clear();measurePoints.Add(target);measureClosed=false;RedrawLiveLayerOnly(); };toolsMenu.Items.Add(measureFrom);
            if(measurePoints.Count>0||RulerModeActive) { var clearMeasure=new MenuItem { Header=WpfFieldTheme.UiText("MapMeasureClear") };clearMeasure.Click+=(s,e)=>ClearRuler();toolsMenu.Items.Add(clearMeasure); }
            var copyCoords=new MenuItem { Header=WpfFieldTheme.UiText("MapCopyCoords") };copyCoords.Click+=(s,e)=> { try { double wx,wy;MapToWorld(target,out wx,out wy); System.Windows.Clipboard.SetText(DestinationSearch.Sector(target)+" · X="+Math.Round(wx).ToString(CultureInfo.InvariantCulture)+" Y="+Math.Round(wy).ToString(CultureInfo.InvariantCulture)); } catch { } };toolsMenu.Items.Add(copyCoords);
            toolsMenu.Items.Add(new Separator());
            var addRwy=new MenuItem { Header=WpfFieldTheme.UiText("AddCustomRunway") };addRwy.Click+=(s,e)=> { AddCustomRunway(target,State!=null&&State.Position!=null?State.Position.Yaw:90.0);RequestRedraw(); };toolsMenu.Items.Add(addRwy);
            if(CustomRunways.Any(r=>DestinationSearch.Metres(target,new DrawingPoint(r.X,r.Y))<=250.0)) { var delRwy=new MenuItem { Header=WpfFieldTheme.UiText("RemoveCustomRunway") };delRwy.Click+=(s,e)=> { RemoveNearestCustomRunway(target);RequestRedraw(); };toolsMenu.Items.Add(delRwy); }
            if(CustomRunways.Count>0) { var copyRwy=new MenuItem { Header=WpfFieldTheme.UiText("RunwayCopyCode") };copyRwy.Click+=(s,e)=> { try { string code=ExportCustomRunwaysCode(); if(code.Length>0) System.Windows.Clipboard.SetText(code); } catch { } };toolsMenu.Items.Add(copyRwy); }
            var impRwy=new MenuItem { Header=WpfFieldTheme.UiText("RunwayImportCode") };impRwy.Click+=(s,e)=> { try { string clip=System.Windows.Clipboard.ContainsText()?System.Windows.Clipboard.GetText():""; int added; if(TryImportCustomRunwaysCode(clip,out added)&&added>0) RequestRedraw(); } catch { } };toolsMenu.Items.Add(impRwy);
            if(WaypointsCopyCode!=null||WaypointsImportCode!=null) {
                bool ruCtx=Localization.Current==AppLanguage.Russian;
                toolsMenu.Items.Add(new Separator());
                if(WaypointsCopyCode!=null) { var cpWp=new MenuItem { Header=ruCtx?"📤 Копировать метки (ZLR-WP)":"📤 Copy Waypoints (ZLR-WP)" }; cpWp.Click+=(s,e)=>WaypointsCopyCode(); toolsMenu.Items.Add(cpWp); }
                if(WaypointsImportCode!=null) { var imWp=new MenuItem { Header=ruCtx?"📥 Импорт меток (ZLR-WP)":"📥 Import Waypoints (ZLR-WP)" }; imWp.Click+=(s,e)=>WaypointsImportCode(); toolsMenu.Items.Add(imWp); }
            }
            menu.Items.Add(toolsMenu);
            if(NavigatePeer!=null&&State!=null&&State.Peers!=null) { for(int pi=0;pi<State.Peers.Length;pi++) { var peer=State.Peers[pi]; if(peer==null||peer.Position==null||string.IsNullOrEmpty(peer.Id)) continue; if(MarkerDistance(MapWindow.ToMap(peer.Position),target)<=ctxHitTol) { string id=peer.Id;var follow=new MenuItem { Header=Localization.Get("SquadFollow")+": "+peer.Name };follow.Click+=(s,e)=>NavigatePeer(id);menu.Items.Add(follow); } } }
            if(PingAt!=null) {
                var pingMenu=new MenuItem { Header=WpfFieldTheme.UiText("MapSquadPings") };
                bool ruPing=Localization.Current==AppLanguage.Russian;
                for(int i=0;i<3;i++) { int kind=i;var ping=new MenuItem { Header=SquadStatusKind.GetDisplayLabel(kind,ruPing),IsEnabled=PingAvailable==null||PingAvailable(),ToolTip=Localization.Get("SquadPingHelp") };ping.Click+=(s,e)=>PingAt(kind,target);pingMenu.Items.Add(ping); }
                pingMenu.Items.Add(new Separator());
                for(int i=3;i<=SquadStatusKind.MaxKind;i++) { int kind=i;var stItem=new MenuItem { Header=SquadStatusKind.GetDisplayLabel(kind,ruPing),IsEnabled=PingAvailable==null||PingAvailable(),ToolTip=Localization.Get("SquadPingHelp") };stItem.Click+=(s,e)=>PingAt(kind,target);pingMenu.Items.Add(stItem); }
                menu.Items.Add(pingMenu);
            }
            menu.Items.Add(new Separator());var clear=new MenuItem { Header=Localization.Get("WpfClearRoute"),IsEnabled=State!=null&&(State.Target!=null||State.LocalRoute.Length>0) };clear.Click+=(s,e)=> { if(ClearRouteRequested!=null)ClearRouteRequested(); };menu.Items.Add(clear);
            var saved=State==null||State.Saved==null?null:State.Saved.Where(z=>z.Points!=null&&z.Points.Length==1&&MarkerDistance(target,z.Centroid)<=ctxHitTol).OrderByDescending(z=>z.IsDeathMarker).ThenBy(z=>MarkerDistance(target,z.Centroid)).FirstOrDefault();
            if(saved!=null&&MarkerDistance(target,saved.Centroid)<=ctxHitTol) { var delete=new MenuItem { Header=Localization.T("DeleteWaypointNamed",saved.DisplayName) };delete.Click+=(s,e)=> { if(DeleteWaypointRequested!=null)DeleteWaypointRequested(saved); };menu.Items.Add(delete); }
            if(AdminMenuAt!=null) { var admin=AdminMenuAt(target);if(admin!=null) { menu.Items.Add(new Separator());menu.Items.Add(admin); } }
            return menu;
        }
        internal Func<DrawingPoint,MenuItem> AdminMenuAt;
        internal Action<int> ZoomRequested;
        internal Action<DrawingPoint> NavigateAt,WaypointAt,RouteStopAt,RouteInsertStopAt;
        internal Action<int,DrawingPoint> PingAt;
        internal Action<SharedMapMarker[]> RouteNavigate;
        internal Action<string> RouteRemove,NavigatePeer;
        internal Action<string,DrawingPoint> RouteStopMoved;
        string draggingStopId;
        bool draggingSingleTarget;
        DrawingPoint? draggingPreviewPoint;
        internal Action<bool> RouteKind;
        internal Action RouteUndo,RouteClear,RoutesRequested,RouteOptimize,RouteReverse,RouteLoop,RouteCopyCode,RouteSkipStop;
        internal Action WaypointsCopyCode,WaypointsImportCode;
        internal Func<bool> PingAvailable;
        internal Action<MapZone> NavigateShared,RouteDestination;
        internal Canvas ContextHost;
        Border openContext;
        Point contextAnchor;
        internal bool ContextMenuOpen { get { return openContext!=null; } }
        Point? contextPointer;
        internal bool BeginContextPointer(Point point) {
            DrawingPoint target;if(!TryMapPoint(point,out target))return false;
            CancelPointer();ClearHover();contextPointer=point;CaptureMouse();return true;
        }
        internal bool EndContextPointer(Point point) {
            if(!contextPointer.HasValue)return false;
            var origin=contextPointer.Value;contextPointer=null;
            if(IsMouseCaptured)ReleaseMouseCapture();
            return OpenContextMenu(origin);
        }
        internal bool OpenContextMenu(Point point) {
            DrawingPoint target;if(ContextHost==null||!TryMapPoint(point,out target))return false;
            CloseContextMenu();ClearHover();contextAnchor=point;
            // Keep every level in the map's existing HWND. Native Popup/MenuItem
            // windows need activation/capture that conflicts with SCUM's focus.
            var actions=BuildContextMenu(target);
            var scroll=new ScrollViewer { Content=ContextItems(actions.Items),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=Math.Max(80,ActualHeight-24) };
            openContext=WpfFieldTheme.Card(scroll,new Thickness(7));openContext.CornerRadius=new CornerRadius(12);openContext.Width=Math.Min(286,Math.Max(190,ActualWidth-16));SystemTypography.ApplyClearType(openContext);
            if(DirectTerrain) openContext.Effect=new System.Windows.Media.Effects.DropShadowEffect { BlurRadius=22,ShadowDepth=4,Opacity=0.32,Color=Colors.Black };
            openContext.SizeChanged+=(s,e)=>PositionContextMenu();ContextHost.Children.Add(openContext);PositionContextMenu();return true;
        }
        StackPanel ContextItems(ItemCollection items) {
            var panel=new StackPanel();
            foreach(var value in items) {
                var item=value as MenuItem;
                if(item==null) { var sepBd=new Border { Height=1,Margin=new Thickness(4,5,4,5) }; sepBd.SetResourceReference(Border.BackgroundProperty,"ThemeLine"); panel.Children.Add(sepBd); continue; }
                string title=(item.IsChecked?"✓ ":"")+Convert.ToString(item.Header,CultureInfo.CurrentCulture);
                if(item.Tag as string=="ctx-header") {
                    string[] hp=title.Split('\n');
                    var hdrStack=new StackPanel();
                    hdrStack.Children.Add(WpfFieldTheme.Text(hp[0],13,WpfFieldTheme.Cyan,true));
                    if(hp.Length>1) hdrStack.Children.Add(WpfFieldTheme.Text(hp[1],11,WpfFieldTheme.Muted,false));
                    var hdrPill=new Border { CornerRadius=new CornerRadius(8),Padding=new Thickness(10,6,10,6),Margin=new Thickness(0,0,0,5),Child=hdrStack };
                    hdrPill.SetResourceReference(Border.BackgroundProperty,"ThemeRaised");
                    panel.Children.Add(hdrPill);continue;
                }
                if(item.HasItems) {
                    var children=ContextItems(item.Items);children.Margin=new Thickness(10,2,0,3);children.Visibility=Visibility.Collapsed;
                    Button expand=null;expand=WpfFieldTheme.Button("▸ "+title,()=> { bool show=children.Visibility!=Visibility.Visible;children.Visibility=show?Visibility.Visible:Visibility.Collapsed;expand.Content=(show?"▾ ":"▸ ")+title; });
                    expand.IsEnabled=item.IsEnabled;expand.HorizontalContentAlignment=HorizontalAlignment.Left;expand.Background=Brushes.Transparent;expand.BorderThickness=new Thickness(0);expand.FontSize=13;expand.FontWeight=FontWeights.SemiBold;expand.Margin=new Thickness(0,1,0,1);expand.MinHeight=30;expand.Padding=new Thickness(9,4,9,4);panel.Children.Add(expand);panel.Children.Add(children);
                } else {
                    var captured=item;bool isPrimary=item.Tag as string=="ctx-primary";
                    var action=WpfFieldTheme.Button(title,()=> { CloseContextMenu();captured.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent,captured)); },isPrimary);
                    action.ToolTip=item.ToolTip;action.IsEnabled=item.IsEnabled;action.HorizontalContentAlignment=HorizontalAlignment.Left;action.FontSize=13;action.Margin=isPrimary?new Thickness(0,1,0,4):new Thickness(0,1,0,1);action.MinHeight=isPrimary?34:29;action.Padding=new Thickness(10,4,10,4);
                    if(!isPrimary) { action.Background=Brushes.Transparent;action.BorderThickness=new Thickness(0); }
                    action.Content=new TextBlock { Text=title,TextWrapping=TextWrapping.Wrap };panel.Children.Add(action);
                }
            }
            return panel;
        }
        internal static void AddAdminContextItems(ItemCollection items,MapWindow.AdminMapAction[] actions) {
            foreach(var action in actions) {
                if(action.Name==null) { items.Add(new Separator());continue; }
                var item=new MenuItem { Header=action.Name,ToolTip=action.Help,Tag=action.Command,IsEnabled=action.Enabled,IsChecked=action.Checked };
                if(action.Children!=null)AddAdminContextItems(item.Items,action.Children);
                else { var captured=action;item.Click+=(s,e)=> { e.Handled=true;if(captured.Invoke!=null)captured.Invoke(); }; }
                items.Add(item);
            }
        }
        void PositionContextMenu() {
            if(openContext==null)return;
            openContext.Measure(new Size(openContext.Width,Math.Max(80,ActualHeight-16)));
            double usableRight=Math.Max(openContext.DesiredSize.Width+16,ActualWidth-RightOverlayInset);Canvas.SetLeft(openContext,Math.Max(8,Math.Min(contextAnchor.X,usableRight-openContext.DesiredSize.Width-8)));
            Canvas.SetTop(openContext,Math.Max(8,Math.Min(contextAnchor.Y,ActualHeight-openContext.DesiredSize.Height-8)));
        }
        internal bool DismissContextOutside(Point point) {
            if(openContext==null)return false;
            double left=Canvas.GetLeft(openContext),top=Canvas.GetTop(openContext);
            if(double.IsNaN(left)||double.IsNaN(top)||openContext.ActualWidth<=0||openContext.ActualHeight<=0) { CloseContextMenu();return true; }
            var bounds=new Rect(left,top,openContext.ActualWidth,openContext.ActualHeight);
            if(bounds.Contains(point))return false;CloseContextMenu();return true;
        }
        internal void CloseContextMenu() {
            contextPointer=null;if(IsMouseCaptured)ReleaseMouseCapture();
            var menu=openContext;if(menu==null)return;openContext=null;if(ContextHost!=null)ContextHost.Children.Remove(menu);
        }
        Point? drag;
        DrawingPoint pan;
        bool dragging;
        internal bool PointerCameraActive { get { return drag.HasValue&&dragging; } }
        internal static volatile bool HudSpeedAutoZoomEnabled = true;
        internal static double MapSide(WpfMapState state,double width,double height,bool compact) {
            return MapCameraService.ComputeMapSide(state.Zoom,width,height,compact,AviationModeEnabled,HudSpeedAutoZoomEnabled,CurrentGroundSpeedMps);
        }
        internal static DrawingPoint CameraCentre(WpfMapState state,double width,double height,bool compact) {
            return MapCameraService.ComputeCameraCentre(state.Pan,state.Position,MapSide(state,width,height,compact),width,height,compact);
        }
        internal bool TryMapPoint(Point p,out DrawingPoint target) {
            target=new DrawingPoint();
            if(Compact||State==null||ActualWidth<=0||ActualHeight<=0||p.X<0||p.Y<0||p.X>ActualWidth||p.Y>ActualHeight)return false;
            double side=MapSide(State,ActualWidth,ActualHeight,Compact);if(side<=0)return false;
            target=new DrawingPoint((float)(CameraCentre(State,ActualWidth,ActualHeight,Compact).X+(p.X-ActualWidth/2)/side),(float)(CameraCentre(State,ActualWidth,ActualHeight,Compact).Y+(p.Y-ActualHeight/2)/side));
            return target.X>=0&&target.X<=1&&target.Y>=0&&target.Y<=1;
        }
        internal void BeginPointer(Point p) {
            if(Compact||State==null)return;
            ClearHover();drag=p;pan=CameraCentre(State,ActualWidth,ActualHeight,Compact);dragging=false;draggingStopId=null;draggingSingleTarget=false;draggingPreviewPoint=null;
            DrawingPoint mapPt;
            if(TryMapPoint(p,out mapPt)) {
                double maxR=16/MapSide(State,ActualWidth,ActualHeight,Compact),bestSq=maxR*maxR;
                if(RouteStopMoved!=null&&State.LocalRoute!=null&&State.LocalRoute.Length>0) {
                    for(int i=0;i<State.LocalRoute.Length;i++) {
                        var s=State.LocalRoute[i];
                        double dSq=MarkerDistanceSq(new DrawingPoint((float)s.X,(float)s.Y),mapPt);
                        if(dSq<=bestSq) { bestSq=dSq;draggingStopId=s.Id; }
                    }
                }
                if(draggingStopId==null&&State.Target!=null&&NavigateAt!=null) {
                    double dSq=MarkerDistanceSq(State.Target.Centroid,mapPt);
                    if(dSq<=bestSq) draggingSingleTarget=true;
                }
            }
        }
        internal void MovePointer(Point p) {
            if(!drag.HasValue||State==null)return;
            double dx=p.X-drag.Value.X,dy=p.Y-drag.Value.Y;
            if(!dragging&&Math.Abs(dx)<=SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(dy)<=SystemParameters.MinimumVerticalDragDistance)return;
            dragging=true;
            if(draggingStopId!=null||draggingSingleTarget) {
                DrawingPoint preview;
                if(TryMapPoint(p,out preview)) { draggingPreviewPoint=preview;RedrawLiveLayerOnly(); }
                return;
            }
            double side=MapSide(State,ActualWidth,ActualHeight,Compact);if(side<=0)return;
            if(PanChanged!=null)PanChanged(new DrawingPoint((float)Math.Max(0,Math.Min(1,pan.X-dx/side)),(float)Math.Max(0,Math.Min(1,pan.Y-dy/side))));
        }
        internal void EndPointer(Point p) {
            if(!drag.HasValue)return;
            string movedId=draggingStopId;
            bool movedSingle=draggingSingleTarget;
            MovePointer(p);bool wasDrag=dragging;CancelPointer();DrawingPoint target;
            if(movedId!=null&&wasDrag&&RouteStopMoved!=null&&TryMapPoint(p,out target)) { RouteStopMoved(movedId,target);return; }
            if(movedSingle&&wasDrag&&NavigateAt!=null&&TryMapPoint(p,out target)) { NavigateAt(target);return; }
            bool click=!wasDrag;
            if(click&&TryMapPoint(p,out target)) {
                if((Keyboard.Modifiers&ModifierKeys.Control)!=0 || (RulerModeActive && Keyboard.Modifiers==ModifierKeys.None)) {
                    if(measureClosed) { measurePoints.Clear();measureClosed=false; }
                    if(measurePoints.Count<32) measurePoints.Add(target);
                    RedrawLiveLayerOnly();return;
                }
                if(measurePoints.Count>0&&!measureClosed&&Keyboard.Modifiers==ModifierKeys.None) {
                    measurePoints.Add(target);measureClosed=true;RedrawLiveLayerOnly();return;
                }
                if((Keyboard.Modifiers&ModifierKeys.Alt)!=0&&RouteInsertStopAt!=null) {
                    RouteInsertStopAt(target);
                    return;
                }
                if((Keyboard.Modifiers&ModifierKeys.Shift)!=0&&RouteStopAt!=null) {
                    var shared=SharedDestination(target);
                    if(shared!=null&&RouteDestination!=null)RouteDestination(shared);
                    else RouteStopAt(target);
                    return;
                }
                if(HitsDestination(target)&&ClearRouteRequested!=null)ClearRouteRequested();else NavigateTo(target);
            }
        }
        internal void CancelPointer() { drag=null;dragging=false;draggingStopId=null;draggingSingleTarget=false;if(draggingPreviewPoint.HasValue){draggingPreviewPoint=null;RedrawLiveLayerOnly();} }
    }
}
