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
using DrawingPoint = System.Drawing.PointF;

namespace ZiloreMiniMap {
    internal sealed partial class WpfFieldConsoleRuntime : IDisposable {
        internal static WpfFieldConsoleRuntime Active;
        static readonly System.Text.StringBuilder sidebarIdBuilder = new System.Text.StringBuilder(256);
        bool systemThemeHooked, displaySettingsHooked;
        System.Drawing.Rectangle[] cachedScreenBounds, cachedScreenWorkingAreas;
        void OnDisplaySettingsChanged(object sender, EventArgs e) {
            cachedScreenBounds = null;
            cachedScreenWorkingAreas = null;
        }
        System.Drawing.Rectangle[] GetScreenBounds() {
            if (cachedScreenBounds == null)
                cachedScreenBounds = System.Windows.Forms.Screen.AllScreens.Select(s => s.Bounds).ToArray();
            return cachedScreenBounds;
        }
        internal bool HasHighResMonitor() {
            try {
                var b = GetScreenBounds();
                for (int i = 0; i < b.Length; i++)
                if (b[i].Width >= 2200 || b[i].Height >= 1200)
                    return true;
            } catch { }
            return false;
        }
        System.Drawing.Rectangle[] GetScreenWorkingAreas() {
            if (cachedScreenWorkingAreas == null)
                cachedScreenWorkingAreas = System.Windows.Forms.Screen.AllScreens.Select(s => s.WorkingArea).ToArray();
            return cachedScreenWorkingAreas;
        }
        void OnSystemThemeChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e) {
            if (disposed || e.Category != Microsoft.Win32.UserPreferenceCategory.General)
                return;
            if (string.Equals(WpfFieldTheme.ActiveTheme, "System", StringComparison.OrdinalIgnoreCase))
                PostUi(() => ApplyUiTheme("System"));
        }
        void PostUi(Action apply) {
            if (disposed || application.Dispatcher.HasShutdownStarted)
                return;
            try {
                application.Dispatcher.BeginInvoke(new Action(() => { if (!disposed) apply(); }));
            } catch (InvalidOperationException) { }
        }
        readonly MapWindow engine;
        DirectPositions privatePositions;
        RelayGroupStore relayGroups;
        readonly Dictionary<string, int> playerVisibility = new Dictionary<string, int>(StringComparer.Ordinal);
        PrivatePositions.Peer[] playerRoster = Array.Empty<PrivatePositions.Peer>();
        int PlayerVisibility(string id) {
            int value;
            return id != null && playerVisibility.TryGetValue(id, out value) ? value : 31;
        }
        bool PlayerMarkerVisible(SharedMapMarker marker) {
            return (PlayerVisibility(marker.OwnerId) & (marker.Death ? 8 : marker.Id == SharedMapMarker.GpsId ? 2 : SquadState.IsStop(marker) ? 16 : 4)) != 0;
        }
        internal Func<System.Threading.Tasks.Task<string>> publicAddressLookup = null;
        readonly Application application;
        readonly DrawingGroup geography;
        readonly WpfTerrainTiles terrain;
        readonly DispatcherTimer refresh;
        ScumAutoLaunch autoLaunch;
        readonly bool diagnostic;
        readonly AppZoneFolder zoneFolder;
        System.Threading.Tasks.Task<string> zoneOpenRead;
        DateTime nextZoneOpenRead;
        bool zoneFileActive;
        bool hudMoving;
        long nextHudRaise, nextRefreshError;
        System.Drawing.Rectangle lastHudBounds;
        Window tactical, hud;
        bool hudClosed;
        Window settingsWindow;
        volatile bool settingsToggleOpen;
        WpfMapSurface tacticalMap, hudMap;
        StackPanel sidebar;
        const int HudStatusHeight = 100;
        Grid hudLayout;
        Border hudStatus, hudAccentBar, hudSectorBadge, hudSquareBezel, hudDividerBorder, hudSquadBadge, hudHpBar, hudFoodBar, hudWaterBar, tacticalSidebarShell, tacticalToolbarChrome, tacticalFloatingRailDock, tacticalToastCard;
        TextBlock tacticalToastText;
        DispatcherTimer tacticalToastTimer, startupUpdateTimer;
        Button sidebarToggleButton;
        UniformGrid hudSurvivalBars;
        ColumnDefinition hudHpFillCol, hudHpRemCol, hudFoodFillCol, hudFoodRemCol, hudWaterFillCol, hudWaterRemCol;
        System.Windows.Shapes.Ellipse hudStatusDot, hudBezel, hudInnerBezel;
        string cachedHudShape, cachedHudTheme, cachedTacticalGlassTheme, lastHudTelText;
        int cachedHudOpacity = -1, lastHudSquadOnline = -1, lastHudHpPct = -1, lastHudFoodPct = -1, lastHudWaterPct = -1;
        bool? cachedHudCompactW;
        TextBlock targetTitle, targetDetail, hudSector, hudGameClock, hudRealClock, hudTelemetry, hudSquadCount, routePreviewTitle, routePreviewDetail;
        long lastHudRealSecond = -1, lastTelemetrySecond = -1;
        WpfSatnavTicker hudLocation, hudRoute;
        Border targetCard, routePreviewCard;
        Border hoverCard;
        TextBlock hoverInfo;
        void ShowHoverDetails(string text) {
            hoverInfo.Text = text ?? "";
            hoverCard.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        }
        readonly HashSet<string> expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Bunkers" };
        readonly List<Window> dialogs = new List<Window>();
        bool rebuilding, hudEnabled = true; volatile bool disposed;
        readonly MapMouseShield mouseShield = new MapMouseShield();
        volatile bool tacticalMouseShieldActive;
        bool tacticalStandalone;
        bool applyingUpdateOnExit, applyingUpdateBusy, eulaDeclined;
        internal bool TacticalStandaloneOpen {
            get {
                return tacticalStandalone && tactical != null && tactical.IsVisible && dialogs.Count == 0;
            }
        }
        internal UpdateRelease ReadyUpdateRelease {
            get {
                return engine == null ? null : engine.WpfReadyUpdateRelease;
            }
        }
        internal void NotifySeamlessUpdateReady(UpdateRelease rel) {
            PostUi(() => { filterIdentity = null; Refresh(); if (TacticalVisible && rel != null) ShowTacticalToast("⬇ v" + rel.VersionText); });
        }
        internal void PostApplyReadyUpdate() {
            PostUi(ApplyReadyUpdateNow);
        }
        void ApplyReadyUpdateNow() { _ = ApplyReadyUpdateNowAsync(); }
        async System.Threading.Tasks.Task ApplyReadyUpdateNowAsync() {
            if (applyingUpdateBusy || disposed || exitRequested)
                return;
            applyingUpdateBusy = true;
            try {
                if (TacticalVisible)
                    ShowTacticalToast(Localization.Current == AppLanguage.Russian ? "⏳ Подготовка обновления…" : "⏳ Applying update…");
                bool started = await System.Threading.Tasks.Task.Run(() => engine.ApplyReadyUpdate(true));
                if (disposed || exitRequested)
                    return;
                if (started) {
                    applyingUpdateOnExit = true;
                    RequestExit();
                } else {
                    engine.WpfCheckUpdates();
                }
            } catch (Exception ex) {
                Program.LogException("ApplyReadyUpdate", ex);
                if (!disposed && !exitRequested)
                    ShowUpdateNotice(Localization.Get("UpdateCheckTitle"), Localization.T("UpdateFailedMsg", ex.Message));
            } finally { applyingUpdateBusy = false; }
        }
        IntPtr gameMapWindow;
        Button followButton, zoomResetButton, zoomLockButton, addTargetStop;
        Slider tacticalOpacity;
        TextBlock tacticalOpacityValue;
        bool syncingOpacity;
        DispatcherTimer cameraSettle;
        string filterIdentity;
        ulong tacticalVisualHash, hudVisualHash;
        bool tacticalVisualHashValid, hudVisualHashValid, cameraInteractionActive;
        int terrainRedrawPending;
        IntPtr cachedTacticalHandle;
        readonly Dictionary<int, CheckBox> poiCategoryControls = new Dictionary<int, CheckBox>();
        readonly Dictionary<string, CheckBox> poiSectionControls = new Dictionary<string, CheckBox>();
        readonly Dictionary<string, TextBlock> poiSectionCounts = new Dictionary<string, TextBlock>();
        string presentedPoiFilter;
        void RefreshInteraction() {
            if (poiFilterDebounce != null)
                poiFilterDebounce.Stop();
            if (tacticalToastTimer != null)
                tacticalToastTimer.Stop();
            EndCameraInteraction(false);
            lastRefresh = -1000;
            Refresh();
        }
        void EndCameraInteraction(bool requestRedraw = true) {
            if (cameraSettle != null)
                cameraSettle.Stop();
            if (tacticalMap == null || !cameraInteractionActive)
                return;
            cameraInteractionActive = false;
            tacticalMap.EndCameraFrames();
            RenderOptions.SetBitmapScalingMode(tacticalMap, BitmapScalingMode.HighQuality);
            if (requestRedraw)
                tacticalMap.RequestRedraw();
        }
        void RefreshCamera() {
            application.Dispatcher.VerifyAccess();
            if (disposed)
                return;
            if (tacticalMap.State == null) {
                RefreshInteraction();
                return;
            }
            // Camera input does not poll groups, recalculate routes or rebuild
            // the sidebar. The regular refresh continues to publish live data.
            engine.WpfUpdateCamera(tacticalMap.State, true);
            tacticalVisualHashValid = false;
            if (!cameraInteractionActive) {
                cameraInteractionActive = true;
                tacticalMap.BeginCameraFrames();
                tacticalMap.ClearHover();
                RenderOptions.SetBitmapScalingMode(tacticalMap, BitmapScalingMode.LowQuality);
            }
            if (cameraSettle == null) {
                cameraSettle = new DispatcherTimer(DispatcherPriority.Background, application.Dispatcher) { Interval = TimeSpan.FromMilliseconds(75) };
                cameraSettle.Tick += (s, e) => { if (disposed) { cameraSettle.Stop(); return; } if (tacticalMap.PointerCameraActive || engine.WpfHasPendingTacticalZoom) return; cameraSettle.Stop(); EndCameraInteraction(true); };
            }
            cameraSettle.Stop();
            cameraSettle.Start();
            SyncCameraControls(tacticalMap.State);
            tacticalMap.RequestRedraw();
        }
        int lastSyncedOpacity = -1; double lastSyncedZoom = double.NaN; bool? lastSyncedFollow, lastSyncedLock;
        void SyncCameraControls(WpfMapState state) {
            if (lastSyncedOpacity != state.Opacity) {
                lastSyncedOpacity = state.Opacity;
                syncingOpacity = true;
                try {
                    tacticalOpacity.Value = state.Opacity;
                    tacticalOpacity.IsEnabled = true;
                    tacticalOpacityValue.Text = state.Opacity.ToString("0", CultureInfo.CurrentCulture) + "%";
                } finally { syncingOpacity = false; }
            }
            bool flw = engine.WpfFollowing;
            if (followButton != null && lastSyncedFollow != flw) {
                lastSyncedFollow = flw;
                followButton.SetResourceReference(Control.BackgroundProperty, flw ? "ThemeAmber" : "ThemePanel");
                followButton.SetResourceReference(Control.ForegroundProperty, flw ? "ThemePrimaryInk" : "ThemeInk");
            }
            if (zoomResetButton != null && (double.IsNaN(lastSyncedZoom) || Math.Abs(lastSyncedZoom - state.Zoom) >= 0.049)) {
                lastSyncedZoom = state.Zoom;
                zoomResetButton.Content = state.Zoom.ToString("0.0", CultureInfo.CurrentCulture) + "×";
            }
            bool lck = engine.WpfTacticalZoomLocked;
            if (zoomLockButton != null && lastSyncedLock != lck) {
                lastSyncedLock = lck;
                zoomLockButton.Content = lck ? "🔒" : "🔓";
                zoomLockButton.SetResourceReference(Control.BackgroundProperty, lck ? "ThemeAmber" : "ThemePanel");
                zoomLockButton.SetResourceReference(Control.ForegroundProperty, lck ? "ThemePrimaryInk" : "ThemeInk");
            }
        }
        void SyncPoiControls(ScumMapStore store) {
            if (store == null || presentedPoiFilter == store.GetFilterHashKey())
                return;
            rebuilding = true;
            try {
                foreach (var entry in poiCategoryControls)
                    entry.Value.IsChecked = store.IsCategoryEnabled(entry.Key);
                foreach (var entry in poiSectionControls) {
                    var categories = store.CategoriesBySection[entry.Key];
                    int enabled = 0;
                    for (int i = 0; i < categories.Count; i++)
                    if (store.IsCategoryEnabled(categories[i].Id))
                        enabled++;
                    entry.Value.IsChecked = enabled == 0 ? (bool?)false : enabled == categories.Count ? (bool?)true : null;
                    poiSectionCounts[entry.Key].Text = enabled + "/" + categories.Count;
                }
                UpdatePoiWarnBanner(store);
                presentedPoiFilter = store.GetFilterHashKey();
            } finally { rebuilding = false; }
        }
        readonly System.Diagnostics.Stopwatch refreshClock = System.Diagnostics.Stopwatch.StartNew();
        long lastRefresh = -1000;
        long lastZoomAnimTick;
        long lastRouteTextRefresh = -1000;
        bool lastRouteNoTargetOrPaused;
        bool peersInterpolating;
        long lastSlowRefresh = -1000;
        DirectPositions[] cachedSharingSessions = Array.Empty<DirectPositions>();
        PrivatePositions.Peer[] cachedPeers = Array.Empty<PrivatePositions.Peer>();
        SharedMapMarker[] cachedSharedMarkers = Array.Empty<SharedMapMarker>();
        readonly List<DirectPositions> scratchSessions = new List<DirectPositions>(4);
        readonly List<PrivatePositions.Peer> scratchPeers = new List<PrivatePositions.Peer>(16);
        readonly List<SharedMapMarker> scratchMarkers = new List<SharedMapMarker>(128);
        readonly List<SharedMapMarker> scratchNetMarkers = new List<SharedMapMarker>(128);
        bool peersCacheValid;
        Position lastNetPos;
        long lastNetPosMs = -10000, lastNetMarkersMs = -10000;
        int lastNetMarkersHash;
        int cachedHudW = -1, cachedHudH = -1;
        bool cachedHudStatus, cachedHudAbove;
        Button deathRemovalButton;
        string chromeLanguage;
        IntPtr inputTacticalHandle;
        volatile IntPtr[] inputDialogHandles = Array.Empty<IntPtr>();
        volatile bool inputEditingText;
        Window setupWindow;
        bool setupHasFocus;
        void SyncModalInput() {
            engine.WpfModal(dialogs.Count > 0 || (setupWindow != null && setupHasFocus));
        }
        void SetSetupInputFocus(bool focused) {
            setupHasFocus = focused;
            if (setupWindow != null && setupWindow.IsVisible) {
                bool allowTop = dialogs.Count == 0;
                setupWindow.Topmost = allowTop;
                if (allowTop && !diagnostic) {
                    EnsureWindowOnTop(setupWindow, focused);
                    if (focused)
                        Native.ForceForeground(new WindowInteropHelper(setupWindow).Handle);
                }
            }
            SyncModalInput();
            RefreshInputOwnership();
            if (!focused)
                SyncHudVisibility(true);
        }
        void RefreshInputOwnership() {
            application.Dispatcher.VerifyAccess();
            System.Threading.Interlocked.Exchange(ref inputTacticalHandle, tactical == null ? IntPtr.Zero : new WindowInteropHelper(tactical).Handle);
            inputDialogHandles = dialogs.Concat(setupWindow == null ? Array.Empty<Window>() : new[] { setupWindow }).Select(w => new WindowInteropHelper(w).Handle).Where(h => h != IntPtr.Zero).ToArray();
            var focused = Keyboard.FocusedElement as DependencyObject;
            var owner = focused == null ? null : System.Windows.Window.GetWindow(focused);
            inputEditingText = owner != null && owner.IsVisible && EditingText(Keyboard.FocusedElement);
        }
        static void RefreshLocalizedChrome(DependencyObject root) {
            var element = root as FrameworkElement;
            string tag = element == null ? null : element.Tag as string;
            if (tag != null && (tag.StartsWith("text:") || tag.StartsWith("tooltip:"))) {
                string value = Localization.Get(tag.Substring(tag.IndexOf(':') + 1));
                if (tag.StartsWith("text:")) {
                    var text = element as TextBlock;
                    var content = element as ContentControl;
                    if (text != null)
                        text.Text = value;
                    else if (content != null)
                        content.Content = value;
                }
                element.ToolTip = value;
                System.Windows.Automation.AutomationProperties.SetName(element, value);
            }
            foreach (object child in LogicalTreeHelper.GetChildren(root)) {
                var dependency = child as DependencyObject;
                if (dependency != null)
                    RefreshLocalizedChrome(dependency);
            }
        }
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [LibraryImport("user32.dll", EntryPoint = "ShowWindow")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool ShowNativeWindow(IntPtr window, int command);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsIconic(IntPtr window);
        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")] private static partial int GetWindowLong(IntPtr window, int index);
        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")] private static partial int SetWindowLong(IntPtr window, int index, int value);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool ClipCursor(IntPtr rectangle);
        [LibraryImport("user32.dll")] private static partial IntPtr GetAncestor(IntPtr window, uint flags);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool ReleaseCapture();
        [LibraryImport("user32.dll", EntryPoint = "SendMessageW")] private static partial IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        internal static void BeginNativeWindowMove(IntPtr handle) {
            if (handle == IntPtr.Zero)
                return;
            ReleaseCapture();
            SendMessage(handle, 0x0112 /* WM_SYSCOMMAND */, new IntPtr(0xF012 /* SC_MOVE + HTCAPTION */), IntPtr.Zero);
        }
        internal WpfFieldConsoleRuntime(string folder, bool diagnostic = false) {
            this.diagnostic = diagnostic;
            application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.DispatcherUnhandledException += (s, e) => {
                Program.LogException("WpfDispatcher", e.Exception);
                if (!diagnostic)
                    e.Handled = true;
            };
            engine = new MapWindow(folder, diagnostic, true);
            engine.WpfInitializeDefaults();
            WpfMapSurface.LoadFlightUiSettings(engine.WpfDataFolder);
            ScumGameClock.Load(engine.WpfDataFolder);
            ScumWeatherEngine.Load(engine.WpfDataFolder); TransportEtaService.Load(engine.WpfDataFolder); ErrorReportService.LoadPreferences(engine.WpfDataFolder);
            ZiloreAiEngine.Load(engine.WpfDataFolder);
            ScumSurvivalTracker.Load(engine.WpfDataFolder);
            UpdatePreferencesStore.Load(engine.WpfDataFolder);
            engine.HydrateReadyUpdateOnStartup();
            SystemTypography.RegisterGlobalPopupClearType();
            WpfFieldTheme.SetBundledFontFamily(SystemTypography.WpfUiFontFamily);
            WpfMapSurface.SetLabelFontFamily(SystemTypography.WpfUiFontFamily);
            WpfFieldTheme.ApplyPalette(engine.WpfUiTheme);
            OverlayTheme.SyncWithWpfTheme(WpfFieldTheme.ResolveTheme(engine.WpfUiTheme));
            engine.WpfRefreshTrayTheme();
            application.Resources.MergedDictionaries.Add(WpfFieldTheme.Resources());
            AdaptiveAppIcon.ApplyToAllOpenWindows(application, WpfFieldTheme.ResolveTheme(engine.WpfUiTheme) == "Light");
            if (!diagnostic)
                try {
                    relayGroups = new RelayGroupStore(engine.WpfDataFolder);
                } catch (Exception error) { Program.LogException("RestorePrivateGroups", error); }
            if (!diagnostic)
                LoadLocalRoute();
            if (!diagnostic)
                zoneFolder = new AppZoneFolder(AppDomain.CurrentDomain.BaseDirectory);
            if (diagnostic)
                engine.WpfStopEngineTimer();
            // RoadRouter initializes asynchronously on ThreadPool in MapWindow.
            geography = TryLoadCustomMapDrawing(folder);
            if (geography == null) {
                terrain = new WpfTerrainTiles();
                geography = terrain.Overview();
            }
            application.Resources[typeof(Window)] = SystemTypography.CreateWindowStyle(WpfFieldTheme.Ink, WpfFieldTheme.Ground);
            if (!diagnostic) {
                string legacyFontsDir = Path.Combine(folder, "field-console-fonts");
                if (Directory.Exists(legacyFontsDir)) {
                    System.Threading.ThreadPool.QueueUserWorkItem(_ => SafeCleanup.DeleteDirectory(legacyFontsDir, true));
                }
            }
            if (!diagnostic) {
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;
                systemThemeHooked = true;
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
                displaySettingsHooked = true;
            }
            LoadTacticalUiLayout();
            tactical = BuildTactical();
            hud = BuildHud();
            if (terrain != null)
                terrain.Changed += () => {
                    if (disposed || System.Threading.Interlocked.Exchange(ref terrainRedrawPending, 1) != 0)
                        return;
                    PostUi(() => {
                        System.Threading.Interlocked.Exchange(ref terrainRedrawPending, 0);
                        if (disposed)
                            return;
                        if (tactical.IsVisible) {
                            tacticalMap.TerrainRevision++;
                            tacticalMap.RequestRedraw();
                        }
                        if (hud.IsVisible) {
                            hudMap.TerrainRevision++;
                            hudMap.InvalidateVisual();
                        }
                    });
                };
            tactical.IsVisibleChanged += (s, e) => { tacticalMouseShieldActive = tactical.IsVisible && tactical.WindowState != WindowState.Minimized; if (!tactical.IsVisible) tacticalMap.CloseContextMenu(); };
            tactical.StateChanged += (s, e) => tacticalMouseShieldActive = tactical.IsVisible && tactical.WindowState != WindowState.Minimized;
            tactical.Closed += (s, e) => tacticalMouseShieldActive = false;
            refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            refresh.Tick += (s, e) => {
                // DispatcherTimer does not schedule its next tick if its callback escapes.
                try {
                    Refresh();
                } catch (Exception error) { if (refreshClock.ElapsedMilliseconds >= nextRefreshError) { nextRefreshError = refreshClock.ElapsedMilliseconds + 10000; Program.LogException("WpfRefresh", error); } }
            };
            if (!diagnostic) {
                refresh.Start();
                autoLaunch = new ScumAutoLaunch(() => application.Dispatcher.BeginInvoke(new Action(RequestExit)));
            }
        }
        internal IntPtr PrimaryWindowHandle {
            get {
                if (tactical != null) {
                    IntPtr h = new WindowInteropHelper(tactical).Handle;
                    if (h != IntPtr.Zero) return h;
                }
                if (hud != null) {
                    IntPtr h = new WindowInteropHelper(hud).Handle;
                    if (h != IntPtr.Zero) return h;
                    if (!disposed) return new WindowInteropHelper(hud).EnsureHandle();
                }
                return IntPtr.Zero;
            }
        }
        internal static void Run(string folder, string localUpdateSource = null) {
            using (var runtime = new WpfFieldConsoleRuntime(folder)) {
                Active = runtime;
                if (!runtime.EnsureEulaAccepted()) {
                    Active = null;
                    return;
                }
                runtime.StartViews();
                Program.CompletePostUpdate(folder, runtime.ShowPostUpdate);
                if (runtime.engine.WpfNeedsOnboarding)
                    runtime.ShowSetupWindow(false);
                if (!string.IsNullOrEmpty(localUpdateSource)) {
                    runtime.application.Dispatcher.BeginInvoke(new Action(() => runtime.engine.RunLocalUpdateTest(localUpdateSource)));
                }
                runtime.application.Run();
                Active = null;
            }
        }
        internal bool SuppressMapMouse(int message, System.Drawing.Point point, uint data) {
            // Called on the mouse hook thread: never read WPF dependency properties here.
            if (!tacticalMouseShieldActive)
                return mouseShield.Suppress(message, data, false, false, false);
            bool active = TacticalMouseShieldEnabled(tacticalMouseShieldActive, Native.IsOurWindow(Native.GetForegroundWindow()));
            if (!active)
                return mouseShield.Suppress(message, data, false, false, false);
            IntPtr target = Native.WindowFromPoint(point);
            bool inside = Native.IsOurWindow(target);
            // The game HWND was validated on the UI thread when opening the map.
            // Never enumerate processes or touch controls in a low-level hook.
            IntPtr game = System.Threading.Interlocked.CompareExchange(ref gameMapWindow, IntPtr.Zero, IntPtr.Zero);
            return mouseShield.Suppress(message, data, true, inside, !inside && game != IntPtr.Zero && GetAncestor(target, 2) == game);
        }
        internal static bool TacticalMouseShieldEnabled(bool visible, bool appFocused) {
            return visible && appFocused;
        }
        internal static bool ShouldHideTactical(bool visible, bool gameFocused, bool appFocused) {
            return visible && !gameFocused && !appFocused;
        }
        internal void SyncTacticalFocus(bool gameFocused, bool appFocused) {
            bool setupOpen = setupWindow != null && setupWindow.IsVisible;
            if (setupOpen || ShouldHideTactical(tactical.IsVisible, gameFocused, appFocused)) {
                tacticalMouseShieldActive = false;
                tactical.Topmost = false;
                tactical.Hide();
                tacticalMap.ClearHover();
                tacticalMap.ResetPoiStream(false);
                if (terrain != null)
                    terrain.TrimCache(20);
                engine.WpfTactical(false);
            } else if (dialogs.Count > 0) {
                // Keep tactical map open underneath modal dialogs (Routes/Search/Settings), never above them.
                tacticalMouseShieldActive = false;
                if (tactical.Topmost)
                    tactical.Topmost = false;
            } else if (tactical.IsVisible) {
                if (!tactical.Topmost)
                    tactical.Topmost = true;
                tacticalMouseShieldActive = tactical.WindowState != WindowState.Minimized;
                if (!diagnostic && refreshClock.ElapsedMilliseconds - lastSlowRefresh >= 200)
                    EnsureWindowOnTop(tactical, false);
            }
        }
        internal void SetTactical(bool active) {
            if (active) {
                if (!tactical.IsVisible)
                    ToggleTactical();
            } else
                CloseTactical();
        }
        internal void CloseTactical(bool closeGameMap = false) {
            tacticalStandalone = false;
            tacticalMouseShieldActive = false;
            tactical.Topmost = false;
            tactical.Hide();
            tacticalMap.ClearHover();
            tacticalMap.ResetPoiStream(false);
            if (terrain != null)
                terrain.TrimCache(20);
            engine.WpfTactical(false);
            SyncHudVisibility(true);
            if (closeGameMap && !diagnostic && gameMapWindow != IntPtr.Zero) {
                var binding = engine.WpfSnapshot(false);
                if (!binding.TrackingPaused)
                    Native.ReturnMapKey(gameMapWindow, binding.MapKey, binding.MapScan);
            }
            Refresh();
        }
        internal System.Drawing.Rectangle LastMonitorBounds;
        System.Drawing.Rectangle lastTacticalBounds;
        internal static System.Drawing.Rectangle RecoverMapBounds(System.Drawing.Rectangle desired, System.Drawing.Rectangle[] monitors) { return MapCameraService.RecoverMapBounds(desired, monitors); }
        System.Drawing.Rectangle TacticalBounds() {
            var monitor = System.Windows.Forms.Screen.FromHandle(gameMapWindow == IntPtr.Zero ? new WindowInteropHelper(hud).EnsureHandle() : gameMapWindow);
            LastMonitorBounds = monitor.Bounds;
            System.Drawing.Rectangle gameBounds;
            Native.TryGameClientBounds(gameMapWindow, out gameBounds);
            return ResolveMapBounds(engine.WpfManualMap, engine.WpfMapBounds, gameBounds, monitor.Bounds, GetScreenBounds());
        }
        internal static System.Drawing.Rectangle ResolveMapBounds(bool manual, System.Drawing.Rectangle saved, System.Drawing.Rectangle game, System.Drawing.Rectangle monitor, System.Drawing.Rectangle[] monitors) { return MapCameraService.ResolveMapBounds(manual, saved, game, monitor, monitors); }
        internal void FitTacticalToMonitor() {
            var bounds = TacticalBounds();
            tactical.WindowState = WindowState.Normal;
            tactical.ResizeMode = ResizeMode.NoResize;
            tactical.WindowStartupLocation = WindowStartupLocation.Manual;
            if (diagnostic) {
                bounds.X = -32000;
                bounds.Y = -32000;
            }
            ApplyTacticalBounds(bounds);
        }
        void ApplyTacticalBounds(System.Drawing.Rectangle bounds) {
            var handle = new WindowInteropHelper(tactical).EnsureHandle();
            cachedTacticalHandle = handle;
            if (IsIconic(handle))
                ShowNativeWindow(handle, 4); // SW_SHOWNOACTIVATE clears native minimization.
            // WPF can retain the iconic window size after minimization. Update its
            // layout properties as well as the native bounds before rendering again.
            var source = HwndSource.FromHwnd(handle);
            if (source == null || source.CompositionTarget == null)
                return;
            var size = source.CompositionTarget.TransformFromDevice.Transform(new Point(bounds.Width, bounds.Height));
            tactical.MinWidth = Math.Min(900, size.X);
            tactical.MinHeight = Math.Min(620, size.Y);
            tactical.Width = size.X;
            tactical.Height = size.Y;
            bool canTopmost = dialogs.Count == 0 && (setupWindow == null || !setupWindow.IsVisible);
            uint swpFlags = 0x0010u | (canTopmost ? 0u : 0x0004u);
            SetWindowPos(handle, canTopmost ? new IntPtr(-1) : IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, swpFlags);
            tactical.UpdateLayout();
            lastTacticalBounds = bounds;
        }
        bool sessionEndingHooked;
        void OnSessionEnding(object sender, Microsoft.Win32.SessionEndingEventArgs e) {
            try {
                SaveLocalRoute();
                SaveTacticalUiLayout();
                if (!eulaDeclined) engine.WpfSave();
                Program.LogInfo("Session", "Saved routes and settings on Windows shutdown.");
            } catch { }
        }
        internal void StartViews() {
            tactical.Hide();
            engine.WpfTactical(false);
            SyncHudVisibility(true);
            Refresh();
            if (!diagnostic) {
                if (!sessionEndingHooked) {
                    Microsoft.Win32.SystemEvents.SessionEnding += OnSessionEnding;
                    sessionEndingHooked = true;
                }
                var warmState = engine.WpfSnapshot(true);
                System.Threading.Tasks.Task.Run(async () => {
                    try {
                        await System.Threading.Tasks.Task.Delay(600).ConfigureAwait(false);
                        if (disposed) return;
                        WpfMapSurface.PrewarmPoiCache(warmState, 1920, 1080);
                        Program.LogInfo("MapCache", "Prewarmed tactical POI spatial index.");
                    } catch (Exception ex) { Program.LogException("MapCache", ex); }
                });
                System.Threading.Tasks.Task.Run(() => IntegrityGuard.VerifyReleaseAuthenticity());
                if (!Program.IsTestBuild && string.IsNullOrEmpty(Program.LocalUpdateSource)) {
                    startupUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                    startupUpdateTimer.Tick += (s, e) => { if (startupUpdateTimer != null) startupUpdateTimer.Stop(); if (!disposed && !exitRequested) engine.WpfCheckUpdatesBackground(); };
                    startupUpdateTimer.Start();
                }
            }
        }
        Window Window(string title, double w, double h) {
            var window = new Window { Title = title, Width = w, Height = h, MinWidth = Math.Min(w, 420), MinHeight = Math.Min(h, 300), WindowStartupLocation = WindowStartupLocation.CenterScreen, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.CanResizeWithGrip, Background = WpfFieldTheme.Ground, Foreground = WpfFieldTheme.Ink, FlowDirection = Localization.Current == AppLanguage.Arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
            window.FontFamily = SystemTypography.WpfUiFontFamily;
            SystemTypography.ApplyClearType(window);
            AdaptiveAppIcon.ApplyToWindow(window, WpfFieldTheme.ResolveTheme(WpfFieldTheme.ActiveTheme) == "Light");
            window.SourceInitialized += (s, e) => RefreshInputOwnership();
            // Placement-controlled map/HUD windows have their own physical-pixel recovery.
            window.Loaded += (s, e) => { if (window != tactical && window != hud && window.Tag as string != "hud-move" && window.Tag as string != "map-placement-preview") FitToolWindow(window, !diagnostic); };
            window.GotKeyboardFocus += (s, e) => RefreshInputOwnership();
            window.LostKeyboardFocus += (s, e) => inputEditingText = EditingText(e.NewFocus);
            window.PreviewKeyDown += (s, e) => {
                if (e.Key != Key.Escape)
                    return;
                try {
                    // Dismiss the innermost interaction before its containing window.
                    var dropdown = ChildrenOf<ComboBox>(window).FirstOrDefault(c => c.IsDropDownOpen);
                    if (dropdown != null)
                        dropdown.IsDropDownOpen = false;
                    else if (window == tactical && tacticalMap != null && tacticalMap.ContextMenuOpen)
                        tacticalMap.CloseContextMenu();
                    else if (window.IsVisible)
                        window.Close();
                } catch (Exception ex) {
                    Program.LogException("WindowEscape", ex);
                }
                e.Handled = true;
            };
            return window;
        }
        static void FitToolWindow(Window window, bool position) {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            if (source == null || source.CompositionTarget == null)
                return;
            var area = System.Windows.Forms.Screen.FromHandle(source.Handle).WorkingArea;
            var transform = source.CompositionTarget.TransformFromDevice;
            var origin = transform.Transform(new Point(area.Left, area.Top));
            var size = transform.Transform(new Vector(area.Width, area.Height));
            double width = Math.Max(1, size.X), height = Math.Max(1, size.Y);
            window.MinWidth = Math.Min(window.MinWidth, width);
            window.MinHeight = Math.Min(window.MinHeight, height);
            if (window.SizeToContent == SizeToContent.Manual || window.SizeToContent == SizeToContent.Height)
                window.Width = Math.Min(window.Width, width);
            else
                window.MaxWidth = Math.Min(window.MaxWidth, width);
            if (window.SizeToContent == SizeToContent.Manual || window.SizeToContent == SizeToContent.Width)
                window.Height = Math.Min(window.Height, height);
            else
                window.MaxHeight = Math.Min(window.MaxHeight, height);
            double curW = window.ActualWidth > 0 ? window.ActualWidth : window.Width, curH = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
            if (position && !double.IsNaN(curW) && !double.IsNaN(curH)) {
                window.Left = Math.Max(origin.X, Math.Min(window.Left, origin.X + width - curW));
                window.Top = Math.Max(origin.Y, Math.Min(window.Top, origin.Y + height - curH));
            }
        }
        bool? cachedHardwareAccelMap;
        Action refreshOnboardingGlass;
        internal bool HardwareAccelMapEnabled {
            get {
                if (cachedHardwareAccelMap.HasValue)
                    return cachedHardwareAccelMap.Value;
                try {
                    var ini = SettingsStore.ReadIni(Path.Combine(engine.WpfDataFolder, "hw-accel.ini"));
                    string v;
                    cachedHardwareAccelMap = ini.TryGetValue("HardwareAccelMap", out v) && v == "1";
                    return cachedHardwareAccelMap.Value;
                } catch { cachedHardwareAccelMap = false; return false; }
            }
            set {
                cachedHardwareAccelMap = value;
                try {
                    SettingsStore.WriteAtomic(Path.Combine(engine.WpfDataFolder, "hw-accel.ini"), "HardwareAccelMap=" + (value ? "1" : "0") + Environment.NewLine);
                } catch (Exception ex) { Program.LogException("HwAccelSave", ex); }
            }
        }
        MapZone lastPresentedTarget;
        internal void ApplyUiTheme(string mode) {
            engine.WpfSetUiTheme(mode);
            WpfFieldTheme.ApplyPalette(engine.WpfUiTheme);
            OverlayTheme.SyncWithWpfTheme(WpfFieldTheme.ResolveTheme(engine.WpfUiTheme));
            engine.WpfRefreshTrayTheme();
            application.Resources.MergedDictionaries.Clear();
            application.Resources.MergedDictionaries.Add(WpfFieldTheme.Resources());
            var winStyle = application.Resources[typeof(Window)] as Style;
            if (winStyle != null) {
                var updated = new Style(typeof(Window));
                foreach (Setter s in winStyle.Setters) {
                    if (s.Property == Control.ForegroundProperty)
                        updated.Setters.Add(new Setter(Control.ForegroundProperty, WpfFieldTheme.Ink));
                    else if (s.Property == Control.BackgroundProperty)
                        updated.Setters.Add(new Setter(Control.BackgroundProperty, WpfFieldTheme.Ground));
                    else
                        updated.Setters.Add(s);
                }
                application.Resources[typeof(Window)] = updated;
            }
            WpfMapSurface.ClearLabelCache();
            filterIdentity = null;
            cachedHudTheme = null;
            cachedTacticalGlassTheme = null;
            lastSyncedFollow = null;
            lastSyncedLock = null;
            lastSetupPreviewKey = null;
            tacticalVisualHashValid = false;
            hudVisualHashValid = false;
            if (refreshOnboardingGlass != null)
                refreshOnboardingGlass();
            if (tacticalMap != null) {
                tacticalMap.TerrainRevision++;
                tacticalMap.RequestRedraw();
            }
            if (hudMap != null) {
                hudMap.TerrainRevision++;
                hudMap.InvalidateVisual();
            }
            Refresh();
        }
        internal void Handle(AppControlAction action) {
            switch (action) {
                case AppControlAction.PingDanger:
                    SendPing(0);
                    break;
                case AppControlAction.PingLoot:
                    SendPing(1);
                    break;
                case AppControlAction.PingPickup:
                    SendPing(2);
                    break;
                case AppControlAction.Settings:
                    ShowSettings();
                    break;
                case AppControlAction.Search:
                    ShowSearch();
                    break;
                case AppControlAction.ToggleOverlay:
                    ToggleHud();
                    break;
                case AppControlAction.AddWaypoint:
                    ShowAddWaypoint();
                    break;
                case AppControlAction.ClearWaypoint:
                    ClearMapRoute();
                    break;
                case AppControlAction.ZoomIn:
                    engine.WpfZoom(TacticalVisible, 1);
                    break;
                case AppControlAction.ZoomOut:
                    engine.WpfZoom(TacticalVisible, -1);
                    break;
                case AppControlAction.StartupGuide:
                    ShowOnboarding();
                    break;
                case AppControlAction.ZoneEditor:
                    ShowZoneWizard();
                    break;
                case AppControlAction.AdminPlayers:
                    ShowWpfAdminPlayers();
                    break;
                default:
                    engine.WpfNativeTool(action);
                    break;
            }
            RefreshInteraction();
        }
        bool exitRequested;
        internal void RequestExit() {
            if (exitRequested)
                return;
            exitRequested = true;
            try {
                SaveLocalRoute();
                SaveTacticalUiLayout();
                ZiloreAiEngine.SaveNow();
                ScumSurvivalTracker.SaveNow();
                engine.WpfSave();
            } catch (Exception ex) { Program.LogException("ExitSave", ex); }
            if (!diagnostic && !applyingUpdateOnExit && UpdatePreferencesStore.Current.AutoApplyOnExitEnabled && engine.WpfReadyUpdateRelease != null) {
                try {
                    if (engine.WpfApplyReadyUpdate(false))
                        Program.LogInfo("SeamlessUpdate", "Applying pre-downloaded GitHub update silently on exit.");
                } catch (Exception uEx) { Program.LogWarn("SeamlessUpdate", "Exit update skipped: " + uEx.Message); }
            }
            // Native admin windows treat user Close as a request to hide. Mark
            // application shutdown first so they cannot cancel or reopen.
            CloseAllWpfAdminProfiles();
            engine.WpfPrepareExit();
            // Finish the tray's native menu callback before ending WPF's loop.
            application.Dispatcher.BeginInvoke(new Action(() => application.Shutdown()));
        }
        internal bool IsSettingsToggleKey(int key, int scan) {
            return settingsToggleOpen && engine.WpfSettingsKeyMatches(key, scan);
        }
        void UpdateSettingsToggle() {
            settingsToggleOpen = settingsWindow != null && settingsWindow.IsVisible && dialogs.Count == 1 && dialogs[0] == settingsWindow;
            RefreshInputOwnership();
        }
        internal void ToggleTactical(bool gameKey = false) {
            if (tactical.IsVisible) {
                bool forward = gameKey && new WindowInteropHelper(tactical).Handle == Native.GetForegroundWindow();
                tacticalStandalone = false;
                tacticalMouseShieldActive = false;
                tactical.Topmost = false;
                tactical.Hide();
                tacticalMap.ClearHover();
                tacticalMap.ResetPoiStream(false);
                if (terrain != null)
                    terrain.TrimCache(20);
                engine.WpfTactical(false);
                SyncHudVisibility(true);
                if (forward && !diagnostic) {
                    var binding = engine.WpfSnapshot(false);
                    Native.ReturnMapKey(gameMapWindow, binding.MapKey, binding.MapScan);
                }
            } else {
                if (dialogs.Count > 0) {
                    if (!gameKey && !diagnostic) {
                        var top = dialogs[dialogs.Count - 1];
                        if (top.WindowState == WindowState.Minimized)
                            top.WindowState = WindowState.Normal;
                        FocusInteractive(top);
                    }
                    return;
                }
                if (setupWindow != null && setupWindow.IsVisible) {
                    if (!gameKey && !diagnostic)
                        ShowSetupWindow(false);
                    return;
                }
                bool gameFocusedNow = !diagnostic && Native.GameFocused();
                tacticalStandalone = !diagnostic && !gameKey && !gameFocusedNow;
                gameMapWindow = diagnostic ? IntPtr.Zero : Native.AdminGameWindow();
                hud.Hide();
                engine.WpfTactical(true);
                engine.WpfFollow();
                tactical.WindowState = WindowState.Normal;
                tactical.Topmost = true;
                if (cachedTacticalHandle != IntPtr.Zero)
                    FitTacticalToMonitor();
                tactical.Show();
                FitTacticalToMonitor();
                if (!diagnostic && cachedTacticalHandle != IntPtr.Zero)
                    Native.EnsureTopmostNoActivate(cachedTacticalHandle, true);
            }
            lastRefresh = -1000;
            Refresh();
        }

        internal bool OwnsForeground(IntPtr handle) {
            // Also called on the keyboard-hook thread. Only read published values.
            if (handle == IntPtr.Zero || inputEditingText)
                return false;
            return handle == System.Threading.Interlocked.CompareExchange(ref inputTacticalHandle, IntPtr.Zero, IntPtr.Zero) || Array.IndexOf(inputDialogHandles, handle) >= 0;
        }
        internal static bool EditingText(IInputElement focus) {
            return focus is TextBox || focus is PasswordBox || focus is ComboBox;
        }
        void FocusInteractive(Window window) {
            if (diagnostic || !window.IsVisible)
                return;
            var handle = new WindowInteropHelper(window).Handle;
            EnsureWindowOnTop(window, true);
            var focused = Keyboard.FocusedElement as DependencyObject;
            window.Cursor = Cursors.Arrow;
            Native.ForceForeground(handle);
            window.Activate();
            if (focused != null && System.Windows.Window.GetWindow(focused) == window)
                Keyboard.Focus((IInputElement)focused);
            else {
                window.Focus();
                window.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }
            if (Native.GetForegroundWindow() == handle)
                ClipCursor(IntPtr.Zero);
        }
        void RestoreGameplayFocus() {
            if (diagnostic || gameMapWindow == IntPtr.Zero)
                return;
            if (Native.IsOurWindow(Native.GetForegroundWindow()) && Native.IsGameWindow(gameMapWindow))
                Native.SetForegroundWindow(gameMapWindow);
        }
        void Modal(Window window) {
            var previousForeground = Native.GetForegroundWindow();
            window.Topmost = true;
            window.ShowInTaskbar = true;
            window.ShowActivated = !diagnostic;
            if (diagnostic) {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -32000;
                window.Top = -32000;
            }
            Window prevDialog = dialogs.Count > 0 ? dialogs[dialogs.Count - 1] : null;
            if (prevDialog != null) {
                window.Owner = prevDialog;
                prevDialog.Topmost = false;
            }
            if (!diagnostic && window.WindowStartupLocation != WindowStartupLocation.Manual) {
                window.WindowStartupLocation = window.Owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            }
            window.Loaded += (s, e) => FocusInteractive(window);
            window.ContentRendered += (s, e) => { UpdateSettingsToggle(); FocusInteractive(window); };
            dialogs.Add(window);
            engine.WpfModal(true);
            if (tactical != null && tactical.IsVisible) {
                tactical.Topmost = false;
            }
            if (hud != null && hud.IsVisible)
                hud.Topmost = false;
            if (setupWindow != null && setupWindow.IsVisible)
                setupWindow.Topmost = false;
            UpdateSettingsToggle();
            try {
                window.ShowDialog();
            } catch (Exception ex) { Program.LogException("ModalShowDialog", ex); } finally {
                dialogs.Remove(window);
                UpdateSettingsToggle();
                SyncModalInput();
                try {
                    if (dialogs.Count == 0)
                        SyncHudVisibility(true);
                } catch { }
                try {
                    engine.WpfSave();
                } catch (Exception ex) { Program.LogException("ModalSave", ex); }
                try {
                    Refresh();
                } catch (Exception ex) { Program.LogException("ModalRefresh", ex); }
                if (dialogs.Count > 0) {
                    var parentDlg = dialogs[dialogs.Count - 1];
                    parentDlg.Topmost = true;
                    FocusInteractive(parentDlg);
                } else if (setupWindow != null && setupWindow.IsVisible) {
                    setupWindow.Topmost = true;
                    EnsureWindowOnTop(setupWindow, true);
                    if (setupHasFocus)
                        FocusInteractive(setupWindow);
                } else if (tactical.IsVisible) {
                    tactical.Topmost = true;
                    if (!diagnostic && cachedTacticalHandle != IntPtr.Zero)
                        Native.EnsureTopmostNoActivate(cachedTacticalHandle, true);
                    if (!tacticalStandalone)
                        RestoreGameplayFocus();
                } else {
                    if (hud != null && hud.IsVisible)
                        EnsureWindowOnTop(hud, true);
                    if (!diagnostic && Native.IsGameWindow(previousForeground))
                        Native.SetForegroundWindow(previousForeground);
                }
            }
        }
        static readonly string[] PreviewSettingTabs = { "general", "shortcuts", "appearance" };
        static readonly string[] PreviewHudShapes = { "Square", "Circle" };
        internal void Preview(string folder) {
            Directory.CreateDirectory(folder);
            engine.WpfPreviewPosition("{X=-336602.375 Y=-270302.625 Z=18853.264|P=0 Y=90 R=0}");
            var d4 = engine.WpfSearch("grid D4").FirstOrDefault();
            if (d4 != null) engine.WpfTarget(d4);
            var end = DateTime.UtcNow.AddSeconds(5);
            while (engine.WpfSnapshot(true).Calculating && DateTime.UtcNow < end) {
                System.Windows.Forms.Application.DoEvents();
                System.Threading.Thread.Sleep(10);
            }
            Refresh();
            Capture(tactical, Path.Combine(folder, "tactical-map.png"));
            for (int i = 0; i < 5; i++)
            using (new WindowLifetime(BuildOnboarding(i)))
                Capture(WindowLifetime.Current, Path.Combine(folder, "onboarding-" + (i + 1) + ".png"));
            for (int i = 0; i < PreviewSettingTabs.Length; i++)
            using (new WindowLifetime(BuildSettings(i)))
                Capture(WindowLifetime.Current, Path.Combine(folder, "settings-" + PreviewSettingTabs[i] + ".png"));
            using (new WindowLifetime(BuildSearch()))
                Capture(WindowLifetime.Current, Path.Combine(folder, "search.png"));
            for (int si = 0; si < PreviewHudShapes.Length; si++) {
                string shape = PreviewHudShapes[si];
                engine.WpfAppearance(shape, 100);
                Refresh();
                Capture(hud, Path.Combine(folder, "hud-" + shape.ToLowerInvariant() + ".png"));
            }
        }
        internal static void Capture(Window window, string file) {
            window.ShowActivated = false;
            window.Left = -32000;
            window.Top = -32000;
            window.Show();
            window.UpdateLayout();
            int pw = Math.Max(1, (int)Math.Ceiling(window.ActualWidth > 0 ? window.ActualWidth : window.Width));
            int ph = Math.Max(1, (int)Math.Ceiling(window.ActualHeight > 0 ? window.ActualHeight : window.Height));
            var bitmap = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(file))
                encoder.Save(stream);
            window.Hide();
        }
        sealed class WindowLifetime : IDisposable {
            internal static Window Current; readonly Window window; internal WindowLifetime(Window window) {
                this.window = window;
                Current = window;
            }
            public void Dispose() {
                window.Close();
            }
        }
        public void Dispose() {
            application.Dispatcher.VerifyAccess();
            if (disposed)
                return;
            disposed = true;
            if (systemThemeHooked) {
                Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged;
                systemThemeHooked = false;
            }
            if (displaySettingsHooked) {
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                displaySettingsHooked = false;
            }
            if (sessionEndingHooked) {
                Microsoft.Win32.SystemEvents.SessionEnding -= OnSessionEnding;
                sessionEndingHooked = false;
            }
            if (!eulaDeclined) {
                try {
                    SaveLocalRoute();
                    SaveTacticalUiLayout();
                    ZiloreAiEngine.SaveNow();
                    ScumSurvivalTracker.SaveNow();
                } catch { }
            }
            if (poiSearchDebounce != null)
                poiSearchDebounce.Stop();
            if (poiFilterDebounce != null)
                poiFilterDebounce.Stop();
            if (tacticalToastTimer != null)
                tacticalToastTimer.Stop();
            if (startupUpdateTimer != null)
                startupUpdateTimer.Stop();
            if (autoLaunch != null)
                autoLaunch.Dispose();
            if (zoneOpenRead != null)
                zoneOpenRead.ContinueWith(task => Program.LogException("ZoneOpenQueue", task.Exception), System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            DisposeRoutePreviews();
            if (privatePositions != null)
                privatePositions.Dispose();
            if (relayGroups != null)
                relayGroups.Dispose();
            refresh.Stop();
            if (cameraSettle != null)
                cameraSettle.Stop();
            if (setupWindow != null)
                setupWindow.Close();
            hudMap.ResetPoiStream();
            tacticalMap.ResetPoiStream();
            hud.Close();
            tactical.Close();
            engine.WpfSave();
            engine.Dispose();
            if (terrain != null)
                terrain.Dispose();
        }
    }
}
