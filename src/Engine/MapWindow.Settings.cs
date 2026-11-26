using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZiloreMiniMap {

    public sealed partial class MapWindow {
        bool SettingsVisible { get { return false; } }
        static bool ValidShortcutKey(int key) { return key==0 || AppControlBindings.ValidKey(key); }
        void InvalidateFullMapSidebar() { fullMapSidebarRevision++; }



        void SettingsChanged() { wpfZoneRevision++;terrainKey=null; lastFrameKey=null; cachedLocationAnchors=null; locationDescriptionRevision++; InvalidateFullMapSidebar(); saveAfter=DateTime.UtcNow.AddMilliseconds(700); }



        static bool IsCustomWaypointZone(MapZone zone) {



            // Saved custom zones are polygons; saved waypoints are the cyan, single-point



            // entries created by Insert or the full-map context menu.



            return zone!=null && zone.Points!=null && zone.Points.Length==1;



        }







        static int ReadCopyInterval(int value,bool legacy) {



            // Legacy settings store seconds; current settings allow 250 ms tracking.



            if(legacy) return value<=1?1000:Math.Min(10,value)*1000;



            return Math.Max(Program.MinimumCopyIntervalMs,Math.Min(10000,value));



        }



        void LoadSettings() {



            try {



                if(!File.Exists(settingsPath)) {



                    isFirstLaunch = true;



                    Localization.DetectSystemLanguage();



                    SaveSettings();



                    return;



                }



                // Legacy settings contain only a virtual key. Never reuse a scan code from a previous load.
                scumCopyScanCode=0;
                scumMapScanCode=0; scumChatScanCode=0;
                appControls=new AppControlBindings();
                settingsShortcutKey=0x24; pinShortcutKey=0x2D; searchShortcutKey=0x2E;
                settingsShortcutScanCode=0; pinShortcutScanCode=0; searchShortcutScanCode=0;
                string[] lines;



                try { lines=File.ReadAllLines(settingsPath); }



                catch(IOException) { return; }



                catch(UnauthorizedAccessException) { return; }



                bool foundWelcomed=false, foundLanguage=false, foundTrackingRevision=false;



                int width=savedWidth,height=savedHeight,left=savedLeft,top=savedTop;



                foreach(string line in lines) {



                    try {



                        if(string.IsNullOrEmpty(line)) continue;



                        int eqIdx=line.IndexOf('=');



                        if(eqIdx<1 || eqIdx>=line.Length-1) continue;



                        string key=line.Substring(0,eqIdx).Trim();



                        string val=line.Substring(eqIdx+1).Trim();



                        if(string.IsNullOrEmpty(key) || string.IsNullOrEmpty(val)) continue;



                        int n; bool b; float z;



                        if((key=="TrackingRevision" && val=="1") || (Program.IsTestBuild && key=="TestTrackingRevision" && val=="2")) { foundTrackingRevision=true; continue; }
                        if(key=="VoiceEnabled") { bool enabled; if(bool.TryParse(val,out enabled)) voiceEnabled=enabled; continue; }
                        if(key=="VoiceName") { if(val==Path.GetFileName(val)) voiceName=val; continue; }
                        if(key=="VoiceVolume") { int volume; if(int.TryParse(val,out volume)) voiceVolume=Math.Max(0,Math.Min(100,volume)); continue; }
                        if(key=="VoiceOutputDevice") { int dev; if(int.TryParse(val,out dev)) voiceOutputDevice=Math.Max(-1,dev); continue; }
                        if(key=="SuppressCopyKeyReminder") { if(bool.TryParse(val,out b)) suppressCopyKeyReminder=b; continue; }
                        if(key=="Welcomed") { foundWelcomed=true; continue; }
                        if(key=="OnboardingVersion") { wpfOnboardingVersion=val;continue; }
                        if(key=="EulaAcceptedVersion") { eulaAcceptedVersion=val;continue; }
                        if(key=="HideOnboardingGuide") { if(bool.TryParse(val,out b))hideOnboardingGuide=b;continue; }
                        if(key=="RouteMode") { RoadRouteMode mode;if(Enum.TryParse(val,out mode)&&Enum.IsDefined<RoadRouteMode>(mode))wpfRouteMode=mode;continue; }
                        if(key=="ManualMapPlacement") { if(bool.TryParse(val,out b))WpfManualMap=b;continue; }
                        if(key=="TacticalZoomLocked") { if(bool.TryParse(val,out b))WpfTacticalZoomLocked=b;continue; }
                        if(key=="AvoidCustomZones") { if(bool.TryParse(val,out b)&&avoidZonesMode==0)avoidZonesMode=b?1:0;continue; }
                        if(key=="AvoidZonesMode") { int am;if(int.TryParse(val,out am))avoidZonesMode=Math.Max(0,Math.Min(2,am));continue; }
                        if(key=="MapPlacementBounds") { var parts=val.Split(',');int x,y,w,h;if(parts.Length==4&&int.TryParse(parts[0],out x)&&int.TryParse(parts[1],out y)&&int.TryParse(parts[2],out w)&&int.TryParse(parts[3],out h)&&x>=-100000&&x<=100000&&y>=-100000&&y<=100000&&w>=640&&w<=32768&&h>=420&&h<=32768)WpfMapBounds=new Rectangle(x,y,w,h);continue; }
                        if(key=="RotateMinimap") { if(bool.TryParse(val,out b))wpfRotateMap=b;continue; }
                        if(key=="UiTheme") { wpfUiTheme=val;continue; }
                        if(key=="PoiClustering") { if(bool.TryParse(val,out b))wpfPoiClustering=b;continue; }
                        if(key=="AdminToolsEnabled") { if(bool.TryParse(val,out b))adminToolsEnabled=b;continue; }
                        if(key=="AdminPanelDismissed") { adminPanelDismissedForFullMap=false;continue; }



                        if(key=="Language") { Localization.SetLanguage(val); foundLanguage=true; continue; }



                        if(bool.TryParse(val,out b)) {
                            if(key=="AutomaticDeathMarkers") automaticDeathMarkers=b;



                            switch(key) { case "LocationHistory":locationHistory=b;break;case "LocationHistoryTimestamps":locationHistoryTimestamps=b;break;case "GridLabels":gridLabels=b;break;case "GridBorders":gridBorders=b;break;case "ShowStatus":showStatus=b;break;case "ShowZones":showZones=b;break;case "AutoZoom":autoZoom=b;break;case "ShowHeading":showHeading=b;break;case "ShowCompass":showCompass=b;break;case "ZoneChime":zoneChime=b;break;case "ShowCustomWaypoints":showCustomWaypoints=b;break;case "ShowScumMap":showScumMap=b;break;case "ShowZoneLabels":showZoneLabels=b;break;case "SmartLabelLod":smartLabelLod=b;break;case "SidebarWildlifeExpanded":sidebarWildlifeExpanded=b;break;case "SidebarZonesExpanded":sidebarZonesExpanded=b;break; }
                        }
                        if(key=="DisabledZoneLayers") {
                            disabledZoneLayers.Clear();
                            if(!string.IsNullOrWhiteSpace(val)) {
                                foreach(string dl in val.Split(new[]{',',';'}, StringSplitOptions.RemoveEmptyEntries)) {
                                    string trimmed = dl.Trim();
                                    if(!string.IsNullOrEmpty(trimmed)) disabledZoneLayers.Add(trimmed);
                                }
                            }
                        }
                        if(key=="DisabledWpfZones") { disabledWpfZones.Clear();foreach(string id in val.Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries))disabledWpfZones.Add(id); }
                        if(key=="ScumMapDisabledCats") {
                            if(scumMap!=null) scumMap.ApplyDisabledCategoriesString(val);
                            else pendingDisabledCats=val;
                        }



                        if(key=="StatusPos") { statusPos=val=="Above"?"Above":"Below"; }



                        if(key=="Shape") { overlayShape=val=="Circle"?"Circle":"Square"; }
                        if(key=="PlayerColor" || key=="PlayerConeColor") {
                            try {
                                if(val.StartsWith('#')) playerConeColor = ColorTranslator.FromHtml(val);
                                else if(int.TryParse(val, out n)) playerConeColor = Color.FromArgb(n);
                            } catch {}
                            continue;
                        }
                        if(key=="RouteColor") {
                            try {
                                if(val.StartsWith('#')) routeGuidanceColor = ColorTranslator.FromHtml(val);
                                else if(int.TryParse(val, out n)) routeGuidanceColor = Color.FromArgb(n);
                            } catch {}
                            continue;
                        }
                        if(key=="RouteHistoryColor") {
                            try { if(val.StartsWith('#'))routeHistoryColor=ColorTranslator.FromHtml(val);else if(int.TryParse(val,out n))routeHistoryColor=Color.FromArgb(n); }catch {}
                            continue;
                        }
                        if(key=="WpfDefaultsRevision"&&int.TryParse(val,out n)) { wpfDefaultsRevision=n;continue; }



                        if(int.TryParse(val,out n)) {
                            foreach(var action in AppControlBindings.AllActions) {
                                string prefix="AppControl"+action;
                                if(key==prefix+"Key" || key==prefix+"ScanCode") {
                                    var loadedControl=CurrentAppControls();
                                    loadedControl.Set(action,key==prefix+"Key"?n:AppControlKey(action),
                                        key==prefix+"ScanCode"?n:AppControlScanCode(action));
                                    StoreAppControls(loadedControl);
                                }
                            }

                            if(key=="SettingsShortcutKey" && ValidShortcutKey(n)) settingsShortcutKey=n;
                            if(key=="PinShortcutKey" && ValidShortcutKey(n)) pinShortcutKey=n;
                            if(key=="SearchShortcutKey" && ValidShortcutKey(n)) searchShortcutKey=n;
                            if(key=="SettingsShortcutScanCode" && PhysicalKeyCapture.Valid(n)) settingsShortcutScanCode=n;
                            if(key=="PinShortcutScanCode" && PhysicalKeyCapture.Valid(n)) pinShortcutScanCode=n;
                            if(key=="SearchShortcutScanCode" && PhysicalKeyCapture.Valid(n)) searchShortcutScanCode=n;
                            if(key=="ScumCopyScanCode" && PhysicalKeyCapture.Valid(n)) scumCopyScanCode=n;
                            if(key=="ScumMapScanCode" && PhysicalKeyCapture.Valid(n)) scumMapScanCode=n;
                            if(key=="ScumChatScanCode" && PhysicalKeyCapture.Valid(n)) scumChatScanCode=n;



                            switch(key) { case "LocationHistoryMinutes":locationHistoryMinutes=Math.Max(1,Math.Min(240,n));break;case "Width":width=Math.Max(240,Math.Min(800,n));break;case "Height":height=Math.Max(240,Math.Min(800,n));break;case "Left":left=n;break;case "Top":top=n;break;case "Opacity":mapOpacity=Math.Max(30,Math.Min(100,n));break;case "FullMapOpacity":fullMapOpacity=Math.Max(0,Math.Min(100,n));break;case "GridOpacity":gridOpacity=Math.Max(0,Math.Min(100,n));break;case "LabelSize":labelSize=Math.Max(6,Math.Min(24,n));break;case "MaxZoom":maxZoom=Math.Max(4,Math.Min(32,n));break;case "AutoZoomMin":autoZoomMin=Math.Max(1,Math.Min(32,n));break;case "AutoZoomMax":autoZoomMax=Math.Max(1,Math.Min(32,n));break;case "ZoomStep":zoomStepPercent=Math.Max(10,Math.Min(100,n));break;case "CopyInterval":copyIntervalMs=ReadCopyInterval(n,true);break;case "CopyIntervalMs":copyIntervalMs=ReadCopyInterval(n,false);break;case "ScumMapKey":if(n>0&&n<256)scumMapKey=n;break;case "ScumChatKey":if(n>0&&n<256)scumChatKey=n;break;case "ScumCopyModifierKey":if(n==0||PhysicalKeyCapture.IsModifierKey(n))scumCopyModifierKey=n;break;case "ScumCopyKey":if(n>0&&n<256&&!PhysicalKeyCapture.IsModifierKey(n))scumCopyKey=n;break; }



                        }



                        if(key=="Zoom" && float.TryParse(val,NumberStyles.Float,CultureInfo.InvariantCulture,out z) && !float.IsNaN(z) && !float.IsInfinity(z)) { zoom=Math.Max(1,Math.Min(maxZoom,z)); targetZoom=zoom; }



                    } catch { /* skip malformed line */ }



                }



                // Upgrade the old one-second default once; retain slower custom intervals.
                copyIntervalMs=Program.UpgradeCopyInterval(copyIntervalMs,foundTrackingRevision);
                if(!foundWelcomed) isFirstLaunch=true;



                if(!foundLanguage) Localization.DetectSystemLanguage();



                if(PhysicalKeyCapture.SameBinding(scumMapKey,scumMapScanCode,scumChatKey,scumChatScanCode)) {



                    // A malformed or hand-edited config must not make the map key permanently



                    // indistinguishable from the chat key.



                    scumMapKey=0x4D;



                    scumChatKey=0x54;
                    scumMapScanCode=0; scumChatScanCode=0;



                }



                if(scumCopyModifierKey!=0 && !PhysicalKeyCapture.IsModifierKey(scumCopyModifierKey)) scumCopyModifierKey=Program.DefaultCopyModifierKey;



                if(scumCopyKey<=0 || scumCopyKey>=256 || PhysicalKeyCapture.IsModifierKey(scumCopyKey)) scumCopyKey=Program.DefaultCopyKey;
                if(!PhysicalKeyCapture.Valid(scumCopyScanCode)) scumCopyScanCode=0;
                if(!PhysicalKeyCapture.Valid(scumMapScanCode)) scumMapScanCode=0;
                if(!PhysicalKeyCapture.Valid(scumChatScanCode)) scumChatScanCode=0;
                if(!PhysicalKeyCapture.Valid(settingsShortcutScanCode)) settingsShortcutScanCode=0;
                if(!PhysicalKeyCapture.Valid(pinShortcutScanCode)) pinShortcutScanCode=0;
                if(!PhysicalKeyCapture.Valid(searchShortcutScanCode)) searchShortcutScanCode=0;
                var loadedBindings=CurrentAppControls();
                loadedBindings.Sanitize(scumMapKey,scumMapScanCode,scumChatKey,scumChatScanCode,scumCopyKey,scumCopyScanCode);
                StoreAppControls(loadedBindings);
                controlBindingRevision++;
                fallbackKeysArmed=false;
                autoZoomMax=Math.Max(autoZoomMin,autoZoomMax);
                zoom=Math.Max(1,Math.Min(maxZoom,zoom)); targetZoom=zoom;
                savedWidth=width; savedHeight=height; savedLeft=left; savedTop=top;



            } catch { /* catch-all: proceed with defaults on any unexpected error */ }



        }



        static void TryDeleteFile(string path) {
            try {
                if(!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            } catch(IOException) { } catch(UnauthorizedAccessException) { }
        }

        static void CommitTempFile(string tempPath,string targetPath,string backupPath) {
            if(File.Exists(targetPath)) File.Replace(tempPath,targetPath,backupPath);
            else File.Move(tempPath,targetPath);
        }

        readonly object settingsIoGate=new object();
        int settingsBgSavePending;
        List<string> BuildSettingsSnapshot() {
            Rectangle minimapBounds=HudBounds;
            var settingsLines=new List<string>(new string[] {
                    "Welcomed=True",
                    "TrackingRevision=1",
                    "LocationHistory="+locationHistory,
                    "LocationHistoryTimestamps="+locationHistoryTimestamps,
                    "LocationHistoryMinutes="+locationHistoryMinutes,
                    "SuppressCopyKeyReminder="+suppressCopyKeyReminder,
                    "VoiceEnabled="+voiceEnabled,
                    "VoiceName="+voiceName,
                    "VoiceVolume="+voiceVolume,
                    "VoiceOutputDevice="+voiceOutputDevice,
                    "Language="+Localization.CurrentCode,
                    "GridLabels="+gridLabels,
                    "GridBorders="+gridBorders,
                    "GridOpacity="+gridOpacity,
                    "ShowZones="+showZones,
                    "LabelSize="+labelSize,
                    "Shape="+overlayShape,
                    "ShowHeading="+showHeading,
                    "ShowCompass="+showCompass,
                    "ZoneChime="+zoneChime,
                    "CopyIntervalMs="+copyIntervalMs,
                    "AutoZoom="+autoZoom,
                    "AutoZoomMin="+autoZoomMin,
                    "AutoZoomMax="+autoZoomMax,
                    "ShowStatus="+showStatus,
                    "StatusPos="+statusPos,
                    "Opacity="+mapOpacity,
                    "FullMapOpacity="+fullMapOpacity,
                    "Width="+minimapBounds.Width,
                    "Height="+minimapBounds.Height,
                    "Left="+minimapBounds.Left,
                    "Top="+minimapBounds.Top,
                    "Zoom="+zoom.ToString(CultureInfo.InvariantCulture),
                    "MaxZoom="+maxZoom,
                    "ZoomStep="+zoomStepPercent,
                    "ScumMapKey="+scumMapKey,
                    "ScumChatKey="+scumChatKey,
                    "ScumCopyModifierKey="+scumCopyModifierKey,
                    "ScumCopyKey="+scumCopyKey,
                    "ShowCustomWaypoints="+showCustomWaypoints,
                    "ShowScumMap="+showScumMap,
                    "ScumMapDisabledCats="+(scumMap!=null?scumMap.GetDisabledCategoriesString():""),
                    "ShowZoneLabels="+showZoneLabels,
                    "SmartLabelLod="+smartLabelLod,
                    "SidebarWildlifeExpanded="+sidebarWildlifeExpanded,
                    "SidebarZonesExpanded="+sidebarZonesExpanded,
                    "DisabledZoneLayers="+string.Join(";",disabledZoneLayers),
                    "RouteColor="+ColorTranslator.ToHtml(routeGuidanceColor),
                    "PlayerColor="+ColorTranslator.ToHtml(playerConeColor),
                    "ScumCopyScanCode="+scumCopyScanCode,
                    "ScumMapScanCode="+scumMapScanCode,
                    "ScumChatScanCode="+scumChatScanCode,
                    "AutomaticDeathMarkers="+automaticDeathMarkers,
                    "RouteMode="+wpfRouteMode,
                    "ManualMapPlacement="+WpfManualMap,
                    "TacticalZoomLocked="+WpfTacticalZoomLocked,
                    "AvoidZonesMode="+avoidZonesMode,
                    "MapPlacementBounds="+string.Join(",",new[]{WpfMapBounds.X,WpfMapBounds.Y,WpfMapBounds.Width,WpfMapBounds.Height}),
                    "RotateMinimap="+wpfRotateMap,
                    "UiTheme="+wpfUiTheme,
                    "OnboardingVersion="+wpfOnboardingVersion,
                    "EulaAcceptedVersion="+eulaAcceptedVersion,
                    "HideOnboardingGuide="+hideOnboardingGuide,
                    "PoiClustering="+wpfPoiClustering,
                    "AdminToolsEnabled="+adminToolsEnabled,
                    "AdminPanelDismissed="+adminPanelDismissedForFullMap,
                    "WpfDefaultsRevision="+wpfDefaultsRevision,
                    "RouteHistoryColor="+ColorTranslator.ToHtml(routeHistoryColor),
                    "SettingsShortcutKey="+settingsShortcutKey,
                    "PinShortcutKey="+pinShortcutKey,
                    "SearchShortcutKey="+searchShortcutKey,
                    "SettingsShortcutScanCode="+settingsShortcutScanCode,
                    "PinShortcutScanCode="+pinShortcutScanCode,
                    "SearchShortcutScanCode="+searchShortcutScanCode,
                    "DisabledWpfZones="+string.Join(";",disabledWpfZones)
                });

                foreach(var action in AppControlBindings.AllActions) {
                    // Preserve legacy keys for the first three actions; new actions have stable names.
                    if(action==AppControlAction.Settings || action==AppControlAction.AddWaypoint || action==AppControlAction.Search) continue;
                    settingsLines.Add("AppControl"+action+"Key="+AppControlKey(action));
                    settingsLines.Add("AppControl"+action+"ScanCode="+AppControlScanCode(action));
                }

            return settingsLines;
        }

        bool WriteSettingsSnapshot(List<string> settingsLines) {
            lock(settingsIoGate) {
                string settingsTmp=settingsPath+".tmp";
                string settingsBak=settingsPath+".bak";
                string versionPath=settingsPath+".version";
                string versionTmp=versionPath+".tmp";
                string versionBak=versionPath+".bak";
                bool settingsExisted=File.Exists(settingsPath);
                bool settingsCommitted=false;
                try {
                    File.WriteAllLines(settingsTmp,settingsLines);
                    File.WriteAllText(versionTmp,VersionString);
                    CommitTempFile(settingsTmp,settingsPath,settingsBak);
                    settingsCommitted=true;
                    CommitTempFile(versionTmp,versionPath,versionBak);
                    return true;
                } catch(IOException ex) {
                    Program.LogWarn("Settings", "SaveSettings I/O error: " + ex.Message);
                    if(settingsCommitted) {
                        try {
                            if(settingsExisted && File.Exists(settingsBak)) File.Replace(settingsBak,settingsPath,null);
                            else if(!settingsExisted) TryDeleteFile(settingsPath);
                        } catch(IOException) { } catch(UnauthorizedAccessException) { }
                    }
                    note=Localization.Get("NoteSettingsSaveFail");
                    return false;
                } catch(UnauthorizedAccessException) {
                    if(settingsCommitted) {
                        try {
                            if(settingsExisted && File.Exists(settingsBak)) File.Replace(settingsBak,settingsPath,null);
                            else if(!settingsExisted) TryDeleteFile(settingsPath);
                        } catch(IOException) { } catch(UnauthorizedAccessException) { }
                    }
                    note=Localization.Get("NoteSettingsSaveFail");
                    return false;
                } finally {
                    TryDeleteFile(settingsTmp);
                    TryDeleteFile(versionTmp);
                }
            }
        }

        bool SaveSettings() {
            if(diagnosticMode) return true;
            saveAfter=DateTime.MaxValue;
            return WriteSettingsSnapshot(BuildSettingsSnapshot());
        }

        void SaveSettingsAsync() {
            if(diagnosticMode) return;
            saveAfter=DateTime.MaxValue;
            var snapshot=BuildSettingsSnapshot();
            if(Interlocked.Exchange(ref settingsBgSavePending,1)!=0) {
                saveAfter=DateTime.UtcNow.AddMilliseconds(400);
                return;
            }
            ThreadPool.QueueUserWorkItem(_=> {
                try { _ = WriteSettingsSnapshot(snapshot); }
                finally { Interlocked.Exchange(ref settingsBgSavePending,0); }
            });
        }



        bool panelOpening;
        void ShowSettings() {
            if(WpfFieldConsoleRuntime.Active!=null) WpfFieldConsoleRuntime.Active.Handle(AppControlAction.Settings);
        }
    }
}
