#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;

namespace ZiloreMiniMap {
    internal sealed partial class WpfFieldConsoleRuntime {
        internal bool EnsureEulaAccepted() {
            if (diagnostic || !engine.WpfNeedsEulaAcceptance) return true;
            bool accepted = false;
            var win = BuildEulaWindow(true, () => { accepted = engine.WpfAcceptEula(); });
            try {
                Modal(win);
            } finally {
                try { if (win.IsLoaded) win.Close(); } catch { }
            }
            if (!accepted) eulaDeclined = true;
            return accepted;
        }

        internal void PostShowEula() {
            PostUi(ShowEulaViewer);
        }

        internal void ShowEulaViewer() {
            if (disposed) return;
            var win = BuildEulaWindow(false, null);
            Modal(win);
        }

        Window BuildEulaWindow(bool requireAcceptance, Action? onAccepted) {
            bool showRu = Localization.Current == AppLanguage.Russian;
            string initialTitle = showRu ? "Лицензионное соглашение (EULA)" : "End User License Agreement (EULA)";
            var win = Window(initialTitle, 740, 600);
            win.MinWidth = 560;
            win.MinHeight = 460;

            var layout = new DockPanel { Margin = new Thickness(20, 14, 20, 18) };

            // Top bar: subtitle + RU/EN toggle so user can inspect both editions immediately
            var headerDock = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var langStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(langStack, Dock.Right);

            var titleLbl = WpfFieldTheme.Text(initialTitle, 17, WpfFieldTheme.Ink, true);
            var subLbl = WpfFieldTheme.Text("", 12.5, WpfFieldTheme.Muted);
            subLbl.Margin = new Thickness(0, 3, 0, 0);
            var titleStack = new StackPanel();
            titleStack.Children.Add(titleLbl);
            titleStack.Children.Add(subLbl);

            var bodyBox = new TextBox {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                FontFamily = SystemTypography.WpfUiFontFamily,
                FontSize = 13,
                Padding = new Thickness(12),
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = WpfFieldTheme.Ink
            };
            SystemTypography.ApplyClearType(bodyBox);

            CheckBox? consentCheck = null;
            Button? acceptBtn = null;
            Button? declineOrCloseBtn = null;
            Button? mailBtn = null;

            Action refreshLanguage = () => {
                string hdr = showRu ? "Лицензионное соглашение (EULA)" : "End User License Agreement (EULA)";
                win.Title = hdr;
                titleLbl.Text = hdr;
                subLbl.Text = showRu
                    ? ("ZiloreMiniMap · Редакция " + EulaAgreement.CurrentVersion + " · Все вопросы: " + EulaAgreement.SupportEmail)
                    : ("ZiloreMiniMap · Edition " + EulaAgreement.CurrentVersion + " · Inquiries: " + EulaAgreement.SupportEmail);
                bodyBox.Text = EulaAgreement.GetText(showRu);
                bodyBox.ScrollToHome();
                if (consentCheck != null) {
                    consentCheck.Content = showRu
                        ? "Я прочитал(а) и принимаю условия Лицензионного соглашения (включая запрет модификации)"
                        : "I have read and accept the End User License Agreement (including the prohibition on modification)";
                }
                if (acceptBtn != null) {
                    acceptBtn.Content = showRu ? "Принять и продолжить" : "Accept & Continue";
                }
                if (declineOrCloseBtn != null) {
                    declineOrCloseBtn.Content = requireAcceptance
                        ? (showRu ? "Отклонить и выйти" : "Decline & Exit")
                        : (showRu ? "Закрыть" : "Close");
                }
                if (mailBtn != null) {
                    mailBtn.Content = "✉ " + EulaAgreement.SupportEmail;
                }
            };

            var ruBtn = WpfFieldTheme.Button("RU", () => { showRu = true; refreshLanguage(); });
            ruBtn.MinHeight = 30;
            ruBtn.MinWidth = 44;
            ruBtn.Padding = new Thickness(8, 3, 8, 3);
            ruBtn.Margin = new Thickness(0, 0, 6, 0);

            var enBtn = WpfFieldTheme.Button("EN", () => { showRu = false; refreshLanguage(); });
            enBtn.MinHeight = 30;
            enBtn.MinWidth = 44;
            enBtn.Padding = new Thickness(8, 3, 8, 3);
            enBtn.Margin = new Thickness(0);

            langStack.Children.Add(ruBtn);
            langStack.Children.Add(enBtn);
            headerDock.Children.Add(langStack);
            headerDock.Children.Add(titleStack);
            DockPanel.SetDock(headerDock, Dock.Top);
            layout.Children.Add(headerDock);

            // Bottom action area
            var footerStack = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            if (requireAcceptance) {
                consentCheck = new CheckBox {
                    Foreground = WpfFieldTheme.Ink,
                    FontSize = 13,
                    Margin = new Thickness(2, 0, 0, 12),
                    IsChecked = false
                };
                footerStack.Children.Add(consentCheck);
            }

            var btnDock = new DockPanel();
            mailBtn = WpfFieldTheme.Button("✉ " + EulaAgreement.SupportEmail, () => {
                _ = WpfFieldTheme.OpenShellUri("mailto:" + EulaAgreement.SupportEmail + "?subject=ZiloreMiniMap%20EULA");
            });
            mailBtn.MinHeight = 36;
            mailBtn.Padding = new Thickness(12, 6, 12, 6);
            mailBtn.Margin = new Thickness(0);
            DockPanel.SetDock(mailBtn, Dock.Left);
            btnDock.Children.Add(mailBtn);

            var rightBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            declineOrCloseBtn = WpfFieldTheme.Button(requireAcceptance ? "Отклонить и выйти" : "Закрыть", () => win.Close(), !requireAcceptance);
            declineOrCloseBtn.MinHeight = 36;
            declineOrCloseBtn.MinWidth = 120;
            declineOrCloseBtn.Padding = new Thickness(14, 6, 14, 6);
            declineOrCloseBtn.Margin = new Thickness(0, 0, requireAcceptance ? 8 : 0, 0);
            rightBtns.Children.Add(declineOrCloseBtn);

            if (requireAcceptance) {
                acceptBtn = WpfFieldTheme.Button("Принять и продолжить", () => {
                    onAccepted?.Invoke();
                    win.Close();
                }, true);
                acceptBtn.IsEnabled = false;
                acceptBtn.MinHeight = 36;
                acceptBtn.MinWidth = 170;
                acceptBtn.Padding = new Thickness(16, 6, 16, 6);
                acceptBtn.Margin = new Thickness(0);
                rightBtns.Children.Add(acceptBtn);

                var acceptLocal = acceptBtn;
                if (consentCheck != null && acceptLocal != null) {
                    consentCheck.Checked += (s, e) => { acceptLocal.IsEnabled = true; };
                    consentCheck.Unchecked += (s, e) => { acceptLocal.IsEnabled = false; };
                }
            }

            btnDock.Children.Add(rightBtns);
            footerStack.Children.Add(btnDock);
            DockPanel.SetDock(footerStack, Dock.Bottom);
            layout.Children.Add(footerStack);

            var textCard = WpfFieldTheme.Card(bodyBox, new Thickness(4));
            layout.Children.Add(textCard);

            refreshLanguage();
            WpfFieldTheme.ApplyFluentShell(win, "ZiloreMiniMap — EULA", layout);
            SystemTypography.ApplyClearType(win);
            return win;
        }
    }
}
