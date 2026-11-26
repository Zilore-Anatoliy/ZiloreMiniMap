using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using DrawingPoint = System.Drawing.PointF;

namespace ZiloreMiniMap {
    internal enum AiTransportMode { Foot, Car, Boat, Aircraft }

    internal sealed class ZiloreAiStop {
        internal string Name;
        internal string CategoryHint;
        internal DrawingPoint Point;
        internal double LegDistanceMeters;
    }

    internal sealed class ZiloreAiPlanResult {
        internal bool Success;
        internal bool AvoidDangerZones;
        internal bool UseAviation;
        internal bool IsLoop;
        internal double RadiusMeters;
        internal double TotalDistanceMeters;
        internal double EstimatedSeconds;
        internal string Summary;
        internal ZiloreAiStop[] Stops = Array.Empty<ZiloreAiStop>();
    }

    /// <summary>
    /// Zilore AI (v4.4.0 — Enterprise Local Tactical Engine):
    /// 1) Zero-GC cached POI index + Intent Parser (sectors, categories, places, loop, PvP avoid, aviation)
    /// 2) Thread-safe Proactive Tactical Copilot (danger zones, sunset/nightfall, stale GPS)
    /// 3) Self-learning Adaptive ETA profile per transport mode (Foot/Car/Boat/Aircraft) + History
    /// 100% EAC-safe, zero game memory access, zero FPS impact.
    /// </summary>
    internal static partial class ZiloreAiEngine {
        static readonly object gate = new object();
        static string profileIniPath;
        static DateTime lastSaveUtc = DateTime.MinValue;
        static bool dirtyProfile;
        static int aiSaveInFlight;

        internal static volatile bool CopilotEnabled = true;

        // Learned average speeds (m/s) with realistic SCUM defaults
        static double footSpeedMps = 4.2;     // ~15 km/h jog/sprint avg
        static double carSpeedMps = 15.5;     // ~56 km/h avg road speed
        static double boatSpeedMps = 11.1;    // ~40 km/h water speed
        static double airSpeedMps = 44.4;     // ~160 km/h cruise airspeed

        // Copilot alert cooldowns (guarded by gate)
        static DateTime lastSunsetAlertUtc = DateTime.MinValue;
        static DateTime lastDangerAlertUtc = DateTime.MinValue;
        static DateTime lastStaleAlertUtc = DateTime.MinValue;
        static DateTime lastNavAlertUtc = DateTime.MinValue;
        static string activeCopilotHint = "";
        static DateTime copilotHintExpiresUtc = DateTime.MinValue;

        // Kinematic Wind / Crosswind Drift Estimator (100% EAC-safe from GPS track vs Yaw)
        static DrawingPoint? prevTrackPt;
        static DateTime prevTrackUtc = DateTime.MinValue;
        static double smoothedDriftDeg = 0.0;
        static double smoothedCrosswindMps = 0.0;
        static AiTransportMode lastDetectedMode = AiTransportMode.Foot;
        static double lastObservedSpeedMps = 0.0;

        // Recent prompt history (up to 8 entries)
        static readonly List<string> recentPrompts = new List<string>(8);

        // Compile-time .NET 10 [GeneratedRegex] parsers (zero startup JIT/regex compilation)
        [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*(?:км|km)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex RxKm();
        [GeneratedRegex(@"(\d{3,5})\s*(?:м|m)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex RxMeters();
        [GeneratedRegex(@"(?:->|→|\bпотом\b|\bзатем\b|\bthen\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex RxStages();
        [GeneratedRegex(@"(?:\+|,|\s+и\s+|\s+and\s+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex RxParts();
        [GeneratedRegex(@"\b([2-4])\b", RegexOptions.CultureInvariant)]
        private static partial Regex RxCount();
        [GeneratedRegex(@"\b([DCBAZ][0-4])\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex RxSector();
        [GeneratedRegex(@"^\d+\s*(км|km|м|m)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex RxPureDist();
        [GeneratedRegex(@"\b(ближайш\w*|nearest|closest|в\s+радиусе.*|within.*|объезд.*|avoid.*|кольц\w*|обратн\w*|loop|roundtrip)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex RxCleanPlace();

        // Cached lowercase POI index per ScumMapStore (eliminates per-query ToLowerInvariant allocations)
        sealed class IndexedMarker {
            internal ScumMapMarker Marker;
            internal string SearchHaystack;
            internal string TitleLower;
            internal string LocalTitleLower;
            internal string DisplayName;
            internal DrawingPoint Point;
        }
        sealed class StoreIndexEntry { internal string LanguageCode; internal IndexedMarker[] Items; internal IndexedMarker[] Traders; }
        static readonly ConditionalWeakTable<ScumMapStore, StoreIndexEntry> storeIndexCache = new ConditionalWeakTable<ScumMapStore, StoreIndexEntry>();

        static StoreIndexEntry GetStoreIndexEntry(ScumMapStore store) {
            if (store == null || store.Markers == null || store.Markers.Count == 0) return null;
            string lang = Localization.CurrentCode;
            var entry = storeIndexCache.GetOrCreateValue(store);
            if (entry.Items != null && entry.Traders != null && entry.LanguageCode == lang && entry.Items.Length == store.Markers.Count) return entry;
            var list = new IndexedMarker[store.Markers.Count];
            var traders = new List<IndexedMarker>(8);
            for (int i = 0; i < store.Markers.Count; i++) {
                var m = store.Markers[i];
                ScumMapCategory cat;
                store.CategoryById.TryGetValue(m.CategoryId, out cat);
                string title = m.Title ?? "";
                string disp = Localization.GetMapObjectName(store.GetMarkerDisplayName(m));
                string hay = ((cat == null ? "" : (cat.Section + " " + cat.Name + " ")) + title).ToLowerInvariant();
                var im = new IndexedMarker {
                    Marker = m,
                    SearchHaystack = hay,
                    TitleLower = title.ToLowerInvariant(),
                    LocalTitleLower = (disp ?? "").ToLowerInvariant(),
                    DisplayName = disp,
                    Point = m.Point
                };
                list[i] = im;
                if (hay.Contains("trader") || hay.Contains("outpost")) traders.Add(im);
            }
            entry.LanguageCode = lang;
            entry.Items = list;
            entry.Traders = traders.Count == 0 ? Array.Empty<IndexedMarker>() : traders.ToArray();
            return entry;
        }

        static IndexedMarker[] GetIndexedMarkers(ScumMapStore store) {
            var entry = GetStoreIndexEntry(store);
            return entry == null ? Array.Empty<IndexedMarker>() : entry.Items;
        }

        internal static ZiloreAiStop FindNearestCachedTrader(ScumMapStore store, DrawingPoint from, double maxDistM) {
            var entry = GetStoreIndexEntry(store);
            if (entry == null || entry.Traders.Length == 0) return null;
            IndexedMarker best = null; double bestDist = maxDistM;
            for (int i = 0; i < entry.Traders.Length; i++) {
                var item = entry.Traders[i];
                double d = DestinationSearch.Metres(from, item.Point);
                if (d < bestDist) { bestDist = d; best = item; }
            }
            return best == null ? null : new ZiloreAiStop { Name = best.DisplayName, Point = best.Point };
        }

        internal static void Load(string dataFolder) {
            if (string.IsNullOrWhiteSpace(dataFolder)) return;
            lock (gate) {
                profileIniPath = Path.Combine(dataFolder, "zilore-ai-profile.ini");
                try {
                    if (!File.Exists(profileIniPath)) return;
                    var ini = SettingsStore.ReadIni(profileIniPath);
                    string v; double d;
                    if (ini.TryGetValue("CopilotEnabled", out v)) CopilotEnabled = v != "0";
                    if (ini.TryGetValue("FootMps", out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                        footSpeedMps = Clamp(d, 2.0, 9.0);
                    if (ini.TryGetValue("CarMps", out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                        carSpeedMps = Clamp(d, 7.0, 38.0);
                    if (ini.TryGetValue("BoatMps", out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                        boatSpeedMps = Clamp(d, 4.0, 25.0);
                    if (ini.TryGetValue("AirMps", out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                        airSpeedMps = Clamp(d, 22.0, 75.0);
                    if (ini.TryGetValue("RecentPrompts", out v) && !string.IsNullOrWhiteSpace(v)) {
                        recentPrompts.Clear();
                        foreach (string item in v.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries)) {
                            string t = item.Trim();
                            if (t.Length > 0 && recentPrompts.Count < 8) recentPrompts.Add(t);
                        }
                    }
                } catch (Exception ex) {
                    Program.LogException("ZiloreAiLoad", ex);
                }
            }
        }

        internal static void ResetSpeedProfile() {
            lock (gate) {
                footSpeedMps = 4.2;
                carSpeedMps = 15.5;
                boatSpeedMps = 11.1;
                airSpeedMps = 44.4;
                dirtyProfile = true;
                SaveLocked();
            }
        }

        internal static void RecordPromptHistory(string prompt) {
            if (string.IsNullOrWhiteSpace(prompt)) return;
            string clean = prompt.Trim().Replace("||", " ").Replace("\r", " ").Replace("\n", " ");
            if (clean.Length < 2 || clean.Length > 120) return;
            lock (gate) {
                recentPrompts.RemoveAll(p => string.Equals(p, clean, StringComparison.OrdinalIgnoreCase));
                recentPrompts.Insert(0, clean);
                if (recentPrompts.Count > 8) recentPrompts.RemoveRange(8, recentPrompts.Count - 8);
                dirtyProfile = true;
                SaveLocked();
            }
        }

        internal static string[] GetRecentPrompts() {
            lock (gate) { return recentPrompts.ToArray(); }
        }

        internal static void ClearPromptHistory() {
            lock (gate) { recentPrompts.Clear(); dirtyProfile = true; SaveLocked(); }
        }

        internal static void SaveNow() {
            lock (gate) { SaveLocked(); }
        }

        static string BuildProfileBodyLocked() {
            string hist = string.Join("||", recentPrompts);
            return "CopilotEnabled=" + (CopilotEnabled ? "1" : "0") + Environment.NewLine
                + "FootMps=" + footSpeedMps.ToString("0.00", CultureInfo.InvariantCulture) + Environment.NewLine
                + "CarMps=" + carSpeedMps.ToString("0.00", CultureInfo.InvariantCulture) + Environment.NewLine
                + "BoatMps=" + boatSpeedMps.ToString("0.00", CultureInfo.InvariantCulture) + Environment.NewLine
                + "AirMps=" + airSpeedMps.ToString("0.00", CultureInfo.InvariantCulture) + Environment.NewLine
                + "RecentPrompts=" + hist + Environment.NewLine;
        }

        static void SaveLocked() {
            if (string.IsNullOrWhiteSpace(profileIniPath)) return;
            try {
                SettingsStore.WriteAtomic(profileIniPath, BuildProfileBodyLocked());
                dirtyProfile = false;
                lastSaveUtc = DateTime.UtcNow;
            } catch (Exception ex) {
                Program.LogException("ZiloreAiSave", ex);
            }
        }

        static void ScheduleSaveLocked() {
            if (string.IsNullOrWhiteSpace(profileIniPath)) return;
            dirtyProfile = true;
            if (System.Threading.Interlocked.CompareExchange(ref aiSaveInFlight, 1, 0) != 0) return;
            System.Threading.Tasks.Task.Run(() => {
                try {
                    string path, body;
                    lock (gate) {
                        if (!dirtyProfile || string.IsNullOrWhiteSpace(profileIniPath)) return;
                        path = profileIniPath;
                        body = BuildProfileBodyLocked();
                        dirtyProfile = false;
                        lastSaveUtc = DateTime.UtcNow;
                    }
                    SettingsStore.WriteAtomic(path, body);
                } catch (Exception ex) {
                    Program.LogException("ZiloreAiSave", ex);
                } finally {
                    System.Threading.Interlocked.Exchange(ref aiSaveInFlight, 0);
                }
            });
        }

        internal static AiTransportMode DetectTransportMode(DrawingPoint mapPt, double speedMps) {
            if (WpfMapSurface.AviationModeEnabled) return AiTransportMode.Aircraft;
            if (RoadRouter.Instance.IsWaterAt(mapPt)) return AiTransportMode.Boat;
            return speedMps >= 8.5 ? AiTransportMode.Car : AiTransportMode.Foot;
        }

        internal static void ObserveTelemetry(Position pos, double speedMps, DateTime nowUtc) {
            if (!PoiAccessGuard.IsUnlocked || pos == null) return;
            if (double.IsNaN(speedMps) || double.IsInfinity(speedMps) || speedMps < 0.0 || speedMps > 110.0) return;
            DrawingPoint mapPt = MapWindow.ToMap(pos);
            AiTransportMode mode = DetectTransportMode(mapPt, speedMps);

            lock (gate) {
                lastDetectedMode = mode;
                lastObservedSpeedMps = speedMps;

                // 1. Kinematic Wind / Crosswind Drift estimation (Aircraft & Boat when moving > 4 m/s)
                if (prevTrackPt.HasValue && (nowUtc - prevTrackUtc).TotalSeconds >= 0.4 && (nowUtc - prevTrackUtc).TotalSeconds <= 4.0) {
                    double moveM = DestinationSearch.Metres(prevTrackPt.Value, mapPt);
                    if (moveM >= 4.0 && speedMps >= 4.0 && (mode == AiTransportMode.Aircraft || mode == AiTransportMode.Boat)) {
                        double trackDeg = (Math.Atan2(mapPt.X - prevTrackPt.Value.X, -(mapPt.Y - prevTrackPt.Value.Y)) * 180.0 / Math.PI + 360.0) % 360.0;
                        double hdgDeg = WpfMapSurface.CompassHeading(pos.Yaw);
                        double drift = (trackDeg - hdgDeg + 540.0) % 360.0 - 180.0;
                        if (Math.Abs(drift) <= 45.0) {
                            smoothedDriftDeg = smoothedDriftDeg * 0.75 + drift * 0.25;
                            double crossMps = Math.Abs(speedMps * Math.Sin(smoothedDriftDeg * Math.PI / 180.0));
                            smoothedCrosswindMps = smoothedCrosswindMps * 0.75 + crossMps * 0.25;
                        }
                    } else if (speedMps < 2.0) {
                        smoothedDriftDeg *= 0.8;
                        smoothedCrosswindMps *= 0.8;
                    }
                }
                prevTrackPt = mapPt;
                prevTrackUtc = nowUtc;

                // 2. Self-learning EMA speed profile per transport mode
                const double alpha = 0.04;
                if (mode == AiTransportMode.Aircraft && speedMps >= 18.0) {
                    airSpeedMps = Clamp(airSpeedMps * (1.0 - alpha) + speedMps * alpha, 22.0, 75.0);
                    dirtyProfile = true;
                } else if (mode == AiTransportMode.Boat && speedMps >= 3.0 && speedMps <= 28.0) {
                    boatSpeedMps = Clamp(boatSpeedMps * (1.0 - alpha) + speedMps * alpha, 4.0, 25.0);
                    dirtyProfile = true;
                } else if (mode == AiTransportMode.Foot && speedMps >= 1.5 && speedMps <= 8.5) {
                    footSpeedMps = Clamp(footSpeedMps * (1.0 - alpha) + speedMps * alpha, 2.0, 9.0);
                    dirtyProfile = true;
                } else if (mode == AiTransportMode.Car && speedMps >= 9.0 && speedMps <= 42.0) {
                    carSpeedMps = Clamp(carSpeedMps * (1.0 - alpha) + speedMps * alpha, 7.0, 38.0);
                    dirtyProfile = true;
                }
                if (dirtyProfile && (nowUtc - lastSaveUtc).TotalSeconds >= 60.0) {
                    ScheduleSaveLocked();
                }
            }
        }

        internal static string GetLiveNavigationTelemetry(bool ru) {
            lock (gate) {
                string modeName = lastDetectedMode == AiTransportMode.Aircraft ? (ru ? "✈ Самолёт" : "✈ Aircraft")
                    : lastDetectedMode == AiTransportMode.Boat ? (ru ? "⛵ Лодка" : "⛵ Boat")
                    : lastDetectedMode == AiTransportMode.Car ? (ru ? "🚗 Машина" : "🚗 Car")
                    : (ru ? "🚶 Пешком" : "🚶 Foot");
                int spdKmh = (int)Math.Round(lastObservedSpeedMps * 3.6);
                string driftTxt = "";
                if ((lastDetectedMode == AiTransportMode.Aircraft || lastDetectedMode == AiTransportMode.Boat) && Math.Abs(smoothedDriftDeg) >= 2.5) {
                    string dir = smoothedDriftDeg > 0 ? "→" : "←";
                    driftTxt = ru
                        ? string.Format(CultureInfo.InvariantCulture, " · 💨 Снос {0}{1:0}° ({2:0.0} м/с)", dir, Math.Abs(smoothedDriftDeg), smoothedCrosswindMps)
                        : string.Format(CultureInfo.InvariantCulture, " · 💨 Drift {0}{1:0}° ({2:0.0} m/s)", dir, Math.Abs(smoothedDriftDeg), smoothedCrosswindMps);
                }
                return modeName + " · " + spdKmh + (ru ? " км/ч" : " km/h") + driftTxt;
            }
        }

        internal static string GetSpeedProfileSummary(bool ru) {
            lock (gate) {
                int f = (int)Math.Round(footSpeedMps * 3.6);
                int c = (int)Math.Round(carSpeedMps * 3.6);
                int b = (int)Math.Round(boatSpeedMps * 3.6);
                int a = (int)Math.Round(airSpeedMps * 3.6);
                string u = ru ? "км/ч" : "km/h";
                return string.Format(CultureInfo.InvariantCulture, "🚶 {0} · 🚗 {1} · ⛵ {2} · ✈ {3} {4}", f, c, b, a, u);
            }
        }

        internal static double EstimateSeconds(double distMeters, bool isAir, bool isWater, bool isRoad) {
            double spd;
            lock (gate) {
                spd = isAir ? airSpeedMps : (isWater ? boatSpeedMps : (isRoad ? carSpeedMps : footSpeedMps));
            }
            return distMeters / Math.Max(1.0, spd);
        }

        internal static string FormatDuration(double seconds, bool ru) {
            int sec = Math.Max(1, (int)Math.Round(seconds));
            if (sec < 60) return sec + (ru ? " с" : "s");
            int mins = (int)Math.Round(sec / 60.0);
            if (mins < 60) return "~" + mins + (ru ? " мин" : "m");
            int h = mins / 60;
            int m = mins % 60;
            return ru ? ("~" + h + " ч " + m + " м") : ("~" + h + "h " + m + "m");
        }

        internal static string EvaluateCopilot(WpfMapState state, DateTime nowLocal) {
            if (!PoiAccessGuard.IsUnlocked || !CopilotEnabled || state == null) return "";
            DateTime nowUtc = nowLocal.ToUniversalTime();
            bool ru = Localization.Current == AppLanguage.Russian;

            lock (gate) {
                if (!string.IsNullOrEmpty(activeCopilotHint) && nowUtc < copilotHintExpiresUtc) {
                    return activeCopilotHint;
                }
                activeCopilotHint = "";

                // 1. Danger / PvP Zone Proximity Alert (cooldown 45s)
                if (state.Position != null && !state.TrackingPaused && (nowUtc - lastDangerAlertUtc).TotalSeconds >= 45.0) {
                    if (WpfMapSurface.IsNearDangerZone(state, 220.0)) {
                        lastDangerAlertUtc = nowUtc;
                        activeCopilotHint = ru
                            ? "🤖 AI: Рядом опасная/PvP зона (<220 м) — будьте осторожны!"
                            : "🤖 AI: Danger/PvP zone ahead (<220m) — stay alert!";
                        copilotHintExpiresUtc = nowUtc.AddSeconds(10);
                        return activeCopilotHint;
                    }
                }

                // 2. Sunset / Nightfall Tactical Warning (18:45 - 19:30 game time, cooldown 10 min)
                if (state.Position != null && !state.TrackingPaused && (nowUtc - lastSunsetAlertUtc).TotalMinutes >= 10.0) {
                    double gMins = ScumGameClock.GetGameMinutesUtc(nowUtc);
                    if (gMins >= 18 * 60 + 45 && gMins <= 19 * 60 + 30) {
                        lastSunsetAlertUtc = nowUtc;
                        string traderHint = "";
                        if (state.Store != null) {
                            DrawingPoint cur = MapWindow.ToMap(state.Position);
                            var tr = FindNearestCachedTrader(state.Store, cur, 15000.0);
                            if (tr != null) {
                                double dm = DestinationSearch.Metres(cur, tr.Point);
                                traderHint = ru
                                    ? (" · ближайший трейдер " + DestinationSearch.Distance(dm))
                                    : (" · nearest trader " + DestinationSearch.Distance(dm));
                            }
                        }
                        activeCopilotHint = (ru ? "🤖 AI: Скоро стемнеет (закат)" : "🤖 AI: Nightfall approaching") + traderHint;
                        copilotHintExpiresUtc = nowUtc.AddSeconds(12);
                        return activeCopilotHint;
                    }
                }

                // 3. Stale GPS Alert (cooldown 60s)
                if (state.Position != null && state.Tracking && !state.TrackingPaused && state.Updated != DateTime.MinValue) {
                    if ((nowUtc - state.Updated).TotalSeconds >= 14.0 && (nowUtc - lastStaleAlertUtc).TotalSeconds >= 60.0) {
                        lastStaleAlertUtc = nowUtc;
                        activeCopilotHint = ru
                            ? "🤖 AI: Нет новых координат >14с — проверьте клавишу Copy Location"
                            : "🤖 AI: No GPS updates >14s — check Copy Location key";
                        copilotHintExpiresUtc = nowUtc.AddSeconds(10);
                        return activeCopilotHint;
                    }
                }
                // 4. Survival & Metabolism Copilot Alert (HP / Water / Food)
                string survHint = ScumSurvivalTracker.EvaluateSurvivalCopilot(nowUtc, ru);
                if (!string.IsNullOrEmpty(survHint)) {
                    activeCopilotHint = survHint;
                    copilotHintExpiresUtc = nowUtc.AddSeconds(10);
                    return activeCopilotHint;
                }

                // 5. 4-Mode Navigation & Wind/Speed Tactical Hints (cooldown 20s)
                if (state.Position != null && !state.TrackingPaused && (nowUtc - lastNavAlertUtc).TotalSeconds >= 20.0) {
                    DrawingPoint curPt = MapWindow.ToMap(state.Position);
                    AiTransportMode mode = lastDetectedMode;
                    double spdKmh = lastObservedSpeedMps * 3.6;

                    if (mode == AiTransportMode.Aircraft) {
                        double altM = state.Position.Z / 100.0;
                        if (altM > 30.0 && spdKmh > 15.0 && spdKmh < 68.0) {
                            lastNavAlertUtc = nowUtc;
                            activeCopilotHint = ru
                                ? "🤖 AI ✈: Низкая скорость (" + (int)spdKmh + " км/ч) — риск сваливания!"
                                : "🤖 AI ✈: Low airspeed (" + (int)spdKmh + " km/h) — stall risk!";
                            copilotHintExpiresUtc = nowUtc.AddSeconds(6);
                            return activeCopilotHint;
                        }
                        if (Math.Abs(smoothedDriftDeg) >= 6.0 && spdKmh >= 60.0) {
                            lastNavAlertUtc = nowUtc;
                            string corr = smoothedDriftDeg > 0 ? "←" : "→";
                            activeCopilotHint = ru
                                ? string.Format(CultureInfo.InvariantCulture, "🤖 AI 💨: Боковой ветер {0:0.0} м/с — держите упреждение {1}{2:0}°", smoothedCrosswindMps, corr, Math.Abs(smoothedDriftDeg))
                                : string.Format(CultureInfo.InvariantCulture, "🤖 AI 💨: Crosswind {0:0.0} m/s — crab {1}{2:0}°", smoothedCrosswindMps, corr, Math.Abs(smoothedDriftDeg));
                            copilotHintExpiresUtc = nowUtc.AddSeconds(7);
                            return activeCopilotHint;
                        }
                    } else if (mode == AiTransportMode.Boat) {
                        if (state.Target != null && !RoadRouter.Instance.IsDirectWaterClear(curPt, state.Target.Centroid)) {
                            double distM = DestinationSearch.Metres(curPt, state.Target.Centroid);
                            if (distM > 120.0 && distM < 2500.0) {
                                lastNavAlertUtc = nowUtc;
                                activeCopilotHint = ru
                                    ? "🤖 AI ⛵: По курсу суша/берег — планируйте обход или причаливание"
                                    : "🤖 AI ⛵: Land/shore ahead on direct course — plan detour or landing";
                                copilotHintExpiresUtc = nowUtc.AddSeconds(8);
                                return activeCopilotHint;
                            }
                        }
                        if (Math.Abs(smoothedDriftDeg) >= 8.0 && spdKmh >= 15.0) {
                            lastNavAlertUtc = nowUtc;
                            string corr = smoothedDriftDeg > 0 ? "←" : "→";
                            activeCopilotHint = ru
                                ? string.Format(CultureInfo.InvariantCulture, "🤖 AI ⛵: Снос волной/ветром {0}{1:0}° — скорректируйте курс", corr, Math.Abs(smoothedDriftDeg))
                                : string.Format(CultureInfo.InvariantCulture, "🤖 AI ⛵: Water/wind drift {0}{1:0}° — adjust heading", corr, Math.Abs(smoothedDriftDeg));
                            copilotHintExpiresUtc = nowUtc.AddSeconds(6);
                            return activeCopilotHint;
                        }
                    } else if (mode == AiTransportMode.Car && spdKmh >= 55.0 && state.Route != null && state.Route.Success) {
                        var turn = RoadRouter.GetNextTurn(curPt, state.Route);
                        if (turn.Valid && turn.DistanceMeters <= 150.0 && Math.Abs(turn.TurnAngleDeg) >= 55.0) {
                            lastNavAlertUtc = nowUtc;
                            string dir = turn.TurnAngleDeg < 0 ? (ru ? "налево" : "left") : (ru ? "направо" : "right");
                            activeCopilotHint = ru
                                ? "🤖 AI 🚗: Резкий поворот " + dir + " через " + (int)turn.DistanceMeters + "м — сбросьте скорость!"
                                : "🤖 AI 🚗: Sharp " + dir + " turn in " + (int)turn.DistanceMeters + "m — slow down!";
                            copilotHintExpiresUtc = nowUtc.AddSeconds(6);
                            return activeCopilotHint;
                        }
                    } else if (mode == AiTransportMode.Foot && state.Target != null) {
                        double distM = DestinationSearch.Metres(curPt, state.Target.Centroid);
                        if (distM >= 2500.0 && spdKmh >= 10.0) {
                            lastNavAlertUtc = nowUtc;
                            activeCopilotHint = ru
                                ? "🤖 AI 🚶: До цели " + DestinationSearch.Distance(distM) + " пешком — берегите выносливость и воду"
                                : "🤖 AI 🚶: " + DestinationSearch.Distance(distM) + " on foot — pace stamina & hydration";
                            copilotHintExpiresUtc = nowUtc.AddSeconds(8);
                            return activeCopilotHint;
                        }
                    }
                }
            }
            return "";
        }

        sealed class IntentRule {
            internal string DisplayRu, DisplayEn;
            internal string[] Triggers;
            internal string[] StoreTerms;
        }

        static readonly IntentRule[] Rules = new[] {
            new IntentRule { DisplayRu = "Трейдер", DisplayEn = "Trader",
                Triggers = new[] { "трейдер", "торгов", "аванпост", "магазин", "trader", "outpost", "shop" },
                StoreTerms = new[] { "trader", "outpost" } },
            new IntentRule { DisplayRu = "Заправка", DisplayEn = "Gas Station",
                Triggers = new[] { "заправ", "бенз", "топлив", "азс", "gas", "fuel", "petrol" },
                StoreTerms = new[] { "gas", "fuel" } },
            new IntentRule { DisplayRu = "Бункер", DisplayEn = "Bunker",
                Triggers = new[] { "бункер", "bunker" },
                StoreTerms = new[] { "bunker" } },
            new IntentRule { DisplayRu = "Военка", DisplayEn = "Military",
                Triggers = new[] { "воен", "армей", "база", "аэродром", "впп", "military", "airfield", "army" },
                StoreTerms = new[] { "military", "airfield", "bunker" } },
            new IntentRule { DisplayRu = "Полиция", DisplayEn = "Police",
                Triggers = new[] { "полиц", "участок", "police" },
                StoreTerms = new[] { "police" } },
            new IntentRule { DisplayRu = "Медицина", DisplayEn = "Medical",
                Triggers = new[] { "медиц", "больниц", "госпит", "аптек", "лекар", "medic", "hospital", "clinic" },
                StoreTerms = new[] { "hospital", "medic", "clinic", "pharmacy" } },
            new IntentRule { DisplayRu = "Транспорт / Гараж", DisplayEn = "Garage / Vehicle",
                Triggers = new[] { "гараж", "авто", "машин", "ремонт", "запчаст", "механ", "garage", "vehicle", "mechanic" },
                StoreTerms = new[] { "garage", "vehicle", "mechanic", "workshop" } },
            new IntentRule { DisplayRu = "Город / Лут", DisplayEn = "Town / Loot",
                Triggers = new[] { "город", "посел", "посёл", "деревн", "еда", "лут", "town", "city", "village", "loot" },
                StoreTerms = new[] { "town", "city", "village" } },
            new IntentRule { DisplayRu = "Охота / Рыбалка", DisplayEn = "Hunt / Fish",
                Triggers = new[] { "охот", "рыбал", "рыб", "hunt", "fish" },
                StoreTerms = new[] { "hunt", "fish" } }
        };

        static IntentRule FindMatchingRule(string pl) {
            for (int ri = 0; ri < Rules.Length; ri++) {
                string[] triggers = Rules[ri].Triggers;
                for (int ti = 0; ti < triggers.Length; ti++) {
                    if (pl.Contains(triggers[ti])) return Rules[ri];
                }
            }
            return null;
        }

        internal static bool TryParseSectorCenter(string token, out string sectorCode, out DrawingPoint center) {
            sectorCode = null;
            center = DrawingPoint.Empty;
            if (string.IsNullOrWhiteSpace(token)) return false;
            Match m = RxSector().Match(token);
            if (!m.Success) return false;
            string code = m.Groups[1].Value.ToUpperInvariant();
            int row = "DCBAZ".IndexOf(code[0]);
            int col = 4 - (code[1] - '0');
            if (row < 0 || row > 4 || col < 0 || col > 4) return false;
            sectorCode = code;
            center = new DrawingPoint((col + 0.5f) / 5.0f, (row + 0.5f) / 5.0f);
            return true;
        }

        internal static ZiloreAiPlanResult BuildRouteFromPrompt(string prompt, WpfMapState state) {
            bool ru = Localization.Current == AppLanguage.Russian;
            var result = new ZiloreAiPlanResult { Success = false, RadiusMeters = 6000.0 };
            if (!PoiAccessGuard.IsUnlocked) {
                result.Summary = ru ? "🔒 Доступ к Zilore AI открывается по коду доступа." : "🔒 Zilore AI requires an active access code.";
                return result;
            }
            if (string.IsNullOrWhiteSpace(prompt) || state == null) {
                result.Summary = ru ? "Введите запрос, например: заправка -> трейдер или B2 -> бункер" : "Enter a prompt, e.g. gas -> trader or B2 -> bunker";
                return result;
            }

            string raw = prompt.Trim();
            string lower = raw.ToLowerInvariant();

            result.AvoidDangerZones = lower.Contains("объезд") || lower.Contains("pvp") || lower.Contains("пвп")
                || lower.Contains("безопас") || lower.Contains("avoid") || lower.Contains("safe");
            result.UseAviation = WpfMapSurface.AviationModeEnabled
                || lower.Contains("полёт") || lower.Contains("полет") || lower.Contains("самол") || lower.Contains("flight") || lower.Contains("air");
            result.IsLoop = lower.Contains("кольц") || lower.Contains("обратн") || lower.Contains("loop") || lower.Contains("roundtrip");

            Match kmMatch = RxKm().Match(lower);
            Match mMatch = RxMeters().Match(lower);
            if (kmMatch.Success) {
                double km;
                if (double.TryParse(kmMatch.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out km))
                    result.RadiusMeters = Clamp(km * 1000.0, 400.0, 16000.0);
            } else if (mMatch.Success) {
                double m;
                if (double.TryParse(mMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out m))
                    result.RadiusMeters = Clamp(m, 300.0, 16000.0);
            }

            DrawingPoint anchor = state.Position != null ? MapWindow.ToMap(state.Position) : state.Pan;
            if (anchor.X <= 0 || anchor.X >= 1 || anchor.Y <= 0 || anchor.Y >= 1) anchor = new DrawingPoint(0.5f, 0.5f);

            string[] stages = RxStages().Split(raw);
            var finalStops = new List<ZiloreAiStop>(16);
            var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DrawingPoint cursor = anchor;

            for (int si = 0; si < stages.Length && finalStops.Count < SquadState.MaximumStops; si++) {
                string stageText = stages[si].Trim();
                if (stageText.Length == 0) continue;

                string[] parts = RxParts().Split(stageText);
                var stageStops = new List<ZiloreAiStop>();

                foreach (string partRaw in parts) {
                    string p = partRaw.Trim();
                    if (p.Length < 2) continue;
                    string pl = p.ToLowerInvariant();
                    if (pl.Contains("объезд") || pl.Contains("avoid") || pl.Contains("радиус") || pl.Contains("within") || pl.Contains("кольц") || pl.Contains("loop") || RxPureDist().IsMatch(pl)) {
                        if (FindMatchingRule(pl) == null && !RxSector().IsMatch(p)) continue;
                    }

                    int desiredCount = 1;
                    Match cntMatch = RxCount().Match(pl);
                    if (cntMatch.Success && !pl.Contains("км") && !pl.Contains("km") && !pl.Contains('м') && !RxSector().IsMatch(p)) {
                        if (!int.TryParse(cntMatch.Groups[1].Value, out desiredCount)) desiredCount = 1;
                    }

                    IntentRule matchedRule = FindMatchingRule(pl);

                    string secCode;
                    DrawingPoint secPt;
                    bool hasSector = TryParseSectorCenter(p, out secCode, out secPt);
                    DrawingPoint searchFrom = hasSector ? secPt : cursor;

                    if (hasSector && matchedRule == null) {
                        // Direct sector target (e.g. "B2" or "D4")
                        string secName = (ru ? "Сектор " : "Sector ") + secCode;
                        if (!usedKeys.Contains(StopDedupKey(secPt))) {
                            usedKeys.Add(StopDedupKey(secPt));
                            stageStops.Add(new ZiloreAiStop { Name = secName, CategoryHint = ru ? "Сектор" : "Sector", Point = secPt });
                            cursor = secPt;
                        }
                        continue;
                    }

                    for (int c = 0; c < desiredCount && (finalStops.Count + stageStops.Count) < SquadState.MaximumStops; c++) {
                        ZiloreAiStop found = null;
                        if (matchedRule != null && state.Store != null) {
                            found = FindNearestByKeywords(state.Store, searchFrom, matchedRule.StoreTerms, result.RadiusMeters, usedKeys);
                            if (found == null) found = FindNearestByKeywords(state.Store, searchFrom, matchedRule.StoreTerms, 20000.0, usedKeys);
                            if (found != null) found.CategoryHint = ru ? matchedRule.DisplayRu : matchedRule.DisplayEn;
                        }
                        if (found == null) {
                            found = FindByPlaceName(state, searchFrom, p, usedKeys);
                        }
                        if (found != null) {
                            usedKeys.Add(StopDedupKey(found.Point));
                            stageStops.Add(found);
                            cursor = found.Point;
                            if (!hasSector) searchFrom = found.Point;
                        }
                    }
                }

                if (stageStops.Count > 1) {
                    DrawingPoint stageStart = finalStops.Count > 0 ? finalStops[finalStops.Count - 1].Point : anchor;
                    stageStops = OptimizeOrder(stageStart, stageStops);
                }
                finalStops.AddRange(stageStops);
                if (finalStops.Count > 0) cursor = finalStops[finalStops.Count - 1].Point;
            }

            if (finalStops.Count == 0) {
                result.Summary = ru
                    ? "Не удалось найти подходящие точки. Попробуйте: «заправка -> трейдер» или «бункер в B2»."
                    : "No matching POIs found. Try: 'gas -> trader' or 'bunker in B2'.";
                return result;
            }

            // Optional roundtrip / loop back to start
            if (result.IsLoop && finalStops.Count < SquadState.MaximumStops) {
                if (DestinationSearch.Metres(finalStops[finalStops.Count - 1].Point, anchor) > 80.0) {
                    finalStops.Add(new ZiloreAiStop {
                        Name = (ru ? "Возврат (" : "Return (") + DestinationSearch.Sector(anchor) + ")",
                        CategoryHint = ru ? "Кольцо" : "Loop",
                        Point = anchor
                    });
                }
            }

            bool isWater = !result.UseAviation && RoadRouter.Instance.IsWaterAt(anchor);
            double totalDist = 0;
            DrawingPoint prev = anchor;
            for (int i = 0; i < finalStops.Count; i++) {
                double leg = DestinationSearch.Metres(prev, finalStops[i].Point);
                double effLeg = (result.UseAviation || isWater) ? leg : leg * 1.28;
                finalStops[i].LegDistanceMeters = effLeg;
                totalDist += effLeg;
                prev = finalStops[i].Point;
            }

            result.Success = true;
            result.Stops = finalStops.ToArray();
            result.TotalDistanceMeters = totalDist;
            result.EstimatedSeconds = EstimateSeconds(totalDist, result.UseAviation, isWater, !result.UseAviation && !isWater);
            string eta = FormatDuration(result.EstimatedSeconds, ru);
            string modeBadge = result.UseAviation ? "✈ " : (isWater ? "⛵ " : "🚗 ");
            string safeNote = result.AvoidDangerZones ? (ru ? " · 🛡 объезд PvP" : " · 🛡 avoid PvP") : "";
            string loopNote = result.IsLoop ? (ru ? " · 🔁 кольцо" : " · 🔁 loop") : "";
            result.Summary = modeBadge + finalStops.Count + (ru ? " точ. · " : " stops · ")
                + DestinationSearch.Distance(totalDist) + " · " + eta + safeNote + loopNote;
            return result;
        }

        static string StopDedupKey(DrawingPoint p) {
            return ((int)Math.Round(p.X * 2000)) + ":" + ((int)Math.Round(p.Y * 2000));
        }

        static ZiloreAiStop FindNearestByKeywords(ScumMapStore store, DrawingPoint from, string[] terms, double maxDistM, HashSet<string> used) {
            var indexed = GetIndexedMarkers(store);
            if (indexed.Length == 0) return null;
            IndexedMarker best = null;
            double bestDist = maxDistM;
            for (int i = 0; i < indexed.Length; i++) {
                var item = indexed[i];
                bool match = false;
                for (int ti = 0; ti < terms.Length; ti++) {
                    if (item.SearchHaystack.Contains(terms[ti])) { match = true; break; }
                }
                if (!match) continue;
                if (used != null && used.Contains(StopDedupKey(item.Point))) continue;
                double d = DestinationSearch.Metres(from, item.Point);
                if (d < bestDist) {
                    bestDist = d;
                    best = item;
                }
            }
            if (best == null) return null;
            return new ZiloreAiStop { Name = best.DisplayName, Point = best.Point };
        }

        static ZiloreAiStop FindByPlaceName(WpfMapState state, DrawingPoint from, string query, HashSet<string> used) {
            string clean = RxCleanPlace().Replace(query, "").Trim();
            if (clean.Length < 2) return null;
            string cleanLower = clean.ToLowerInvariant();

            if (state.Saved != null) {
                MapZone bestZone = null; double bestD = double.MaxValue;
                for (int i = 0; i < state.Saved.Length; i++) {
                    var z = state.Saved[i];
                    if (z == null || z.IsDeathMarker || z.Points == null || z.Points.Length == 0) continue;
                    string dn = z.DisplayName ?? "";
                    if (dn.IndexOf(clean, StringComparison.OrdinalIgnoreCase) < 0 && (z.Name ?? "").IndexOf(clean, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var pt = z.Centroid;
                    if (used != null && used.Contains(StopDedupKey(pt))) continue;
                    double d = DestinationSearch.Metres(from, pt);
                    if (d < bestD) { bestD = d; bestZone = z; }
                }
                if (bestZone != null) {
                    return new ZiloreAiStop { Name = bestZone.DisplayName, Point = bestZone.Centroid };
                }
            }

            var indexed = GetIndexedMarkers(state.Store);
            if (indexed.Length > 0) {
                IndexedMarker bestM = null; double bestD = double.MaxValue;
                for (int i = 0; i < indexed.Length; i++) {
                    var item = indexed[i];
                    if (!item.TitleLower.Contains(cleanLower) && !item.LocalTitleLower.Contains(cleanLower)) continue;
                    if (used != null && used.Contains(StopDedupKey(item.Point))) continue;
                    double d = DestinationSearch.Metres(from, item.Point);
                    if (d < bestD) { bestD = d; bestM = item; }
                }
                if (bestM != null) {
                    return new ZiloreAiStop { Name = bestM.DisplayName, Point = bestM.Point };
                }
            }
            return null;
        }

        static List<ZiloreAiStop> OptimizeOrder(DrawingPoint start, List<ZiloreAiStop> items) {
            var remaining = new List<ZiloreAiStop>(items);
            var ordered = new List<ZiloreAiStop>(items.Count);
            DrawingPoint cur = start;
            while (remaining.Count > 0) {
                int bestIdx = 0; double bestD = double.MaxValue;
                for (int i = 0; i < remaining.Count; i++) {
                    double d = DestinationSearch.Metres(cur, remaining[i].Point);
                    if (d < bestD) { bestD = d; bestIdx = i; }
                }
                ordered.Add(remaining[bestIdx]);
                cur = remaining[bestIdx].Point;
                remaining.RemoveAt(bestIdx);
            }
            bool improved = true;
            for (int pass = 0; improved && pass < 12; pass++) {
                improved = false;
                for (int i = 0; i < ordered.Count - 2; i++) {
                    for (int j = i + 2; j < ordered.Count; j++) {
                        double before = DestinationSearch.Metres(ordered[i].Point, ordered[i + 1].Point)
                            + (j + 1 < ordered.Count ? DestinationSearch.Metres(ordered[j].Point, ordered[j + 1].Point) : 0);
                        double after = DestinationSearch.Metres(ordered[i].Point, ordered[j].Point)
                            + (j + 1 < ordered.Count ? DestinationSearch.Metres(ordered[i + 1].Point, ordered[j + 1].Point) : 0);
                        if (after + 5.0 < before) {
                            ordered.Reverse(i + 1, j - i);
                            improved = true;
                        }
                    }
                }
            }
            return ordered;
        }

        static double Clamp(double v, double min, double max) { return v < min ? min : (v > max ? max : v); }
    }
}
