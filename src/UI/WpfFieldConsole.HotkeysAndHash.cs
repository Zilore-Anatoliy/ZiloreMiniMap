using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ZiloreMiniMap {
    internal sealed partial class WpfFieldConsoleRuntime {
        static ulong MixHash(ulong h, ulong v) {
            unchecked {
                h ^= v;
                return h * 1099511628211UL;
            }
        }
        static ulong MixInt(ulong h, int v) {
            return MixHash(h, (ulong)(uint)v);
        }
        static ulong MixBool(ulong h, bool v) {
            return MixHash(h, v ? 1UL : 0UL);
        }
        static ulong MixStr(ulong h, string s) {
            if (s == null)
                return MixHash(h, 0x9E3779B97F4A7C15UL);
            unchecked {
                h = MixInt(h, s.Length);
                for (int i = 0; i < s.Length; i++) {
                    h ^= s[i];
                    h *= 1099511628211UL;
                }
                return h;
            }
        }
        static ulong VisualIdentityHash(WpfMapState state) {
            unchecked {
                ulong h = 14695981039346656037UL;
                h = MixStr(h, Localization.CurrentCode);
                h = MixStr(h, WpfFieldTheme.ActiveTheme);
                if (state.Position != null) {
                    h = MixHash(h, (ulong)(long)Math.Round(state.Position.X));
                    h = MixHash(h, (ulong)(long)Math.Round(state.Position.Y));
                    h = MixInt(h, (int)Math.Round(state.Position.Yaw * 10.0));
                } else {
                    h = MixHash(h, 0x1111UL);
                }
                h = MixInt(h, state.Target == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(state.Target));
                h = MixInt(h, state.Route == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(state.Route));
                int flags = (state.Calculating ? 1 : 0) | (state.Grid ? 2 : 0) | (state.Zones ? 4 : 0) | (state.Waypoints ? 8 : 0) | (state.History ? 16 : 0) | (state.Labels ? 32 : 0) | (state.Tracking ? 64 : 0) | (state.Heading ? 128 : 0) | (state.Compass ? 256 : 0) | (state.Status ? 512 : 0) | (state.StatusAbove ? 1024 : 0) | (state.RotateMap ? 2048 : 0) | (state.PoiClustering ? 4096 : 0) | (WpfMapSurface.MinimapPoiClusteringEnabled ? 8192 : 0) | (WpfMapSurface.AviationModeEnabled ? 16384 : 0) | (WpfMapSurface.DebugProfilerEnabled ? 32768 : 0) | (state.SquadPlanning ? 65536 : 0) | (state.SquadNavigating ? 131072 : 0) | (state.ActiveSquadShared ? 262144 : 0) | (state.GridLabels ? 524288 : 0) | (state.SmartLabels ? 1048576 : 0) | (state.CustomZonesExpanded ? 2097152 : 0) | (state.TrackingPaused ? 4194304 : 0);
                h = MixInt(h, flags);
                h = MixInt(h, state.HudWidth);
                h = MixInt(h, state.HudHeight);
                h = MixInt(h, state.HudLeft);
                h = MixInt(h, state.HudTop);
                h = MixInt(h, state.PlayerColor);
                h = MixInt(h, state.RouteColor);
                h = MixInt(h, state.HistoryColor);
                if (state.Peers != null) {
                    h = MixInt(h, state.Peers.Length);
                    for (int i = 0; i < state.Peers.Length; i++) {
                        var peer = state.Peers[i];
                        h = MixStr(h, peer.Id);
                        h = MixStr(h, peer.Name);
                        if (peer.Position != null) {
                            h = MixHash(h, (ulong)(long)Math.Round(peer.Position.X));
                            h = MixHash(h, (ulong)(long)Math.Round(peer.Position.Y));
                            h = MixInt(h, (int)Math.Round(peer.Position.Yaw * 10.0));
                        }
                    }
                }
                if (state.SharedMarkers != null) {
                    h = MixInt(h, state.SharedMarkers.Length);
                    bool hasPing = false;
                    for (int i = 0; i < state.SharedMarkers.Length; i++) {
                        var m = state.SharedMarkers[i];
                        if (SquadState.IsPing(m)) hasPing = true;
                        h = MixStr(h, m.OwnerId);
                        h = MixStr(h, m.Id);
                        h = MixStr(h, m.Name);
                        h = MixBool(h, m.Death);
                        h = MixInt(h, (int)Math.Round(m.X * 100000.0));
                        h = MixInt(h, (int)Math.Round(m.Y * 100000.0));
                    }
                    if (hasPing) h = MixHash(h, (ulong)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond));
                }
                h = MixInt(h, state.SquadPlan == null ? -1 : state.SquadPlan.Revision);
                h = MixInt(h, state.OwnRoutePlan == null ? -1 : state.OwnRoutePlan.Revision);
                if (state.OtherRoutePlans != null) {
                    h = MixInt(h, state.OtherRoutePlans.Length);
                    for (int i = 0; i < state.OtherRoutePlans.Length; i++) {
                        var rt = state.OtherRoutePlans[i];
                        h = MixStr(h, rt.Key);
                        h = MixBool(h, rt.Shared);
                        h = MixInt(h, rt.Plan == null ? -1 : rt.Plan.Revision);
                    }
                }
                h = MixStr(h, state.ActiveSquadRoute);
                h = MixStr(h, state.OwnRouteSummary);
                h = MixStr(h, state.LocalPeerId);
                h = MixInt(h, state.GridOpacity);
                h = MixInt(h, state.LabelSize);
                h = MixInt(h, state.ZoneRevision);
                h = MixStr(h, state.Store == null ? "" : state.Store.GetFilterHashKey());
                if (state.Saved != null) {
                    h = MixInt(h, System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(state.Saved));
                    for (int i = 0; i < state.Saved.Length; i++) {
                        if (state.Saved[i].IsDeathMarker)
                            h = MixStr(h, state.Saved[i].DisplayName);
                    }
                }
                h = MixInt(h, state.CustomZones == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(state.CustomZones));
                h = MixInt(h, state.Trail == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(state.Trail));
                h = MixStr(h, state.Location);
                h = MixStr(h, state.Shape);
                h = MixStr(h, state.RouteSummary);
                h = MixStr(h, state.TrackingStatus);
                h = MixInt(h, state.Opacity);
                h = MixInt(h, state.MapKey);
                h = MixInt(h, state.MapScan);
                h = MixInt(h, state.ChatKey);
                h = MixInt(h, state.CopyKey);
                h = MixInt(h, state.CopyModifier);
                h = MixHash(h, (ulong)BitConverter.DoubleToInt64Bits(Math.Round(state.Zoom, 4)));
                h = MixHash(h, (ulong)BitConverter.DoubleToInt64Bits(Math.Round(state.Pan.X, 6)));
                h = MixHash(h, (ulong)BitConverter.DoubleToInt64Bits(Math.Round(state.Pan.Y, 6)));
                h = MixInt(h, (int)Math.Round(WpfMapSurface.CurrentGroundSpeedMps * 2.0));
                return h;
            }
        }
        long lastTacticalHotkeyMs = -1000;
        string poiSearchQuery = "";
        internal void ShowTacticalToast(string message) {
            if (diagnostic || tacticalToastCard == null || tacticalToastText == null || string.IsNullOrWhiteSpace(message))
                return;
            tacticalToastText.Text = message;
            tacticalToastCard.Visibility = Visibility.Visible;
            if (tacticalToastTimer == null) {
                tacticalToastTimer = new DispatcherTimer(DispatcherPriority.Background, application.Dispatcher) { Interval = TimeSpan.FromMilliseconds(2000) };
                tacticalToastTimer.Tick += (s, e) => { tacticalToastTimer.Stop(); if (tacticalToastCard != null) tacticalToastCard.Visibility = Visibility.Collapsed; };
            }
            tacticalToastTimer.Stop();
            tacticalToastTimer.Start();
        }
        void ToggleAviationHotkey() {
            WpfMapSurface.AviationModeEnabled = !WpfMapSurface.AviationModeEnabled;
            System.Threading.Tasks.Task.Run(() => WpfMapSurface.SaveFlightUiSettings());
            ownRouteSignature = null;
            activeRouteSignature = null;
            filterIdentity = null;
            RefreshInteraction();
            ShowTacticalToast(WpfFieldTheme.UiText(WpfMapSurface.AviationModeEnabled ? "FlightModeOn" : "FlightModeOff"));
        }
        void ToggleVoiceHotkey() {
            bool next = !engine.WpfVoiceEnabled;
            engine.WpfVoiceEnabled = next;
            engine.WpfScheduleSave();
            filterIdentity = null;
            RefreshInteraction();
            bool ru = Localization.Current == AppLanguage.Russian;
            ShowTacticalToast(next ? (ru ? "🔊 Голосовая навигация: ВКЛ" : "🔊 Voice navigation: ON") : (ru ? "🔇 Голосовая навигация: ВЫКЛ" : "🔇 Voice navigation: OFF"));
        }
        void CycleRouteModeHotkey() {
            var next = (RoadRouteMode)(((int)engine.WpfRouteMode + 1) % 3);
            engine.WpfSetRouteMode(next);
            SquadRoutePlanner.ClearLegCache();
            ownRouteSignature = null;
            activeRouteSignature = null;
            UpdateRoutePreviews();
            filterIdentity = null;
            RefreshInteraction();
            ShowTacticalToast("🛣 " + S("RouteMode" + next));
        }
        void CycleAvoidZonesHotkey() {
            int next = (engine.WpfAvoidZonesMode + 1) % 3;
            engine.WpfAvoidZonesMode = next;
            ownRouteSignature = null;
            activeRouteSignature = null;
            UpdateRoutePreviews();
            RefreshInteraction();
            bool ru = Localization.Current == AppLanguage.Russian;
            string[] labels = ru ? new[] { "Выкл", "Все зоны", "Опасные / PvP" } : new[] { "Off", "All zones", "Danger / PvP" };
            ShowTacticalToast("🛡 " + (ru ? "Объезд зон: " : "Zone avoid: ") + labels[next]);
        }
        void CycleThemeHotkey() {
            string[] order = { "Dark", "OLED", "Light", "System" };
            int cur = Array.FindIndex(order, t => string.Equals(t, engine.WpfUiTheme, StringComparison.OrdinalIgnoreCase));
            string next = order[(cur + 1) % order.Length];
            ApplyUiTheme(next);
            ShowTacticalToast("🎨 " + WpfFieldTheme.UiText("Theme" + (next == "OLED" ? "Oled" : next)));
        }
        bool HandleTacticalHotkey(int vk, ModifierKeys mods) {
            bool ru = Localization.Current == AppLanguage.Russian;
            if (mods == ModifierKeys.None) {
                if (vk == 0x70) {
                    ShowHotkeysAndRoutesGuide(0);
                    return true;
                }
                if (vk == 0x75) {
                    ShowZiloreAi();
                    return true;
                }
                if (vk == 0x76) {
                    ShowSurvivalTracker();
                    return true;
                }
                if (vk == 0x77) {
                    ShowGameClockDialog();
                    return true;
                }
                if (vk == 0x78) {
                    ShowRoutes();
                    return true;
                }
            }
            if (mods == ModifierKeys.Shift && vk == 0x70) {
                ShowUserGuide(0);
                return true;
            }
            if (mods == ModifierKeys.Control) {
                if (vk == 0x46) {
                    ShowSearch();
                    return true;
                }
                if (vk == 0x5A || vk == 0x59) {
                    long nowUr = refreshClock.ElapsedMilliseconds;
                    if (nowUr - lastTacticalHotkeyMs < 120)
                        return true;
                    lastTacticalHotkeyMs = nowUr;
                    bool ok = vk == 0x5A ? UndoRouteHistory() : RedoRouteHistory();
                    if (ok)
                        ShowTacticalToast(vk == 0x5A ? (ru ? "↶ Отмена правки маршрута" : "↶ Undo route edit") : (ru ? "↷ Повтор правки маршрута" : "↷ Redo route edit"));
                    return ok;
                }
            }
            if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && vk == 0x43) {
                if (CopyOwnRouteShareCode()) {
                    ShowTacticalToast(WpfFieldTheme.UiText("RouteCopyCode") + " ✓");
                    return true;
                }
                return false;
            }
            bool isLayerKey = (vk >= 0x31 && vk <= 0x35);
            bool isZoomKey = (vk == 0xBB || vk == 0x6B || vk == 0xBD || vk == 0x6D || vk == 0x30 || vk == 0x60);
            bool isToolKey = (vk == 0x52 || vk == 0x43 || vk == 0x46 || vk == 0x56 || vk == 0x47 || vk == 0x42 || vk == 0x4E || vk == 0x4F || vk == 0x4C || vk == 0x58 || vk == 0x48 || vk == 0x54);
            if (!isLayerKey && !isZoomKey && !isToolKey)
                return false;
            if (isLayerKey) {
                if (WpfMapSurface.AltLayerHotkeysEnabled) {
                    if (mods != ModifierKeys.Alt)
                        return false;
                } else {
                    if (mods != ModifierKeys.None)
                        return false;
                }
            } else {
                if (mods != ModifierKeys.None)
                    return false;
            }
            long now = refreshClock.ElapsedMilliseconds;
            if (now - lastTacticalHotkeyMs < 120)
                return true;
            lastTacticalHotkeyMs = now;
            var st = engine.WpfSnapshot(false);
            if (vk == 0x31) {
                engine.WpfLayer("zones", !st.Zones);
                RefreshInteraction();
                ShowTacticalToast(Localization.Get("ShowSavedZones") + ": " + (!st.Zones ? "ON" : "OFF"));
                return true;
            }
            if (vk == 0x32) {
                engine.WpfLayer("waypoints", !st.Waypoints);
                RefreshInteraction();
                ShowTacticalToast(Localization.Get("ShowCustomWaypoints") + ": " + (!st.Waypoints ? "ON" : "OFF"));
                return true;
            }
            if (vk == 0x33) {
                engine.WpfLayer("grid", !st.Grid);
                RefreshInteraction();
                ShowTacticalToast(Localization.Get("GridBorders") + ": " + (!st.Grid ? "ON" : "OFF"));
                return true;
            }
            if (vk == 0x34) {
                engine.WpfLayer("history", !st.History);
                RefreshInteraction();
                ShowTacticalToast(Localization.Get("HistoryEnabled") + ": " + (!st.History ? "ON" : "OFF"));
                return true;
            }
            if (vk == 0x35) {
                engine.WpfLayer("labels", !st.Labels);
                RefreshInteraction();
                ShowTacticalToast(Localization.Get("ShowZoneLabels") + ": " + (!st.Labels ? "ON" : "OFF"));
                return true;
            }
            if (vk == 0x52) {
                tacticalMap.ClearRuler();
                ShowTacticalToast(WpfFieldTheme.UiText("MapMeasureClear"));
                return true;
            }
            if (vk == 0x43) {
                engine.WpfFollow();
                tacticalMap.TriggerPlayerPulse();
                Refresh();
                return true;
            }
            if (vk == 0x46) {
                ToggleAviationHotkey();
                return true;
            }
            if (vk == 0x56) {
                ToggleVoiceHotkey();
                return true;
            }
            if (vk == 0x47) {
                CycleRouteModeHotkey();
                return true;
            }
            if (vk == 0x42) {
                CycleAvoidZonesHotkey();
                return true;
            }
            if (vk == 0x4E) {
                if (activeStops.Length > 0 && activeStop < activeStops.Length) {
                    SkipActiveSquadStop();
                    ShowTacticalToast(WpfFieldTheme.UiText("RouteSkipStop"));
                } else
                    ShowTacticalToast(ru ? "Нет активного маршрута" : "No active route");
                return true;
            }
            if (vk == 0x4F) {
                if (squad.Stops.Length > 2) {
                    OptimizeOwnRouteOrder();
                    ShowTacticalToast(WpfFieldTheme.UiText("RouteOptimize"));
                } else
                    ShowTacticalToast(ru ? "Нужно 3+ точек для оптимизации" : "Need 3+ stops to optimize");
                return true;
            }
            if (vk == 0x4C) {
                bool nextLock = !engine.WpfTacticalZoomLocked;
                engine.WpfSetTacticalZoomLocked(nextLock);
                SyncCameraControls(engine.WpfSnapshot(true));
                ShowTacticalToast(nextLock ? (ru ? "🔒 Масштаб зафиксирован" : "🔒 Zoom locked") : (ru ? "🔓 Масштаб разблокирован" : "🔓 Zoom unlocked"));
                return true;
            }
            if (vk == 0x58) {
                ClearMapRoute();
                ShowTacticalToast(Localization.Get("WpfClearRoute"));
                return true;
            }
            if (vk == 0x48) {
                if (sidebarToggleButton != null)
                    sidebarToggleButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return true;
            }
            if (vk == 0x54) {
                CycleThemeHotkey();
                return true;
            }
            if (vk == 0xBB || vk == 0x6B) {
                engine.WpfZoom(true, 1);
                RefreshCamera();
                return true;
            }
            if (vk == 0xBD || vk == 0x6D) {
                engine.WpfZoom(true, -1);
                RefreshCamera();
                return true;
            }
            if (vk == 0x30 || vk == 0x60) {
                engine.WpfNativeTool(AppControlAction.ResetZoom);
                RefreshInteraction();
                ShowTacticalToast("1.0×");
                return true;
            }
            return false;
        }
        internal bool TryDismissTactical(int key, int scan, bool chatPaused) {
            if (!tactical.IsVisible || dialogs.Count != 0 || EditingText(Keyboard.FocusedElement))
                return false;
            if (!chatPaused && HandleTacticalHotkey(key, Keyboard.Modifiers))
                return true;
            var binding = engine.WpfSnapshot(false);
            bool mapKey = PhysicalKeyCapture.Matches(key, scan, binding.MapKey, binding.MapScan);
            if (key != 0x1B && key != 0x09 && (!mapKey || chatPaused))
                return false;
            if (key == 0x1B && tacticalMap.ContextMenuOpen) {
                tacticalMap.CloseContextMenu();
                return true;
            }
            if (key == 0x1B && tacticalMap.RulerModeActive) {
                tacticalMap.ClearRuler();
                ShowTacticalToast(WpfFieldTheme.UiText("RulerModeOffToast"));
                return true;
            }
            CloseTactical(mapKey && new WindowInteropHelper(tactical).Handle == Native.GetForegroundWindow());
            return true;
        }
        static bool IsInsideTextInput(DependencyObject obj) {
            while (obj != null) {
                if (obj is TextBoxBase || obj is PasswordBox)
                    return true;
                obj = (obj is Visual || obj is System.Windows.Media.Media3D.Visual3D) ? VisualTreeHelper.GetParent(obj) : LogicalTreeHelper.GetParent(obj);
            }
            return false;
        }
        IntPtr TacticalWindowMessage(IntPtr hwnd, int message, IntPtr w, IntPtr l, ref bool handled) {
            // MA_NOACTIVATE keeps keyboard/held movement in SCUM while still delivering clicks,
            // except when the user explicitly clicks a text input (e.g. POI search box) in the sidebar.
            if (message == 0x21) {
                if (tacticalStandalone && !Native.GameFocused()) {
                    handled = true;
                    return new IntPtr(1);
                }
                try {
                    if (tactical != null && tactical.IsVisible) {
                        var hit = tactical.InputHitTest(Mouse.GetPosition(tactical)) as DependencyObject;
                        if (IsInsideTextInput(hit)) {
                            handled = true;
                            return new IntPtr(1);
                        }
                    }
                } catch { }
                handled = true;
                return new IntPtr(3);
            }
            return IntPtr.Zero;
        }
    }
}
