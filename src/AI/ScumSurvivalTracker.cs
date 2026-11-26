#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ZiloreMiniMap {
    internal sealed class ScumFoodItem {
        internal string NameRu = "", NameEn = "";
        internal string Category = ""; // "Food", "Drink", "Med"
        internal int EnergyDelta;    // % energy boost (approximate)
        internal int HydrationDelta; // % hydration boost (approximate)
        internal int HealthDelta;    // % HP recovery
        internal string NoteRu = "", NoteEn = "";
        internal string DisplayName(bool ru) { return ru ? NameRu : NameEn; }
        internal string Note(bool ru) { return ru ? NoteRu : NoteEn; }
    }

    internal sealed class SurvivalSnapshot {
        internal double HealthPct;
        internal double EnergyPct;
        internal double HydrationPct;
        internal double EnergyBurnPerMin;
        internal double HydrationBurnPerMin;
        internal bool PassiveHudScanEnabled;
        internal bool LastScanValid;
        internal DateTime LastUpdateUtc;
    }

    /// <summary>
    /// Enterprise Survival & Metabolism Tracker for SCUM:
    /// 1) Digital Twin Metabolism (HP / Energy / Hydration) driven by movement speed & activity
    /// 2) Self-Learning EMA Calibration (adapts burn rates to your character & server settings)
    /// 3) Optional Passive Screen HUD Bar Scanner (OBS/Discord-style screen region sampling,
    ///    strictly zero game memory access, 100% EAC-safe, throttled 5s background worker)
    /// 4) Built-in SCUM Nutrition & Medical Guide with contextual recommendations
    /// </summary>
    internal static class ScumSurvivalTracker {
        static readonly object gate = new object();
        static string? iniPath;
        static DateTime lastTickUtc = DateTime.MinValue;
        static DateTime lastSaveUtc = DateTime.MinValue;
        static DateTime lastScanUtc = DateTime.MinValue;
        static int scanInFlight;
        static int saveInFlight;
        static bool dirty;
        static byte[]? scanPixelBuffer;

        // Current tracked vitals (0..100%)
        static double healthPct = 100.0;
        static double energyPct = 85.0;
        static double hydrationPct = 85.0;

        // Self-learning burn rates (% per real minute at baseline activity)
        static double energyBurnPerMin = 0.55;
        static double hydrationBurnPerMin = 0.85;

        // Passive HUD Screen Bar Scanner config (normalized screen coordinates 0..1)
        // Default: SCUM bottom-left vitals cluster (configurable via survival-tracker.ini)
        internal static volatile bool PassiveHudScanEnabled = false;
        static float scanLeftNorm = 0.015f;
        static float scanTopNorm = 0.865f;
        static float scanWidthNorm = 0.135f;
        static float scanHeightNorm = 0.115f;
        static bool lastScanValid;

        // Copilot alert cooldowns
        static DateTime lastWaterWarnUtc = DateTime.MinValue;
        static DateTime lastFoodWarnUtc = DateTime.MinValue;
        static DateTime lastHealthWarnUtc = DateTime.MinValue;

        internal static readonly ScumFoodItem[] NutritionGuide = new[] {
            new ScumFoodItem { NameRu = "Чистая вода (фляга / колодец)", NameEn = "Clean Water (Canteen / Well)", Category = "Drink",
                EnergyDelta = 0, HydrationDelta = 35, HealthDelta = 0,
                NoteRu = "Идеальная гидратация без лишних калорий и сахара.", NoteEn = "Pure hydration with zero empty calories." },
            new ScumFoodItem { NameRu = "Арбуз (долька)", NameEn = "Watermelon Slice", Category = "Drink",
                EnergyDelta = 12, HydrationDelta = 28, HealthDelta = 0,
                NoteRu = "Лучший природный баланс воды и быстрой энергии.", NoteEn = "Best natural mix of hydration and quick carbs." },
            new ScumFoodItem { NameRu = "Газировка / Энергетик", NameEn = "Soda / Energy Drink", Category = "Drink",
                EnergyDelta = 14, HydrationDelta = 22, HealthDelta = 0,
                NoteRu = "Быстрый рывок энергии, но много сахара — не злоупотребляйте.", NoteEn = "Fast energy boost, high sugar — use moderately." },
            new ScumFoodItem { NameRu = "Тушёнка (Beef Stew / MRE)", NameEn = "Canned Beef Stew / MRE", Category = "Food",
                EnergyDelta = 38, HydrationDelta = 8, HealthDelta = 0,
                NoteRu = "Высокая калорийность и белок для долгих переходов.", NoteEn = "High kcal and protein for long expeditions." },
            new ScumFoodItem { NameRu = "Жареный стейк (охота)", NameEn = "Cooked Meat Steak", Category = "Food",
                EnergyDelta = 42, HydrationDelta = -3, HealthDelta = 0,
                NoteRu = "Максимум белка и силы; обязательно запивайте водой.", NoteEn = "Maximum protein; pair with water." },
            new ScumFoodItem { NameRu = "Суп / Овощное рагу", NameEn = "Vegetable Soup / Stew", Category = "Food",
                EnergyDelta = 26, HydrationDelta = 24, HealthDelta = 2,
                NoteRu = "Восполняет и еду, и воду, плюс витамины.", NoteEn = "Restores both energy and hydration plus vitamins." },
            new ScumFoodItem { NameRu = "Орехи / Сухофрукты", NameEn = "Nuts / Dried Fruit", Category = "Food",
                EnergyDelta = 22, HydrationDelta = -4, HealthDelta = 0,
                NoteRu = "Лёгкий вес в рюкзаке, отличный перекус в рейде.", NoteEn = "Lightweight raid snack, dense calories." },
            new ScumFoodItem { NameRu = "Бинты + Антисептик", NameEn = "Bandages + Antiseptic", Category = "Med",
                EnergyDelta = 0, HydrationDelta = 0, HealthDelta = 20,
                NoteRu = "Останавливает кровотечение и предотвращает сепсис.", NoteEn = "Stops bleeding and prevents infection." },
            new ScumFoodItem { NameRu = "Обезболивающее / Витамины", NameEn = "Painkillers / Vitamins", Category = "Med",
                EnergyDelta = 0, HydrationDelta = -2, HealthDelta = 12,
                NoteRu = "Поддерживает боеспособность и иммунитет.", NoteEn = "Maintains combat readiness and immunity." }
        };

        internal static void Load(string? dataFolder) {
            if (string.IsNullOrWhiteSpace(dataFolder)) return;
            lock (gate) {
                iniPath = Path.Combine(dataFolder, "survival-tracker.ini");
                try {
                    if (!File.Exists(iniPath)) return;
                    var ini = SettingsStore.ReadIni(iniPath);
                    string? v; double d; float f;
                    if (ini.TryGetValue("PassiveHudScan", out v)) PassiveHudScanEnabled = v == "1";
                    if (ini.TryGetValue("HealthPct", out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) healthPct = Clamp(d, 0, 100);
                    if (ini.TryGetValue("EnergyPct", out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) energyPct = Clamp(d, 0, 100);
                    if (ini.TryGetValue("HydrationPct", out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) hydrationPct = Clamp(d, 0, 100);
                    if (ini.TryGetValue("EnergyBurnPerMin", out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) energyBurnPerMin = Clamp(d, 0.15, 3.5);
                    if (ini.TryGetValue("HydrationBurnPerMin", out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) hydrationBurnPerMin = Clamp(d, 0.20, 4.5);
                    if (ini.TryGetValue("ScanLeftNorm", out v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) scanLeftNorm = (float)Clamp(f, 0.0, 0.9);
                    if (ini.TryGetValue("ScanTopNorm", out v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) scanTopNorm = (float)Clamp(f, 0.0, 0.9);
                    if (ini.TryGetValue("ScanWidthNorm", out v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) scanWidthNorm = (float)Clamp(f, 0.04, 0.5);
                    if (ini.TryGetValue("ScanHeightNorm", out v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) scanHeightNorm = (float)Clamp(f, 0.04, 0.5);
                } catch (Exception ex) {
                    Program.LogException("SurvivalLoad", ex);
                }
            }
        }

        internal static void SaveNow() {
            lock (gate) { SaveLocked(); }
        }

        static string BuildIniBodyLocked() {
            return "PassiveHudScan=" + (PassiveHudScanEnabled ? "1" : "0") + Environment.NewLine
                + "HealthPct=" + healthPct.ToString("0.0", CultureInfo.InvariantCulture) + Environment.NewLine
                + "EnergyPct=" + energyPct.ToString("0.0", CultureInfo.InvariantCulture) + Environment.NewLine
                + "HydrationPct=" + hydrationPct.ToString("0.0", CultureInfo.InvariantCulture) + Environment.NewLine
                + "EnergyBurnPerMin=" + energyBurnPerMin.ToString("0.000", CultureInfo.InvariantCulture) + Environment.NewLine
                + "HydrationBurnPerMin=" + hydrationBurnPerMin.ToString("0.000", CultureInfo.InvariantCulture) + Environment.NewLine
                + "ScanLeftNorm=" + scanLeftNorm.ToString("0.000", CultureInfo.InvariantCulture) + Environment.NewLine
                + "ScanTopNorm=" + scanTopNorm.ToString("0.000", CultureInfo.InvariantCulture) + Environment.NewLine
                + "ScanWidthNorm=" + scanWidthNorm.ToString("0.000", CultureInfo.InvariantCulture) + Environment.NewLine
                + "ScanHeightNorm=" + scanHeightNorm.ToString("0.000", CultureInfo.InvariantCulture) + Environment.NewLine;
        }

        static void SaveLocked() {
            string? savePath = iniPath;
            if (string.IsNullOrWhiteSpace(savePath)) return;
            try {
                SettingsStore.WriteAtomic(savePath, BuildIniBodyLocked());
                dirty = false;
                lastSaveUtc = DateTime.UtcNow;
            } catch (Exception ex) {
                Program.LogException("SurvivalSave", ex);
            }
        }

        static void ScheduleSaveLocked() {
            if (string.IsNullOrWhiteSpace(iniPath)) return;
            dirty = true;
            if (Interlocked.CompareExchange(ref saveInFlight, 1, 0) != 0) return;
            Task.Run(async () => {
                try {
                    await Task.Delay(350);
                    string? path; string body;
                    lock (gate) {
                        if (!dirty || string.IsNullOrWhiteSpace(iniPath)) return;
                        path = iniPath;
                        body = BuildIniBodyLocked();
                        dirty = false;
                        lastSaveUtc = DateTime.UtcNow;
                    }
                    if (!string.IsNullOrWhiteSpace(path)) SettingsStore.WriteAtomic(path, body);
                } catch (Exception ex) {
                    Program.LogException("SurvivalSave", ex);
                } finally {
                    Interlocked.Exchange(ref saveInFlight, 0);
                }
            });
        }

        internal static SurvivalSnapshot GetSnapshot() {
            lock (gate) {
                return new SurvivalSnapshot {
                    HealthPct = healthPct,
                    EnergyPct = energyPct,
                    HydrationPct = hydrationPct,
                    EnergyBurnPerMin = energyBurnPerMin,
                    HydrationBurnPerMin = hydrationBurnPerMin,
                    PassiveHudScanEnabled = PassiveHudScanEnabled,
                    LastScanValid = lastScanValid,
                    LastUpdateUtc = lastTickUtc
                };
            }
        }

        /// <summary>
        /// Advances the digital-twin metabolism based on elapsed time and movement speed.
        /// Optionally triggers the throttled passive HUD bar scanner (every 5s, off UI thread).
        /// </summary>
        internal static void GetScanRegion(out float left, out float top, out float width, out float height) {
            lock (gate) { left = scanLeftNorm; top = scanTopNorm; width = scanWidthNorm; height = scanHeightNorm; }
        }

        internal static void SetScanRegion(float left, float top, float width, float height) {
            lock (gate) {
                scanLeftNorm = (float)Clamp(left, 0.0, 0.9);
                scanTopNorm = (float)Clamp(top, 0.0, 0.9);
                scanWidthNorm = (float)Clamp(width, 0.04, 0.5);
                scanHeightNorm = (float)Clamp(height, 0.04, 0.5);
                ScheduleSaveLocked();
            }
        }

        internal static void ResetBurnRates() {
            lock (gate) {
                energyBurnPerMin = 0.55;
                hydrationBurnPerMin = 0.85;
                ScheduleSaveLocked();
            }
        }

        internal static void Update(double speedMps, bool gameFocused, DateTime nowUtc) {
            if (!PoiAccessGuard.IsUnlocked) return;
            bool triggerScan = false;
            lock (gate) {
                if (lastTickUtc != DateTime.MinValue) {
                    double dtMin = (nowUtc - lastTickUtc).TotalMinutes;
                    if (dtMin > 0.001 && dtMin <= 5.0) {
                        // Activity multiplier: idle 0.7x, walk 1.0x, sprint 1.9x, driving/flying 0.85x
                        double actMult = speedMps < 0.6 ? 0.70 : (speedMps <= 3.5 ? 1.0 : (speedMps <= 8.5 ? 1.90 : 0.85));
                        energyPct = Clamp(energyPct - energyBurnPerMin * actMult * dtMin, 0.0, 100.0);
                        hydrationPct = Clamp(hydrationPct - hydrationBurnPerMin * actMult * dtMin, 0.0, 100.0);
                        if (energyPct > 40.0 && hydrationPct > 40.0 && healthPct < 100.0) {
                            healthPct = Clamp(healthPct + 0.35 * dtMin, 0.0, 100.0);
                        }
                        dirty = true;
                    }
                }
                lastTickUtc = nowUtc;

                if (dirty && (nowUtc - lastSaveUtc).TotalSeconds >= 60.0) {
                    ScheduleSaveLocked();
                }

                if (PassiveHudScanEnabled && gameFocused && (nowUtc - lastScanUtc).TotalSeconds >= 5.0) {
                    lastScanUtc = nowUtc;
                    triggerScan = true;
                }
            }

            if (triggerScan && Interlocked.CompareExchange(ref scanInFlight, 1, 0) == 0) {
                Task.Run(() => {
                    try { RunPassiveHudScan(); }
                    catch (Exception ex) { Program.LogException("PassiveHudScan", ex); }
                    finally { Interlocked.Exchange(ref scanInFlight, 0); }
                });
            }
        }

        /// <summary>
        /// Self-learning calibration: when the user (or passive HUD scanner) reports actual
        /// HP / Energy / Hydration values, we adapt the personal burn rates via EMA.
        /// </summary>
        internal static void CalibrateActual(double? actualHealth, double? actualEnergy, double? actualHydration, bool fromPassiveScan = false) {
            lock (gate) {
                const double learnAlpha = 0.15;
                if (actualEnergy.HasValue) {
                    double target = Clamp(actualEnergy.Value, 0, 100);
                    double err = energyPct - target; // positive => we underestimated burn
                    if (Math.Abs(err) >= 3.0 && Math.Abs(err) <= 45.0) {
                        double scale = Clamp(1.0 + (err / 100.0), 0.65, 1.45);
                        energyBurnPerMin = Clamp(energyBurnPerMin * (1.0 - learnAlpha) + (energyBurnPerMin * scale) * learnAlpha, 0.15, 3.5);
                    }
                    energyPct = target;
                    dirty = true;
                }
                if (actualHydration.HasValue) {
                    double target = Clamp(actualHydration.Value, 0, 100);
                    double err = hydrationPct - target;
                    if (Math.Abs(err) >= 3.0 && Math.Abs(err) <= 45.0) {
                        double scale = Clamp(1.0 + (err / 100.0), 0.65, 1.45);
                        hydrationBurnPerMin = Clamp(hydrationBurnPerMin * (1.0 - learnAlpha) + (hydrationBurnPerMin * scale) * learnAlpha, 0.20, 4.5);
                    }
                    hydrationPct = target;
                    dirty = true;
                }
                if (actualHealth.HasValue) {
                    healthPct = Clamp(actualHealth.Value, 0, 100);
                    dirty = true;
                }
                if (fromPassiveScan) lastScanValid = true;
                if (!fromPassiveScan) ScheduleSaveLocked();
            }
        }

        internal static void LogConsumed(ScumFoodItem? item) {
            if (item == null) return;
            lock (gate) {
                energyPct = Clamp(energyPct + item.EnergyDelta, 0, 100);
                hydrationPct = Clamp(hydrationPct + item.HydrationDelta, 0, 100);
                healthPct = Clamp(healthPct + item.HealthDelta, 0, 100);
                ScheduleSaveLocked();
            }
        }

        /// <summary>
        /// Passive Screen HUD Bar Scanner (OBS/Discord style, 100% EAC-safe):
        /// Captures only the small calibrated bottom-left HUD rectangle via standard GDI
        /// and estimates bar fill ratios using color-channel heuristics (Green/Red=HP, Amber/Yellow=Energy, Cyan/Blue=Water).
        /// </summary>
        static Rectangle GetActiveGameScreenBounds() {
            try {
                IntPtr hwnd = Native.AdminGameWindow();
                if (hwnd == IntPtr.Zero) hwnd = Native.GetForegroundWindow();
                if (hwnd != IntPtr.Zero) return System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;
            } catch (Exception ex) { SafeCleanup.LogThrottledWarning("SurvivalScreen", "Game screen query failed", ex); }
            var primary = System.Windows.Forms.Screen.PrimaryScreen;
            return primary != null ? primary.Bounds : new Rectangle(0, 0, 1920, 1080);
        }

        static void RunPassiveHudScan() {
            Rectangle screen = GetActiveGameScreenBounds();
            int x = Math.Max(screen.Left, (int)Math.Round(screen.Left + screen.Width * scanLeftNorm));
            int y = Math.Max(screen.Top, (int)Math.Round(screen.Top + screen.Height * scanTopNorm));
            int w = Math.Max(32, Math.Min(360, (int)Math.Round(screen.Width * scanWidthNorm)));
            int h = Math.Max(32, Math.Min(260, (int)Math.Round(screen.Height * scanHeightNorm)));
            if (x + w > screen.Right) w = Math.Max(16, screen.Right - x);
            if (y + h > screen.Bottom) h = Math.Max(16, screen.Bottom - y);

            double? hp = null, eng = null, hyd = null;
            bool captured = GdiScreenCapturePool.TryCaptureAndInspect("survival-hud", new Rectangle(x, y, w, h), PixelFormat.Format32bppArgb, 150, bmp => {
                int hpCols = 0, engCols = 0, hydCols = 0, totalCols = 0;
                BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try {
                    int stride = data.Stride;
                    int totalBytes = stride * h;
                    byte[] buf = (scanPixelBuffer != null && scanPixelBuffer.Length >= totalBytes) ? scanPixelBuffer : (scanPixelBuffer = new byte[totalBytes]);
                    Marshal.Copy(data.Scan0, buf, 0, totalBytes);
                    for (int px = 0; px < w; px += 2) {
                        totalCols++;
                        bool hasHp = false, hasEng = false, hasHyd = false;
                        int xOff = px * 4;
                        for (int py = 0; py < h; py += 2) {
                            int idx = py * stride + xOff;
                            byte b = buf[idx], gr = buf[idx + 1], r = buf[idx + 2];
                            if (r > 165 && gr < 85 && b < 85) hasHp = true;
                            else if (gr > 155 && r > 130 && b < 75) hasEng = true;
                            else if (b > 160 && gr > 110 && r < 90) hasHyd = true;
                            if (hasHp && hasEng && hasHyd) break;
                        }
                        if (hasHp) hpCols++;
                        if (hasEng) engCols++;
                        if (hasHyd) hydCols++;
                    }
                } finally {
                    bmp.UnlockBits(data);
                }
                if (totalCols < 10) return;
                hp = hpCols >= 3 ? (double?)Clamp(hpCols * 100.0 / totalCols, 5, 100) : null;
                eng = engCols >= 3 ? (double?)Clamp(engCols * 100.0 / totalCols, 5, 100) : null;
                hyd = hydCols >= 3 ? (double?)Clamp(hydCols * 100.0 / totalCols, 5, 100) : null;
            });
            if (captured && (hp.HasValue || eng.HasValue || hyd.HasValue)) {
                CalibrateActual(hp, eng, hyd, true);
            }
        }

        internal static string GetRecommendation(bool ru) {
            var s = GetSnapshot();
            if (s.HealthPct < 45.0) {
                return ru
                    ? "🩹 Критическое здоровье (" + (int)s.HealthPct + "%): используйте бинты/антисептик и избегайте боя."
                    : "🩹 Low Health (" + (int)s.HealthPct + "%): apply bandages/antiseptic and avoid combat.";
            }
            if (s.HydrationPct < 35.0 && s.EnergyPct < 40.0) {
                return ru
                    ? "🥣 Низкие вода и еда: идеально подойдут Арбуз или Овощное рагу (вода + ккал)."
                    : "🥣 Low water & energy: eat Watermelon or Vegetable Stew for both hydration and kcal.";
            }
            if (s.HydrationPct < 35.0) {
                int minsLeft = Math.Max(1, (int)Math.Round(s.HydrationPct / Math.Max(0.2, s.HydrationBurnPerMin)));
                return ru
                    ? "💧 Мало воды (" + (int)s.HydrationPct + "%, ~" + minsLeft + " мин): выпейте флягу или проложите маршрут к колодцу/трейдеру."
                    : "💧 Low water (" + (int)s.HydrationPct + "%, ~" + minsLeft + "m left): drink clean water or route to a well/trader.";
            }
            if (s.EnergyPct < 35.0) {
                int minsLeft = Math.Max(1, (int)Math.Round(s.EnergyPct / Math.Max(0.15, s.EnergyBurnPerMin)));
                return ru
                    ? "🍖 Мало энергии (" + (int)s.EnergyPct + "%, ~" + minsLeft + " мин): съешьте тушёнку, стейк или орехи."
                    : "🍖 Low energy (" + (int)s.EnergyPct + "%, ~" + minsLeft + "m left): eat beef stew, cooked meat, or nuts.";
            }
            return ru
                ? "✅ Показатели в норме (HP " + (int)s.HealthPct + "% · Еда " + (int)s.EnergyPct + "% · Вода " + (int)s.HydrationPct + "%)."
                : "✅ Vitals optimal (HP " + (int)s.HealthPct + "% · Food " + (int)s.EnergyPct + "% · Water " + (int)s.HydrationPct + "%).";
        }

        internal static string EvaluateSurvivalCopilot(DateTime nowUtc, bool ru) {
            if (!PoiAccessGuard.IsUnlocked) return "";
            lock (gate) {
                if (healthPct < 35.0 && (nowUtc - lastHealthWarnUtc).TotalSeconds >= 90.0) {
                    lastHealthWarnUtc = nowUtc;
                    return ru ? "🤖 AI: Низкое здоровье (" + (int)healthPct + "%) — нужна аптечка!" : "🤖 AI: Low HP (" + (int)healthPct + "%) — heal up!";
                }
                if (hydrationPct < 25.0 && (nowUtc - lastWaterWarnUtc).TotalSeconds >= 120.0) {
                    lastWaterWarnUtc = nowUtc;
                    return ru ? "🤖 AI: Обезвоживание (" + (int)hydrationPct + "%) — пора попить воды!" : "🤖 AI: Dehydration (" + (int)hydrationPct + "%) — drink water!";
                }
                if (energyPct < 25.0 && (nowUtc - lastFoodWarnUtc).TotalSeconds >= 120.0) {
                    lastFoodWarnUtc = nowUtc;
                    return ru ? "🤖 AI: Голод (" + (int)energyPct + "%) — перекусите (тушёнка/стейк)" : "🤖 AI: Low energy (" + (int)energyPct + "%) — eat food!";
                }
            }
            return "";
        }

        static double Clamp(double v, double min, double max) { return v < min ? min : (v > max ? max : v); }
    }
}
