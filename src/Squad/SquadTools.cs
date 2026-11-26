using System;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DrawingPoint = System.Drawing.PointF;

namespace ZiloreMiniMap {
    public sealed partial class MapWindow {
        internal MapPreset WpfCapturePreset(string name) {
            WpfMapSurface.SavePresetFlightExtras(name);
            return new MapPreset { Name = name, Map = mapSidecarPaths.MapId, Categories = scumMap == null ? "" : scumMap.GetDisabledCategoriesString(), ZoneLayers = string.Join(";", disabledZoneLayers), Zones = string.Join(";", disabledWpfZones), Grid = gridBorders, Waypoints = showCustomWaypoints, History = locationHistory, Labels = showZoneLabels, ShowZones = showZones, Poi = showScumMap && (scumMap == null || scumMap.MasterEnabled), Rotate = wpfRotateMap, Cluster = wpfPoiClustering, Opacity = mapOpacity, Zoom = zoom, TacticalOpacity = WpfTacticalOpacityValue, TacticalZoom = fullMapZoom };
        }
        internal bool WpfApplyPreset(MapPreset p) {
            if (p.Map != mapSidecarPaths.MapId)
                return false;
            gridBorders = p.Grid;
            showCustomWaypoints = p.Waypoints;
            locationHistory = p.History;
            showZoneLabels = p.Labels;
            showZones = p.ShowZones;
            showScumMap = p.Poi;
            if (scumMap != null) {
                scumMap.MasterEnabled = p.Poi;
                scumMap.ApplyDisabledCategoriesString(p.Categories);
            }
            disabledZoneLayers.Clear();
            foreach (string s in p.ZoneLayers.Split(';').Where(s => s.Length > 0))
                disabledZoneLayers.Add(s);
            disabledWpfZones.Clear();
            foreach (string s in p.Zones.Split(';').Where(s => s.Length > 0))
                disabledWpfZones.Add(s);
            fullMapOpacity = p.TacticalOpacity;
            fullMapZoom = Math.Max(1, Math.Min(maxZoom, p.TacticalZoom));
            wpfRotateMap = p.Rotate;
            wpfPoiClustering = p.Cluster;
            mapOpacity = p.Opacity;
            zoom = targetZoom = Math.Max(1, Math.Min(maxZoom, p.Zoom));
            WpfMapSurface.ApplyPresetFlightExtras(p.Name);
            SettingsChanged();
            SaveSettings();
            WpfRebuildSettings();
            return true;
        }
    }
    internal sealed partial class WpfFieldConsoleRuntime {
        readonly SquadState squad = new SquadState();
        readonly SquadCoordinatorService squadCoordinator = new SquadCoordinatorService();
        readonly Dictionary<string, TextBlock> playerStatusLabels = new Dictionary<string, TextBlock>(StringComparer.Ordinal);
        string followingPeer; MapZone squadTarget; DateTime nextSquadRoute;
        SharedMapMarker[] activeStops = Array.Empty<SharedMapMarker>(); int activeStop;
        bool squadOnlineOnly;
        void ToggleSquadLead(string ownerId) {
            WpfMapSurface.SquadLeadPeerId = (WpfMapSurface.SquadLeadPeerId == ownerId) ? null : ownerId;
            filterIdentity = null;
            peersCacheValid = false;
            Refresh();
        }
        void SendRallyHere() {
            var stSnap = engine.WpfSnapshot(false);
            if (stSnap.Position == null)
                return;
            var pt = MapWindow.ToMap(stSnap.Position);
            if (CanSendPing())
                SendPing(2, pt);
            NavigateDestination(new MapZone { Name = WpfFieldTheme.UiText("SquadRallyHere"), Points = new[] { pt } });
        }
        void NavigateToLatestRally() {
            var rally = cachedSharedMarkers.LastOrDefault(m => SquadState.IsPing(m) && m.Id.Length >= 10 && m.Id.Substring(8, 2) == "02");
            if (rally == null)
                return;
            NavigateDestination(new MapZone { Name = rally.Owner + " · " + rally.Name, Points = new[] { new DrawingPoint((float)rally.X, (float)rally.Y) } });
        }
        void UpdatePeerSmoothingTargets(PrivatePositions.Peer[] peers) {
            squadCoordinator.UpdateSmoothingTargets(peers);
        }
        void SmoothPeersInPlace(PrivatePositions.Peer[] peers) {
            squadCoordinator.SmoothPeersInPlace(peers, refreshClock.ElapsedMilliseconds);
        }
        readonly FlightTelemetryService flightTelemetry = new FlightTelemetryService();
        internal double CurrentEstimatedSpeedKmh {
            get {
                return flightTelemetry.SpeedKmh;
            }
        }
        internal double CurrentVerticalSpeedMps {
            get {
                return flightTelemetry.VerticalSpeedMps;
            }
        }
        internal void UpdateFlightTelemetry(Position pos, DateTime now) {
            flightTelemetry.Update(pos, now, now);
        }
        internal void UpdateFlightTelemetry(Position pos, DateTime sampleUtc, DateTime nowUtc) {
            flightTelemetry.Update(pos, sampleUtc, nowUtc);
        }
        static string S(string key) {
            return Localization.Get(key);
        }
        void StopSquadNavigation() {
            followingPeer = null;
            activeStops = Array.Empty<SharedMapMarker>();
            activeStop = 0;
            squadTarget = null;
            squadRoutePlanner.Clear();
            activeRouteSignature = null;
            WpfMapSurface.ActiveRouteStopIndex = -1;
            engine.WpfClear();
        }
        static TextBlock SquadHelp(string key) {
            var text = WpfFieldTheme.Text(S(key), 13, WpfFieldTheme.Muted);
            text.TextWrapping = TextWrapping.Wrap;
            text.TextTrimming = TextTrimming.None;
            text.Margin = new Thickness(0, 10, 0, 10);
            return text;
        }
        bool CanSendPing() {
            bool anyReady = privatePositions != null && privatePositions.Ready;
            if (!anyReady && relayGroups != null) {
                var rs = relayGroups.Sessions;
                if (rs != null)
                for (int i = 0; i < rs.Length; i++)
                if (rs[i].Ready) {
                    anyReady = true;
                    break;
                }
            }
            if (!anyReady)
                return false;
            bool squadDialog = dialogs.Count > 0 && dialogs[dialogs.Count - 1].Tag as string == "squad-tools";
            return engine.WpfCanPing(squadDialog);
        }
        static IEnumerable<TextBlock> SquadPlayerLabels(Panel players) {
            return players.Children.OfType<Border>().Select(b => b.Child).OfType<StackPanel>().SelectMany(p => p.Children.OfType<TextBlock>()).Where(t => t.Tag is string);
        }
        void SendPing(int kind, DrawingPoint? at = null) {
            if (!CanSendPing())
                return;
            var state = engine.WpfSnapshot(false);
            if (!at.HasValue && state.Position == null)
                return;
            var p = at ?? MapWindow.ToMap(state.Position);
            if (kind < 0 || kind > SquadStatusKind.MaxKind) return;
            string label = SquadStatusKind.GetDisplayLabel(kind, Localization.Current == AppLanguage.Russian);
            squad.Ping(kind, label, p.X, p.Y);
            Refresh();
        }
        void LocateSquadPeer(string ownerId) {
            var peer = squadCoordinator.FindPeerWithPosition(ownerId);
            if (peer == null || peer.Position == null) return;
            var pt = MapWindow.ToMap(peer.Position);
            if (!tactical.IsVisible) SetTactical(true);
            engine.WpfPan(pt);
            if (tacticalMap != null) tacticalMap.TriggerPeerPulse(ownerId);
            RefreshCamera();
        }
        string PeerStatus(PrivatePositions.Peer p) {
            var selfState = engine.WpfSnapshot(false);
            return squadCoordinator.FormatPeerStatus(p, selfState.Position, activeStops, DateTime.UtcNow, Localization.Current == AppLanguage.Russian);
        }
        void UpdateSquad() {
            var now = DateTime.UtcNow;
            PrivatePositions.Peer[] current = Array.Empty<PrivatePositions.Peer>();
            if (privatePositions != null || (relayGroups != null && relayGroups.Sessions.Length > 0) || squadCoordinator.KnownPeerCount > 0) {
                var ppRoster = privatePositions == null ? null : privatePositions.Roster();
                var rgRoster = relayGroups == null ? null : relayGroups.Roster();
                current = squadCoordinator.SyncRoster(ppRoster, rgRoster, now);
                playerRoster = squadCoordinator.Roster;
            }
            SyncSelectedSharedRoute();
            var state = engine.WpfSnapshot(false);
            // A manual destination or Clear takes precedence over squad navigation.
            if (squadTarget != null && !object.ReferenceEquals(squadTarget, state.Target) || (followingPeer != null && squadTarget == null && state.Target != null)) {
                followingPeer = null;
                activeStops = Array.Empty<SharedMapMarker>();
                activeStop = 0;
                squadTarget = null;
                squadRoutePlanner.Clear();
                activeRouteSignature = null;
                WpfMapSurface.ActiveRouteStopIndex = -1;
            }
            if (followingPeer != null) {
                var peer = current.FirstOrDefault(p => p.Id == followingPeer);
                if (peer == null || peer.Position == null) {
                    bool clear = squadTarget != null;
                    squadTarget = null;
                    if (clear)
                        engine.WpfClear();
                    return;
                }
                var destination = MapWindow.ToMap(peer.Position);
                if (now >= nextSquadRoute && (squadTarget == null || DestinationSearch.Metres(squadTarget.Centroid, destination) >= 25)) {
                    squadTarget = new MapZone { Name = S("SquadFollow") + ": " + peer.Name, Points = new[] { destination } };
                    engine.WpfSquadTarget(squadTarget);
                    nextSquadRoute = now.AddSeconds(2);
                }
            } else if (activeStops.Length > 0 && activeStop < activeStops.Length) {
                int previousStop = activeStop;
                if (state.Position != null && state.Tracking && !state.TrackingPaused && PrivatePositions.Fresh(state.Updated, now)) {
                    var curPt = MapWindow.ToMap(state.Position);
                    UpdateFlightTelemetry(state.Position, state.Updated, now);
                    double avBase = WpfMapSurface.AviationArrivalRadiusM;
                    double arrivalRadius = WpfMapSurface.AviationModeEnabled
                        ? Math.Max(avBase, Math.Min(avBase + 100.0, avBase + flightTelemetry.SpeedMps * 1.8))
                        : Math.Max(40.0, Math.Min(85.0, 40.0 + flightTelemetry.SpeedMps * 1.5));
                    if (DestinationSearch.Metres(curPt, new DrawingPoint((float)activeStops[activeStop].X, (float)activeStops[activeStop].Y)) < arrivalRadius)
                        activeStop++;
                }
                if (activeStop >= activeStops.Length) {
                    StopSquadNavigation();
                } else if (previousStop != activeStop || squadTarget == null || !object.ReferenceEquals(squadTarget, state.Target))
                    SetSquadStop();
                else {
                    var stop = activeStops[activeStop];
                    if (squadTarget.Centroid.X != (float)stop.X || squadTarget.Centroid.Y != (float)stop.Y)
                        SetSquadStop();
                }
            }
        }
        void SetSquadStop() {
            var m = activeStops[activeStop];
            squadTarget = new MapZone { Name = (activeStop + 1) + "/" + activeStops.Length + " · " + m.Name, Points = new[] { new DrawingPoint((float)m.X, (float)m.Y) } };
            engine.WpfSquadTarget(squadTarget);
        }
        void StartSquadRoute(SharedMapMarker[] stops) {
            if (stops == null || stops.Length == 0)
                return;
            followingPeer = null;
            activeStops = stops.Select(m => m.Copy()).ToArray();
            activeStop = 0;
            activeRouteSignature = null;
            SetSquadStop();
            Refresh();
        }
        void SkipActiveSquadStop() {
            if (activeStops.Length == 0)
                return;
            activeStop++;
            if (activeStop >= activeStops.Length) {
                StopSquadNavigation();
            } else {
                SetSquadStop();
            }
            filterIdentity = null;
            Refresh();
        }
        void OptimizeOwnRouteOrder() {
            var stops = squad.Stops;
            if (stops == null || stops.Length < 3)
                return;
            var state = engine.WpfSnapshot(false);
            DrawingPoint? origin = state.Position != null ? (DrawingPoint?)MapWindow.ToMap(state.Position) : null;
            var optimized = RouteOptimizer.OptimizeStopOrder(stops, origin);
            ApplySavedRouteTemplate(new SavedRouteTemplate { Name = "Optimized", Shared = squad.Shared, Stops = optimized });
        }
        void LoopOwnRoute() {
            var stops = squad.Stops;
            if (stops == null || stops.Length < 2 || stops.Length >= SquadState.MaximumStops)
                return;
            var first = stops[0];
            var last = stops[stops.Length - 1];
            if (DestinationSearch.Metres(new DrawingPoint((float)first.X, (float)first.Y), new DrawingPoint((float)last.X, (float)last.Y)) < 25.0)
                return;
            PushRouteUndo();
            if (squad.AddStop(first.Name, first.X, first.Y))
                OwnRouteEdited();
            else
                routeHistory.PopLastUndo();
        }
        bool CopyOwnRouteShareCode() {
            var stops = squad.Stops;
            if (stops == null || stops.Length == 0)
                return false;
            try {
                string code = SavedRouteLibrary.EncodeShareCode(new SavedRouteTemplate { Name = "Route", Shared = squad.Shared, Stops = stops });
                System.Windows.Clipboard.SetText(code);
                return true;
            } catch { return false; }
        }
        static string FormatLegEta(double metres) {
            if (metres < 10)
                return "";
            string dist = DestinationSearch.Distance(metres);
            if (WpfMapSurface.AviationModeEnabled) {
                int airMin = Math.Max(1, (int)Math.Round(metres / 2666.0)); // ~160 km/h cruise airspeed
                return Localization.Current == AppLanguage.Russian
                    ? "✈ " + dist + " · ~" + airMin + " мин полёт"
                    : "✈ " + dist + " · ~" + airMin + "m flight";
            }
            int carMin = Math.Max(1, (int)Math.Round(metres / 1100.0)); // ~66 km/h avg road speed
            int footMin = Math.Max(1, (int)Math.Round(metres / 240.0)); // ~14.4 km/h jog/sprint avg
            return Localization.Current == AppLanguage.Russian
                ? dist + " · ~" + carMin + " мин авто / ~" + footMin + " мин пешком"
                : dist + " · ~" + carMin + "m car / ~" + footMin + "m foot";
        }
        void NavigateDestination(MapZone destination) {
            StopSquadNavigation();
            engine.WpfTarget(destination);
            filterIdentity = null;
            RefreshInteraction();
        }
        void AddSquadDestination(MapZone destination) {
            if (destination != null && squad.AddStop(destination.DisplayName, destination.Centroid.X, destination.Centroid.Y))
                OwnRouteEdited();
        }
        void AddSquadStop(DrawingPoint at) {
            AddSquadDestination(engine.WpfClickedTarget(at, .003f));
        }
        void ShowSquadTools() {
            Modal(BuildSquadTools(1));
        }
        void ShowRoutes() {
            Modal(BuildSquadTools(0));
        }
        void ShowMapPresets() {
            Modal(BuildSquadTools(2));
        }
        Window BuildSquadTools(int initial) {
            var window = Window(S("SquadTools"), 700, 780);
            window.Tag = "squad-tools";
            var body = new StackPanel { Margin = new Thickness(22, 14, 28, 22) };
            var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var layout = new DockPanel();
            var header = new DockPanel { Margin = new Thickness(22, 14, 22, 0) };
            var headerTitle = WpfFieldTheme.Text(S("SquadTools"), 22, null, true);
            header.Children.Add(headerTitle);
            header.MouseLeftButtonDown += (s, e) => { if (e.OriginalSource is TextBlock) window.DragMove(); };
            DockPanel.SetDock(header, Dock.Top);
            layout.Children.Add(header);
            var done = WpfFieldTheme.Button(S("Done"), () => window.Close(), true);
            done.MinWidth = 124;
            done.MinHeight = 36;
            done.HorizontalAlignment = HorizontalAlignment.Right;
            done.Margin = new Thickness(22, 8, 22, 16);
            DockPanel.SetDock(done, Dock.Bottom);
            layout.Children.Add(done);
            layout.Children.Add(scroll);
            WpfFieldTheme.ApplyFluentShell(window, "ZiloreMiniMap — " + S("SquadTools"), layout);
            var tabs = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(22, 12, 22, 4) };
            DockPanel.SetDock(tabs, Dock.Top);
            layout.Children.Insert(1, tabs);
            var pages = new[] { new StackPanel(), new StackPanel(), new StackPanel() };
            var pageHost = body;
            int selected = -1;
            string[] tabKeys = { "RouteList", "SquadTools", "SquadPresets" };
            Action<int> select = index => { selected = index; window.Title = S(tabKeys[index]); headerTitle.Text = window.Title; pageHost.Children.Clear(); pageHost.Children.Add(pages[index]); scroll.ScrollToTop(); for (int i = 0; i < tabs.Children.Count; i++) { var button = (Button)tabs.Children[i]; button.SetResourceReference(Control.BackgroundProperty, i == index ? "ThemeAmber" : "ThemePanel"); button.SetResourceReference(Control.ForegroundProperty, i == index ? "ThemePrimaryInk" : "ThemeInk"); } };
            for (int i = 0; i < 3; i++) {
                int index = i;
                var tab = WpfFieldTheme.Button(S(tabKeys[i]), () => select(index));
                tab.Margin = new Thickness(0, 0, 6, 6);
                tab.Tag = "squad-tab-" + i;
                tabs.Children.Add(tab);
            }
            window.PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Tab && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0) { select((selected + ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0 ? 2 : 1)) % 3); ((Button)tabs.Children[selected]).Focus(); e.Handled = true; } };
            var routePage = pages[0];
            var navHeroStack = new StackPanel();
            var navigation = WpfFieldTheme.Text("", 15.5, null, true);
            navigation.TextWrapping = TextWrapping.Wrap;
            navigation.TextTrimming = TextTrimming.None;
            navHeroStack.Children.Add(navigation);
            Action redraw = null;
            var modes = new WrapPanel { Margin = new Thickness(0, 10, 0, 6) };
            var modeButtons = new List<Button>();
            foreach (RoadRouteMode mode in Enum.GetValues<RoadRouteMode>()) {
                var choice = mode;
                var button = WpfFieldTheme.Button(S("RouteMode" + mode), () => {
                    engine.WpfSetRouteMode(choice);
                    SquadRoutePlanner.ClearLegCache();
                    ownRouteSignature = null;
                    activeRouteSignature = null;
                    UpdateRoutePreviews();
                    filterIdentity = null;
                    Refresh();
                    redraw();
                });
                button.Tag = "route-mode-" + mode;
                button.MinHeight = 32;
                button.Padding = new Thickness(11, 4, 11, 4);
                button.FontSize = 12.5;
                button.Margin = new Thickness(0, 0, 6, 6);
                modeButtons.Add(button);
                modes.Children.Add(button);
            }
            navHeroStack.Children.Add(modes);
            var navActions = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            var addTarget = WpfFieldTheme.Button(S("SquadAddStop"), () => { AddSquadDestination(engine.WpfSnapshot(true).Target); redraw(); });
            addTarget.Tag = "route-add-target";
            addTarget.MinHeight = 32;
            addTarget.FontSize = 12.5;
            addTarget.Padding = new Thickness(10, 4, 10, 4);
            addTarget.Margin = new Thickness(0, 0, 6, 6);
            addTarget.ToolTip = S("MapGpsSavedHelp");
            navActions.Children.Add(addTarget);
            var addHere = WpfFieldTheme.Button(WpfFieldTheme.UiText("RouteAddCurrentPos"), () => { var st = engine.WpfSnapshot(false); if (st.Position != null) { AddSquadStop(MapWindow.ToMap(st.Position)); redraw(); } });
            addHere.Tag = "route-add-here";
            addHere.MinHeight = 32;
            addHere.FontSize = 12.5;
            addHere.Padding = new Thickness(10, 4, 10, 4);
            addHere.Margin = new Thickness(0, 0, 6, 6);
            navActions.Children.Add(addHere);
            var skipStop = WpfFieldTheme.Button(WpfFieldTheme.UiText("RouteSkipStop"), () => { SkipActiveSquadStop(); redraw(); });
            skipStop.Tag = "route-skip-stop";
            skipStop.MinHeight = 32;
            skipStop.FontSize = 12.5;
            skipStop.Padding = new Thickness(10, 4, 10, 4);
            skipStop.Margin = new Thickness(0, 0, 6, 6);
            navActions.Children.Add(skipStop);
            string routeIdentity = null;
            Button aviationBtn = null;
            Func<string> avLabel = () => WpfFieldTheme.UiText(WpfMapSurface.AviationModeEnabled ? "FlightModeOn" : "FlightModeOff");
            aviationBtn = WpfFieldTheme.Button(avLabel(), () => { WpfMapSurface.AviationModeEnabled = !WpfMapSurface.AviationModeEnabled; WpfMapSurface.SaveFlightUiSettings(); aviationBtn.Content = avLabel(); routeIdentity = null; filterIdentity = null; Refresh(); redraw(); }, WpfMapSurface.AviationModeEnabled);
            aviationBtn.Tag = "route-aviation-mode";
            aviationBtn.MinHeight = 32;
            aviationBtn.FontSize = 12.5;
            aviationBtn.Padding = new Thickness(10, 4, 10, 4);
            aviationBtn.Margin = new Thickness(0, 0, 6, 6);
            navActions.Children.Add(aviationBtn);
            var stopNavigation = WpfFieldTheme.Button(S("SquadStopNav"), () => { StopSquadNavigation(); filterIdentity = null; Refresh(); redraw(); });
            stopNavigation.Tag = "route-stop-navigation";
            stopNavigation.MinHeight = 32;
            stopNavigation.FontSize = 12.5;
            stopNavigation.Padding = new Thickness(10, 4, 10, 4);
            stopNavigation.Margin = new Thickness(0, 0, 0, 6);
            navActions.Children.Add(stopNavigation);
            navHeroStack.Children.Add(navActions);
            var navHeroCard = WpfFieldTheme.Card(navHeroStack, new Thickness(14, 12, 14, 8));
            navHeroCard.SetResourceReference(Border.BackgroundProperty, "ThemeAccentCard");
            navHeroCard.SetResourceReference(Border.BorderBrushProperty, "ThemeAccentBorder");
            navHeroCard.Margin = new Thickness(0, 4, 0, 12);
            routePage.Children.Add(navHeroCard);
            var routes = new StackPanel();
            routePage.Children.Add(routes);
            redraw = () => {
                foreach (var button in modeButtons) {
                    bool active = (string)button.Tag == "route-mode-" + engine.WpfRouteMode;
                    button.SetResourceReference(Control.BackgroundProperty, active ? "ThemeAmber" : "ThemePanel");
                    button.SetResourceReference(Control.ForegroundProperty, active ? "ThemePrimaryInk" : "ThemeInk");
                }
                var state = engine.WpfSnapshot(true);
                navigation.Text = S("FieldTarget") + ": " + (state.Target == null ? S("KeyNone") : state.Target.DisplayName) + (state.Target == null ? "" : " · " + state.RouteSummary);
                addTarget.IsEnabled = state.Target != null && activeStops.Length == 0 && followingPeer == null && squad.Stops.Length < SquadState.MaximumStops;
                addHere.IsEnabled = state.Position != null && squad.Stops.Length < SquadState.MaximumStops;
                skipStop.IsEnabled = activeStops.Length > 0 && activeStop < activeStops.Length;
                stopNavigation.IsEnabled = state.Target != null || followingPeer != null || activeStops.Length > 0;
                var local = squad.Stops;
                var remote = SquadState.Routes(ReceivedSquadMarkers());
                var plan = ownRoutePlanner.Plan;
                string identity = StopSignature(local) + "|" + squad.Shared + "|" + ownRoutePlanner.Busy + "|" + (plan == null ? 0 : plan.Revision) + "|" + activeStop + "|" + string.Join("|", remote.Select(r => SquadState.RouteKey(r[0]) + ":" + r[0].Owner + ":" + StopSignature(r)));
                if (identity == routeIdentity)
                    return;
                routeIdentity = identity;
                routes.Children.Clear();
                var hdrRow = new DockPanel { Margin = new Thickness(0, 2, 0, 8) };
                var kind = new StackPanel { Orientation = Orientation.Horizontal };
                var soloBtn = WpfFieldTheme.Button(S("SoloRoute"), () => { ChangeOwnRouteKind(false); redraw(); }, !squad.Shared);
                soloBtn.MinHeight = 28;
                soloBtn.FontSize = 12;
                soloBtn.Padding = new Thickness(10, 3, 10, 3);
                soloBtn.Margin = new Thickness(0, 0, 4, 0);
                var sqBtn = WpfFieldTheme.Button(S("SquadRoute"), () => { ChangeOwnRouteKind(true); redraw(); }, squad.Shared);
                sqBtn.MinHeight = 28;
                sqBtn.FontSize = 12;
                sqBtn.Padding = new Thickness(10, 3, 10, 3);
                kind.Children.Add(soloBtn);
                kind.Children.Add(sqBtn);
                DockPanel.SetDock(kind, Dock.Right);
                hdrRow.Children.Add(kind);
                var hdrStack = new StackPanel();
                hdrStack.Children.Add(WpfFieldTheme.Text(S(squad.Shared ? "SquadRoute" : "SoloRoute") + " · " + local.Length + " / 16", 16.5, null, true));
                string ownSumTxt = state.OwnRouteSummary ?? (ownRoutePlanner.Busy ? S("FieldCalculating") : plan == null ? "" : engine.WpfTravelSummary(plan.Distance, plan.MissingRoads == 0));
                if (!string.IsNullOrWhiteSpace(ownSumTxt))
                    hdrStack.Children.Add(WpfFieldTheme.Text(ownSumTxt, 12.5, WpfFieldTheme.Cyan, true));
                hdrRow.Children.Add(hdrStack);
                routes.Children.Add(hdrRow);
                if (local.Length == 0) {
                    var emptyHint = WpfFieldTheme.Text(WpfFieldTheme.UiText("RouteGesturesHint"), 12.5, WpfFieldTheme.Muted);
                    emptyHint.TextWrapping = TextWrapping.Wrap;
                    emptyHint.TextTrimming = TextTrimming.None;
                    var emptyCard = WpfFieldTheme.Card(emptyHint, new Thickness(12, 10, 12, 10));
                    emptyCard.Margin = new Thickness(0, 0, 0, 8);
                    routes.Children.Add(emptyCard);
                }
                for (int idx = 0; idx < local.Length; idx++) {
                    var marker = local[idx];
                    bool isCurrentStop = activeStops.Length > 0 && activeStops[0].OwnerId == null && idx == activeStop;
                    bool isLastStop = idx == local.Length - 1 && local.Length > 1;
                    var stopRow = new DockPanel();
                    var numCircle = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Background = isCurrentStop ? WpfFieldTheme.Amber : (isLastStop ? WpfFieldTheme.StatusLive : WpfFieldTheme.Cyan) };
                    var numText = new TextBlock { Text = (SquadState.StopIndex(marker) + 1).ToString(CultureInfo.InvariantCulture), FontSize = 12, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    numText.SetResourceReference(TextBlock.ForegroundProperty, "ThemePrimaryInk");
                    numCircle.Child = numText;
                    DockPanel.SetDock(numCircle, Dock.Left);
                    stopRow.Children.Add(numCircle);
                    var remove = WpfFieldTheme.Button(S("SquadDelete"), () => { RemoveOwnRouteStop(marker.Id); redraw(); });
                    remove.Tag = "route-remove:" + marker.Id;
                    remove.MinHeight = 28;
                    remove.FontSize = 12;
                    remove.Padding = new Thickness(8, 2, 8, 2);
                    remove.Margin = new Thickness(4, 0, 0, 0);
                    DockPanel.SetDock(remove, Dock.Right);
                    stopRow.Children.Add(remove);
                    var ren = WpfFieldTheme.Button("✎", () => {
                        var rw = Window(WpfFieldTheme.UiText("RouteRenameTitle"), 420, 190);
                        rw.ResizeMode = ResizeMode.NoResize;
                        var rl = new StackPanel { Margin = new Thickness(18) };
                        var rbox = new TextBox { MaxLength = 48, Text = marker.Name, Padding = new Thickness(8, 5, 8, 5), FontSize = 14 };
                        var rbtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
                        Action rcommit = () => { if (!string.IsNullOrWhiteSpace(rbox.Text)) { RenameOwnRouteStop(marker.Id, rbox.Text); rw.Close(); redraw(); } };
                        var rok = WpfFieldTheme.Button(S("Done"), rcommit, true);
                        rok.Margin = new Thickness(0, 0, 8, 0);
                        var rcn = WpfFieldTheme.Button(S("Cancel"), () => rw.Close());
                        rbtns.Children.Add(rok);
                        rbtns.Children.Add(rcn);
                        rl.Children.Add(rbox);
                        rl.Children.Add(rbtns);
                        rbox.KeyDown += (ks, ke) => { if (ke.Key == System.Windows.Input.Key.Enter) { rcommit(); ke.Handled = true; } };
                        rw.Loaded += (ls, le) => { rbox.Focus(); rbox.SelectAll(); };
                        WpfFieldTheme.ApplyFluentShell(rw, WpfFieldTheme.UiText("RouteRenameTitle"), rl);
                        Modal(rw);
                    });
                    ren.ToolTip = WpfFieldTheme.UiText("RouteRenameTip");
                    ren.MinWidth = 28;
                    ren.MinHeight = 28;
                    ren.Padding = new Thickness(6, 2, 6, 2);
                    ren.Margin = new Thickness(4, 0, 0, 0);
                    DockPanel.SetDock(ren, Dock.Right);
                    stopRow.Children.Add(ren);
                    var down = WpfFieldTheme.Button("↓", () => { MoveOwnRouteStop(marker.Id, 1); redraw(); });
                    down.IsEnabled = idx < local.Length - 1;
                    down.MinWidth = 28;
                    down.MinHeight = 28;
                    down.Padding = new Thickness(6, 2, 6, 2);
                    down.Margin = new Thickness(4, 0, 0, 0);
                    DockPanel.SetDock(down, Dock.Right);
                    stopRow.Children.Add(down);
                    var up = WpfFieldTheme.Button("↑", () => { MoveOwnRouteStop(marker.Id, -1); redraw(); });
                    up.IsEnabled = idx > 0;
                    up.MinWidth = 28;
                    up.MinHeight = 28;
                    up.Padding = new Thickness(6, 2, 6, 2);
                    up.Margin = new Thickness(4, 0, 0, 0);
                    DockPanel.SetDock(up, Dock.Right);
                    stopRow.Children.Add(up);
                    string secTag = DestinationSearch.Sector(new DrawingPoint((float)marker.X, (float)marker.Y));
                    string legInfo = "";
                    if (idx == 0 && state.Position != null)
                        legInfo = FormatLegEta(DestinationSearch.Metres(MapWindow.ToMap(state.Position), new DrawingPoint((float)marker.X, (float)marker.Y)));
                    else if (idx > 0)
                        legInfo = FormatLegEta(DestinationSearch.Metres(new DrawingPoint((float)local[idx - 1].X, (float)local[idx - 1].Y), new DrawingPoint((float)marker.X, (float)marker.Y)));
                    var labelStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    var label = WpfFieldTheme.Text(marker.Name, 14, null, true);
                    label.TextWrapping = TextWrapping.Wrap;
                    label.TextTrimming = TextTrimming.None;
                    labelStack.Children.Add(label);
                    string subLine = secTag + (legInfo.Length > 0 ? " · " + legInfo : "");
                    labelStack.Children.Add(WpfFieldTheme.Text(subLine, 11.5, WpfFieldTheme.Muted));
                    stopRow.Children.Add(labelStack);
                    var stopCard = WpfFieldTheme.Card(stopRow, new Thickness(10, 7, 10, 7));
                    stopCard.Margin = new Thickness(0, 0, 0, 5);
                    if (isCurrentStop)
                        stopCard.SetResourceReference(Border.BorderBrushProperty, "ThemeAmber");
                    routes.Children.Add(stopCard);
                }
                var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
                var own = WpfFieldTheme.Button(S("SquadStartRoute"), () => { StartSquadRoute(local); window.Close(); }, true);
                own.IsEnabled = local.Length > 0;
                own.MinHeight = 34;
                own.Margin = new Thickness(0, 0, 6, 6);
                row.Children.Add(own);
                var undo = WpfFieldTheme.Button(S("SquadUndoStop"), () => { UndoOwnRouteStop(); redraw(); });
                undo.IsEnabled = local.Length > 0;
                undo.MinHeight = 34;
                undo.Margin = new Thickness(0, 0, 6, 6);
                row.Children.Add(undo);
                var opt = WpfFieldTheme.Button(WpfFieldTheme.UiText("RouteOptimize"), () => { OptimizeOwnRouteOrder(); redraw(); });
                opt.IsEnabled = local.Length > 2;
                opt.MinHeight = 34;
                opt.Margin = new Thickness(0, 0, 6, 6);
                opt.ToolTip = WpfFieldTheme.UiText("RouteOptimizeTip");
                row.Children.Add(opt);
                var rev = WpfFieldTheme.Button(WpfFieldTheme.UiText("RouteReverse"), () => { ReverseOwnRoute(); redraw(); });
                rev.IsEnabled = local.Length > 1;
                rev.MinHeight = 34;
                rev.Margin = new Thickness(0, 0, 6, 6);
                row.Children.Add(rev);
                var loopBtn = WpfFieldTheme.Button(WpfFieldTheme.UiText("RouteLoop"), () => { LoopOwnRoute(); redraw(); });
                loopBtn.IsEnabled = local.Length >= 2 && local.Length < SquadState.MaximumStops;
                loopBtn.MinHeight = 34;
                loopBtn.Margin = new Thickness(0, 0, 6, 6);
                loopBtn.ToolTip = WpfFieldTheme.UiText("RouteLoopTip");
                row.Children.Add(loopBtn);
                Button copyOwnBtn = null;
                copyOwnBtn = WpfFieldTheme.Button(WpfFieldTheme.UiText("RouteCopyCode"), () => { if (CopyOwnRouteShareCode()) copyOwnBtn.Content = "✓"; });
                copyOwnBtn.IsEnabled = local.Length > 0;
                copyOwnBtn.MinHeight = 34;
                copyOwnBtn.Margin = new Thickness(0, 0, 6, 6);
                copyOwnBtn.ToolTip = WpfFieldTheme.UiText("RouteCopyCodeTip");
                row.Children.Add(copyOwnBtn);
                var clear = WpfFieldTheme.Button(S("WpfClearRoute"), () => { ClearOwnRoute(); redraw(); });
                clear.IsEnabled = local.Length > 0;
                clear.MinHeight = 34;
                clear.Margin = new Thickness(0, 0, 0, 6);
                row.Children.Add(clear);
                routes.Children.Add(row);
                foreach (var route in remote) {
                    var copy = route;
                    var rb = WpfFieldTheme.Button(S("SquadStartRoute") + ": " + route[0].Owner + " · " + route.Length, () => { StartSquadRoute(copy); window.Close(); });
                    rb.Margin = new Thickness(0, 2, 0, 4);
                    routes.Children.Add(rb);
                }
            };
            redraw();
            string templatesFile = Path.Combine(engine.WpfDataFolder, "saved-routes.tsv");
            var savedTemplates = SavedRouteLibrary.Load(templatesFile);
            var tplSection = new StackPanel { Margin = new Thickness(0, 14, 0, 4) };
            tplSection.Children.Add(new Border { Height = 1, Background = WpfFieldTheme.Line, Margin = new Thickness(0, 0, 0, 10) });
            tplSection.Children.Add(WpfFieldTheme.Text(WpfFieldTheme.UiText("RouteSavedTitle"), 16, null, true));
            var tplSaveRow = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
            var tplName = new TextBox { MaxLength = 48, Padding = new Thickness(8, 5, 8, 5), FontSize = 13.5, Text = Localization.Current == AppLanguage.Russian ? "Мой маршрут" : "My route" };
            var tplList = new StackPanel();
            Action renderTemplates = null;
            renderTemplates = () => {
                tplList.Children.Clear();
                foreach (var tpl in savedTemplates.ToArray()) {
                    var item = tpl;
                    var trow = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                    var del = WpfFieldTheme.Button(S("SquadDelete"), () => { savedTemplates.Remove(item); SavedRouteLibrary.Save(templatesFile, savedTemplates); renderTemplates(); });
                    del.MinHeight = 30;
                    del.Padding = new Thickness(8, 2, 8, 2);
                    del.Margin = new Thickness(4, 0, 0, 0);
                    DockPanel.SetDock(del, Dock.Right);
                    trow.Children.Add(del);
                    Button copyCode = null;
                    copyCode = WpfFieldTheme.Button(Localization.Current == AppLanguage.Russian ? "Код" : "Code", () => { try { System.Windows.Clipboard.SetText(SavedRouteLibrary.EncodeShareCode(item)); copyCode.Content = "✓"; } catch { } });
                    copyCode.ToolTip = Localization.Current == AppLanguage.Russian ? "Скопировать код маршрута для Discord" : "Copy route share code for Discord";
                    copyCode.MinHeight = 30;
                    copyCode.Padding = new Thickness(8, 2, 8, 2);
                    copyCode.Margin = new Thickness(6, 0, 0, 0);
                    DockPanel.SetDock(copyCode, Dock.Right);
                    trow.Children.Add(copyCode);
                    var load = WpfFieldTheme.Button(item.Name + " (" + item.Stops.Length + ")", () => { ApplySavedRouteTemplate(item); redraw(); });
                    load.HorizontalContentAlignment = HorizontalAlignment.Left;
                    load.MinHeight = 32;
                    trow.Children.Add(load);
                    tplList.Children.Add(trow);
                }
            };
            var tplSaveBtn = WpfFieldTheme.Button(WpfFieldTheme.UiText("RouteSaveCurrent"), () => {
                var stops = squad.Stops;
                if (stops.Length == 0)
                    return;
                string n = tplName.Text.Trim();
                if (n.Length == 0)
                    return;
                savedTemplates.RemoveAll(t => string.Equals(t.Name, n, StringComparison.OrdinalIgnoreCase));
                savedTemplates.Insert(0, new SavedRouteTemplate { Name = n, Shared = squad.Shared, Stops = stops });
                if (savedTemplates.Count > 32)
                    savedTemplates.RemoveRange(32, savedTemplates.Count - 32);
                SavedRouteLibrary.Save(templatesFile, savedTemplates);
                renderTemplates();
            }, true);
            tplSaveBtn.MinHeight = 32;
            tplSaveBtn.Padding = new Thickness(10, 4, 10, 4);
            tplSaveBtn.Margin = new Thickness(8, 0, 0, 0);
            Button tplImportBtn = null;
            tplImportBtn = WpfFieldTheme.Button(WpfFieldTheme.UiText("RouteImportClipboard"), () => {
                try {
                    string clip = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : "";
                    SavedRouteTemplate imported;
                    if (SavedRouteLibrary.TryDecodeShareCode(clip, out imported)) {
                        savedTemplates.RemoveAll(t => string.Equals(t.Name, imported.Name, StringComparison.OrdinalIgnoreCase));
                        savedTemplates.Insert(0, imported);
                        if (savedTemplates.Count > 32)
                            savedTemplates.RemoveRange(32, savedTemplates.Count - 32);
                        SavedRouteLibrary.Save(templatesFile, savedTemplates);
                        ApplySavedRouteTemplate(imported);
                        renderTemplates();
                        redraw();
                        tplImportBtn.Content = "✓";
                    } else {
                        ShowFluentNotice(window, Localization.Current == AppLanguage.Russian ? "В буфере обмена нет корректного кода ZLR-ROUTE:..." : "No valid ZLR-ROUTE:... code found in clipboard.", S("RouteList"), false);
                    }
                } catch { }
            });
            tplImportBtn.MinHeight = 32;
            tplImportBtn.Padding = new Thickness(10, 4, 10, 4);
            tplImportBtn.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(tplImportBtn, Dock.Right);
            DockPanel.SetDock(tplSaveBtn, Dock.Right);
            tplSaveRow.Children.Add(tplImportBtn);
            tplSaveRow.Children.Add(tplSaveBtn);
            tplSaveRow.Children.Add(tplName);
            tplSection.Children.Add(tplSaveRow);
            tplSection.Children.Add(tplList);
            renderTemplates();
            routePage.Children.Add(tplSection);
            var squadPage = pages[1];
            squadPage.Children.Add(WpfFieldTheme.Button(S("PrivatePositions"), ShowPrivatePositions));
            squadPage.Children.Add(SquadHelp("SquadPingHelp"));
            var rallyRow = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 6, 0, 6) };
            var rallyHereBtn = WpfFieldTheme.Button(WpfFieldTheme.UiText("SquadRallyHere"), SendRallyHere, true);
            rallyHereBtn.ToolTip = WpfFieldTheme.UiText("SquadRallyTip");
            rallyHereBtn.MinHeight = 34;
            rallyHereBtn.Margin = new Thickness(0, 0, 4, 0);
            var goToRallyBtn = WpfFieldTheme.Button(WpfFieldTheme.UiText("SquadGoToRally"), NavigateToLatestRally);
            goToRallyBtn.MinHeight = 34;
            goToRallyBtn.Margin = new Thickness(4, 0, 0, 0);
            rallyRow.Children.Add(rallyHereBtn);
            rallyRow.Children.Add(goToRallyBtn);
            squadPage.Children.Add(rallyRow);
            var pingRow = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 4, 0, 10) };
            var pingButtons = new List<Button>();
            string[] keys = { "PingDanger", "PingLoot", "PingPickup" };
            for (int i = 0; i < 3; i++) {
                int k = i;
                var ping = WpfFieldTheme.Button(S(keys[i]), () => SendPing(k));
                ping.Tag = "squad-ping-" + i;
                ping.MinHeight = 36;
                ping.Margin = new Thickness(0, 0, 6, 0);
                pingButtons.Add(ping);
                pingRow.Children.Add(ping);
            }
            squadPage.Children.Add(pingRow);
            bool ruSq = Localization.Current == AppLanguage.Russian;
            var statusHdr = WpfFieldTheme.Text(ruSq ? "ТАКТИЧЕСКИЙ СТАТУС ОТРЯДА (60–90с)" : "SQUAD TACTICAL STATUS (60–90s)", 11, WpfFieldTheme.Muted, true);
            statusHdr.Margin = new Thickness(0, 2, 0, 5);
            squadPage.Children.Add(statusHdr);
            var statusRow = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 10) };
            foreach (int sk in new[] { SquadStatusKind.StatusCombat, SquadStatusKind.StatusLooting, SquadStatusKind.StatusNeedHelp }) {
                int statusKind = sk;
                var stBtn = WpfFieldTheme.Button(SquadStatusKind.GetDisplayLabel(statusKind, ruSq), () => SendPing(statusKind));
                stBtn.Tag = "squad-status-" + statusKind;
                stBtn.MinHeight = 34;
                stBtn.FontSize = 12.5;
                stBtn.Margin = new Thickness(0, 0, 6, 0);
                if (statusKind == SquadStatusKind.StatusCombat || statusKind == SquadStatusKind.StatusNeedHelp) stBtn.Foreground = WpfFieldTheme.Danger;
                pingButtons.Add(stBtn);
                statusRow.Children.Add(stBtn);
            }
            squadPage.Children.Add(statusRow);
            var players = new StackPanel();
            squadPage.Children.Add(players);
            string playerIdentity = null;
            Action update = () => { redraw(); bool ready = CanSendPing(); foreach (var ping in pingButtons) ping.IsEnabled = ready; string identity = string.Join(";", playerRoster.Select(p => p.Id + ":" + p.Name + ":" + (p.Position != null) + ":" + PlayerVisibility(p.Id))) + "|" + followingPeer; if (identity != playerIdentity) { playerIdentity = identity; players.Children.Clear(); AddPlayers(players, false); } foreach (var label in SquadPlayerLabels(players)) { var peer = playerRoster.FirstOrDefault(p => p.Id == (string)label.Tag); if (peer != null) label.Text = PeerStatus(peer); } };
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => { if (window.IsVisible) update(); };
            window.Loaded += (s, e) => { update(); timer.Start(); };
            window.Closed += (s, e) => timer.Stop();
            body = pages[2];
            body.Children.Add(WpfFieldTheme.Text(S("SquadPresets"), 19, null, true));
            string file = Path.Combine(engine.WpfDataFolder, "map-presets.dat");
            var presets = new List<MapPreset>();
            bool presetReadable = true;
            var presetStatus = WpfFieldTheme.Text("", 13, WpfFieldTheme.Amber);
            presetStatus.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(presetStatus);
            try {
                presets.AddRange(MapPresetFile.Load(file));
            } catch (Exception e) { presetReadable = false; Program.LogException("MapPresets", e); presetStatus.Text = S("PresetReadFailed"); }
            var presetName = new TextBox { MaxLength = 64, Text = S("SquadLooting"), Margin = new Thickness(0, 10, 0, 12), Padding = new Thickness(8, 6, 8, 6) };
            System.Windows.Automation.AutomationProperties.SetName(presetName, S("SquadPresets"));
            body.Children.Add(presetName);
            var names = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            foreach (string key in new[] { "SquadLooting", "SquadDriving", "SquadExploring" }) {
                string n = S(key);
                names.Children.Add(WpfFieldTheme.Button(n, () => presetName.Text = n));
            }
            body.Children.Add(names);
            var saved = new StackPanel();
            Action showSaved = null;
            Action save = () => { try { MapPresetFile.Save(file, presets); presetStatus.Text = ""; showSaved(); } catch (Exception e) { Program.LogException("MapPresetsSave", e); presetStatus.Text = S("LocalSaveFailed"); throw; } };
            showSaved = () => { saved.Children.Clear(); foreach (var p in presets.ToArray()) { var row = new WrapPanel(); row.Children.Add(WpfFieldTheme.Button(p.Name, () => { if (engine.WpfApplyPreset(p)) { filterIdentity = null; Refresh(); } })); row.Children.Add(WpfFieldTheme.Button(S("SquadDelete"), () => { int index = presets.IndexOf(p); presets.RemoveAt(index); try { save(); } catch { presets.Insert(index, p); } })); saved.Children.Add(row); } };
            var savePreset = WpfFieldTheme.Button(S("SquadSavePreset"), () => { if (!presetReadable) return; string name = presetName.Text.Trim(); if (name.Length == 0) return; var previous = presets.ToArray(); var p = engine.WpfCapturePreset(name); presets.RemoveAll(v => v.Name == name && v.Map == p.Map); if (presets.Count >= 24) { presets.Clear(); presets.AddRange(previous); return; } presets.Add(p); try { save(); } catch { presets.Clear(); presets.AddRange(previous); } });
            savePreset.IsEnabled = presetReadable;
            body.Children.Add(savePreset);
            if (!presetReadable) {
                Button recover = null;
                recover = WpfFieldTheme.Button(S("PresetRecover"), () => {
                    if (!ShowFluentConfirm(window, S("PresetReadFailed"), S("PresetRecover"), true))
                        return;
                    try {
                        string backup = MapPresetFile.Recover(file);
                        presetReadable = true;
                        savePreset.IsEnabled = true;
                        recover.IsEnabled = false;
                        presetStatus.Text = S("PresetRecover") + ": " + backup;
                    } catch (Exception e) { Program.LogException("MapPresetsRecover", e); presetStatus.Text = S("LocalSaveFailed"); }
                });
                recover.Tag = "preset-recover";
                body.Children.Add(recover);
            }
            body.Children.Add(saved);
            showSaved();
            select(Math.Max(0, Math.Min(2, initial)));
            return window;
        }
    }
}
