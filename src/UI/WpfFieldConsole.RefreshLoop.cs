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
        internal void Refresh() {
            application.Dispatcher.VerifyAccess();
            if (disposed)
                return;
            RefreshSetupPreview();
            // The native controller also requests refreshes. Coalesce those with the WPF timer.
            bool targetChanged = !object.ReferenceEquals(lastPresentedTarget, engine.WpfCurrentTarget);
            long minIntervalMs = (!diagnostic && !TacticalVisible && !hud.IsVisible) ? 200 : 8;
            if (!diagnostic && !targetChanged && refreshClock.ElapsedMilliseconds - lastRefresh < minIntervalMs)
                return;
            lastRefresh = refreshClock.ElapsedMilliseconds;
            bool runSlowTick = diagnostic || targetChanged || (refreshClock.ElapsedMilliseconds - lastSlowRefresh >= 200);
            IntPtr tickForeground = diagnostic ? IntPtr.Zero : Native.GetForegroundWindow();
            bool tickGameFg = !diagnostic && Native.IsGameWindow(tickForeground);
            if (!diagnostic && tactical.IsVisible) {
                bool isGameFg = tickGameFg, isOurFg = Native.IsOurWindow(tickForeground);
                if (tacticalStandalone) {
                    if (isGameFg) {
                        tacticalStandalone = false;
                        SyncTacticalFocus(true, isOurFg);
                    } else if (setupWindow != null && setupWindow.IsVisible) {
                        tacticalStandalone = false;
                        SyncTacticalFocus(false, false);
                    } else if (dialogs.Count > 0) {
                        tacticalMouseShieldActive = false;
                        if (tactical.Topmost)
                            tactical.Topmost = false;
                    } else {
                        if (!tactical.Topmost)
                            tactical.Topmost = true;
                        tacticalMouseShieldActive = tactical.WindowState != WindowState.Minimized;
                    }
                } else {
                    SyncTacticalFocus(isGameFg, isOurFg);
                }
            }
            if (!diagnostic && runSlowTick)
                MaintainActiveWindowTopmost();
            bool tacticalVisible = TacticalVisible;
            bool zoomedThisTick = false;
            if (!diagnostic && tacticalVisible) {
                if (runSlowTick) {
                    var bounds = TacticalBounds();
                    if (bounds != lastTacticalBounds)
                        ApplyTacticalBounds(bounds);
                }
                if (engine.WpfHasPendingTacticalZoom) {
                    long nowZ = refreshClock.ElapsedMilliseconds;
                    double dtZ = lastZoomAnimTick > 0 ? Math.Max(0.001, Math.Min(0.1, (nowZ - lastZoomAnimTick) / 1000.0)) : 0.016;
                    lastZoomAnimTick = nowZ;
                    if (engine.WpfStepTacticalZoom(dtZ)) {
                        RefreshCamera();
                        zoomedThisTick = true;
                    }
                } else {
                    lastZoomAnimTick = 0;
                }
                // Fast-path during active smooth zoom or pointer pan: avoid redundant WpfSnapshot + PublishState double-redraw on the same frame
                if ((zoomedThisTick || tacticalMap.PointerCameraActive) && !runSlowTick && !targetChanged)
                    return;
            }
            if (runSlowTick) {
                scratchSessions.Clear();
                if (relayGroups != null) {
                    var rs = relayGroups.Sessions;
                    if (rs != null)
                    for (int si = 0; si < rs.Length; si++)
                        scratchSessions.Add(rs[si]);
                }
                if (privatePositions != null)
                    scratchSessions.Add(privatePositions);
                cachedSharingSessions = scratchSessions.Count == 0 ? Array.Empty<DirectPositions>() : scratchSessions.ToArray();
            }
            var sharingSessions = cachedSharingSessions;
            if (runSlowTick) {
                lastSlowRefresh = refreshClock.ElapsedMilliseconds;
                List<MapZone> refreshedZones;
                if (zoneFolder != null && zoneFolder.Poll(out refreshedZones))
                    engine.WpfRefreshFolderZones(refreshedZones);
                if (!diagnostic && !zoneFileActive && dialogs.Count == 0) {
                    if (zoneOpenRead != null && zoneOpenRead.IsCompleted) {
                        string request = null;
                        try {
                            request = zoneOpenRead.GetAwaiter().GetResult();
                        } catch (Exception error) { Program.LogException("ZoneOpenQueue", error); } finally { zoneOpenRead = null; }
                        if (request != null) {
                            ShowZoneFile(request);
                            return;
                        }
                    }
                    if (zoneOpenRead == null && DateTime.UtcNow >= nextZoneOpenRead) {
                        nextZoneOpenRead = DateTime.UtcNow.AddSeconds(1);
                        string profile = engine.WpfDataFolder;
                        zoneOpenRead = System.Threading.Tasks.Task.Run(() => ZoneFileOpen.Take(profile));
                    }
                }
                if (relayGroups != null)
                    relayGroups.SynchronizeNames();
                if (sharingSessions.Length > 0) {
                    var sample = engine.WpfSnapshot(false);
                    var pubPos = sample.Tracking && !sample.TrackingPaused ? sample.Position : null;
                    long nowMs = refreshClock.ElapsedMilliseconds;
                    bool sendPos = (nowMs - lastNetPosMs >= 2000) || (pubPos == null) != (lastNetPos == null);
                    if (!sendPos && pubPos != null && lastNetPos != null) {
                        double dx = pubPos.X - lastNetPos.X, dy = pubPos.Y - lastNetPos.Y;
                        double dyaw = Math.Abs((pubPos.Yaw - lastNetPos.Yaw) % 360.0);
                        if (dyaw > 180.0)
                            dyaw = 360.0 - dyaw;
                        if (dx * dx + dy * dy >= 50.0 * 50.0 || dyaw >= 2.0 || Math.Abs(pubPos.Z - lastNetPos.Z) >= 100.0)
                            sendPos = true;
                    }
                    if (sendPos) {
                        lastNetPosMs = nowMs;
                        lastNetPos = pubPos == null ? null : new Position { X = pubPos.X, Y = pubPos.Y, Z = pubPos.Z, Yaw = pubPos.Yaw };
                        foreach (var session in sharingSessions)
                            session.Update(pubPos, sample.Updated);
                    }
                    scratchNetMarkers.Clear();
                    foreach (var m in OwnSquadPublication()) {
                        if (scratchNetMarkers.Count >= 128)
                            break;
                        scratchNetMarkers.Add(m);
                    }
                    if (scratchNetMarkers.Count < 128)
                    foreach (var m in engine.WpfSharedMarkers()) {
                        if (scratchNetMarkers.Count >= 128)
                            break;
                        scratchNetMarkers.Add(m);
                    }
                    var markers = scratchNetMarkers.Count == 0 ? Array.Empty<SharedMapMarker>() : scratchNetMarkers.ToArray();
                    int mHash = markers.Length;
                    for (int mi = 0; mi < markers.Length; mi++)
                        unchecked {
                            mHash = mHash * 31 + (markers[mi].Id == null ? 0 : markers[mi].Id.GetHashCode()) ^ (int)(markers[mi].X * 10000) ^ (int)(markers[mi].Y * 10000);
                        }
                    if (mHash != lastNetMarkersHash || nowMs - lastNetMarkersMs >= 4000) {
                        lastNetMarkersHash = mHash;
                        lastNetMarkersMs = nowMs;
                        foreach (var session in sharingSessions)
                            session.UpdateMarkers(relayGroups == null ? markers : relayGroups.MarkersFor(session, markers));
                    }
                }
                if (sharingSessions.Length > 0 || squadCoordinator.KnownPeerCount > 0 || activeStops.Length > 0 || followingPeer != null || squadTarget != null || squad.Stops.Length > 0) {
                    UpdateSquad();
                    UpdateRoutePreviews();
                    foreach (var peer in playerRoster) {
                        TextBlock label;
                        if (playerStatusLabels.TryGetValue(peer.Id, out label))
                            label.Text = PeerStatus(peer);
                    }
                }
            }
            if (!diagnostic) {
                bool wantHud = ShouldShowHud(hudEnabled, tacticalVisible) && !hudMoving;
                if (runSlowTick || wantHud != hud.IsVisible)
                    SyncHudVisibility();
                if (!tacticalVisible && !hud.IsVisible) {
                    DateTime bgNowUtc = DateTime.UtcNow;
                    long bgSec = bgNowUtc.Ticks / TimeSpan.TicksPerSecond;
                    if (bgSec != lastTelemetrySecond) {
                        lastTelemetrySecond = bgSec;
                        var bgSnap = engine.WpfSnapshot(false);
                        if (bgSnap.Position != null && !bgSnap.TrackingPaused) {
                            UpdateFlightTelemetry(bgSnap.Position, bgSnap.Updated, bgNowUtc);
                            WpfMapSurface.CurrentGroundSpeedMps = CurrentEstimatedSpeedKmh / 3.6;
                            bool bgFg = tickGameFg;
                            ZiloreAiEngine.ObserveTelemetry(bgSnap.Position, CurrentEstimatedSpeedKmh / 3.6, bgNowUtc);
                            ScumSurvivalTracker.Update(CurrentEstimatedSpeedKmh / 3.6, bgFg, bgNowUtc);
                            ScumWeatherEngine.Update(bgSnap.Position, MapWindow.ToMap(bgSnap.Position), bgFg, bgNowUtc);
                        }
                    }
                    if (refresh != null && refresh.Interval.TotalMilliseconds != 150) refresh.Interval = TimeSpan.FromMilliseconds(150);
                    return;
                }
            }
            bool fullView = diagnostic || tacticalVisible;
            var state = engine.WpfSnapshot(fullView);
            targetChanged |= !object.ReferenceEquals(lastPresentedTarget, state.Target);
            lastPresentedTarget = state.Target;
            if (fullView) {
                SyncCameraControls(state);
                SyncTacticalGlassAppearance();
            }
            var compact = (fullView && (diagnostic || hud.IsVisible)) ? engine.WpfSnapshot(false) : state;
            if (sharingSessions.Length > 0) {
                if (runSlowTick || !peersCacheValid) {
                    scratchPeers.Clear();
                    if (privatePositions != null) {
                        var ps = privatePositions.Snapshot();
                        if (ps != null)
                        for (int i = 0; i < ps.Length; i++)
                        if ((PlayerVisibility(ps[i].Id) & 1) != 0)
                            scratchPeers.Add(ps[i]);
                    }
                    if (relayGroups != null) {
                        var rg = relayGroups.Roster(true);
                        if (rg != null)
                        for (int i = 0; i < rg.Length; i++)
                        if ((PlayerVisibility(rg[i].Id) & 1) != 0)
                            scratchPeers.Add(rg[i]);
                    }
                    cachedPeers = scratchPeers.Count == 0 ? Array.Empty<PrivatePositions.Peer>() : scratchPeers.ToArray();
                    UpdatePeerSmoothingTargets(cachedPeers);

                    scratchMarkers.Clear();
                    if (privatePositions != null) {
                        var pm = privatePositions.SnapshotMarkers();
                        if (pm != null)
                        for (int i = 0; i < pm.Length; i++)
                        if (PlayerMarkerVisible(pm[i]))
                            scratchMarkers.Add(pm[i]);
                    }
                    if (relayGroups != null) {
                        var rm = relayGroups.Markers();
                        if (rm != null)
                        for (int i = 0; i < rm.Length; i++)
                        if (PlayerMarkerVisible(rm[i]))
                            scratchMarkers.Add(rm[i]);
                    }
                    var recv = squad.Receive(scratchMarkers);
                    scratchMarkers.Clear();
                    if (recv != null)
                    for (int i = 0; i < recv.Length; i++)
                        scratchMarkers.Add(recv[i]);
                    var loc = squad.Local();
                    if (loc != null) {
                        string ownerName = S("PrivateGroupSurvivor");
                        for (int i = 0; i < loc.Length; i++) {
                            loc[i].Owner = ownerName;
                            scratchMarkers.Add(loc[i]);
                        }
                    }
                    cachedSharedMarkers = scratchMarkers.Count == 0 ? Array.Empty<SharedMapMarker>() : scratchMarkers.ToArray();
                    peersCacheValid = true;
                }
                peersInterpolating = false;
                if (!diagnostic && cachedPeers.Length > 0) {
                    ulong peerSumBefore = 0;
                    for (int pi = 0; pi < cachedPeers.Length; pi++) {
                        var pp = cachedPeers[pi].Position;
                        if (pp != null) unchecked { peerSumBefore = peerSumBefore * 31 + (ulong)(long)Math.Round(pp.X) + (ulong)(long)Math.Round(pp.Y) * 7 + (ulong)(long)Math.Round(pp.Yaw * 10.0) * 13; }
                    }
                    SmoothPeersInPlace(cachedPeers);
                    ulong peerSumAfter = 0;
                    for (int pi = 0; pi < cachedPeers.Length; pi++) {
                        var pp = cachedPeers[pi].Position;
                        if (pp != null) unchecked { peerSumAfter = peerSumAfter * 31 + (ulong)(long)Math.Round(pp.X) + (ulong)(long)Math.Round(pp.Y) * 7 + (ulong)(long)Math.Round(pp.Yaw * 10.0) * 13; }
                    }
                    peersInterpolating = (peerSumBefore != peerSumAfter);
                }
                state.Peers = compact.Peers = cachedPeers;
                state.SharedMarkers = compact.SharedMarkers = cachedSharedMarkers;
            } else {
                state.Peers = compact.Peers = Array.Empty<PrivatePositions.Peer>();
                state.SharedMarkers = compact.SharedMarkers = squad.Local();
            }
            state.LocalPeerId = compact.LocalPeerId = sharingSessions.Length > 0 ? sharingSessions[0].LocalId : null;
            AddRouteState(state, compact);
            if (fullView) {
                bool hasDeath = false;
                if (state.Saved != null)
                for (int di = 0; di < state.Saved.Length; di++)
                if (state.Saved[di].IsDeathMarker) {
                    hasDeath = true;
                    break;
                }
                if (deathRemovalButton.IsEnabled != hasDeath)
                    deathRemovalButton.IsEnabled = hasDeath;
            }
            if (chromeLanguage != Localization.CurrentCode) {
                chromeLanguage = Localization.CurrentCode;
                RefreshLocalizedChrome(tactical);
                if (settingsWindow != null)
                    RefreshLocalizedChrome(settingsWindow);
            }
            if ((diagnostic || tacticalVisible) && !zoomedThisTick) {
                ulong hash = VisualIdentityHash(state);
                if (!tacticalVisualHashValid || hash != tacticalVisualHash || tacticalMap.HasActivePulse) {
                    tacticalVisualHash = hash;
                    tacticalVisualHashValid = true;
                    tacticalMap.PublishState(state);
                }
            }
            Position rawCompactPos = compact.Position;
            if (diagnostic || hud.IsVisible) {
                if (!diagnostic && rawCompactPos != null)
                    compact.Position = SmoothHudPosition(rawCompactPos);
                ulong hash = VisualIdentityHash(compact);
                if (!hudVisualHashValid || hash != hudVisualHash || hudMap.HasActivePulse) {
                    hudVisualHash = hash;
                    hudVisualHashValid = true;
                    hudMap.State = compact;
                    hudMap.InvalidateVisual();
                }
            }
            if (cachedHudW != compact.HudWidth || cachedHudH != compact.HudHeight || cachedHudStatus != compact.Status || cachedHudAbove != compact.StatusAbove) {
                cachedHudW = compact.HudWidth;
                cachedHudH = compact.HudHeight;
                cachedHudStatus = compact.Status;
                cachedHudAbove = compact.StatusAbove;
                hud.Width = compact.HudWidth;
                hud.Height = compact.HudHeight + (compact.Status ? HudStatusHeight : 0);
                hudLayout.RowDefinitions[0].Height = new GridLength(compact.StatusAbove && compact.Status ? HudStatusHeight : compact.HudHeight);
                hudLayout.RowDefinitions[1].Height = new GridLength(compact.StatusAbove && compact.Status ? compact.HudHeight : compact.Status ? HudStatusHeight : 0);
                Grid.SetRow((hudMap.Parent as UIElement) ?? hudMap, compact.StatusAbove && compact.Status ? 1 : 0);
                Grid.SetRow(hudStatus, compact.StatusAbove && compact.Status ? 0 : 1);
                hudStatus.Visibility = compact.Status ? Visibility.Visible : Visibility.Collapsed;
            }
            var routeBrush = (state.Target == null && !compact.TrackingPaused) ? WpfFieldTheme.Amber : WpfMapSurface.CachedBrush(state.RouteColor);
            if (!object.ReferenceEquals(hudRoute.Foreground, routeBrush))
                hudRoute.Foreground = routeBrush;
            if (fullView) {
                UpdateTargetCardTelemetry(state);
                UpdateSidebarTelemetry(state);
                var prevVis = state.LocalRoute.Length > 0 && state.ActiveSquadRoute == null ? Visibility.Visible : Visibility.Collapsed;
                if (routePreviewCard.Visibility != prevVis)
                    routePreviewCard.Visibility = prevVis;
                if (prevVis == Visibility.Visible) {
                    string prevTitle = Localization.Get("OwnRoute") + " · " + Localization.Get(squad.Shared ? "SquadRoute" : "SoloRoute") + " · " + state.LocalRoute.Length + " / 16";
                    if (routePreviewTitle.Text != prevTitle)
                        routePreviewTitle.Text = prevTitle;
                    var prevBrush = squad.Shared ? WpfFieldTheme.Cyan : WpfFieldTheme.Amber;
                    if (!object.ReferenceEquals(routePreviewTitle.Foreground, prevBrush))
                        routePreviewTitle.Foreground = prevBrush;
                    string prevDet = state.OwnRouteSummary ?? "";
                    if (routePreviewDetail.Text != prevDet)
                        routePreviewDetail.Text = prevDet;
                }
            }
            DrawingPoint compactMapPt = rawCompactPos == null ? default(DrawingPoint) : MapWindow.ToMap(rawCompactPos);
            string sector = rawCompactPos == null ? "" : DestinationSearch.Sector(compactMapPt);
            string locationLabel = compact.Location;
            if (sector.Length > 0 && locationLabel.StartsWith(sector + " - ", StringComparison.Ordinal))
                locationLabel = locationLabel.Substring(sector.Length + 3);
            double staleThresholdSec = Math.Max(8.0, (engine.WpfCopyIntervalMs * 2.5) / 1000.0);
            bool staleGps = !compact.TrackingPaused && rawCompactPos != null && compact.Updated != DateTime.MinValue && (DateTime.UtcNow - compact.Updated).TotalSeconds > staleThresholdSec;
            if (hudStatusDot != null) {
                var dotBrush = compact.TrackingPaused || staleGps ? WpfFieldTheme.StatusPaused : (compact.Position != null && compact.Tracking ? WpfFieldTheme.StatusLive : WpfFieldTheme.StatusIdle);
                if (!object.ReferenceEquals(hudStatusDot.Fill, dotBrush))
                    hudStatusDot.Fill = dotBrush;
                if (!string.Equals(hudStatusDot.ToolTip as string, compact.TrackingStatus, StringComparison.Ordinal))
                    hudStatusDot.ToolTip = compact.TrackingStatus;
            }
            SyncHudGlassAppearance(compact);
            if ((hudBezel != null || hudSquareBezel != null) && runSlowTick) {
                bool nearDanger = WpfMapSurface.IsNearDangerZone(compact, 150.0);
                var bezelBrush = nearDanger ? WpfFieldTheme.Danger : (staleGps ? WpfFieldTheme.StatusPaused : WpfFieldTheme.Cyan);
                if (hudBezel != null && !object.ReferenceEquals(hudBezel.Stroke, bezelBrush))
                    hudBezel.Stroke = bezelBrush;
                if (hudSquareBezel != null && !object.ReferenceEquals(hudSquareBezel.BorderBrush, bezelBrush))
                    hudSquareBezel.BorderBrush = bezelBrush;
                if (hudAccentBar != null && !object.ReferenceEquals(hudAccentBar.Background, bezelBrush))
                    hudAccentBar.Background = bezelBrush;
            }
            if (staleGps && !locationLabel.EndsWith(WpfFieldTheme.UiText("StaleCoordsHint"), StringComparison.Ordinal))
                locationLabel += WpfFieldTheme.UiText("StaleCoordsHint");
            if (hudGameClock != null && hudRealClock != null) {
                DateTime nowLocal = DateTime.Now;
                long curRealSec = nowLocal.Ticks / TimeSpan.TicksPerSecond;
                if (curRealSec != lastHudRealSecond) {
                    lastHudRealSecond = curRealSec;
                    DateTime nowUtc = nowLocal.ToUniversalTime();
                    string gTxt, rTxt;
                    bool isDay;
                    bool gameFg = tickGameFg;
                    lastTelemetrySecond = nowUtc.Ticks / TimeSpan.TicksPerSecond;
                    if (rawCompactPos != null && !compact.TrackingPaused) {
                        ZiloreAiEngine.ObserveTelemetry(rawCompactPos, CurrentEstimatedSpeedKmh / 3.6, nowUtc);
                        ScumSurvivalTracker.Update(CurrentEstimatedSpeedKmh / 3.6, gameFg, nowUtc);
                        ScumWeatherEngine.Update(rawCompactPos, compactMapPt, gameFg, nowUtc);
                    }
                    ScumGameClock.GetFormatted(nowLocal, out gTxt, out isDay, out rTxt);
                    var wSnap = ScumWeatherEngine.GetSnapshot();
                    if (wSnap.HasPosition && compact.HudWidth >= 220)
                        gTxt += ScumWeatherEngine.FormatCompactHud(wSnap);
                    if (hudGameClock.Text != gTxt)
                        hudGameClock.Text = gTxt;
                    var gBrush = wSnap.HypothermiaRisk ? WpfFieldTheme.Cyan : (isDay ? WpfFieldTheme.Amber : WpfFieldTheme.Cyan);
                    if (!object.ReferenceEquals(hudGameClock.Foreground, gBrush))
                        hudGameClock.Foreground = gBrush;
                    if (hudRealClock.Text != rTxt) {
                        hudRealClock.Text = rTxt;
                        hudRealClock.ToolTip = nowLocal.ToString("dd.MM.yyyy · HH:mm:ss", CultureInfo.InvariantCulture);
                    }
                    if (hudSquadBadge != null && hudSquadCount != null) {
                        int onlinePeers = 0;
                        if (compact.Peers != null && compact.Peers.Length > 0) {
                            for (int pi = 0; pi < compact.Peers.Length; pi++) {
                                var peerItem = compact.Peers[pi];
                                if (peerItem.Online || (peerItem.Updated != DateTime.MinValue && (nowUtc - peerItem.Updated).TotalSeconds < 15.0))
                                    onlinePeers++;
                            }
                        }
                        if (onlinePeers != lastHudSquadOnline) {
                            lastHudSquadOnline = onlinePeers;
                            hudSquadBadge.Visibility = onlinePeers > 0 ? Visibility.Visible : Visibility.Collapsed;
                            if (onlinePeers > 0)
                                hudSquadCount.Text = "👥" + onlinePeers.ToString(CultureInfo.InvariantCulture);
                        }
                    }
                    if (hudSurvivalBars != null) {
                        bool showSurv = PoiAccessGuard.IsUnlocked && compact.HudWidth >= 190;
                        var survVis = showSurv ? Visibility.Visible : Visibility.Collapsed;
                        if (hudSurvivalBars.Visibility != survVis)
                            hudSurvivalBars.Visibility = survVis;
                        if (showSurv) {
                            var snap = ScumSurvivalTracker.GetSnapshot();
                            int hp = (int)Math.Round(Math.Max(0, Math.Min(100, snap.HealthPct)));
                            int fd = (int)Math.Round(Math.Max(0, Math.Min(100, snap.EnergyPct)));
                            int wt = (int)Math.Round(Math.Max(0, Math.Min(100, snap.HydrationPct)));
                            if (hp != lastHudHpPct && hudHpFillCol != null && hudHpRemCol != null) {
                                lastHudHpPct = hp;
                                hudHpFillCol.Width = new GridLength(Math.Max(0.1, hp), GridUnitType.Star);
                                hudHpRemCol.Width = new GridLength(Math.Max(0, 100 - hp), GridUnitType.Star);
                                if (hudHpBar != null)
                                    hudHpBar.Background = hp < 35 ? WpfFieldTheme.Danger : (hp < 65 ? WpfFieldTheme.Amber : WpfFieldTheme.StatusLive);
                            }
                            if (fd != lastHudFoodPct && hudFoodFillCol != null && hudFoodRemCol != null) {
                                lastHudFoodPct = fd;
                                hudFoodFillCol.Width = new GridLength(Math.Max(0.1, fd), GridUnitType.Star);
                                hudFoodRemCol.Width = new GridLength(Math.Max(0, 100 - fd), GridUnitType.Star);
                                if (hudFoodBar != null)
                                    hudFoodBar.Background = fd < 25 ? WpfFieldTheme.Danger : WpfFieldTheme.Amber;
                            }
                            if (wt != lastHudWaterPct && hudWaterFillCol != null && hudWaterRemCol != null) {
                                lastHudWaterPct = wt;
                                hudWaterFillCol.Width = new GridLength(Math.Max(0.1, wt), GridUnitType.Star);
                                hudWaterRemCol.Width = new GridLength(Math.Max(0, 100 - wt), GridUnitType.Star);
                                if (hudWaterBar != null)
                                    hudWaterBar.Background = wt < 25 ? WpfFieldTheme.Danger : WpfFieldTheme.Cyan;
                            }
                        }
                    }
                }
            }
            if (hudSector.Text != sector)
                hudSector.Text = sector;
            if (hudSectorBadge != null) {
                var vis = sector.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                if (hudSectorBadge.Visibility != vis)
                    hudSectorBadge.Visibility = vis;
            }
            if (hudLocation.Text != locationLabel) {
                hudLocation.Text = locationLabel;
                hudLocation.ToolTip = compact.Location;
            }
            bool wantCentered = (state.Target == null && !compact.TrackingPaused);
            if (hudRoute.Centered != wantCentered)
                hudRoute.Centered = wantCentered;
            bool noTargetOrPaused = (state.Target == null || compact.TrackingPaused);
            bool modeFlip = (noTargetOrPaused != lastRouteNoTargetOrPaused);
            if (targetChanged || modeFlip || refreshClock.ElapsedMilliseconds - lastRouteTextRefresh >= 500) {
                lastRouteNoTargetOrPaused = noTargetOrPaused;
                lastRouteTextRefresh = refreshClock.ElapsedMilliseconds;
                if (rawCompactPos != null && !compact.TrackingPaused && !staleGps) {
                    UpdateFlightTelemetry(rawCompactPos, compact.Updated, DateTime.UtcNow);
                    WpfMapSurface.CurrentGroundSpeedMps = CurrentEstimatedSpeedKmh / 3.6;
                } else {
                    WpfMapSurface.CurrentGroundSpeedMps = 0.0;
                }
                if (hudTelemetry != null) {
                    string tTxt = "";
                    if (rawCompactPos != null && !compact.TrackingPaused && compact.HudWidth >= 225 && !WpfMapSurface.AviationModeEnabled) {
                        int spd = (int)Math.Round(CurrentEstimatedSpeedKmh);
                        int hdg = (int)Math.Round(WpfMapSurface.CompassHeading(rawCompactPos.Yaw)) % 360;
                        string trIcon = spd >= 25 ? "🚗 " : (spd >= 3 ? "🚶 " : "");
                        tTxt = trIcon + spd.ToString(CultureInfo.InvariantCulture) + (Localization.Current == AppLanguage.Russian ? " км/ч · " : " km/h · ") + hdg.ToString("000", CultureInfo.InvariantCulture) + "°";
                    }
                    if (lastHudTelText != tTxt) {
                        lastHudTelText = tTxt;
                        hudTelemetry.Text = tTxt;
                    }
                }
                if (WpfMapSurface.AviationModeEnabled && rawCompactPos != null && !compact.TrackingPaused) {
                    bool ru = Localization.Current == AppLanguage.Russian;
                    int altM = (int)Math.Round(rawCompactPos.Z / 100.0);
                    int spdKmh = (int)Math.Round(CurrentEstimatedSpeedKmh);
                    double hdgDeg = WpfMapSurface.CompassHeading(rawCompactPos.Yaw);
                    int hdg = (int)Math.Round(hdgDeg) % 360;
                    double vs = CurrentVerticalSpeedMps;
                    string vsTxt = Math.Abs(vs) < 0.4 ? "" : (vs > 0 ? " ↑+" : " ↓") + vs.ToString("0.0", CultureInfo.InvariantCulture);
                    string tel = "✈ " + altM + (ru ? "м" : "m") + vsTxt + " · " + spdKmh + (ru ? " км/ч · " : " km/h · ") + hdg.ToString("000", CultureInfo.InvariantCulture) + "°";
                    var curMapPt = MapWindow.ToMap(rawCompactPos);
                    string rwyCue = WpfMapSurface.GetRunwayApproachCue(curMapPt, altM);
                    if (rwyCue != null)
                        tel += rwyCue;
                    if (state.Target != null) {
                        double airM = DestinationSearch.Metres(curMapPt, state.Target.Centroid);
                        double brg = (Math.Atan2(state.Target.Centroid.X - curMapPt.X, -(state.Target.Centroid.Y - curMapPt.Y)) * 180.0 / Math.PI + 360.0) % 360.0;
                        double diff = (brg - hdgDeg + 540.0) % 360.0 - 180.0;
                        string bankCue = Math.Abs(diff) <= 7.0 ? "●" : (diff < 0 ? "←" : "→");
                        double effSpeedMps = CurrentEstimatedSpeedKmh >= 60.0 ? CurrentEstimatedSpeedKmh / 3.6 : 44.4;
                        int etaSec = (int)Math.Round(airM / Math.Max(10.0, effSpeedMps));
                        string etaTxt = etaSec < 60 ? (etaSec + (ru ? "с" : "s")) : (Math.Max(1, (int)Math.Round(etaSec / 60.0)) + (ru ? "м" : "m"));
                        double todM = Math.Max(300.0, altM * 4.5);
                        string todHint = (rwyCue == null && altM > 60 && airM <= todM && airM > 150) ? WpfFieldTheme.UiText("DescendHint") : "";
                        tel += " " + bankCue + " " + ((int)Math.Round(brg) % 360).ToString("000", CultureInfo.InvariantCulture) + "° " + state.Target.DisplayName + " (" + DestinationSearch.Distance(airM) + " · ~" + etaTxt + ")" + todHint;
                    }
                    string airAiHint = ZiloreAiEngine.EvaluateCopilot(compact, DateTime.Now);
                    hudRoute.Text = string.IsNullOrEmpty(airAiHint) ? tel : (airAiHint + " · " + tel);
                } else {
                    string groundTxt = compact.TrackingPaused ? compact.TrackingStatus : state.Target == null ? WpfFieldTheme.UiText("HudGrandServer") : "→ " + state.Target.DisplayName + " · " + state.RouteSummary;
                    if (!compact.TrackingPaused && rawCompactPos != null && state.Target != null && state.Route != null && state.Route.Success) {
                        var curMapPt = MapWindow.ToMap(rawCompactPos);
                        var nextTurn = RoadRouter.GetNextTurn(curMapPt, state.Route);
                        if (nextTurn.Valid && nextTurn.DistanceMeters <= 220.0) {
                            string arrow = nextTurn.TurnAngleDeg < -110 ? "↺" : nextTurn.TurnAngleDeg < 0 ? "↰" : nextTurn.TurnAngleDeg > 110 ? "↻" : "↱";
                            int distM = Math.Max(5, (int)(Math.Round(nextTurn.DistanceMeters / 5.0) * 5));
                            groundTxt = arrow + " " + distM + (Localization.Current == AppLanguage.Russian ? "м · " : "m · ") + groundTxt;
                        }
                        // Off-route rerouting is owned by MapWindow's voice/route engine (with proper cooldowns).
                        // Calling WpfTarget/WpfSquadTarget here reset routeGeneration and caused voice navigation to loop.
                    }
                    string aiHint = ZiloreAiEngine.EvaluateCopilot(compact, DateTime.Now);
                    hudRoute.Text = string.IsNullOrEmpty(aiHint) ? groundTxt : aiHint;
                }
            }
            if (fullView) {
                int deathCount = 0;
                if (state.Saved != null)
                for (int di = 0; di < state.Saved.Length; di++)
                if (state.Saved[di].IsDeathMarker)
                    deathCount++;
                bool anyReady = false;
                for (int si = 0; si < sharingSessions.Length; si++)
                if (sharingSessions[si].Ready) {
                    anyReady = true;
                    break;
                }
                var sid = sidebarIdBuilder;
                sid.Length = 0;
                sid.Append(state.Store == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(state.Store)).Append('|')
                   .Append(state.Grid).Append(state.Zones).Append(state.Waypoints).Append(state.History).Append(state.Labels)
                   .Append(Localization.CurrentCode).Append('|').Append(WpfFieldTheme.ActiveTheme).Append('|')
                   .Append(state.Saved == null ? 0 : state.Saved.Length).Append('|').Append(deathCount).Append('|')
                   .Append(state.MapKey).Append('|').Append(state.Bindings.Key(AppControlAction.Search)).Append('|')
                   .Append(state.CustomZonesExpanded).Append('|').Append(PoiAccessGuard.IsUnlocked).Append('|').Append(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(state.CustomZones))
                   .Append('|').Append(WpfMapSurface.AviationModeEnabled).Append(engine.WpfVoiceEnabled).Append((int)engine.WpfRouteMode).Append(engine.WpfAvoidZonesMode).Append(WpfMapSurface.AltLayerHotkeysEnabled).Append(engine.WpfReadyUpdateRelease == null ? "" : engine.WpfReadyUpdateRelease.VersionText)
                   .Append("|players:").Append(sharingSessions.Length == 0 ? "off" : anyReady ? "ready" : "waiting");
                foreach (var peer in playerRoster)
                    sid.Append(';').Append(peer.Id).Append(':').Append(peer.Name).Append(':').Append(peer.Position != null);
                string identity = sid.ToString();
                if (identity != filterIdentity) {
                    filterIdentity = identity;
                    rebuilding = true;
                    try {
                        RebuildSidebar(state);
                    } finally { rebuilding = false; }
                }
                SyncPoiControls(state.Store);
            }
            if (!diagnostic && refresh != null) {
                bool fastNeeded = engine.WpfHasPendingTacticalZoom || tacticalMap.PointerCameraActive || tacticalMap.HasActivePulse || hudMap.HasActivePulse || smoothHudInterpolating || peersInterpolating;
                int targetMs = fastNeeded ? 16 : 45;
                if ((int)refresh.Interval.TotalMilliseconds != targetMs) refresh.Interval = TimeSpan.FromMilliseconds(targetMs);
            }
        }
    }
}
