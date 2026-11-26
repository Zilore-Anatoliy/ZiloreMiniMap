using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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
        [GeneratedRegex(@"^ZiloreMiniMap\s*[-—–:]\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex RxWizardTitlePrefix();
        [GeneratedRegex(@"^\s*\d+[.\s]+", RegexOptions.CultureInvariant)] private static partial Regex RxWizardStageNumber();
        sealed class GuideChecklistState {
            internal readonly bool[] Read = new bool[6]; internal bool NoNumpad;
        }
        static Style GuideInputStyle(bool choice = false) {
            string type = choice ? "RadioButton" : "CheckBox";
            return (Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type INPUT}'>
<Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type INPUT}'>
<Border x:Name='outer' Background='Transparent' Padding='{TemplateBinding Padding}'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='36'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
<Border x:Name='box' Width='22' Height='22' HorizontalAlignment='Left' VerticalAlignment='Top' Margin='0,2,10,0' CornerRadius='RADIUS' BorderThickness='2' BorderBrush='{DynamicResource ThemeLine}' Background='{DynamicResource ThemeGround}'><TextBlock x:Name='tick' Text='✓' FontFamily='Segoe UI Symbol' FontSize='15' FontWeight='Bold' Foreground='{DynamicResource ThemePrimaryInk}' TextAlignment='Center' VerticalAlignment='Center' Visibility='Collapsed'/></Border>
<ContentPresenter Grid.Column='1' Content='{TemplateBinding Content}' ContentTemplate='{TemplateBinding ContentTemplate}' RecognizesAccessKey='True' VerticalAlignment='Center'/></Grid></Border>
<ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='tick' Property='Visibility' Value='Visible'/><Setter TargetName='box' Property='Background' Value='{DynamicResource ThemeAmber}'/><Setter TargetName='box' Property='BorderBrush' Value='{DynamicResource ThemeAmber}'/></Trigger>
<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='box' Property='BorderBrush' Value='{DynamicResource ThemeAmber}'/></Trigger>
<Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='box' Property='BorderBrush' Value='{DynamicResource ThemeCyan}'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='outer' Property='Opacity' Value='0.5'/></Trigger></ControlTemplate.Triggers>
</ControlTemplate></Setter.Value></Setter></Style>".Replace("INPUT", type).Replace("RADIUS", choice ? "11" : "5"));
        }
        static string CleanWizardTitle(string raw) {
            if (string.IsNullOrWhiteSpace(raw))
                return "ZiloreMiniMap";
            string s = raw.Trim();
            s = RxWizardTitlePrefix().Replace(s, "");
            return string.IsNullOrWhiteSpace(s) ? "ZiloreMiniMap" : ("ZiloreMiniMap — " + s);
        }
        FrameworkElement BuildGuideChecklist(GuideChecklistState state, Action<bool> readyChanged, bool showPreference = true) {
            var root = new StackPanel();
            Action draw = null;
            draw = () => {
                bool ru = Localization.Current == AppLanguage.Russian;
                root.Children.Clear();

                // 1. Live Environment Status Bar
                string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
                string tempDir = Path.GetTempPath() ?? "";
                bool unpacked = !string.IsNullOrEmpty(baseDir) && (string.IsNullOrEmpty(tempDir) || !baseDir.StartsWith(tempDir, StringComparison.OrdinalIgnoreCase));
                bool scumRunning = !diagnostic && Native.AdminGameWindow() != IntPtr.Zero;
                var binding = engine.WpfSnapshot(false);
                bool defaultKey = binding.CopyKey == Program.DefaultCopyKey && binding.CopyModifier == 0;
                bool keyReady = state.NoNumpad ? binding.CopyKey != Program.DefaultCopyKey && binding.CopyModifier == 0 : defaultKey;
                if (!keyReady)
                    state.Read[3] = false;
                string currentKeyName = (binding.CopyModifier == 0 ? "" : PhysicalKeyCapture.KeyName(binding.CopyModifier) + " + ") + PhysicalKeyCapture.KeyName(binding.CopyKey);

                var envDock = new DockPanel();
                var recheckBtn = WpfFieldTheme.Button("↻", () => draw());
                recheckBtn.MinHeight = 28;
                recheckBtn.MinWidth = 32;
                recheckBtn.Padding = new Thickness(8, 2, 8, 2);
                recheckBtn.FontSize = 13;
                recheckBtn.ToolTip = ru ? "Обновить статус SCUM и окружения" : "Refresh SCUM and environment status";
                DockPanel.SetDock(recheckBtn, Dock.Right);
                envDock.Children.Add(recheckBtn);

                var pills = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
                var pAssets = WpfFieldTheme.StatusPill(unpacked ? (ru ? "Папка распакована" : "Extracted OK") : (ru ? "Запуск из Temp" : "Running from Temp"), unpacked ? WpfFieldTheme.StatusLive : WpfFieldTheme.StatusPaused);
                pAssets.Margin = new Thickness(0, 0, 8, 4);
                pills.Children.Add(pAssets);
                var pScum = WpfFieldTheme.StatusPill(scumRunning ? (ru ? "SCUM запущен" : "SCUM running") : (ru ? "SCUM не запущен" : "SCUM not running"), scumRunning ? WpfFieldTheme.StatusLive : WpfFieldTheme.StatusIdle);
                pScum.Margin = new Thickness(0, 0, 8, 4);
                pills.Children.Add(pScum);
                var pKey = WpfFieldTheme.StatusPill((ru ? "Клавиша: " : "Key: ") + currentKeyName, keyReady ? WpfFieldTheme.Cyan : WpfFieldTheme.StatusPaused);
                pKey.Margin = new Thickness(0, 0, 8, 4);
                pills.Children.Add(pKey);
                envDock.Children.Add(pills);
                var envCard = WpfFieldTheme.Card(envDock, new Thickness(12, 9, 12, 5));
                envCard.Margin = new Thickness(0, 10, 0, 12);
                root.Children.Add(envCard);

                // 2. Intro + Progress Bar + Quick Check All
                var intro = WpfFieldTheme.Text(Localization.Get("GuideChecklistIntro"), 15, null, true);
                intro.TextWrapping = TextWrapping.Wrap;
                root.Children.Add(intro);
                var switchHelp = WpfFieldTheme.Text(Localization.Get("GuideSwitchApps"), 13, WpfFieldTheme.Muted);
                switchHelp.TextWrapping = TextWrapping.Wrap;
                switchHelp.Margin = new Thickness(0, 3, 0, 12);
                root.Children.Add(switchHelp);

                var progressDock = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                var progress = WpfFieldTheme.Text("", 13.5, WpfFieldTheme.Cyan, true);
                progress.Tag = "guide-progress";
                progress.VerticalAlignment = VerticalAlignment.Center;
                var progressBar = new ProgressBar { Minimum = 0, Maximum = 6, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0), Foreground = WpfFieldTheme.Cyan, Background = WpfFieldTheme.Raised, BorderThickness = new Thickness(0) };
                var checkAllBtn = WpfFieldTheme.Button(ru ? "Отметить всё" : "Check all", () => {
                    for (int k = 0; k < state.Read.Length; k++)
                    if (k != 3 || keyReady)
                        state.Read[k] = true;
                    draw();
                });
                checkAllBtn.MinHeight = 28;
                checkAllBtn.Padding = new Thickness(10, 2, 10, 2);
                checkAllBtn.FontSize = 12.5;
                DockPanel.SetDock(progress, Dock.Left);
                DockPanel.SetDock(checkAllBtn, Dock.Right);
                progressDock.Children.Add(progress);
                if (showPreference)
                    progressDock.Children.Add(checkAllBtn);
                progressDock.Children.Add(progressBar);
                root.Children.Add(progressDock);

                // 3. Keyboard Type 2-Column Card
                var choicesGrid = new UniformGrid { Columns = 2 };
                string group = "numpad-" + Guid.NewGuid().ToString("N");
                foreach (bool noPad in new[] { false, true }) {
                    bool selected = noPad;
                    var choiceText = WpfFieldTheme.Text(Localization.Get(noPad ? "GuideNoNumpad" : "GuideHaveNumpad"), 13.5, null, state.NoNumpad == noPad);
                    choiceText.TextWrapping = TextWrapping.Wrap;
                    var choice = new RadioButton { Style = GuideInputStyle(true), GroupName = group, IsChecked = state.NoNumpad == noPad, Content = choiceText, Padding = new Thickness(8, 6, 8, 6), Tag = noPad ? "guide-no-numpad" : "guide-have-numpad" };
                    choice.Checked += (s, e) => { if (state.NoNumpad == selected) return; state.NoNumpad = selected; state.Read[2] = state.Read[3] = false; draw(); };
                    var choiceCard = WpfFieldTheme.Card(choice, new Thickness(4));
                    choiceCard.Margin = noPad ? new Thickness(5, 0, 0, 0) : new Thickness(0, 0, 5, 0);
                    if (state.NoNumpad == noPad)
                        choiceCard.SetResourceReference(Border.BorderBrushProperty, "ThemeAmber");
                    choicesGrid.Children.Add(choiceCard);
                }
                choicesGrid.Margin = new Thickness(0, 0, 0, 12);
                root.Children.Add(choicesGrid);

                // 4. Checklist Cards with Integrated Callouts & Proper Spacing
                var checkCards = new Border[6];
                Action sync = () => {
                    int count = state.Read.Count(value => value);
                    progress.Text = count + " / " + state.Read.Length;
                    progressBar.Value = count;
                    checkAllBtn.IsEnabled = count < state.Read.Length && keyReady;
                    for (int ci = 0; ci < checkCards.Length; ci++) {
                        if (checkCards[ci] != null)
                            checkCards[ci].SetResourceReference(Border.BorderBrushProperty, state.Read[ci] ? "ThemeCyan" : "ThemeLine");
                    }
                    readyChanged(count == state.Read.Length && keyReady);
                };

                for (int i = 0; i < 6; i++) {
                    int index = i;
                    string key = i == 0 ? "GuideExtracted" : i == 1 ? "GuideWindowed" : i == 2 ? (state.NoNumpad ? "GuideScumOther" : "GuideScumNumpad") : i == 3 ? (state.NoNumpad ? "GuideMiniOther" : "GuideMiniNumpad") : i == 4 ? "GuideCloseSettings" : "GuideTrackingTest";
                    var cardBody = new StackPanel();
                    var text = WpfFieldTheme.Text(Localization.Get(key), 14);
                    text.TextWrapping = TextWrapping.Wrap;
                    var check = new CheckBox { Style = GuideInputStyle(), Content = text, IsChecked = state.Read[i], Padding = new Thickness(6, 4, 6, 4), HorizontalContentAlignment = HorizontalAlignment.Stretch, Tag = "guide-read-" + i };
                    if (i == 3)
                        check.IsEnabled = keyReady;
                    check.Click += (s, e) => { state.Read[index] = check.IsChecked == true; if (index == 2 && state.NoNumpad) draw(); else sync(); };
                    cardBody.Children.Add(check);

                    if (i == 2) {
                        var pathTxt = WpfFieldTheme.Text("SCUM: OPTIONS → MOUSE & KEYBOARD → CONTROLS → UI → COPY LOCATION → APPLY", 12, WpfFieldTheme.Amber, true);
                        pathTxt.TextWrapping = TextWrapping.Wrap;
                        pathTxt.TextTrimming = TextTrimming.None;
                        var pathPill = WpfFieldTheme.Badge(pathTxt, new Thickness(10, 5, 10, 5));
                        pathPill.Margin = new Thickness(38, 4, 4, 2);
                        cardBody.Children.Add(pathPill);
                    }
                    if (i == 3) {
                        var keyDock = new DockPanel { Margin = new Thickness(38, 4, 4, 2) };
                        if (state.NoNumpad) {
                            var capture = WpfFieldTheme.Button(Localization.Get("WizardBtnCapture"), () => {
                                CaptureKey(Localization.Get("WizardKeyCopy"), (vk, scan) => { if (vk == Program.DefaultCopyKey) return Localization.Get("GuideNoNumpad"); string error = engine.WpfGameBind(2, vk, scan); if (error == null) engine.WpfSingleCopyKey(true); return error; }, false);
                                state.Read[3] = false;
                                draw();
                            }, true);
                            capture.Tag = "guide-capture-copy";
                            capture.IsEnabled = state.Read[2];
                            capture.FontSize = 13;
                            capture.MinHeight = 30;
                            capture.Padding = new Thickness(12, 3, 12, 3);
                            DockPanel.SetDock(capture, Dock.Right);
                            keyDock.Children.Add(capture);
                        } else if (!defaultKey) {
                            var defaults = WpfFieldTheme.Button(Localization.Get("GuideUseNumpad"), () => { string error = engine.WpfGameBind(2, Program.DefaultCopyKey, 0); if (error != null) { ShowZoneMessage(error); return; } engine.WpfSingleCopyKey(true); state.Read[3] = false; draw(); }, true);
                            defaults.Tag = "guide-use-numpad";
                            defaults.FontSize = 13;
                            defaults.MinHeight = 30;
                            defaults.Padding = new Thickness(12, 3, 12, 3);
                            DockPanel.SetDock(defaults, Dock.Right);
                            keyDock.Children.Add(defaults);
                        }
                        var actual = WpfFieldTheme.Text(Localization.Get("WizardKeyCopy") + ": " + currentKeyName, 13.5, WpfFieldTheme.Amber, true);
                        actual.Tag = "guide-current-key";
                        actual.VerticalAlignment = VerticalAlignment.Center;
                        keyDock.Children.Add(actual);
                        cardBody.Children.Add(keyDock);
                    }

                    var card = WpfFieldTheme.Card(cardBody, new Thickness(10, 8, 10, 8));
                    card.Margin = new Thickness(0, 0, 0, 8);
                    checkCards[i] = card;
                    root.Children.Add(card);
                }
                sync();
            };
            draw();
            return root;
        }
        Window BuildFirstTimeGuide() {
            var window = Window(Localization.Get("FirstTimeGuideTitle"), 860, 800);
            var body = new DockPanel { Margin = new Thickness(24) };
            var done = WpfFieldTheme.Button(Localization.Get("Done"), () => { if (engine.WpfCompleteOnboarding()) window.Close(); else ShowFluentNotice(window, Localization.Get("LocalSaveFailed"), Localization.Get("WizardTitle"), true); }, true);
            done.Tag = "guide-done";
            done.IsEnabled = false;
            done.MinWidth = 160;
            var footer = new StackPanel();
            var actions = new DockPanel();
            DockPanel.SetDock(done, Dock.Right);
            actions.Children.Add(done);
            var progress = WpfFieldTheme.Text("", 16, WpfFieldTheme.Cyan, true);
            progress.Tag = "guide-progress";
            actions.Children.Add(progress);
            footer.Children.Add(actions);
            DockPanel.SetDock(footer, Dock.Bottom);
            body.Children.Add(footer);
            var state = new GuideChecklistState();
            var content = new StackPanel();
            content.Children.Add(WpfFieldTheme.Text(Localization.Get("FirstTimeGuideTitle"), 22, null, true));
            content.Children.Add(BuildGuideChecklist(state, ready => { done.IsEnabled = ready; progress.Text = state.Read.Count(value => value) + " / " + state.Read.Length; }, false));
            body.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            WpfFieldTheme.ApplyFluentShell(window, "ZiloreMiniMap — " + Localization.Get("FirstTimeGuideTitle"), body);
            return window;
        }
        void ShowSetupWindow(bool detailed) {
            if (disposed)
                return;
            if (setupWindow != null) {
                setupWindow.WindowState = WindowState.Normal;
                setupWindow.Topmost = true;
                setupWindow.Show();
                FocusInteractive(setupWindow);
                return;
            }
            if (tactical.IsVisible) {
                tacticalStandalone = false;
                tactical.Topmost = false;
                tactical.Hide();
                engine.WpfTactical(false);
            }
            // Automatic setup and Settings use one complete flow: the checklist
            // leads into appearance, map layers, tracking and sharing settings.
            var window = BuildOnboarding(0);
            setupWindow = window;
            window.Topmost = true;
            window.Owner = null;
            window.ShowInTaskbar = true;
            window.ResizeMode = ResizeMode.CanResizeWithGrip;
            window.ShowActivated = !diagnostic;
            if (diagnostic) {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -32000;
                window.Top = -32000;
            }
            if (hud != null && hud.IsVisible)
                hud.Topmost = false;
            window.Activated += (s, e) => SetSetupInputFocus(true);
            window.Deactivated += (s, e) => SetSetupInputFocus(false);
            window.Loaded += (s, e) => FocusInteractive(window);
            window.ContentRendered += (s, e) => FocusInteractive(window);
            window.Closed += (s, e) => { setupWindow = null; setupHasFocus = false; SyncModalInput(); RefreshInputOwnership(); engine.WpfCompleteOnboarding(); engine.WpfSave(); if (!disposed) { SyncHudVisibility(true); Refresh(); } };
            window.Show();
            RefreshInputOwnership();
        }
        void ShowOnboarding() {
            ShowSetupWindow(true);
        }
        internal void ShowZoneWizard(string file = null) {
            if (!application.Dispatcher.CheckAccess()) {
                application.Dispatcher.BeginInvoke(new Action(() => ShowZoneWizard(file)));
                return;
            }
            if (file != null && Path.GetExtension(file).Equals(".tsv", StringComparison.OrdinalIgnoreCase)) {
                ShowZoneFile(file);
                return;
            }
            Modal(BuildZoneWizard(file));
        }
        internal void ShowZoneAssociation() {
            try {
                ZoneFileOpen.Register(System.Windows.Forms.Application.ExecutablePath);
                ShowZoneMessage(Localization.Get("ZoneAssociationInfo"));
            } catch (Exception error) { Program.LogException("ZoneFileAssociation", error); ShowZoneMessage(Localization.T("ZoneOpenError", error.Message)); }
        }
        void ShowZoneMessage(string message) {
            ShowFluentNotice(null, message, Localization.Get("ZoneEditorTitle"));
        }
        void ShowZoneFile(string file) { _ = ShowZoneFileAsync(file); }
        async System.Threading.Tasks.Task ShowZoneFileAsync(string file) {
            try {
                if (zoneFileActive || disposed)
                    return;
                zoneFileActive = true;
                try {
                    var imported = await System.Threading.Tasks.Task.Run(() => { if (!Path.GetExtension(file).Equals(".tsv", StringComparison.OrdinalIgnoreCase) || new FileInfo(file).Length > 32L * 1024 * 1024) throw new InvalidDataException("Invalid zone TSV file."); return ZoneStore.Load(file); });
                    if (disposed)
                        return;
                    if (imported.Count == 0)
                        throw new InvalidDataException("The file contains no zones.");
                    string initial = Path.GetFileNameWithoutExtension(file);
                    if (initial.Equals("zones", StringComparison.OrdinalIgnoreCase))
                        initial = Path.GetFileName(Path.GetDirectoryName(file));
                    string layer = null;
                    Modal(BuildZonePackName(initial, value => layer = value));
                    if (layer == null || disposed)
                        return;
                    var wizard = BuildZoneWizard();
                    try {
                        ((ZoneEditor)wizard.Tag).WpfImportZonePack(imported, layer);
                        Modal(wizard);
                    } finally { wizard.Close(); }
                } catch (Exception error) { Program.LogException("ZoneFileImport", error); if (!disposed) ShowZoneMessage(Localization.T("ZoneOpenError", error.Message)); } finally { zoneFileActive = false; }
            } catch (Exception ex) { Program.LogException("ShowZoneFile", ex); }
        }
        Window BuildZonePackName(string initial, Action<string> accepted) {
            var window = Window(Localization.Get("NameThisMap"), 520, 295);
            window.ResizeMode = ResizeMode.NoResize;
            var layout = new StackPanel { Margin = new Thickness(22) };
            layout.Children.Add(WpfFieldTheme.Text(Localization.Get("NameThisMap"), 23));
            var input = new TextBox { Tag = "zone-pack-name", Text = initial == null ? "" : initial.Substring(0, Math.Min(80, initial.Length)), MaxLength = 80, Margin = new Thickness(0, 18, 0, 14) };
            layout.Children.Add(input);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = WpfFieldTheme.Button(Localization.Get("Cancel"), () => window.Close());
            row.Children.Add(cancel);
            var add = WpfFieldTheme.Button(Localization.Get("ZeImportFile"), () => { if (string.IsNullOrWhiteSpace(input.Text)) return; accepted(input.Text.Trim()); window.Close(); }, true);
            add.Tag = "zone-pack-accept";
            add.IsEnabled = !string.IsNullOrWhiteSpace(input.Text);
            row.Children.Add(add);
            layout.Children.Add(row);
            input.TextChanged += (s, e) => add.IsEnabled = !string.IsNullOrWhiteSpace(input.Text);
            input.KeyDown += (s, e) => { if (e.Key == Key.Enter && add.IsEnabled) { accepted(input.Text.Trim()); window.Close(); e.Handled = true; } else if (e.Key == Key.Escape) window.Close(); };
            window.Loaded += (s, e) => { input.Focus(); input.SelectAll(); };
            WpfFieldTheme.ApplyFluentShell(window, "ZiloreMiniMap — " + Localization.Get("NameThisMap"), layout);
            return window;
        }
        Window BuildZoneWizard(string file = null) {
            var editor = engine.WpfZoneEditor();
            var nativeHandle = editor.Handle;
            var window = Window(Localization.Get("ZoneEditorTitle"), 1450, 920);
            window.MinWidth = 640;
            window.MinHeight = 420;
            window.Tag = editor;
            window.WindowStyle = WindowStyle.None;
            window.AllowsTransparency = true;
            window.Background = Brushes.Transparent;
            editor.WpfOwner = new WpfWindowOwner(new WindowInteropHelper(window).EnsureHandle());
            var root = new DockPanel { Margin = new Thickness(18, 14, 18, 18) };
            var instruction = WpfFieldTheme.Text(editor.WpfInstruction, 15, WpfFieldTheme.Amber);
            instruction.TextWrapping = TextWrapping.Wrap;
            var footer = new DockPanel();
            var save = WpfFieldTheme.Button(Localization.Get("ZeSaveZones"), () => editor.WpfClick(editor.WpfAction("ZeSaveZones")), true);
            save.Tag = "ZeSaveZones";
            DockPanel.SetDock(save, Dock.Right);
            footer.Children.Add(save);
            footer.Children.Add(new ScrollViewer { Content = instruction, MaxHeight = 90, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var footerCard = WpfFieldTheme.Card(footer, new Thickness(12));
            footerCard.Margin = new Thickness(0, 14, 0, 0);
            DockPanel.SetDock(footerCard, Dock.Bottom);
            root.Children.Add(footerCard);
            var workspace = new Grid();
            workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            workspace.ColumnDefinitions.Add(new ColumnDefinition());
            workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            root.Children.Add(workspace);
            window.SizeChanged += (s, e) => { workspace.ColumnDefinitions[0].Width = new GridLength(Math.Min(250, Math.Max(160, window.ActualWidth * .23))); workspace.ColumnDefinitions[2].Width = new GridLength(Math.Min(280, Math.Max(170, window.ActualWidth * .25))); };
            var tools = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            var toolScroll = new ScrollViewer { Content = tools, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            workspace.Children.Add(WpfFieldTheme.Card(toolScroll, new Thickness(14)));
            var sourceCanvas = new WpfZoneCanvas(editor, true);
            var targetCanvas = new WpfZoneCanvas(editor, false);
            Action redraw = () => { sourceCanvas.Redraw(); targetCanvas.Redraw(); };
            bool syncing = false;
            var buttons = new List<KeyValuePair<Button, System.Windows.Forms.Control>>();
            Func<string, Button> action = key => { var native = editor.WpfAction(key); var button = WpfFieldTheme.Button(Localization.Get(key), () => { if (key == "ZeZoneColour") editor.WpfChooseOutlineColour(); else editor.WpfClick(native); redraw(); }); button.Tag = key; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.MinHeight = 34; button.Padding = new Thickness(12, 6, 12, 6); button.Margin = new Thickness(0, 4, 0, 4); button.FontSize = 13.5; buttons.Add(new KeyValuePair<Button, System.Windows.Forms.Control>(button, native)); return button; };
            tools.Children.Add(WpfFieldTheme.SectionHeader("01  " + Localization.Get("ZeImportTitle")));
            foreach (var key in new[] { "ZeOpenScreenshot", "ZeImportFile", "ZeExport", "ZeManualFallback" })
                tools.Children.Add(action(key));
            tools.Children.Add(WpfFieldTheme.Text(Localization.Get("ZeLayer"), 12, WpfFieldTheme.Muted));
            var layer = new ComboBox { Margin = new Thickness(0, 5, 0, 6) };
            layer.SelectionChanged += (s, e) => { if (!syncing && layer.SelectedIndex >= 0) { editor.WpfLayer.SelectedIndex = layer.SelectedIndex; redraw(); } };
            tools.Children.Add(layer);
            tools.Children.Add(action("ZeNewLayer"));
            tools.Children.Add(action("ZeDeleteLayer"));
            tools.Children.Add(WpfFieldTheme.SectionHeader("02  " + Localization.Get("HeaderMapZones")));
            var drawButtons = new List<Button>();
            for (int i = 0; i < editor.WpfDrawingTool.Items.Count; i++) {
                int tool = i;
                var button = WpfFieldTheme.Button(editor.WpfDrawingTool.Items[i].ToString(), () => { editor.WpfDrawingTool.SelectedIndex = tool; redraw(); });
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.MinHeight = 34;
                button.Padding = new Thickness(10, 5, 10, 5);
                button.Margin = new Thickness(0, 4, 0, 4);
                drawButtons.Add(button);
                tools.Children.Add(button);
            }
            foreach (var key in new[] { "ZeUndoPoint", "ZeClearDraft", "ControlResetZoom" })
                tools.Children.Add(action(key));
            var mapGrid = new Grid { Margin = new Thickness(14, 0, 14, 0) };
            mapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
            mapGrid.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(mapGrid, 1);
            workspace.Children.Add(mapGrid);
            var sourcePanel = new DockPanel();
            var sourceTitle = WpfFieldTheme.Text(Localization.Get("ZeOpenScreenshot"), 14, WpfFieldTheme.Muted);
            DockPanel.SetDock(sourceTitle, Dock.Top);
            sourcePanel.Children.Add(sourceTitle);
            sourcePanel.Children.Add(sourceCanvas);
            mapGrid.Children.Add(sourcePanel);
            var targetPanel = new DockPanel();
            var mapTitle = WpfFieldTheme.Text(Localization.Get("HeaderMapZones"), 16, null, true);
            DockPanel.SetDock(mapTitle, Dock.Top);
            targetPanel.Children.Add(mapTitle);
            targetPanel.Children.Add(targetCanvas);
            Grid.SetColumn(targetPanel, 1);
            mapGrid.Children.Add(targetPanel);
            var review = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            var reviewScroll = new ScrollViewer { Content = review, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var reviewCard = WpfFieldTheme.Card(reviewScroll, new Thickness(14));
            Grid.SetColumn(reviewCard, 2);
            workspace.Children.Add(reviewCard);
            review.Children.Add(WpfFieldTheme.SectionHeader("03  " + Localization.Get("ZeFilterZones")));
            var filter = new TextBox { Text = editor.WpfFilter.Text, Margin = new Thickness(0, 8, 0, 8) };
            filter.TextChanged += (s, e) => { if (!syncing) editor.WpfFilter.Text = filter.Text; };
            review.Children.Add(filter);
            var zoneTypes = Enum.GetValues<ZoneCategory>();
            var typeKeys = new[] { "ZeTypeCity", "ZeTypeTown", "ZeTypeFarm", "ZeTypeTrader", "ZeTypeFaction", "ZeTypeMilitary", "ZeTypeBunker", "ZeTypeGasStation", "ZeTypeCustom", "ZeTypeUnknown" };
            var typeFilter = new ComboBox { Tag = "zone-type-filter", ItemsSource = new[] { Localization.Get("ZeAllTypes") }.Concat(typeKeys.Select(Localization.Get)).ToArray(), SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 8) };
            typeFilter.SelectionChanged += (s, e) => { if (!syncing) { editor.WpfCategoryFilter = typeFilter.SelectedIndex <= 0 ? (ZoneCategory?)null : zoneTypes[typeFilter.SelectedIndex - 1]; redraw(); } };
            review.Children.Add(typeFilter);
            var selectionHelp = WpfFieldTheme.Text(Localization.Get("ZeMultiHelp"), 12, WpfFieldTheme.Muted);
            selectionHelp.TextWrapping = TextWrapping.Wrap;
            review.Children.Add(selectionHelp);
            var zonesList = new ListBox { Tag = "zone-selection-list", SelectionMode = SelectionMode.Extended, MinHeight = 160, MaxHeight = 260 };
            zonesList.SelectionChanged += (s, e) => { if (!syncing) { editor.WpfSelectVisible(zonesList.SelectedItems.Cast<object>().Select(item => zonesList.Items.IndexOf(item)).ToArray()); redraw(); } };
            review.Children.Add(zonesList);
            var selectAll = WpfFieldTheme.Button(Localization.Get("SelectAll"), () => { zonesList.SelectAll(); });
            selectAll.Tag = "zone-select-all";
            selectAll.MinHeight = 34;
            selectAll.Margin = new Thickness(0, 6, 0, 4);
            review.Children.Add(selectAll);
            var selectColour = WpfFieldTheme.Button(Localization.Get("ZeSelectSameColour"), () => { editor.WpfSelectSameColour(); redraw(); });
            selectColour.Tag = "zone-select-colour";
            selectColour.MinHeight = 34;
            selectColour.Margin = new Thickness(0, 4, 0, 6);
            review.Children.Add(selectColour);
            var details = WpfFieldTheme.Text("", 13, WpfFieldTheme.Muted);
            details.TextWrapping = TextWrapping.Wrap;
            details.Margin = new Thickness(0, 8, 0, 8);
            review.Children.Add(details);
            var zoneName = new TextBox { Text = editor.WpfName.Text, MaxLength = 80, Margin = new Thickness(0, 8, 0, 8) };
            zoneName.TextChanged += (s, e) => { if (!syncing) editor.WpfName.Text = zoneName.Text; };
            zoneName.PreviewKeyDown += (s, e) => { if (e.Key == Key.Enter) { editor.WpfNameEnter(); redraw(); e.Handled = true; } };
            review.Children.Add(zoneName);
            review.Children.Add(action("ZeZoneColour"));
            var fillColour = WpfFieldTheme.Button(Localization.Get("ZoneFillColour"), () => { editor.WpfChooseFillColour(); redraw(); });
            fillColour.HorizontalAlignment = HorizontalAlignment.Stretch;
            fillColour.Margin = new Thickness(0, 8, 0, 4);
            review.Children.Add(fillColour);
            var fillOpacityLabel = WpfFieldTheme.Text("", 13, WpfFieldTheme.Muted);
            review.Children.Add(fillOpacityLabel);
            var fillOpacity = new Slider { Minimum = 0, Maximum = 100, Value = 16, IsSnapToTickEnabled = true, TickFrequency = 1, Margin = new Thickness(0, 4, 0, 8) };
            fillOpacity.ValueChanged += (s, e) => { if (!syncing) { editor.WpfSetFillOpacity((int)fillOpacity.Value); redraw(); } };
            review.Children.Add(fillOpacity);
            foreach (var key in new[] { "ZeAddZone", "ZeApplyName", "ZeDuplicate", "ZeMoveUp", "ZeMoveDown", "ZeDeselect", "ZeDeleteSelected", "ZeDeleteAll" })
                review.Children.Add(action(key));
            var nativeShape = editor.WpfAction("ZoneShape") as System.Windows.Forms.ComboBox;
            var shape = new ComboBox { ItemsSource = nativeShape.Items.Cast<object>().ToArray(), SelectedIndex = 0, Margin = new Thickness(0, 8, 0, 8) };
            shape.SelectionChanged += (s, e) => { if (shape.SelectedIndex >= 0) nativeShape.SelectedIndex = shape.SelectedIndex; };
            review.Children.Add(shape);
            review.Children.Add(action("ZeChangeShape"));
            var fill = new CheckBox { Content = Localization.Get("ZeFillZones"), IsChecked = true };
            fill.Checked += (s, e) => { var native = editor.WpfAction("ZeFillZones") as System.Windows.Forms.CheckBox; if (native != null) native.Checked = true; redraw(); };
            fill.Unchecked += (s, e) => { var native = editor.WpfAction("ZeFillZones") as System.Windows.Forms.CheckBox; if (native != null) native.Checked = false; redraw(); };
            review.Children.Add(fill);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            string previousInstruction = null, previousDetails = null;
            int lastSyncRevision = -1;
            bool lastSyncBusy = false;
            int lastOutlineRgb = -1, lastFillRgb = -1;
            SolidColorBrush cachedOutlineBrush = null, cachedFillBrush = null;
            Action<bool> sync = force => {
                if (editor.IsDisposed)
                    return;
                string curInstr = editor.WpfInstruction, curDet = editor.WpfDetails;
                bool curBusy = editor.WpfBusy;
                if (!force && editor.WpfRevision == lastSyncRevision && curBusy == lastSyncBusy && curInstr == previousInstruction && curDet == previousDetails)
                    return;
                lastSyncRevision = editor.WpfRevision;
                lastSyncBusy = curBusy;
                syncing = true;
                try {
                    var layers = editor.WpfLayer.Items.Cast<object>().ToArray();
                    if (!layer.Items.Cast<object>().SequenceEqual(layers))
                        layer.ItemsSource = layers;
                    layer.SelectedIndex = editor.WpfLayer.SelectedIndex;
                    var items = editor.WpfList.Items.Cast<object>().ToArray();
                    if (!zonesList.Items.Cast<object>().SequenceEqual(items))
                        zonesList.ItemsSource = items;
                    var selected = editor.WpfSelectedVisible;
                    if (!zonesList.SelectedItems.Cast<object>().Select(item => zonesList.Items.IndexOf(item)).OrderBy(i => i).SequenceEqual(selected)) {
                        zonesList.UnselectAll();
                        foreach (int i in selected)
                            zonesList.SelectedItems.Add(zonesList.Items[i]);
                    }
                    if (zoneName.Text != editor.WpfName.Text)
                        zoneName.Text = editor.WpfName.Text;
                    instruction.Text = curInstr;
                    details.Text = curDet;
                    if (previousInstruction != curInstr || previousDetails != curDet || curBusy)
                        redraw();
                    previousInstruction = curInstr;
                    previousDetails = curDet;
                    mapGrid.ColumnDefinitions[0].Width = editor.WpfHasScreenshot ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                    sourcePanel.Visibility = editor.WpfHasScreenshot ? Visibility.Visible : Visibility.Collapsed;
                    foreach (var pair in buttons) {
                        pair.Key.IsEnabled = pair.Value != null && !pair.Value.IsDisposed && pair.Value.Enabled && !curBusy;
                        if ((string)pair.Key.Tag == "ZeZoneColour") {
                            var colour = editor.WpfColour;
                            int rgb = (colour.R << 16) | (colour.G << 8) | colour.B;
                            if (cachedOutlineBrush == null || lastOutlineRgb != rgb) {
                                lastOutlineRgb = rgb;
                                cachedOutlineBrush = new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B));
                                cachedOutlineBrush.Freeze();
                            }
                            pair.Key.Background = cachedOutlineBrush;
                            pair.Key.Foreground = ColourButtonText(Color.FromRgb(colour.R, colour.G, colour.B));
                        }
                    }
                    for (int i = 0; i < drawButtons.Count; i++) {
                        bool selTool = i == editor.WpfDrawingTool.SelectedIndex;
                        drawButtons[i].SetResourceReference(Control.BackgroundProperty, selTool ? "ThemeAmber" : "ThemeRaised");
                        drawButtons[i].SetResourceReference(Control.ForegroundProperty, selTool ? "ThemePrimaryInk" : "ThemeInk");
                        drawButtons[i].IsEnabled = !curBusy && ((i != 3 && i != 5) || selected.Length <= 1);
                    }
                    shape.IsEnabled = !curBusy && selected.Length == 1;
                    foreach (var p in buttons)
                    if ((string)p.Key.Tag == "ZeChangeShape") {
                        p.Key.IsEnabled = shape.IsEnabled;
                        break;
                    }
                    save.IsEnabled = !curBusy;
                    zoneName.IsEnabled = filter.IsEnabled = layer.IsEnabled = zonesList.IsEnabled = typeFilter.IsEnabled = selectAll.IsEnabled = selectColour.IsEnabled = !curBusy;
                    foreach (var pair in buttons.Where(p => (string)p.Key.Tag == "ZeMoveUp" || (string)p.Key.Tag == "ZeMoveDown"))
                        pair.Key.IsEnabled = !curBusy && selected.Length == 1;
                    var fillColor = editor.WpfFillColour;
                    int frgb = (fillColor.R << 16) | (fillColor.G << 8) | fillColor.B;
                    if (cachedFillBrush == null || lastFillRgb != frgb) {
                        lastFillRgb = frgb;
                        cachedFillBrush = new SolidColorBrush(Color.FromRgb(fillColor.R, fillColor.G, fillColor.B));
                        cachedFillBrush.Freeze();
                    }
                    fillColour.Background = cachedFillBrush;
                    fillColour.Foreground = ColourButtonText(Color.FromRgb(fillColor.R, fillColor.G, fillColor.B));
                    fillOpacity.Value = Math.Round(fillColor.A * 100.0 / 255);
                    fillOpacityLabel.Text = Localization.Get("ZoneFillOpacity") + " · " + fillOpacity.Value + "%";
                    fillColour.IsEnabled = fillOpacity.IsEnabled = !curBusy;
                } finally { syncing = false; }
            };
            timer.Tick += (s, e) => { if (window.IsVisible) sync(false); };
            editor.FormClosed += (s, e) => { if (window.IsVisible) window.Close(); };
            window.PreviewKeyDown += (s, e) => { if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && !(Keyboard.FocusedElement is TextBox)) { editor.WpfClick(editor.WpfAction("ZeUndoPoint")); redraw(); e.Handled = true; } };
            window.Loaded += async (s, e) => { try { timer.Start(); if (!string.IsNullOrEmpty(file)) await editor.ImportAutomatic(file); } catch (Exception ex) { Program.LogException("ZoneWizardAutoImport", ex); if (!disposed && window.IsVisible) ShowFluentNotice(window, ex.Message, Localization.Get("ZoneEditorTitle"), true); } };
            window.Closing += (s, e) => { if (editor.WpfBusy) e.Cancel = true; };
            window.Closed += (s, e) => { timer.Stop(); editor.Close(); editor.Dispose(); Refresh(); };
            WpfFieldTheme.ApplyFluentShell(window, "ZiloreMiniMap — " + Localization.Get("ZoneEditorTitle"), root);
            sync(true);
            return window;
        }
        Window BuildOnboarding(int initial) {
            var window = Window(Localization.Get("WizardTitle"), 1120, 730);
            window.MinWidth = 680;
            window.MinHeight = 480;
            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(275) });
            layout.ColumnDefinitions.Add(new ColumnDefinition());
            var rail = new DockPanel { LastChildFill = true };
            rail.SetResourceReference(Panel.BackgroundProperty, "ThemePanel");
            var railBorder = new Border { BorderThickness = new Thickness(0, 0, 1, 0), Child = rail };
            railBorder.SetResourceReference(Border.BorderBrushProperty, "ThemeLine");

            var brand = new StackPanel { Margin = new Thickness(22, 22, 18, 18) };
            var brandTitleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            brandTitleRow.Children.Add(WpfFieldTheme.Text("ZILORE", 25, null, true));
            var grandTag = WpfFieldTheme.Badge(WpfFieldTheme.Text("GRAND", 11, WpfFieldTheme.Amber, true), new Thickness(8, 2, 8, 2));
            grandTag.Margin = new Thickness(8, 2, 0, 0);
            grandTag.VerticalAlignment = VerticalAlignment.Center;
            brandTitleRow.Children.Add(grandTag);
            brand.Children.Add(brandTitleRow);
            brand.Children.Add(WpfFieldTheme.Text(Localization.Get("WpfGettingStarted"), 11, WpfFieldTheme.Muted, true));
            DockPanel.SetDock(brand, Dock.Top);
            rail.Children.Add(brand);

            Action draw = null;
            var language = new ComboBox { ItemsSource = Localization.LanguageDisplayNames, SelectedIndex = (int)Localization.Current, Margin = new Thickness(18, 10, 18, 18) };
            language.SelectionChanged += (s, e) => { engine.WpfSetLanguage(Localization.LanguageAt(language.SelectedIndex)); filterIdentity = null; Refresh(); if (draw != null) draw(); };
            DockPanel.SetDock(language, Dock.Bottom);
            rail.Children.Add(language);

            // GRAND banner + Belarus badge card
            var grandRailPanel = new StackPanel();
            var grandRailImg = WpfFieldTheme.LoadEmbeddedImage("grand-banner.png");
            if (grandRailImg != null) {
                var grandImgCtrl = new System.Windows.Controls.Image { Source = grandRailImg, Height = 34, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 7) };
                RenderOptions.SetBitmapScalingMode(grandImgCtrl, BitmapScalingMode.HighQuality);
                grandRailPanel.Children.Add(grandImgCtrl);
            }
            var byRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var byFlag = WpfFieldTheme.LoadEmbeddedImage("flag-by.png");
            if (byFlag != null) {
                var flagCtrl = new System.Windows.Controls.Image { Source = byFlag, Width = 22, Height = 11, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                RenderOptions.SetBitmapScalingMode(flagCtrl, BitmapScalingMode.HighQuality);
                byRow.Children.Add(flagCtrl);
            }
            var byLbl = WpfFieldTheme.Text(WpfFieldTheme.UiText("OnboardingBelarusBadge"), 11, WpfFieldTheme.Muted, true);
            byLbl.TextWrapping = TextWrapping.Wrap;
            byRow.Children.Add(byLbl);
            grandRailPanel.Children.Add(byRow);
            var grandRailCard = WpfFieldTheme.Card(grandRailPanel, new Thickness(10, 9, 10, 9));
            grandRailCard.Margin = new Thickness(18, 10, 18, 0);
            DockPanel.SetDock(grandRailCard, Dock.Bottom);
            rail.Children.Add(grandRailCard);
            var guideHotkeysBtn = WpfFieldTheme.Button(WpfFieldTheme.UiText("HotkeysGuideBtn"), () => ShowHotkeysAndRoutesGuide(0));
            guideHotkeysBtn.Tag = "setup-hotkeys-guide";
            guideHotkeysBtn.MinHeight = 34;
            guideHotkeysBtn.FontSize = 12.5;
            guideHotkeysBtn.Margin = new Thickness(18, 8, 18, 0);
            DockPanel.SetDock(guideHotkeysBtn, Dock.Bottom);
            rail.Children.Add(guideHotkeysBtn);

            var steps = new StackPanel { Margin = new Thickness(14, 4, 14, 0) };
            rail.Children.Add(new ScrollViewer { Content = steps, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            layout.Children.Add(railBorder);

            var right = new DockPanel { Margin = new Thickness(32, 22, 26, 20) };
            Grid.SetColumn(right, 1);
            layout.Children.Add(right);
            var footerSep = new Border { Height = 1, Margin = new Thickness(0, 12, 0, 14) };
            footerSep.SetResourceReference(Border.BackgroundProperty, "ThemeLine");
            var footer = new DockPanel();
            var footerWrap = new StackPanel();
            footerWrap.Children.Add(footerSep);
            footerWrap.Children.Add(footer);
            DockPanel.SetDock(footerWrap, Dock.Bottom);
            right.Children.Add(footerWrap);

            var content = new StackPanel { Margin = new Thickness(0, 0, 14, 12) };
            var pageScroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            right.Children.Add(pageScroll);
            window.SizeChanged += (s, e) => { bool narrow = window.ActualWidth < 900; layout.ColumnDefinitions[0].Width = new GridLength(narrow ? 210 : 275); right.Margin = new Thickness(narrow ? 16 : 32, 18, narrow ? 14 : 26, 18); };

            int step = Math.Max(0, Math.Min(4, initial)), visited = step;
            var checklist = new GuideChecklistState();
            bool guideReady = false;
            Button wizardNext = null;
            int pageGeneration = 0;

            draw = () => {
                bool ru = Localization.Current == AppLanguage.Russian;
                pageScroll.ScrollToTop();
                int drawing = ++pageGeneration;
                window.Title = CleanWizardTitle(Localization.Get("WizardTitle"));
                ((TextBlock)brand.Children[1]).Text = Localization.Get("WpfGettingStarted");
                byLbl.Text = WpfFieldTheme.UiText("OnboardingBelarusBadge");
                guideHotkeysBtn.Content = WpfFieldTheme.UiText("HotkeysGuideBtn");
                setupPreview = null;
                wizardNext = null;
                content.Children.Clear();
                footer.Children.Clear();
                steps.Children.Clear();
                string[] stages = { "FirstTimeGuideTitle", "FieldAppearance", "SecMapZones", "SecTrackingZoom", "PrivatePositions" };
                visited = Math.Max(visited, step);

                for (int i = 0; i < stages.Length; i++) {
                    int index = i;
                    Action navigate = () => { if (drawing != pageGeneration || index > visited || (step == 0 && !guideReady && index > 0)) return; step = index; draw(); };
                    string cleanTitle = RxWizardStageNumber().Replace(Localization.Get(stages[i]), "");
                    var stepRow = new DockPanel();
                    var indicator = new Border { Width = 3.5, Height = 22, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Background = i == step ? WpfFieldTheme.Amber : Brushes.Transparent };
                    DockPanel.SetDock(indicator, Dock.Left);
                    stepRow.Children.Add(indicator);
                    var numCircle = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Background = i == step ? WpfFieldTheme.Amber : (i < step ? WpfFieldTheme.Cyan : WpfFieldTheme.Raised) };
                    var numTb = new TextBlock { Text = i < step ? "✓" : (i + 1).ToString(CultureInfo.InvariantCulture), FontSize = 12, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    numTb.SetResourceReference(TextBlock.ForegroundProperty, i <= step ? "ThemePrimaryInk" : "ThemeMuted");
                    numCircle.Child = numTb;
                    DockPanel.SetDock(numCircle, Dock.Left);
                    stepRow.Children.Add(numCircle);
                    var titleTb = new TextBlock { Text = cleanTitle, TextWrapping = TextWrapping.Wrap, FontSize = 14, FontWeight = i == step ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
                    titleTb.SetResourceReference(TextBlock.ForegroundProperty, i == step ? "ThemeInk" : "ThemeMuted");
                    stepRow.Children.Add(titleTb);
                    var button = WpfFieldTheme.Button("", navigate);
                    button.Content = stepRow;
                    button.IsEnabled = i <= visited;
                    if (i == step) {
                        button.SetResourceReference(Control.BackgroundProperty, "ThemeRaised");
                        button.BorderThickness = new Thickness(1);
                    } else {
                        button.Background = Brushes.Transparent;
                        button.BorderThickness = new Thickness(0);
                    }
                    button.HorizontalContentAlignment = HorizontalAlignment.Left;
                    button.Padding = new Thickness(6, 8, 10, 8);
                    button.MinHeight = 44;
                    button.Margin = new Thickness(0, 2, 0, 2);
                    steps.Children.Add(button);
                }

                // Step Header + Visual Progress Bar
                var stepTopDock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var stepPill = WpfFieldTheme.Badge(WpfFieldTheme.Text(Localization.T("WizardStepIndicator", step + 1, stages.Length), 11.5, WpfFieldTheme.Amber, true), new Thickness(8, 2, 8, 2));
                DockPanel.SetDock(stepPill, Dock.Left);
                stepTopDock.Children.Add(stepPill);
                var wizProg = new ProgressBar { Minimum = 0, Maximum = stages.Length, Value = step + 1, Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Tag = "setup-progress" };
                stepTopDock.Children.Add(wizProg);
                content.Children.Add(stepTopDock);
                string stageHeading = RxWizardStageNumber().Replace(Localization.Get(stages[step]), "");
                content.Children.Add(WpfFieldTheme.Text(stageHeading, 24, null, true));

                if (step == 0) {
                    content.Children.Add(BuildGuideChecklist(checklist, ready => { guideReady = ready; if (wizardNext != null) wizardNext.IsEnabled = ready; }));
                } else if (step == 1) {
                    var intro = WpfFieldTheme.Text(Localization.Get("SetupAppearanceHelp"), 14, WpfFieldTheme.Muted);
                    intro.TextWrapping = TextWrapping.Wrap;
                    intro.Margin = new Thickness(0, 8, 0, 14);
                    content.Children.Add(intro);

                    // Live Theme Switcher Card
                    string[] themeCodes = { "System", "Dark", "OLED", "Light" };
                    string[] themeLabels = { WpfFieldTheme.UiText("ThemeSystem"), WpfFieldTheme.UiText("ThemeDark"), WpfFieldTheme.UiText("ThemeOled"), WpfFieldTheme.UiText("ThemeLight") };
                    int curThemeIdx = Math.Max(0, Array.FindIndex(themeCodes, t => string.Equals(t, WpfFieldTheme.ActiveTheme, StringComparison.OrdinalIgnoreCase)));
                    var themeSeg = WpfFieldTheme.SegmentedControl(themeLabels, curThemeIdx, idx => { ApplyUiTheme(themeCodes[idx]); draw(); }, "setup-theme-");
                    content.Children.Add(WpfFieldTheme.SettingRow(WpfFieldTheme.UiText("UiThemeSection"), WpfFieldTheme.UiText("UiThemeHint"), themeSeg, true));

                    var appearance = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                    appearance.ColumnDefinitions.Add(new ColumnDefinition());
                    appearance.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                    appearance.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(246) });

                    var controls = new StackPanel();
                    controls.Children.Add(WpfNumber(Localization.Get("MapWidth"), 240, 800, engine.WpfHudWidth, 10, v => engine.WpfHudWidth = v));
                    controls.Children.Add(WpfNumber(Localization.Get("MapHeight"), 240, 800, engine.WpfHudHeight, 10, v => engine.WpfHudHeight = v));
                    controls.Children.Add(WpfNumber(Localization.Get("MapOpacity"), 30, 100, engine.WpfMapOpacity, 5, v => engine.WpfMapOpacity = v));
                    AddAppearanceControls(controls, engine.WpfSnapshot(false));
                    bool hiResSetup = HasHighResMonitor();
                    string hwLbl = ru ? (hiResSetup ? "Аппаратное ускорение карты M (GPU · рекомендуется для 2K/4K)" : "Аппаратное ускорение карты M (GPU / 2K–4K)") : (hiResSetup ? "Hardware-accelerated M map (GPU · Recommended for 2K/4K)" : "Hardware-accelerated M map (GPU / 2K–4K)");
                    var hwSetupBox = WpfCheck(hwLbl, HardwareAccelMapEnabled, v => { HardwareAccelMapEnabled = v; });
                    hwSetupBox.ToolTip = ru ? "Включает DirectTerrain GPU для плавного 144 Гц зума (применится после перезапуска)" : "Enables DirectTerrain GPU for smooth 144Hz zoom (applies after restart)";
                    controls.Children.Add(hwSetupBox);
                    var ctrlCard = WpfFieldTheme.Card(controls, new Thickness(14, 12, 14, 12));
                    Grid.SetColumn(ctrlCard, 0);
                    appearance.Children.Add(ctrlCard);

                    var prevStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                    var prevTitle = WpfFieldTheme.Text(ru ? "Предпросмотр HUD" : "HUD Live Preview", 13, WpfFieldTheme.Muted, true);
                    prevTitle.HorizontalAlignment = HorizontalAlignment.Center;
                    prevTitle.Margin = new Thickness(0, 0, 0, 8);
                    prevStack.Children.Add(prevTitle);
                    var preview = new WpfMapSurface { Compact = true, BaseMap = geography, Terrain = terrain, Width = 206, Height = 206, Tag = "setup-preview" };
                    prevStack.Children.Add(preview);
                    var moveBtn = WpfFieldTheme.Button(Localization.Get("WpfMoveMinimap"), ShowMoveHud);
                    moveBtn.MinHeight = 34;
                    moveBtn.FontSize = 13;
                    moveBtn.Margin = new Thickness(0, 12, 0, 0);
                    prevStack.Children.Add(moveBtn);
                    var prevCard = WpfFieldTheme.Card(prevStack, new Thickness(14, 12, 14, 14));
                    prevCard.VerticalAlignment = VerticalAlignment.Top;
                    Grid.SetColumn(prevCard, 2);
                    appearance.Children.Add(prevCard);
                    content.Children.Add(appearance);
                    setupPreview = preview;
                } else if (step == 2) {
                    var intro = WpfFieldTheme.Text(Localization.Get("SetupMapHelp"), 14, WpfFieldTheme.Muted);
                    intro.TextWrapping = TextWrapping.Wrap;
                    intro.Margin = new Thickness(0, 8, 0, 14);
                    content.Children.Add(intro);
                    var mapStack = new StackPanel();
                    mapStack.Children.Add(WpfCheck(Localization.Get("GridLabels"), engine.WpfGridLabels, v => engine.WpfGridLabels = v));
                    mapStack.Children.Add(WpfCheck(Localization.Get("GridBorders"), engine.WpfGridBorders, v => engine.WpfGridBorders = v));
                    mapStack.Children.Add(WpfCheck(Localization.Get("ShowScumMap"), engine.WpfShowScumMap, v => engine.WpfShowScumMap = v));
                    mapStack.Children.Add(WpfCheck(Localization.Get("ShowSavedZones"), engine.WpfShowZones, v => engine.WpfShowZones = v));
                    mapStack.Children.Add(WpfCheck(WpfFieldTheme.UiText("MinimapPoiClustersTitle"), WpfMapSurface.MinimapPoiClusteringEnabled, v => { WpfMapSurface.MinimapPoiClusteringEnabled = v; WpfMapSurface.SaveFlightUiSettings(); Refresh(); }));
                    content.Children.Add(WpfFieldTheme.Card(mapStack, new Thickness(14, 10, 14, 10)));

                    var poiBtn = WpfFieldTheme.Button(Localization.Get("ConfigurePoiFilters"), () => { if (PoiAccessGuard.IsUnlocked || PromptPoiAccessCode()) Modal(BuildPoiFilters()); });
                    poiBtn.HorizontalAlignment = HorizontalAlignment.Left;
                    poiBtn.MinHeight = 36;
                    poiBtn.FontSize = 13.5;
                    poiBtn.Margin = new Thickness(0, 12, 0, 4);
                    content.Children.Add(poiBtn);

                    var moreMap = new StackPanel();
                    moreMap.Children.Add(WpfColorSwatch(Localization.Get("RouteGuidanceColor"), () => engine.WpfRouteColor, engine.WpfPickRouteColor));
                    moreMap.Children.Add(WpfColorSwatch(Localization.Get("PlayerMarkerColor"), () => engine.WpfPlayerColor, engine.WpfPickPlayerColor));
                    var defaults = WpfFieldTheme.Button(Localization.Get("SetupDefaultColours"), () => { engine.WpfDefaultColours(); draw(); Refresh(); });
                    defaults.Tag = "setup-default-colours";
                    defaults.HorizontalAlignment = HorizontalAlignment.Left;
                    moreMap.Children.Add(defaults);
                    content.Children.Add(new Expander { Header = WpfFieldTheme.Text(Localization.Get("SetupMoreOptions"), 15, WpfFieldTheme.Ink, true), Content = moreMap, IsExpanded = false, Margin = new Thickness(0, 10, 0, 4), Tag = "setup-more-options" });
                } else if (step == 3) {
                    var intro = WpfFieldTheme.Text(Localization.Get("SetupTrackingHelp"), 14, WpfFieldTheme.Muted);
                    intro.TextWrapping = TextWrapping.Wrap;
                    intro.Margin = new Thickness(0, 8, 0, 14);
                    content.Children.Add(intro);
                    var trackStack = new StackPanel();
                    trackStack.Children.Add(WpfCheck(Localization.Get("AutoZoomSpeed"), engine.WpfAutoZoom, v => engine.WpfAutoZoom = v));
                    trackStack.Children.Add(WpfNumber(Localization.Get("MaxZoom"), 4, 32, engine.WpfMaxZoom, 1, v => engine.WpfMaxZoom = v));
                    content.Children.Add(WpfFieldTheme.Card(trackStack, new Thickness(14, 10, 14, 10)));

                    var avCheck = new CheckBox { IsChecked = WpfMapSurface.AutoDetectAviationEnabled, Width = 46 };
                    avCheck.Checked += (s, e) => { WpfMapSurface.AutoDetectAviationEnabled = true; WpfMapSurface.SaveFlightUiSettings(); Refresh(); };
                    avCheck.Unchecked += (s, e) => { WpfMapSurface.AutoDetectAviationEnabled = false; WpfMapSurface.SaveFlightUiSettings(); Refresh(); };
                    var avRow = WpfFieldTheme.SettingRow(WpfFieldTheme.UiText("AutoDetectAviationTitle"), WpfFieldTheme.UiText("AutoDetectAviationSub"), avCheck, true);
                    avRow.Margin = new Thickness(0, 12, 0, 4);
                    content.Children.Add(avRow);

                    var moreTrack = new StackPanel();
                    moreTrack.Children.Add(WpfNumber(Localization.Get("ZoomStep"), 10, 100, engine.WpfZoomStepPercent, 5, v => engine.WpfZoomStepPercent = v));
                    moreTrack.Children.Add(WpfNumber(Localization.Get("PositionInterval"), engine.WpfCopyIntervalMin, 10000, engine.WpfCopyIntervalMs, 50, v => engine.WpfCopyIntervalMs = v));
                    content.Children.Add(new Expander { Header = WpfFieldTheme.Text(Localization.Get("SetupMoreOptions"), 15, WpfFieldTheme.Ink, true), Content = moreTrack, IsExpanded = false, Margin = new Thickness(0, 10, 0, 4), Tag = "setup-more-options" });
                } else if (step == 4) {
                    var intro = WpfFieldTheme.Text(Localization.Get("SetupSharingHelp"), 14, WpfFieldTheme.Muted);
                    intro.TextWrapping = TextWrapping.Wrap;
                    intro.Margin = new Thickness(0, 8, 0, 14);
                    content.Children.Add(intro);

                    // Ready Summary Card
                    var snap = engine.WpfSnapshot(false);
                    string copyKeyTxt = (snap.CopyModifier == 0 ? "" : PhysicalKeyCapture.KeyName(snap.CopyModifier) + " + ") + PhysicalKeyCapture.KeyName(snap.CopyKey);
                    var sumGrid = new UniformGrid { Columns = 2 };
                    Action<string, string> addSum = (k, v) => {
                        var sp = new StackPanel { Margin = new Thickness(0, 4, 10, 4) };
                        sp.Children.Add(WpfFieldTheme.Text(k, 11.5, WpfFieldTheme.Muted, true));
                        sp.Children.Add(WpfFieldTheme.Text(v, 14, WpfFieldTheme.Cyan, true));
                        sumGrid.Children.Add(sp);
                    };
                    addSum(ru ? "Тема интерфейса" : "UI Theme", WpfFieldTheme.ActiveTheme);
                    addSum(ru ? "Клавиша координат" : "Copy Location Key", copyKeyTxt);
                    addSum(ru ? "Авто-зум" : "Auto-Zoom", engine.WpfAutoZoom ? (ru ? "Включён" : "Enabled") : (ru ? "Выключен" : "Disabled"));
                    addSum(ru ? "Авто-авиарежим" : "Auto Aviation", WpfMapSurface.AutoDetectAviationEnabled ? (ru ? "Включён (> 110 км/ч)" : "Enabled (> 110 km/h)") : (ru ? "Выключен" : "Disabled"));
                    var sumCard = WpfFieldTheme.Card(sumGrid, new Thickness(14, 10, 14, 10));
                    sumCard.SetResourceReference(Border.BackgroundProperty, "ThemeAccentCard");
                    sumCard.SetResourceReference(Border.BorderBrushProperty, "ThemeAccentBorder");
                    sumCard.Margin = new Thickness(0, 0, 0, 14);
                    content.Children.Add(sumCard);

                    var connection = WpfFieldTheme.Button(Localization.Get("PrivatePositions"), ShowPrivatePositions, true);
                    connection.Tag = "setup-private-positions";
                    connection.HorizontalAlignment = HorizontalAlignment.Left;
                    connection.MinHeight = 38;
                    content.Children.Add(connection);

                    var sharingDetails = new StackPanel();
                    foreach (string helpKey in new[] { "PrivateGroupHelp", "PrivatePositionGpsHelp", "PrivatePositionMarkersHelp", "SetupSharingVisibility" }) {
                        var help = WpfFieldTheme.Text(Localization.Get(helpKey), 13.5, WpfFieldTheme.Muted);
                        help.TextWrapping = TextWrapping.Wrap;
                        help.Margin = new Thickness(0, 8, 0, 0);
                        sharingDetails.Children.Add(help);
                    }
                    content.Children.Add(new Expander { Header = WpfFieldTheme.Text(Localization.Get("SetupMoreOptions"), 15, WpfFieldTheme.Ink, true), Content = sharingDetails, Margin = new Thickness(0, 12, 0, 0) });
                }

                // Footer buttons
                var next = WpfFieldTheme.Button(step == 4 ? (Localization.Get("Done") + " ✓") : (Localization.Get("WizardBtnNext")), () => {
                    if (drawing != pageGeneration || (step == 0 && !guideReady) || !ValidateNumericFields(window))
                        return;
                    if (step == 4) {
                        if (engine.WpfCompleteOnboarding())
                            window.Close();
                        else
                            ShowFluentNotice(window, Localization.Get("LocalSaveFailed"), Localization.Get("WizardTitle"), true);
                    } else {
                        step++;
                        draw();
                    }
                }, true);
                wizardNext = next;
                next.IsEnabled = step != 0 || guideReady;
                next.Tag = "setup-next";
                next.MinWidth = 130;
                next.MinHeight = 38;
                DockPanel.SetDock(next, Dock.Right);
                footer.Children.Add(next);

                if (step < 4) {
                    var skip = WpfFieldTheme.Button(ru ? "Пропустить" : "Skip", () => { engine.WpfCompleteOnboarding(); window.Close(); });
                    skip.MinWidth = 105;
                    skip.MinHeight = 38;
                    skip.Margin = new Thickness(0, 0, 8, 0);
                    DockPanel.SetDock(skip, Dock.Right);
                    footer.Children.Add(skip);
                }

                var back = WpfFieldTheme.Button(Localization.Get("WizardBtnPrev"), () => { step--; draw(); });
                back.IsEnabled = step > 0;
                back.MinWidth = 110;
                back.MinHeight = 38;
                back.HorizontalAlignment = HorizontalAlignment.Left;
                footer.Children.Add(back);
                RefreshSetupPreview();
            };
            draw();
            bool glassMode = true;
            Button glassToggle = null;
            Action ApplyOnboardingGlass = () => {
                bool isLight = string.Equals(WpfFieldTheme.ResolveTheme(WpfFieldTheme.ActiveTheme), "Light", StringComparison.OrdinalIgnoreCase);
                bool isOled = string.Equals(WpfFieldTheme.ResolveTheme(WpfFieldTheme.ActiveTheme), "OLED", StringComparison.OrdinalIgnoreCase);
                byte shellAlpha = (byte)(glassMode ? 196 : 250);
                byte railAlpha = (byte)(glassMode ? 148 : 248);
                var shellBg = new SolidColorBrush(isLight ? Color.FromArgb(shellAlpha, 243, 246, 250) : (isOled ? Color.FromArgb(shellAlpha, 4, 6, 10) : Color.FromArgb(shellAlpha, 11, 16, 23)));
                shellBg.Freeze();
                var railBg = new SolidColorBrush(isLight ? Color.FromArgb(railAlpha, 255, 255, 255) : (isOled ? Color.FromArgb(railAlpha, 9, 13, 20) : Color.FromArgb(railAlpha, 18, 25, 35)));
                railBg.Freeze();
                rail.Background = railBg;
                var shellBorder = window.Content as Border;
                if (shellBorder != null)
                    shellBorder.Background = shellBg;
                if (glassToggle != null)
                    glassToggle.Content = Localization.Current == AppLanguage.Russian ? (glassMode ? "👁 Стекло: ВКЛ" : "👁 Стекло: ВЫКЛ") : (glassMode ? "👁 Glass: ON" : "👁 Glass: OFF");
            };
            glassToggle = WpfFieldTheme.Button("", () => { glassMode = !glassMode; ApplyOnboardingGlass(); });
            glassToggle.MinHeight = 32;
            glassToggle.FontSize = 12;
            glassToggle.Padding = new Thickness(10, 4, 10, 4);
            glassToggle.Margin = new Thickness(18, 0, 18, 8);
            DockPanel.SetDock(glassToggle, Dock.Bottom);
            rail.Children.Insert(rail.Children.Count - 1, glassToggle);
            refreshOnboardingGlass = ApplyOnboardingGlass;
            window.Closed += (s, e) => { setupPreview = null; refreshOnboardingGlass = null; engine.WpfSave(); };
            WpfFieldTheme.ApplyFluentShell(window, CleanWizardTitle(Localization.Get("WizardTitle")), layout);
            language.SelectionChanged += (s, e) => ApplyOnboardingGlass();
            ApplyOnboardingGlass();
            RefreshSetupPreview();
            return window;
        }
        void CaptureKey(string title, Func<int, int, string> validate, bool unbind) {
            var window = Window(title, 460, 220);
            window.ResizeMode = ResizeMode.NoResize;
            var layout = new StackPanel { Margin = new Thickness(22) };
            var instruction = WpfFieldTheme.Text(Localization.Get("ShortcutPrompt"), 16);
            instruction.TextWrapping = TextWrapping.Wrap;
            layout.Children.Add(instruction);
            if (unbind) {
                var ub = WpfFieldTheme.Button(Localization.Get("ControlUnbind"), () => { string error = validate(0, 0); if (error == null) window.Close(); else instruction.Text = error; });
                ub.Margin = new Thickness(0, 14, 0, 0);
                layout.Children.Add(ub);
            }
            WpfFieldTheme.ApplyFluentShell(window, "ZiloreMiniMap — " + title, layout);
            window.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) return; e.Handled = true; if (Keyboard.Modifiers != ModifierKeys.None) { instruction.Text = Localization.Get("ShortcutSingleKey"); return; } int vk = KeyInterop.VirtualKeyFromKey(e.Key); int scan; if (!PhysicalKeyCapture.TryGet(vk, out scan)) { instruction.Text = Localization.Get("ShortcutRetry"); return; } string error = validate(vk, scan); if (error == null) window.Close(); else instruction.Text = error; };
            Modal(window);
        }
        void ShowAddWaypoint(DrawingPoint? point = null) {
            Modal(BuildAddWaypoint(point));
        }
        Window BuildAddWaypoint(DrawingPoint? point = null) {
            var window = Window(Localization.Get("MapSaveLocation"), 540, 360);
            var root = new DockPanel { Margin = new Thickness(24) };
            var content = new StackPanel();
            var name = new TextBox { MaxLength = 120, MinHeight = 44, Tag = "waypoint-name" };
            var prompt = new TextBlock { Text = Localization.Get("WaypointNamePrompt"), TextWrapping = TextWrapping.Wrap };
            var label = new Label { Content = prompt, Target = name, Foreground = WpfFieldTheme.Ink, FontSize = 18, Padding = new Thickness(0, 0, 0, 10) };
            content.Children.Add(label);
            content.Children.Add(name);
            System.Windows.Automation.AutomationProperties.SetLabeledBy(name, label);
            System.Windows.Automation.AutomationProperties.SetName(name, prompt.Text);
            bool available = point.HasValue || engine.WpfSnapshot(false).Position != null, saving = false, closed = false;
            if (!available) {
                var waiting = WpfFieldTheme.Text(engine.WpfTrackingStatus(), 16, WpfFieldTheme.Amber);
                waiting.TextWrapping = TextWrapping.Wrap;
                waiting.Margin = new Thickness(0, 12, 0, 0);
                content.Children.Add(waiting);
            }
            Button save = null;
            Action commit = () => {
                if (closed || saving || !available || string.IsNullOrWhiteSpace(name.Text))
                    return;
                saving = true;
                save.IsEnabled = false;
                try {
                    if (engine.WpfWaypoint(name.Text.Trim(), point)) {
                        Refresh();
                        window.Close();
                    }
                } finally { saving = false; if (!closed) { save.IsEnabled = available && !string.IsNullOrWhiteSpace(name.Text); name.Focus(); } }
            };
            var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var cancel = WpfFieldTheme.Button(Localization.Get("Cancel"), () => window.Close());
            cancel.Tag = "waypoint-cancel";
            actions.Children.Add(cancel);
            save = WpfFieldTheme.Button(Localization.Get("MapSaveLocation"), commit, true);
            save.Tag = "waypoint-save";
            save.IsEnabled = false;
            actions.Children.Add(save);
            name.TextChanged += (s, e) => save.IsEnabled = available && !saving && !string.IsNullOrWhiteSpace(name.Text);
            name.PreviewKeyDown += (s, e) => { if (e.Key == Key.Enter) { commit(); e.Handled = true; } };
            window.ContentRendered += (s, e) => { name.Focus(); name.SelectAll(); };
            window.Closed += (s, e) => closed = true;
            DockPanel.SetDock(actions, Dock.Bottom);
            root.Children.Add(actions);
            root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            WpfFieldTheme.ApplyFluentShell(window, "ZiloreMiniMap — " + Localization.Get("MapSaveLocation"), root);
            return window;
        }
    }
}
