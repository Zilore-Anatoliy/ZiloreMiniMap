using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.IO;

namespace ZiloreMiniMap {
    internal sealed class WpfMapState {
        internal Position Position;
        internal DateTime Updated;
        internal MapZone Target;
        internal RoadRoute Route;
        internal bool Calculating,Grid,Zones,Waypoints,History,Labels,Tracking,Heading,Compass,Status;
        internal int HudWidth,HudHeight,HudLeft,HudTop,PlayerColor,RouteColor,HistoryColor;
        internal bool StatusAbove;
        internal bool RotateMap;
        internal bool PoiClustering=true;
        internal PrivatePositions.Peer[] Peers=Array.Empty<PrivatePositions.Peer>();
        internal SharedMapMarker[] SharedMarkers=Array.Empty<SharedMapMarker>();
        internal SquadRoutePlan SquadPlan,OwnRoutePlan;
        internal SquadRouteDrawing[] OtherRoutePlans=Array.Empty<SquadRouteDrawing>();
        internal SharedMapMarker[] LocalRoute=Array.Empty<SharedMapMarker>();
        internal string ActiveSquadRoute,OwnRouteSummary,LocalPeerId;
        internal bool SquadPlanning,SquadNavigating,ActiveSquadShared;
        internal bool GridLabels,SmartLabels;
        internal int GridOpacity,LabelSize;
        internal int ZoneRevision;
        internal ScumMapStore Store;
        internal MapZone[] Saved;
        internal WpfZoneOption[] CustomZones;
        internal bool CustomZonesExpanded;
        internal PointF[] Trail;
        internal string Location,Shape,RouteSummary,TrackingStatus;
        internal bool TrackingPaused;
        internal int Opacity,MapKey,MapScan,ChatKey,CopyKey,CopyModifier;
        internal float Zoom;
        internal PointF Pan;
        internal AppControlBindings Bindings;
    }
    internal sealed class WpfZoneOption { internal MapZone Zone;internal string Key,Layer;internal bool Enabled,LayerEnabled; }
    public sealed partial class MapWindow {
        readonly int wpfOwnerThread=Environment.CurrentManagedThreadId;
        void WpfVerifyAccess() { if(Environment.CurrentManagedThreadId!=wpfOwnerThread)throw new InvalidOperationException("Map state belongs to the UI thread."); }
        int wpfDefaultsRevision;
        internal RoadRouteMode wpfRouteMode=RoadRouteMode.Shortest;
        internal RoadRouteMode WpfRouteMode { get { return wpfRouteMode; } }
        internal void WpfSetRouteMode(RoadRouteMode mode) {
            WpfVerifyAccess();if(!Enum.IsDefined<RoadRouteMode>(mode)||mode==wpfRouteMode)return;
            wpfRouteMode=mode;routeGeneration++;activeRoute=null;lastRouteTarget=null;
            if(searchTarget!=null&&position!=null) {
                if(routeCalculating)wpfQueuedTarget=searchTarget;
                else UpdateRouteAsync(ToMap(position),searchTarget);
            }
            lastFrameKey=null;SaveSettings();
        }
        internal bool wpfRotateMap;
        internal string wpfUiTheme="System";
        internal string WpfUiTheme { get { return wpfUiTheme; } }
        internal void WpfSetUiTheme(string theme) { wpfUiTheme=string.IsNullOrWhiteSpace(theme)?"System":theme;SettingsChanged();SaveSettings(); }
        internal bool wpfPoiClustering=true;
        int WpfTacticalOpacityValue { get { return fullMapOpacity; } }
        internal bool WpfTacticalZoomLocked;
        int avoidZonesMode; // 0 = Off, 1 = All custom zones, 2 = Danger/PvP/Red zones only
        internal int WpfAvoidZonesMode {
            get { return avoidZonesMode; }
            set {
                WpfVerifyAccess();
                int next=Math.Max(0,Math.Min(2,value));
                if(avoidZonesMode==next)return;
                avoidZonesMode=next;
                SyncRoadRouterAvoidZones();
                routeGeneration++;activeRoute=null;lastRouteTarget=null;
                if(searchTarget!=null&&position!=null) { if(routeCalculating)wpfQueuedTarget=searchTarget; else UpdateRouteAsync(ToMap(position),searchTarget); }
                SettingsChanged();SaveSettings();
            }
        }
        static bool IsDangerZone(MapZone z) {
            if(z==null)return false;
            string combined=((z.Name??"")+" "+(z.Layer??"")).ToLowerInvariant();
            if(combined.Contains("danger")||combined.Contains("pvp")||combined.Contains("rad")||combined.Contains("kill")||combined.Contains("enemy")||combined.Contains("опасн")||combined.Contains("радиац")||combined.Contains("красн"))return true;
            var c=Color.FromArgb(z.Argb);
            return c.R>=180&&c.G<=90&&c.B<=90;
        }
        internal int wpfAvoidRevision;
        void SyncRoadRouterAvoidZones() {
            wpfAvoidRevision++;
            SquadRoutePlanner.ClearLegCache();
            if(avoidZonesMode<=0) { RoadRouter.Instance.SetAvoidZones(null); return; }
            var polys=WpfAllZones().Where(z=>WpfZoneVisible(z)&&WpfCustomZone(z)&&z.Points!=null&&z.Points.Length>=3&&(avoidZonesMode==1||IsDangerZone(z))).Select(z=>z.Points);
            RoadRouter.Instance.SetAvoidZones(polys,avoidZonesMode==2?6.0:4.0);
        }
        float fullMapTargetZoom=1.0f;
        bool fullMapZoomAnimating;
        internal bool WpfHasPendingTacticalZoom { get { return fullMapZoomAnimating&&Math.Abs(fullMapTargetZoom-fullMapZoom)>Math.Max(0.004f,fullMapTargetZoom*0.002f); } }
        internal void WpfSetTacticalZoomLocked(bool locked) { WpfVerifyAccess();if(WpfTacticalZoomLocked==locked)return;WpfTacticalZoomLocked=locked;SaveSettings(); }
        internal void WpfSetTacticalOpacity(int opacity) { WpfVerifyAccess();fullMapOpacity=Math.Max(0,Math.Min(100,opacity));WpfCameraChanged(); }
        internal bool WpfStepTacticalZoom(double dtSeconds) {
            WpfVerifyAccess();
            if(!fullMapZoomAnimating)return false;
            double eps=Math.Max(0.004,fullMapTargetZoom*0.002);
            if(Math.Abs(fullMapTargetZoom-fullMapZoom)<=eps||fullMapZoom<=0.01f||fullMapTargetZoom<=0.01f) {
                fullMapZoom=fullMapTargetZoom;fullMapZoomAnimating=false;WpfCameraChanged();return true;
            }
            // Logarithmic (multiplicative) interpolation keeps visual zoom speed constant across 1x..32x.
            double alpha=1.0-Math.Exp(-18.0*Math.Max(0.001,Math.Min(0.1,dtSeconds)));
            fullMapZoom=(float)(fullMapZoom*Math.Pow(fullMapTargetZoom/(double)fullMapZoom,alpha));
            if(Math.Abs(fullMapTargetZoom-fullMapZoom)<=eps) { fullMapZoom=fullMapTargetZoom;fullMapZoomAnimating=false; }
            WpfCameraChanged();return true;
        }
        internal void WpfRotate(bool enabled) { wpfRotateMap=enabled;SaveSettings(); }
        internal void WpfPoiClustering(bool enabled) { wpfPoiClustering=enabled;SaveSettings(); }
        internal void WpfDefaultColours() { routeGuidanceColor=OverlayTheme.Route;playerConeColor=OverlayTheme.Accent;routeHistoryColor=Color.FromArgb(164,177,188);SettingsChanged();SaveSettings(); }
        readonly HashSet<string> disabledWpfZones=new HashSet<string>(StringComparer.Ordinal);
        sealed class ZoneKeyCache { public ZoneKeyCache() {}internal string Name,Layer,Key;internal PointF[] Points;internal PointF Centre; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MapZone,ZoneKeyCache> zoneKeys=new System.Runtime.CompilerServices.ConditionalWeakTable<MapZone,ZoneKeyCache>();
        internal int wpfZoneRevision;
        int cachedZoneRevision=-1,cachedZoneCount=-1;
        List<MapZone> cachedZoneList;
        MapZone[] cachedVisibleZones;
        WpfZoneOption[] cachedZoneOptions;
        int cachedTrailRevision=-1;
        int cachedBindingsRevision=-1;
        AppControlBindings cachedAppBindings;
        PointF[] cachedTrail;
        List<MapZone> appFolderZones=new List<MapZone>();
        MapZone[] cachedAllZones = Array.Empty<MapZone>();
        IEnumerable<MapZone> WpfAllZones() {
            var keys=new HashSet<string>(StringComparer.Ordinal);
            if(zones!=null) for(int i=0;i<zones.Count;i++) { var z=zones[i]; if(z!=null&&keys.Add(WpfZoneKey(z))) yield return z; }
            if(appFolderZones!=null) for(int i=0;i<appFolderZones.Count;i++) { var z=appFolderZones[i]; if(z!=null&&keys.Add(WpfZoneKey(z))) yield return z; }
        }
        internal void WpfRefreshFolderZones(List<MapZone> value) {
            // Downloaded packs use the default SCUM map coordinates.
            appFolderZones=mapSidecarPaths.MapId!="default"?new List<MapZone>():value;
            SettingsChanged();
        }
        void WpfRefreshZoneCache() {
            int zCount=zones==null?0:zones.Count;
            if(cachedZoneRevision==wpfZoneRevision&&cachedZoneList==zones&&cachedZoneCount==zCount)return;
            int estCap=zCount+(appFolderZones==null?0:appFolderZones.Count);
            var allList=new List<MapZone>(estCap);
            var visList=new List<MapZone>(estCap);
            var optList=new List<WpfZoneOption>(estCap);
            var seenKeys=new HashSet<string>(StringComparer.Ordinal);
            Action<List<MapZone>> scan=src=> {
                if(src==null) return;
                for(int i=0;i<src.Count;i++) {
                    var z=src[i];
                    if(z==null) continue;
                    string key=WpfZoneKey(z);
                    if(!seenKeys.Add(key)) continue;
                    allList.Add(z);
                    bool isCustom=WpfCustomZone(z);
                    string layer=string.IsNullOrWhiteSpace(z.Layer)?"Default":z.Layer;
                    bool enabled=!disabledWpfZones.Contains(key);
                    bool layerEnabled=!disabledZoneLayers.Contains(layer);
                    if(!isCustom||(layerEnabled&&enabled)) visList.Add(z);
                    if(isCustom) optList.Add(new WpfZoneOption { Zone=z,Key=key,Layer=layer,Enabled=enabled,LayerEnabled=layerEnabled });
                }
            };
            scan(zones); scan(appFolderZones);
            cachedAllZones=allList.Count==0?Array.Empty<MapZone>():allList.ToArray();
            bool visSame=cachedVisibleZones!=null&&cachedVisibleZones.Length==visList.Count;
            if(visSame) for(int i=0;i<visList.Count;i++) if(!object.ReferenceEquals(cachedVisibleZones[i],visList[i])) { visSame=false; break; }
            if(!visSame) cachedVisibleZones=visList.Count==0?Array.Empty<MapZone>():visList.ToArray();
            bool optSame=cachedZoneOptions!=null&&cachedZoneOptions.Length==optList.Count;
            if(optSame) for(int i=0;i<optList.Count;i++) { var a=cachedZoneOptions[i]; var b=optList[i]; if(a.Zone!=b.Zone||a.Key!=b.Key||a.Enabled!=b.Enabled||a.LayerEnabled!=b.LayerEnabled) { optSame=false; break; } }
            if(!optSame) cachedZoneOptions=optList.Count==0?Array.Empty<WpfZoneOption>():optList.ToArray();
            cachedZoneRevision=wpfZoneRevision;cachedZoneList=zones;cachedZoneCount=zCount;
            SyncRoadRouterAvoidZones();
        }
        internal static bool WpfCustomZone(MapZone zone) {
            return zone!=null&&zone.Points!=null&&zone.Points.Length>0&&!zone.IsDeathMarker&&(zone.Points.Length>1||(!string.IsNullOrWhiteSpace(zone.Layer)&&zone.Layer!="Default"&&zone.Layer!="Custom Waypoints"));
        }
        internal static string WpfZoneKey(MapZone zone) {
            var cached=zoneKeys.GetOrCreateValue(zone);if(cached.Key!=null&&cached.Name==zone.Name&&cached.Layer==zone.Layer&&cached.Points==zone.Points)return cached.Key;
            var pts=zone.Points;
            var sb=new System.Text.StringBuilder((zone.Layer==null?0:zone.Layer.Length)+(zone.Name==null?0:zone.Name.Length)+(pts==null?0:pts.Length*24)+8);
            sb.Append(zone.Layer).Append('\n').Append(zone.Name).Append('\n');
            if(pts!=null) {
                for(int i=0;i<pts.Length;i++) {
                    if(i>0) sb.Append(';');
                    sb.Append(pts[i].X.ToString("R",System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(pts[i].Y.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            cached.Key=Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString())));
            cached.Name=zone.Name;cached.Layer=zone.Layer;cached.Points=pts;cached.Centre=zone.Centroid;return cached.Key;
        }
        internal static PointF WpfZoneCentre(MapZone zone) {
            var cached=zoneKeys.GetOrCreateValue(zone);if(cached.Key==null||cached.Name!=zone.Name||cached.Layer!=zone.Layer||cached.Points!=zone.Points)WpfZoneKey(zone);
            if(cached.Centre==PointF.Empty&&zone.Points.Length>0)cached.Centre=zone.Centroid;return cached.Centre;
        }
        internal void WpfZoneToggle(string key,bool enabled) {
            if(enabled) { disabledWpfZones.Remove(key);var zone=WpfAllZones().FirstOrDefault(z=>WpfCustomZone(z)&&WpfZoneKey(z)==key);if(zone!=null)disabledZoneLayers.Remove(string.IsNullOrWhiteSpace(zone.Layer)?"Default":zone.Layer);showZones=true; }
            else disabledWpfZones.Add(key);SettingsChanged();SaveSettings();
        }
        internal void WpfZoneLayerToggle(string layer,bool enabled) { if(enabled)disabledZoneLayers.Remove(layer);else disabledZoneLayers.Add(layer);if(enabled)showZones=true;SettingsChanged();SaveSettings(); }
        internal void WpfExpandZones() { sidebarZonesExpanded=!sidebarZonesExpanded;SettingsChanged();SaveSettings(); }
        internal void WpfInitializeDefaults() {
            if(scumMap!=null)foreach(var category in scumMap.Categories)category.DefaultEnabled=true;
            if(wpfDefaultsRevision>=3)return;
            if(wpfDefaultsRevision<1) { showScumMap=true;if(scumMap!=null) { scumMap.MasterEnabled=true;scumMap.SetAllEnabled(true); }fullMapOpacity=85; }
            if(wpfDefaultsRevision<3&&fullMapOpacity==0) fullMapOpacity=85;
            if(labelSize==6)labelSize=16;
            wpfDefaultsRevision=3;SettingsChanged();SaveSettings();
        }
        void WpfCameraChanged() { WpfVerifyAccess();terrainKey=null;lastFrameKey=null;saveAfter=DateTime.UtcNow.AddMilliseconds(700); }
        internal void WpfUpdateCamera(WpfMapState state,bool tactical) {
            WpfVerifyAccess();if(tactical&&wpfFollowPlayer&&position!=null)fullMapPan=ToMap(position);
            state.Zoom=tactical?fullMapZoom:zoom;state.Opacity=tactical?WpfTacticalOpacityValue:mapOpacity;state.Pan=fullMapPan;
        }
        internal int WpfSettingsRevision { get;private set; }
        internal void WpfRebuildSettings() { WpfSettingsRevision++; }
        internal void WpfStopEngineTimer() { timer.Stop(); }
        bool wpfFollowPlayer=true;
        internal bool WpfFollowing { get { return wpfFollowPlayer; } }
        internal void WpfFollow() { wpfFollowPlayer=true; }
        internal string WpfTrackingStatus() {
            if(musicalScumbagOpen)return Localization.Get("FieldPaused")+" — MusicalScumbag";
            if(AdminPlayerWorkPaused)return Localization.Get("FieldPaused")+" — "+Localization.Get("PlayersTitle");
            return note;
        }
        internal bool WpfZoneVisible(MapZone zone) {
            return zone!=null&&(!WpfCustomZone(zone)||(!disabledZoneLayers.Contains(string.IsNullOrWhiteSpace(zone.Layer)?"Default":zone.Layer)&&!disabledWpfZones.Contains(WpfZoneKey(zone))));
        }
        int cachedSharedMarkersRev=-1,cachedSharedMarkersZoneCount=-1;MapZone cachedSharedTarget,cachedSharedUnshared;string cachedSharedLang;SharedMapMarker[] cachedSharedMarkersArr;
        internal SharedMapMarker[] WpfSharedMarkers() {
            int zCount=zones==null?0:zones.Count; string lang=Localization.CurrentCode;
            if(cachedSharedMarkersArr!=null&&cachedSharedMarkersRev==wpfZoneRevision&&cachedSharedMarkersZoneCount==zCount&&object.ReferenceEquals(cachedSharedTarget,searchTarget)&&object.ReferenceEquals(cachedSharedUnshared,wpfUnsharedTarget)&&cachedSharedLang==lang) return cachedSharedMarkersArr;
            var markers=new List<SharedMapMarker>();
            if(searchTarget!=null&&!object.ReferenceEquals(searchTarget,wpfUnsharedTarget)&&searchTarget.Points!=null&&searchTarget.Points.Length>0) {
                var point=searchTarget.Centroid;string name=searchTarget.DisplayName??"";if(name.Length>96)name=name.Substring(0,96);
                var gps=new SharedMapMarker { Id=SharedMapMarker.GpsId,Name=name,X=point.X,Y=point.Y };
                if(SharedMapMarker.Valid(gps))markers.Add(gps);
            }
            if(zones!=null&&zones.Count>0) {
                var customPts=new List<MapZone>(Math.Min(128,zones.Count));
                for(int zi=0;zi<zones.Count;zi++) { var z=zones[zi]; if(z!=null&&z.Category==ZoneCategory.Custom&&z.Points!=null&&z.Points.Length==1) customPts.Add(z); }
                if(customPts.Count>1) {
                    customPts.Sort((a,b)=> {
                        int dc=b.IsDeathMarker.CompareTo(a.IsDeathMarker); if(dc!=0) return dc;
                        return (b.DeathCreatedUtc??DateTime.MinValue).CompareTo(a.DeathCreatedUtc??DateTime.MinValue);
                    });
                }
                int take=Math.Min(128,customPts.Count);
                string deathLbl=Localization.Get("DeathName");
                for(int zi=0;zi<take&&markers.Count<128;zi++) {
                    var z=customPts[zi];
                    string mName=(z.IsDeathMarker?deathLbl:z.DisplayName)??"";
                    if(mName.Length>96) mName=mName.Substring(0,96);
                    var mk=new SharedMapMarker { Id=Convert.ToHexString(Convert.FromBase64String(WpfZoneKey(z))),Name=mName,Death=z.IsDeathMarker,X=z.Points[0].X,Y=z.Points[0].Y };
                    if(SharedMapMarker.Valid(mk)) markers.Add(mk);
                }
            }
            cachedSharedMarkersRev=wpfZoneRevision;cachedSharedMarkersZoneCount=zCount;cachedSharedTarget=searchTarget;cachedSharedUnshared=wpfUnsharedTarget;cachedSharedLang=lang;
            cachedSharedMarkersArr=markers.Count==0?Array.Empty<SharedMapMarker>():markers.ToArray();
            return cachedSharedMarkersArr;
        }
        static string WpfLocationHeader(string desc) {
            if(string.IsNullOrEmpty(desc)) return "";
            int sep=desc.IndexOf("  |  -> ",StringComparison.Ordinal);
            return sep>=0?desc.Substring(0,sep):desc;
        }
        AppControlBindings CachedAppControls() {
            if(cachedAppBindings==null||cachedBindingsRevision!=controlBindingRevision) { cachedAppBindings=CurrentAppControls(); cachedBindingsRevision=controlBindingRevision; }
            return cachedAppBindings;
        }
        internal WpfMapState WpfSnapshot(bool tactical) {
            WpfVerifyAccess();
            WpfRefreshZoneCache();
            if(cachedTrailRevision!=locationHistoryRevision) { int tc=locationHistoryPoints.Count; var arr=tc==0?Array.Empty<PointF>():new PointF[tc]; for(int ti=0;ti<tc;ti++) arr[ti]=locationHistoryPoints[ti].MapPoint; cachedTrail=arr; cachedTrailRevision=locationHistoryRevision; }
            if(tactical&&wpfFollowPlayer&&position!=null)fullMapPan=ToMap(position);
            return new WpfMapState { Position=position,Updated=updated,Target=searchTarget,Route=activeRoute,Calculating=routeCalculating,
                RotateMap=wpfRotateMap,PoiClustering=wpfPoiClustering,ZoneRevision=wpfZoneRevision,CustomZonesExpanded=sidebarZonesExpanded,CustomZones=cachedZoneOptions,
                Grid=gridBorders,GridLabels=gridLabels,GridOpacity=gridOpacity,LabelSize=labelSize,SmartLabels=smartLabelLod,Zones=showZones,Waypoints=showCustomWaypoints,History=locationHistory,Labels=showZoneLabels,Tracking=TrackingEnabled,
                Heading=showHeading,Compass=showCompass,Status=showStatus,StatusAbove=statusPos=="Above",HudWidth=HudBounds.Width,HudHeight=HudBounds.Height,HudLeft=HudBounds.Left,HudTop=HudBounds.Top,PlayerColor=playerConeColor.ToArgb(),RouteColor=routeGuidanceColor.ToArgb(),Store=scumMap,Saved=cachedVisibleZones,Trail=cachedTrail,
                HistoryColor=routeHistoryColor.ToArgb(),TrackingStatus=WpfTrackingStatus(),TrackingPaused=IntensiveWorkPaused||chat.Paused||inventoryInputLocked,
                Location=position==null?WpfTrackingStatus():WpfLocationHeader(GetLocationDescription()),
                Shape=overlayShape,RouteSummary=FieldConsoleRouteSummary(),Opacity=tactical?WpfTacticalOpacityValue:mapOpacity,Zoom=tactical?fullMapZoom:zoom,Pan=fullMapPan,
                MapKey=scumMapKey,MapScan=scumMapScanCode,ChatKey=scumChatKey,CopyKey=scumCopyKey,CopyModifier=scumCopyModifierKey,Bindings=CachedAppControls() };
        }
        internal bool WpfCanPing(bool fromSquadTools) {
            WpfVerifyAccess();
            // An explicit ping may use a fresh captured position while this
            // dialog protects game input. It never resumes coordinate capture.
            return (!shortcutCaptureOpen||fromSquadTools&&panelOpening)&&!IntensiveWorkPaused&&!diagnosticMode&&!closing&&!adminCommandBusy&&!chat.Paused&&!inventoryInputLocked&&position!=null&&PrivatePositions.Fresh(updated,DateTime.UtcNow);
        }
        internal List<MapZone> WpfSearch(string text) {
            PointF? origin=position==null?(PointF?)null:ToMap(position);var matches=DestinationSearch.Find(WpfAllZones().ToList(),text,origin);
            if(scumMap!=null)matches.AddRange(DestinationSearch.FindScumMap(scumMap,text,origin,100));
            var grid=GridTarget(text);if(grid!=null)matches.Insert(0,grid);return matches;
        }
        internal MapZone WpfClickedTarget(PointF point,float hitRadius) {
            WpfRefreshZoneCache();
            var allZones=cachedAllZones;
            string name=null;bool mapObject=false;
            double hitR2=(double)hitRadius*hitRadius;
            double bestDist2=hitR2;
            MapZone bestSaved=null;
            for(int i=0;i<allZones.Length;i++) {
                var z=allZones[i];
                if(!WpfCustomZone(z)||z.Points==null||z.Points.Length==0||z.Points.Length>=3) continue;
                double dx=z.Points[0].X-point.X, dy=z.Points[0].Y-point.Y;
                double d2=dx*dx+dy*dy;
                if(d2<=bestDist2) { bestDist2=d2; bestSaved=z; }
            }
            if(bestSaved!=null) { name=bestSaved.Name; mapObject=bestSaved.IsMapObject; }
            if(string.IsNullOrWhiteSpace(name)&&scumMap!=null) {
                var markers=scumMap.Markers;
                double bestM2=hitR2;
                ScumMapMarker bestMarker=null;
                for(int i=0;i<markers.Count;i++) {
                    var m=markers[i];
                    double dx=m.X-point.X, dy=m.Y-point.Y;
                    double d2=dx*dx+dy*dy;
                    if(d2<=bestM2) { bestM2=d2; bestMarker=m; }
                }
                if(bestMarker!=null) { name=scumMap.GetMarkerDisplayName(bestMarker); mapObject=true; }
            }
            if(string.IsNullOrWhiteSpace(name)&&zones!=null) {
                double bestArea=double.MaxValue;
                MapZone bestAreaZone=null;
                for(int i=0;i<allZones.Length;i++) {
                    var z=allZones[i];
                    var pts=z.Points;
                    if(pts==null||pts.Length<=2||string.IsNullOrWhiteSpace(z.Name)) continue;
                    float minX=pts[0].X,maxX=minX,minY=pts[0].Y,maxY=minY;
                    for(int p=1;p<pts.Length;p++) {
                        float px=pts[p].X, py=pts[p].Y;
                        if(px<minX) minX=px; else if(px>maxX) maxX=px;
                        if(py<minY) minY=py; else if(py>maxY) maxY=py;
                    }
                    if(point.X<minX||point.X>maxX||point.Y<minY||point.Y>maxY) continue;
                    double area=(double)(maxX-minX)*(maxY-minY);
                    if(area<bestArea&&z.Contains(point)) { bestArea=area; bestAreaZone=z; }
                }
                if(bestAreaZone!=null) { name=bestAreaZone.Name; mapObject=bestAreaZone.IsMapObject; }
            }
            if(string.IsNullOrWhiteSpace(name)&&scumMap!=null) {
                var towns=scumMap.Settlements;
                double bestT2=double.MaxValue;
                string bestTownName=null;
                for(int i=0;i<towns.Count;i++) {
                    var t=towns[i];
                    double dx=t.X-point.X, dy=t.Y-point.Y;
                    double d2=dx*dx+dy*dy;
                    if(d2<=(double)t.Radius*t.Radius&&d2<bestT2) { bestT2=d2; bestTownName=t.Name; }
                }
                if(bestTownName!=null) { name=bestTownName; mapObject=true; }
            }
            if(string.IsNullOrWhiteSpace(name)) name=DestinationSearch.Sector(point);
            return new MapZone { Name=name,IsMapObject=mapObject,Points=new[]{point} };
        }
        MapZone wpfQueuedTarget;
        MapZone wpfUnsharedTarget;
        internal MapZone WpfCurrentTarget { get { return searchTarget; } }
        internal void WpfSquadTarget(MapZone target) { WpfVerifyAccess();wpfUnsharedTarget=target;WpfTarget(target); }
        internal void WpfTarget(MapZone target) { WpfVerifyAccess();searchTarget=target;pin=null;activeRoute=null;routeGeneration++;lastRouteTarget=null;lastFrameKey=null;saveAfter=DateTime.UtcNow.AddMilliseconds(700);if(position!=null) { if(routeCalculating)wpfQueuedTarget=target;else UpdateRouteAsync(ToMap(position),target); } }
        internal void WpfClear() { WpfVerifyAccess();ClearActiveWaypoint(); }
        internal void WpfModal(bool open) { panelOpening=open;shortcutCaptureOpen=open;Native.CancelActiveCopy();fallbackKeysArmed=false; }
        internal bool WpfSettingsKeyMatches(int key,int scan) { return MatchesAppControl(AppControlAction.Settings,key,scan); }
        internal void WpfLayer(string key,bool value) {
            switch(key) { case "zones":showZones=value;break;case "waypoints":showCustomWaypoints=value;break;case "grid":gridBorders=value;break;case "history":locationHistory=value;break;case "labels":showZoneLabels=value;break; }
            SettingsChanged();
        }
        internal void WpfAppearance(string shape,int opacity) { overlayShape=shape=="Circle"?"Circle":"Square";mapOpacity=Math.Max(30,Math.Min(100,opacity));SettingsChanged();SaveSettings(); }
        string wpfOnboardingVersion="";
        string eulaAcceptedVersion="";
        internal string WpfEulaAcceptedVersion { get { return eulaAcceptedVersion; } }
        internal bool WpfNeedsEulaAcceptance {
            get {
                if(EulaAgreement.IsVersionAccepted(eulaAcceptedVersion)) return false;
                if(!diagnosticMode && EulaAgreement.IsAcceptedInInstallerRegistry()) {
                    eulaAcceptedVersion=EulaAgreement.CurrentVersion;
                    saveAfter=DateTime.UtcNow.AddMilliseconds(500);
                    return false;
                }
                return true;
            }
        }
        internal bool WpfAcceptEula() {
            string previous=eulaAcceptedVersion;
            eulaAcceptedVersion=EulaAgreement.CurrentVersion;
            if(SaveSettings()) return true;
            eulaAcceptedVersion=previous;
            return false;
        }
        bool hideOnboardingGuide;
        internal bool WpfHideOnboardingGuide { get { return hideOnboardingGuide; } }
        internal void WpfSetHideOnboardingGuide(bool hide) { hideOnboardingGuide=hide;SaveSettings(); }
        static string MajorMinorVersion(string version) {
            if(string.IsNullOrWhiteSpace(version)) return "";
            var parts=version.Trim().Split('.');
            return parts.Length>=2 ? parts[0]+"."+parts[1] : version.Trim();
        }
        internal bool WpfNeedsOnboarding {
            get {
                if(isFirstLaunch || string.IsNullOrWhiteSpace(wpfOnboardingVersion)) return true;
                return !string.Equals(MajorMinorVersion(wpfOnboardingVersion),MajorMinorVersion(VersionString),StringComparison.Ordinal);
            }
        }
        internal bool WpfCompleteOnboarding() { string previous=wpfOnboardingVersion;bool first=isFirstLaunch;wpfOnboardingVersion=VersionString;isFirstLaunch=false;if(SaveSettings())return true;wpfOnboardingVersion=previous;isFirstLaunch=first;return false; }
        internal void WpfPreviewPosition(string value) { if(!diagnosticMode)throw new InvalidOperationException("Preview requires diagnostic mode.");Accept(value);zoom=targetZoom=4f;autoZoom=false; }
        internal void WpfTactical(bool active) { fullMapActive=active;if(!active){fullMapZoomAnimating=false;fullMapTargetZoom=fullMapZoom;}Native.CancelActiveCopy();inputGeneration++;fallbackKeysArmed=false; }
        internal void WpfZoom(bool tactical,int direction) {
            WpfVerifyAccess();
            if(tactical&&WpfTacticalZoomLocked)return;
            float pct=tactical?Math.Max(25,zoomStepPercent):zoomStepPercent;
            float step=1f+pct/100f;
            if(tactical) {
                float baseZoom=fullMapZoomAnimating?fullMapTargetZoom:fullMapZoom;
                float nextTarget=Math.Max(1,Math.Min(maxZoom,baseZoom*(float)Math.Pow(step,direction)));
                if(diagnosticMode) {
                    fullMapZoom=fullMapTargetZoom=nextTarget;fullMapZoomAnimating=false;
                } else {
                    fullMapTargetZoom=nextTarget;
                    fullMapZoom=(float)(fullMapZoom*Math.Pow(fullMapTargetZoom/Math.Max(0.01,(double)fullMapZoom),0.28));
                    fullMapZoomAnimating=Math.Abs(fullMapTargetZoom-fullMapZoom)>Math.Max(0.004f,fullMapTargetZoom*0.002f);
                    if(!fullMapZoomAnimating) fullMapZoom=fullMapTargetZoom;
                }
            } else { autoZoom=false;zoom=Math.Max(1,Math.Min(maxZoom,zoom*(float)Math.Pow(step,direction)));targetZoom=zoom; }
            WpfCameraChanged();
        }
        internal void WpfPan(PointF point) { WpfVerifyAccess();wpfFollowPlayer=false;fullMapPan=point;WpfCameraChanged(); }
        internal string WpfBind(AppControlAction action,int key,int scan) {
            string error=AppControlBindingError(action,key,scan);if(error!=null)return error;
            var bindings=CurrentAppControls();bindings.Set(action,key,scan);ApplyAppControls(bindings);return null;
        }
        internal void WpfSingleCopyKey(bool single) { Native.CancelActiveCopy();scumCopyModifierKey=single?0:0x11;inputGeneration++;SettingsChanged();SaveSettings(); }
        internal string WpfGameBind(int index,int key,int scan) {
            if(key<=0||key>=256||key==0x1B||key==0x0D||key==0x09||PhysicalKeyCapture.IsModifierKey(key))return Localization.Get("KeyWizardNonModifier");
            int[] gameKeys={scumMapKey,scumChatKey,scumCopyKey},scans={scumMapScanCode,scumChatScanCode,scumCopyScanCode};gameKeys[index]=key;scans[index]=scan;
            if(PhysicalKeyCapture.HasConflicts(gameKeys,scans))return Localization.Get("KeyWizardDifferentKeys");
            string error=CurrentAppControls().BindingError(gameKeys[0],scans[0],gameKeys[1],scans[1],gameKeys[2],scans[2]);if(error!=null)return error;
            scumMapKey=gameKeys[0];scumChatKey=gameKeys[1];scumCopyKey=gameKeys[2];scumMapScanCode=scans[0];scumChatScanCode=scans[1];scumCopyScanCode=scans[2];inputGeneration++;SettingsChanged();SaveSettings();return null;
        }
        internal bool WpfManualMap;
        internal Rectangle WpfMapBounds;
        internal void WpfPlaceMap(bool manual,Rectangle bounds) { WpfVerifyAccess();WpfManualMap=manual;WpfMapBounds=bounds;SaveSettings(); }
        internal bool WpfSave() { return SaveSettings(); }
        internal void WpfScheduleSave() { WpfVerifyAccess();saveAfter=DateTime.UtcNow.AddMilliseconds(700); }
        internal void WpfMoveHud(int left,int top) { SetHudLocation(new Point(left,top));SettingsChanged();SaveSettings(); }
        internal bool WpfDeleteWaypoint(MapZone marker) {
            if(marker==null||zones==null||!zones.Contains(marker)||marker.Points==null||marker.Points.Length!=1)return false;
            if(marker.IsDeathMarker)return RemoveDeathMarker(marker);
            var candidate=new List<MapZone>(zones);candidate.Remove(marker);
            if(!TryCommitZones(candidate,UiDialogOwner))return false;
            if(searchTarget==marker)WpfClear();SettingsChanged();return true;
        }
        internal ZoneEditor WpfZoneEditor() {
            if(zonesLoadFailed)throw new InvalidOperationException(Localization.Get("NoteZonesLoadFail"));
            var waypoints=zones.Where(IsCustomWaypointZone).ToList();
            return new ZoneEditor(map,zones.Where(z=>!IsCustomWaypointZone(z)).ToList(),zonesPath,value=> {
                zones=value.Concat(waypoints).ToList();
                try { if(File.Exists(zonesPath))lastZonesFileWriteTimeUtc=File.GetLastWriteTimeUtc(zonesPath); }catch(IOException) { }
                SettingsChanged();
            },waypoints) { WpfMode=true };
        }
        internal bool WpfWaypoint(string name,PointF? point) {
            WpfVerifyAccess();
            if(string.IsNullOrWhiteSpace(name)||!point.HasValue&&position==null)return false;PointF destination=point.HasValue?point.Value:ToMap(position);
            var next=new List<MapZone>(zones??new List<MapZone>());next.Add(new MapZone { Name=name,Category=ZoneCategory.Custom,Argb=OverlayTheme.Route.ToArgb(),Points=new[]{destination},Layer="Custom Waypoints" });
            if(!TryCommitZones(next,UiDialogOwner))return false;
            SettingsChanged();return true;
        }
        internal void WpfNativeTool(AppControlAction action) {
            switch(action) { case AppControlAction.DeathMarker:TriggerDeathMarker();break;case AppControlAction.ZoneEditor:OpenZoneImport(null);break;case AppControlAction.AdminCommands:ShowAdminCommands();break;case AppControlAction.AdminPlayers:ShowAdminPlayers();break;case AppControlAction.ResetZoom:fullMapZoom=fullMapTargetZoom=zoom=targetZoom=1;fullMapZoomAnimating=false;SettingsChanged();break; }
        }
        internal void WpfExit() { ExitApp(); }
        internal void WpfPrepareExit() {
            WpfVerifyAccess();if(closing)return;
            closing=true;wpfAdminControlsRequested=false;adminControlsCommandReturn=false;
            Native.CancelActiveCopy();timer.Stop();SaveSettings();
            foreach(var tool in new System.Windows.Forms.Form[]{adminCommandsPanel,adminPlayersPanel,spawnCartDialog}.Concat(adminProfileWindows).ToArray())
                if(tool!=null&&!tool.IsDisposed)tool.Close();
        }

        // Direct Pure-WPF Settings API (v0.6.0 Step 1 — bypasses hidden WinForms SettingsPanel)
        internal void WpfSetLanguage(AppLanguage lang) {
            WpfVerifyAccess();
            if(Localization.Current==lang)return;
            Localization.Current=lang;
            BuildTrayMenu();
            SettingsChanged();
            SaveSettings();
        }
        internal bool WpfAutomaticDeathMarkers { get { return automaticDeathMarkers; } set { WpfVerifyAccess();SetAutomaticDeathMarkers(value);SettingsChanged(); } }
        internal int WpfHudWidth { get { return HudBounds.Width; } set { WpfVerifyAccess();SetHudSize(new Size(value,HudBounds.Height));SettingsChanged(); } }
        internal int WpfHudHeight { get { return HudBounds.Height; } set { WpfVerifyAccess();SetHudSize(new Size(HudBounds.Width,value));SettingsChanged(); } }
        internal int WpfMapOpacity { get { return mapOpacity; } set { WpfVerifyAccess();mapOpacity=Math.Max(30,Math.Min(100,value));SettingsChanged(); } }
        internal bool WpfStatusAbove { get { return statusPos=="Above"; } set { WpfVerifyAccess();statusPos=value?"Above":"Below";SettingsChanged(); } }
        internal bool WpfShowStatus { get { return showStatus; } set { WpfVerifyAccess();showStatus=value;SettingsChanged(); } }
        internal bool WpfShowHeading { get { return showHeading; } set { WpfVerifyAccess();showHeading=value;SettingsChanged(); } }
        internal bool WpfShowCompass { get { return showCompass; } set { WpfVerifyAccess();showCompass=value;SettingsChanged(); } }
        internal bool WpfZoneChime { get { return zoneChime; } set { WpfVerifyAccess();zoneChime=value;SettingsChanged(); } }

        internal bool WpfGridLabels { get { return gridLabels; } set { WpfVerifyAccess();gridLabels=value;SettingsChanged(); } }
        internal bool WpfGridBorders { get { return gridBorders; } set { WpfVerifyAccess();gridBorders=value;SettingsChanged(); } }
        internal int WpfGridOpacity { get { return gridOpacity; } set { WpfVerifyAccess();gridOpacity=Math.Max(0,Math.Min(100,value));SettingsChanged(); } }
        internal bool WpfShowScumMap { get { return showScumMap; } set { WpfVerifyAccess();showScumMap=value;if(scumMap!=null)scumMap.MasterEnabled=value;SettingsChanged(); } }
        internal bool WpfShowZones { get { return showZones; } set { WpfVerifyAccess();showZones=value;SettingsChanged(); } }
        internal bool WpfShowZoneLabels { get { return showZoneLabels; } set { WpfVerifyAccess();showZoneLabels=value;SettingsChanged(); } }
        internal bool WpfSmartLabelLod { get { return smartLabelLod; } set { WpfVerifyAccess();smartLabelLod=value;SettingsChanged(); } }
        internal int WpfLabelSize { get { return labelSize; } set { WpfVerifyAccess();labelSize=Math.Max(6,Math.Min(24,value));SettingsChanged(); } }

        internal bool WpfLocationHistory { get { return locationHistory; } set { WpfVerifyAccess();locationHistory=value;SettingsChanged(); } }
        internal bool WpfLocationHistoryTimestamps { get { return locationHistoryTimestamps; } set { WpfVerifyAccess();locationHistoryTimestamps=value;SettingsChanged(); } }
        internal int WpfLocationHistoryMinutes { get { return locationHistoryMinutes; } set { WpfVerifyAccess();locationHistoryMinutes=Math.Max(1,Math.Min(240,value));PruneLocationHistory(DateTime.UtcNow);SettingsChanged(); } }
        internal void WpfClearLocationHistory() { WpfVerifyAccess();ClearLocationHistory(); }
        internal bool WpfShowCustomWaypoints { get { return showCustomWaypoints; } set { WpfVerifyAccess();showCustomWaypoints=value;SettingsChanged(); } }
        internal Color WpfRouteColor { get { return routeGuidanceColor; } }
        internal Color WpfPlayerColor { get { return playerConeColor; } }
        internal Color WpfHistoryColor { get { return routeHistoryColor; } }
        internal void WpfPickRouteColor() { WpfVerifyAccess();ChangeRouteColor(); }
        internal void WpfPickPlayerColor() { WpfVerifyAccess();ChangePlayerColor(); }
        internal void WpfPickHistoryColor() { WpfVerifyAccess();ChangeHistoryColor(); }

        internal int WpfCustomZoneCount { get { return zones==null?0:zones.Count; } }
        internal bool WpfClearAllZones() {
            WpfVerifyAccess();
            if(!TryCommitZones(new List<MapZone>(),UiDialogOwner))return false;
            searchTarget=null;activeRoute=null;routeGeneration++;lastRouteTarget=null;
            SettingsChanged();
            return true;
        }

        internal bool WpfAutoZoom { get { return autoZoom; } set { WpfVerifyAccess();autoZoom=value;SettingsChanged(); } }
        internal int WpfAutoZoomMin { get { return autoZoomMin; } set { WpfVerifyAccess();autoZoomMin=Math.Max(1,Math.Min(32,value));if(autoZoomMax<autoZoomMin)autoZoomMax=autoZoomMin;SettingsChanged(); } }
        internal int WpfAutoZoomMax { get { return autoZoomMax; } set { WpfVerifyAccess();autoZoomMax=Math.Max(autoZoomMin,Math.Min(32,value));SettingsChanged(); } }
        internal int WpfMaxZoom { get { return maxZoom; } set { WpfVerifyAccess();maxZoom=Math.Max(4,Math.Min(32,value));zoom=Math.Min(maxZoom,zoom);targetZoom=Math.Min(maxZoom,targetZoom);SettingsChanged(); } }
        internal int WpfZoomStepPercent { get { return zoomStepPercent; } set { WpfVerifyAccess();zoomStepPercent=Math.Max(10,Math.Min(100,value));SettingsChanged(); } }
        internal int WpfCopyIntervalMin { get { return Program.TrackingIntervalMs(0,scumCopyModifierKey); } }
        internal int WpfCopyIntervalMs { get { return Program.TrackingIntervalMs(copyIntervalMs,scumCopyModifierKey); } set { WpfVerifyAccess();copyIntervalMs=Math.Max(WpfCopyIntervalMin,Math.Min(10000,value));next=DateTime.UtcNow;SettingsChanged(); } }

        internal bool WpfVoiceEnabled { get { return voiceEnabled; } set { WpfVerifyAccess();voiceEnabled=value;SettingsChanged(); } }
        internal string WpfVoiceName { get { return voiceName; } set { WpfVerifyAccess();voiceName=value;SettingsChanged(); } }
        internal int WpfVoiceVolume { get { return voiceVolume; } set { WpfVerifyAccess();voiceVolume=value;SettingsChanged(); } }
        internal int WpfVoiceOutputDevice { get { return voiceOutputDevice; } set { WpfVerifyAccess();voiceOutputDevice=value;SettingsChanged(); } }
        internal string WpfVoiceRoot { get { return VoiceRoot; } }
        internal string[] WpfDiscoverVoices() { WpfVerifyAccess();Voice.InvalidateCache();return Voice.DiscoverVoices(); }
        internal void WpfVoicePreview() { WpfVerifyAccess();Voice.Preview(); }

        internal void WpfCheckUpdates() { WpfVerifyAccess();CheckForUpdates(true); }
        internal UpdateRelease WpfReadyUpdateRelease { get { return ReadyUpdateRelease; } }
        internal bool WpfApplyReadyUpdate(bool relaunch=true) { WpfVerifyAccess(); return ApplyReadyUpdate(relaunch); }
        Position cachedRouteSumPos; MapZone cachedRouteSumTarget; RoadRoute cachedRouteSumRoute;
        bool cachedRouteSumCalc,cachedRouteSumLoaded,cachedRouteSumAir; TransportProfile cachedRouteSumProfile;
        int cachedRouteSumSpeedKey; string cachedRouteSumLang,cachedRouteSumText="";
        string FieldConsoleRouteSummary() {
            if(searchTarget==null) return "";
            if(position==null) return Localization.Get("WaitingForCoordinates");
            bool isAir=WpfMapSurface.AviationModeEnabled;
            bool roadsLoaded=RoadRouter.Instance.IsLoaded;
            var profile=TransportEtaService.ActiveProfile;
            int spdKey=(int)Math.Round((double.IsNaN(filteredSpeed)||double.IsInfinity(filteredSpeed)?0.0:filteredSpeed)*2.0);
            string lang=Localization.CurrentCode;
            if(cachedRouteSumText!=null && object.ReferenceEquals(cachedRouteSumPos,position)
                && object.ReferenceEquals(cachedRouteSumTarget,searchTarget) && object.ReferenceEquals(cachedRouteSumRoute,activeRoute)
                && cachedRouteSumCalc==routeCalculating && cachedRouteSumLoaded==roadsLoaded && cachedRouteSumAir==isAir
                && cachedRouteSumProfile==profile && cachedRouteSumSpeedKey==spdKey && cachedRouteSumLang==lang) {
                return cachedRouteSumText;
            }
            PointF curMap=ToMap(position);
            bool isWater=!isAir&&RoadRouter.Instance.IsWaterAt(curMap);
            bool road=!isAir&&!isWater&&activeRoute!=null&&activeRoute.Success;
            bool loadingRoads=!isAir&&!isWater&&!roadsLoaded;
            double distance=road?RoadRouter.RemainingDistanceMeters(curMap,activeRoute):DestinationSearch.Metres(curMap,searchTarget.Centroid);
            double? estSec=road?(double?)(activeRoute.TotalDistanceMeters>1.0?activeRoute.EstimatedSeconds*Math.Min(1.25,distance/activeRoute.TotalDistanceMeters):activeRoute.EstimatedSeconds):null;
            string summary=WpfTravelSummary(distance,road,routeCalculating,estSec,loadingRoads,isWater);
            cachedRouteSumPos=position; cachedRouteSumTarget=searchTarget; cachedRouteSumRoute=activeRoute;
            cachedRouteSumCalc=routeCalculating; cachedRouteSumLoaded=roadsLoaded; cachedRouteSumAir=isAir;
            cachedRouteSumProfile=profile; cachedRouteSumSpeedKey=spdKey; cachedRouteSumLang=lang; cachedRouteSumText=summary;
            return summary;
        }
        internal string WpfTravelSummary(double distance,bool road,bool calculating=false,double? estimatedSeconds=null,bool loadingRoads=false,bool isWater=false) {
            bool isAir=WpfMapSurface.AviationModeEnabled;
            TransportProfile effectiveProfile;
            int minutes=TransportEtaService.ComputeEtaMinutes(distance,filteredSpeed,road,estimatedSeconds,isAir,isWater,TransportEtaService.ActiveProfile,out effectiveProfile);
            string suffix=loadingRoads
                ? (" · "+(Localization.Current==AppLanguage.Russian?"Загрузка дорог…":"Loading roads…"))
                : (calculating?" · "+Localization.Get("FieldCalculating"):(road||isAir||isWater)?"":" · "+Localization.Get("FieldNoRoad"));
            string icon=TransportEtaService.ProfileIcon(effectiveProfile);
            string modeTag=road?(Localization.Get("RoadSuffix")+(icon.Length>0?" "+icon:"")):(isAir?" ✈":(isWater?" ⛵":(icon.Length>0?" "+icon:"")+" ("+Localization.Get("FieldDirect")+")"));
            return DestinationSearch.Distance(distance)+modeTag+" · "+Localization.Get("NavigationEta")+" "+Localization.T("FieldMinutes",minutes)+suffix;
        }
        internal List<MapZone> WpfCustomWaypoints() {
            var list=new List<MapZone>();
            if(zones==null) return list;
            for(int i=0;i<zones.Count;i++) {
                var z=zones[i];
                if(z!=null&&!z.IsDeathMarker&&z.Category==ZoneCategory.Custom&&z.Points!=null&&z.Points.Length==1) list.Add(z);
            }
            return list;
        }
        internal int WpfImportWaypointPack(WaypointPackResult pack) {
            WpfVerifyAccess();
            if(pack==null||pack.Items==null||pack.Items.Length==0) return 0;
            var next=new List<MapZone>(zones??new List<MapZone>());
            int added=0;
            string layerName=string.IsNullOrWhiteSpace(pack.PackName)?"Custom Waypoints":pack.PackName;
            for(int i=0;i<pack.Items.Length;i++) {
                var item=pack.Items[i];
                var pt=new PointF((float)item.X,(float)item.Y);
                bool dup=false;
                for(int j=0;j<next.Count;j++) {
                    var ex=next[j];
                    if(ex!=null&&ex.Points!=null&&ex.Points.Length==1&&string.Equals(ex.Name,item.Name,StringComparison.OrdinalIgnoreCase)&&DestinationSearch.Metres(ex.Points[0],pt)<10.0) { dup=true; break; }
                }
                if(dup) continue;
                next.Add(new MapZone { Name=item.Name,Category=ZoneCategory.Custom,Argb=item.Argb,Points=new[]{pt},Layer=layerName });
                added++;
            }
            if(added==0) return 0;
            if(!TryCommitZones(next,UiDialogOwner)) return 0;
            SettingsChanged();
            return added;
        }
        internal string WpfDataFolder { get { return dataFolder; } }
    }
}
