using System;

using System.Diagnostics;

using System.Drawing;

using System.Globalization;

using System.IO;

using System.Reflection;

using System.Runtime.InteropServices;

using System.Threading;

using System.Text.RegularExpressions;

using System.Windows.Forms;

using System.Threading.Tasks;

using System.Drawing.Imaging;

using System.Collections.Generic;

using System.Linq;

using System.Drawing.Drawing2D;

[assembly: AssemblyTitle("ZiloreMiniMap")]

[assembly: AssemblyDescription("Tactical Real-Time Overlay for SCUM")]

[assembly: AssemblyCompany("Zilore")]

[assembly: AssemblyProduct("ZiloreMiniMap")]

[assembly: AssemblyCopyright("Copyright © 2026 Zilore")]

[assembly: AssemblyVersion("0.0.4.0")]

[assembly: AssemblyFileVersion("0.0.4.0")]

[assembly: AssemblyInformationalVersion("0.0.4")]

namespace ZiloreMiniMap {

    public sealed partial class MapWindow : IDisposable {

        readonly System.Windows.Threading.Dispatcher uiDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        readonly System.Windows.Threading.DispatcherTimer timer;
        readonly Icon appIcon;
        internal event EventHandler Disposed;
        internal bool IsDisposed { get { return resourcesReleased; } }
        internal bool IsHandleCreated { get { return !resourcesReleased && !uiDispatcher.HasShutdownStarted; } }
        internal bool InvokeRequired { get { return !uiDispatcher.CheckAccess(); } }
        internal void BeginInvoke(Action action) {
            if (action == null || resourcesReleased || uiDispatcher.HasShutdownStarted) return;
            uiDispatcher.BeginInvoke(action);
        }
        internal void Close() { Dispose(); }
        void SetTimerIntervalMs(int ms) {
            var span = TimeSpan.FromMilliseconds(ms);
            if (timer.Interval != span) timer.Interval = span;
        }
        internal IntPtr ResolveClipboardOwnerHandle(Form panel = null) {
            if (panel != null && !panel.IsDisposed && panel.IsHandleCreated) return panel.Handle;
            return WpfFieldConsoleRuntime.Active != null ? WpfFieldConsoleRuntime.Active.PrimaryWindowHandle : IntPtr.Zero;
        }

        bool TrackingEnabled { get { return !IntensiveWorkPaused && !diagnosticMode && !closing && !adminCommandBusy && !shortcutCaptureOpen; } }

        readonly MapMotion motion=new MapMotion();

        readonly Stopwatch motionClock=Stopwatch.StartNew();

        bool fixedCentreReady;

        string terrainKey { get; set; }

        double filteredSpeed;

        Image lazyMap;
        MapTilePyramid mapTiles;
        bool legacyMapLoaded;
        Image map { get { EnsureLegacyMapLoaded(); return lazyMap; } }
        void EnsureLegacyMapLoaded() {
            if(legacyMapLoaded) return;
            legacyMapLoaded=true;
            string mapFile=Path.Combine(dataFolder,"map.png");
            Stream tileResource=InstallationAssets.Open("map-tiles.bin");
            if(tileResource==null && diagnosticMode) {
                string localTiles=Path.Combine(dataFolder,"map-tiles.bin");
                if(File.Exists(localTiles)) tileResource=File.OpenRead(localTiles);
            }
            if(tileResource!=null) {
                if(File.Exists(mapFile)) {
                    try { using(var file=File.OpenRead(mapFile)) lazyMap=SafeMapImage.Decode(file); }
                    catch(Exception ex) { Program.LogWarn("LegacyMap", "Custom map fallback: " + ex.Message); }
                }
                if(lazyMap==null) { mapTiles=new MapTilePyramid(tileResource); lazyMap=mapTiles.Overview(); }
                else tileResource.Dispose();
            } else {
                string warn;
                lazyMap=SafeMapImage.Load(mapFile,()=>System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("map.png"),out warn);
            }
        }

        // Keep sector lines opt-in. They are useful for grid searches but obscure the
        // terrain at normal zoom, especially on the full map and circular minimap.
        bool gridLabels=true,gridBorders=false,showStatus=true,autoZoom=true;

        bool showHeading=true,showCompass=true,zoneChime=false;

        string statusPos="Below";

        string overlayShape="Circle";

        int copyIntervalMs=Program.MinimumCopyIntervalMs;

        int mapOpacity=70;

        int gridOpacity=7;

        bool locationHistory=true,locationHistoryTimestamps=false;
        readonly List<LocationHistoryPoint> locationHistoryPoints=new List<LocationHistoryPoint>();
        int locationHistoryRevision;
        int locationHistoryMinutes=30;
        const int LocationHistoryLimit=14401;
        const int LocationHistoryTimestampIntervalSeconds=30;

        sealed class LocationHistoryPoint {
            internal PointF MapPoint;
            internal DateTime SampledAtUtc;
        }

        int labelSize=16;

        bool showZones=true;

        bool showCustomWaypoints=true;

        bool showZoneLabels=true;

        bool smartLabelLod=true;

        bool fullMapActive;

        float fullMapZoom=1.0f;

        PointF fullMapPan=new PointF(0.5f,0.5f);

        int fullMapOpacity=100;
        readonly bool[] fallbackKeyDown=new bool[256];

        bool fallbackKeysArmed;

        DateTime lastMToggleTime = DateTime.MinValue;

        bool sidebarWildlifeExpanded = true;
        bool sidebarZonesExpanded = false;
        readonly HashSet<string> disabledZoneLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int fullMapSidebarRevision;

        Position pin;

        MapZone searchTarget;

        Color routeGuidanceColor = OverlayTheme.Route;
        Color playerConeColor = Color.FromArgb(229, 57, 53);
        Color routeHistoryColor = Color.FromArgb(164,177,188);
        bool copyInProgress;
        int scumMapKey=0x4D;
        int scumChatKey=0x54;
        int scumMapScanCode,scumChatScanCode;
        int settingsShortcutKey=0x24,pinShortcutKey=0x2D,searchShortcutKey=0x2E;
        int settingsShortcutScanCode,pinShortcutScanCode,searchShortcutScanCode;
        bool shortcutCaptureOpen;
        int scumCopyModifierKey=Program.DefaultCopyModifierKey;
        int scumCopyKey=Program.DefaultCopyKey;
        int scumCopyScanCode;
        RoadRoute activeRoute;
        PointF lastRoutePlayerPt = PointF.Empty;
        MapZone lastRouteTarget;
        bool routeCalculating;
        long routeGeneration;

        List<MapZone> zones=new List<MapZone>();

        ScumMapStore scumMap;

        bool showScumMap=true;

        string pendingDisabledCats="v2;-2008,-2007,-2006,-2005,-2004,-2003,-2002,-2001,-2000,-1014,-1013,-1012,-1011,-1010,-1009,-1008,-1007,-1006,-1005,-1004,-1003,-1002,-1001,-1000,4,5,6,7,9,12,13,14,17,19,20,21,22,23,24,25,28,29,30,32,34,36,37,38,39,40,41,42,43,44,45,47,48,49,50,52,53,54,55,56,57,58,59,60,61,62,63,64,78,81,210,250,251,252,279,320,421,642,643,712,761,762,774,776,777,778,789,790,803,858,859,860,861,862,863,864,865,866,867,868,869,871,872,873,874";

        int savedWidth=240,savedHeight=240,savedLeft=-1,savedTop=-1;

        readonly string dataFolder;

        string zonesPath;
        string customWaypointsPath;
        MapSidecarPaths mapSidecarPaths;
        DateTime lastZonesFileWriteTimeUtc = DateTime.MinValue;
        DateTime lastWaypointsFileWriteTimeUtc = DateTime.MinValue;

        readonly string settingsPath;

        bool isFirstLaunch=false;
        bool suppressCopyKeyReminder=false;

        DateTime saveAfter=DateTime.MaxValue;

        string lastFrameKey { get; set; }

        // The location label is used both to decide whether a frame changed and to draw the HUD.

        // Keep the expensive zone lookup stable while smooth-motion frames are being rendered.

        string cachedLocationDescription;

        Position cachedLocationPosition;

        MapZone cachedLocationTarget;

        RoadRoute cachedLocationRoute;

        int locationDescriptionRevision;

        int cachedLocationDescriptionRevision=-1;

        readonly GameKeys keys;

        readonly ChatState chat=new ChatState();

        readonly TrayController trayController;
        NotifyIcon tray { get { return trayController != null ? trayController.IconControl : null; } }

        bool tickBusy,closing;
        int renderQueued;

        DateTime resumeAfter=DateTime.MinValue;

        Position position;

        PointF? previousMapPoint;

        DateTime previousTime=DateTime.MinValue;

        float targetZoom=1.0f;

                string lastVisitedAnchorName;
        PointF lastVisitedAnchorPoint;
        struct LocationAnchor {
            public string Name;
            public string DisplayName { get { return Zone!=null?Zone.DisplayName:Localization.GetMapObjectName(Name); } }
            public PointF Point;
            public MapZone Zone;
            public double Radius;
            public double DistanceTo(PointF p) {
                if (Zone != null) return Zone.DistanceTo(p);
                double dx = Point.X - p.X, dy = Point.Y - p.Y;
                return Math.Sqrt(dx * dx + dy * dy);
            }
            public bool Contains(PointF p) {
                if (Zone != null) return Zone.Contains(p);
                return DistanceTo(p) <= (Radius > 0 ? Radius : 0.015);
            }
        }

        List<LocationAnchor> cachedLocationAnchors;
        List<LocationAnchor> GetLocationAnchors() {
            if(cachedLocationAnchors != null) return cachedLocationAnchors;
            List<LocationAnchor> list = new List<LocationAnchor>();
            if (zones != null) {
                foreach (MapZone z in zones) {
                    if (z != null && !string.IsNullOrWhiteSpace(z.Name)) {
                        list.Add(new LocationAnchor { Name = z.Name, Point = z.Centroid, Zone = z, Radius = 0 });
                    }
                }
            }
            if (scumMap != null && scumMap.Markers != null) {
                foreach (ScumMapMarker m in scumMap.Markers) {
                    int cid = m.CategoryId;
                    // Settlements, major regional POIs, Outposts, and major Bunkers
                    if (cid == 27 || cid == 26 || cid == 80 || cid == 1 || cid == 456 || cid == 763) {
                        string name = scumMap.GetMarkerDisplayName(m);
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        if (name == "Bunkers" || name == "Bunker") {
                            name = DestinationSearch.Sector(m.Point) + " Bunker";
                        }

                        double rad = 0.015;
                        if (cid == 27) {
                            rad = 0.018;
                        } else if (name.IndexOf("City", StringComparison.OrdinalIgnoreCase) >= 0 || name.Equals("Novigrad", StringComparison.OrdinalIgnoreCase)) {
                            rad = 0.035;
                        } else if (cid == 80) {
                            rad = 0.014;
                        } else if (cid == 1 || cid == 456 || cid == 763) {
                            rad = 0.010;
                        }

                        list.Add(new LocationAnchor { Name = name, Point = m.Point, Zone = null, Radius = rad });
                    }
                }
            }
            cachedLocationAnchors = list;
            return list;
        }

        MapZone lastVisitedZone;

        DateTime lastVisitedTime=DateTime.MinValue;

        DateTime updated=DateTime.MinValue,next=DateTime.MinValue,sent;

        uint sequence;

        bool pending;
        bool clipboardRestoreAllowed;

        IDataObject savedDataObject;
        bool clipboardSnapshotReady;

        float zoom=32f;

        int maxZoom=32;

        int autoZoomMin=5;

        int autoZoomMax=32;

        int zoomStepPercent=10;

        int failures;

        int attempts,responses;

        bool wasGameFocused;
        volatile bool inventoryInputLocked;
        DateTime nextInventoryProbe=DateTime.MinValue;
        int inventoryProbeMisses;

        DateTime lastAltDownTime = DateTime.MinValue;

        readonly string diagnosticPath;

        string lastDiagnostic="";

        string note="";

        readonly bool diagnosticMode;
        readonly bool wpfHostMode;
        internal bool IsWpfHostMode { get { return wpfHostMode; } }
        internal bool HasLegacySettingsUi { get { return false; } }
        internal bool HasLegacyOverlayUi { get { return false; } }
        internal Rectangle HudBounds { get { return new Rectangle(savedLeft, savedTop, savedWidth, savedHeight); } }
        internal void SetHudLocation(Point location) {
            savedLeft = location.X; savedTop = location.Y;
        }
        internal void SetHudSize(Size size) {
            savedWidth = Math.Max(160, Math.Min(800, size.Width));
            savedHeight = Math.Max(160, Math.Min(800, size.Height));
        }

        public MapWindow(string folder,bool diagnostic=false,bool wpfHost=false) {

            timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal, uiDispatcher);
            diagnosticMode=diagnostic;
            wpfHostMode=wpfHost;

            dataFolder=folder;

            diagnosticPath=Path.Combine(folder,"automatic.log");

            settingsPath=Path.Combine(folder,"settings.ini");

            LoadSettings();
            TraceShortcutBindings();

            note=Localization.Get("NoteAutoWaiting");

            try { scumMap=ScumMapStore.Load(dataFolder); } catch { scumMap=new ScumMapStore(); }
            if(scumMap!=null) {
                scumMap.MasterEnabled=showScumMap;
                if(pendingDisabledCats!=null) scumMap.ApplyDisabledCategoriesString(pendingDisabledCats);
            }

            string mapFile=Path.Combine(folder,"map.png");
            bool customMapLoaded=File.Exists(mapFile);
            mapSidecarPaths=MapSidecarPaths.ForMap(dataFolder,customMapLoaded?mapFile:null);
            if(customMapLoaded || (!wpfHostMode && !File.Exists(mapSidecarPaths.ImagePath))) {
                EnsureLegacyMapLoaded();
                if(lazyMap!=null) mapSidecarPaths.EnsureImage(lazyMap);
            }
            zonesPath=mapSidecarPaths.ZonesPath;
            customWaypointsPath=mapSidecarPaths.WaypointsPath;
            try {
                bool migrated;
                zones=MapSidecarStore.Load(mapSidecarPaths,Path.Combine(dataFolder,"zones.tsv"),!customMapLoaded,out migrated);
                if(File.Exists(zonesPath)) lastZonesFileWriteTimeUtc=File.GetLastWriteTimeUtc(zonesPath);
                if(File.Exists(customWaypointsPath)) lastWaypointsFileWriteTimeUtc=File.GetLastWriteTimeUtc(customWaypointsPath);
            } catch(InvalidDataException) { zonesLoadFailed=true; note=Localization.Get("NoteZonesLoadFail"); }
              catch(IOException) { zonesLoadFailed=true; note=Localization.Get("NoteZonesLoadFail"); }
              catch(UnauthorizedAccessException) { zonesLoadFailed=true; note=Localization.Get("NoteZonesLoadFail"); }

            // Legacy GDI+ fullMap pyramid generation removed; WPF uses WpfTerrainTiles directly.

            if(savedLeft>=0 && savedTop>=0) {
                try {
                    Rectangle area=Screen.FromPoint(new Point(savedLeft,savedTop)).WorkingArea;
                    savedLeft=Math.Max(area.Left,Math.Min(area.Right-savedWidth,savedLeft));
                    savedTop=Math.Max(area.Top,Math.Min(area.Bottom-savedHeight,savedTop));
                } catch {}
            } else {
                try {
                    Rectangle area=Screen.PrimaryScreen.WorkingArea;
                    const int margin=20;
                    savedLeft=Math.Max(area.Left,area.Right-savedWidth-margin);
                    savedTop=Math.Max(area.Top,area.Top+margin);
                } catch { savedLeft=48; savedTop=48; }
            }
            // A global low-level hook is not guaranteed to run on this form's UI thread.

            // Marshal it before touching WinForms so a hotkey cannot leave a dialog or

            // overlay in a half-open state after a thread-affinity exception.

            keys=new GameKeys((key,scanCode) => {

                if(closing || IsDisposed || !IsHandleCreated || (shortcutCaptureOpen && !(WpfFieldConsoleRuntime.Active!=null&&WpfFieldConsoleRuntime.Active.IsSettingsToggleKey(key,scanCode)))) {
                    return;
                }

                int bindingRevision=controlBindingRevision;
                long chatRevision=chat.Revision;
                IntPtr origin=Native.GetForegroundWindow();
                try { BeginInvoke((Action)(()=> {
                    TraceShortcutContext("queued",key,scanCode);
                    if(key>0 && key<fallbackKeyDown.Length) fallbackKeyDown[key]=true;
                    if(bindingRevision!=controlBindingRevision || !chat.AllowsQueuedShortcut(chatRevision) || Native.GetForegroundWindow()!=origin || adminCommandBusy) {
                        if(Native.InputTraceActive) Native.TraceInput("shortcut queued rejected vk="+key+" scan="+scanCode+
                            " bindingsChanged="+(bindingRevision!=controlBindingRevision)+" chatChanged="+!chat.AllowsQueuedShortcut(chatRevision)+
                            " focusChanged="+(Native.GetForegroundWindow()!=origin)+" admin="+adminCommandBusy);
                        return;
                    }
                    OnGameKey(key,scanCode);
                })); } catch(InvalidOperationException) { }

            }, IsWatchedPhysicalKey, chat, () => scumChatKey, () => scumCopyModifierKey, () => inventoryInputLocked, () => scumCopyKey, () => scumChatScanCode,
                (key,scan)=>PhysicalKeyCapture.Matches(key,scan,scumMapKey,scumMapScanCode));
            keys.SuppressPhysicalMouse=(message,point,data)=>WpfFieldConsoleRuntime.Active!=null&&WpfFieldConsoleRuntime.Active.SuppressMapMouse(message,point,data);
            keys.OnPhysicalMouse = (message,point) => {
                if(closing || IsDisposed || !IsHandleCreated) return;
                // All engine/UI access belongs on the UI thread, outside the hook.
                try { BeginInvoke(new Action(()=> {
                    if(GameKeys.MouseActionBlocksCopy(message,scumCopyModifierKey))Native.CancelActiveCopy();
                })); } catch(InvalidOperationException) {}
            };

            try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { appIcon = (Icon)SystemIcons.Application.Clone(); }
            trayController = new TrayController(
                uiDispatcher,
                () => resourcesReleased,
                appIcon,
                DisplayVersion + (Program.IsTestBuild ? " TEST" : ""),
                diagnosticMode,
                () => { if (WpfFieldConsoleRuntime.Active != null) WpfFieldConsoleRuntime.Active.ToggleTactical(false); else TriggerFullMap(); },
                () => { if (WpfFieldConsoleRuntime.Active != null) WpfFieldConsoleRuntime.Active.Handle(AppControlAction.Search); },
                ToggleOverlay,
                ShowSettings,
                ShowAdminCommands,
                () => CheckForUpdates(true),
                () => { if (WpfFieldConsoleRuntime.Active != null) WpfFieldConsoleRuntime.Active.ShowAbout(); },
                ExitApp);

            sequence=Native.GetClipboardSequenceNumber();
            wasGameFocused = Native.GameFocused() || Native.IsOurWindow(Native.GetForegroundWindow());
            if (!diagnosticMode) { failures = 0; next = DateTime.UtcNow; }

            try { if(Clipboard.ContainsText()) Accept(Clipboard.GetText()); } catch(ExternalException) {}

            timer.Interval = TimeSpan.FromMilliseconds(50); timer.Tick += Tick; timer.Start();

            System.Threading.ThreadPool.QueueUserWorkItem(_ => {
                RoadRouter.Instance.InitializeFromResource();
                if (!closing && !IsDisposed && IsHandleCreated) {
                    try {
                        BeginInvoke((Action)(() => {
                            if (closing || IsDisposed) return;
                            MapZone pendingTarget = wpfQueuedTarget ?? searchTarget;
                            wpfQueuedTarget = null;
                            locationDescriptionRevision++;
                            lastFrameKey = null;
                            if (pendingTarget != null && position != null && (activeRoute == null || !activeRoute.Success)) {
                                UpdateRouteAsync(ToMap(position), pendingTarget);
                            }
                            RenderOverlay();
                        }));
                    } catch (InvalidOperationException) { }
                }
            });

        }

        void BuildTrayMenu() {
            if (trayController != null) trayController.RebuildMenu();
        }
        internal void WpfRefreshTrayTheme() { if(IsHandleCreated && InvokeRequired) { BeginInvoke(new Action(BuildTrayMenu)); return; } BuildTrayMenu(); }

        static void DisposeChildControls(Control parent) {
            if(parent==null) return;
            for(int i=parent.Controls.Count-1; i>=0; i--) {
                Control child=parent.Controls[i];
                // Control.Dispose owns its descendants. Removing every nested child
                // individually causes repeated native layout work during a rebuild.
                child.Dispose();
            }
        }

        public const string VersionString = "0.0.4";
        public const string SupportEmail = "support@zilore.app";
        public const string GitHubUrl = "https://github.com/Zilore-Anatoliy/ZiloreMiniMap";
        public const string WebsiteUrl = "https://minimap.zilore.app";
        public const string DiscordUrl = "https://discord.gg/xbWXKahvS";
        public const string DeveloperDiscordUrl = "https://discord.com/users/1295520560817897504";
        public static readonly Tuple<string,string>[] WargmServers = new[] {
            Tuple.Create("GRAND [RU PVE, Custom]", "https://wargm.ru/server/78747/votes"),
            Tuple.Create("GRAND LIGHT [PVE, Custom]", "https://wargm.ru/server/79269/votes"),
            Tuple.Create("GRAND LIGHT 2 [PVE]", "https://wargm.ru/server/79915/votes"),
            Tuple.Create("GRAND 2 [PVE, Custom]", "https://wargm.ru/server/79583/votes"),
            Tuple.Create("GRAND 3 ULTRA HARD [RU PVE, Custom]", "https://wargm.ru/server/80071/votes")
        };
        public const string BuildNumber = "20598";
        public const string BuildTimestamp = "2026-11-26 18:27";
        public const string ReleaseNotes = "• Накопительный релиз. · Встроено подробное Руководство пользователя с поиском по 10 разделам (трей, Настройки и Shift+F1) + иллюстрированная версия на сайте. · Добавлено лицензионное соглашение (EULA) в установщике и при первом запуске программы (RU/EN). · Единая нативная типографика Windows 11 / 10 (Segoe UI Variable ClearType), современное окно профиля игрока, ускоренный поиск POI, плавный пометровый ETA и фоновое сохранение настроек. · Усилена приватность локальных архивов диагностики; исправлены 60 Гц пересчёт HUD без цели, авто-зум, HiDPI-подписи и таймеры пингов.";
        public static string DisplayVersion { get { return "ZiloreMiniMap v" + VersionString + " Build (" + BuildNumber + ")"; } }

        public static readonly Version CurrentVersion = new Version(VersionString + ".0");

        bool resourcesReleased;

        void ReleaseResources() {
            if(resourcesReleased)return; resourcesReleased=true; routeGeneration++;
            timer.Stop();
            var onDisposed = Disposed;
            Disposed = null;
            if (onDisposed != null) {
                try { onDisposed(this, EventArgs.Empty); } catch (Exception ex) { Program.LogException("EngineDisposed", ex); }
            }
            if(keys!=null)keys.Dispose();
            if(voicePlayer!=null)voicePlayer.Dispose();
            if(trayController!=null) trayController.Dispose();
            if(appIcon!=null) { try { appIcon.Dispose(); } catch { } }
            if(mapTiles!=null)mapTiles.Dispose();
            if(lazyMap!=null) { lazyMap.Dispose(); lazyMap=null; }
        }

        public void Dispose() {
            ReleaseResources();
        }

        void ExitApp() { if(WpfFieldConsoleRuntime.Active!=null) { WpfFieldConsoleRuntime.Active.RequestExit();return; }closing=true; SaveSettings(); Close(); }

        T RunUiModal<T>(Func<T> showDialog) {
            Form activeOwner=Form.ActiveForm;
            bool previousPanelOpening=panelOpening, previousCapture=shortcutCaptureOpen;
            bool resumeTimer=timer.IsEnabled;
            panelOpening=true;
            shortcutCaptureOpen=true;
            timer.Stop();
            try {
                Native.CancelActiveCopy();
                PrepareUiInput();
                return showDialog();
            }
            finally {
                panelOpening=previousPanelOpening;
                shortcutCaptureOpen=previousCapture;
                fallbackKeysArmed=false;
                if(!closing && !IsDisposed) {
                    if(activeOwner!=null && !activeOwner.IsDisposed && activeOwner.Visible && activeOwner.Enabled
                        && !diagnosticMode && Native.IsOurWindow(Native.GetForegroundWindow()))activeOwner.BringToFront();
                    if(resumeTimer)timer.Start();
                }
            }
        }

        void PrepareUiInput() {
            adminControlsCommandReturn=false;
            Native.CancelActiveCopy();
            Native.ReleaseCapture();
            Cursor.Clip=Rectangle.Empty;
            if(adminCommandsPanel!=null && !adminCommandsPanel.IsDisposed)adminCommandsPanel.Hide();
        }

        IWin32Window UiDialogOwner { get { return null; } }
        internal void ShowEngineAlert(string message,string title=null,IWin32Window owner=null) {
            if(diagnosticMode || closing) return;
            string safeTitle=string.IsNullOrWhiteSpace(title)?"ZiloreMiniMap":title;
            if(WpfFieldConsoleRuntime.Active!=null) {
                WpfFieldConsoleRuntime.Active.ShowUpdateNotice(safeTitle,message??"");
                return;
            }
            RunUiModal(() => MessageBox.Show(owner??UiDialogOwner,message??"",safeTitle,MessageBoxButtons.OK,MessageBoxIcon.Warning));
        }

        bool ToolWindowVisible {
            get { return (spawnCartDialog!=null && !spawnCartDialog.IsDisposed && spawnCartDialog.Visible)
                || (adminPlayersPanel!=null && !adminPlayersPanel.IsDisposed && adminPlayersPanel.Visible)
                || adminProfileWindows.Any(profile=>!profile.IsDisposed && profile.Visible); }
        }

        int adminPlayerToolOpening;
        bool IntensiveWorkPaused {
            get { return musicalScumbagOpen || AdminPlayerWorkPaused; }
        }
        bool AdminPlayerWorkPaused {
            get { return adminPlayerToolOpening>0
                || (adminPlayersPanel!=null && !adminPlayersPanel.IsDisposed && adminPlayersPanel.Visible)
                || adminProfileWindows.Any(profile=>!profile.IsDisposed && profile.Visible); }
        }
        internal static bool CanRestoreAdminTool(IntPtr foreground,IntPtr game,bool ownWindow) {
            return ownWindow || (game!=IntPtr.Zero && foreground==game);
        }
        void RestoreAdminTool(Form tool,IntPtr game) {
            if(closing || IsDisposed || tool==null || tool.IsDisposed || !tool.Enabled)return;
            if(playerToolsAwaitingReopen && IsPlayerTool(tool))return;
            IntPtr foreground=Native.GetForegroundWindow();
            if(!diagnosticMode && !CanRestoreAdminTool(foreground,game,Native.IsOurWindow(foreground)))return;
            Native.ReleaseCapture();Cursor.Clip=Rectangle.Empty;
            tool.WindowState=FormWindowState.Normal;tool.TopMost=true;
            tool.Show();tool.BringToFront();
            if(!diagnosticMode) { tool.Activate();Native.ForceForeground(tool.Handle); }
        }
        void ProtectToolWindow(Form tool) {
            Action update=()=> {
                UpdateIntensiveWorkPause();
                if(ToolWindowVisible) PrepareUiInput();
            };
            tool.VisibleChanged+=(s,e)=>update();
            tool.FormClosed+=(s,e)=>update();
            tool.Disposed+=(s,e)=>update();
        }

        public void OpenStartupGuide(bool bindingsOnly=false) {
            if(WpfFieldConsoleRuntime.Active!=null) { WpfFieldConsoleRuntime.Active.Handle(AppControlAction.StartupGuide); return; }
        }

        public void OpenZoneImport(string file) {
            if(WpfFieldConsoleRuntime.Active!=null) { WpfFieldConsoleRuntime.Active.ShowZoneWizard(file);return; }
            if(zonesLoadFailed) { ShowEngineAlert(Localization.Get("NoteZonesLoadFail"),Localization.Get("ZoneEditorTitle")); return; }
            if(IsHandleCreated && InvokeRequired) {
                BeginInvoke(new Action(() => OpenZoneImport(file)));
                return;
            }

            IWin32Window owner = UiDialogOwner;
            try {
                List<MapZone> existingWaypoints=zones.Where(IsCustomWaypointZone).ToList();
                List<MapZone> editableZones=zones.Where(zone=>!IsCustomWaypointZone(zone)).ToList();
                using(ZoneEditor editor=new ZoneEditor(map,editableZones,zonesPath,value=> {
                    zones=value.Concat(existingWaypoints).ToList();
                    try { if(File.Exists(zonesPath)) lastZonesFileWriteTimeUtc=File.GetLastWriteTimeUtc(zonesPath); } catch {}
                    SettingsChanged();
                },existingWaypoints)) {
                    if(!string.IsNullOrEmpty(file))editor.Shown+=async(s,e)=>await editor.ImportAutomatic(file);
                    OverlayTheme.Frame(editor,Localization.Get("HeaderMapZones"),()=>editor.Close());
                    if(owner == null) editor.StartPosition = FormStartPosition.CenterScreen;
                    RunUiModal(() => editor.ShowDialog(owner));
                }
            } catch(Exception ex) {
                Program.LogException("ZoneEditor", ex);
                ShowEngineAlert(Localization.T("ZoneEditorOpenError",ex.Message),Localization.Get("ZoneEditorTitle"),owner);
            }
        }

        public void CheckAutomaticImport(string file,string output) {

            Exception failure=null;

            using(ZoneEditor editor=new ZoneEditor(map,new List<MapZone>(),zonesPath,value=>{})) {

                editor.Shown+=async(s,e)=> {

                    try { await editor.ImportAutomatic(file);editor.PreviewAutomatic(output); }

                    catch(Exception ex) { failure=ex; }finally { editor.Close(); }

                };

                RunUiModal(() => editor.ShowDialog());

            }

            if(failure!=null)throw failure;

        }

        void ToggleOverlay() {
            if(WpfFieldConsoleRuntime.Active!=null) WpfFieldConsoleRuntime.Active.Handle(AppControlAction.ToggleOverlay);
        }
    }
}
