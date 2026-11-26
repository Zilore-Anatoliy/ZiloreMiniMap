#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ZiloreMiniMap {
    internal sealed partial class WpfFieldConsoleRuntime {
        internal void PostShowUserGuide(int initialChapter = 0) {
            PostUi(() => ShowUserGuide(initialChapter));
        }

        internal void ShowUserGuide(int initialChapter = 0) {
            if (disposed) return;
            var win = BuildUserGuideWindow(initialChapter);
            Modal(win);
        }

        Window BuildUserGuideWindow(int initialChapter) {
            bool showRu = Localization.Current == AppLanguage.Russian;
            string startTitle = showRu ? "Руководство пользователя ZiloreMiniMap" : "ZiloreMiniMap User Guide";
            var win = Window(startTitle, 940, 680);
            win.MinWidth = 720;
            win.MinHeight = 520;

            var root = new DockPanel { Margin = new Thickness(18, 12, 18, 16) };

            // Top bar: title + RU/EN switcher + Illustrated Web Guide link
            var topDock = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var topRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(topRight, Dock.Right);

            var headingLbl = WpfFieldTheme.Text(startTitle, 17.5, WpfFieldTheme.Ink, true);
            var subtitleLbl = WpfFieldTheme.Text("", 12.5, WpfFieldTheme.Muted);
            subtitleLbl.Margin = new Thickness(0, 2, 0, 0);
            var titleStack = new StackPanel();
            titleStack.Children.Add(headingLbl);
            titleStack.Children.Add(subtitleLbl);

            // Footer bar
            var footerDock = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var footerLeft = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(footerLeft, Dock.Left);

            var webBtn = WpfFieldTheme.Button("", () => { _ = WpfFieldTheme.OpenShellUri(UserGuideContent.WebGuideUrl(showRu)); }, true);
            webBtn.MinHeight = 32;
            webBtn.Padding = new Thickness(12, 5, 12, 5);
            webBtn.Margin = new Thickness(0, 0, 8, 0);

            var eulaOpenBtn = WpfFieldTheme.Button("⚖ EULA", () => ShowEulaViewer());
            eulaOpenBtn.MinHeight = 34;
            eulaOpenBtn.Padding = new Thickness(12, 5, 12, 5);
            eulaOpenBtn.Margin = new Thickness(0, 0, 8, 0);

            var mailBtn = WpfFieldTheme.Button("✉ " + EulaAgreement.SupportEmail, () => {
                _ = WpfFieldTheme.OpenShellUri("mailto:" + EulaAgreement.SupportEmail + "?subject=ZiloreMiniMap%20Support");
            });
            mailBtn.MinHeight = 34;
            mailBtn.Padding = new Thickness(12, 5, 12, 5);
            mailBtn.Margin = new Thickness(0);

            footerLeft.Children.Add(eulaOpenBtn);
            footerLeft.Children.Add(mailBtn);

            var closeBtn = WpfFieldTheme.Button(showRu ? "Закрыть" : "Close", () => win.Close(), true);
            closeBtn.MinWidth = 115;
            closeBtn.MinHeight = 34;
            closeBtn.HorizontalAlignment = HorizontalAlignment.Right;
            DockPanel.SetDock(closeBtn, Dock.Right);

            footerDock.Children.Add(footerLeft);
            footerDock.Children.Add(closeBtn);
            DockPanel.SetDock(footerDock, Dock.Bottom);

            // Split workspace: Left (Search + Chapter List), Right (Formatted Reader)
            var bodyGrid = new Grid();
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(268) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftPanel = new DockPanel();
            var searchLabel = WpfFieldTheme.Text("", 11.5, WpfFieldTheme.Muted, true);
            searchLabel.Margin = new Thickness(2, 0, 0, 4);
            DockPanel.SetDock(searchLabel, Dock.Top);

            var searchBox = new TextBox {
                FontFamily = SystemTypography.WpfUiFontFamily,
                FontSize = 13,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 8)
            };
            SystemTypography.ApplyClearType(searchBox);
            DockPanel.SetDock(searchBox, Dock.Top);

            var chapterList = new ListBox {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(chapterList, ScrollBarVisibility.Disabled);

            leftPanel.Children.Add(searchLabel);
            leftPanel.Children.Add(searchBox);
            leftPanel.Children.Add(chapterList);
            var leftCard = WpfFieldTheme.Card(leftPanel, new Thickness(10));
            Grid.SetColumn(leftCard, 0);
            bodyGrid.Children.Add(leftCard);

            var readerStack = new StackPanel { Margin = new Thickness(4, 2, 10, 12) };
            var readerScroll = new ScrollViewer {
                Content = readerStack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var rightCard = WpfFieldTheme.Card(readerScroll, new Thickness(16, 14, 12, 14));
            Grid.SetColumn(rightCard, 2);
            bodyGrid.Children.Add(rightCard);

            int selectedChapterNum = Math.Max(1, initialChapter + 1);
            bool updatingList = false;

            Action<GuideChapter?> renderChapter = ch => {
                readerStack.Children.Clear();
                if (ch == null) {
                    var empty = WpfFieldTheme.Text(
                        showRu ? "По вашему запросу разделов не найдено. Попробуйте другое ключевое слово." : "No chapters matched your query. Try another keyword.",
                        13.5, WpfFieldTheme.Muted);
                    empty.TextWrapping = TextWrapping.Wrap;
                    readerStack.Children.Add(empty);
                    return;
                }

                var chHeader = WpfFieldTheme.Text(ch.Icon + "  " + ch.Number + ". " + ch.Title, 18, WpfFieldTheme.Ink, true);
                chHeader.TextWrapping = TextWrapping.Wrap;
                chHeader.Margin = new Thickness(0, 0, 0, 10);
                readerStack.Children.Add(chHeader);

                var divider = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 12), Background = WpfFieldTheme.Line };
                readerStack.Children.Add(divider);

                for (int i = 0; i < ch.Lines.Length; i++) {
                    string s = ch.Lines[i].Trim();
                    if (s.Length == 0) continue;

                    if (s.StartsWith("### ", StringComparison.Ordinal)) {
                        var h3 = WpfFieldTheme.Text(s.Substring(4).Trim(), 14.5, WpfFieldTheme.Cyan, true);
                        h3.TextWrapping = TextWrapping.Wrap;
                        h3.Margin = new Thickness(0, 12, 0, 5);
                        readerStack.Children.Add(h3);
                    } else if (s.StartsWith("- ", StringComparison.Ordinal)) {
                        var row = new DockPanel { Margin = new Thickness(4, 2, 0, 5) };
                        var bullet = WpfFieldTheme.Text("•", 13.5, WpfFieldTheme.Amber, true);
                        bullet.Margin = new Thickness(0, 0, 8, 0);
                        DockPanel.SetDock(bullet, Dock.Left);
                        row.Children.Add(bullet);
                        row.Children.Add(BuildRichTextBlock(s.Substring(2).Trim(), 13.2));
                        readerStack.Children.Add(row);
                    } else {
                        var p = BuildRichTextBlock(s, 13.4);
                        p.Margin = new Thickness(0, 2, 0, 8);
                        readerStack.Children.Add(p);
                    }
                }
                readerScroll.ScrollToTop();
            };

            Action populateChapters = () => {
                updatingList = true;
                try {
                    chapterList.Items.Clear();
                    var chapters = UserGuideContent.Search(searchBox.Text, showRu);
                    int selectIdx = -1;
                    for (int i = 0; i < chapters.Length; i++) {
                        var ch = chapters[i];
                        var itemText = WpfFieldTheme.Text(ch.Icon + "  " + ch.Number + ". " + ch.Title, 12.6, WpfFieldTheme.Ink);
                        itemText.TextWrapping = TextWrapping.Wrap;
                        var lbi = new ListBoxItem {
                            Content = itemText,
                            Tag = ch,
                            Padding = new Thickness(8, 7, 8, 7),
                            Margin = new Thickness(0, 1, 0, 1)
                        };
                        chapterList.Items.Add(lbi);
                        if (ch.Number == selectedChapterNum) selectIdx = i;
                    }
                    if (chapters.Length > 0) {
                        if (selectIdx < 0) selectIdx = 0;
                        chapterList.SelectedIndex = selectIdx;
                        selectedChapterNum = chapters[selectIdx].Number;
                        renderChapter(chapters[selectIdx]);
                    } else {
                        renderChapter(null);
                    }
                } finally {
                    updatingList = false;
                }
            };

            chapterList.SelectionChanged += (s, e) => {
                if (updatingList) return;
                if (chapterList.SelectedItem is ListBoxItem lbi && lbi.Tag is GuideChapter ch) {
                    selectedChapterNum = ch.Number;
                    renderChapter(ch);
                }
            };

            searchBox.TextChanged += (s, e) => populateChapters();

            Action refreshLanguage = () => {
                string hdr = showRu ? "Руководство пользователя ZiloreMiniMap" : "ZiloreMiniMap User Guide";
                win.Title = hdr;
                headingLbl.Text = hdr;
                subtitleLbl.Text = showRu
                    ? "Пошаговая инструкция по всем возможностям, настройкам и горячим клавишам (v" + MapWindow.VersionString + ")"
                    : "Complete step-by-step manual covering all features, settings, and hotkeys (v" + MapWindow.VersionString + ")";
                webBtn.Content = showRu ? "🌐 Версия со скриншотами ↗" : "🌐 Illustrated Web Guide ↗";
                searchLabel.Text = showRu ? "ПОИСК ПО РУКОВОДСТВУ" : "SEARCH GUIDE";
                closeBtn.Content = showRu ? "Закрыть" : "Close";
                eulaOpenBtn.Content = showRu ? "⚖ Лицензия (EULA)" : "⚖ License (EULA)";
                populateChapters();
            };

            var ruBtn = WpfFieldTheme.Button("RU", () => { showRu = true; refreshLanguage(); });
            ruBtn.MinHeight = 32;
            ruBtn.MinWidth = 44;
            ruBtn.Padding = new Thickness(8, 4, 8, 4);
            ruBtn.Margin = new Thickness(0, 0, 6, 0);

            var enBtn = WpfFieldTheme.Button("EN", () => { showRu = false; refreshLanguage(); });
            enBtn.MinHeight = 32;
            enBtn.MinWidth = 44;
            enBtn.Padding = new Thickness(8, 4, 8, 4);
            enBtn.Margin = new Thickness(0);

            topRight.Children.Add(webBtn);
            topRight.Children.Add(ruBtn);
            topRight.Children.Add(enBtn);
            topDock.Children.Add(topRight);
            topDock.Children.Add(titleStack);
            DockPanel.SetDock(topDock, Dock.Top);

            root.Children.Add(topDock);
            root.Children.Add(footerDock);
            root.Children.Add(bodyGrid);

            refreshLanguage();
            WpfFieldTheme.ApplyFluentShell(win, "ZiloreMiniMap — " + (showRu ? "Руководство" : "User Guide"), root);
            SystemTypography.ApplyClearType(win);
            return win;
        }

        static TextBlock BuildRichTextBlock(string markdownLine, double fontSize) {
            var tb = new TextBlock {
                FontFamily = SystemTypography.WpfUiFontFamily,
                FontSize = fontSize,
                Foreground = WpfFieldTheme.Ink,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = fontSize * 1.48
            };
            SystemTypography.ApplyClearType(tb);

            int i = 0, len = markdownLine.Length;
            while (i < len) {
                if (i + 1 < len && markdownLine[i] == '*' && markdownLine[i + 1] == '*') {
                    int end = markdownLine.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (end > i + 2) {
                        var run = new Run(markdownLine.Substring(i + 2, end - i - 2)) { FontWeight = FontWeights.SemiBold };
                        tb.Inlines.Add(run);
                        i = end + 2;
                        continue;
                    }
                }
                if (markdownLine[i] == '`') {
                    int end = markdownLine.IndexOf('`', i + 1);
                    if (end > i + 1) {
                        var run = new Run(" " + markdownLine.Substring(i + 1, end - i - 1) + " ") {
                            FontFamily = new FontFamily("Cascadia Code, Consolas"),
                            FontWeight = FontWeights.SemiBold,
                            Foreground = WpfFieldTheme.Cyan
                        };
                        tb.Inlines.Add(run);
                        i = end + 1;
                        continue;
                    }
                }
                if (markdownLine[i] == '*') {
                    int end = markdownLine.IndexOf('*', i + 1);
                    if (end > i + 1) {
                        var run = new Run(markdownLine.Substring(i + 1, end - i - 1)) { FontStyle = FontStyles.Italic };
                        tb.Inlines.Add(run);
                        i = end + 1;
                        continue;
                    }
                }
                int nextSpecial = len;
                int nStar = markdownLine.IndexOf('*', i);
                int nTick = markdownLine.IndexOf('`', i);
                if (nStar >= 0 && nStar < nextSpecial) nextSpecial = nStar;
                if (nTick >= 0 && nTick < nextSpecial) nextSpecial = nTick;
                if (nextSpecial == i) nextSpecial = i + 1;
                tb.Inlines.Add(new Run(markdownLine.Substring(i, nextSpecial - i)));
                i = nextSpecial;
            }
            return tb;
        }
    }
}
