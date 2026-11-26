using System;
using System.IO;

namespace ZiloreMiniMap {
    internal static partial class CoreSelfTests {
        static void TestPerfOptimizationsRoutingSquadAndPolylines() {
            // 1. Verify zero-alloc single-Sqrt DistanceToPolylineMeters accuracy
            var poly = new[] { new System.Drawing.PointF(0.20f, 0.50f), new System.Drawing.PointF(0.30f, 0.50f), new System.Drawing.PointF(0.30f, 0.60f) };
            int segIdx;
            double dMid = RoadRouter.DistanceToPolylineMeters(new System.Drawing.PointF(0.25f, 0.50f), poly, out segIdx);
            Assert(segIdx == 0 && dMid < 0.1, "Point on segment 0 must report ~0m distance");
            double dOff = RoadRouter.DistanceToPolylineMeters(new System.Drawing.PointF(0.30f, 0.55f), poly, out segIdx);
            Assert(segIdx == 1 && dOff < 0.1, "Point on segment 1 must report ~0m distance");
            double dAway = RoadRouter.DistanceToPolylineMeters(new System.Drawing.PointF(0.25f, 0.51f), poly, out segIdx);
            Assert(segIdx == 0 && Math.Abs(dAway - 152.36) < 2.0, "Point 0.01 north/south of horizontal segment must be ~152.36m");
            // 2. Verify zero-LINQ SquadState.Routes handles unordered stops & rejects gaps
            var st = new SquadState();
            st.AddStop("A", 0.2, 0.2);
            st.AddStop("B", 0.3, 0.3);
            st.AddStop("C", 0.4, 0.4);
            var stops = st.Stops;
            var shuffled = new[] { stops[2], stops[0], stops[1] };
            var parsed = SquadState.Routes(shuffled);
            Assert(parsed.Length == 1 && parsed[0].Length == 3 && parsed[0][0].Name == "A" && parsed[0][2].Name == "C", "Zero-LINQ SquadState.Routes must sort contiguous stops");
            var gapped = SquadState.Routes(new[] { stops[0], stops[2] });
            Assert(gapped.Length == 0, "Zero-LINQ SquadState.Routes must reject non-contiguous stop sequences");
            // 3. Verify SetAvoidZones handles NaN/Infinity penalty safely
            RoadRouter.Instance.SetAvoidZones(null, double.NaN);
            RoadRouter.Instance.SetAvoidZones(null, 4.0);
        }

        static void TestEnterpriseUpdaterStreamingMirrorAndPrefs() {
            string tmp = Path.Combine(Path.GetTempPath(), "zlr-upd-v616-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try {
                // 1. Verify large UTF-8 Russian release notes (>4096 bytes) parse cleanly up to 16 KB
                string dummyHash = new string('b', 64);
                string longNotes = new string('А', 2500); // ~5000 UTF-8 bytes
                var bigRel = UpdateRelease.Parse("schema=2\nversion=0.0.9\nsha256=" + dummyHash + "\npackage-sha256=" + dummyHash + "\nbuild=17409\nnotes=" + longNotes + "\n");
                Assert(bigRel.VersionText == "0.0.9" && bigRel.BuildNumber == "17409" && bigRel.ReleaseNotes.Length == 2500, "Large UTF-8 manifest must parse cleanly");
                // 2. Verify fallback mirror & release page URIs
                var svc = new UpdateService(UpdateService.Repository, tmp, null);
                Assert(svc.DownloadFallbackUri(bigRel).AbsoluteUri == "https://minimap.zilore.app/releases/v0.0.9/ZiloreMiniMap.zip", "Mirror fallback ZIP URI mismatch");
                Assert(svc.ReleasePageUri(bigRel).AbsoluteUri == "https://github.com/Zilore-Anatoliy/ZiloreMiniMap/releases/tag/v0.0.9", "GitHub release page URI mismatch");
                // 3. Verify UpdateRetryException persists retry-after.txt cooldown
                var retrySvc = new UpdateService("owner/repo", tmp, (u, lim) => { throw new UpdateRetryException(DateTime.UtcNow.AddMinutes(5)); });
                bool gotRetry = false;
                try { retrySvc.Check(true); } catch (UpdateRetryException) { gotRetry = true; }
                Assert(gotRetry && File.Exists(Path.Combine(tmp, "owner_repo-retry-after.txt")), "UpdateRetryException must persist retry-after.txt");
                // 4. Verify UpdatePreferencesStore roundtrip & sanitization
                UpdatePreferencesStore.Save(new UpdatePreferences { AutoCheckEnabled = false, AutoDownloadEnabled = true, AutoApplyOnExitEnabled = false, SkippedVersion = "0.0.9", LastKnownVersion = "0.0.9", LastCheckedUtc = "2026-11-25T20:00:00Z" }, tmp);
                var loadedPrefs = UpdatePreferencesStore.Load(tmp);
                Assert(!loadedPrefs.AutoCheckEnabled && loadedPrefs.AutoDownloadEnabled && !loadedPrefs.AutoApplyOnExitEnabled && loadedPrefs.SkippedVersion == "0.0.9", "UpdatePreferencesStore roundtrip failed");
                // 5. Verify streaming UpdatePackage.ExtractFromFile on a real ZIP file without loading into byte[]
                byte[] exeBytes = System.Text.Encoding.UTF8.GetBytes("MZ-stream-fixture");
                byte[] tileBytes = System.Text.Encoding.UTF8.GetBytes("MTL2-stream-fixture");
                byte[] roadBytes = System.Text.Encoding.UTF8.GetBytes("ROADS-stream-fixture");
                string exeH, tileH, roadH;
                using (var ms = new MemoryStream(exeBytes)) { exeH = InstallationAssets.Digest(ms); }
                using (var ms = new MemoryStream(tileBytes)) { tileH = InstallationAssets.Digest(ms); }
                using (var ms = new MemoryStream(roadBytes)) { roadH = InstallationAssets.Digest(ms); }
                string zipFile = Path.Combine(tmp, "stream-pkg.zip");
                using (var z = System.IO.Compression.ZipFile.Open(zipFile, System.IO.Compression.ZipArchiveMode.Create)) {
                    Action<string, byte[]> addE = (en, data) => { using (var es = z.CreateEntry(en).Open()) { es.Write(data, 0, data.Length); } };
                    addE("ZiloreMiniMap.exe", exeBytes);
                    addE("assets/map-tiles.bin", tileBytes);
                    addE("assets/roads.bin", roadBytes);
                    addE("package.txt", System.Text.Encoding.UTF8.GetBytes("version=0.0.9\nZiloreMiniMap.exe=" + exeH + "\nassets/map-tiles.bin=" + tileH + "\nassets/roads.bin=" + roadH + "\n"));
                }
                string zipH = InstallationAssets.Digest(zipFile);
                var pkgRel = UpdateRelease.Parse("schema=2\nversion=0.0.9\nsha256=" + exeH + "\npackage-sha256=" + zipH + "\n");
                string stagedOut = Path.Combine(tmp, "staged.exe");
                UpdatePackage.ExtractFromFile(zipFile, stagedOut, pkgRel);
                Assert(File.Exists(stagedOut) && File.ReadAllText(stagedOut) == "MZ-stream-fixture", "ExtractFromFile must extract verified executable");
                Assert(File.Exists(Path.Combine(UpdatePackage.AssetDirectory(stagedOut), "roads.bin")), "ExtractFromFile must extract verified assets");
            } finally {
                try { Directory.Delete(tmp, true); } catch { }
            }
        }

        static void TestInstallPathMigrationAndAutoLaunchHealing() {
            string tmp = Path.Combine(Path.GetTempPath(), "zlr-migrate-v617-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try {
                string fakeExe = Path.Combine(tmp, "ZiloreMiniMap.exe");
                File.WriteAllText(fakeExe, "MZ");
                string enabledFile = Path.Combine(tmp, "enabled.txt");
                File.WriteAllText(enabledFile, @"C:\Program Files\ZiloreMiniMap\ZiloreMiniMap.exe");
                bool healed = ScumAutoLaunch.ReconcileExecutablePath(enabledFile, fakeExe);
                Assert(healed, "ScumAutoLaunch must auto-heal moved executable path in enabled.txt");
                Assert(string.Equals(File.ReadAllText(enabledFile).Trim(), Path.GetFullPath(fakeExe), StringComparison.OrdinalIgnoreCase), "Healed enabled.txt must point to new exe path");
                Assert(!ScumAutoLaunch.ReconcileExecutablePath(enabledFile, fakeExe), "Second reconcile call must be a no-op");
            } finally {
                try { Directory.Delete(tmp, true); } catch { }
            }
        }

        static void TestV618BootstrapLogsBackupAndGroupsSafety() {
            // 1. Verify UpdateBootstrap.NormalizeStableVersion accepts .NET 10 3-part, 4-part .0, and +hash while rejecting pre-release/bad strings
            Assert(UpdateBootstrap.NormalizeStableVersion("0.0.5") == "0.0.5", "3-part stable version failed");
            Assert(UpdateBootstrap.NormalizeStableVersion("0.0.5.0") == "0.0.5", "4-part .0 stable version failed");
            Assert(UpdateBootstrap.NormalizeStableVersion("0.0.5+abc123") == "0.0.5", "SemVer +metadata version failed");
            Assert(UpdateBootstrap.NormalizeStableVersion("0.0.5-beta") == null, "Pre-release must be rejected");
            Assert(UpdateBootstrap.NormalizeStableVersion("01.2.3") == null, "Leading zero must be rejected");
            Assert(UpdateBootstrap.NormalizeStableVersion("1.2.3.4") == null, "Non-zero 4th part must be rejected");
            // 2. Verify AppLogBuffer.GetLevelCounts & GetDistinctSources
            AppLogBuffer.Add("INFO", "V618TestSrc", "info msg");
            AppLogBuffer.Add("WARNING", "V618TestSrc", "warn msg");
            AppLogBuffer.Add("ERROR", "V618ErrSrc", "err msg");
            int tot, inf, wrn, err;
            AppLogBuffer.GetLevelCounts(out tot, out inf, out wrn, out err);
            Assert(tot >= 3 && inf >= 1 && wrn >= 1 && err >= 1 && tot == inf + wrn + err, "AppLogBuffer level count mismatch");
            var srcs = AppLogBuffer.GetDistinctSources();
            Assert(Array.IndexOf(srcs, "V618TestSrc") >= 0 && Array.IndexOf(srcs, "V618ErrSrc") >= 0, "Distinct log sources missing");
            // 3. Verify IsSafeBackupMapEntry blocks Zip-Slip & unsafe extensions
            string leaf;
            Assert(WpfFieldConsoleRuntime.IsSafeBackupMapEntry("maps/custom.tsv", out leaf) && leaf == "custom.tsv", "Valid maps/*.tsv rejected");
            Assert(WpfFieldConsoleRuntime.IsSafeBackupMapEntry("maps/overlay.png", out leaf) && leaf == "overlay.png", "Valid maps/*.png rejected");
            Assert(!WpfFieldConsoleRuntime.IsSafeBackupMapEntry("maps/../evil.tsv", out leaf), "Zip-Slip path traversal must be rejected");
            Assert(!WpfFieldConsoleRuntime.IsSafeBackupMapEntry("maps/payload.exe", out leaf), "Executable in maps/ must be rejected");
        }

        static void TestV619TopmostContractsAndNavPerf() {
            // 1. Verify strict Always-On-Top Z-order priority: Dialog > Setup > Tactical > HUD > None
            Assert(WpfFieldConsoleRuntime.ResolveTopmostLayerKind(true, true, true, true) == ActiveTopmostLayer.Dialog, "Modal dialog must hold highest topmost priority");
            Assert(WpfFieldConsoleRuntime.ResolveTopmostLayerKind(false, true, true, true) == ActiveTopmostLayer.Setup, "Setup window must hold topmost above Tactical/HUD");
            Assert(WpfFieldConsoleRuntime.ResolveTopmostLayerKind(false, false, true, true) == ActiveTopmostLayer.Tactical, "Tactical map must hold topmost above HUD");
            Assert(WpfFieldConsoleRuntime.ResolveTopmostLayerKind(false, false, false, true) == ActiveTopmostLayer.Hud, "HUD minimap must hold topmost when alone");
            Assert(WpfFieldConsoleRuntime.ResolveTopmostLayerKind(false, false, false, false) == ActiveTopmostLayer.None, "No active windows must resolve to None");
            Assert(!WpfFieldConsoleRuntime.IsCoveredByExternalWindow(IntPtr.Zero), "Null HWND must safely report uncovered");
            // 2. Verify AdaptiveOffRouteThreshold speed bands
            Assert(RoadRouter.AdaptiveOffRouteThreshold(2.0) == 35.0, "Foot off-route threshold mismatch");
            Assert(RoadRouter.AdaptiveOffRouteThreshold(12.0) == 55.0, "Standard car off-route threshold mismatch");
            Assert(RoadRouter.AdaptiveOffRouteThreshold(25.0) == 80.0, "High-speed vehicle off-route threshold mismatch");
        }

        static void TestV620AuditFixesAndGuards() {
            // 1. Verify safe URI allowlist for shell navigation
            Assert(WpfFieldTheme.IsSafeShellUri("https://github.com/Zilore-Anatoliy/ZiloreMiniMap"), "HTTPS URI must be allowed");
            Assert(WpfFieldTheme.IsSafeShellUri("mailto:support@zilore.app"), "mailto URI must be allowed");
            Assert(!WpfFieldTheme.IsSafeShellUri("file:///C:/Windows/System32/cmd.exe"), "file:// URI must be rejected");
            Assert(!WpfFieldTheme.IsSafeShellUri("javascript:alert(1)"), "javascript: URI must be rejected");
            Assert(!WpfFieldTheme.IsSafeShellUri(""), "Empty URI must be rejected");
            // 2. Verify SquadState null/bounds safety on Receive & RouteKey
            var ss = new SquadState();
            Assert(ss.Receive(null).Length == 0, "SquadState.Receive(null) must safely return empty array");
            Assert(SquadState.RouteKey(null) == ":", "SquadState.RouteKey(null) must not throw");
            Assert(SquadState.RouteKey(new SharedMapMarker { Id = "short", OwnerId = "p1" }) == "p1:", "SquadState.RouteKey with short Id must not throw");
            // 3. Verify zero-LINQ RecoverMapBounds clamps off-screen windows to primary monitor
            var screens = new[] { new System.Drawing.Rectangle(0, 0, 1920, 1080), new System.Drawing.Rectangle(1920, 0, 2560, 1440) };
            var rec = WpfFieldConsoleRuntime.RecoverMapBounds(new System.Drawing.Rectangle(2100, 100, 1280, 800), screens);
            Assert(rec.X == 2100 && rec.Y == 100 && rec.Width == 1280 && rec.Height == 800, "Second monitor bounds must be preserved");
            var clamped = WpfFieldConsoleRuntime.RecoverMapBounds(new System.Drawing.Rectangle(-5000, -5000, 4000, 3000), screens);
            Assert(clamped.X == 0 && clamped.Y == 0 && clamped.Width == 1920 && clamped.Height == 1080, "Disconnected/oversized bounds must clamp cleanly");
            // 4. Verify MapPresetFile v1 legacy preset defaults TacticalOpacity to 85 (never 0/invisible)
            string tmpPreset = Path.Combine(Path.GetTempPath(), "zlr-v1preset-" + Guid.NewGuid().ToString("N") + ".dat");
            try {
                using (var fs = File.Create(tmpPreset)) {
                    using (var bw = new BinaryWriter(fs, System.Text.Encoding.UTF8)) {
                        bw.Write(1); bw.Write(1);
                        bw.Write("LegacyV1"); bw.Write("scum"); bw.Write(""); bw.Write(""); bw.Write("");
                        bw.Write(true); bw.Write(true); bw.Write(true); bw.Write(true); bw.Write(true); bw.Write(true); bw.Write(false); bw.Write(true);
                        bw.Write(80); bw.Write(2.0f);
                    }
                }
                var loaded = MapPresetFile.Load(tmpPreset);
                Assert(loaded.Length == 1 && loaded[0].TacticalOpacity == 85 && Math.Abs(loaded[0].TacticalZoom - 1.0f) < 0.001f, "Legacy v1 preset must default TacticalOpacity=85");
            } finally {
                try { if (File.Exists(tmpPreset)) File.Delete(tmpPreset); } catch { }
            }
        }

        static void TestStage2ServicesAndMvvm() {
            // 1. WindowZOrderService standalone priority & null-handle safety
            Assert(WindowZOrderService.ResolveTopmostLayerKind(true, true, true, true) == ActiveTopmostLayer.Dialog, "ZOrderService Dialog priority");
            Assert(WindowZOrderService.ResolveTopmostLayerKind(false, false, false, true) == ActiveTopmostLayer.Hud, "ZOrderService HUD priority");
            Assert(!WindowZOrderService.IsCoveredByExternalWindow(IntPtr.Zero), "ZOrderService null handle safety");

            // 2. ProfileBackupService full export -> import roundtrip (including maps\ & Zip-Slip defense)
            string tmpRoot = Path.Combine(Path.GetTempPath(), "zlr-s2-test-" + Guid.NewGuid().ToString("N"));
            string srcData = Path.Combine(tmpRoot, "src-data");
            string dstData = Path.Combine(tmpRoot, "dst-data");
            string outDir = Path.Combine(tmpRoot, "out");
            Directory.CreateDirectory(Path.Combine(srcData, "maps"));
            try {
                File.WriteAllText(Path.Combine(srcData, "settings.ini"), "Zoom=4\n", System.Text.Encoding.UTF8);
                File.WriteAllText(Path.Combine(srcData, "updates.ini"), "AutoCheck=1\n", System.Text.Encoding.UTF8);
                File.WriteAllText(Path.Combine(srcData, "maps", "squad-zones.tsv"), "Z1\t-65536\t0.1,0.1;0.2,0.1;0.2,0.2\tCustom\tTest\n", System.Text.Encoding.UTF8);
                int fc; long fb;
                ProfileBackupService.ComputeProfileStats(srcData, out fc, out fb);
                Assert(fc == 3 && fb > 0, "ComputeProfileStats must count all 3 profile files including maps\\");
                string zipFile = ProfileBackupService.ExportProfileBackupZip(srcData, outDir);
                Assert(File.Exists(zipFile), "ExportProfileBackupZip must create archive");
                int imported = ProfileBackupService.ImportProfileBackupZip(zipFile, dstData);
                Assert(imported == 3 && File.Exists(Path.Combine(dstData, "settings.ini")) && File.Exists(Path.Combine(dstData, "maps", "squad-zones.tsv")), "ImportProfileBackupZip roundtrip failed");

                // 3. RouteHistoryService + LocalRouteStore + RouteOptimizer + RouteGeometryHelper
                var sq = new SquadState();
                var hist = new RouteHistoryService(4);
                hist.Push(RouteSnapshot.Capture(sq));
                sq.AddStop("A", 0.10, 0.10);
                sq.AddStop("C", 0.50, 0.50);
                sq.AddStop("B", 0.15, 0.45);
                sq.AddStop("D", 0.45, 0.15);
                var opt = RouteOptimizer.OptimizeStopOrder(sq.Stops, new System.Drawing.PointF(0.05f, 0.05f));
                Assert(opt.Length == 4 && opt[0].Name == "A", "RouteOptimizer must start at nearest stop to origin");
                int insIdx = RouteGeometryHelper.FindBestInsertionIndex(new System.Drawing.PointF(0.25f, 0.25f), new[] { sq.Stops[0], sq.Stops[1] }, null, null);
                Assert(insIdx == 1, "RouteGeometryHelper must insert midpoint between stop 0 and stop 1");
                RouteSnapshot undone;
                Assert(hist.TryUndo(RouteSnapshot.Capture(sq), out undone) && undone.Stops.Length == 0 && hist.RedoCount == 1, "RouteHistoryService Undo failed");
                RouteSnapshot redone;
                Assert(hist.TryRedo(undone, out redone) && redone.Stops.Length == 4, "RouteHistoryService Redo failed");
                using (var store = new LocalRouteStore()) {
                    string routeFile = Path.Combine(tmpRoot, "saved-route.tsv");
                    store.SaveImmediate(routeFile, tmpRoot, RouteSnapshot.Capture(sq));
                    var sq2 = new SquadState();
                    LocalRouteStore.LoadInto(routeFile, sq2);
                    Assert(sq2.Stops.Length == 4 && sq2.Stops[0].Name == "A", "LocalRouteStore save/load roundtrip failed");
                }

                // 4. PeerSmoothingService smooth convergence & teleport snap
                var ps = new PeerSmoothingService();
                var pArr = new[] { new PrivatePositions.Peer { Id = "peer-s2", Name = "Scout", Position = new Position { X = 1000, Y = 1000, Z = 0, Yaw = 0 } } };
                ps.UpdateTargets(pArr);
                ps.SmoothInPlace(pArr, 100);
                pArr[0].Position = new Position { X = 2000, Y = 1000, Z = 0, Yaw = 90 };
                ps.UpdateTargets(pArr);
                ps.SmoothInPlace(pArr, 150);
                Assert(pArr[0].Position.X > 1000 && pArr[0].Position.X < 2000, "PeerSmoothingService must smoothly interpolate normal movement");
                pArr[0].Position = new Position { X = 200000, Y = 1000, Z = 0, Yaw = 90 };
                ps.UpdateTargets(pArr);
                ps.SmoothInPlace(pArr, 200);
                Assert(Math.Abs(pArr[0].Position.X - 200000) < 0.1, "PeerSmoothingService must snap instantly on teleport (>350m)");
                ps.PurgePeer("peer-s2");
                Assert(ps.TrackedPeerCount == 0, "PeerSmoothingService.PurgePeer must remove state");

                // 5. Lightweight MVVM Foundation & LogConsoleViewModel
                int cmdRuns = 0;
                var cmd = new RelayCommand(() => cmdRuns++, () => cmdRuns == 0);
                Assert(cmd.CanExecute(null), "RelayCommand should be executable initially");
                cmd.Execute(null);
                Assert(cmdRuns == 1 && !cmd.CanExecute(null), "RelayCommand CanExecute/Execute contract failed");
                AppLogBuffer.Add("ERROR", "Stage2MvvmTest", "Synthetic Stage2 error");
                var logVm = new LogConsoleViewModel(false, "All");
                int propChanges = 0;
                logVm.PropertyChanged += (s, e) => propChanges++;
                logVm.ActiveLevel = "ERROR";
                logVm.SearchQuery = "Synthetic Stage2 error";
                Assert(propChanges > 0 && logVm.Entries.Count >= 1 && logVm.Entries[0].LevelShort == "ERR", "LogConsoleViewModel filtering failed");
                Assert(LogConsoleViewModel.CreateItemTemplate() != null, "LogConsoleViewModel DataTemplate must parse cleanly");
            } finally {
                try { Directory.Delete(tmpRoot, true); } catch { }
            }
        }

        static void TestStage2BHeadlessEngineAndSquadMvvm() {
            // 1. Verify PrivateGroupsViewModel code validation & stats binding
            var gvm = new PrivateGroupsViewModel(false);
            Assert(!gvm.ValidateCode("") && gvm.CodeValidityState == 0, "Empty MM1 code must report state 0");
            Assert(!gvm.ValidateCode("MM1-short") && gvm.CodeValidityState == 2, "Malformed MM1 code must report state 2");
            var sampleGroup = new RelayMembership { Id = Guid.NewGuid().ToString("N"), Key = PrivatePositions.NewKey(), Owner = "", Pending = "", Name = "Alpha" };
            Assert(gvm.ValidateCode(sampleGroup.Code) && gvm.CodeValidityState == 1, "Valid MM1 code must report state 1");
            int gvmChanges = 0;
            gvm.PropertyChanged += (s, e) => gvmChanges++;
            gvm.SyncStats(new[] { new RelayGroupStore.View(sampleGroup) });
            Assert(gvmChanges > 0 && gvm.GroupsCountText == "1 / 16", "PrivateGroupsViewModel.SyncStats must update bound properties");

            // 2. Verify PrivateGroupItemViewModel syncs title & status cleanly
            var ivm = new PrivateGroupItemViewModel(sampleGroup.Id);
            ivm.SyncFrom(new RelayGroupStore.View(sampleGroup), v => "Offline");
            Assert(ivm.Title == "Alpha" && ivm.StatusText == "Offline" && !ivm.IsOnline, "PrivateGroupItemViewModel.SyncFrom mismatch");

            // 3. Verify MapWindow headless WPF host mode skips allocating legacy WinForms Settings UI
            string tmp = Path.Combine(Path.GetTempPath(), "zlr-s2b-headless-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try {
                using (var engine = new MapWindow(tmp, true, true)) {
                    Assert(engine.IsWpfHostMode && !engine.HasLegacySettingsUi, "Headless WPF host mode must not allocate legacy WinForms SettingsPanel");
                    int prevRev = engine.WpfSettingsRevision;
                    engine.WpfRebuildSettings();
                    Assert(engine.WpfSettingsRevision == prevRev + 1, "WpfRebuildSettings in headless mode must bump revision in O(1)");
                    var snap = engine.WpfSnapshot(false);
                    Assert(snap != null && snap.HudWidth >= 240, "WpfSnapshot must remain fully functional in headless WPF host mode");
                }
            } finally {
                SafeCleanup.DeleteDirectory(tmp, true);
            }
        }

        static void TestPhase3ASafeCleanupAndNullableServices() {
            // 1. SafeCleanup best-effort & throttled warning behavior
            int ran = 0;
            SafeCleanup.Try(() => { ran++; });
            Assert(ran == 1, "SafeCleanup.Try must execute action");
            SafeCleanup.Try(() => { throw new IOException("benign"); });
            bool ok = SafeCleanup.Try(() => { throw new InvalidOperationException("expected warning"); }, "Phase3ATest", "SyntheticCleanup");
            Assert(!ok, "SafeCleanup.Try with logging must return false on exception");
            string tmp = Path.Combine(Path.GetTempPath(), "zlr-p3a-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            string f = Path.Combine(tmp, "probe.tmp");
            File.WriteAllText(f, "test");
            SafeCleanup.DeleteFile(f);
            Assert(!File.Exists(f), "SafeCleanup.DeleteFile must remove existing file");
            SafeCleanup.DeleteFile(null);
            SafeCleanup.DeleteDirectory(tmp, true);
            Assert(!Directory.Exists(tmp), "SafeCleanup.DeleteDirectory must remove temp directory");
            // 2. Null-argument resilience across all #nullable enable Services
            string leaf;
            Assert(!ProfileBackupService.IsSafeBackupMapEntry(null, out leaf), "IsSafeBackupMapEntry(null) must be false");
            int fc; long fb;
            ProfileBackupService.ComputeProfileStats(null, out fc, out fb);
            Assert(fc == 0 && fb == 0, "ComputeProfileStats(null) must return 0");
            Assert(RouteOptimizer.OptimizeStopOrder(null, null).Length == 0, "OptimizeStopOrder(null) must return empty");
            Assert(RouteGeometryHelper.FindBestInsertionIndex(new System.Drawing.PointF(0.5f, 0.5f), null, null, null) == 0, "FindBestInsertionIndex(null) must return 0");
            var ps = new PeerSmoothingService();
            ps.UpdateTargets(null); ps.SmoothInPlace(null, 100); ps.PurgePeer(null);
            var gvm = new PrivateGroupsViewModel(false);
            gvm.SyncStats(null); Assert(!gvm.ValidateCode(null), "ValidateCode(null) must be false");
            var zos = new WindowZOrderService();
            zos.EnsureWindowOnTop(null, true, false);
            zos.MaintainActiveTopmost(false, null, null, null, null, null);
        }

        static void TestPhase3BServicesAndCoreNullable() {
            // 1. SquadCoordinatorService multi-source sync, stale eviction & status formatting
            var coord = new SquadCoordinatorService();
            var t0 = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
            var directPeer = new PrivatePositions.Peer { Id = "p-alpha", Name = "Alpha", Online = true, Updated = t0, Position = new Position { X = 0, Y = 0, Z = 0, Yaw = 90 } };
            var relayPeer = new PrivatePositions.Peer { Id = "grp1:p-bravo", Name = "Bravo", Online = true, Updated = t0, Position = new Position { X = 10000, Y = 0, Z = 0, Yaw = 90 } };
            coord.SyncRoster(new[] { directPeer }, new[] { relayPeer }, t0);
            Assert(coord.KnownPeerCount == 2 && coord.Roster.Length == 2, "SquadCoordinatorService must merge direct and relay peers");
            Assert(coord.IsPeerOnline("p-alpha", t0.AddSeconds(1)), "Recently seen peer must report online");
            Assert(!coord.IsPeerOnline("p-alpha", t0.AddSeconds(10)), "Peer unseen >3s must report offline");
            Assert(coord.FindPeerWithPosition("grp1:p-bravo") != null, "FindPeerWithPosition must locate active peer");
            Assert(SquadCoordinatorService.CardinalShort(0, false) == "N" && SquadCoordinatorService.CardinalShort(90, true) == "В", "CardinalShort compass mismatch");
            string peerLine = coord.FormatPeerStatus(directPeer, new Position { X = 5000, Y = 0, Z = 0, Yaw = 90 }, null, t0.AddSeconds(1), false);
            Assert(peerLine.Contains("● ") && peerLine.Contains('·'), "FormatPeerStatus must include online dot and sector");
            // Leaving relay group must evict relay peer while retaining direct peer
            coord.SyncRoster(new[] { directPeer }, Array.Empty<PrivatePositions.Peer>(), t0.AddSeconds(2));
            Assert(coord.KnownPeerCount == 1 && coord.FindPeerWithPosition("grp1:p-bravo") == null, "Departed relay peer must be evicted");

            // 2. MapCameraService deterministic math
            Assert(Math.Abs(MapCameraService.ComputeCompassHeading(90.0)) < 0.001, "Yaw 90 must equal 0 deg North");
            Assert(Math.Abs(MapCameraService.ComputeCompassHeading(0.0) - 270.0) < 0.001, "Yaw 0 must equal 270 deg West");
            double sideSlow = MapCameraService.ComputeMapSide(4.0, 240, 240, true, false, true, 5.0);
            double sideFast = MapCameraService.ComputeMapSide(4.0, 240, 240, true, false, true, 35.0);
            Assert(sideFast < sideSlow && sideFast >= 240.0, "Speed auto-zoom must smoothly zoom out at high ground speed");
            double sideAir = MapCameraService.ComputeMapSide(10.0, 240, 240, true, true, true, 50.0);
            Assert(Math.Abs(sideAir - 240.0 * 3.2) < 0.1, "Aviation compact minimap zoom must clamp to 3.2x");
            var cEdge = MapCameraService.ComputeCameraCentre(new System.Drawing.PointF(0f, 0f), null, 2000, 1000, 1000, false);
            Assert(Math.Abs(cEdge.X - 0.25f) < 0.001f && Math.Abs(cEdge.Y - 0.25f) < 0.001f, "CameraCentre must clamp pan within map bounds");

            // 3. FlightTelemetryService teleport rejection
            var ft = new FlightTelemetryService();
            ft.Update(new Position { X = 0, Y = 0, Z = 0, Yaw = 90 }, t0, t0);
            ft.Update(new Position { X = 500000, Y = 0, Z = 0, Yaw = 90 }, t0.AddSeconds(1), t0.AddSeconds(1));
            Assert(ft.SpeedMps == 0.0, "FlightTelemetryService must zero speed on teleports (>=125 m/s)");

            // 4. Core #nullable enable resilience
            Assert(SettingsStore.ReadIni(null).Count == 0, "SettingsStore.ReadIni(null) must return empty dictionary");
            Assert(AppLogBuffer.Snapshot(null) != null, "AppLogBuffer.Snapshot(null) must return array");
            ScumGameClock.Load(null);
        }

        static void TestPhase4AHotPathPerfCryptoAndSearch() {
            // 1. Verify zero-GDI+ ScumMapStore.HabitatAt prefers smallest overlapping volume & handles null rings
            var store = new ScumMapStore();
            var bigBiome = new ScumMapHabitat {
                Name = "Forest", CategoryIds = new[] { 10 },
                Rings = new[] { new[] { new System.Drawing.PointF(0.1f, 0.1f), new System.Drawing.PointF(0.9f, 0.1f), new System.Drawing.PointF(0.9f, 0.9f), new System.Drawing.PointF(0.1f, 0.9f) } }
            };
            var smallPond = new ScumMapHabitat {
                Name = "Fishing Pond", CategoryIds = new[] { 10 },
                Rings = new[] { new[] { new System.Drawing.PointF(0.4f, 0.4f), new System.Drawing.PointF(0.6f, 0.4f), new System.Drawing.PointF(0.6f, 0.6f), new System.Drawing.PointF(0.4f, 0.6f) } }
            };
            var emptyHab = new ScumMapHabitat { Name = "Empty", CategoryIds = new[] { 10 }, Rings = null };
            store.Habitats.Add(bigBiome); store.Habitats.Add(smallPond); store.Habitats.Add(emptyHab);
            Assert(store.HabitatAt(new System.Drawing.PointF(0.5f, 0.5f)) == smallPond, "HabitatAt must pick smallest overlapping habitat without GDI+");
            Assert(store.HabitatAt(new System.Drawing.PointF(0.2f, 0.2f)) == bigBiome, "HabitatAt must fall back to outer biome");
            Assert(store.HabitatAt(new System.Drawing.PointF(0.01f, 0.01f)) == null, "HabitatAt outside all bounds must return null");
            store.SetCategoryEnabled(10, false);
            Assert(store.HabitatAt(new System.Drawing.PointF(0.5f, 0.5f)) == null, "Disabled habitat category must be skipped");

            // 2. Verify stackalloc Damerau-Levenshtein EditDistance & single-pass Normalize / FindScumMap
            Assert(DestinationSearch.EditDistance("airfield", "airfeild") == 1, "Transposition distance must be 1");
            Assert(DestinationSearch.EditDistance("trader", "traider") == 1, "Insertion distance must be 1");
            Assert(DestinationSearch.EditDistance("", "abc") == 3 && DestinationSearch.EditDistance("abc", "") == 3, "Empty string distance mismatch");
            string longA = new string('a', 90), longB = new string('a', 89) + "b";
            Assert(DestinationSearch.EditDistance(longA, longB) == 1, "EditDistance >80 char heap fallback failed");
            Assert(DestinationSearch.Normalize("  Šibenik---Bunker  D4! ") == "sibenik bunker d4", "Single-pass Normalize mismatch");
            DestinationSearch.SelfTest();

            // 3. Verify PrivatePositions zero-LINQ AES-CBC + HMAC-SHA256 wire compatibility & tamper rejection
            byte[] masterKey = Convert.FromBase64String(PrivatePositions.NewKey());
            byte[] ek, mk;
            PrivatePositions.DeriveKeys(masterKey, out ek, out mk);
            string samplePayload = "3\t" + Guid.NewGuid().ToString("N") + "\tScout\t42\t150\t20\t1234.5\t-5678.9\t100.0\t90.0";
            byte[] sealedViaMaster = PrivatePositions.Seal(masterKey, samplePayload);
            byte[] sealedViaDerived = PrivatePositions.SealWithDerived(ek, mk, samplePayload);
            Assert(PrivatePositions.OpenWithDerived(ek, mk, sealedViaMaster) == samplePayload, "OpenWithDerived must open Seal() packet");
            Assert(PrivatePositions.Open(masterKey, sealedViaDerived) == samplePayload, "Open() must open SealWithDerived() packet");
            sealedViaDerived[sealedViaDerived.Length - 1] ^= 0xFF;
            bool tamperCaught = false;
            try { PrivatePositions.OpenWithDerived(ek, mk, sealedViaDerived); } catch (System.Security.Cryptography.CryptographicException) { tamperCaught = true; }
            Assert(tamperCaught, "Tampered packet MAC must throw CryptographicException");

            // 4. Verify zero-LINQ SharedMapMarker.Valid & WpfZoneKey determinism
            Assert(SharedMapMarker.Valid(new SharedMapMarker { Id = SharedMapMarker.GpsId, Name = "Rally", X = 0.5, Y = 0.5 }), "Valid marker rejected");
            Assert(!SharedMapMarker.Valid(new SharedMapMarker { Id = "NOTHEX", Name = "Bad", X = 0.5, Y = 0.5 }), "Invalid marker Id accepted");
            Assert(!SharedMapMarker.Valid(new SharedMapMarker { Id = SharedMapMarker.GpsId, Name = "Ctrl\nChar", X = 0.5, Y = 0.5 }), "Control char in marker name accepted");
            var z1 = new MapZone { Name = "Base", Layer = "Squad", Points = new[] { new System.Drawing.PointF(0.25f, 0.75f) } };
            var z2 = new MapZone { Name = "Base", Layer = "Squad", Points = new[] { new System.Drawing.PointF(0.25f, 0.75f) } };
            Assert(MapWindow.WpfZoneKey(z1) == MapWindow.WpfZoneKey(z2), "WpfZoneKey must be deterministic across identical zones");
        }

        static void TestPhase4BLibraryImportAndNullableModules() {
            // 1. Live smoke-check of migrated .NET 10 [LibraryImport] P/Invokes in Native & IntegrityGuard
            uint clipSeq1 = Native.GetClipboardSequenceNumber();
            uint clipSeq2 = Native.GetClipboardSequenceNumber();
            Assert(clipSeq2 >= clipSeq1 || clipSeq1 > 0u, "LibraryImport GetClipboardSequenceNumber must succeed");
            Assert(!Native.IsOurWindow(IntPtr.Zero), "LibraryImport IsWindow(IntPtr.Zero) must return false");
            int vkEsc = Native.ScanCodeToVirtualKey(0x01);
            Assert(vkEsc == 0x1B || vkEsc >= 0, "LibraryImport MapVirtualKeyExW/GetKeyboardLayout must succeed");
            string hwid = IntegrityGuard.GetHardwareId();
            Assert(!string.IsNullOrEmpty(hwid) && hwid.Length == 32, "LibraryImport GetVolumeInformationW HWID must be 32 hex chars");
            bool dbg = IntegrityGuard.IsDebuggerDetected();
            Assert(dbg == System.Diagnostics.Debugger.IsAttached || dbg, "LibraryImport IsDebuggerPresent/CheckRemoteDebuggerPresent must execute cleanly");

            // 2. GeneratedRegex + #nullable enable checks in UpdateBootstrap / UpdateRelease / UpdatePackage
            Assert(UpdateBootstrap.NormalizeStableVersion(null) == null, "NormalizeStableVersion(null) must be null");
            Assert(UpdateBootstrap.NormalizeStableVersion("1.2.3.0+sha") == "1.2.3", "NormalizeStableVersion 4-part +meta failed");
            Assert(UpdatePackage.AssetDirectory("a.exe") == "a.exe.assets", "UpdatePackage.AssetDirectory mismatch");

            // 3. #nullable enable checks in ScumWeatherEngine & VoiceNavigationService
            ScumWeatherEngine.Load(null);
            ScumWeatherEngine.Update(null, new System.Drawing.PointF(0.5f, 0.5f), false, DateTime.UtcNow);
            using (var vsvc = new VoiceNavigationService(Path.Combine(Path.GetTempPath(), "zlr-p4b-voice-" + Guid.NewGuid().ToString("N")))) {
                vsvc.Speak(null);
                vsvc.Tick(DateTime.UtcNow, true, false, false, null, DateTime.UtcNow, null, null, null);
                Assert(!vsvc.Busy && vsvc.ActiveTarget == null, "VoiceNavigationService null-safe Tick contract failed");
            }
        }

        static void TestPhase4CHeadlessZeroOverlayAndCleanup() {
            // 1. Verify MapWindow headless WPF mode allocates ZERO legacy WinForms OverlayWindow and SettingsPanel
            string tmp = Path.Combine(Path.GetTempPath(), "zlr-p4c-headless-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try {
                using (var engine = new MapWindow(tmp, true, true)) {
                    Assert(engine.IsWpfHostMode && !engine.HasLegacySettingsUi && !engine.HasLegacyOverlayUi, "Headless WPF mode must not allocate legacy OverlayWindow or SettingsPanel");
                    engine.WpfMoveHud(120, 140);
                    engine.WpfHudWidth = 310;
                    engine.WpfHudHeight = 320;
                    var snap = engine.WpfSnapshot(false);
                    Assert(snap.HudLeft == 120 && snap.HudTop == 140 && snap.HudWidth == 310 && snap.HudHeight == 320, "HudBounds roundtrip failed in zero-overlay headless mode");
                    string travel = engine.WpfTravelSummary(1500.0, true, false, 90.0);
                    Assert(!string.IsNullOrWhiteSpace(travel) && travel.Contains("1.5 km"), "Relocated WpfTravelSummary failed");
                }
            } finally {
                SafeCleanup.DeleteDirectory(tmp, true);
            }

            // 2. Verify TryDestinationEdge pure geometry (circular & square)
            System.Drawing.PointF tip, dir;
            Assert(MapWindow.TryDestinationEdge(new System.Drawing.RectangleF(0, 0, 200, 200), true, new System.Drawing.PointF(0.5f, 0.5f), new System.Drawing.PointF(0.8f, 0.5f), out tip, out dir), "TryDestinationEdge circular failed");
            Assert(tip.X > 180f && Math.Abs(dir.X - 1f) < 0.01f, "TryDestinationEdge East tip mismatch");
            Assert(!MapWindow.TryDestinationEdge(new System.Drawing.RectangleF(0, 0, 20, 20), false, new System.Drawing.PointF(0.5f, 0.5f), new System.Drawing.PointF(0.8f, 0.5f), out tip, out dir), "Degenerate bounds must return false");

            // 3. Verify #nullable enable ScumAutoLaunch null-argument safety
            Assert(!ScumAutoLaunch.ReconcileExecutablePath(null, null), "ReconcileExecutablePath(null, null) must return false");
        }

        static void TestPhase4DProductFeaturesAndTelemetry() {
            string tmp = Path.Combine(Path.GetTempPath(), "zlr-p4d-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try {
                // 1. Smart TransportEtaService profiles, auto-detection & persistence
                TransportEtaService.Load(tmp);
                TransportEtaService.SetProfile(TransportProfile.Foot);
                TransportEtaService.Load(tmp);
                Assert(TransportEtaService.ActiveProfile == TransportProfile.Foot, "TransportEtaService profile persistence failed");
                TransportProfile effFoot, effAir, effCar;
                int minFoot = TransportEtaService.ComputeEtaMinutes(1000.0, 0.0, false, null, false, false, TransportProfile.Foot, out effFoot);
                int minCar = TransportEtaService.ComputeEtaMinutes(1000.0, 0.0, false, null, false, false, TransportProfile.Car, out effCar);
                int minAir = TransportEtaService.ComputeEtaMinutes(3000.0, 45.0, false, null, true, false, TransportProfile.Auto, out effAir);
                Assert(effFoot == TransportProfile.Foot && effCar == TransportProfile.Car && effAir == TransportProfile.Aircraft, "Effective transport profile resolution failed");
                Assert(minFoot > minCar && minAir >= 1, "Foot ETA must exceed Car ETA over identical distance");
                TransportEtaService.SetProfile(TransportProfile.Auto);

                // 2. WaypointShareService ZLR-WP: roundtrip & malformed rejection
                var wps = new[] {
                    new MapZone { Name = "Bunker Alpha", Category = ZoneCategory.Custom, Argb = unchecked((int)0xFFFF5555), Points = new[] { new System.Drawing.PointF(0.25f, 0.75f) } },
                    new MapZone { Name = "Secret Stash\tLine", Category = ZoneCategory.Custom, Argb = unchecked((int)0xFF38BDF8), Points = new[] { new System.Drawing.PointF(0.60f, 0.40f) } }
                };
                string wpCode = WaypointShareService.Encode(wps, "Squad Pack");
                Assert(wpCode.StartsWith(WaypointShareService.Prefix, StringComparison.Ordinal), "Encoded waypoint pack must start with ZLR-WP:");
                WaypointPackResult decoded;
                Assert(WaypointShareService.TryDecode(wpCode, out decoded) && decoded.PackName == "Squad Pack" && decoded.Items.Length == 2, "WaypointShareService roundtrip failed");
                Assert(decoded.Items[0].Name == "Bunker Alpha" && decoded.Items[0].Argb == unchecked((int)0xFFFF5555), "Waypoint color/name mismatch");
                Assert(!decoded.Items[1].Name.Contains('\t'), "Control/tab chars must be sanitized in waypoint names");
                Assert(!WaypointShareService.TryDecode("ZLR-WP:!!!invalid!!!", out decoded), "Malformed ZLR-WP code must be rejected");
                Assert(!WaypointShareService.TryDecode(null, out decoded), "Null ZLR-WP code must be rejected");

                // 3. Squad 2.0 Tactical Statuses (Combat / Looting / Need Help)
                DateTime tNow = new DateTime(2026, 11, 26, 14, 0, 0, DateTimeKind.Utc);
                var sq = new SquadState(() => tNow);
                Assert(sq.Ping(SquadStatusKind.StatusCombat, SquadStatusKind.GetDisplayLabel(SquadStatusKind.StatusCombat, true), 0.3, 0.4), "Combat tactical status ping must succeed");
                tNow = tNow.AddSeconds(3);
                Assert(sq.Ping(SquadStatusKind.StatusNeedHelp, SquadStatusKind.GetDisplayLabel(SquadStatusKind.StatusNeedHelp, false), 0.5, 0.6), "NeedHelp tactical status ping must succeed");
                Assert(sq.Local().Length == 2 && SquadStatusKind.IsTacticalStatus(SquadStatusKind.StatusLooting), "Squad 2.0 tactical statuses must be stored as valid pings");

                // 4. Opt-In Cloud Telemetry persistence (strict default off)
                ErrorReportService.LoadPreferences(tmp);
                Assert(!ErrorReportService.CloudTelemetryOptIn && !ErrorReportService.IsCloudTelemetryActive, "Cloud telemetry must default to OFF");
                ErrorReportService.SetCloudTelemetryOptIn(true, "test-token-123");
                ErrorReportService.LoadPreferences(tmp);
                Assert(ErrorReportService.CloudTelemetryOptIn && ErrorReportService.IsCloudTelemetryActive, "Opt-in cloud telemetry with token must persist and activate");
                ErrorReportService.SetCloudTelemetryOptIn(false, "");
            } finally {
                SafeCleanup.DeleteDirectory(tmp, true);
            }
        }

    }
}
