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
        internal Window BuildPostUpdate(string result) {
            var window = Window(Localization.Get("UpdateCheckTitle"), 560, 360);
            var layout = new DockPanel { Margin = new Thickness(24) };
            var done = WpfFieldTheme.Button(Localization.Get("Done"), () => window.Close(), true);
            done.HorizontalAlignment = HorizontalAlignment.Right;
            done.MinWidth = 120;
            done.Margin = new Thickness(0, 14, 0, 0);
            DockPanel.SetDock(done, Dock.Bottom);
            layout.Children.Add(done);
            var text = WpfFieldTheme.Text(result, 15);
            text.TextWrapping = TextWrapping.Wrap;
            text.TextTrimming = TextTrimming.None;
            var card = WpfFieldTheme.Card(new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, new Thickness(16));
            layout.Children.Add(card);
            WpfFieldTheme.ApplyFluentShell(window, "ZiloreMiniMap — " + Localization.Get("UpdateCheckTitle"), layout);
            return window;
        }
        internal void ShowUpdateNotice(string title, string message) {
            PostUi(() => {
                var window = Window(title, 520, 260);
                window.ResizeMode = ResizeMode.NoResize;
                var layout = new DockPanel { Margin = new Thickness(22) };
                var done = WpfFieldTheme.Button(Localization.Get("Done"), () => window.Close(), true);
                done.HorizontalAlignment = HorizontalAlignment.Right;
                done.MinWidth = 110;
                done.Margin = new Thickness(0, 14, 0, 0);
                DockPanel.SetDock(done, Dock.Bottom);
                layout.Children.Add(done);
                var text = WpfFieldTheme.Text(message, 14.5);
                text.TextWrapping = TextWrapping.Wrap;
                text.TextTrimming = TextTrimming.None;
                layout.Children.Add(WpfFieldTheme.Card(new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, new Thickness(14)));
                WpfFieldTheme.ApplyFluentShell(window, "ZiloreMiniMap — " + title, layout);
                Modal(window);
            });
        }
        internal void ShowUpdatePrompt(UpdateRelease release, Func<Action<long, long?>, System.Threading.CancellationToken, System.Threading.Tasks.Task> downloadAndInstall, bool alreadyReady = false, string releasePageUrl = null) {
            PostUi(() => {
                bool ru = Localization.Current == AppLanguage.Russian;
                string title = Localization.Get("UpdateAvailableDialogTitle");
                var window = Window(title, 610, 470);
                window.ResizeMode = ResizeMode.NoResize;
                var layout = new DockPanel { Margin = new Thickness(22) };
                bool downloading = false;
                System.Threading.CancellationTokenSource dlCts = null;

                var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
                Button skipBtn = null, laterBtn = null, installBtn = null;
                skipBtn = WpfFieldTheme.Button(ru ? "Пропустить" : "Skip version", () => {
                    if (downloading)
                        return;
                    UpdatePreferencesStore.Update(p => { p.SkippedVersion = release.VersionText; });
                    window.Close();
                });
                skipBtn.MinHeight = 36;
                skipBtn.FontSize = 12.5;
                skipBtn.Padding = new Thickness(10, 5, 10, 5);
                skipBtn.Margin = new Thickness(0);
                DockPanel.SetDock(skipBtn, Dock.Left);
                footer.Children.Add(skipBtn);

                laterBtn = WpfFieldTheme.Button(ru ? "Позже" : "Later", () => {
                    if (downloading) {
                        if (dlCts != null)
                            try {
                                dlCts.Cancel();
                            } catch { }
                        return;
                    }
                    window.Close();
                });
                laterBtn.MinWidth = 105;
                laterBtn.MinHeight = 36;
                laterBtn.Margin = new Thickness(0, 0, 8, 0);

                var progressPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
                var progressLbl = WpfFieldTheme.Text(Localization.Get("UpdateDownloading"), 13, WpfFieldTheme.Cyan, true);
                var progressBar = new ProgressBar { Height = 10, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 6, 0, 0) };
                progressPanel.Children.Add(progressLbl);
                progressPanel.Children.Add(progressBar);

                string installLabel = alreadyReady ? (ru ? "Перезапустить и обновить (загружено)" : "Restart & update (ready)") : (ru ? "Обновить сейчас" : "Update now");
                installBtn = WpfFieldTheme.Button(installLabel, async () => {
                    if (downloading)
                        return;
                    downloading = true;
                    installBtn.IsEnabled = false;
                    skipBtn.IsEnabled = false;
                    laterBtn.Content = Localization.Get("Cancel");
                    progressPanel.Visibility = Visibility.Visible;
                    progressBar.IsIndeterminate = true;
                    progressLbl.SetResourceReference(TextBlock.ForegroundProperty, "ThemeCyan");
                    progressLbl.Text = Localization.Get("UpdateDownloading");
                    dlCts = new System.Threading.CancellationTokenSource();
                    var speedWatch = System.Diagnostics.Stopwatch.StartNew();
                    long firstBytes = -1;
                    try {
                        await downloadAndInstall((got, total) => {
                            if (firstBytes < 0)
                                firstBytes = got;
                            double elapsedSec = Math.Max(0.25, speedWatch.Elapsed.TotalSeconds);
                            double mbps = Math.Max(0.0, (got - firstBytes) / 1048576.0 / elapsedSec);
                            PostUi(() => {
                                if (total.HasValue && total.Value > 0) {
                                    progressBar.IsIndeterminate = false;
                                    double pct = Math.Max(0, Math.Min(100, got * 100.0 / total.Value));
                                    progressBar.Value = pct;
                                    string spdTxt = mbps >= 0.05 ? (" · " + mbps.ToString("0.0", CultureInfo.InvariantCulture) + (ru ? " МБ/с" : " MB/s")) : "";
                                    progressLbl.Text = (ru ? "Загрузка: " : "Downloading: ") + (got / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " / " + (total.Value / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB (" + (int)pct + "%)" + spdTxt;
                                } else {
                                    progressLbl.Text = (ru ? "Загрузка: " : "Downloading: ") + (got / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
                                }
                            });
                        }, dlCts.Token);
                        progressLbl.Text = Localization.Get("UpdateReadyRestarting");
                        downloading = false;
                        window.Close();
                        RequestExit();
                    } catch (OperationCanceledException) {
                        downloading = false;
                        installBtn.IsEnabled = true;
                        skipBtn.IsEnabled = true;
                        laterBtn.Content = ru ? "Позже" : "Later";
                        progressBar.IsIndeterminate = false;
                        progressLbl.SetResourceReference(TextBlock.ForegroundProperty, "ThemeMuted");
                        progressLbl.Text = ru ? "Загрузка приостановлена — при повторном запуске она продолжится с места остановки." : "Download paused — clicking Update again will resume where it left off.";
                    } catch (Exception ex) {
                        downloading = false;
                        installBtn.IsEnabled = true;
                        skipBtn.IsEnabled = true;
                        laterBtn.Content = ru ? "Позже" : "Later";
                        progressBar.IsIndeterminate = false;
                        progressBar.Value = 0;
                        progressLbl.SetResourceReference(TextBlock.ForegroundProperty, "ThemeAmber");
                        progressLbl.Text = Localization.T("UpdateFailedMsg", ex.Message);
                    } finally {
                        if (dlCts != null) {
                            dlCts.Dispose();
                            dlCts = null;
                        }
                    }
                }, true);
                installBtn.MinWidth = 155;
                installBtn.MinHeight = 36;

                var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                btnRow.Children.Add(laterBtn);
                btnRow.Children.Add(installBtn);
                footer.Children.Add(btnRow);
                DockPanel.SetDock(footer, Dock.Bottom);
                layout.Children.Add(footer);
                DockPanel.SetDock(progressPanel, Dock.Bottom);
                layout.Children.Add(progressPanel);

                var heroDock = new DockPanel();
                if (!string.IsNullOrWhiteSpace(releasePageUrl)) {
                    var ghBtn = WpfFieldTheme.Button("GitHub ↗", () => { _ = WpfFieldTheme.OpenShellUri(releasePageUrl); });
                    ghBtn.MinHeight = 30;
                    ghBtn.FontSize = 12;
                    ghBtn.Padding = new Thickness(10, 3, 10, 3);
                    ghBtn.VerticalAlignment = VerticalAlignment.Center;
                    DockPanel.SetDock(ghBtn, Dock.Right);
                    heroDock.Children.Add(ghBtn);
                }
                var hero = new StackPanel();
                hero.Children.Add(WpfFieldTheme.Text(ru ? "Доступна новая версия ZiloreMiniMap" : "New ZiloreMiniMap version available", 18, WpfFieldTheme.Amber, true));
                var verText = WpfFieldTheme.Text("v" + MapWindow.VersionString + "  →  " + release.DisplayTitle + "  (GitHub)", 13.5, WpfFieldTheme.Cyan, true);
                verText.Margin = new Thickness(0, 4, 0, 0);
                hero.Children.Add(verText);
                heroDock.Children.Add(hero);
                var heroCard = WpfFieldTheme.Card(heroDock, new Thickness(14, 12, 14, 12));
                heroCard.SetResourceReference(Border.BackgroundProperty, "ThemeAccentCard");
                heroCard.SetResourceReference(Border.BorderBrushProperty, "ThemeAccentBorder");
                heroCard.Margin = new Thickness(0, 0, 0, 12);
                DockPanel.SetDock(heroCard, Dock.Top);
                layout.Children.Add(heroCard);

                string notes = string.IsNullOrWhiteSpace(release.ReleaseNotes)
                    ? (ru ? "Нажмите «Обновить сейчас», чтобы скачать проверенное обновление и автоматически перезапустить программу." : "Click 'Update now' to download the verified update and restart automatically.")
                    : release.ReleaseNotes;
                var notesBlock = WpfFieldTheme.Text(notes, 13.5);
                notesBlock.TextWrapping = TextWrapping.Wrap;
                notesBlock.TextTrimming = TextTrimming.None;
                layout.Children.Add(WpfFieldTheme.Card(new ScrollViewer { Content = notesBlock, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, new Thickness(14)));

                window.Closing += (s, e) => { if (downloading && dlCts != null) { try { dlCts.Cancel(); } catch { } e.Cancel = true; } };
                WpfFieldTheme.ApplyFluentShell(window, "ZiloreMiniMap — " + title, layout);
                Modal(window);
            });
        }
        void ShowPostUpdate(string result) {
            Modal(BuildPostUpdate(result));
        }
        Window BuildTactical() {
            var window = Window("ZiloreMiniMap — Field console", 1280, 850);
            window.MinWidth = 900;
            window.MinHeight = 620;
            window.Topmost = false;
            window.ShowActivated = false;
            bool solidTest = (Program.IsTestBuild && (Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, "--fast-tac-test", StringComparison.OrdinalIgnoreCase)) || Environment.GetEnvironmentVariable("SCUM_MINIMAP_TAC_OPAQUE") == "1"))
                || Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, "--hw-accel", StringComparison.OrdinalIgnoreCase))
                || Environment.GetEnvironmentVariable("SCUM_MINIMAP_HW_ACCEL") == "1"
                || HardwareAccelMapEnabled;
            window.AllowsTransparency = !solidTest;
            window.Background = solidTest ? WpfFieldTheme.ColorBrush("#0B1117") : Brushes.Transparent;
            window.SourceInitialized += (s, e) => {
                var handle = new WindowInteropHelper(window).Handle;
                _ = SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x08000000);
                HwndSource.FromHwnd(handle).AddHook(TacticalWindowMessage);
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            tacticalMap = new WpfMapSurface { BaseMap = geography, Terrain = terrain, RightOverlayInset = 320 };
            SystemTypography.ApplyClearType(tacticalMap);
            RenderOptions.SetBitmapScalingMode(tacticalMap, BitmapScalingMode.HighQuality);
            tacticalMap.DirectTerrain = solidTest && (RenderCapability.Tier >> 16) > 0 && RenderOptions.ProcessRenderMode != RenderMode.SoftwareOnly && Environment.GetEnvironmentVariable("SCUM_MINIMAP_TAC_RASTER") != "1";
            tacticalMap.RouteStopAt = AddSquadStop;
            tacticalMap.RouteInsertStopAt = InsertOwnRouteStop;
            tacticalMap.RouteDestination = AddSquadDestination;
            tacticalMap.PingAt = (kind, p) => SendPing(kind, p);
            tacticalMap.PingAvailable = CanSendPing;
            tacticalMap.RoutesRequested = ShowRoutes;
            tacticalMap.RouteNavigate = StartSquadRoute;
            tacticalMap.RouteRemove = RemoveOwnRouteStop;
            tacticalMap.RouteStopMoved = MoveOwnRouteStopPosition;
            tacticalMap.RouteKind = ChangeOwnRouteKind;
            tacticalMap.RouteUndo = UndoOwnRouteStop;
            tacticalMap.RouteClear = ClearOwnRoute;
            tacticalMap.NavigatePeer = FollowSquadPeer;
            tacticalMap.RouteOptimize = () => { OptimizeOwnRouteOrder(); ShowTacticalToast(WpfFieldTheme.UiText("RouteOptimize")); };
            tacticalMap.RouteReverse = () => { ReverseOwnRoute(); ShowTacticalToast(WpfFieldTheme.UiText("RouteReverse")); };
            tacticalMap.RouteLoop = () => { LoopOwnRoute(); ShowTacticalToast(WpfFieldTheme.UiText("RouteLoop")); };
            tacticalMap.RouteCopyCode = () => { if (CopyOwnRouteShareCode()) ShowTacticalToast(WpfFieldTheme.UiText("RouteCopyCode") + " ✓"); };
            tacticalMap.WaypointsCopyCode = () => {
                bool ruW = Localization.Current == AppLanguage.Russian;
                string wpCode = WaypointShareService.Encode(engine.WpfCustomWaypoints(), "Zilore Waypoints");
                if (string.IsNullOrEmpty(wpCode)) { ShowTacticalToast(ruW ? "Нет сохранённых меток для экспорта" : "No custom waypoints to export"); return; }
                if (SafeCleanup.Try(() => System.Windows.Clipboard.SetText(wpCode), "WaypointShare", "Clipboard copy failed"))
                    ShowTacticalToast(ruW ? "✓ Код меток ZLR-WP скопирован" : "✓ ZLR-WP waypoint code copied");
            };
            tacticalMap.WaypointsImportCode = () => {
                bool ruW = Localization.Current == AppLanguage.Russian;
                string clip = "";
                SafeCleanup.Try(() => { if (System.Windows.Clipboard.ContainsText()) clip = System.Windows.Clipboard.GetText() ?? ""; }, "WaypointShare", "Clipboard read failed");
                WaypointPackResult pack;
                if (!WaypointShareService.TryDecode(clip, out pack)) { ShowTacticalToast(ruW ? "Буфер не содержит корректный код ZLR-WP:" : "Clipboard does not contain a valid ZLR-WP: code"); return; }
                int added = engine.WpfImportWaypointPack(pack);
                if (added > 0) { filterIdentity = null; Refresh(); ShowTacticalToast(ruW ? ("✓ Импортировано меток: " + added) : ("✓ Imported waypoints: " + added)); }
                else ShowTacticalToast(ruW ? "Все метки из кода уже присутствуют" : "All waypoints from code already exist");
            };
            tacticalMap.RouteSkipStop = () => { SkipActiveSquadStop(); ShowTacticalToast(WpfFieldTheme.UiText("RouteSkipStop")); };
            tacticalMap.ClearRouteRequested = ClearMapRoute;
            tacticalMap.StopNavigationRequested = () => { StopSquadNavigation(); Refresh(); };
            tacticalMap.DeleteWaypointRequested = marker => { engine.WpfDeleteWaypoint(marker); Refresh(); };
            tacticalMap.NavigateAt = p => { double s = tacticalMap.State != null ? WpfMapSurface.MapSide(tacticalMap.State, tacticalMap.ActualWidth, tacticalMap.ActualHeight, false) : Math.Max(1, Math.Min(tacticalMap.ActualWidth, tacticalMap.ActualHeight)); NavigateDestination(engine.WpfClickedTarget(p, (float)(8.0 / Math.Max(1.0, s)))); };
            tacticalMap.WaypointAt = p => ShowAddWaypoint(p);
            tacticalMap.ZoomRequested = d => { engine.WpfZoom(true, d); RefreshCamera(); };
            tacticalMap.PanChanged = p => { engine.WpfPan(p); RefreshCamera(); };
            tacticalMap.NavigateShared = NavigateDestination;
            tacticalMap.AdminMenuAt = point => {
                var actions = engine.WpfMapAdminActions(point);
                if (actions.Length == 0)
                    return null;
                var root = new MenuItem { Header = Localization.Get("ControlAdminCommands") };
                WpfMapSurface.AddAdminContextItems(root.Items, actions);
                return root;
            };
            // The sidebar overlays the full monitor viewport; toggling it must not move the camera.
            var scene = new Grid();
            Grid.SetColumnSpan(scene, 2);
            scene.Children.Add(tacticalMap);
            var contextLayer = new Canvas();
            tacticalMap.ContextHost = contextLayer;
            Grid.SetColumnSpan(contextLayer, 2);
            Panel.SetZIndex(contextLayer, 200);
            grid.Children.Add(contextLayer);
            window.PreviewMouseDown += (s, e) => { if (tacticalMap.DismissContextOutside(e.GetPosition(contextLayer)) && e.ChangedButton == MouseButton.Left) e.Handled = true; };
            window.PreviewKeyDown += (s, e) => {
                if (EditingText(Keyboard.FocusedElement))
                    return;
                var key = (e.Key == Key.System) ? e.SystemKey : e.Key;
                if (HandleTacticalHotkey(KeyInterop.VirtualKeyFromKey(key), Keyboard.Modifiers))
                    e.Handled = true;
            };
            var topCards = new StackPanel { Width = 378, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16) };
            var targetBody = new StackPanel();
            targetAccentBar = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = WpfFieldTheme.Cyan, Margin = new Thickness(0, 0, 0, 8), Opacity = 0.92 };
            targetBody.Children.Add(targetAccentBar);
            var targetHdr = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var hdrIcons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var focusTargetBtn = WpfFieldTheme.Button("⌖", () => { var st = engine.WpfSnapshot(true); if (st.Target != null) { engine.WpfPan(st.Target.Centroid); RefreshCamera(); } });
            focusTargetBtn.ToolTip = WpfFieldTheme.UiText("TargetCenterTip");
            focusTargetBtn.MinWidth = 28;
            focusTargetBtn.MinHeight = 26;
            focusTargetBtn.Padding = new Thickness(4, 1, 4, 1);
            focusTargetBtn.FontSize = 13;
            focusTargetBtn.Margin = new Thickness(0, 0, 4, 0);
            var saveTargetBtn = WpfFieldTheme.Button("★", () => { var st = engine.WpfSnapshot(true); if (st.Target != null) ShowAddWaypoint(st.Target.Centroid); });
            saveTargetBtn.ToolTip = WpfFieldTheme.UiText("TargetSaveWaypointTip");
            saveTargetBtn.MinWidth = 28;
            saveTargetBtn.MinHeight = 26;
            saveTargetBtn.Padding = new Thickness(4, 1, 4, 1);
            saveTargetBtn.FontSize = 12.5;
            saveTargetBtn.Margin = new Thickness(0, 0, 4, 0);
            var clear = WpfFieldTheme.Button("✕", ClearMapRoute);
            clear.Tag = "tooltip:WpfClearRoute";
            clear.ToolTip = Localization.Get("WpfClearRoute") + " [X]";
            clear.MinWidth = 28;
            clear.MinHeight = 26;
            clear.Padding = new Thickness(4, 1, 4, 1);
            clear.FontSize = 12;
            clear.Foreground = WpfFieldTheme.Danger;
            hdrIcons.Children.Add(focusTargetBtn);
            hdrIcons.Children.Add(saveTargetBtn);
            hdrIcons.Children.Add(clear);
            DockPanel.SetDock(hdrIcons, Dock.Right);
            targetHdr.Children.Add(hdrIcons);
            var hdrLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var fieldTargetLbl = WpfFieldTheme.Text(Localization.Get("FieldTarget"), 10.5, WpfFieldTheme.Muted, true);
            fieldTargetLbl.Tag = "text:FieldTarget";
            fieldTargetLbl.VerticalAlignment = VerticalAlignment.Center;
            hdrLeft.Children.Add(fieldTargetLbl);
            targetSectorBadgeText = WpfFieldTheme.Text("", 11, WpfFieldTheme.Cyan, true);
            targetSectorBadge = WpfFieldTheme.Badge(targetSectorBadgeText, new Thickness(7, 1.5, 7, 1.5));
            targetSectorBadge.Margin = new Thickness(8, 0, 0, 0);
            targetSectorBadge.VerticalAlignment = VerticalAlignment.Center;
            hdrLeft.Children.Add(targetSectorBadge);
            targetHdr.Children.Add(hdrLeft);
            targetBody.Children.Add(targetHdr);
            targetTitle = WpfFieldTheme.Text("", 18, null, true);
            targetTitle.Margin = new Thickness(0, 0, 0, 2);
            targetBody.Children.Add(targetTitle);
            targetDetail = WpfFieldTheme.Text("", 13, WpfFieldTheme.Muted);
            targetDetail.TextWrapping = TextWrapping.Wrap;
            targetDetail.TextTrimming = TextTrimming.None;
            targetBody.Children.Add(targetDetail);
            var targetActions = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
            addTargetStop = WpfFieldTheme.Button(S("SquadAddStop"), () => AddSquadDestination(engine.WpfSnapshot(true).Target));
            addTargetStop.Tag = "text:SquadAddStop";
            addTargetStop.ToolTip = S("MapGpsSavedHelp");
            addTargetStop.MinHeight = 28;
            addTargetStop.Padding = new Thickness(9, 3, 9, 3);
            addTargetStop.FontSize = 12;
            addTargetStop.Margin = new Thickness(0, 0, 5, 4);
            targetActions.Children.Add(addTargetStop);
            skipTargetStop = WpfFieldTheme.Button(WpfFieldTheme.UiText("RouteSkipStop"), () => { SkipActiveSquadStop(); ShowTacticalToast(WpfFieldTheme.UiText("RouteSkipStop")); });
            skipTargetStop.MinHeight = 28;
            skipTargetStop.Padding = new Thickness(9, 3, 9, 3);
            skipTargetStop.FontSize = 12;
            skipTargetStop.Margin = new Thickness(0, 0, 5, 4);
            skipTargetStop.Visibility = Visibility.Collapsed;
            targetActions.Children.Add(skipTargetStop);
            targetRouteModeBtn = WpfFieldTheme.Button("🛣", () => { if (WpfMapSurface.AviationModeEnabled) ToggleAviationHotkey(); else CycleRouteModeHotkey(); });
            targetRouteModeBtn.ToolTip = WpfFieldTheme.UiText("TargetModeTip");
            targetRouteModeBtn.MinHeight = 28;
            targetRouteModeBtn.Padding = new Thickness(9, 3, 9, 3);
            targetRouteModeBtn.FontSize = 12;
            targetRouteModeBtn.Margin = new Thickness(0, 0, 5, 4);
            targetActions.Children.Add(targetRouteModeBtn);
            var manageTarget = WpfFieldTheme.Button(S("RouteList"), ShowRoutes);
            manageTarget.Tag = "text:RouteList";
            manageTarget.MinHeight = 28;
            manageTarget.Padding = new Thickness(9, 3, 9, 3);
            manageTarget.FontSize = 12;
            manageTarget.Margin = new Thickness(0, 0, 0, 4);
            targetActions.Children.Add(manageTarget);
            targetBody.Children.Add(targetActions);
            targetCard = WpfFieldTheme.Card(targetBody, new Thickness(13, 10, 13, 8));
            targetCard.CornerRadius = new CornerRadius(12);
            SystemTypography.ApplyClearType(targetCard);
            if (solidTest) targetCard.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.26, Color = Colors.Black };
            topCards.Children.Add(targetCard);
            targetCard.PreviewMouseRightButtonDown += (s, e) => { if (tacticalMap.BeginContextPointer(e.GetPosition(tacticalMap))) e.Handled = true; };
            var preview = new StackPanel();
            routePreviewTitle = WpfFieldTheme.Text("", 16.5, null, true);
            preview.Children.Add(routePreviewTitle);
            routePreviewDetail = WpfFieldTheme.Text("", 13.5, WpfFieldTheme.Muted);
            routePreviewDetail.TextWrapping = TextWrapping.Wrap;
            routePreviewDetail.TextTrimming = TextTrimming.None;
            routePreviewDetail.Margin = new Thickness(0, 4, 0, 0);
            preview.Children.Add(routePreviewDetail);
            var previewActions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var startPreview = WpfFieldTheme.Button(Localization.Get("SquadStartRoute"), () => StartSquadRoute(squad.Stops), true);
            startPreview.Tag = "text:SquadStartRoute";
            startPreview.MinHeight = 32;
            startPreview.Padding = new Thickness(10, 4, 10, 4);
            startPreview.FontSize = 13;
            startPreview.Margin = new Thickness(0, 0, 6, 6);
            previewActions.Children.Add(startPreview);
            var optPreview = WpfFieldTheme.Button(WpfFieldTheme.UiText("RouteOptimize"), () => OptimizeOwnRouteOrder());
            optPreview.ToolTip = WpfFieldTheme.UiText("RouteOptimizeTip");
            optPreview.MinHeight = 32;
            optPreview.Padding = new Thickness(10, 4, 10, 4);
            optPreview.FontSize = 13;
            optPreview.Margin = new Thickness(0, 0, 6, 6);
            previewActions.Children.Add(optPreview);
            Button codePreview = null;
            DispatcherTimer codePreviewTimer = null;
            codePreview = WpfFieldTheme.Button(WpfFieldTheme.UiText("RouteCopyCode"), () => { if (CopyOwnRouteShareCode()) { codePreview.Content = "✓"; if (codePreviewTimer == null) { codePreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) }; codePreviewTimer.Tick += (rs, re) => { codePreviewTimer.Stop(); codePreview.Content = WpfFieldTheme.UiText("RouteCopyCode"); }; } codePreviewTimer.Stop(); codePreviewTimer.Start(); } });
            codePreview.ToolTip = WpfFieldTheme.UiText("RouteCopyCodeTip");
            codePreview.MinHeight = 32;
            codePreview.Padding = new Thickness(10, 4, 10, 4);
            codePreview.FontSize = 13;
            codePreview.Margin = new Thickness(0, 0, 6, 6);
            previewActions.Children.Add(codePreview);
            var undoPreview = WpfFieldTheme.Button("↶", () => { UndoOwnRouteStop(); ShowTacticalToast(Localization.Get("SquadUndoStop")); });
            undoPreview.ToolTip = Localization.Get("SquadUndoStop") + " (Ctrl+Z)";
            undoPreview.MinHeight = 32;
            undoPreview.MinWidth = 32;
            undoPreview.Padding = new Thickness(8, 4, 8, 4);
            undoPreview.FontSize = 13;
            undoPreview.Margin = new Thickness(0, 0, 6, 6);
            previewActions.Children.Add(undoPreview);
            var revPreview = WpfFieldTheme.Button("⇄", () => { ReverseOwnRoute(); ShowTacticalToast(WpfFieldTheme.UiText("RouteReverse")); });
            revPreview.ToolTip = WpfFieldTheme.UiText("RouteReverse");
            revPreview.MinHeight = 32;
            revPreview.MinWidth = 32;
            revPreview.Padding = new Thickness(8, 4, 8, 4);
            revPreview.FontSize = 13;
            revPreview.Margin = new Thickness(0, 0, 6, 6);
            previewActions.Children.Add(revPreview);
            var clearPreview = WpfFieldTheme.Button(Localization.Get("WpfClearRoute"), ClearMapRoute);
            clearPreview.Tag = "text:WpfClearRoute";
            clearPreview.MinHeight = 32;
            clearPreview.Padding = new Thickness(10, 4, 10, 4);
            clearPreview.FontSize = 13;
            clearPreview.Margin = new Thickness(0, 0, 6, 6);
            previewActions.Children.Add(clearPreview);
            var managePreview = WpfFieldTheme.Button(S("RouteList"), ShowRoutes);
            managePreview.Tag = "text:RouteList";
            managePreview.MinHeight = 32;
            managePreview.Padding = new Thickness(10, 4, 10, 4);
            managePreview.FontSize = 13;
            managePreview.Margin = new Thickness(0, 0, 0, 6);
            previewActions.Children.Add(managePreview);
            preview.Children.Add(previewActions);
            routePreviewCard = WpfFieldTheme.Card(preview);
            routePreviewCard.Margin = new Thickness(0, 8, 0, 0);
            routePreviewCard.Visibility = Visibility.Collapsed;
            topCards.Children.Add(routePreviewCard);
            routePreviewCard.PreviewMouseRightButtonDown += (s, e) => { if (tacticalMap.BeginContextPointer(e.GetPosition(tacticalMap))) e.Handled = true; };
            hoverInfo = WpfFieldTheme.Text("", 15);
            hoverInfo.TextWrapping = TextWrapping.Wrap;
            hoverInfo.TextTrimming = TextTrimming.None;
            hoverCard = WpfFieldTheme.Card(new ScrollViewer { Content = hoverInfo, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            hoverCard.Margin = new Thickness(0, 8, 0, 0);
            hoverCard.Visibility = Visibility.Collapsed;
            hoverCard.IsHitTestVisible = false;
            topCards.Children.Add(hoverCard);
            scene.Children.Add(topCards);
            tacticalMap.HoverChanged = ShowHoverDetails;
            // The details card is an overlay on the map, not a dead area for its
            // context menu. Preserve normal left-click/scroll behavior in it.
            hoverCard.PreviewMouseRightButtonDown += (s, e) => { if (tacticalMap.BeginContextPointer(e.GetPosition(tacticalMap))) e.Handled = true; };
            var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var zOutBtn = WpfFieldTheme.Button("−", () => { engine.WpfZoom(true, -1); RefreshCamera(); });
            zOutBtn.Tag = "tooltip:ControlZoomOut";
            zOutBtn.MinWidth = 30;
            zoomResetButton = WpfFieldTheme.Button("1.0×", () => { engine.WpfNativeTool(AppControlAction.ResetZoom); RefreshInteraction(); });
            zoomResetButton.Tag = "tooltip:ControlResetZoom";
            zoomResetButton.MinWidth = 48;
            var zInBtn = WpfFieldTheme.Button("+", () => { engine.WpfZoom(true, 1); RefreshCamera(); });
            zInBtn.Tag = "tooltip:ControlZoomIn";
            zInBtn.MinWidth = 30;
            zoomLockButton = WpfFieldTheme.Button("🔓", () => { engine.WpfSetTacticalZoomLocked(!engine.WpfTacticalZoomLocked); SyncCameraControls(engine.WpfSnapshot(true)); });
            zoomLockButton.MinWidth = 32;
            zoomLockButton.ToolTip = Localization.Current == AppLanguage.Russian ? "Зафиксировать масштаб карты [L]" : "Lock map zoom [L]";
            zoomLockButton.ToolTipOpening += (zs, ze) => { zoomLockButton.ToolTip = Localization.Current == AppLanguage.Russian ? "Зафиксировать масштаб карты [L]" : "Lock map zoom [L]"; };
            controls.Children.Add(zOutBtn);
            controls.Children.Add(zoomResetButton);
            controls.Children.Add(zInBtn);
            controls.Children.Add(zoomLockButton);
            var sep1 = new Border { Width = 1, Height = 20, Margin = new Thickness(4, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            sep1.SetResourceReference(Border.BackgroundProperty, "ThemeLine");
            controls.Children.Add(sep1);
            followButton = WpfFieldTheme.Button(Localization.Get("WpfFollowPlayer"), () => { engine.WpfFollow(); if (tacticalMap != null) tacticalMap.TriggerPlayerPulse(); Refresh(); });
            followButton.Tag = "text:WpfFollowPlayer";
            controls.Children.Add(followButton);
            rulerToggleButton = WpfFieldTheme.Button("📏", () => { if (tacticalMap != null) { bool on = tacticalMap.ToggleRulerMode(); ShowTacticalToast(WpfFieldTheme.UiText(on ? "RulerModeOnToast" : "RulerModeOffToast")); } });
            rulerToggleButton.MinWidth = 32;
            rulerToggleButton.Tag = "toolbar-ruler";
            rulerToggleButton.ToolTip = WpfFieldTheme.UiText("ToolbarRulerTip");
            rulerToggleButton.ToolTipOpening += (rs, re) => { rulerToggleButton.ToolTip = WpfFieldTheme.UiText("ToolbarRulerTip"); };
            controls.Children.Add(rulerToggleButton);
            tacticalMap.RulerModeChanged = SyncRulerButtonVisual;
            var sep2 = new Border { Width = 1, Height = 20, Margin = new Thickness(4, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            sep2.SetResourceReference(Border.BackgroundProperty, "ThemeLine");
            controls.Children.Add(sep2);
            var opIcon = WpfFieldTheme.Text("◐", 14, WpfFieldTheme.Muted, true);
            opIcon.VerticalAlignment = VerticalAlignment.Center;
            opIcon.Margin = new Thickness(0, 0, 6, 0);
            opIcon.ToolTip = Localization.Get("MapOpacity");
            controls.Children.Add(opIcon);
            var opacity = new Slider { Minimum = 0, Maximum = 100, Value = engine.WpfSnapshot(true).Opacity, Width = 92, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0), ToolTip = Localization.Get("MapOpacity") };
            tacticalOpacity = opacity;
            opacity.Tag = "tooltip:MapOpacity";
            opacity.SmallChange = 1;
            opacity.LargeChange = 10;
            opacity.IsSnapToTickEnabled = true;
            opacity.TickFrequency = 1;
            opacity.IsMoveToPointEnabled = true;
            System.Windows.Automation.AutomationProperties.SetHelpText(opacity, (string)opacity.ToolTip);
            opacity.IsEnabled = true;
            opacity.ValueChanged += (s, e) => { if (!syncingOpacity) { int v = (int)Math.Round(opacity.Value); if (tacticalOpacityValue != null) tacticalOpacityValue.Text = v.ToString(CultureInfo.InvariantCulture) + "%"; engine.WpfSetTacticalOpacity(v); RefreshCamera(); } };
            controls.Children.Add(opacity);
            tacticalOpacityValue = WpfFieldTheme.Text(opacity.Value.ToString("0", CultureInfo.CurrentCulture) + "%", 12.5, WpfFieldTheme.Cyan, true);
            tacticalOpacityValue.Tag = "map-opacity-value";
            tacticalOpacityValue.MinWidth = 36;
            tacticalOpacityValue.TextAlignment = TextAlignment.Right;
            tacticalOpacityValue.VerticalAlignment = VerticalAlignment.Center;
            tacticalOpacityValue.Margin = new Thickness(6, 0, 2, 0);
            controls.Children.Add(tacticalOpacityValue);
            foreach (var button in controls.Children.OfType<Button>()) {
                button.MinHeight = 30;
                button.Padding = new Thickness(9, 3, 9, 3);
                button.FontSize = 12.5;
                button.Margin = new Thickness(0, 0, 4, 0);
            }
            var toolbar = new DockPanel();
            var moveGrip = new Grid { Width = 16, Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.SizeAll };
            var moveDots = WpfFieldTheme.Text("⋮", 14, WpfFieldTheme.Muted, true);
            moveDots.HorizontalAlignment = HorizontalAlignment.Center;
            moveDots.VerticalAlignment = VerticalAlignment.Center;
            moveDots.IsHitTestVisible = false;
            var move = new Thumb { Opacity = 0, Cursor = Cursors.SizeAll, Tag = "tooltip:WpfMoveControls" };
            moveGrip.Children.Add(moveDots);
            moveGrip.Children.Add(move);
            DockPanel.SetDock(moveGrip, Dock.Left);
            toolbar.Children.Add(moveGrip);
            var resizeGrip = new Grid { Width = 14, Margin = new Thickness(4, 0, 0, 0), Cursor = Cursors.SizeNWSE };
            var resizeIcon = WpfFieldTheme.Text("⋱", 11, WpfFieldTheme.Muted, false);
            resizeIcon.HorizontalAlignment = HorizontalAlignment.Center;
            resizeIcon.VerticalAlignment = VerticalAlignment.Center;
            resizeIcon.IsHitTestVisible = false;
            var resize = new Thumb { Opacity = 0, Cursor = Cursors.SizeNWSE, Tag = "tooltip:WpfResizeControls" };
            resizeGrip.Children.Add(resizeIcon);
            resizeGrip.Children.Add(resize);
            DockPanel.SetDock(resizeGrip, Dock.Right);
            toolbar.Children.Add(resizeGrip);
            toolbar.Children.Add(controls);
            tacticalToastText = WpfFieldTheme.Text("", 13.5, WpfFieldTheme.Cyan, true);
            tacticalToastCard = WpfFieldTheme.Card(tacticalToastText, new Thickness(16, 8, 16, 8));
            tacticalToastCard.HorizontalAlignment = HorizontalAlignment.Center;
            tacticalToastCard.VerticalAlignment = VerticalAlignment.Top;
            tacticalToastCard.Margin = new Thickness(0, 18, 0, 0);
            tacticalToastCard.Visibility = Visibility.Collapsed;
            tacticalToastCard.IsHitTestVisible = false;
            Panel.SetZIndex(tacticalToastCard, 150);
            scene.Children.Add(tacticalToastCard);
            var chrome = WpfFieldTheme.Card(toolbar, new Thickness(8, 6, 8, 6));
            chrome.CornerRadius = new CornerRadius(14);
            if (solidTest) chrome.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 20, ShadowDepth = 3, Opacity = 0.28, Color = Colors.Black };
            tacticalToolbarChrome = chrome;
            SystemTypography.ApplyClearType(chrome);
            chrome.HorizontalAlignment = HorizontalAlignment.Left;
            chrome.VerticalAlignment = VerticalAlignment.Bottom;
            chrome.Margin = new Thickness(tacticalLayout.ToolbarLeft, 0, 0, tacticalLayout.ToolbarBottom);
            scene.Children.Add(chrome);
            grid.Children.Add(scene);
            double controlScale = tacticalLayout.ToolbarScale;
            bool controlsUserMoved = tacticalLayout.ToolbarCustomPos;
            var controlScaleTransform = new ScaleTransform(controlScale, controlScale);
            chrome.LayoutTransform = controlScaleTransform;
            Action centerControls = () => { double availW = Math.Max(100, scene.ActualWidth - (tacticalSidebarShell != null && tacticalSidebarShell.Visibility == Visibility.Visible ? 320 : 0)); double minLeft = availW >= 1100 ? 270.0 : 12.0; double maxLeft = Math.Max(8.0, availW - chrome.ActualWidth - 8.0); double cx = Math.Max(minLeft, (availW - chrome.ActualWidth) / 2.0); chrome.Margin = new Thickness(Math.Max(8.0, Math.Min(cx, maxLeft)), 0, 0, 34); };
            Action clampControls = () => { if (!controlsUserMoved && scene.ActualWidth > 0 && chrome.ActualWidth > 0) { centerControls(); return; } chrome.Margin = new Thickness(Math.Max(8, Math.Min(chrome.Margin.Left, Math.Max(8, scene.ActualWidth - chrome.ActualWidth - 8))), 0, 0, Math.Max(8, Math.Min(chrome.Margin.Bottom, Math.Max(8, scene.ActualHeight - chrome.ActualHeight - 8)))); };
            Action resetToolbarAction = () => { controlsUserMoved = false; controlScale = 1; controlScaleTransform.ScaleX = 1; controlScaleTransform.ScaleY = 1; tacticalLayout.ToolbarCustomPos = false; tacticalLayout.ToolbarScale = 1; tacticalLayout.ToolbarBottom = 34; centerControls(); SaveTacticalUiLayout(); };
            move.ToolTip = WpfFieldTheme.UiText("ToolbarResetTip");
            move.DragDelta += (s, e) => { controlsUserMoved = true; chrome.Margin = new Thickness(chrome.Margin.Left + e.HorizontalChange, 0, 0, chrome.Margin.Bottom - e.VerticalChange); clampControls(); };
            move.DragCompleted += (s, e) => { if (controlsUserMoved) { tacticalLayout.ToolbarCustomPos = true; tacticalLayout.ToolbarLeft = chrome.Margin.Left; tacticalLayout.ToolbarBottom = chrome.Margin.Bottom; SaveTacticalUiLayout(); } };
            move.MouseDoubleClick += (s, e) => { resetToolbarAction(); e.Handled = true; };
            resize.DragDelta += (s, e) => { controlScale = Math.Max(.75, Math.Min(1.45, controlScale + e.HorizontalChange / 400)); controlScaleTransform.ScaleX = controlScale; controlScaleTransform.ScaleY = controlScale; };
            resize.DragCompleted += (s, e) => { tacticalLayout.ToolbarScale = controlScale; SaveTacticalUiLayout(); };
            chrome.SizeChanged += (s, e) => clampControls();
            scene.SizeChanged += (s, e) => clampControls();
            var toolbarMenu = new ContextMenu();
            var resetControls = new MenuItem { Header = Localization.Get("WpfResetControls") };
            toolbarMenu.Opened += (s, e) => resetControls.Header = Localization.Get("WpfResetControls");
            resetControls.Click += (s, e) => resetToolbarAction();
            toolbarMenu.Items.Add(resetControls);
            chrome.ContextMenu = toolbarMenu;
            sidebar = new StackPanel { Margin = new Thickness(22) };
            var scroller = new ScrollViewer { Content = sidebar, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.Transparent };
            tacticalSidebarShell = new Border { Child = scroller, BorderThickness = new Thickness(1, 0, 0, 0), SnapsToDevicePixels = true };
            SystemTypography.ApplyClearType(tacticalSidebarShell);
            Grid.SetColumn(tacticalSidebarShell, 1);
            grid.Children.Add(tacticalSidebarShell);
            window.Content = grid;
            var railSep = new Border { Height = 1, Margin = new Thickness(8, 85, 8, 0), VerticalAlignment = VerticalAlignment.Top, Opacity = 0.65 };
            railSep.SetResourceReference(Border.BackgroundProperty, "ThemeLine");
            tacticalFloatingRailDock = new Border { Width = 44, Height = 216, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 8, 328, 0), IsHitTestVisible = false, SnapsToDevicePixels = true, Child = railSep };
            if (solidTest) tacticalFloatingRailDock.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.28, Color = Colors.Black };
            Grid.SetColumnSpan(tacticalFloatingRailDock, 2);
            grid.Children.Add(tacticalFloatingRailDock);
            Action<Button> styleRailBtn = b => { b.Background = Brushes.Transparent; b.BorderThickness = new Thickness(0); b.Width = 36; b.Height = 36; b.MinHeight = 36; b.Padding = new Thickness(0); b.HorizontalAlignment = HorizontalAlignment.Right; b.VerticalAlignment = VerticalAlignment.Top; };
            var sidebarToggle = WpfFieldTheme.Button("›", () => { bool collapse = tacticalSidebarShell.Visibility == Visibility.Visible; tacticalSidebarShell.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible; grid.ColumnDefinitions[1].Width = new GridLength(collapse ? 0 : 320); if (tacticalMap != null) { tacticalMap.RightOverlayInset = collapse ? 0 : 320; tacticalMap.RequestRedraw(); } tacticalLayout.SidebarCollapsed = collapse; SaveTacticalUiLayout(); clampControls(); });
            styleRailBtn(sidebarToggle);
            sidebarToggle.FontSize = 20;
            sidebarToggle.FontWeight = FontWeights.Bold;
            sidebarToggle.Margin = new Thickness(12, 54, 332, 0);
            sidebarToggle.ToolTip = Localization.Get("SidebarTitle");
            System.Windows.Automation.AutomationProperties.SetName(sidebarToggle, Localization.Get("SidebarTitle"));
            Grid.SetColumnSpan(sidebarToggle, 2);
            grid.Children.Add(sidebarToggle);
            var closeOverlay = WpfFieldTheme.Button("×", () => CloseTactical(true));
            styleRailBtn(closeOverlay);
            closeOverlay.FontSize = 21;
            closeOverlay.FontWeight = FontWeights.Bold;
            closeOverlay.Margin = new Thickness(12, 12, 332, 0);
            closeOverlay.ToolTip = Localization.Get("Close");
            System.Windows.Automation.AutomationProperties.SetName(closeOverlay, Localization.Get("Close"));
            Grid.SetColumnSpan(closeOverlay, 2);
            grid.Children.Add(closeOverlay);
            Func<object, string, int, Action, Button> floating = (icon, label, y, click) => { var button = WpfFieldTheme.Button("", click); button.Content = icon; button.Tag = "tooltip:" + label; styleRailBtn(button); button.Margin = new Thickness(12, y, 332, 0); ToolTipService.SetInitialShowDelay(button, 250); ToolTipService.SetShowOnDisabled(button, true); Grid.SetColumnSpan(button, 2); grid.Children.Add(button); return button; };
            var settings = floating("⚙", "Settings", 96, ShowSettings);
            settings.FontFamily = SystemTypography.WpfSymbolFontFamily;
            settings.FontSize = 20;
            var help = floating("?", "OpenStartupGuide", 138, ShowOnboarding);
            help.FontSize = 19;
            help.FontWeight = FontWeights.SemiBold;
            var grave = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 3,19 L 3,7 C 3,0 17,0 17,7 L 17,19 Z M 10,6 L 10,15 M 6,10 L 14,10"), StrokeThickness = 1.8, Width = 18, Height = 20, Stretch = Stretch.Uniform };
            grave.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ThemeInk");
            deathRemovalButton = floating(grave, "ClearDeathMarkers", 180, () => { engine.ClearDeathMarkers(); Refresh(); });
            sidebarToggleButton = sidebarToggle;
            sidebarToggle.Tag = "tooltip:WpfCollapseSidebar";
            closeOverlay.Tag = "tooltip:Close";
            sidebarToggle.Click += (s, e) => { bool open = tacticalSidebarShell.Visibility == Visibility.Visible; sidebarToggle.Content = open ? "›" : "‹"; sidebarToggle.Tag = open ? "tooltip:WpfCollapseSidebar" : "tooltip:WpfExpandSidebar"; RefreshLocalizedChrome(sidebarToggle); tacticalFloatingRailDock.Margin = new Thickness(8, 8, open ? 328 : 8, 0); sidebarToggle.Margin = new Thickness(12, 54, open ? 332 : 12, 0); closeOverlay.Margin = new Thickness(12, 12, open ? 332 : 12, 0); foreach (var button in new[] { settings, help, deathRemovalButton }) button.Margin = new Thickness(12, button.Margin.Top, open ? 332 : 12, 0); };
            if (!diagnostic && tacticalLayout.SidebarCollapsed) {
                sidebarToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            RefreshLocalizedChrome(grid);
            window.Closing += (s, e) => { if (codePreviewTimer != null) codePreviewTimer.Stop(); if (!disposed && !diagnostic) { e.Cancel = true; window.Hide(); engine.WpfTactical(false); SyncHudVisibility(true); } };
            return window;
        }
        Window BuildHud() {
            var window = new Window { Title = "ZiloreMiniMap HUD", Width = 240, Height = 300, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, ShowActivated = false, Left = 48, Top = 48, FontFamily = SystemTypography.WpfUiFontFamily };
            SystemTypography.ApplyClearType(window);
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(240) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HudStatusHeight) });
            hudMap = new WpfMapSurface { Compact = true, BaseMap = geography, Terrain = terrain };
            RenderOptions.SetBitmapScalingMode(hudMap, BitmapScalingMode.Linear);
            var hudFrame = new Grid();
            hudFrame.Children.Add(hudMap);
            hudBezel = new System.Windows.Shapes.Ellipse { Stroke = WpfFieldTheme.Cyan, StrokeThickness = 2.2, Opacity = 0.82, IsHitTestVisible = false, Margin = new Thickness(1) };
            hudInnerBezel = new System.Windows.Shapes.Ellipse { Stroke = WpfFieldTheme.Line, StrokeThickness = 1.2, Opacity = 0.7, IsHitTestVisible = false, Margin = new Thickness(4) };
            hudSquareBezel = new Border { BorderBrush = WpfFieldTheme.Cyan, BorderThickness = new Thickness(2.0), CornerRadius = new CornerRadius(10), Opacity = 0.82, IsHitTestVisible = false, Margin = new Thickness(1), Visibility = Visibility.Collapsed };
            hudFrame.Children.Add(hudBezel);
            hudFrame.Children.Add(hudInnerBezel);
            hudFrame.Children.Add(hudSquareBezel);
            layout.Children.Add(hudFrame);
            var text = new StackPanel();
            hudAccentBar = new Border { Height = 2, CornerRadius = new CornerRadius(1), Background = WpfFieldTheme.Cyan, Margin = new Thickness(0, 0, 0, 5), Opacity = 0.85 };
            text.Children.Add(hudAccentBar);
            var locationRow = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            hudStatusDot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = WpfFieldTheme.StatusIdle, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            DockPanel.SetDock(hudStatusDot, Dock.Left);
            locationRow.Children.Add(hudStatusDot);
            hudSquadCount = WpfFieldTheme.Text("", 10.5, WpfFieldTheme.Cyan, true);
            hudSquadBadge = new Border { Background = WpfFieldTheme.ColorBrush("#2E2DD4BF"), BorderBrush = WpfFieldTheme.ColorBrush("#662DD4BF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(0, 0, 6, 0), Visibility = Visibility.Collapsed, Child = hudSquadCount };
            DockPanel.SetDock(hudSquadBadge, Dock.Left);
            locationRow.Children.Add(hudSquadBadge);
            hudSector = WpfFieldTheme.Text("", 11.5, WpfFieldTheme.Amber, true);
            hudSectorBadge = new Border { Background = WpfFieldTheme.ColorBrush("#3338BDF8"), BorderBrush = WpfFieldTheme.ColorBrush("#7038BDF8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 7, 0), Child = hudSector };
            DockPanel.SetDock(hudSectorBadge, Dock.Left);
            locationRow.Children.Add(hudSectorBadge);
            hudLocation = new WpfSatnavTicker(13.5, WpfFieldTheme.ColorBrush("#F8FAFC"));
            locationRow.Children.Add(hudLocation);
            hudRoute = new WpfSatnavTicker(13.0, WpfFieldTheme.Cyan);
            text.Children.Add(locationRow);
            text.Children.Add(hudRoute);
            hudDividerBorder = new Border { Height = 1, Background = WpfFieldTheme.ColorBrush("#3894A3B8"), Margin = new Thickness(0, 4, 0, 4) };
            text.Children.Add(hudDividerBorder);
            var clockRow = new DockPanel { MinHeight = 20, Margin = new Thickness(0, 0, 0, 1) };
            hudRealClock = WpfFieldTheme.Text("", 11.5, WpfFieldTheme.ColorBrush("#E2E8F0"), true);
            hudRealClock.VerticalAlignment = VerticalAlignment.Center;
            hudRealClock.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(hudRealClock, Dock.Right);
            clockRow.Children.Add(hudRealClock);
            hudGameClock = WpfFieldTheme.Text("", 11.5, WpfFieldTheme.Amber, true);
            hudGameClock.FontFamily = SystemTypography.WpfClockFontFamily;
            hudGameClock.VerticalAlignment = VerticalAlignment.Center;
            hudGameClock.Padding = new Thickness(2, 1, 6, 2);
            hudGameClock.ToolTip = "SCUM Server Time (" + ScumGameClock.TimeScale.ToString("0.##", CultureInfo.InvariantCulture) + "x)";
            DockPanel.SetDock(hudGameClock, Dock.Left);
            clockRow.Children.Add(hudGameClock);
            hudTelemetry = WpfFieldTheme.Text("", 11.0, WpfFieldTheme.ColorBrush("#CBD5E1"), true);
            hudTelemetry.HorizontalAlignment = HorizontalAlignment.Center;
            hudTelemetry.VerticalAlignment = VerticalAlignment.Center;
            clockRow.Children.Add(hudTelemetry);
            text.Children.Add(clockRow);
            hudSurvivalBars = new UniformGrid { Columns = 3, Height = 3, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
            Func<Brush, Thickness, Action<ColumnDefinition, ColumnDefinition, Border>, Border> makeMiniBar = (fill, m, bind) => {
                var g = new Grid();
                var cFill = new ColumnDefinition { Width = new GridLength(100, GridUnitType.Star) };
                var cRem = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
                g.ColumnDefinitions.Add(cFill);
                g.ColumnDefinitions.Add(cRem);
                var bar = new Border { Background = fill, CornerRadius = new CornerRadius(1.5) };
                Grid.SetColumn(bar, 0);
                g.Children.Add(bar);
                bind(cFill, cRem, bar);
                return new Border { Background = WpfFieldTheme.ColorBrush("#2894A3B8"), CornerRadius = new CornerRadius(1.5), Margin = m, Child = g };
            };
            hudSurvivalBars.Children.Add(makeMiniBar(WpfFieldTheme.StatusLive, new Thickness(0, 0, 2, 0), (f, r, b) => { hudHpFillCol = f; hudHpRemCol = r; hudHpBar = b; }));
            hudSurvivalBars.Children.Add(makeMiniBar(WpfFieldTheme.Amber, new Thickness(1, 0, 1, 0), (f, r, b) => { hudFoodFillCol = f; hudFoodRemCol = r; hudFoodBar = b; }));
            hudSurvivalBars.Children.Add(makeMiniBar(WpfFieldTheme.Cyan, new Thickness(2, 0, 0, 0), (f, r, b) => { hudWaterFillCol = f; hudWaterRemCol = r; hudWaterBar = b; }));
            text.Children.Add(hudSurvivalBars);
            var status = new Border { Background = WpfFieldTheme.ColorBrush("#AD0B1118"), BorderBrush = WpfFieldTheme.ColorBrush("#5538BDF8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 7, 12, 8), Margin = new Thickness(0, 6, 0, 0), Child = text };
            Grid.SetRow(status, 1);
            layout.Children.Add(status);
            hudLayout = layout;
            SystemTypography.ApplyClearType(status);
            hudStatus = status;
            window.Content = layout;
            window.SourceInitialized += (s, e) => { IntPtr handle = new WindowInteropHelper(window).Handle; _ = SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x08000000 | 0x20 | 0x80); };
            window.Closing += (s, e) => { if (!disposed && !exitRequested) { e.Cancel = true; window.Hide(); } };
            window.Closed += (s, e) => { hudClosed = true; };
            return window;
        }
        static DrawingGroup TryLoadCustomMapDrawing(string folder) {
            string customMapPath = Path.Combine(folder, "map.png");
            if (!File.Exists(customMapPath))
                return null;
            try {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(customMapPath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                var drawing = new DrawingGroup();
                using (var dc = drawing.Open()) {
                    dc.DrawImage(bitmap, new Rect(0, 0, 1024, 1024));
                }
                drawing.Freeze();
                return drawing;
            } catch (Exception error) {
                Program.LogException("WpfCustomMap", error);
                return null;
            }
        }
    }
}
