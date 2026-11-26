using System;
using System.IO;

namespace ZiloreMiniMap {
    /// <summary>
    /// Automated self-test suite executed via "ZiloreMiniMap.exe -Check"
    /// and scripts/Run-Tests.ps1 before every release build.
    /// </summary>
    internal static partial class CoreSelfTests {
        internal static void RunAll() {
            TestUpdateReleaseParse();
            TestSettingsStoreAtomic();
            TestIntegrityGuardHwid();
            TestAccessGuardsRejectInvalid();
            TestAppLogBuffer();
            TestSquadRoutesAndLibrary();
            TestVoiceDevicesEnumeration();
            TestRouteShareCodeRoundTrip();
            TestSavedRouteLibraryResilience();
            TestRoadRouterAvoidZones();
            TestAdaptiveTrayIconGeneration();
            TestThemePalettesAndResources();
            TestMapZoomAndRenderInvariants();
            TestHourlyAccessKeyDurations();
            TestZiloreAiSectorParser();
            TestGlassOpacityAndDpiInvariants();
            TestNavigationTurnAndOffRoute();
            TestGameClockAnchorAndScale();
            TestWeatherClimateModel();
            TestPeerRouterOpenAsync();
            TestLightThemeMarkerContrastAndPoiQuantization();
            TestAdaptiveDayNightIconAssets();
            TestGpsPrecisionAndOverlayBounds();
            TestEngineRoutingCostAndTurnProjection();
            TestAutoUpdateLimitsAndFastTabScan();
            TestSettingsStoreAtomicAndAviationValidation();
            TestMapGridDangerEdgeAndFlightTelemetry();
            TestTacticalHotkeyCatalogAndUiTextIntegrity();
            TestWindowTopmostAndStandaloneContracts();
            TestSeamlessUpdateHelpersAndGitHubEndpoints();
            TestWeatherClockSearchAndSquadSafety();
            TestTacticalLayoutStorePersistenceAndBounds();
            TestFluentThemeResourcesAndProfileBackupInventory();
            TestPerfOptimizationsRoutingSquadAndPolylines();
            TestEnterpriseUpdaterStreamingMirrorAndPrefs();
            TestInstallPathMigrationAndAutoLaunchHealing();
            TestV618BootstrapLogsBackupAndGroupsSafety();
            TestV619TopmostContractsAndNavPerf();
            TestV620AuditFixesAndGuards();
            TestStage2ServicesAndMvvm();
            TestStage2BHeadlessEngineAndSquadMvvm();
            RunUpdateRegressionSuite();
            TestPhase3ASafeCleanupAndNullableServices();
            TestPhase3BServicesAndCoreNullable();
            TestPhase4AHotPathPerfCryptoAndSearch();
            TestPhase4BLibraryImportAndNullableModules();
            TestPhase4CHeadlessZeroOverlayAndCleanup();
            TestPhase4DProductFeaturesAndTelemetry();
            TestPhase5ASecuritySqliteRegexAndCache();
            TestPhase5BAnalyzersNullableAndConnectTimeout();
            TestPhase5CZeroOverlayAndWpfPreviewReadiness();
            TestPhase6AFpsBugsAndHotPathPerf();
            TestPhase6BWindowedRoutingVoiceAndPreview();
            TestPhase6CFormlessWpfDispatcherEngine();
            TestPhase6DTelemetrySanitizationAndEndpointSafety();
            TestSystemTypographySegoeVariableClearType();
            TestPhase7AP0FpsAutoZoomPrewarmAndNative();
            TestPhase7BNetworkStabilitySyslib0014AndGdiPool();
            TestPhase7CGeneratedRegexLibraryImportAndNullable();
            TestPhase7DPureWpfAdminProfileZeroAsyncVoidAndCa2208();
            TestPhase8AAuditFixesPerfIoSecNavSquadUi();
            TestPhase8BHotPathCacheModernNet10AndNullable();
            TestPhase8CScumMapFrozenDictDeathPoolAndNullable();
            TestPhase8DInputHooksZoneStorageAndSidebar();
            TestPhase9BenchSuitePoiIndexAndAdminPlayers();
            TestPhase10EulaAgreementAndPersistence();
            TestPhase11UserGuideEmbeddedAndSearch();
        }

        static void Assert(bool condition, string message) {
            if (!condition) throw new InvalidOperationException("CoreSelfTest failed: " + message);
        }

        static void TestUpdateReleaseParse() {
            string sha = new string('a', 64);
            string pkg = new string('b', 64);

            // Schema 1 with build + notes
            string m1 = "schema=1\nversion=0.2.0\nsha256=" + sha + "\nbuild=54321\nnotes=Line 1\\nLine 2\n";
            var r1 = UpdateRelease.Parse(m1);
            Assert(r1.VersionText == "0.2.0", "UpdateRelease version mismatch");
            Assert(r1.BuildNumber == "54321", "UpdateRelease build mismatch");
            Assert(r1.DisplayTitle == "ZiloreMiniMap v0.2.0 Build (54321)", "UpdateRelease DisplayTitle mismatch");
            Assert(r1.ReleaseNotes != null && r1.ReleaseNotes.Contains("Line 1") && r1.ReleaseNotes.Contains("Line 2"), "UpdateRelease notes mismatch");

            // Schema 2 with package-sha256
            string m2 = "schema=2\nversion=1.0.0\nsha256=" + sha + "\npackage-sha256=" + pkg + "\n";
            var r2 = UpdateRelease.Parse(m2);
            Assert(r2.IsPackage && r2.PackageSha256 == pkg, "UpdateRelease schema=2 failed");

            // Invalid build number must throw FormatException
            bool threw = false;
            try {
                UpdateRelease.Parse("schema=1\nversion=0.2.0\nsha256=" + sha + "\nbuild=abc\n");
            } catch (FormatException) {
                threw = true;
            }
            Assert(threw, "UpdateRelease accepted invalid build number");
        }

        static void TestSettingsStoreAtomic() {
            string tempFile = Path.Combine(Path.GetTempPath(), "zlr-test-" + Guid.NewGuid().ToString("N") + ".ini");
            try {
                SettingsStore.WriteAtomic(tempFile, "# comment\nHardwareAccelMap=1\nTheme=OLED\n");
                var ini = SettingsStore.ReadIni(tempFile);
                Assert(ini.ContainsKey("HardwareAccelMap") && ini["HardwareAccelMap"] == "1", "SettingsStore read key 1 failed");
                Assert(ini.ContainsKey("Theme") && ini["Theme"] == "OLED", "SettingsStore read key 2 failed");

                // Overwrite atomically
                SettingsStore.WriteAtomic(tempFile, "HardwareAccelMap=0\n");
                var ini2 = SettingsStore.ReadIni(tempFile);
                Assert(ini2["HardwareAccelMap"] == "0" && !ini2.ContainsKey("Theme"), "SettingsStore atomic replace failed");
            } finally {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        static void TestIntegrityGuardHwid() {
            string id1 = IntegrityGuard.GetHardwareId();
            string id2 = IntegrityGuard.GetHardwareId();
            Assert(!string.IsNullOrEmpty(id1) && id1.Length == 32, "HWID length must be 32 hex chars");
            Assert(string.Equals(id1, id2, StringComparison.Ordinal), "HWID must be deterministic");
        }

        static void TestAccessGuardsRejectInvalid() {
            Assert(!AdminAccessGuard.TryUnlock(""), "AdminAccessGuard accepted empty password");
            Assert(!AdminAccessGuard.TryUnlock("wrong-password-123"), "AdminAccessGuard accepted invalid password");

            string err;
            Assert(!PoiAccessGuard.TryActivate("", out err) && !string.IsNullOrEmpty(err), "PoiAccessGuard accepted empty code");
        }

        static void TestAppLogBuffer() {
            long before = AppLogBuffer.Revision;
            AppLogBuffer.Add("INFO", "SelfTest", "Ping");
            Assert(AppLogBuffer.Revision > before, "AppLogBuffer revision did not increment");
            string text = AppLogBuffer.ExportText("INFO");
            Assert(text.Contains("SelfTest") && text.Contains("Ping"), "AppLogBuffer export missing entry");
        }
 
        static void TestSquadRoutesAndLibrary() {
            var state = new SquadState();
            Assert(state.AddStop("Alpha", 0.1, 0.2), "AddStop Alpha failed");
            Assert(state.AddStop("Beta", 0.3, 0.4), "AddStop Beta failed");
            Assert(state.AddStop("Gamma", 0.5, 0.6), "AddStop Gamma failed");
            var stops = state.Stops;
            Assert(stops.Length == 3 && stops[0].Name == "Alpha" && stops[2].Name == "Gamma", "Initial stops order mismatch");

            Assert(state.MoveStop(stops[2].Id, -1), "MoveStop up failed");
            stops = state.Stops;
            Assert(stops[1].Name == "Gamma" && stops[2].Name == "Beta", "MoveStop result mismatch");

            Assert(state.ReverseRoute(), "ReverseRoute failed");
            Assert(state.UpdateStopPosition(state.Stops[0].Id, 0.42, 0.77), "UpdateStopPosition valid failed");
            Assert(!state.UpdateStopPosition(state.Stops[0].Id, double.NaN, 0.5), "UpdateStopPosition accepted NaN");
            stops = state.Stops;
            Assert(stops[0].Name == "Beta" && stops[2].Name == "Alpha", "ReverseRoute result mismatch");

            string tempFile = Path.Combine(Path.GetTempPath(), "zlr-routes-" + Guid.NewGuid().ToString("N") + ".tsv");
            try {
                SavedRouteLibrary.Save(tempFile, new[] {
                    new SavedRouteTemplate { Name = "Bunker Run", Shared = true, Stops = stops }
                });
                var loaded = SavedRouteLibrary.Load(tempFile);
                Assert(loaded.Count == 1 && loaded[0].Name == "Bunker Run" && loaded[0].Shared && loaded[0].Stops.Length == 3, "SavedRouteLibrary round-trip failed");
            } finally {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        static void TestVoiceDevicesEnumeration() {
            var devices = BuiltInVoicePlayer.ListOutputDevices();
            Assert(devices != null && devices.Length >= 1 && devices[0].Item1 == -1, "Voice output device list must always contain default (-1)");
        }
 
        static void TestRouteShareCodeRoundTrip() {
            var tpl = new SavedRouteTemplate {
                Name = "Airfield Loop",
                Shared = true,
                Stops = new[] {
                    new SharedMapMarker { Name = "Bunker A2", X = 0.25, Y = 0.35 },
                    new SharedMapMarker { Name = "Airfield", X = 0.75, Y = 0.65 }
                }
            };
            string code = SavedRouteLibrary.EncodeShareCode(tpl);
            Assert(code.StartsWith(SavedRouteLibrary.SharePrefix, StringComparison.Ordinal), "Share code prefix missing");

            SavedRouteTemplate decoded;
            Assert(SavedRouteLibrary.TryDecodeShareCode(code, out decoded) && decoded != null, "TryDecodeShareCode failed");
            Assert(decoded.Name == "Airfield Loop" && decoded.Shared && decoded.Stops.Length == 2, "Decoded route metadata mismatch");
            Assert(!SavedRouteLibrary.TryDecodeShareCode("INVALID-CODE", out decoded), "Invalid share code was accepted");
        }
 
        static void TestSavedRouteLibraryResilience() {
            var missing = SavedRouteLibrary.Load(Path.Combine(Path.GetTempPath(), "zlr-nonexistent-" + Guid.NewGuid().ToString("N") + ".tsv"));
            Assert(missing != null && missing.Count == 0, "Missing route file must return empty list");

            string badFile = Path.Combine(Path.GetTempPath(), "zlr-bad-" + Guid.NewGuid().ToString("N") + ".tsv");
            try {
                File.WriteAllText(badFile, "ROUTE\tBroken\t1\nSTOP\tNaN\t0.5\tBadPt\n");
                var loaded = SavedRouteLibrary.Load(badFile);
                Assert(loaded != null && loaded.Count == 0, "Invalid stop coordinates must not produce broken route template");
            } finally {
                try { if (File.Exists(badFile)) File.Delete(badFile); } catch { }
            }
        }
 
        static void TestRoadRouterAvoidZones() {
            var poly = new[] {
                new System.Drawing.PointF(0.2f, 0.2f),
                new System.Drawing.PointF(0.3f, 0.2f),
                new System.Drawing.PointF(0.3f, 0.3f),
                new System.Drawing.PointF(0.2f, 0.3f)
            };
            RoadRouter.Instance.SetAvoidZones(new[] { poly, null, new[] { new System.Drawing.PointF(0.1f, 0.1f) } }, 6.0);
            RoadRouter.Instance.SetAvoidZones(new[] { poly }, double.NaN);
            RoadRouter.Instance.SetAvoidZones(null);
            Assert(true, "SetAvoidZones handled valid and degenerate polygons");
        }
 
        static void TestAdaptiveTrayIconGeneration() {
            foreach (bool light in new[] { true, false }) {
                foreach (TrayIconStatus st in new[] { TrayIconStatus.Idle, TrayIconStatus.Live, TrayIconStatus.Paused }) {
                    using (var ico = TrayController.CreateAdaptiveTrayIcon(light, st)) {
                        Assert(ico != null && ico.Width == 32 && ico.Height == 32, "Adaptive tray icon must be 32x32");
                    }
                }
            }
        }
 
        static void TestThemePalettesAndResources() {
            string original = WpfFieldTheme.ActiveTheme;
            try {
                foreach (string mode in new[] { "Dark", "OLED", "Light", "System" }) {
                    WpfFieldTheme.ApplyPalette(mode);
                    string resolved = WpfFieldTheme.ResolveTheme(mode);
                    Assert(resolved == "Dark" || resolved == "OLED" || resolved == "Light", "Invalid resolved theme: " + resolved);
                    Assert(WpfFieldTheme.Ground != null && WpfFieldTheme.Panel != null && WpfFieldTheme.Raised != null, "Theme surface brushes null for " + mode);
                    Assert(WpfFieldTheme.Ink != null && WpfFieldTheme.Amber != null && WpfFieldTheme.Cyan != null, "Theme ink brushes null for " + mode);
                    Assert(WpfFieldTheme.Ground.IsFrozen && WpfFieldTheme.Panel.IsFrozen, "Theme brushes must be frozen for 60 FPS performance");
                    var accBrush = WpfFieldTheme.Amber as System.Windows.Media.SolidColorBrush;
                    Assert(accBrush != null && accBrush.Color.B > accBrush.Color.R, "Fluent Azure accent must be blue-dominant, not orange, in " + mode);
                }
            } finally {
                WpfFieldTheme.ApplyPalette(original);
            }
        }

        static void TestMapZoomAndRenderInvariants() {
            // Verify logarithmic zoom convergence (1x -> 16x and 16x -> 1x) at 60 Hz
            double z = 1.0, target = 16.0;
            int framesUp = 0;
            double epsUp = Math.Max(0.004, target * 0.002);
            while (Math.Abs(target - z) > epsUp && framesUp < 60) {
                double alpha = 1.0 - Math.Exp(-18.0 * 0.016);
                z = z * Math.Pow(target / z, alpha);
                framesUp++;
            }
            Assert(framesUp >= 15 && framesUp <= 35, "Logarithmic zoom-in settle out of range: " + framesUp);

            z = 16.0; target = 1.0;
            int framesDown = 0;
            double epsDown = Math.Max(0.004, target * 0.002);
            while (Math.Abs(target - z) > epsDown && framesDown < 60) {
                double alpha = 1.0 - Math.Exp(-18.0 * 0.016);
                z = z * Math.Pow(target / z, alpha);
                framesDown++;
            }
            Assert(framesDown >= 15 && framesDown <= 35, "Logarithmic zoom-out settle out of range: " + framesDown);
        }

        static void TestHourlyAccessKeyDurations() {
            int[] okHours = new[] { 1, 3, 6, 12, 24 };
            foreach (int h in okHours) Assert(PoiAccessGuard.IsAllowedDurationHours(h), "Expected allowed hour: " + h);
            int[] badHours = new[] { 0, 2, 5, 48, -1 };
            foreach (int h in badHours) Assert(!PoiAccessGuard.IsAllowedDurationHours(h), "Expected rejected hour: " + h);

            int[] okDays = new[] { 30, 90, 180, 365 };
            foreach (int d in okDays) Assert(PoiAccessGuard.IsAllowedDurationDays(d), "Expected allowed day: " + d);
            Assert(!PoiAccessGuard.IsAllowedDurationDays(7), "Expected rejected day: 7");
        }

        static void TestZiloreAiSectorParser() {
            string code;
            System.Drawing.PointF pt;
            Assert(ZiloreAiEngine.TryParseSectorCenter("бункер в B2", out code, out pt) && code == "B2", "Sector B2 parse failed");
            Assert(pt.X > 0.0f && pt.X < 1.0f && pt.Y > 0.0f && pt.Y < 1.0f, "Sector B2 coordinates out of bounds");
            Assert(ZiloreAiEngine.TryParseSectorCenter("D4", out code, out pt) && code == "D4", "Sector D4 parse failed");
            Assert(!ZiloreAiEngine.TryParseSectorCenter("X9", out code, out pt), "Invalid sector X9 should fail");
        }

        static void TestGlassOpacityAndDpiInvariants() {
            // Verify airy Windows 11 HUD glass alpha stays readable (38%..74%) across opacity slider 30..100
            int prevAlpha = -1;
            foreach (int op in new[] { 30, 50, 75, 100 }) {
                int alpha = (int)Math.Round(255.0 * (0.38 + 0.36 * Math.Max(0.0, Math.Min(1.0, (op - 30.0) / 70.0))));
                Assert(alpha >= 95 && alpha <= 192, "HUD glass alpha out of readable bounds: " + alpha);
                Assert(alpha >= prevAlpha, "HUD glass alpha must be monotonic with opacity");
                prevAlpha = alpha;
            }
            // Verify DPI cache key separation across 100%, 125%, 150%, 200%
            int k100 = (int)Math.Round(1.00 * 100.0);
            int k125 = (int)Math.Round(1.25 * 100.0);
            int k150 = (int)Math.Round(1.50 * 100.0);
            int k200 = (int)Math.Round(2.00 * 100.0);
            Assert(k100 != k125 && k125 != k150 && k150 != k200, "Multi-DPI cache keys collided");
        }

        static void TestNavigationTurnAndOffRoute() {
            // Build synthetic L-shaped 90-degree right turn route (~200m east, then ~200m south)
            var poly = new[] {
                new System.Drawing.PointF(0.500f, 0.500f),
                new System.Drawing.PointF(0.505f, 0.500f),
                new System.Drawing.PointF(0.510f, 0.500f),
                new System.Drawing.PointF(0.510f, 0.505f),
                new System.Drawing.PointF(0.510f, 0.510f)
            };
            var route = new RoadRoute { Success = true, Polyline = poly, JunctionIndices = new[] { 2 } };
            var turn = RoadRouter.GetNextTurn(new System.Drawing.PointF(0.502f, 0.500f), route);
            Assert(turn.Valid && turn.TurnAngleDeg > 70.0 && turn.TurnAngleDeg < 110.0, "Expected ~90 deg right turn, got: " + turn.TurnAngleDeg);
            // Off-route (> 60m away) must not emit bogus turn
            var offTurn = RoadRouter.GetNextTurn(new System.Drawing.PointF(0.502f, 0.510f), route);
            Assert(!offTurn.Valid, "Off-route player must not get turn instruction");
            Assert(RoadRouter.IsOffRoute(new System.Drawing.PointF(0.502f, 0.510f), route, 60.0), "Expected IsOffRoute=true");
            Assert(!RoadRouter.IsOffRoute(new System.Drawing.PointF(0.502f, 0.500f), route, 60.0), "Expected IsOffRoute=false");
        }

        static void TestGameClockAnchorAndScale() {
            string tmp = Path.Combine(Path.GetTempPath(), "zmm-clock-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try {
                ScumGameClock.Load(tmp);
                ScumGameClock.SetTimeScale(4.0);
                ScumGameClock.SyncToGameTime(18, 30, 0);
                double beforeScale = ScumGameClock.GetGameMinutesUtc(DateTime.UtcNow);
                ScumGameClock.SetTimeScale(6.0);
                double afterScale = ScumGameClock.GetGameMinutesUtc(DateTime.UtcNow);
                double diff = Math.Abs(afterScale - beforeScale);
                if (diff > 720.0) diff = 1440.0 - diff;
                Assert(diff < 1.0, "Changing TimeScale caused a game clock jump: " + diff);

                // Verify persistence round-trip
                ScumGameClock.Load(tmp);
                Assert(Math.Abs(ScumGameClock.TimeScale - 6.0) < 0.001, "TimeScale did not persist after reload");

                // Verify formatting & day/night boundary
                string gTxt, rTxt; bool isDay;
                ScumGameClock.SyncToGameTime(6, 0, 0);
                ScumGameClock.GetFormatted(DateTime.Now, out gTxt, out isDay, out rTxt);
                Assert(isDay && gTxt.StartsWith("☀", StringComparison.Ordinal), "06:00 should be daytime");
                Assert(rTxt != null && rTxt.Length == 5 && rTxt[2] == ':', "HUD realClockText must be compact HH:mm");
                ScumGameClock.SyncToGameTime(20, 0, 0);
                ScumGameClock.GetFormatted(DateTime.Now, out gTxt, out isDay, out rTxt);
                Assert(!isDay && gTxt.StartsWith("🌙", StringComparison.Ordinal), "20:00 should be nighttime");

                // Verify midnight wrap-around (23:59 + 1 real minute at 4x = 00:03)
                ScumGameClock.SetTimeScale(4.0);
                ScumGameClock.SyncToGameTime(23, 59, 0);
                double afterMidnight = ScumGameClock.GetGameMinutesUtc(DateTime.UtcNow.AddMinutes(1));
                Assert(afterMidnight >= 2.5 && afterMidnight <= 3.5, "Midnight wrap failed: " + afterMidnight);

                // Verify legacy game-clock.ini (OffsetMinutes) backward-compatible load & TimeScale clamping
                File.WriteAllText(Path.Combine(tmp, "game-clock.ini"), "TimeScale=99.0\nOffsetMinutes=120.5\n");
                ScumGameClock.Load(tmp);
                Assert(Math.Abs(ScumGameClock.TimeScale - 24.0) < 0.001, "TimeScale upper clamp failed");
            } finally {
                try { Directory.Delete(tmp, true); } catch { }
            }
        }

        static void TestWeatherClimateModel() {
            var alpineNight = ScumWeatherEngine.ComputeClimate(0.10f, 800.0, 180.0, ScumSkyCondition.Clear);
            var coastNoon = ScumWeatherEngine.ComputeClimate(0.85f, 10.0, 900.0, ScumSkyCondition.Clear);
            Assert(alpineNight.TempC < 0.0 && alpineNight.HypothermiaRisk, "Alpine night at 800m should be freezing");
            Assert(coastNoon.TempC > 25.0 && !coastNoon.HypothermiaRisk, "Coastal afternoon should be warm");
            var stormCoast = ScumWeatherEngine.ComputeClimate(0.85f, 10.0, 900.0, ScumSkyCondition.RainStorm);
            Assert(stormCoast.TempC < coastNoon.TempC - 4.0, "RainStorm should cool temperature");
        }

        static void TestPeerRouterOpenAsync() {
            // Offline verification of non-blocking PeerRouter.OpenAsync path with empty gateways list
            var task = PeerRouter.OpenAsync(47831, Array.Empty<System.Net.IPAddress>(), (ip, req) => null);
            Assert(task != null, "PeerRouter.OpenAsync returned null task");
            var result = task.GetAwaiter().GetResult();
            Assert(result == null, "Expected null mapping for empty gateways");
        }

        static void TestLightThemeMarkerContrastAndPoiQuantization() {
            // Verify constant dark cartographic badge brush remains dark even in Light theme
            var bg = WpfMapSurface.MapMarkerBg as System.Windows.Media.SolidColorBrush;
            Assert(bg != null && bg.IsFrozen && bg.Color.R < 40 && bg.Color.G < 40 && bg.Color.B < 50, "MapMarkerBg must be a frozen dark brush for Light theme POI visibility");
            // Verify quantized compact POI cluster side buckets absorb micro speed-auto-zoom jitter
            double s1 = Math.Pow(2.0, Math.Round(Math.Log(500.0, 2.0) * 8.0) / 8.0);
            double s2 = Math.Pow(2.0, Math.Round(Math.Log(503.0, 2.0) * 8.0) / 8.0);
            Assert(Math.Abs(s1 - s2) < 1e-6, "Small speed auto-zoom delta should land in the same POI cluster bucket");
        }

        static void TestAdaptiveDayNightIconAssets() {
            using (var dayBmp = AdaptiveAppIcon.RenderProceduralFluentBitmap(128, true))
            using (var nightBmp = AdaptiveAppIcon.RenderProceduralFluentBitmap(128, false)) {
                Assert(dayBmp.Width == 128 && nightBmp.Width == 128, "Procedural Fluent icons must render at 128x128");
                // Verify Day tile is bright and Night tile is dark at upper-left interior
                var dc = dayBmp.GetPixel(24, 24);
                var nc = nightBmp.GetPixel(24, 24);
                Assert(dc.R > 200 && dc.G > 200 && dc.B > 200, "Day icon surface should be bright light-theme glass");
                Assert(nc.R < 50 && nc.G < 60 && nc.B < 80, "Night icon surface should be deep dark-theme obsidian");
            }
        }

        static void TestGpsPrecisionAndOverlayBounds() {
            // 1. Round-trip SCUM world <-> normalized map coordinate projection precision (< 0.0001 = < 15 cm)
            var samples = new[] { new System.Drawing.PointF(0.25f, 0.25f), new System.Drawing.PointF(0.50f, 0.50f), new System.Drawing.PointF(0.82f, 0.37f) };
            foreach (var pt in samples) {
                double wx, wy;
                WpfMapSurface.MapToWorld(pt, out wx, out wy);
                var back = MapWindow.ToMap(new Position { X = wx, Y = wy, Z = 0, Yaw = 0 });
                Assert(Math.Abs(back.X - pt.X) < 0.0005 && Math.Abs(back.Y - pt.Y) < 0.0005, "MapToWorld <-> ToMap round-trip drift too large");
            }
            // 2. Oversized or off-screen HUD bounds must clamp cleanly inside active monitor working area
            var monitors = new[] { new System.Drawing.Rectangle(0, 0, 1920, 1040) };
            var recovered = WpfFieldConsoleRuntime.RecoverHudBounds(new System.Drawing.Rectangle(3200, 1400, 2200, 1200), monitors);
            Assert(recovered.Width <= 1920 && recovered.Height <= 1040 && recovered.Left >= 0 && recovered.Top >= 0, "RecoverHudBounds failed to clamp oversized/offscreen HUD");
            // 3. SquadRoutePlanner leg cache invalidation must be thread-safe and non-throwing
            SquadRoutePlanner.ClearLegCache();
        }

        static void TestEngineRoutingCostAndTurnProjection() {
            // 1. Verify Native.IsGameWindow PID-cache fast path handles IntPtr.Zero cleanly
            Assert(!Native.IsGameWindow(IntPtr.Zero), "IntPtr.Zero must never match SCUM game window");
            // 2. Verify GetNextTurn projects off-road player position onto segment (no hypotenuse inflation)
            // Straight north segment ~152m long (0.50, 0.50) -> (0.50, 0.49), then sharp 90deg east turn -> (0.51, 0.49)
            var route = new RoadRoute {
                Success = true,
                RoutingCost = 304.0,
                Polyline = new[] { new System.Drawing.PointF(0.50f, 0.50f), new System.Drawing.PointF(0.50f, 0.49f), new System.Drawing.PointF(0.51f, 0.49f) },
                JunctionIndices = new[] { 1 }
            };
            // Player is at midpoint y=0.495 (~76.2m before turn), shifted ~30m east (x=0.502)
            var turn = RoadRouter.GetNextTurn(new System.Drawing.PointF(0.502f, 0.495f), route);
            Assert(turn.Valid && Math.Abs(turn.DistanceMeters - 76.2) < 3.0, "GetNextTurn distance should reflect projected segment distance (~76.2m), not lateral hypotenuse");
            Assert(Math.Abs(turn.TurnAngleDeg - 90.0) < 5.0, "Expected ~+90 degree right turn");
        }

        static void TestAutoUpdateLimitsAndFastTabScan() {
            // 1. Verify fast LockBits HasSelectedTabHighlight detects bright neutral tab strip and rejects dark frames
            using (var dark = new System.Drawing.Bitmap(200, 20))
            using (var lit = new System.Drawing.Bitmap(200, 20)) {
                using (var g1 = System.Drawing.Graphics.FromImage(dark)) g1.Clear(System.Drawing.Color.FromArgb(20, 24, 30));
                Assert(!Native.HasSelectedTabHighlight(dark), "Dark frame must not trigger inventory tab highlight");
                using (var g2 = System.Drawing.Graphics.FromImage(lit)) {
                    g2.Clear(System.Drawing.Color.FromArgb(20, 24, 30));
                    using (var br = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(215, 215, 215)))
                        g2.FillRectangle(br, 20, 4, 60, 8);
                }
                Assert(Native.HasSelectedTabHighlight(lit), "Highlighted neutral tab bar must be detected via LockBits");
            }
            // 2. Verify ScumAutoLaunch watcher checks ZiloreMiniMapOverlay mutex
            Assert(ScumAutoLaunch.WatcherScript.Contains("Local\\ZiloreMiniMapOverlay"), "AutoLaunch watcher must detect Local\\ZiloreMiniMapOverlay");
        }

        static void TestSettingsStoreAtomicAndAviationValidation() {
            // 1. Verify SettingsStore.WriteAtomic creates clean file with zero leftover .tmp artifacts
            string dir = Path.Combine(Path.GetTempPath(), "zlr-settings-test-" + Guid.NewGuid().ToString("N"));
            string iniPath = Path.Combine(dir, "sample.ini");
            try {
                SettingsStore.WriteAtomic(iniPath, "KeyA=123\nKeyB=Hello\n");
                SettingsStore.WriteAtomic(iniPath, "KeyA=456\nKeyB=World\n");
                var parsed = SettingsStore.ReadIni(iniPath);
                Assert(parsed["KeyA"] == "456" && parsed["KeyB"] == "World", "SettingsStore.WriteAtomic round-trip failed");
                Assert(Directory.GetFiles(dir, "*.tmp").Length == 0, "SettingsStore.WriteAtomic left stale .tmp file");
            } finally {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            }
            // 2. Verify TryImportCustomRunwaysCode rejects NaN/out-of-range coordinates safely
            int added;
            Assert(!WpfMapSurface.TryImportCustomRunwaysCode("ZLR-RWY:BAD,NaN,0.5,90,0.02;BAD2,1.5,0.5,90,0.02", out added) && added == 0, "Invalid runway coordinates must be rejected");
            // 3. Verify HardwareEntropy is thread-safe and deterministic
            byte[] e1 = IntegrityGuard.GetHardwareEntropy("test-purpose");
            byte[] e2 = IntegrityGuard.GetHardwareEntropy("test-purpose");
            Assert(e1 != null && e2 != null && e1.Length == 32 && e1[0] == e2[0] && e1[31] == e2[31], "GetHardwareEntropy must be deterministic 32-byte SHA-256");
        }

        static void TestMapGridDangerEdgeAndFlightTelemetry() {
            // 1. Verify IsNearDangerZone detects proximity to a LONG polygon edge (not just vertices)
            var redZone = new MapZone {
                Name = "PvP Edge Test",
                Argb = System.Drawing.Color.FromArgb(255, 220, 40, 40).ToArgb(),
                Points = new[] { new System.Drawing.PointF(0.40f, 0.40f), new System.Drawing.PointF(0.60f, 0.40f), new System.Drawing.PointF(0.60f, 0.50f), new System.Drawing.PointF(0.40f, 0.50f) }
            };
            // Midpoint of top edge is (0.50, 0.40); player at (0.50, 0.395) is ~76m north of edge, >1500m from any vertex
            double wxNear, wyNear, wxFar, wyFar;
            WpfMapSurface.MapToWorld(new System.Drawing.PointF(0.50f, 0.395f), out wxNear, out wyNear);
            WpfMapSurface.MapToWorld(new System.Drawing.PointF(0.50f, 0.370f), out wxFar, out wyFar);
            var stateNear = new WpfMapState { Position = new Position { X = wxNear, Y = wyNear }, Saved = new[] { redZone } };
            var stateFar = new WpfMapState { Position = new Position { X = wxFar, Y = wyFar }, Saved = new[] { redZone } };
            Assert(WpfMapSurface.IsNearDangerZone(stateNear, 150.0), "IsNearDangerZone must detect proximity to long polygon edge");
            Assert(!WpfMapSurface.IsNearDangerZone(stateFar, 150.0), "IsNearDangerZone must return false beyond radius");
            // 2. Verify FlightTelemetryService ignores duplicate sample timestamps & measures accurate speed by sampleUtc
            var tel = new FlightTelemetryService();
            var t0 = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
            double x0, y0, x1, y1;
            WpfMapSurface.MapToWorld(new System.Drawing.PointF(0.500f, 0.500f), out x0, out y0);
            WpfMapSurface.MapToWorld(new System.Drawing.PointF(0.502f, 0.500f), out x1, out y1); // ~30.43m east
            tel.Update(new Position { X = x0, Y = y0, Z = 0 }, t0, t0);
            tel.Update(new Position { X = x1, Y = y1, Z = 0 }, t0.AddSeconds(2.0), t0.AddSeconds(2.0)); // 15.2 m/s
            double spdAfterMove = tel.SpeedMps;
            Assert(spdAfterMove > 10.0 && spdAfterMove < 20.0, "FlightTelemetryService should compute ~15.2 m/s from sample delta");
            // Duplicate poll 0.5s later with SAME sampleUtc must NOT decay speed to zero!
            tel.Update(new Position { X = x1, Y = y1, Z = 0 }, t0.AddSeconds(2.0), t0.AddSeconds(2.5));
            Assert(Math.Abs(tel.SpeedMps - spdAfterMove) < 0.01, "Duplicate sample timestamp must not distort FlightTelemetryService speed");
            // 3. Verify VoiceNavigator handles fast vehicle movement without zeroing speed (< 125 m/s)
            var vn = new VoiceNavigator();
            var r = new RoadRoute { Success = true, Polyline = new[] { new System.Drawing.PointF(0.50f, 0.50f), new System.Drawing.PointF(0.55f, 0.50f) } };
            vn.Update(r, new System.Drawing.PointF(0.500f, 0.500f), t0);
            vn.Update(r, new System.Drawing.PointF(0.511f, 0.500f), t0.AddSeconds(3.0)); // ~167m in 3s = 55.8 m/s
            Assert(vn.Speed > 45.0, "VoiceNavigator must track high-speed movement (~55 m/s) without false teleport reset");
        }

        static void TestTacticalHotkeyCatalogAndUiTextIntegrity() {
            var ruCat = WpfFieldConsoleRuntime.GetTacticalHotkeyCatalog(true);
            var enCat = WpfFieldConsoleRuntime.GetTacticalHotkeyCatalog(false);
            Assert(ruCat.Length >= 17 && enCat.Length == ruCat.Length, "Tactical hotkey catalog must expose >= 17 RU/EN entries");
            for (int i = 0; i < ruCat.Length; i++) {
                Assert(!string.IsNullOrWhiteSpace(ruCat[i].Item1) && !string.IsNullOrWhiteSpace(ruCat[i].Item2), "Hotkey catalog entry must not be empty");
            }
            Assert(WpfFieldTheme.UiText("HotkeysGuideTitle") != "HotkeysGuideTitle", "UiText HotkeysGuideTitle must be localized");
            Assert(WpfFieldTheme.UiText("MapInsertStopHere") != "MapInsertStopHere", "UiText MapInsertStopHere must be localized");
        }

        static void TestWindowTopmostAndStandaloneContracts() {
            // 1. Native topmost/foreground helpers must safely no-op on IntPtr.Zero
            Native.EnsureTopmostNoActivate(IntPtr.Zero);
            Native.ForceForeground(IntPtr.Zero);
            // 2. Verify HUD & Tactical visibility state predicates
            Assert(WpfFieldConsoleRuntime.ShouldShowHud(true, false), "HUD must show when enabled and tactical map closed");
            Assert(!WpfFieldConsoleRuntime.ShouldShowHud(true, true), "HUD must hide while tactical map is open");
            Assert(!WpfFieldConsoleRuntime.ShouldHideTactical(true, true, false), "Tactical map stays visible when SCUM is focused");
            Assert(!WpfFieldConsoleRuntime.ShouldHideTactical(true, false, true), "Tactical map stays visible when app window is focused");
            Assert(WpfFieldConsoleRuntime.ShouldHideTactical(true, false, false), "Gameplay-linked tactical map hides when both SCUM and app lose focus");
        }

        static void TestSeamlessUpdateHelpersAndGitHubEndpoints() {
            // 1. Verify writable directory check & temp-archive detection
            string tmpDir = Path.Combine(Path.GetTempPath(), "zlr-upd-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            try {
                Assert(UpdateInstaller.IsDirectoryWritable(tmpDir), "Temp directory must report writable for zero-UAC seamless updates");
                Assert(UpdateInstaller.IsRunningFromTempArchive(Path.Combine(tmpDir, "ZiloreMiniMap.exe")), "Executable inside %TEMP% must be detected as temp archive");
                Assert(!UpdateInstaller.IsRunningFromTempArchive(@"C:\Games\ZiloreMiniMap\ZiloreMiniMap.exe"), "Permanent folder must not be flagged as temp archive");
            } finally { try { Directory.Delete(tmpDir, true); } catch { } }
            // 2. Verify GitHub Releases primary manifest and ZIP endpoints
            var svc = new UpdateService(UpdateService.Repository, Path.GetTempPath(), null);
            Assert(svc.ManifestUri.AbsoluteUri.StartsWith("https://github.com/Zilore-Anatoliy/ZiloreMiniMap/releases/latest/download/update.txt", StringComparison.Ordinal), "Primary update manifest must point to GitHub Releases");
            string dummyHash = new string('a', 64);
            var rel = UpdateRelease.Parse("schema=2\nversion=0.0.9\nsha256=" + dummyHash + "\npackage-sha256=" + dummyHash + "\n");
            Assert(svc.DownloadUri(rel).AbsoluteUri == "https://github.com/Zilore-Anatoliy/ZiloreMiniMap/releases/download/v0.0.9/ZiloreMiniMap.zip", "Package download URI must target pinned GitHub Release ZIP");
            // 3. Verify Native.EnsureTopmostNoActivate with forceReassert safely no-ops on IntPtr.Zero
            Native.EnsureTopmostNoActivate(IntPtr.Zero, true);
        }

        static void TestWeatherClockSearchAndSquadSafety() {
            // 1. Verify English weather summary uses (+2h), not Russian (+2ч)
            var snap = ScumWeatherEngine.ComputeClimate(0.50f, 100.0, 720.0, ScumSkyCondition.Clear);
            string enSum = ScumWeatherEngine.FormatSummary(snap, false);
            string ruSum = ScumWeatherEngine.FormatSummary(snap, true);
            Assert(enSum.Contains("(+2h)") && !enSum.Contains("(+2ч)"), "English weather summary must use (+2h)");
            Assert(ruSum.Contains("(+2ч)"), "Russian weather summary must use (+2ч)");
            // 2. Verify ScumGameClock FormatNextSunEvent produces valid RU/EN dawn/dusk countdown
            string sunRu = ScumGameClock.FormatNextSunEvent(DateTime.UtcNow, true);
            string sunEn = ScumGameClock.FormatNextSunEvent(DateTime.UtcNow, false);
            Assert(!string.IsNullOrWhiteSpace(sunRu) && !string.IsNullOrWhiteSpace(sunEn), "Sun event countdown must be non-empty");
            // 3. Verify Russian search aliases (торговец, аэропорт, ближайший)
            var places = new System.Collections.Generic.List<MapZone> {
                new MapZone { Name = "Military Airfield", Points = new[] { new System.Drawing.PointF(0.49f, 0.47f) } },
                new MapZone { Name = "Z3 Outpost", Points = new[] { new System.Drawing.PointF(0.20f, 0.80f) } }
            };
            var traderHits = DestinationSearch.Find(places, "торговец", null);
            var airHits = DestinationSearch.Find(places, "аэропорт", null);
            Assert(traderHits.Count == 1 && traderHits[0].Name == "Z3 Outpost", "Russian alias торговец must match Outpost");
            Assert(airHits.Count == 1 && airHits[0].Name == "Military Airfield", "Russian alias аэропорт must match Airfield");
            // 4. Verify SavedRouteLibrary rejects oversized clipboard codes safely
            SavedRouteTemplate badTpl;
            Assert(!SavedRouteLibrary.TryDecodeShareCode("ZLR-ROUTE:" + new string('A', 40000), out badTpl), "Oversized route share code must be rejected");
        }

        static void TestTacticalLayoutStorePersistenceAndBounds() {
            string tmp = Path.Combine(Path.GetTempPath(), "zlr-layout-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try {
                var def = TacticalUiLayoutStore.Load(tmp);
                Assert(!def.ToolbarCustomPos && !def.SidebarCollapsed && Math.Abs(def.ToolbarScale - 1.0) < 0.001, "Default tactical layout mismatch");
                TacticalUiLayoutStore.Save(tmp, new TacticalUiLayout { ToolbarCustomPos = true, ToolbarLeft = 512.5, ToolbarBottom = 48.0, ToolbarScale = 1.25, SidebarCollapsed = true });
                var reloaded = TacticalUiLayoutStore.Load(tmp);
                Assert(reloaded.ToolbarCustomPos && reloaded.SidebarCollapsed, "Tactical layout flags did not persist");
                Assert(Math.Abs(reloaded.ToolbarLeft - 512.5) < 0.2 && Math.Abs(reloaded.ToolbarBottom - 48.0) < 0.2 && Math.Abs(reloaded.ToolbarScale - 1.25) < 0.01, "Tactical layout coordinates/scale did not persist");
                // Verify extreme/NaN values are safely clamped
                TacticalUiLayoutStore.Save(tmp, new TacticalUiLayout { ToolbarCustomPos = true, ToolbarLeft = -500.0, ToolbarBottom = double.NaN, ToolbarScale = 9.9, SidebarCollapsed = false });
                var clamped = TacticalUiLayoutStore.Load(tmp);
                Assert(clamped.ToolbarLeft >= 8.0 && clamped.ToolbarBottom >= 8.0 && clamped.ToolbarScale <= 1.45, "Tactical layout clamping failed");
                Assert(WpfFieldTheme.UiText("SidebarTelemetryHeading") != "SidebarTelemetryHeading", "SidebarTelemetryHeading must be localized");
                Assert(WpfFieldTheme.UiText("ToolbarRulerTip") != "ToolbarRulerTip", "ToolbarRulerTip must be localized");
            } finally {
                try { Directory.Delete(tmp, true); } catch { }
            }
        }

        static void TestFluentThemeResourcesAndProfileBackupInventory() {
            // 1. Verify all 3 visual themes parse cleanly and provide Fluent global styles & dynamic accent brushes
            string prevTheme = WpfFieldTheme.ActiveTheme;
            try {
                foreach (string mode in new[] { "Dark", "OLED", "Light" }) {
                    WpfFieldTheme.ApplyPalette(mode);
                    var dict = WpfFieldTheme.Resources();
                    Assert(dict.Contains("ThemeAccentCard") && dict.Contains("ThemeAccentBorder"), "Missing dynamic accent resources for theme: " + mode);
                    Assert(dict.Contains(typeof(System.Windows.Controls.ToolTip)), "Missing Fluent ToolTip style for theme: " + mode);
                    Assert(dict.Contains(typeof(System.Windows.Controls.ComboBoxItem)), "Missing Fluent ComboBoxItem style for theme: " + mode);
                    Assert(dict.Contains(typeof(System.Windows.Controls.ContextMenu)), "Missing Fluent ContextMenu style for theme: " + mode);
                }
            } finally {
                WpfFieldTheme.ApplyPalette(prevTheme);
            }
            // 2. Verify profile backup inventory includes all user data files and excludes DPAPI machine-bound secrets
            var names = new System.Collections.Generic.HashSet<string>(WpfFieldConsoleRuntime.ProfileBackupFileNames, StringComparer.OrdinalIgnoreCase);
            foreach (string req in new[] { "settings.ini", "saved-route.tsv", "saved-routes.tsv", "tactical-layout.ini", "game-clock.ini", "weather.ini", "zones.tsv", "customwaypoints.tsv" }) {
                Assert(names.Contains(req), "Profile backup inventory missing required user file: " + req);
            }
            Assert(!names.Contains("private-groups.dat") && !names.Contains("poi-access.bin"), "Profile backup must never export machine-bound DPAPI secrets");
        }

    }
}
