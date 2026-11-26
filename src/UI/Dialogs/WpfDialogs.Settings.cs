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
    internal sealed partial class WpfFieldConsoleRuntime {
        void ShowSettings() {
            if (settingsWindow != null) {
                if (settingsWindow.WindowState == WindowState.Minimized) {
                    settingsWindow.WindowState = WindowState.Normal;
                    FocusInteractive(settingsWindow);
                    return;
                }
                if (settingsToggleOpen)
                    settingsWindow.Close();
                else if (dialogs.Count > 0)
                    FocusInteractive(dialogs[dialogs.Count - 1]);
                return;
            }
            settingsWindow = BuildSettings(0);
            try {
                Modal(settingsWindow);
            } finally { settingsWindow = null; settingsToggleOpen = false; }
        }
        bool ValidateNumericFields(Window window) {
            var fields = LogicalSettingsChildren(window).OfType<TextBox>().Where(t => t.Resources.Contains("commit-number")).ToArray();
            foreach (var field in fields)
                ((Action)field.Resources["commit-number"])();
            var invalid = fields.FirstOrDefault(t => !string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetItemStatus(t)));
            if (invalid == null)
                return true;
            for (var parent = LogicalTreeHelper.GetParent(invalid); parent != null; parent = LogicalTreeHelper.GetParent(parent)) {
                var expander = parent as Expander;
                if (expander != null)
                    expander.IsExpanded = true;
            }
            invalid.Focus();
            invalid.BringIntoView();
            return false;
        }
        static IEnumerable<DependencyObject> LogicalSettingsChildren(DependencyObject parent) {
            foreach (object child in LogicalTreeHelper.GetChildren(parent)) {
                var element = child as DependencyObject;
                if (element == null)
                    continue;
                yield return element;
                foreach (var descendant in LogicalSettingsChildren(element))
                    yield return descendant;
            }
        }
        Border WpfCheck(string text, bool initial, Action<bool> onChanged, string tag = null, string subtitle = null, bool accentGlow = false) {
            var box = new CheckBox { Content = "", IsChecked = initial, MinHeight = 30, Width = 48, Padding = new Thickness(0), Margin = new Thickness(0), Tag = tag };
            System.Windows.Automation.AutomationProperties.SetName(box, text);
            box.Checked += (s, e) => { onChanged(true); Refresh(); };
            box.Unchecked += (s, e) => { onChanged(false); Refresh(); };
            return WpfFieldTheme.SettingRow(text, subtitle, box, accentGlow);
        }
        Border WpfNumber(string labelText, int min, int max, int initial, int step, Action<int> onChanged, Action<Action<int>> bindExternalSync = null) {
            string expectedRange = min.ToString(CultureInfo.CurrentCulture) + " – " + max.ToString(CultureInfo.CurrentCulture);
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            int current = Math.Max(min, Math.Min(max, initial));
            var input = new TextBox { Text = current.ToString(CultureInfo.CurrentCulture), MinWidth = 72, MaxWidth = 96, MinHeight = 34, FontSize = 14, Padding = new Thickness(8, 4, 8, 4), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), Tag = "setting-number" };
            var range = WpfFieldTheme.Text(expectedRange, 12, WpfFieldTheme.Muted);
            range.Tag = "setting-range";
            System.Windows.Automation.AutomationProperties.SetName(input, labelText);
            System.Windows.Automation.AutomationProperties.SetHelpText(input, expectedRange);
            Action<int> setValue = val => {
                current = Math.Max(min, Math.Min(max, val));
                range.Text = expectedRange;
                range.SetResourceReference(TextBlock.ForegroundProperty, "ThemeMuted");
                input.SetResourceReference(Control.BorderBrushProperty, "ThemeLine");
                System.Windows.Automation.AutomationProperties.SetItemStatus(input, "");
                input.Text = current.ToString(CultureInfo.CurrentCulture);
            };
            if (bindExternalSync != null)
                bindExternalSync(setValue);
            Action commit = () => {
                int parsed;
                if (int.TryParse(input.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed) && parsed >= min && parsed <= max) {
                    setValue(parsed);
                    onChanged(parsed);
                    Refresh();
                } else {
                    input.BorderBrush = Brushes.OrangeRed;
                    range.Foreground = Brushes.OrangeRed;
                    range.Text = Localization.Get("InvalidNumber") + ": " + expectedRange;
                    System.Windows.Automation.AutomationProperties.SetItemStatus(input, range.Text);
                }
            };
            input.Resources["commit-number"] = commit;
            var less = WpfFieldTheme.Button("−", () => { setValue(current - step); onChanged(current); Refresh(); });
            var more = WpfFieldTheme.Button("+", () => { setValue(current + step); onChanged(current); Refresh(); });
            less.MinWidth = more.MinWidth = 34;
            less.MinHeight = more.MinHeight = 34;
            less.Padding = more.Padding = new Thickness(0);
            less.Margin = more.Margin = new Thickness(0);
            input.LostKeyboardFocus += (s, e) => commit();
            input.PreviewKeyDown += (s, e) => {
                if (e.Key == Key.Enter) {
                    commit();
                    e.Handled = true;
                } else if (e.Key == Key.Up || e.Key == Key.Down) {
                    setValue(current + (e.Key == Key.Up ? step : -step));
                    onChanged(current);
                    Refresh();
                    e.Handled = true;
                }
            };
            right.Children.Add(less);
            right.Children.Add(input);
            right.Children.Add(more);
            var card = WpfFieldTheme.SettingRow(labelText, expectedRange, right);
            var grid = card.Child as Grid;
            var left = grid != null && grid.Children.Count > 0 ? grid.Children[0] as StackPanel : null;
            if (left != null && left.Children.Count > 1) {
                left.Children.RemoveAt(1);
                left.Children.Insert(1, range);
            }
            return card;
        }
        Border WpfColorSwatch(string labelText, Func<System.Drawing.Color> getColor, Action pickColor) {
            Button btn = null;
            Action paint = () => {
                var c = getColor();
                var sb = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
                sb.Freeze();
                btn.Background = sb;
                btn.ToolTip = System.Drawing.ColorTranslator.ToHtml(c);
            };
            btn = WpfFieldTheme.Button("   ", () => { pickColor(); paint(); Refresh(); });
            btn.MinWidth = 76;
            btn.MinHeight = 32;
            btn.Padding = new Thickness(12, 4, 12, 4);
            btn.Margin = new Thickness(0);
            paint();
            return WpfFieldTheme.SettingRow(labelText, null, btn);
        }
        Window BuildSettings(int initial) {
            var window = Window(Localization.Get("Settings"), 1040, 790);
            window.WindowStyle = WindowStyle.None;
            window.AllowsTransparency = true;
            window.Background = Brushes.Transparent;
            var shell = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), SnapsToDevicePixels = true };
            shell.SetResourceReference(Border.BackgroundProperty, "ThemeGround");
            shell.SetResourceReference(Border.BorderBrushProperty, "ThemeShellBorder");
            var outerDock = new DockPanel();
            var titleBar = WpfFieldTheme.BuildChromeTitleBar(window, "ZiloreMiniMap — " + Localization.Get("Settings"));
            DockPanel.SetDock(titleBar, Dock.Top);
            outerDock.Children.Add(titleBar);
            var root = new DockPanel { Margin = new Thickness(20, 14, 20, 16) };
            outerDock.Children.Add(root);
            shell.Child = outerDock;
            var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var version = WpfFieldTheme.Text("", 12, WpfFieldTheme.Muted, true);
            var verPill = WpfFieldTheme.Badge(version, new Thickness(10, 4, 10, 4));
            verPill.VerticalAlignment = VerticalAlignment.Center;
            verPill.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(verPill, Dock.Right);
            heading.Children.Add(verPill);
            var searchWrap = new Grid { Width = 310, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            var settingsSearch = new TextBox { Height = 34, Padding = new Thickness(10, 5, 28, 5), FontSize = 13, Tag = "settings-search", ToolTip = WpfFieldTheme.UiText("SettingsSearchPlaceholder") };
            var searchHint = WpfFieldTheme.Text("⌕  " + WpfFieldTheme.UiText("SettingsSearchPlaceholder"), 12, WpfFieldTheme.Muted);
            searchHint.Margin = new Thickness(11, 0, 28, 0);
            searchHint.VerticalAlignment = VerticalAlignment.Center;
            searchHint.IsHitTestVisible = false;
            var searchClear = new Button { Content = "✕", Width = 24, Height = 24, MinHeight = 24, Padding = new Thickness(0), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = WpfFieldTheme.Muted, Visibility = Visibility.Collapsed };
            searchClear.Click += (s, e) => { settingsSearch.Text = ""; settingsSearch.Focus(); };
            searchWrap.Children.Add(settingsSearch);
            searchWrap.Children.Add(searchHint);
            searchWrap.Children.Add(searchClear);
            DockPanel.SetDock(searchWrap, Dock.Right);
            heading.Children.Add(searchWrap);
            var headingTitle = WpfFieldTheme.Text(Localization.Get("Settings"), 24, null, true);
            headingTitle.VerticalAlignment = VerticalAlignment.Center;
            heading.Children.Add(headingTitle);
            DockPanel.SetDock(heading, Dock.Top);
            root.Children.Add(heading);
            var searchResultsPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(searchResultsPanel, Dock.Top);
            root.Children.Add(searchResultsPanel);
            var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0), Tag = "settings-footer" };
            var done = WpfFieldTheme.Button(Localization.Get("Done"), () => { if (!ValidateNumericFields(window)) return; if (engine.WpfSave()) window.Close(); else ShowFluentNotice(window, Localization.Get("LocalSaveFailed"), Localization.Get("Settings"), true); }, true);
            done.Tag = "text:Done";
            done.MinWidth = 130;
            done.MinHeight = 36;
            DockPanel.SetDock(done, Dock.Right);
            footer.Children.Add(done);
            var guideQuickBtn = WpfFieldTheme.Button(WpfFieldTheme.UiText("HotkeysGuideBtn"), () => ShowHotkeysAndRoutesGuide(0));
            guideQuickBtn.MinHeight = 36;
            guideQuickBtn.Padding = new Thickness(12, 5, 12, 5);
            guideQuickBtn.FontSize = 13;
            guideQuickBtn.HorizontalAlignment = HorizontalAlignment.Left;
            footer.Children.Add(guideQuickBtn);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);
            var workspace = new Grid();
            workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(188) });
            workspace.ColumnDefinitions.Add(new ColumnDefinition());
            root.Children.Add(workspace);
            var nav = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Tag = "settings-categories", Padding = new Thickness(4) };
            System.Windows.Automation.AutomationProperties.SetName(nav, Localization.Get("Settings"));
            var navDock = new DockPanel();
            var grandSettImg = WpfFieldTheme.LoadEmbeddedImage("grand-banner.png");
            if (grandSettImg != null) {
                var grandBanner = new System.Windows.Controls.Image { Source = grandSettImg, Height = 24, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(grandBanner, BitmapScalingMode.HighQuality);
                var grandPill = new Border { Background = WpfFieldTheme.ColorBrush("#0F172A"), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(6, 6, 6, 10), Child = grandBanner };
                DockPanel.SetDock(grandPill, Dock.Top);
                navDock.Children.Add(grandPill);
            }
            navDock.Children.Add(nav);
            var navCard = WpfFieldTheme.Card(navDock, new Thickness(4, 6, 4, 6));
            workspace.Children.Add(navCard);
            var body = new StackPanel { Margin = new Thickness(20, 0, 16, 12) };
            var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetColumn(scroll, 1);
            workspace.Children.Add(scroll);
            int[] order = { 0, 2, 3, 1, 4, 5, 6 };
            string[] keys = { "FieldGeneral", "FieldShortcuts", "FieldAppearance", "SettingsTracking", "SettingsGroups", "SettingsTools", "SettingsLogs" };
            var items = new Dictionary<int, ListBoxItem>();
            foreach (int index in order) {
                var rail = new DockPanel { LastChildFill = true };
                var icTxt = WpfFieldTheme.Text("", 16, WpfFieldTheme.Muted, false);
                icTxt.Width = 24;
                icTxt.TextAlignment = TextAlignment.Center;
                icTxt.VerticalAlignment = VerticalAlignment.Center;
                icTxt.Margin = new Thickness(0, 0, 8, 0);
                DockPanel.SetDock(icTxt, Dock.Left);
                rail.Children.Add(icTxt);
                var lbTxt = WpfFieldTheme.Text("", 13, WpfFieldTheme.Muted, true);
                lbTxt.TextWrapping = TextWrapping.Wrap;
                lbTxt.VerticalAlignment = VerticalAlignment.Center;
                rail.Children.Add(lbTxt);
                var item = new ListBoxItem { Content = rail, Padding = new Thickness(6, 9, 10, 9), Margin = new Thickness(0, 2, 0, 2), Tag = "settings-category:" + index };
                items.Add(index, item);
                nav.Items.Add(item);
            }
            int currentPage = -1, settingsRevision = -1;
            bool selecting = false;
            var expanded = new Dictionary<string, bool>();
            Action<int> page = null;
            DispatcherTimer logTimer = null;
            Action<string, StackPanel, bool> section = (key, panel, open) => {
                if (panel.Children.Count == 0)
                    return;
                string secText = (key == "VoiceHeading" ? WpfFieldTheme.UiText("VoiceHeadingClean") : Localization.Get(key)).Replace("&&", "&");
                string curTabTitle = currentPage >= 0 ? (currentPage == 6 ? WpfFieldTheme.UiText("SettingsLogs") : Localization.Get(keys[currentPage])) : "";
                if (!string.Equals(secText, curTabTitle, StringComparison.OrdinalIgnoreCase)) {
                    var subHdr = WpfFieldTheme.Text(secText.ToUpper(CultureInfo.CurrentCulture), 11.5, WpfFieldTheme.Muted, true);
                    subHdr.Margin = new Thickness(4, 12, 0, 6);
                    subHdr.Tag = "settings-section:" + key;
                    body.Children.Add(subHdr);
                }
                body.Children.Add(panel);
            };
            page = index => {
                if (!items.ContainsKey(index))
                    index = 0;
                if (currentPage >= 0 && index != currentPage && !ValidateNumericFields(window)) {
                    selecting = true;
                    nav.SelectedItem = items[currentPage];
                    selecting = false;
                    return;
                }
                currentPage = index;
                settingsRevision = engine.WpfSettingsRevision;
                settingsPreview = null;
                window.Title = Localization.Get("Settings");
                headingTitle.Text = window.Title;
                guideQuickBtn.Content = WpfFieldTheme.UiText("HotkeysGuideBtn");
                searchHint.Text = "⌕  " + WpfFieldTheme.UiText("SettingsSearchPlaceholder");
                version.Text = MapWindow.DisplayVersion + (Program.IsTestBuild ? " " + Localization.Get("WpfTestBuild") : "");
                Func<int, string> navTitle = k => k == 6 ? WpfFieldTheme.UiText("SettingsLogs") : Localization.Get(keys[k]);
                string[] navIcons = { "⚙", "⌨", "🎨", "🎯", "👥", "🛠", "📋" };
                foreach (var pair in items) {
                    string t = navTitle(pair.Key);
                    string ic = pair.Key >= 0 && pair.Key < navIcons.Length ? navIcons[pair.Key] : "";
                    var rail = (DockPanel)pair.Value.Content;
                    var icTb = (TextBlock)rail.Children[0];
                    var lbTb = (TextBlock)rail.Children[1];
                    icTb.Text = ic;
                    lbTb.Text = t;
                    bool activeTab = pair.Key == index;
                    icTb.SetResourceReference(TextBlock.ForegroundProperty, activeTab ? "ThemeAmber" : "ThemeMuted");
                    lbTb.SetResourceReference(TextBlock.ForegroundProperty, activeTab ? "ThemeInk" : "ThemeMuted");
                    System.Windows.Automation.AutomationProperties.SetName(pair.Value, t);
                }
                selecting = true;
                nav.SelectedItem = items[index];
                selecting = false;
                done.Content = Localization.Get("Done");
                body.Children.Clear();
                scroll.ScrollToTop();
                var state = engine.WpfSnapshot(true);

                if (index == 0) {
                    // Pure WPF General Tab
                    var general = new StackPanel();
                    var langCombo = new ComboBox { ItemsSource = Localization.LanguageDisplayNames, SelectedIndex = Enum.IsDefined<AppLanguage>(Localization.Current) ? (int)Localization.Current : 0, MinWidth = 220 };
                    langCombo.SelectionChanged += (s, e) => {
                        AppLanguage nextLang = Localization.LanguageAt(langCombo.SelectedIndex);
                        if (nextLang != Localization.Current) {
                            engine.WpfSetLanguage(nextLang);
                            page(currentPage);
                            Refresh();
                        }
                    };
                    general.Children.Add(WpfFieldTheme.SettingRow(Localization.Get("Language"), Localization.Current == AppLanguage.Russian ? "Язык интерфейса и локаций" : "Interface & location language", langCombo, true));
                    var guide = WpfFieldTheme.Button(Localization.Current == AppLanguage.Russian ? "Открыть" : "Open", () => { window.Close(); application.Dispatcher.BeginInvoke(new Action(() => ShowSetupWindow(false))); });
                    guide.Tag = "first-time-guide";
                    guide.MinHeight = 34;
                    guide.MinWidth = 110;
                    guide.Padding = new Thickness(14, 6, 14, 6);
                    guide.FontSize = 13.5;
                    guide.Margin = new Thickness(0);
                    general.Children.Add(WpfFieldTheme.SettingRow(Localization.Get("FirstTimeGuideTitle"), Localization.Current == AppLanguage.Russian ? "Пошаговая настройка клавиш, оверлея и отслеживания" : "Step-by-step setup for keys, overlay, and tracking", guide));
                    var fullGuideBtn = WpfFieldTheme.Button(Localization.Current == AppLanguage.Russian ? "Открыть" : "Open", () => ShowUserGuide(0));
                    fullGuideBtn.Tag = "user-guide";
                    fullGuideBtn.MinHeight = 34;
                    fullGuideBtn.MinWidth = 110;
                    fullGuideBtn.Padding = new Thickness(14, 6, 14, 6);
                    fullGuideBtn.FontSize = 13.5;
                    fullGuideBtn.Margin = new Thickness(0);
                    general.Children.Add(WpfFieldTheme.SettingRow(Localization.Current == AppLanguage.Russian ? "Руководство пользователя" : "User Guide", Localization.Current == AppLanguage.Russian ? "Подробная документация по функциям, маршрутам, отряду и клавишам (Shift+F1)" : "Complete manual for features, routes, squad radar, and hotkeys (Shift+F1)", fullGuideBtn));
                    var eulaBtn = WpfFieldTheme.Button(Localization.Current == AppLanguage.Russian ? "Открыть" : "View", () => ShowEulaViewer());
                    eulaBtn.Tag = "view-eula";
                    eulaBtn.MinHeight = 34;
                    eulaBtn.MinWidth = 110;
                    eulaBtn.Padding = new Thickness(14, 6, 14, 6);
                    eulaBtn.FontSize = 13.5;
                    eulaBtn.Margin = new Thickness(0);
                    general.Children.Add(WpfFieldTheme.SettingRow(Localization.Current == AppLanguage.Russian ? "Лицензионное соглашение (EULA)" : "License Agreement (EULA)", Localization.Current == AppLanguage.Russian ? ("Условия использования, запрет модификации · " + EulaAgreement.SupportEmail) : ("Terms of use & modification restrictions · " + EulaAgreement.SupportEmail), eulaBtn));
                    section("SecGeneral", general, true);

                    var startup = new StackPanel();
                    var auto = new CheckBox { Content = "", IsChecked = ScumAutoLaunch.Enabled, Width = 48, MinHeight = 30, Tag = "auto-launch-scum", IsEnabled = !diagnostic };
                    System.Windows.Automation.AutomationProperties.SetName(auto, ScumAutoLaunch.Label);
                    auto.ToolTip = ScumAutoLaunch.Help;
                    auto.Click += async (s, e) => {
                        if (diagnostic)
                            return;
                        bool enable = auto.IsChecked == true;
                        auto.IsEnabled = false;
                        try {
                            await System.Threading.Tasks.Task.Run(() => ScumAutoLaunch.SetEnabled(enable));
                        } catch (Exception error) { Program.LogException("ScumAutoLaunchSettings", error); ShowFluentNotice(window, error.Message, "ZiloreMiniMap", true); }
                        auto.IsChecked = ScumAutoLaunch.Enabled;
                        auto.IsEnabled = true;
                    };
                    startup.Children.Add(WpfFieldTheme.SettingRow(ScumAutoLaunch.Label, ScumAutoLaunch.Help, auto));
                    section("SettingsStartup", startup, true);
                } else if (index == 1) {
                    // Pure WPF Shortcuts Tab
                    var help = WpfFieldTheme.Text(Localization.Get("KeyWizardIntro"), 13.5, WpfFieldTheme.Muted);
                    help.TextWrapping = TextWrapping.Wrap;
                    help.Margin = new Thickness(4, 0, 0, 10);
                    body.Children.Add(help);
                    var gameHdr = WpfFieldTheme.Text((Localization.Current == AppLanguage.Russian ? "Клавиши игры SCUM" : "SCUM Game Keys").ToUpper(CultureInfo.CurrentCulture), 11.5, WpfFieldTheme.Muted, true);
                    gameHdr.Margin = new Thickness(4, 4, 0, 6);
                    body.Children.Add(gameHdr);
                    int[] gameKeys = { state.MapKey, state.ChatKey, state.CopyKey };
                    string[] gameLabels = { "KeyWizardMapKey", "KeyWizardChatKey", "KeyWizardCopyKey" };
                    for (int i = 0; i < gameKeys.Length; i++) {
                        int binding = i;
                        string label = Localization.Get(gameLabels[i]);
                        string value = (i == 2 && state.CopyModifier != 0 ? PhysicalKeyCapture.KeyName(state.CopyModifier) + " + " : "") + PhysicalKeyCapture.KeyName(gameKeys[i]);
                        var key = WpfFieldTheme.Button(value, () => { CaptureKey(label, (vk, scan) => engine.WpfGameBind(binding, vk, scan), false); page(1); }, i == 0);
                        key.Tag = "game-binding-" + i;
                        key.MinWidth = 100;
                        key.MinHeight = 34;
                        key.Padding = new Thickness(12, 4, 12, 4);
                        key.FontSize = 13.5;
                        key.Margin = new Thickness(0);
                        System.Windows.Automation.AutomationProperties.SetName(key, label + ": " + value);
                        body.Children.Add(WpfFieldTheme.SettingRow(label, null, key, i == 0));
                    }
                    var appHdr = WpfFieldTheme.Text((Localization.Current == AppLanguage.Russian ? "Управление ZiloreMiniMap" : "ZiloreMiniMap Controls").ToUpper(CultureInfo.CurrentCulture), 11.5, WpfFieldTheme.Muted, true);
                    appHdr.Margin = new Thickness(4, 12, 0, 6);
                    body.Children.Add(appHdr);
                    foreach (var action in AppControlBindings.AllActions) {
                        var captured = action;
                        string actLabel = Localization.Get(AppControlBindings.LabelKey(action));
                        string keyTxt = PhysicalKeyCapture.KeyName(state.Bindings.Key(action));
                        var key = WpfFieldTheme.Button(keyTxt, () => { CaptureKey(actLabel, (vk, scan) => engine.WpfBind(captured, vk, scan), true); page(1); });
                        key.MinWidth = 100;
                        key.MinHeight = 34;
                        key.Padding = new Thickness(12, 4, 12, 4);
                        key.FontSize = 13.5;
                        key.Margin = new Thickness(0);
                        System.Windows.Automation.AutomationProperties.SetName(key, actLabel + ": " + keyTxt);
                        body.Children.Add(WpfFieldTheme.SettingRow(actLabel, null, key));
                    }
                    var physHelp = WpfFieldTheme.Text(Localization.Get("ShortcutPhysicalHelp"), 13, WpfFieldTheme.Muted);
                    physHelp.TextWrapping = TextWrapping.Wrap;
                    physHelp.Margin = new Thickness(4, 8, 0, 8);
                    body.Children.Add(physHelp);
                    body.Children.Add(WpfCheck(Localization.Get("AutomaticDeathMarkers"), engine.WpfAutomaticDeathMarkers, v => engine.WpfAutomaticDeathMarkers = v));
                    body.Children.Add(WpfCheck(WpfFieldTheme.UiText("LayerHotkeysAlt"), WpfMapSurface.AltLayerHotkeysEnabled, v => { WpfMapSurface.AltLayerHotkeysEnabled = v; WpfMapSurface.SaveFlightUiSettings(); filterIdentity = null; Refresh(); }, "alt-layer-hotkeys", WpfFieldTheme.UiText("LayerHotkeysTip")));
                    var tacHkHdr = WpfFieldTheme.Text(WpfFieldTheme.UiText("TacticalHotkeysHeading").ToUpper(CultureInfo.CurrentCulture), 11.5, WpfFieldTheme.Muted, true);
                    tacHkHdr.Margin = new Thickness(4, 14, 0, 6);
                    body.Children.Add(tacHkHdr);
                    var tacHkGrid = new UniformGrid { Columns = 2 };
                    foreach (var hk in GetTacticalHotkeyCatalog(Localization.Current == AppLanguage.Russian)) {
                        var hkRow = new DockPanel { Margin = new Thickness(0, 2, 8, 2) };
                        var kTxt = WpfFieldTheme.Text(hk.Item1, 11.5, WpfFieldTheme.Cyan, true);
                        var kBadge = WpfFieldTheme.Badge(kTxt, new Thickness(6, 2, 6, 2));
                        kBadge.Margin = new Thickness(0, 0, 8, 0);
                        kBadge.VerticalAlignment = VerticalAlignment.Center;
                        DockPanel.SetDock(kBadge, Dock.Left);
                        hkRow.Children.Add(kBadge);
                        var dTxt = WpfFieldTheme.Text(hk.Item2, 12, WpfFieldTheme.Muted);
                        dTxt.TextWrapping = TextWrapping.Wrap;
                        dTxt.VerticalAlignment = VerticalAlignment.Center;
                        hkRow.Children.Add(dTxt);
                        tacHkGrid.Children.Add(hkRow);
                    }
                    var tacHkCard = WpfFieldTheme.Card(tacHkGrid, new Thickness(12, 10, 12, 10));
                    tacHkCard.Margin = new Thickness(0, 0, 0, 6);
                    body.Children.Add(tacHkCard);
                } else if (index == 2) {
                    BuildSettingsAppearanceTab(window, body, state, section, () => { nav.Background = Brushes.Transparent; page(currentPage); });
                } else if (index == 3) {
                    BuildSettingsTrackingTab(section);
                } else if (index == 4) {
                    var groups = new StackPanel();
                    var privBtn = WpfFieldTheme.Button(Localization.Current == AppLanguage.Russian ? "Открыть" : "Open", ShowPrivatePositions, true);
                    privBtn.MinHeight = 34;
                    privBtn.MinWidth = 110;
                    privBtn.Padding = new Thickness(14, 6, 14, 6);
                    privBtn.FontSize = 13.5;
                    privBtn.Margin = new Thickness(0);
                    groups.Children.Add(WpfFieldTheme.SettingRow(Localization.Get("PrivatePositions"), Localization.Get("PrivateGroupHelp"), privBtn, true));
                    var routesBtn = WpfFieldTheme.Button(Localization.Current == AppLanguage.Russian ? "Открыть" : "Open", ShowRoutes);
                    routesBtn.MinHeight = 34;
                    routesBtn.MinWidth = 110;
                    routesBtn.Padding = new Thickness(14, 6, 14, 6);
                    routesBtn.FontSize = 13.5;
                    routesBtn.Margin = new Thickness(0);
                    groups.Children.Add(WpfFieldTheme.SettingRow(Localization.Get("RouteList"), Localization.Get("RouteSessionHelp"), routesBtn));
                    body.Children.Add(groups);
                } else if (index == 6) {
                    logTimer = BuildSettingsLogsTab(window, body, () => currentPage, logTimer);
                } else {
                    BuildSettingsToolsTab(window, section, () => page(currentPage));
                }
            };
            nav.SelectionChanged += (sender, e) => {
                if (selecting || nav.SelectedItem == null)
                    return;
                try {
                    var item = nav.SelectedItem as ListBoxItem;
                    string tag = item == null ? null : item.Tag as string;
                    int catIdx;
                    if (tag != null && tag.StartsWith("settings-category:", StringComparison.Ordinal) && int.TryParse(tag.AsSpan(18), NumberStyles.Integer, CultureInfo.InvariantCulture, out catIdx))
                        page(catIdx);
                } catch (Exception ex) {
                    Program.LogException("SettingsNav", ex);
                }
            };
            var searchCatalog = new[] {
                Tuple.Create(0,"Язык · Руководство · Автозапуск SCUM","Language · Guide · Auto-Launch SCUM","язык language руководство guide автозапуск startup launch"),
                Tuple.Create(2,"Тема оформления (Светлая / Тёмная / OLED / Авто) · GPU 4K","UI Theme (Light / Dark / OLED / System) · GPU 4K","тема theme dark oled light светлая тёмная gpu 4k 144 ускорение"),
                Tuple.Create(2,"Миникарта: размер, форма, прозрачность, вращение, компас","Minimap: size, shape, opacity, rotation, compass","миникарта minimap hud размер форма круг квадрат прозрачность opacity вращение компас"),
                Tuple.Create(2,"Сетка секторов · POI фильтры · Зоны · Цвета · Трек","Sector grid · POI filters · Zones · Colors · Trail","сетка grid poi зоны zones цвета color трек history метки waypoints пресеты presets"),
                Tuple.Create(3,"Авто-зум · Авиарежим ✈ · Объезд PvP-зон · Интервал GPS","Auto-zoom · Aviation ✈ · PvP avoidance · GPS interval","зум zoom авиа aviation полёт flight объезд avoid pvp интервал gps tracking"),
                Tuple.Create(3,"Голосовая навигация · Выбор голоса и устройства · Громкость","Voice navigation · Voice pack & output device · Volume","голос voice звук audio устройство device громкость volume chime"),
                Tuple.Create(1,"Горячие клавиши SCUM и ZiloreMiniMap · Клавиши карты M","SCUM & ZiloreMiniMap hotkeys · Tactical map M keys","клавиши горячие shortcuts keys hotkeys бинды numpad copy location"),
                Tuple.Create(4,"Отряд · Приватные группы · Маршруты","Squad · Private groups · Routes","отряд squad группы groups маршруты routes relay p2p"),
                Tuple.Create(5,"Админ-панель · Обновления · Папка данных · О программе · Debug FPS","Admin tools · Updates · Data folder · About · Debug FPS","админ admin обновления updates папка data о программе about бэкап backup fps debug"),
                Tuple.Create(6,"Журнал событий и выгрузка ZIP для поддержки","Diagnostic log & support ZIP export","журнал лог log error ошибки support поддержка")
            };
            settingsSearch.TextChanged += (s, e) => {
                string q = (settingsSearch.Text ?? "").Trim();
                bool empty = q.Length == 0;
                searchHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
                searchClear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
                searchResultsPanel.Children.Clear();
                if (empty) {
                    searchResultsPanel.Visibility = Visibility.Collapsed;
                    return;
                }
                bool ruS = Localization.Current == AppLanguage.Russian;
                var hitWrap = new WrapPanel();
                int hits = 0;
                foreach (var entry in searchCatalog) {
                    string titleTxt = ruS ? entry.Item2 : entry.Item3;
                    if (titleTxt.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || entry.Item4.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) {
                        int targetIdx = entry.Item1;
                        var jumpBtn = WpfFieldTheme.Button("→ " + titleTxt, () => { settingsSearch.Text = ""; page(targetIdx); });
                        jumpBtn.MinHeight = 30;
                        jumpBtn.Padding = new Thickness(10, 4, 10, 4);
                        jumpBtn.FontSize = 12.5;
                        jumpBtn.Margin = new Thickness(0, 0, 6, 6);
                        hitWrap.Children.Add(jumpBtn);
                        hits++;
                    }
                }
                if (hits == 0)
                    searchResultsPanel.Children.Add(WpfFieldTheme.Text(WpfFieldTheme.UiText("SettingsSearchEmpty"), 12.5, WpfFieldTheme.Muted));
                else
                    searchResultsPanel.Children.Add(WpfFieldTheme.Card(hitWrap, new Thickness(10, 8, 10, 4)));
                searchResultsPanel.Visibility = Visibility.Visible;
            };
            window.SizeChanged += (sender, e) => { workspace.ColumnDefinitions[0].Width = new GridLength(window.ActualWidth < 760 ? 146 : 188); searchWrap.Width = window.ActualWidth < 760 ? 200 : 310; };
            window.PreviewKeyDown += (sender, e) => { if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { int position = Array.IndexOf(order, currentPage); int requested = order[(position + ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? order.Length - 1 : 1)) % order.Length]; page(requested); if (currentPage == requested) items[currentPage].Focus(); e.Handled = true; } };
            page(initial);
            window.Activated += (sender, e) => { if (settingsRevision != engine.WpfSettingsRevision) page(currentPage); };
            window.Closed += (sender, e) => { if (logTimer != null) { logTimer.Stop(); logTimer = null; } settingsPreview = null; lastSetupPreviewKey = null; body.Children.Clear(); };
            window.Content = shell;
            return window;
        }
        void AddAppearanceControls(StackPanel body, WpfMapState state) {
            var rotate = new CheckBox { Content = Localization.Get("RotateMinimap"), IsChecked = state.RotateMap, Margin = new Thickness(0, 8, 0, 8), Tag = "rotate-minimap" };
            rotate.Checked += (s, e) => { engine.WpfRotate(true); Refresh(); };
            rotate.Unchecked += (s, e) => { engine.WpfRotate(false); Refresh(); };
            body.Children.Insert(0, rotate);
            var clusterPois = new CheckBox { Content = Localization.Get("PoiClustering"), IsChecked = state.PoiClustering, Margin = new Thickness(0, 8, 0, 8), Tag = "poi-clustering" };
            clusterPois.Checked += (s, e) => { engine.WpfPoiClustering(true); Refresh(); };
            clusterPois.Unchecked += (s, e) => { engine.WpfPoiClustering(false); Refresh(); };
            body.Children.Insert(1, clusterPois);
            var shape = new ComboBox { ItemsSource = new[] { Localization.Get("ZeSquare"), Localization.Get("ZeCircle") }, SelectedIndex = state.Shape == "Circle" ? 1 : 0, Margin = new Thickness(0, 14, 0, 14), Tag = "minimap-shape" };
            shape.SelectionChanged += (s, e) => { engine.WpfAppearance(shape.SelectedIndex == 1 ? "Circle" : "Square", engine.WpfSnapshot(false).Opacity); Refresh(); };
            body.Children.Insert(0, shape);
            body.Children.Insert(0, WpfFieldTheme.Text(Localization.Get("ZeChangeShape"), 18, null, true));
        }
        WpfMapSurface setupPreview, settingsPreview;
        string lastSetupPreviewKey;
        void RefreshSetupPreview() {
            if (setupPreview == null && settingsPreview == null)
                return;
            var state = engine.WpfSnapshot(false);
            if (state.Position == null)
                state.Position = new Position { X = 0, Y = 0, Yaw = 90 };
            string key = state.Shape + "|" + state.Opacity + "|" + state.HudWidth + "|" + state.HudHeight + "|" + state.RotateMap + "|" + state.PoiClustering + "|" + state.Grid + "|" + state.Zones + "|" + state.Labels + "|" + state.PlayerColor + "|" + state.RouteColor + "|" + WpfFieldTheme.ActiveTheme + "|" + Math.Round(state.Zoom, 2) + "|" + (int)Math.Round(state.Position.X / 500.0) + "|" + (int)Math.Round(state.Position.Y / 500.0) + "|" + (int)Math.Round(state.Position.Yaw / 5.0);
            if (key == lastSetupPreviewKey)
                return;
            lastSetupPreviewKey = key;
            double maxDim = Math.Max(1, Math.Max(state.HudWidth, state.HudHeight));
            double pw = 210.0 * state.HudWidth / maxDim, ph = 210.0 * state.HudHeight / maxDim;
            if (setupPreview != null) {
                if (Math.Abs(setupPreview.Width - pw) > 0.5)
                    setupPreview.Width = pw;
                if (Math.Abs(setupPreview.Height - ph) > 0.5)
                    setupPreview.Height = ph;
                setupPreview.State = state;
                setupPreview.InvalidateVisual();
            }
            if (settingsPreview != null) {
                if (Math.Abs(settingsPreview.Width - pw) > 0.5)
                    settingsPreview.Width = pw;
                if (Math.Abs(settingsPreview.Height - ph) > 0.5)
                    settingsPreview.Height = ph;
                settingsPreview.State = state;
                settingsPreview.InvalidateVisual();
            }
        }
    }
}
