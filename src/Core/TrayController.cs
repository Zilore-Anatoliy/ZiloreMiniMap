#nullable enable
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ZiloreMiniMap {
    internal enum TrayIconStatus { Idle, Live, Paused }

    /// <summary>
    /// Modern rounded Fluent-style renderer for the system tray menu,
    /// synchronized with Light / Dark / OLED themes via OverlayTheme.
    /// </summary>
    internal sealed class TacticalTrayRenderer : ToolStripProfessionalRenderer {
        internal TacticalTrayRenderer() : base(new TacticalTrayColorTable()) {
            RoundedEdges = true;
        }

        static GraphicsPath RoundedRect(Rectangle r, int radius) {
            var p = new GraphicsPath();
            int d = Math.Max(2, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) {
            using (var bg = new SolidBrush(OverlayTheme.Background))
                e.Graphics.FillRectangle(bg, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) {
            var r = new Rectangle(0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
            using (var pen = new Pen(OverlayTheme.Border, 1f))
                e.Graphics.DrawRectangle(pen, r);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            SystemTypography.ApplyGdiClearType(g);
            var rect = new Rectangle(4, 1, Math.Max(8, e.Item.Width - 8), Math.Max(8, e.Item.Height - 2));
            if (e.Item.Enabled && (e.Item.Selected || e.Item.Pressed)) {
                using (var path = RoundedRect(rect, 6))
                using (var fill = new SolidBrush(OverlayTheme.SurfaceCard))
                using (var edge = new Pen(OverlayTheme.BorderHighlight, 1f)) {
                    g.FillPath(fill, path);
                    g.DrawPath(edge, path);
                }
                using (var bar = new SolidBrush(OverlayTheme.Accent))
                    g.FillRectangle(bar, rect.Left + 2, rect.Top + 5, 3, Math.Max(4, rect.Height - 10));
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
            int y = e.Item.Height / 2;
            using (var pen = new Pen(OverlayTheme.Border, 1f))
                e.Graphics.DrawLine(pen, 10, y, Math.Max(10, e.Item.Width - 10), y);
        }

        sealed class TacticalTrayColorTable : ProfessionalColorTable {
            public override Color ToolStripDropDownBackground { get { return OverlayTheme.Background; } }
            public override Color MenuBorder { get { return OverlayTheme.Border; } }
            public override Color MenuItemBorder { get { return OverlayTheme.BorderHighlight; } }
            public override Color MenuItemSelected { get { return OverlayTheme.SurfaceCard; } }
            public override Color ImageMarginGradientBegin { get { return OverlayTheme.Background; } }
            public override Color ImageMarginGradientMiddle { get { return OverlayTheme.Background; } }
            public override Color ImageMarginGradientEnd { get { return OverlayTheme.Background; } }
        }
    }

    /// <summary>
    /// Encapsulates the Windows system tray icon (with automatic Day/Night taskbar adaptation),
    /// custom Fluent context menu, update notifications, and ErrorReportService balloon routing.
    /// </summary>
    internal sealed class TrayController : IDisposable {
        readonly System.Windows.Threading.Dispatcher? uiDispatcher;
        readonly Func<bool>? isOwnerDisposed;
        readonly NotifyIcon icon;
        readonly Icon? fallbackIcon;
        static readonly Font HeaderFont = SystemTypography.GetGdiFont(9f, FontStyle.Bold);
        readonly TacticalTrayRenderer trayRenderer = new TacticalTrayRenderer();
        readonly ContextMenuStrip menu = new ContextMenuStrip { ShowImageMargin = false };
        readonly Action onFullMap, onSearch, onToggleOverlay, onSettings, onAdmin, onCheckUpdates, onAbout, onExit;
        readonly bool diagnosticMode;
        Icon? dynamicIcon;
        TrayIconStatus currentStatus = TrayIconStatus.Idle;
        bool lastTaskbarLight;
        bool themeHooked;
        string? pendingReportZip, pendingReportHint;
        bool disposed;

        internal NotifyIcon IconControl { get { return icon; } }

        internal TrayController(
            System.Windows.Threading.Dispatcher? uiDispatcher,
            Func<bool>? isOwnerDisposed,
            Icon? appIcon,
            string? tooltipText,
            bool diagnosticMode,
            Action onFullMap,
            Action onSearch,
            Action onToggleOverlay,
            Action onSettings,
            Action onAdmin,
            Action onCheckUpdates,
            Action onAbout,
            Action onExit) {
            this.uiDispatcher = uiDispatcher;
            this.isOwnerDisposed = isOwnerDisposed;
            this.fallbackIcon = appIcon;
            this.diagnosticMode = diagnosticMode;
            this.onFullMap = onFullMap;
            this.onSearch = onSearch;
            this.onToggleOverlay = onToggleOverlay;
            this.onSettings = onSettings;
            this.onAdmin = onAdmin;
            this.onCheckUpdates = onCheckUpdates;
            this.onAbout = onAbout;
            this.onExit = onExit;

            lastTaskbarLight = WpfFieldTheme.SystemTaskbarPrefersLight();
            dynamicIcon = CreateAdaptiveTrayIcon(lastTaskbarLight, currentStatus);

            string safeTip = tooltipText ?? "ZiloreMiniMap";
            icon = new NotifyIcon {
                Icon = dynamicIcon ?? fallbackIcon,
                Text = safeTip.Length > 63 ? safeTip.Substring(0, 63) : safeTip,
                Visible = true,
                ContextMenuStrip = menu
            };

            icon.DoubleClick += (s, e) => this.onSettings();
            icon.BalloonTipClicked += (s, e) => {
                if (!string.IsNullOrEmpty(pendingReportZip)) {
                    string zip = pendingReportZip;
                    string? hint = pendingReportHint;
                    pendingReportZip = null;
                    pendingReportHint = null;
                    ErrorReportService.OpenMailtoAndSelectReport(zip, hint);
                } else {
                    this.onCheckUpdates();
                }
            };

            if (!diagnosticMode) {
                ErrorReportService.Initialize();
                ErrorReportService.ReportCreated += OnErrorReportCreated;
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                themeHooked = true;
            }

            RebuildMenu();
        }

        void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) {
            if (disposed || (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color)) return;
            if (uiDispatcher != null && !uiDispatcher.HasShutdownStarted && (isOwnerDisposed == null || !isOwnerDisposed())) {
                try { _ = uiDispatcher.BeginInvoke(new Action(() => RefreshThemeIcon(null))); } catch { }
            }
        }

        internal void RefreshThemeIcon(TrayIconStatus? statusOverride = null) {
            if (disposed) return;
            TrayIconStatus nextStatus = statusOverride ?? currentStatus;
            bool taskbarLight = WpfFieldTheme.SystemTaskbarPrefersLight();
            if (dynamicIcon != null && nextStatus == currentStatus && taskbarLight == lastTaskbarLight) return;
            currentStatus = nextStatus;
            lastTaskbarLight = taskbarLight;
            Icon? next = CreateAdaptiveTrayIcon(taskbarLight, currentStatus);
            if (next != null) {
                Icon? prev = dynamicIcon;
                dynamicIcon = next;
                icon.Icon = dynamicIcon;
                if (prev != null) { try { prev.Dispose(); } catch { } }
            }
        }

        internal static Icon CreateAdaptiveTrayIcon(bool lightTaskbar, TrayIconStatus status = TrayIconStatus.Idle) {
            return AdaptiveAppIcon.CreateTrayIcon(lightTaskbar, status);
        }

        void OnErrorReportCreated(string zip, string level, string source) {
            if (disposed || uiDispatcher == null || uiDispatcher.HasShutdownStarted || (isOwnerDisposed != null && isOwnerDisposed())) return;
            try {
                _ = uiDispatcher.BeginInvoke(new Action(() => {
                    if (disposed) return;
                    pendingReportZip = zip;
                    pendingReportHint = level + " (" + source + ")";
                    string title = Localization.Current == AppLanguage.Russian
                        ? "ZiloreMiniMap — Отчёт об ошибке"
                        : "ZiloreMiniMap — Error Report";
                    string text = Localization.Current == AppLanguage.Russian
                        ? "Обнаружена ошибка (" + source + "). Архив сохранён. Нажмите, чтобы отправить на " + MapWindow.SupportEmail
                        : "Error detected (" + source + "). Archive saved. Click to send to " + MapWindow.SupportEmail;
                    icon.ShowBalloonTip(6000, title, text, level == "ERROR" ? ToolTipIcon.Error : ToolTipIcon.Warning);
                }));
            } catch (Exception ex) { Program.LogException("TrayErrorReport", ex); }
        }

        internal void ShowBalloonTip(int timeoutMs, string title, string text, ToolTipIcon tipIcon) {
            if (disposed) return;
            pendingReportZip = null;
            pendingReportHint = null;
            try { icon.ShowBalloonTip(timeoutMs, title, text, tipIcon); } catch (Exception ex) { Program.LogException("TrayBalloon", ex); }
        }

        internal void RebuildMenu() {
            if (disposed) return;
            RefreshThemeIcon(null);

            while (menu.Items.Count > 0) {
                ToolStripItem itm = menu.Items[0];
                menu.Items.RemoveAt(0);
                itm.Dispose();
            }

            string fullMapText = Localization.Get("FullMap");
            if (!string.IsNullOrEmpty(fullMapText) && fullMapText == fullMapText.ToUpper(CultureInfo.CurrentCulture))
                fullMapText = char.ToUpper(fullMapText[0], CultureInfo.CurrentCulture) + fullMapText.Substring(1).ToLower(CultureInfo.CurrentCulture);

            bool ruTray = Localization.Current == AppLanguage.Russian;
            string themeTag = WpfFieldTheme.SystemTaskbarPrefersLight() ? (ruTray ? "День" : "Day") : (ruTray ? "Ночь" : "Night");
            var headerItem = new ToolStripMenuItem("◆  ZiloreMiniMap v" + MapWindow.VersionString + " · " + themeTag + " · SCUM GRAND") {
                Enabled = false,
                Font = HeaderFont
            };
            menu.Items.Add(headerItem);
            var readyRel = WpfFieldConsoleRuntime.Active != null ? WpfFieldConsoleRuntime.Active.ReadyUpdateRelease : null;
            if (readyRel != null) {
                string readyLbl = ruTray
                    ? ("⬇  Установить обновление v" + readyRel.VersionText + " (перезапуск)")
                    : ("⬇  Install Update v" + readyRel.VersionText + " (Restart)");
                var updNowItem = new ToolStripMenuItem(readyLbl, null, (s, e) => { var rt = WpfFieldConsoleRuntime.Active; if (rt != null) rt.PostApplyReadyUpdate(); }) { Font = HeaderFont };
                menu.Items.Add(updNowItem);
            }
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("🗺  " + fullMapText, null, (s, e) => onFullMap());
            menu.Items.Add("⌕  " + Localization.Get("SearchPlaceOrGrid"), null, (s, e) => onSearch());
            menu.Items.Add("◉  " + Localization.Get("TrayShowHide"), null, (s, e) => onToggleOverlay());
            menu.Items.Add("🛣  " + Localization.Get("RouteList"), null, (s, e) => { var rt = WpfFieldConsoleRuntime.Active; if (rt != null) rt.PostShowRoutes(); });
            menu.Items.Add("📘  " + (ruTray ? "Руководство пользователя" : "User Guide"), null, (s, e) => { var rt = WpfFieldConsoleRuntime.Active; if (rt != null) rt.PostShowUserGuide(); });
            menu.Items.Add("📖  " + WpfFieldTheme.UiText("HotkeysGuideTitle"), null, (s, e) => { var rt = WpfFieldConsoleRuntime.Active; if (rt != null) rt.PostShowHotkeysGuide(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("⚙  " + Localization.Get("Settings"), null, (s, e) => onSettings());
            menu.Items.Add("🛡  " + Localization.Get("ControlAdminCommands"), null, (s, e) => onAdmin());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("💬  " + WpfFieldTheme.UiText("AboutDiscord"), null, (s, e) => OpenUrl(MapWindow.DiscordUrl));
            menu.Items.Add("👤  " + WpfFieldTheme.UiText("AboutDeveloper"), null, (s, e) => OpenUrl(MapWindow.DeveloperDiscordUrl));

            var wargmMenu = new ToolStripMenuItem("★  " + WpfFieldTheme.UiText("AboutWargmHeading"));
            var wargmDrop = wargmMenu.DropDown as ToolStripDropDownMenu;
            if (wargmDrop != null) { wargmDrop.ShowImageMargin = false; wargmDrop.Padding = new Padding(4, 5, 4, 5); }
            foreach (var srv in MapWindow.WargmServers) {
                string url = srv.Item2;
                var sub = new ToolStripMenuItem("▸  " + srv.Item1, null, (s, e) => OpenUrl(url)) { Padding = new Padding(6, 3, 10, 3) };
                wargmMenu.DropDownItems.Add(sub);
            }
            menu.Items.Add(wargmMenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("↻  " + Localization.Get("TrayCheckUpdates"), null, (s, e) => onCheckUpdates()).Enabled = !Program.IsTestBuild;
            menu.Items.Add("ℹ  " + WpfFieldTheme.UiText("About"), null, (s, e) => onAbout());
            menu.Items.Add("⚖  " + (ruTray ? "Лицензионное соглашение (EULA)" : "License Agreement (EULA)"), null, (s, e) => { var rt = WpfFieldConsoleRuntime.Active; if (rt != null) rt.PostShowEula(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("⏻  " + Localization.Get("TrayExit"), null, (s, e) => onExit());

            OverlayTheme.Menu(menu);
            menu.Renderer = trayRenderer;
            menu.Padding = new Padding(4, 6, 4, 6);
            foreach (ToolStripItem item in menu.Items) {
                if (item is ToolStripMenuItem) item.Padding = new Padding(6, 3, 10, 3);
            }
            headerItem.ForeColor = OverlayTheme.Accent;
        }

        static void OpenUrl(string url) {
            if (!WpfFieldTheme.OpenShellUri(url)) Program.LogWarn("TrayOpenUrl", "Blocked or failed to open URL.");
        }

        public void Dispose() {
            if (disposed) return;
            disposed = true;
            if (themeHooked) {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                themeHooked = false;
            }
            ErrorReportService.ReportCreated -= OnErrorReportCreated;
            try {
                icon.Visible = false;
                icon.Dispose();
            } catch { }
            if (dynamicIcon != null) {
                try { dynamicIcon.Dispose(); } catch { }
                dynamicIcon = null;
            }
            try { menu.Dispose(); } catch { }
        }
    }
}
