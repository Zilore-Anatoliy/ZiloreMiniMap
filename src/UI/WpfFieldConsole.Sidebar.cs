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
        static readonly ControlTemplate SquadPillTemplate = CreateSquadPillTemplate();
        static ControlTemplate CreateSquadPillTemplate() {
            var template = new ControlTemplate(typeof(ToggleButton));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            border.AppendChild(content);
            template.VisualTree = border;
            template.Seal();
            return template;
        }
        static Border SidebarGlassCard(UIElement content, Thickness? padding = null, bool accent = false) {
            bool isLight = string.Equals(WpfFieldTheme.ResolveTheme(WpfFieldTheme.ActiveTheme), "Light", StringComparison.OrdinalIgnoreCase);
            var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = padding ?? new Thickness(12, 9, 12, 9), SnapsToDevicePixels = true, Child = content };
            if (accent) {
                card.SetResourceReference(Border.BackgroundProperty, "ThemeAccentCard");
                card.SetResourceReference(Border.BorderBrushProperty, "ThemeAccentBorder");
            } else {
                card.Background = WpfFieldTheme.ColorBrush(isLight ? "#CCFFFFFF" : "#7A111827");
                card.BorderBrush = WpfFieldTheme.ColorBrush(isLight ? "#6694A3B8" : "#4438BDF8");
            }
            return card;
        }
        static FrameworkElement SidebarSectionTitle(string text, Thickness margin) {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = margin };
            var bar = new Border { Width = 3, Height = 12, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            bar.SetResourceReference(Border.BackgroundProperty, "ThemeCyan");
            row.Children.Add(bar);
            row.Children.Add(WpfFieldTheme.Text(text, 11, WpfFieldTheme.Muted, true));
            return row;
        }
        void AddPlayers(Panel container, bool registerLabels = true) {
            if (privatePositions == null && (relayGroups == null || relayGroups.Sessions.Length == 0) && playerRoster.Length == 0)
                return;
            DateTime nowUtc = DateTime.UtcNow;
            int onlineCount = 0;
            for (int pi = 0; pi < playerRoster.Length; pi++) {
                if (squadCoordinator.IsPeerOnline(playerRoster[pi].Id, nowUtc))
                    onlineCount++;
            }
            string squadHeader = Localization.Get("SharedPlayers") + (playerRoster.Length > 0 ? (" · " + onlineCount + "/" + playerRoster.Length + " " + WpfFieldTheme.UiText("SquadOnlineCount")) : "");
            var hdrDock = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var onlineFilterBtn = WpfFieldTheme.Button(WpfFieldTheme.UiText("SquadOnlineOnly"), () => { squadOnlineOnly = !squadOnlineOnly; filterIdentity = null; Refresh(); }, squadOnlineOnly);
            onlineFilterBtn.MinHeight = 24;
            onlineFilterBtn.Padding = new Thickness(8, 2, 8, 2);
            onlineFilterBtn.FontSize = 11;
            DockPanel.SetDock(onlineFilterBtn, Dock.Right);
            hdrDock.Children.Add(onlineFilterBtn);
            hdrDock.Children.Add(WpfFieldTheme.Text(squadHeader, 11, WpfFieldTheme.Muted, true));
            container.Children.Add(hdrDock);
            if (playerRoster.Length == 0)
                container.Children.Add(WpfFieldTheme.Text(Localization.Get("PlayerWaiting"), 12, WpfFieldTheme.Muted));
            foreach (var player in playerRoster) {
                bool isOnline = squadCoordinator.IsPeerOnline(player.Id, DateTime.UtcNow);
                if (squadOnlineOnly && !isOnline)
                    continue;
                string ownerId = player.Id;
                bool isLead = WpfMapSurface.SquadLeadPeerId == ownerId;
                double pSpd;
                WpfMapSurface.PeerSpeedKmh.TryGetValue(ownerId, out pSpd);
                string badgePrefix = (isLead ? "★ " : "") + (pSpd >= 110.0 ? "✈ " : (pSpd >= 25.0 ? "🚗 " : ""));
                var body = new StackPanel();
                body.Children.Add(WpfFieldTheme.Text(badgePrefix + player.Name, 16, isLead ? WpfFieldTheme.Amber : WpfMapSurface.MemberInk(ownerId), true));
                var statusLabel = WpfFieldTheme.Text(PeerStatus(player), 11, WpfFieldTheme.Muted);
                statusLabel.Tag = ownerId;
                statusLabel.TextWrapping = TextWrapping.Wrap;
                statusLabel.TextTrimming = TextTrimming.None;
                if (registerLabels)
                    playerStatusLabels[ownerId] = statusLabel;
                body.Children.Add(statusLabel);
                var peerActions = new UniformGrid { Columns = 3, Margin = new Thickness(0, 6, 0, 0) };
                var locate = WpfFieldTheme.Button(WpfFieldTheme.UiText("SquadLocateBtn"), () => LocateSquadPeer(ownerId));
                locate.ToolTip = WpfFieldTheme.UiText("SquadLocateTip");
                locate.IsEnabled = player.Position != null;
                locate.MinHeight = 30;
                locate.Padding = new Thickness(5, 3, 5, 3);
                locate.FontSize = 11.5;
                locate.Margin = new Thickness(0, 0, 3, 0);
                var follow = WpfFieldTheme.Button(S(followingPeer == ownerId ? "SquadStopNav" : "SquadFollow"), () => { if (followingPeer == ownerId) { StopSquadNavigation(); filterIdentity = null; Refresh(); } else FollowSquadPeer(ownerId); });
                follow.IsEnabled = player.Position != null || followingPeer == ownerId;
                follow.MinHeight = 30;
                follow.Padding = new Thickness(5, 3, 5, 3);
                follow.FontSize = 11.5;
                follow.Margin = new Thickness(2, 0, 2, 0);
                var leadBtn = WpfFieldTheme.Button(WpfFieldTheme.UiText(isLead ? "SquadUnsetLead" : "SquadSetLead"), () => ToggleSquadLead(ownerId), isLead);
                leadBtn.MinHeight = 30;
                leadBtn.Padding = new Thickness(5, 3, 5, 3);
                leadBtn.FontSize = 11.5;
                leadBtn.Margin = new Thickness(3, 0, 0, 0);
                peerActions.Children.Add(locate);
                peerActions.Children.Add(follow);
                peerActions.Children.Add(leadBtn);
                body.Children.Add(peerActions);
                var pills = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                string[] labels = { "PlayerLiveLocation", "PlayerGps", "PlayerSaved", "PlayerDeaths", "RouteList" };
                for (int index = 0; index < labels.Length; index++) {
                    int bit = 1 << index;
                    bool selected = (PlayerVisibility(ownerId) & bit) != 0;
                    var pill = new ToggleButton { Content = Localization.Get(labels[index]), IsChecked = selected, Tag = "player:" + ownerId + ":" + bit, Margin = new Thickness(0, 0, 5, 5), Padding = new Thickness(9, 4, 9, 4), FontSize = 12, BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
                    pill.SetResourceReference(Control.ForegroundProperty, selected ? "ThemePrimaryInk" : "ThemeInk");
                    pill.SetResourceReference(Control.BackgroundProperty, selected ? "ThemeAmber" : "ThemePanel");
                    pill.SetResourceReference(Control.BorderBrushProperty, "ThemeLine");
                    pill.Template = SquadPillTemplate;
                    System.Windows.Automation.AutomationProperties.SetName(pill, player.Name + ": " + Localization.Get(labels[index]));
                    pill.Click += (s, e) => { int mask = PlayerVisibility(ownerId); bool on = pill.IsChecked == true; playerVisibility[ownerId] = on ? mask | bit : mask & ~bit; pill.SetResourceReference(Control.BackgroundProperty, on ? "ThemeAmber" : "ThemePanel"); pill.SetResourceReference(Control.ForegroundProperty, on ? "ThemePrimaryInk" : "ThemeInk"); peersCacheValid = false; lastRefresh = -1000; Refresh(); };
                    pills.Children.Add(pill);
                }
                body.Children.Add(pills);
                var card = SidebarGlassCard(body, new Thickness(10));
                card.Margin = new Thickness(0, 8, 0, 10);
                container.Children.Add(card);
            }
        }
        sealed class CustomZoneListItem {
            public bool IsLayer {
                get; set;
            }
            public string Key {
                get; set;
            }
            public string Title {
                get; set;
            }
            public bool Checked {
                get; set;
            }
            public Thickness ItemMargin {
                get {
                    return IsLayer ? new Thickness(12, 4, 0, 4) : new Thickness(28, 2, 0, 2);
                }
            }
            public string ItemTag {
                get {
                    return (IsLayer ? "custom-zone-layer:" : "custom-zone:") + Key;
                }
            }
        }
        ListBox BuildVirtualizedCustomZonesList(WpfZoneOption[] customZones) {
            var flat = new List<CustomZoneListItem>();
            foreach (var group in customZones.GroupBy(z => z.Layer).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)) {
                string layer = group.Key;
                flat.Add(new CustomZoneListItem { IsLayer = true, Key = layer, Title = layer, Checked = group.First().LayerEnabled });
                foreach (var option in group.OrderBy(z => z.Zone.DisplayName, StringComparer.CurrentCultureIgnoreCase)) {
                    flat.Add(new CustomZoneListItem { IsLayer = false, Key = option.Key, Title = option.Zone.DisplayName, Checked = option.Enabled });
                }
            }
            var list = new ListBox {
                ItemsSource = flat,
                MaxHeight = 320,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 4, 0, 6)
            };
            VirtualizingPanel.SetIsVirtualizing(list, true);
            VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(list, true);
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);

            var itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(Control.FocusableProperty, false));
            list.ItemContainerStyle = itemStyle;

            var factory = new FrameworkElementFactory(typeof(CheckBox));
            factory.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding("Title"));
            factory.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding("Title"));
            factory.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("Checked") { Mode = System.Windows.Data.BindingMode.OneWay });
            factory.SetBinding(FrameworkElement.MarginProperty, new System.Windows.Data.Binding("ItemMargin"));
            factory.SetBinding(FrameworkElement.TagProperty, new System.Windows.Data.Binding("ItemTag"));
            factory.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) => {
                if (rebuilding)
                    return;
                var box = (CheckBox)s;
                var item = box.DataContext as CustomZoneListItem;
                if (item == null)
                    return;
                bool isChecked = box.IsChecked == true;
                item.Checked = isChecked;
                if (item.IsLayer)
                    engine.WpfZoneLayerToggle(item.Key, isChecked);
                else
                    engine.WpfZoneToggle(item.Key, isChecked);
                filterIdentity = null;
                Refresh();
            }));
            list.ItemTemplate = new DataTemplate { VisualTree = factory };
            return list;
        }
        internal bool PromptPoiAccessCode() {
            bool granted = false;
            bool ru = Localization.Current == AppLanguage.Russian;
            var window = Window(Localization.Get("SidebarCategories"), 455, 325);
            var layout = new StackPanel { Margin = new Thickness(20) };
            layout.Children.Add(WpfFieldTheme.Text(ru ? "Доступ к POI и Zilore AI" : "POI & Zilore AI Access", 18, null, true));
            var hint = WpfFieldTheme.Text(ru ? "Введите код доступа (1–24 ч. / 30–365 дн.) или мастер-код:" : "Enter your access code (1–24h / 30–365d) or master code:", 13, WpfFieldTheme.Muted);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 6, 0, 8);
            layout.Children.Add(hint);
            var donateDock = new DockPanel();
            var donateInfoBtn = WpfFieldTheme.Button(ru ? "★ Подробнее" : "★ Details", () => ShowDonateAccessDialog());
            donateInfoBtn.MinHeight = 28;
            donateInfoBtn.Padding = new Thickness(10, 3, 10, 3);
            donateInfoBtn.FontSize = 12;
            donateInfoBtn.VerticalAlignment = VerticalAlignment.Center;
            donateInfoBtn.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(donateInfoBtn, Dock.Right);
            donateDock.Children.Add(donateInfoBtn);
            var donateTxt = WpfFieldTheme.Text(ru ? "★ Код доступа можно получить через донат." : "★ Access codes can be obtained via donation.", 12.5, WpfFieldTheme.Amber, true);
            donateTxt.TextWrapping = TextWrapping.Wrap;
            donateTxt.VerticalAlignment = VerticalAlignment.Center;
            donateDock.Children.Add(donateTxt);
            var donateCard = WpfFieldTheme.Card(donateDock, new Thickness(10, 7, 10, 7));
            donateCard.Margin = new Thickness(0, 0, 0, 12);
            layout.Children.Add(donateCard);
            var box = new TextBox { Height = 34, Padding = new Thickness(8, 5, 8, 5), FontSize = 14 };
            layout.Children.Add(box);
            var error = WpfFieldTheme.Text("", 12.5, WpfFieldTheme.Danger);
            error.TextWrapping = TextWrapping.Wrap;
            error.Margin = new Thickness(0, 6, 0, 0);
            error.Visibility = Visibility.Collapsed;
            layout.Children.Add(error);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            Action submit = () => {
                string err;
                if (PoiAccessGuard.TryActivate(box.Text, out err)) {
                    granted = true;
                    window.Close();
                } else {
                    error.Text = err;
                    error.Visibility = Visibility.Visible;
                    box.SelectAll();
                    box.Focus();
                }
            };
            var ok = WpfFieldTheme.Button(Localization.Current == AppLanguage.Russian ? "Активировать" : "Activate", submit, true);
            ok.Margin = new Thickness(0, 0, 8, 0);
            var cancel = WpfFieldTheme.Button(Localization.Get("Cancel"), () => window.Close());
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            layout.Children.Add(buttons);
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { submit(); e.Handled = true; } };
            window.Loaded += (s, e) => box.Focus();
            WpfFieldTheme.ApplyFluentShell(window, "ZiloreMiniMap — " + Localization.Get("SidebarCategories"), layout);
            Modal(window);
            return granted;
        }
        void RebuildSidebar(WpfMapState state) {
            poiCategoryControls.Clear();
            poiSectionControls.Clear();
            poiSectionCounts.Clear();
            presentedPoiFilter = null;
            playerStatusLabels.Clear();
            sidebar.Children.Clear();
            bool ruSb = Localization.Current == AppLanguage.Russian;
            var topRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var keyPill = WpfFieldTheme.Badge(WpfFieldTheme.Text(PhysicalKeyCapture.KeyName(state.MapKey), 12, WpfFieldTheme.Cyan, true), new Thickness(8, 3, 8, 3));
            keyPill.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(keyPill, Dock.Right);
            topRow.Children.Add(keyPill);
            var grandSbImg = WpfFieldTheme.LoadEmbeddedImage("grand-banner.png");
            if (grandSbImg != null) {
                var grandSb = new System.Windows.Controls.Image { Source = grandSbImg, Height = 24, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(grandSb, BitmapScalingMode.HighQuality);
                var grandBadge = new Border { Background = WpfFieldTheme.ColorBrush("#0F172A"), CornerRadius = new CornerRadius(7), Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Child = grandSb };
                DockPanel.SetDock(grandBadge, Dock.Left);
                topRow.Children.Add(grandBadge);
            }
            var sbTitle = WpfFieldTheme.Text(Localization.Get("SidebarTitle"), 18, null, true);
            sbTitle.VerticalAlignment = VerticalAlignment.Center;
            topRow.Children.Add(sbTitle);
            sidebar.Children.Add(topRow);
            var readyUpd = engine.WpfReadyUpdateRelease;
            if (readyUpd != null) {
                string updLbl = ruSb ? ("⬇ Обновление v" + readyUpd.VersionText + " готово · Перезапустить") : ("⬇ Update v" + readyUpd.VersionText + " ready · Restart");
                var updPillBtn = WpfFieldTheme.Button(updLbl, () => ApplyReadyUpdateNow(), true);
                updPillBtn.Tag = "sidebar-update-ready";
                updPillBtn.Background = WpfFieldTheme.StatusLive;
                updPillBtn.BorderBrush = WpfFieldTheme.StatusLive;
                updPillBtn.Foreground = WpfFieldTheme.ColorBrush("#052E16");
                updPillBtn.MinHeight = 34;
                updPillBtn.FontSize = 12.5;
                updPillBtn.FontWeight = FontWeights.Bold;
                updPillBtn.Margin = new Thickness(0, 0, 0, 8);
                sidebar.Children.Add(updPillBtn);
            }
            var search = WpfFieldTheme.Button("", ShowSearch, true);
            var searchDock = new DockPanel { LastChildFill = true };
            var searchKeyBadge = new TextBlock { Text = "[" + PhysicalKeyCapture.KeyName(state.Bindings.Key(AppControlAction.Search)) + "]", FontSize = 11, FontWeight = FontWeights.SemiBold, Opacity = 0.85, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(searchKeyBadge, Dock.Right);
            searchDock.Children.Add(searchKeyBadge);
            var searchLbl = new TextBlock { Text = "⌕  " + Localization.Get("SearchTitle"), FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            searchDock.Children.Add(searchLbl);
            search.Content = searchDock;
            search.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            search.Padding = new Thickness(10, 5, 10, 5);
            search.Margin = new Thickness(0, 0, 0, 10);
            search.MinHeight = 36;
            sidebar.Children.Add(search);
            var quickGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 8) };
            Func<string, string, Action, string, Thickness, Button> makeQuickBtn = (lbl, tip, act, tag, m) => {
                var b = WpfFieldTheme.Button("", act);
                b.Content = new TextBlock { Text = lbl, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, FontSize = 12.5, FontWeight = FontWeights.Medium };
                b.ToolTip = tip;
                b.Tag = tag;
                b.MinHeight = 34;
                b.Padding = new Thickness(6, 4, 6, 4);
                b.Margin = m;
                return b;
            };
            quickGrid.Children.Add(makeQuickBtn("🛣 " + S("RouteList"), S("RouteList") + " [F9]", ShowRoutes, "sidebar-routes", new Thickness(0, 0, 3, 6)));
            quickGrid.Children.Add(makeQuickBtn("👥 " + (ruSb ? "Отряд" : "Squad"), S("SquadTools"), ShowSquadTools, "sidebar-squad", new Thickness(3, 0, 0, 6)));
            quickGrid.Children.Add(makeQuickBtn("🗂 " + (ruSb ? "Профили" : "Presets"), S("SquadPresets"), ShowMapPresets, "sidebar-presets", new Thickness(0, 0, 3, 0)));
            quickGrid.Children.Add(makeQuickBtn("⊞ " + (ruSb ? "Позиция карты" : "Map Layout"), Localization.Get("MapPlacement"), () => Modal(BuildMapPlacement()), "map-placement", new Thickness(3, 0, 0, 0)));
            sidebar.Children.Add(quickGrid);
            var modeStrip = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 10) };
            Func<string, string, Action, bool, string, Button> makeMiniTool = (ic, tip, act, accent, tag) => {
                var b = WpfFieldTheme.Button(ic, act, accent);
                b.ToolTip = tip;
                if (tag != null)
                    b.Tag = tag;
                b.MinWidth = 0;
                b.MinHeight = 32;
                b.FontSize = 13;
                b.Padding = new Thickness(1, 3, 1, 3);
                b.Margin = new Thickness(1.5, 0, 1.5, 0);
                return b;
            };
            bool isLightThemeNow = string.Equals(WpfFieldTheme.ResolveTheme(WpfFieldTheme.ActiveTheme), "Light", StringComparison.OrdinalIgnoreCase);
            modeStrip.Children.Add(makeMiniTool(WpfMapSurface.AviationModeEnabled ? "✈" : "🚗", WpfFieldTheme.UiText("AviationModeTitle") + " [F]", () => ToggleAviationHotkey(), WpfMapSurface.AviationModeEnabled, null));
            modeStrip.Children.Add(makeMiniTool(engine.WpfVoiceEnabled ? "🔊" : "🔇", WpfFieldTheme.UiText("VoiceHeadingClean") + " [V]", () => ToggleVoiceHotkey(), engine.WpfVoiceEnabled, null));
            modeStrip.Children.Add(makeMiniTool("🛣", S("RouteMode" + engine.WpfRouteMode) + " [G]", () => CycleRouteModeHotkey(), engine.WpfRouteMode != RoadRouteMode.Shortest, null));
            modeStrip.Children.Add(makeMiniTool("🛡", WpfFieldTheme.UiText("SidebarAvoidZonesTip"), () => CycleAvoidZonesHotkey(), engine.WpfAvoidZonesMode > 0, "sidebar-avoid"));
            modeStrip.Children.Add(makeMiniTool("🕒", WpfFieldTheme.UiText("SidebarClockBtn"), ShowGameClockDialog, false, "sidebar-clock"));
            modeStrip.Children.Add(makeMiniTool(isLightThemeNow ? "☀" : "☾", WpfFieldTheme.UiText("SidebarThemeTip"), () => CycleThemeHotkey(), false, "sidebar-theme"));
            sidebar.Children.Add(modeStrip);
            bool aiUnlocked = PoiAccessGuard.IsUnlocked;
            var aiDock = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var aiCfgSb = WpfFieldTheme.Button("⚙", ShowAiSettings);
            aiCfgSb.Tag = "sidebar-ai-settings";
            aiCfgSb.ToolTip = Localization.Current == AppLanguage.Russian ? "Настройки Zilore AI" : "Zilore AI Settings";
            aiCfgSb.MinWidth = 34;
            aiCfgSb.MinHeight = 36;
            aiCfgSb.Padding = new Thickness(0);
            aiCfgSb.Margin = new Thickness(4, 0, 0, 0);
            DockPanel.SetDock(aiCfgSb, Dock.Right);
            aiDock.Children.Add(aiCfgSb);
            var aiGrid = new UniformGrid { Columns = 2 };
            var aiBtn = WpfFieldTheme.Button(aiUnlocked ? "🤖 Zilore AI [F6]" : "🔒 Zilore AI", ShowZiloreAi, aiUnlocked);
            aiBtn.Tag = "sidebar-zilore-ai";
            aiBtn.MinHeight = 36;
            aiBtn.FontSize = 11.5;
            aiBtn.Padding = new Thickness(4, 4, 4, 4);
            aiBtn.Margin = new Thickness(0, 0, 3, 0);
            var survBtn = WpfFieldTheme.Button(aiUnlocked ? (Localization.Current == AppLanguage.Russian ? "❤️ Выживание" : "❤️ Survival") : (Localization.Current == AppLanguage.Russian ? "🔒 Выживание" : "🔒 Survival"), ShowSurvivalTracker);
            survBtn.Tag = "sidebar-survival";
            survBtn.MinHeight = 36;
            survBtn.FontSize = 11.5;
            survBtn.Padding = new Thickness(4, 4, 4, 4);
            survBtn.Margin = new Thickness(3, 0, 0, 0);
            aiGrid.Children.Add(aiBtn);
            aiGrid.Children.Add(survBtn);
            aiDock.Children.Add(aiGrid);
            sidebar.Children.Add(aiDock);
            AddPlayers(sidebar);
            string lp = WpfMapSurface.AltLayerHotkeysEnabled ? "Alt+" : "";
            var layersHdrDock = new DockPanel { Margin = new Thickness(2, 2, 0, 6) };
            Button hkModeBtn = null;
            Func<string> hkLabel = () => WpfFieldTheme.UiText(WpfMapSurface.AltLayerHotkeysEnabled ? "LayerHotkeysAlt" : "LayerHotkeysPlain");
            hkModeBtn = WpfFieldTheme.Button(hkLabel(), () => { WpfMapSurface.AltLayerHotkeysEnabled = !WpfMapSurface.AltLayerHotkeysEnabled; WpfMapSurface.SaveFlightUiSettings(); filterIdentity = null; Refresh(); });
            hkModeBtn.ToolTip = WpfFieldTheme.UiText("LayerHotkeysTip");
            hkModeBtn.MinHeight = 24;
            hkModeBtn.FontSize = 11;
            hkModeBtn.Padding = new Thickness(7, 2, 7, 2);
            hkModeBtn.Margin = new Thickness(0);
            DockPanel.SetDock(hkModeBtn, Dock.Right);
            layersHdrDock.Children.Add(hkModeBtn);
            layersHdrDock.Children.Add(SidebarSectionTitle(Localization.Get("SidebarLayers"), new Thickness(0)));
            sidebar.Children.Add(layersHdrDock);
            var layersPanel = new StackPanel();
            var customRow = new DockPanel();
            var customMaster = new CheckBox { IsChecked = state.Zones, Width = 46, Tag = "custom-zones:master", ToolTip = Localization.Get("ShowSavedZones") };
            customMaster.Checked += (s, e) => { if (!rebuilding) { engine.WpfLayer("zones", true); SchedulePoiFilterRefresh(); } };
            customMaster.Unchecked += (s, e) => { if (!rebuilding) { engine.WpfLayer("zones", false); SchedulePoiFilterRefresh(); } };
            DockPanel.SetDock(customMaster, Dock.Right);
            customRow.Children.Add(customMaster);
            var customExpand = WpfFieldTheme.Button((state.CustomZonesExpanded ? "⌄  " : "›  ") + Localization.Get("ShowSavedZones") + " [" + lp + "1]", () => { engine.WpfExpandZones(); filterIdentity = null; Refresh(); });
            customExpand.Tag = "custom-zones:expand";
            customExpand.HorizontalContentAlignment = HorizontalAlignment.Left;
            customExpand.Padding = new Thickness(0, 5, 0, 5);
            customExpand.Margin = new Thickness(0);
            customExpand.MinHeight = 32;
            customExpand.FontSize = 13;
            customExpand.Background = Brushes.Transparent;
            customExpand.BorderThickness = new Thickness(0);
            customRow.Children.Add(customExpand);
            layersPanel.Children.Add(customRow);
            if (state.CustomZonesExpanded && state.CustomZones != null && state.CustomZones.Length > 0)
                layersPanel.Children.Add(BuildVirtualizedCustomZonesList(state.CustomZones));
            AddLayer(layersPanel, "waypoints", Localization.Get("ShowCustomWaypoints") + " [" + lp + "2]", state.Waypoints);
            AddLayer(layersPanel, "grid", Localization.Get("GridBorders") + " [" + lp + "3]", state.Grid);
            AddLayer(layersPanel, "history", Localization.Get("HistoryEnabled") + " [" + lp + "4]", state.History);
            AddLayer(layersPanel, "labels", Localization.Get("ShowZoneLabels") + " [" + lp + "5]", state.Labels);
            var layersCard = SidebarGlassCard(layersPanel, new Thickness(12, 8, 12, 8));
            layersCard.Margin = new Thickness(0, 0, 0, 8);
            sidebar.Children.Add(layersCard);
            bool poiUnlocked = PoiAccessGuard.IsUnlocked;
            if (state.Store != null && !poiUnlocked && state.Store.MasterEnabled)
                state.Store.MasterEnabled = false;
            if (!poiUnlocked) {
                bool ruLock = Localization.Current == AppLanguage.Russian;
                var lockPanel = new StackPanel();
                lockPanel.Children.Add(WpfFieldTheme.Text(Localization.Get("SidebarCategories"), 11, WpfFieldTheme.Muted, true));
                var lockHint = WpfFieldTheme.Text(ruLock ? "Доступ к категориям точек (POI) и Zilore AI открывается по коду доступа." : "Access to POI categories and Zilore AI requires an access code.", 12.5, WpfFieldTheme.Muted);
                lockHint.TextWrapping = TextWrapping.Wrap;
                lockHint.Margin = new Thickness(0, 6, 0, 6);
                lockPanel.Children.Add(lockHint);
                var lockDonate = WpfFieldTheme.Text(ruLock ? "★ Получить код доступа можно через донат." : "★ You can get an access code via donation.", 12.5, WpfFieldTheme.Amber, true);
                lockDonate.TextWrapping = TextWrapping.Wrap;
                lockDonate.Margin = new Thickness(0, 0, 0, 10);
                lockPanel.Children.Add(lockDonate);
                var unlockBtn = WpfFieldTheme.Button(ruLock ? "🔑  Ввести код доступа" : "🔑  Enter access code", () => {
                    if (PromptPoiAccessCode()) {
                        if (state.Store != null)
                            state.Store.MasterEnabled = true;
                        filterIdentity = null;
                        Refresh();
                    }
                }, true);
                unlockBtn.MinHeight = 36;
                unlockBtn.FontSize = 13.5;
                lockPanel.Children.Add(unlockBtn);
                var donateBtn = WpfFieldTheme.Button(ruLock ? "★  О донате и возможностях" : "★  Donation & Perks", ShowDonateAccessDialog);
                donateBtn.MinHeight = 32;
                donateBtn.FontSize = 12.5;
                donateBtn.Margin = new Thickness(0, 6, 0, 0);
                lockPanel.Children.Add(donateBtn);
                var lockCard = SidebarGlassCard(lockPanel, new Thickness(12, 10, 12, 12), true);
                lockCard.Margin = new Thickness(0, 12, 0, 6);
                sidebar.Children.Add(lockCard);
                sidebar.Children.Add(BuildSidebarTelemetryCard(state, ruSb));
                return;
            }
            string poiTitleText = Localization.Get("SidebarCategories") + " · " + PoiAccessGuard.FormatRemaining(Localization.Current == AppLanguage.Russian);
            sidebar.Children.Add(SidebarSectionTitle(poiTitleText, new Thickness(2, 12, 0, 7)));
            var poiBulkGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 10) };
            var all = WpfFieldTheme.Button(Localization.Get("SelectAll"), () => FilterAll(true));
            var off = WpfFieldTheme.Button(Localization.Get("DeselectAll"), () => FilterAll(false));
            all.MinHeight = off.MinHeight = 32;
            all.FontSize = 12.5;
            off.FontSize = 12.5;
            all.Padding = off.Padding = new Thickness(8, 4, 8, 4);
            all.Margin = new Thickness(0, 0, 3, 0);
            off.Margin = new Thickness(3, 0, 0, 0);
            poiBulkGrid.Children.Add(all);
            poiBulkGrid.Children.Add(off);
            sidebar.Children.Add(poiBulkGrid);
            poiWarnCard = null;
            poiWarnText = null;
            if (state.Store != null) {
                poiWarnText = WpfFieldTheme.Text("", 12.5, WpfFieldTheme.Amber, true);
                poiWarnText.TextWrapping = TextWrapping.Wrap;
                poiWarnText.TextTrimming = TextTrimming.None;
                poiWarnCard = SidebarGlassCard(poiWarnText, new Thickness(10, 8, 10, 8), true);
                poiWarnCard.Margin = new Thickness(0, 0, 0, 10);
                sidebar.Children.Add(poiWarnCard);
                UpdatePoiWarnBanner(state.Store);

                poiSectionContainers.Clear();
                poiSectionPanels.Clear();
                poiSectionTitles.Clear();
                poiSectionPopulated.Clear();
                poiCategoryMeta.Clear();
                var poiFilterWrap = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                var poiFilterBox = new TextBox { Text = poiSearchQuery, Height = 34, Padding = new Thickness(10, 5, 28, 5), FontSize = 13, ToolTip = WpfFieldTheme.UiText("PoiFilterPlaceholder") };
                var poiFilterHint = WpfFieldTheme.Text("⌕  " + WpfFieldTheme.UiText("PoiFilterPlaceholder"), 12.5, WpfFieldTheme.Muted);
                poiFilterHint.Margin = new Thickness(11, 0, 28, 0);
                poiFilterHint.VerticalAlignment = VerticalAlignment.Center;
                poiFilterHint.IsHitTestVisible = false;
                poiFilterHint.Visibility = string.IsNullOrEmpty(poiSearchQuery) ? Visibility.Visible : Visibility.Collapsed;
                var poiFilterClear = new Button { Content = "✕", Width = 24, Height = 24, MinHeight = 24, Padding = new Thickness(0), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = WpfFieldTheme.Muted, Visibility = string.IsNullOrEmpty(poiSearchQuery) ? Visibility.Collapsed : Visibility.Visible };
                var activeStore = state.Store;
                poiFilterBox.LostKeyboardFocus += (s, e) => { if (TacticalVisible && dialogs.Count == 0) RestoreGameplayFocus(); };
                poiFilterClear.Click += (s, e) => { poiFilterBox.Text = ""; poiFilterBox.Focus(); };
                poiFilterBox.TextChanged += (s, e) => {
                    bool empty = string.IsNullOrEmpty(poiFilterBox.Text);
                    poiFilterHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
                    poiFilterClear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
                    if (rebuilding)
                        return;
                    poiSearchQuery = poiFilterBox.Text ?? "";
                    SchedulePoiSearchFilter(activeStore);
                };
                poiFilterWrap.Children.Add(poiFilterBox);
                poiFilterWrap.Children.Add(poiFilterHint);
                poiFilterWrap.Children.Add(poiFilterClear);
                sidebar.Children.Add(poiFilterWrap);

                string q = (poiSearchQuery ?? "").Trim();
                foreach (string section in activeStore.Sections) {
                    var categories = activeStore.CategoriesBySection[section];
                    int enabled = 0;
                    for (int ci = 0; ci < categories.Count; ci++)
                    if (activeStore.IsCategoryEnabled(categories[ci].Id))
                        enabled++;
                    bool open = q.Length > 0 || expanded.Contains(section);
                    string secLabel = Localization.GetSectionName(section);

                    var sectionWrap = new StackPanel();
                    var title = new Grid();
                    title.ColumnDefinitions.Add(new ColumnDefinition());
                    title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var secTitleTxt = WpfFieldTheme.Text((open ? "⌄  " : "›  ") + secLabel, 16, null, true);
                    title.Children.Add(secTitleTxt);
                    var count = WpfFieldTheme.Text(enabled + "/" + categories.Count, 11, WpfFieldTheme.Muted, true);
                    var countBadge = WpfFieldTheme.Badge(count, new Thickness(6, 1, 6, 1));
                    Grid.SetColumn(countBadge, 1);
                    title.Children.Add(countBadge);
                    var button = new Button { Content = title, HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, Foreground = WpfFieldTheme.Ink, Padding = new Thickness(0, 8, 0, 8), BorderThickness = new Thickness(0) };
                    string captured = section;
                    button.Tag = "poi-expand:" + section;
                    var groupRow = new DockPanel();
                    var groupToggle = new CheckBox { IsThreeState = true, IsChecked = enabled == 0 ? (bool?)false : enabled == categories.Count ? (bool?)true : null, Width = 42, Margin = new Thickness(8, 0, 0, 0), ToolTip = secLabel, Tag = "poi-section:" + section };
                    poiSectionControls[section] = groupToggle;
                    poiSectionCounts[section] = count;
                    poiSectionTitles[section] = secTitleTxt;
                    System.Windows.Automation.AutomationProperties.SetName(groupToggle, secLabel);
                    groupToggle.Click += (s, e) => FilterSection(captured, !categories.All(c => activeStore.IsCategoryEnabled(c.Id)));
                    DockPanel.SetDock(groupToggle, Dock.Right);
                    groupRow.Children.Add(groupToggle);
                    groupRow.Children.Add(button);
                    sectionWrap.Children.Add(groupRow);

                    var catPanel = new StackPanel { Visibility = open ? Visibility.Visible : Visibility.Collapsed };
                    poiSectionPanels[section] = catPanel;
                    poiSectionContainers[section] = sectionWrap;
                    if (open)
                        EnsurePoiSectionPopulated(activeStore, section);

                    button.Click += (s, e) => {
                        bool nowOpen;
                        if (expanded.Remove(captured))
                            nowOpen = false;
                        else {
                            expanded.Add(captured);
                            nowOpen = true;
                        }
                        if (nowOpen)
                            EnsurePoiSectionPopulated(activeStore, captured);
                        catPanel.Visibility = nowOpen ? Visibility.Visible : Visibility.Collapsed;
                        secTitleTxt.Text = (nowOpen ? "⌄  " : "›  ") + Localization.GetSectionName(captured);
                        if (!string.IsNullOrWhiteSpace(poiSearchQuery))
                            ApplyPoiSearchFilterInPlace(activeStore);
                    };

                    sectionWrap.Children.Add(catPanel);
                    var secDiv = new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6) };
                    secDiv.SetResourceReference(Border.BackgroundProperty, "ThemeLine");
                    sectionWrap.Children.Add(secDiv);
                    sidebar.Children.Add(sectionWrap);
                }
                if (q.Length > 0)
                    ApplyPoiSearchFilterInPlace(activeStore);
            }
            sidebar.Children.Add(BuildSidebarTelemetryCard(state, ruSb));
        }
        void AddLayer(Panel container, string key, string label, bool value) {
            var lbl = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, FontSize = 13.5 };
            var box = new CheckBox { Content = lbl, IsChecked = value, Margin = new Thickness(0, 4, 0, 4) };
            box.Checked += (s, e) => { if (!rebuilding) { engine.WpfLayer(key, true); SchedulePoiFilterRefresh(); } };
            box.Unchecked += (s, e) => { if (!rebuilding) { engine.WpfLayer(key, false); SchedulePoiFilterRefresh(); } };
            container.Children.Add(box);
        }
        readonly Dictionary<string, StackPanel> poiSectionContainers = new Dictionary<string, StackPanel>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, StackPanel> poiSectionPanels = new Dictionary<string, StackPanel>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, TextBlock> poiSectionTitles = new Dictionary<string, TextBlock>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> poiSectionPopulated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<int, Tuple<string, string>> poiCategoryMeta = new Dictionary<int, Tuple<string, string>>();
        Border poiWarnCard;
        TextBlock poiWarnText;
        void UpdatePoiWarnBanner(ScumMapStore store) {
            if (poiWarnCard == null || poiWarnText == null || store == null)
                return;
            int totalEnabled = 0;
            foreach (var list in store.CategoriesBySection.Values) {
                for (int i = 0; i < list.Count; i++)
                if (store.IsCategoryEnabled(list[i].Id)) {
                    totalEnabled++;
                    break;
                }
                if (totalEnabled > 0)
                    break;
            }
            bool show = !store.MasterEnabled || totalEnabled == 0;
            var warnVis = show ? Visibility.Visible : Visibility.Collapsed;
            if (poiWarnCard.Visibility != warnVis) poiWarnCard.Visibility = warnVis;
            if (show) {
                string warnMsg = WpfFieldTheme.UiText(!store.MasterEnabled ? "PoiHiddenMasterOff" : "PoiHiddenNoneSelected");
                if (poiWarnText.Text != warnMsg) poiWarnText.Text = warnMsg;
            }
        }
        void EnsurePoiSectionPopulated(ScumMapStore store, string section) {
            if (store == null || poiSectionPopulated.Contains(section))
                return;
            StackPanel panel;
            if (!poiSectionPanels.TryGetValue(section, out panel))
                return;
            poiSectionPopulated.Add(section);
            var categories = store.CategoriesBySection[section];
            var sorted = new List<ScumMapCategory>(categories);
            sorted.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            bool prevRebuild = rebuilding;
            rebuilding = true;
            try {
                for (int i = 0; i < sorted.Count; i++) {
                    var category = sorted[i];
                    string catTitle = Localization.GetCategoryName(category.Name);
                    var catDock = new DockPanel { LastChildFill = true };
                    var cntTxt = WpfFieldTheme.Text(category.MarkerCount.ToString(CultureInfo.InvariantCulture), 11, WpfFieldTheme.Muted, true);
                    cntTxt.Margin = new Thickness(6, 0, 4, 0);
                    cntTxt.VerticalAlignment = VerticalAlignment.Center;
                    DockPanel.SetDock(cntTxt, Dock.Right);
                    catDock.Children.Add(cntTxt);
                    var nameTxt = new TextBlock { Text = catTitle, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 13.5 };
                    catDock.Children.Add(nameTxt);
                    var row = new CheckBox { Content = catDock, ToolTip = catTitle + " (" + category.MarkerCount + ")", IsChecked = store.IsCategoryEnabled(category.Id), Margin = new Thickness(14, 2, 0, 2), Tag = "poi-category:" + category.Id };
                    int id = category.Id;
                    poiCategoryControls[id] = row;
                    poiCategoryMeta[id] = Tuple.Create(catTitle, category.Name ?? "");
                    row.Checked += (s, e) => Category(id, true);
                    row.Unchecked += (s, e) => Category(id, false);
                    panel.Children.Add(row);
                }
            } finally { rebuilding = prevRebuild; }
        }
        void ApplyPoiSearchFilterInPlace(ScumMapStore store) {
            if (store == null)
                return;
            string q = (poiSearchQuery ?? "").Trim();
            foreach (string section in store.Sections) {
                StackPanel container, panel;
                if (!poiSectionContainers.TryGetValue(section, out container) || !poiSectionPanels.TryGetValue(section, out panel))
                    continue;
                TextBlock secTitle;
                poiSectionTitles.TryGetValue(section, out secTitle);
                var categories = store.CategoriesBySection[section];
                if (q.Length == 0) {
                    if (container.Visibility != Visibility.Visible) container.Visibility = Visibility.Visible;
                    bool open = expanded.Contains(section);
                    var pVis = open ? Visibility.Visible : Visibility.Collapsed;
                    if (panel.Visibility != pVis) panel.Visibility = pVis;
                    if (secTitle != null) {
                        string titleTxt = (open ? "⌄  " : "›  ") + Localization.GetSectionName(section);
                        if (secTitle.Text != titleTxt) secTitle.Text = titleTxt;
                    }
                    if (poiSectionPopulated.Contains(section)) {
                        for (int i = 0; i < categories.Count; i++) {
                            CheckBox cb;
                            if (poiCategoryControls.TryGetValue(categories[i].Id, out cb) && cb.Visibility != Visibility.Visible)
                                cb.Visibility = Visibility.Visible;
                        }
                    }
                    continue;
                }
                bool anyMatch = false;
                for (int i = 0; i < categories.Count; i++) {
                    var c = categories[i];
                    string loc = Localization.GetCategoryName(c.Name);
                    if (loc.Contains(q, StringComparison.OrdinalIgnoreCase) || (c.Name != null && c.Name.Contains(q, StringComparison.OrdinalIgnoreCase))) {
                        anyMatch = true;
                        break;
                    }
                }
                if (!anyMatch) {
                    if (container.Visibility != Visibility.Collapsed) container.Visibility = Visibility.Collapsed;
                    continue;
                }
                if (container.Visibility != Visibility.Visible) container.Visibility = Visibility.Visible;
                EnsurePoiSectionPopulated(store, section);
                if (panel.Visibility != Visibility.Visible) panel.Visibility = Visibility.Visible;
                if (secTitle != null) {
                    string openTxt = "⌄  " + Localization.GetSectionName(section);
                    if (secTitle.Text != openTxt) secTitle.Text = openTxt;
                }
                for (int i = 0; i < categories.Count; i++) {
                    var c = categories[i];
                    CheckBox cb;
                    if (!poiCategoryControls.TryGetValue(c.Id, out cb))
                        continue;
                    Tuple<string, string> meta;
                    bool matched = poiCategoryMeta.TryGetValue(c.Id, out meta) && (meta.Item1.Contains(q, StringComparison.OrdinalIgnoreCase) || meta.Item2.Contains(q, StringComparison.OrdinalIgnoreCase));
                    var cbVis = matched ? Visibility.Visible : Visibility.Collapsed;
                    if (cb.Visibility != cbVis) cb.Visibility = cbVis;
                }
            }
        }
        DispatcherTimer poiSearchDebounce;
        ScumMapStore pendingSearchStore;
        void SchedulePoiSearchFilter(ScumMapStore store) {
            pendingSearchStore = store;
            if (diagnostic) {
                ApplyPoiSearchFilterInPlace(store);
                return;
            }
            if (poiSearchDebounce == null) {
                poiSearchDebounce = new DispatcherTimer(DispatcherPriority.Background, application.Dispatcher) { Interval = TimeSpan.FromMilliseconds(120) };
                poiSearchDebounce.Tick += (s, e) => {
                    poiSearchDebounce.Stop();
                    if (!disposed && pendingSearchStore != null)
                        ApplyPoiSearchFilterInPlace(pendingSearchStore);
                };
            }
            poiSearchDebounce.Stop();
            poiSearchDebounce.Start();
        }
        DispatcherTimer poiFilterDebounce;
        void SchedulePoiFilterRefresh() {
            if (diagnostic) {
                RefreshInteraction();
                return;
            }
            if (poiFilterDebounce == null) {
                poiFilterDebounce = new DispatcherTimer(DispatcherPriority.Background, application.Dispatcher) { Interval = TimeSpan.FromMilliseconds(90) };
                poiFilterDebounce.Tick += (s, e) => {
                    poiFilterDebounce.Stop();
                    if (!disposed)
                        RefreshInteraction();
                };
            }
            poiFilterDebounce.Stop();
            poiFilterDebounce.Start();
        }
        void Category(int id, bool enabled) {
            if (rebuilding)
                return;
            var store = engine.WpfSnapshot(true).Store;
            if (store == null)
                return;
            store.SetCategoryEnabled(id, enabled);
            engine.WpfScheduleSave();
            SyncPoiControls(store);
            SchedulePoiFilterRefresh();
        }
        void FilterAll(bool enabled) {
            var store = engine.WpfSnapshot(true).Store;
            if (store == null)
                return;
            store.SetAllEnabled(enabled);
            engine.WpfScheduleSave();
            SyncPoiControls(store);
            SchedulePoiFilterRefresh();
        }
        void FilterSection(string section, bool enabled) {
            var store = engine.WpfSnapshot(true).Store;
            if (store == null || !store.CategoriesBySection.ContainsKey(section))
                return;
            store.SetSectionEnabled(section, enabled);
            engine.WpfScheduleSave();
            SyncPoiControls(store);
            SchedulePoiFilterRefresh();
        }
    }
}
