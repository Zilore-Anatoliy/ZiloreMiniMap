#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZiloreMiniMap {
    internal sealed partial class WpfFieldConsoleRuntime {
        readonly Dictionary<string, Window> wpfAdminProfileWindows = new Dictionary<string, Window>(StringComparer.OrdinalIgnoreCase);

        internal void CloseAllWpfAdminProfiles() {
            foreach (var w in wpfAdminProfileWindows.Values.ToArray()) {
                try { w.Close(); } catch { }
            }
            wpfAdminProfileWindows.Clear();
        }

        internal void ShowWpfAdminProfile(AdminPlayer? player, string page = "Overview") {
            if (player == null || string.IsNullOrWhiteSpace(player.Id) || disposed) return;
            PostUi(() => {
                if (disposed) return;
                string scope = engine.WpfAdminPlayerServer;
                string key = (scope ?? "") + "\n" + player.Id;
                if (wpfAdminProfileWindows.TryGetValue(key, out var existing) && existing.IsLoaded) {
                    SelectAdminProfileTab(existing, page);
                    if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
                    existing.Show();
                    existing.Activate();
                    return;
                }

                var win = BuildWpfAdminProfile(player, scope ?? "", page);
                wpfAdminProfileWindows[key] = win;
                win.Closed += (s, e) => { wpfAdminProfileWindows.Remove(key); };
                win.Topmost = true;
                win.ShowInTaskbar = true;
                win.ShowActivated = !diagnostic;
                win.Show();
                if (!diagnostic) win.Activate();
            });
        }

        static void SelectAdminProfileTab(Window win, string? page) {
            if (win.Tag is TabControl tabs && !string.IsNullOrEmpty(page)) {
                foreach (object item in tabs.Items) {
                    if (item is TabItem ti && string.Equals(ti.Tag as string, page, StringComparison.OrdinalIgnoreCase)) {
                        tabs.SelectedItem = ti;
                        break;
                    }
                }
            }
        }

        Window BuildWpfAdminProfile(AdminPlayer player, string scope, string initialPage) {
            bool ru = Localization.Current == AppLanguage.Russian;
            string title = Localization.T("ProfileTitle", player.Name ?? player.Id);
            var win = Window(title, 860, 640);
            win.MinWidth = 680;
            win.MinHeight = 500;

            var root = new DockPanel { Margin = new Thickness(18, 12, 18, 16) };

            var statusLbl = WpfFieldTheme.Text(Localization.T("PlayersScope", scope), 12.5, WpfFieldTheme.Muted);
            statusLbl.Margin = new Thickness(2, 10, 2, 0);
            statusLbl.TextWrapping = TextWrapping.Wrap;
            DockPanel.SetDock(statusLbl, Dock.Bottom);
            root.Children.Add(statusLbl);

            var headerPanel = new DockPanel();
            var quickActions = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(quickActions, Dock.Right);

            Action<string> runTeleport = cmd => {
                if (string.IsNullOrWhiteSpace(cmd)) return;
                statusLbl.Text = ru ? "Отправка команды…" : "Sending command…";
                _ = Task.Run(async () => {
                    try {
                        bool ok = await engine.WpfRunAdminCommand(cmd).ConfigureAwait(false);
                        PostUi(() => { statusLbl.Text = Localization.Get(ok ? "PlayersTeleportSent" : "PlayersTeleportStopped"); });
                    } catch (Exception ex) {
                        PostUi(() => { statusLbl.Text = ex.Message; });
                    }
                });
            };

            if (!string.IsNullOrWhiteSpace(player.TeleportTo)) {
                var tpToBtn = WpfFieldTheme.Button(Localization.Get("PlayersGo"), () => runTeleport(player.TeleportTo), true);
                tpToBtn.Margin = new Thickness(6, 0, 0, 0);
                tpToBtn.Padding = new Thickness(12, 6, 12, 6);
                quickActions.Children.Add(tpToBtn);
            }
            if (!string.IsNullOrWhiteSpace(player.TeleportHere)) {
                var tpHereBtn = WpfFieldTheme.Button(Localization.Get("PlayersHere"), () => runTeleport(player.TeleportHere));
                tpHereBtn.Margin = new Thickness(6, 0, 0, 0);
                tpHereBtn.Padding = new Thickness(12, 6, 12, 6);
                quickActions.Children.Add(tpHereBtn);
            }
            string? steamUrl = SteamProfiles.Url(player.Id);
            if (steamUrl != null) {
                var steamBtn = WpfFieldTheme.Button("Steam", () => {
                    if (!WpfFieldTheme.OpenShellUri(steamUrl))
                        statusLbl.Text = ru ? "Не удалось открыть ссылку Steam." : "Could not open Steam profile URL.";
                });
                steamBtn.Margin = new Thickness(6, 0, 0, 0);
                steamBtn.Padding = new Thickness(12, 6, 12, 6);
                quickActions.Children.Add(steamBtn);
            }
            headerPanel.Children.Add(quickActions);

            var idStack = new StackPanel();
            var nameHeading = WpfFieldTheme.Text(player.Name ?? player.Id, 18, WpfFieldTheme.Ink, true);
            var subMeta = WpfFieldTheme.Text(
                (player.PlatformName ?? "") + "  ·  SteamID: " + player.Id,
                12.5,
                WpfFieldTheme.Muted);
            var summaryLbl = WpfFieldTheme.Text(ru ? "Загрузка данных профиля…" : "Loading profile data…", 13, WpfFieldTheme.Cyan, true);
            summaryLbl.Margin = new Thickness(0, 4, 0, 0);
            idStack.Children.Add(nameHeading);
            idStack.Children.Add(subMeta);
            idStack.Children.Add(summaryLbl);
            headerPanel.Children.Add(idStack);

            var headerCard = WpfFieldTheme.Card(headerPanel, new Thickness(14, 12, 14, 12));
            headerCard.Margin = new Thickness(0, 0, 0, 10);
            DockPanel.SetDock(headerCard, Dock.Top);
            root.Children.Add(headerCard);

            var toolbar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var showAllCheck = new CheckBox {
                Content = Localization.Get("ProfileAllAssets"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0),
                Foreground = WpfFieldTheme.Ink
            };
            toolbar.Children.Add(showAllCheck);

            var vehiclesList = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), SelectionMode = SelectionMode.Extended };
            var flagsList = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), SelectionMode = SelectionMode.Extended };
            var squadPanel = new StackPanel();
            var overviewText = WpfFieldTheme.Text("", 13.5, WpfFieldTheme.Ink);
            overviewText.TextWrapping = TextWrapping.Wrap;
            var squadHeaderLbl = WpfFieldTheme.Text("", 13.5, WpfFieldTheme.Amber, true);
            squadHeaderLbl.TextWrapping = TextWrapping.Wrap;
            squadHeaderLbl.Margin = new Thickness(0, 0, 0, 8);

            var tabs = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            var tabOverview = new TabItem {
                Header = Localization.Get("ProfileOverview"),
                Tag = "Overview",
                Content = WpfFieldTheme.Card(new ScrollViewer { Content = overviewText, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, new Thickness(14))
            };
            var tabVehicles = new TabItem {
                Header = Localization.Get("ProfileVehicles"),
                Tag = "Vehicles",
                Content = WpfFieldTheme.Card(vehiclesList, new Thickness(8))
            };
            var tabFlags = new TabItem {
                Header = Localization.Get("ProfileFlags"),
                Tag = "Flags",
                Content = WpfFieldTheme.Card(flagsList, new Thickness(8))
            };
            var squadWrap = new StackPanel();
            squadWrap.Children.Add(squadHeaderLbl);
            squadWrap.Children.Add(squadPanel);
            var tabSquad = new TabItem {
                Header = Localization.Get("ProfileSquad"),
                Tag = "Squad",
                Content = WpfFieldTheme.Card(new ScrollViewer { Content = squadWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, new Thickness(14))
            };
            tabs.Items.Add(tabOverview);
            tabs.Items.Add(tabVehicles);
            tabs.Items.Add(tabFlags);
            tabs.Items.Add(tabSquad);
            win.Tag = tabs;

            MapWindow.WpfAdminProfileSnapshot? latestSnapshot = null;

            Func<List<AdminAsset>> getSelectedAssets = () => {
                var selected = new List<AdminAsset>();
                var activeList = tabs.SelectedItem == tabFlags ? flagsList : (tabs.SelectedItem == tabVehicles ? vehiclesList : null);
                if (activeList != null) {
                    foreach (object item in activeList.SelectedItems) {
                        if (item is ListBoxItem lbi && lbi.Tag is AdminAsset asset) selected.Add(asset);
                    }
                }
                return selected;
            };

            Action renderData = () => {
                var snap = latestSnapshot;
                if (snap == null) return;
                var activePlayer = snap.Player ?? player;
                bool showAll = showAllCheck.IsChecked == true;
                var assets = snap.Assets ?? Array.Empty<AdminAsset>();
                var members = snap.Members ?? Array.Empty<SquadMember>();
                var squadIds = new HashSet<string>(StringComparer.Ordinal);
                for (int mi = 0; mi < members.Count; mi++) squadIds.Add(members[mi].Id);

                int ownVehicles = 0, ownFlags = 0;
                for (int ai = 0; ai < assets.Count; ai++) {
                    if (assets[ai].OwnerId == activePlayer.Id) {
                        if (assets[ai].Kind == "Vehicle") ownVehicles++;
                        else if (assets[ai].Kind == "Flag") ownFlags++;
                    }
                }

                string squadSummary = string.IsNullOrWhiteSpace(snap.Squad) ? Localization.Get("ProfileNoSquad") : snap.Squad;
                summaryLbl.Text = Localization.T("ProfileSummary", ownVehicles, ownFlags, squadSummary);
                squadHeaderLbl.Text = squadSummary;

                string popInfo = activePlayer.Location == null
                    ? Localization.Get("ProfileNoPopulation")
                    : Localization.T(
                        "ProfilePopulation",
                        activePlayer.SeenUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                        Localization.Get(activePlayer.InSnapshot ? "PlayersSnapshot" : "PlayersHistory"),
                        activePlayer.Fame,
                        activePlayer.Balance,
                        activePlayer.Gold);
                overviewText.Text = Localization.T(
                    "ProfileIdentity",
                    activePlayer.Name ?? activePlayer.Id,
                    activePlayer.PlatformName ?? "",
                    activePlayer.Id,
                    popInfo);

                vehiclesList.Items.Clear();
                flagsList.Items.Clear();
                for (int ai = 0; ai < assets.Count; ai++) {
                    var asset = assets[ai];
                    bool relevant = showAll || asset.OwnerId == activePlayer.Id || (asset.Kind == "Flag" && squadIds.Contains(asset.OwnerId));
                    if (!relevant) continue;
                    string ownerTxt = !string.IsNullOrEmpty(asset.OwnerId)
                        ? asset.OwnerId
                        : (!string.IsNullOrEmpty(asset.OwnerDatabaseId) ? Localization.T("ProfileUnresolvedOwner", asset.OwnerDatabaseId) : Localization.Get("ProfileNoOwner"));
                    if (!string.IsNullOrEmpty(asset.AliasId)) ownerTxt = Localization.T("ProfileAlias", ownerTxt, asset.AliasId);
                    string line = string.Format(
                        CultureInfo.InvariantCulture,
                        "#{0} · {1} · {2} · X={3:0} Y={4:0} Z={5:0} · {6}",
                        asset.Id, asset.Name, ownerTxt, asset.Location.X, asset.Location.Y, asset.Location.Z,
                        asset.SeenUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
                    var lbi = new ListBoxItem { Content = WpfFieldTheme.Text(line, 12.5, WpfFieldTheme.Ink), Tag = asset, Padding = new Thickness(8, 5, 8, 5) };
                    if (asset.Kind == "Flag") flagsList.Items.Add(lbi);
                    else vehiclesList.Items.Add(lbi);
                }

                squadPanel.Children.Clear();
                for (int mi = 0; mi < members.Count; mi++) {
                    var mem = members[mi];
                    var row = new DockPanel();
                    var openBtn = WpfFieldTheme.Button(Localization.Get("ProfileOpenPlayer"), () => {
                        var target = engine.WpfFindKnownAdminPlayer(mem.Id, mem.Name, mem.PlatformName);
                        ShowWpfAdminProfile(target, "Overview");
                    });
                    openBtn.Padding = new Thickness(10, 4, 10, 4);
                    DockPanel.SetDock(openBtn, Dock.Right);
                    row.Children.Add(openBtn);
                    string stText = Localization.Get(mem.Status == "Online" ? "ProfileOnline" : mem.Status == "Offline" ? "ProfileOffline" : "ProfileUnknownStatus");
                    var memTxt = WpfFieldTheme.Text(Localization.T("ProfileMemberDetails", mem.Name, mem.PlatformName, mem.Rank, stText), 12.5, WpfFieldTheme.Ink);
                    memTxt.TextWrapping = TextWrapping.Wrap;
                    row.Children.Add(memTxt);
                    var card = WpfFieldTheme.Card(row, new Thickness(10, 8, 10, 8));
                    card.Margin = new Thickness(0, 0, 0, 6);
                    squadPanel.Children.Add(card);
                }
            };

            Action reloadAsync = () => {
                _ = Task.Run(async () => {
                    try {
                        var snap = await engine.WpfLoadAdminProfileAsync(scope, player).ConfigureAwait(false);
                        PostUi(() => {
                            if (!win.IsLoaded) return;
                            latestSnapshot = snap;
                            renderData();
                        });
                    } catch (Exception ex) {
                        PostUi(() => { if (win.IsLoaded) statusLbl.Text = ex.Message; });
                    }
                });
            };

            var importBtn = WpfFieldTheme.Button(Localization.Get("PlayersImport"), () => {
                string clipText = "";
                try { if (System.Windows.Clipboard.ContainsText()) clipText = System.Windows.Clipboard.GetText(); } catch { }
                if (string.IsNullOrWhiteSpace(clipText)) {
                    statusLbl.Text = ru ? "В буфере обмена нет текста отчёта." : "Clipboard does not contain report text.";
                    return;
                }
                _ = Task.Run(async () => {
                    try {
                        string msg = await engine.WpfImportAdminClipboardAsync(scope, clipText).ConfigureAwait(false);
                        PostUi(() => {
                            if (!win.IsLoaded) return;
                            statusLbl.Text = msg;
                            reloadAsync();
                        });
                    } catch (Exception ex) {
                        PostUi(() => { if (win.IsLoaded) statusLbl.Text = ex.Message; });
                    }
                });
            });
            importBtn.Margin = new Thickness(0, 0, 8, 0);
            importBtn.Padding = new Thickness(10, 5, 10, 5);
            toolbar.Children.Add(importBtn);

            var tpAssetBtn = WpfFieldTheme.Button(Localization.Get("PlayersGo"), () => {
                var sel = getSelectedAssets();
                if (sel.Count != 1) { statusLbl.Text = Localization.Get("ProfileSelectLocation"); return; }
                string? cmd = MapWindow.AssetTeleportCommand(sel[0]);
                if (cmd != null) runTeleport(cmd);
            });
            tpAssetBtn.Margin = new Thickness(0, 0, 8, 0);
            tpAssetBtn.Padding = new Thickness(10, 5, 10, 5);
            toolbar.Children.Add(tpAssetBtn);

            var showMapBtn = WpfFieldTheme.Button(Localization.Get("ProfileShowSelected"), () => {
                var sel = getSelectedAssets();
                if (sel.Count == 0) { statusLbl.Text = Localization.Get("ProfileSelectLocation"); return; }
                engine.WpfSetAdminProfileMarkers(sel);
                statusLbl.Text = ru ? ("Показано меток на карте: " + sel.Count) : ("Shown markers on map: " + sel.Count);
            });
            showMapBtn.Margin = new Thickness(0, 0, 8, 0);
            showMapBtn.Padding = new Thickness(10, 5, 10, 5);
            toolbar.Children.Add(showMapBtn);

            var clearMapBtn = WpfFieldTheme.Button(Localization.Get("ProfileClearMarkers"), () => {
                engine.WpfClearAdminProfileMarkers();
                statusLbl.Text = Localization.Get("ProfileMarkersCleared");
            });
            clearMapBtn.Padding = new Thickness(10, 5, 10, 5);
            toolbar.Children.Add(clearMapBtn);

            showAllCheck.Checked += (s, e) => renderData();
            showAllCheck.Unchecked += (s, e) => renderData();

            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);
            root.Children.Add(tabs);

            SelectAdminProfileTab(win, initialPage);
            WpfFieldTheme.ApplyFluentShell(win, "ZiloreMiniMap — " + title, root);
            SystemTypography.ApplyClearType(win);

            win.Loaded += (s, e) => {
                reloadAsync();
                if (!diagnostic) {
                    _ = Task.Run(async () => {
                        string? steamName = await SteamProfiles.Name(player.Id).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(steamName)) {
                            PostUi(() => {
                                if (win.IsLoaded) subMeta.Text = (player.PlatformName ?? "") + "  ·  Steam: " + steamName + "  ·  ID: " + player.Id;
                            });
                        }
                    });
                }
            };

            return win;
        }
    }
}
