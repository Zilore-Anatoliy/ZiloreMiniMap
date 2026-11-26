using System;
using System.IO;

namespace ZiloreMiniMap {
    internal static partial class CoreSelfTests {
        static void TestPhase5ASecuritySqliteRegexAndCache() {
            // 1. AdminAccessGuard constant-time byte check rejects null/empty/wrong passwords
            Assert(!AdminAccessGuard.TryUnlock(null), "AdminAccessGuard must reject null");
            Assert(!AdminAccessGuard.TryUnlock(""), "AdminAccessGuard must reject empty");
            Assert(!AdminAccessGuard.TryUnlock("definitely-wrong-password-123"), "AdminAccessGuard must reject invalid password");

            // 2. PoiAccessGuard duration contracts & forged key rejection
            string poiErr;
            Assert(!PoiAccessGuard.TryActivate(null, out poiErr) && !string.IsNullOrEmpty(poiErr), "PoiAccessGuard must reject null");
            Assert(!PoiAccessGuard.TryActivate("ZLR-POI-invalid-base64", out poiErr), "PoiAccessGuard must reject invalid key");
            Assert(PoiAccessGuard.IsAllowedDurationDays(30) && PoiAccessGuard.IsAllowedDurationDays(365) && !PoiAccessGuard.IsAllowedDurationDays(7), "PoiAccessGuard day duration policy mismatch");
            Assert(PoiAccessGuard.IsAllowedDurationHours(1) && PoiAccessGuard.IsAllowedDurationHours(24) && !PoiAccessGuard.IsAllowedDurationHours(5), "PoiAccessGuard hour duration policy mismatch");

            // 3. ErrorReportService source-generated System.Text.Json (Obfuscar-safe)
            string json = ErrorReportService.SerializeCloudPayload("ERROR", "Phase5ATest", "Synthetic msg", "line1");
            Assert(json.Contains("\"version\":", StringComparison.Ordinal) && json.Contains("\"source\":\"Phase5ATest\"", StringComparison.Ordinal) && json.Contains("\"message\":\"Synthetic msg\"", StringComparison.Ordinal), "Source-generated ErrorReportJsonContext output mismatch");

            // 4. LocalSqliteDatabase [LibraryImport] + PtrToStringUTF8 CRUD & transaction rollback
            string tmp = Path.Combine(Path.GetTempPath(), "zlr-p5a-sql-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try {
                string dbPath = Path.Combine(tmp, "probe.sqlite");
                using (var db = new LocalSqliteDatabase(dbPath, d => d.Execute("CREATE TABLE kv (id INTEGER PRIMARY KEY, val TEXT NOT NULL)"))) {
                    db.Transaction(() => {
                        db.Execute("INSERT INTO kv (id, val) VALUES (?, ?)", 1L, "Бункер D4 — Alpha");
                        db.Execute("INSERT INTO kv (id, val) VALUES (?, ?)", 2L, "Airfield");
                    });
                    Assert(db.ScalarLong("SELECT COUNT(*) FROM kv") == 2L, "SQLite insert count mismatch");
                    using (var st = db.Prepare("SELECT id, val FROM kv WHERE id = ?", 1L)) {
                        Assert(st.Read() && st.Long(0) == 1L && st.Text(1) == "Бункер D4 — Alpha", "SQLite PtrToStringUTF8 Cyrillic roundtrip failed");
                    }
                    bool rolledBack = false;
                    try {
                        db.Transaction(() => {
                            db.Execute("INSERT INTO kv (id, val) VALUES (?, ?)", 3L, "Temp");
                            throw new InvalidOperationException("force rollback");
                        });
                    } catch (InvalidOperationException) { rolledBack = true; }
                    Assert(rolledBack && db.ScalarLong("SELECT COUNT(*) FROM kv") == 2L, "SQLite transaction rollback failed");
                }
            } finally {
                SafeCleanup.DeleteDirectory(tmp, true);
            }

            // 5. SpawnModifiers [GeneratedRegex] location normalization
            string normTr = SpawnModifiers.NormalizeLocation("{X=100.5 Y=-200.25 Z=30|P=0 Y=90 R=0}");
            Assert(normTr == "\"{X=100.5 Y=-200.25 Z=30|P=0 Y=90 R=0}\"", "SpawnModifiers transform GeneratedRegex failed");
            string normVec = SpawnModifiers.NormalizeLocation("X=10 Y=20 Z=30");
            Assert(normVec == "\"X=10 Y=20 Z=30\"", "SpawnModifiers vector GeneratedRegex failed");

            // 6. MemberInk cache bounding check
            for (int i = 0; i < 300; i++) {
                var b = WpfMapSurface.MemberInk("peer-cache-probe-" + i);
                Assert(b != null && b.IsFrozen, "MemberInk must return frozen Brush");
            }
        }

        static void TestPhase5BAnalyzersNullableAndConnectTimeout() {
            // 1. Position [GeneratedRegex] + RelativeTeleport #nullable enable
            Assert(Position.Parse(null) == null && Position.Parse("invalid") == null, "Position.Parse null/invalid must return null");
            var pos = Position.Parse("{X=100.5 Y=-250.25 Z=50.0|P=0 Y=90 R=0}");
            Assert(pos != null && Math.Abs(pos.X - 100.5) < 0.001 && Math.Abs(pos.Yaw - 90.0) < 0.001, "Position [GeneratedRegex] parse failed");
            string tpCmd = RelativeTeleport.BuildCommand(pos, TeleportDirection.Up, 10.0);
            Assert(tpCmd == "#Teleport 100.5 -250.25 1050", "RelativeTeleport.BuildCommand mismatch");
            Assert(RelativeTeleport.BuildCommand(null, TeleportDirection.Up, 10.0) == null, "RelativeTeleport(null) must return null");

            // 2. InstallationAssets SHA256.HashData + Convert.ToHexStringLower
            using (var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("abc"))) {
                Assert(InstallationAssets.Digest(ms) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "InstallationAssets.Digest SHA-256 vector mismatch");
            }

            // 3. CopyInputDeliveryGate & CopyInputLease #nullable enable contracts
            var gate = new CopyInputDeliveryGate(() => true);
            Assert(CopyInputDeliveryGate.OwnsTag(gate.Tag) && gate.AllowsDown(gate.Tag), "CopyInputDeliveryGate must allow matching tag");
            gate.Cancel();
            Assert(!gate.AllowsDown(gate.Tag), "Cancelled CopyInputDeliveryGate must deny");
            int releasedKeys = 0;
            var lease = new CopyInputLease((k, up) => { if (up) releasedKeys++; return true; });
            Assert(lease.Send(0x43, false), "CopyInputLease key-down failed");
            lease.Cancel();
            Assert(lease.Cancelled && releasedKeys == 1, "CopyInputLease.Cancel must release held keys");

            // 4. ScumSurvivalTracker #nullable enable safety
            ScumSurvivalTracker.Load(null);
            ScumSurvivalTracker.LogConsumed(null);
            Assert(!string.IsNullOrEmpty(ScumSurvivalTracker.GetRecommendation(false)), "ScumSurvivalTracker recommendation empty");

            // 5. DirectPositions.RelayRoom HMACSHA256.HashData + Convert.ToHexStringLower determinism
            byte[] k32 = new byte[32];
            string room1 = DirectPositions.RelayRoom(k32);
            string room2 = DirectPositions.RelayRoom(k32);
            Assert(room1.Length == 64 && room1 == room2 && room1 == room1.ToLowerInvariant(), "RelayRoom HMAC-SHA256 lowercase hex mismatch");
        }

        static void TestPhase5CZeroOverlayAndWpfPreviewReadiness() {
            // 1. Verify legacy WinForms OverlayWindow / SettingsPanel are permanently eliminated in BOTH constructor modes
            string tmp = Path.Combine(Path.GetTempPath(), "zlr-p5c-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try {
                using (var engine = new MapWindow(tmp, true, true)) {
                    Assert(!engine.HasLegacyOverlayUi && !engine.HasLegacySettingsUi, "OverlayWindow and SettingsPanel must be permanently retired");
                    engine.WpfStopEngineTimer();
                    engine.WpfMoveHud(80, 90);
                    engine.WpfHudWidth = 280;
                    engine.WpfHudHeight = 290;
                    Assert(engine.HudBounds.Left == 80 && engine.HudBounds.Top == 90 && engine.HudBounds.Width == 280 && engine.HudBounds.Height == 290, "HudBounds contract failed after OverlayWindow removal");
                }
            } finally {
                SafeCleanup.DeleteDirectory(tmp, true);
            }
        }

        static void TestPhase6AFpsBugsAndHotPathPerf() {
            // 1. Null-safe LabelText / DynamicPeerLabelText (Bug #3 guard against OnRender NRE)
            var ftNull = WpfMapSurface.LabelText(null, 12.0, WpfFieldTheme.Cyan, 1.0, true);
            Assert(ftNull != null, "LabelText(null) must not throw NullReferenceException");
            var ftDyn = WpfMapSurface.DynamicPeerLabelText("ping:test6a", null, 12.0, WpfFieldTheme.Amber, 1.0);
            Assert(ftDyn != null, "DynamicPeerLabelText(null) must not throw NullReferenceException");

            // 2. PenCache logarithmic quantization prevents sawtooth flushes under continuous auto-zoom
            int startCount = WpfMapSurface.PenCacheCount;
            for (int i = 0; i < 400; i++) {
                double worldScaleThick = 2.0 / (400.0 + i * 0.5);
                var pen = WpfMapSurface.CachedPen(WpfFieldTheme.Cyan, worldScaleThick, true);
                Assert(pen != null && pen.IsFrozen, "CachedPen must return frozen Pen");
            }
            Assert(WpfMapSurface.PenCacheCount - startCount < 40, "PenCache quantization must collapse 400 continuous auto-zoom steps into <40 buckets");

            // 3. Cached DangerZone AABB revision invalidation
            var redPoly = new MapZone {
                Name = "PvP Sector",
                Argb = System.Drawing.Color.FromArgb(255, 220, 40, 40).ToArgb(),
                Points = new[] { new System.Drawing.PointF(0.4f, 0.4f), new System.Drawing.PointF(0.6f, 0.4f), new System.Drawing.PointF(0.6f, 0.6f), new System.Drawing.PointF(0.4f, 0.6f) }
            };
            var stIn = new WpfMapState { Position = new Position { X = 617718 - 0.5 * 1521618, Y = 618618 - 0.5 * 1523618 }, Saved = new[] { redPoly }, ZoneRevision = 101 };
            Assert(WpfMapSurface.IsNearDangerZone(stIn, 150.0), "Cached AABB IsNearDangerZone must detect inside red zone");
            stIn.Saved = Array.Empty<MapZone>(); stIn.ZoneRevision = 102;
            Assert(!WpfMapSurface.IsNearDangerZone(stIn, 150.0), "Cached AABB IsNearDangerZone must invalidate on ZoneRevision change");

            // 4. O(traders) cached trader lookup in ZiloreAiEngine
            var aiStore = new ScumMapStore();
            aiStore.CategoryById[1] = new ScumMapCategory { Id = 1, Section = "Outposts", Name = "Trader" };
            aiStore.Markers.Add(new ScumMapMarker { Id = 1, CategoryId = 1, X = 0.25f, Y = 0.25f, Title = "Z3 Outpost" });
            aiStore.Markers.Add(new ScumMapMarker { Id = 2, CategoryId = 1, X = 0.80f, Y = 0.80f, Title = "B4 Outpost" });
            var tr = ZiloreAiEngine.FindNearestCachedTrader(aiStore, new System.Drawing.PointF(0.26f, 0.26f), 15000.0);
            Assert(tr != null && tr.Name.Contains("Z3"), "FindNearestCachedTrader must find nearest cached trader in O(traders)");

            // 5. Subsampled HasSelectedTabHighlight works on full-res capture strip (no 2nd Bitmap/DrawImage needed)
            using (var wideStrip = new System.Drawing.Bitmap(384, 60, System.Drawing.Imaging.PixelFormat.Format32bppArgb)) {
                using (var g = System.Drawing.Graphics.FromImage(wideStrip)) {
                    g.Clear(System.Drawing.Color.FromArgb(255, 14, 18, 22));
                    using (var b = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 225, 225, 225))) g.FillRectangle(b, 20, 12, 140, 24);
                }
                Assert(Native.HasSelectedTabHighlight(wideStrip), "Subsampled HasSelectedTabHighlight must detect tab on native 384x60 strip");
            }
        }

        static void TestPhase6BWindowedRoutingVoiceAndPreview() {
            // 1. Verify windowed DistanceToPolylineMeters matches full scan & handles teleport fallback
            var longLine = new System.Drawing.PointF[300];
            for (int i = 0; i <= 200; i++) longLine[i] = new System.Drawing.PointF(0.20f + i * 0.001f, 0.50f);
            for (int i = 201; i < 300; i++) longLine[i] = new System.Drawing.PointF(0.40f, 0.50f + (i - 200) * 0.001f);
            var probeMid = new System.Drawing.PointF(0.3205f, 0.5002f);
            int fullSeg, winSeg, jumpSeg;
            double dFull = RoadRouter.DistanceToPolylineMeters(probeMid, longLine, out fullSeg);
            double dWin = RoadRouter.DistanceToPolylineMeters(probeMid, longLine, 118, out winSeg);
            double dJump = RoadRouter.DistanceToPolylineMeters(probeMid, longLine, 290, out jumpSeg);
            Assert(winSeg == fullSeg && Math.Abs(dWin - dFull) < 0.01, "Windowed DistanceToPolylineMeters must match full scan");
            Assert(jumpSeg == fullSeg && Math.Abs(dJump - dFull) < 0.01, "Windowed DistanceToPolylineMeters must fall back cleanly after teleport");

            // 2. Verify O(1) CumulativeMeters GetNextTurn detects 90-deg turn at junction 200
            var synthRoute = new RoadRoute {
                Success = true,
                PlayerPoint = longLine[0],
                TargetPoint = longLine[299],
                Polyline = longLine,
                JunctionIndices = new[] { 200 }
            };
            var turn1 = RoadRouter.GetNextTurn(new System.Drawing.PointF(0.390f, 0.50f), synthRoute);
            Assert(turn1.Valid && synthRoute.LastMatchedSegment >= 185 && turn1.DistanceMeters > 100.0 && turn1.DistanceMeters < 200.0 && Math.Abs(turn1.TurnAngleDeg) > 70.0, "GetNextTurn O(1) cumulative turn detection failed");
            var turn2 = RoadRouter.GetNextTurn(new System.Drawing.PointF(0.395f, 0.50f), synthRoute);
            Assert(turn2.Valid && turn2.DistanceMeters < turn1.DistanceMeters, "GetNextTurn distance must shrink smoothly as player approaches junction");

            // 3. Verify windowed VoiceNavigator + BinarySearch At() on 300-segment route
            var nav = new VoiceNavigator();
            var cue1 = nav.NextCue(synthRoute, new System.Drawing.PointF(0.380f, 0.50f));
            var cue2 = nav.NextCue(synthRoute, new System.Drawing.PointF(0.390f, 0.50f));
            Assert(cue1 != null && cue2 != null && cue2.Distance < cue1.Distance && cue2.Action == VoicePacks.Clips[6], "Windowed VoiceNavigator right-turn cue regression");
            Assert(VoicePacks.Discover(null).Length == 0 && !VoicePacks.IsClip(null) && !VoicePacks.IsSafePath(null), "VoicePacks #nullable enable null safety failed");
        }

        static void TestPhase6CFormlessWpfDispatcherEngine() {
            // 1. Verify MapWindow no longer inherits System.Windows.Forms.Form (Pure WPF Dispatcher engine)
            Assert(!typeof(System.Windows.Forms.Form).IsAssignableFrom(typeof(MapWindow)), "MapWindow must no longer inherit WinForms Form");
            Assert(!typeof(System.Windows.Forms.Control).IsAssignableFrom(typeof(MapWindow)), "MapWindow must no longer inherit WinForms Control");
            Assert(typeof(IDisposable).IsAssignableFrom(typeof(MapWindow)), "MapWindow must implement IDisposable");

            // 2. Verify lifecycle, WPF DispatcherTimer, BeginInvoke, and Disposed event work cleanly without a Form HWND
            string tmp = Path.Combine(Path.GetTempPath(), "zlr-p6c-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            bool disposedEventRaised = false;
            try {
                using (var engine = new MapWindow(tmp, true, true)) {
                    Assert(engine.IsHandleCreated && !engine.IsDisposed && !engine.InvokeRequired, "Formless WPF Dispatcher engine state mismatch");
                    engine.Disposed += (s, e) => disposedEventRaised = true;
                    engine.WpfStopEngineTimer();
                    engine.BeginInvoke(() => { });
                }
                Assert(disposedEventRaised, "MapWindow.Disposed event must fire on Dispose()");
            } finally {
                SafeCleanup.DeleteDirectory(tmp, true);
            }
        }

        static void TestPhase6DTelemetrySanitizationAndEndpointSafety() {
            // 1. Verify HTTPS endpoint validation accepts official report.zilore.app and rejects insecure/userinfo URLs
            Assert(ErrorReportService.IsSafeTelemetryEndpoint(ErrorReportService.CloudReportEndpoint), "Official telemetry endpoint must pass HTTPS validation");
            Assert(!ErrorReportService.IsSafeTelemetryEndpoint("http://report.zilore.app"), "Plain HTTP telemetry endpoint must be rejected");
            Assert(!ErrorReportService.IsSafeTelemetryEndpoint("https://user:pass@report.zilore.app"), "UserInfo telemetry endpoint must be rejected");
            Assert(!ErrorReportService.IsSafeTelemetryEndpoint(null), "Null telemetry endpoint must be rejected");

            // 2. Verify client-side PII & secret redaction via [GeneratedRegex]
            string dirty = @"Error at C:\Users\TestPlayer\AppData\Local\ZiloreMiniMap with key ZLR-POI-ABCDEFGHIJKLMNOP and ReportToken=super_secret_123";
            string clean = ErrorReportService.SanitizeTelemetryText(dirty);
            Assert(!clean.Contains("TestPlayer", StringComparison.Ordinal) && clean.Contains(@"C:\Users\<redacted>\", StringComparison.Ordinal), "Windows username must be redacted");
            Assert(!clean.Contains("ABCDEFGHIJKLMNOP", StringComparison.Ordinal) && clean.Contains("ZLR-POI-<redacted>", StringComparison.Ordinal), "POI key must be redacted");
            Assert(!clean.Contains("super_secret_123", StringComparison.Ordinal) && clean.Contains("ReportToken=<redacted>", StringComparison.Ordinal), "ReportToken must be redacted");

            // 3. Verify serialized payload respects Cloudflare Worker 64KB size cap
            string hugeLogs = new string('A', 40000);
            string payloadJson = ErrorReportService.SerializeCloudPayload("ERROR", "Phase6D", dirty, hugeLogs);
            Assert(payloadJson.Length < 32768 && !payloadJson.Contains("TestPlayer", StringComparison.Ordinal), "Cloud telemetry JSON must be capped and sanitized");
        }

        static void TestSystemTypographySegoeVariableClearType() {
            // 1. Verify WPF font family targets Windows 11 Segoe UI Variable with Windows 10 Segoe UI fallback
            string wpfSource = SystemTypography.WpfUiFontFamily.Source;
            Assert(wpfSource.Contains("Segoe UI Variable", StringComparison.Ordinal) && wpfSource.Contains("Segoe UI", StringComparison.Ordinal), "WPF font family must target Segoe UI Variable with Segoe UI fallback");
            Assert(!wpfSource.Contains("Barlow", StringComparison.OrdinalIgnoreCase), "Legacy Barlow font must be completely removed");

            // 2. Verify GDI+ font family resolution & caching (zero handle leak)
            string gdiFam = SystemTypography.GdiUiFontFamilyName;
            Assert(gdiFam.StartsWith("Segoe UI", StringComparison.Ordinal), "GDI+ system font must resolve to Segoe UI Variable Text or Segoe UI");
            var f1 = SystemTypography.GetGdiFont(10f, System.Drawing.FontStyle.Bold);
            var f2 = SystemTypography.GetGdiFont(10f, System.Drawing.FontStyle.Bold);
            Assert(object.ReferenceEquals(f1, f2), "SystemTypography.GetGdiFont must cache fonts");
            Assert(OverlayTheme.UiFont(9.5f).FontFamily.Name.StartsWith("Segoe UI", StringComparison.Ordinal), "OverlayTheme.UiFont must delegate to SystemTypography");

            // 3. Verify ClearType hints are enforced on WPF elements (including AllowsTransparency overlays)
            var el = new System.Windows.Controls.Border();
            SystemTypography.ApplyClearType(el);
            Assert(System.Windows.Media.TextOptions.GetTextRenderingMode(el) == System.Windows.Media.TextRenderingMode.ClearType, "TextRenderingMode must be ClearType");
            Assert(System.Windows.Media.TextOptions.GetTextFormattingMode(el) == System.Windows.Media.TextFormattingMode.Display, "TextFormattingMode must be Display");
            Assert(System.Windows.Media.TextOptions.GetTextHintingMode(el) == System.Windows.Media.TextHintingMode.Fixed, "TextHintingMode must be Fixed");
            Assert(System.Windows.Media.RenderOptions.GetClearTypeHint(el) == System.Windows.Media.ClearTypeHint.Enabled, "ClearTypeHint must be Enabled for transparent overlays");

            // 4. Verify map label rendering uses Segoe UI Variable without bundled Barlow TTFs
            var ft = WpfMapSurface.LabelText("Навигация · Settings · D4", 12.0, WpfFieldTheme.Ink, 1.0, false);
            Assert(ft != null && ft.Width > 0, "WpfMapSurface.LabelText must render with Segoe UI Variable");
        }

        static void TestPhase7AP0FpsAutoZoomPrewarmAndNative() {
            // 1. P0 #1: Smooth Compact auto-zoom must NOT rebuild PoiClusters on every fractional zoom step
            var store = new ScumMapStore { MasterEnabled = true };
            store.CategoryById[1] = new ScumMapCategory { Id = 1, Section = "Outposts", Name = "Trader" };
            for (int i = 0; i < 120; i++) {
                store.Markers.Add(new ScumMapMarker { Id = i + 1, CategoryId = 1, X = 0.1f + (i % 12) * 0.06f, Y = 0.1f + (i / 12) * 0.06f, Title = "POI-" + i });
            }
            double prevSpeed = WpfMapSurface.CurrentGroundSpeedMps;
            bool prevAir = WpfMapSurface.AviationModeEnabled;
            WpfMapSurface.CurrentGroundSpeedMps = 0.0; WpfMapSurface.AviationModeEnabled = false;
            var st = new WpfMapState { Store = store, Zoom = 8.0f, Pan = new System.Drawing.PointF(0.4f, 0.4f), PoiClustering = true, Labels = true, LabelSize = 12, Saved = Array.Empty<MapZone>() };
            int beforeBuilds = WpfMapSurface.PoiClusterRebuilds;
            var firstLayout = WpfMapSurface.PoiClusters(st, 240, 240, true);
            Assert(firstLayout.Length > 0, "Initial compact POI clustering must return clusters");
            for (int step = 1; step <= 40; step++) {
                st.Zoom = 8.0f + step * 0.004f; // smooth auto-zoom inside single quantized side bucket
                var nextLayout = WpfMapSurface.PoiClusters(st, 240, 240, true);
                Assert(object.ReferenceEquals(firstLayout, nextLayout), "Compact PoiClusters must reuse cached array across smooth auto-zoom ticks");
            }
            Assert(WpfMapSurface.PoiClusterRebuilds - beforeBuilds == 1, "40 smooth auto-zoom frames must trigger exactly 1 PoiCluster rebuild");

            // 2. P0 #2: Concurrent PrewarmPoiCache + UI PoiClusters must be 100% thread-safe under lock(cache)
            var tasks = new System.Threading.Tasks.Task[4];
            for (int ti = 0; ti < tasks.Length; ti++) {
                int tid = ti;
                tasks[ti] = System.Threading.Tasks.Task.Run(() => {
                    for (int k = 0; k < 20; k++) {
                        var sLocal = new WpfMapState { Store = store, Zoom = 4.0f + (tid + k) * 0.1f, PoiClustering = true };
                        WpfMapSurface.PrewarmPoiCache(sLocal, 1280, 720);
                    }
                });
            }
            System.Threading.Tasks.Task.WaitAll(tasks);

            // 3. P0 #3: HiDPI SelectLabels accepts explicit pixelsPerDip without crashing or mismeasuring
            st.Zoom = 2.0f; st.Pan = new System.Drawing.PointF(0.4f, 0.4f);
            var hiDpiLabels = WpfMapSurface.SelectLabels(st, 800, 600, false, 1.5);
            WpfMapSurface.CurrentGroundSpeedMps = prevSpeed; WpfMapSurface.AviationModeEnabled = prevAir;
            Assert(hiDpiLabels != null && hiDpiLabels.Length > 0, "HiDPI SelectLabels(pixelsPerDip=1.5) must produce labels");

            // 4. Zero-alloc Native.IsScumExecutablePath & unified GameFocused/IsGameWindow
            Assert(Native.IsScumExecutablePath(@"C:\Steam\steamapps\common\SCUM\SCUM\Binaries\Win64\SCUM-Win64-Shipping.exe".AsSpan()), "SCUM-Win64-Shipping.exe must match");
            Assert(Native.IsScumExecutablePath(@"D:\Games\SCUM.exe".AsSpan()), "SCUM.exe must match");
            Assert(!Native.IsScumExecutablePath(@"C:\Windows\System32\notepad.exe".AsSpan()), "notepad.exe must not match SCUM");
            Assert(!Native.IsScumExecutablePath(ReadOnlySpan<char>.Empty), "Empty path must not match SCUM");
            Assert(!Native.IsGameWindow(IntPtr.Zero), "IsGameWindow(IntPtr.Zero) must return false");
        }

        static void TestPhase7BNetworkStabilitySyslib0014AndGdiPool() {
            // 1. Separated HTTP timeouts (12s for Steam/manifests vs 10m for large binary downloads)
            Assert(UpdateService.EffectiveFetchTimeout(65536, true) == TimeSpan.FromSeconds(12), "Steam fetches must use 12s timeout");
            Assert(UpdateService.EffectiveFetchTimeout(32768, false) == TimeSpan.FromSeconds(12), "Manifest fetches must use 12s timeout");
            Assert(UpdateService.EffectiveFetchTimeout(50 * 1024 * 1024, false) == TimeSpan.FromMinutes(10), "Binary fetches must retain 10m timeout");

            // 2. SteamProfiles TTL cache must NOT cache transient null failures and must expire after 30m
            SteamProfiles.ClearCache();
            const string fakeSteamId = "76561198012345678";
            DateTime now = new DateTime(2026, 11, 26, 18, 0, 0, DateTimeKind.Utc);
            int calls = 0;
            string failRes = SteamProfiles.ResolveNameAsync(fakeSteamId, id => { calls++; return System.Threading.Tasks.Task.FromResult<string>(null); }, () => now).GetAwaiter().GetResult();
            Assert(failRes == null && calls == 1 && SteamProfiles.CachedCount == 0, "Null Steam lookup must not be cached");
            string ok1 = SteamProfiles.ResolveNameAsync(fakeSteamId, id => { calls++; return System.Threading.Tasks.Task.FromResult("SurvivorOne"); }, () => now).GetAwaiter().GetResult();
            string ok2 = SteamProfiles.ResolveNameAsync(fakeSteamId, id => { calls++; return System.Threading.Tasks.Task.FromResult("Changed"); }, () => now.AddMinutes(5)).GetAwaiter().GetResult();
            Assert(ok1 == "SurvivorOne" && ok2 == "SurvivorOne" && calls == 2 && SteamProfiles.CachedCount == 1, "Valid Steam profile name must be served from TTL cache");
            string ok3 = SteamProfiles.ResolveNameAsync(fakeSteamId, id => { calls++; return System.Threading.Tasks.Task.FromResult("SurvivorRefreshed"); }, () => now.AddMinutes(31)).GetAwaiter().GetResult();
            Assert(ok3 == "SurvivorRefreshed" && calls == 3, "Expired Steam profile cache entry must refresh after 30m TTL");
            SteamProfiles.ClearCache();

            // 3. DirectPositions.ConnectWithTimeout aborts immediately on external CancellationToken
            using (var cts = new System.Threading.CancellationTokenSource()) {
                cts.Cancel();
                using (var tcp = new System.Net.Sockets.TcpClient()) {
                    bool canceled = false;
                    try {
                        DirectPositions.ConnectWithTimeout(tcp, "127.0.0.1", 1, 5000, cts.Token);
                    } catch (OperationCanceledException) {
                        canceled = true;
                    }
                    Assert(canceled, "ConnectWithTimeout must throw OperationCanceledException immediately on external cancel");
                }
            }

            // 4. GdiScreenCapturePool reuses Bitmap for identical geometry and reallocates cleanly on resize
            var b1 = GdiScreenCapturePool.InspectPooledSurfaceForTest("test-slot", 64, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb, (b, g) => b);
            var b2 = GdiScreenCapturePool.InspectPooledSurfaceForTest("test-slot", 64, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb, (b, g) => b);
            Assert(object.ReferenceEquals(b1, b2), "GdiScreenCapturePool must reuse identical slot Bitmap without reallocating");
            var b3 = GdiScreenCapturePool.InspectPooledSurfaceForTest("test-slot", 80, 40, System.Drawing.Imaging.PixelFormat.Format32bppArgb, (b, g) => b);
            Assert(!object.ReferenceEquals(b2, b3) && b3.Width == 80 && b3.Height == 40, "GdiScreenCapturePool must resize slot cleanly");
            GdiScreenCapturePool.DisposeSlot("test-slot");

            // 5. RouterAddressDiscovery local gateway enforcement remains intact after HttpClient migration
            var gw = System.Net.IPAddress.Parse("192.168.1.1");
            Assert(RouterAddressDiscovery.LocalEndpoint(new Uri("http://192.168.1.1:1900/igd.xml"), gw), "Gateway HTTP endpoint must be allowed");
            Assert(!RouterAddressDiscovery.LocalEndpoint(new Uri("http://192.168.1.2:1900/igd.xml"), gw), "Non-gateway host must be rejected");
            Assert(!RouterAddressDiscovery.LocalEndpoint(new Uri("https://192.168.1.1:1900/igd.xml"), gw), "Non-HTTP UPnP scheme must be rejected");
        }
        static void TestPhase7CGeneratedRegexLibraryImportAndNullable() {
            // 1. Verify [GeneratedRegex] in PlayerList parses valid SCUM player list and rejects malformed lines
            string samplePlayers = "[12:34:56]\n1. AlphaSurvivor\nPlatform ID: 76561198012345678\nPlatform Name: AlphaSteam\nLocation: X=1200.5 Y=-3400.25 Z=150.0\nFame: 420\nAccount balance: 5000\nGold balance: 12\n";
            var parsedPlayers = PlayerList.Parse(samplePlayers, DateTime.UtcNow);
            Assert(parsedPlayers.Count == 1 && parsedPlayers[0].Id == "76561198012345678" && Math.Abs(parsedPlayers[0].Location.X - 1200.5) < 0.01, "GeneratedRegex PlayerList.Parse failed");

            // 2. Verify [GeneratedRegex] in AdminProfileParser parses bulk flag pages & validates #ListFlags command
            string sampleFlags = "[12:35:00] Page 1/2\n[12:35:00] Flag ID: 98765 | Owner: [76561198012345678] AlphaSurvivor (42) | Location: X=100.0 Y=200.0 Z=30.0\n";
            AdminProfileParser.ValidateBulkResponse(sampleFlags, "#ListFlags 1 true");

            // 3. Verify [GeneratedRegex] in ZiloreAiEngine parses sector, count, distance and multi-stage plan
            var aiStore = new ScumMapStore { MasterEnabled = true };
            aiStore.CategoryById[1] = new ScumMapCategory { Id = 1, Section = "Outposts", Name = "Trader" };
            aiStore.CategoryById[2] = new ScumMapCategory { Id = 2, Section = "Services", Name = "Gas Station" };
            aiStore.Markers.Add(new ScumMapMarker { Id = 1, CategoryId = 1, X = 0.52f, Y = 0.52f, Title = "Central Outpost" });
            aiStore.Markers.Add(new ScumMapMarker { Id = 2, CategoryId = 2, X = 0.51f, Y = 0.51f, Title = "North Gas" });
            var aiState = new WpfMapState { Store = aiStore, Pan = new System.Drawing.PointF(0.50f, 0.50f), Saved = Array.Empty<MapZone>() };
            var aiPlan = ZiloreAiEngine.PlanRoute("заправка -> трейдер 5 км", aiState, true);
            Assert(aiPlan != null && aiPlan.Success && aiPlan.Stops != null && aiPlan.Stops.Length == 2, "GeneratedRegex ZiloreAiEngine.PlanRoute failed");

            // 4. Verify migrated [LibraryImport] P/Invokes in Native (WindowFromPoint, GetCursorInfo, TryGameClientBounds)
            IntPtr wfp = Native.WindowFromPoint(new System.Drawing.Point(0, 0));
            _ = wfp; // Smoke test: must invoke cleanly without MarshalDirectiveException
            bool curVis = Native.IsCursorVisible();
            _ = curVis;
            System.Drawing.Rectangle dummyBounds;
            Assert(!Native.TryGameClientBounds(IntPtr.Zero, out dummyBounds), "TryGameClientBounds(IntPtr.Zero) must return false");

            // 5. Verify CA2211-clean OverlayTheme properties & #nullable enable IntegrityGuard / PeerFirewall
            OverlayTheme.SyncWithWpfTheme("Light");
            Assert(OverlayTheme.Background.R > 200, "OverlayTheme.Background property must reflect Light theme");
            OverlayTheme.SyncWithWpfTheme("Dark");
            Assert(OverlayTheme.Background.R < 30, "OverlayTheme.Background property must reflect Dark theme");
            Assert(!IntegrityGuard.IsFileSignatureIntact(null) && !IntegrityGuard.IsFileSignatureIntact(""), "IntegrityGuard null guard failed");
            Assert(RouterAddressDiscovery.Location(null, null) == null && RouterAddressDiscovery.Find(null, g => null, g => null) == null, "RouterAddressDiscovery null guards failed");
        }

        static void TestPhase7DPureWpfAdminProfileZeroAsyncVoidAndCa2208() {
            // 1. Verify CA2208 ArgumentException/ArgumentOutOfRangeException report exact ParamName
            string portParam = null;
            try { PeerRouter.Request(80, false); } catch (ArgumentOutOfRangeException ex) { portParam = ex.ParamName; }
            Assert(portParam == "port", "PeerRouter.Request must pass ParamName=port (CA2208)");
            string groupParam = null;
            try { RelayMembership.ValidateGroupName(""); } catch (ArgumentException ex) { groupParam = ex.ParamName; }
            Assert(groupParam == "value", "RelayMembership.ValidateGroupName must pass ParamName=value (CA2208)");
            string codeParam = null;
            try { RelayMembership.Parse("invalid"); } catch (ArgumentException ex) { codeParam = ex.ParamName; }
            Assert(codeParam == "code", "RelayMembership.Parse must pass ParamName=code (CA2208)");

            // 2. Verify zero standalone async void on Native.ReturnMapKey (now synchronous fire-and-forget Task wrapper)
            var rmk = typeof(Native).GetMethod("ReturnMapKey", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert(rmk != null && rmk.GetCustomAttributes(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute), false).Length == 0, "Native.ReturnMapKey must not be async void");
            Native.ReturnMapKey(IntPtr.Zero, 0, 0);

            // 3. Verify Pure WPF Admin Profile bridge on formless MapWindow engine
            string tmp = Path.Combine(Path.GetTempPath(), "zlr-p7d-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try {
                using (var engine = new MapWindow(tmp, true, true)) {
                    var fallback = engine.WpfFindKnownAdminPlayer("76561198012345678", "TestAdmin", "SteamAdmin");
                    Assert(fallback != null && fallback.Id == "76561198012345678" && fallback.Name == "TestAdmin", "WpfFindKnownAdminPlayer fallback failed");
                    engine.WpfSetAdminProfileMarkers(new[] { new AdminAsset { Kind = "Flag", Id = "1", Name = "Base", Location = new Position { X = 10, Y = 20, Z = 5 } } });
                    engine.WpfClearAdminProfileMarkers();
                    engine.ShowEngineAlert("diagnostic-suppressed", "Test");
                }
            } finally {
                SafeCleanup.DeleteDirectory(tmp, true);
            }
        }

        static void TestPhase8AAuditFixesPerfIoSecNavSquadUi() {
            // 1. P0 Security: Local diagnostic ZIP must redact Windows username, POI keys, and ReportToken
            string dirtySummary = @"Crash at C:\Users\SecretPlayer99\AppData\Local\ZiloreMiniMap key=ZLR-POI-1234567890ABCDEF ReportToken=tok_ultra_secret";
            string zipPath = ErrorReportService.CreateReportArchive("ERROR", "Phase8ATest", dirtySummary);
            try {
                Assert(File.Exists(zipPath), "CreateReportArchive must produce ZIP");
                using (var z = System.IO.Compression.ZipFile.OpenRead(zipPath)) {
                    var entry = z.GetEntry("report.txt");
                    Assert(entry != null, "report.txt missing in diagnostic ZIP");
                    using (var sr = new StreamReader(entry.Open())) {
                        string content = sr.ReadToEnd();
                        Assert(!content.Contains("SecretPlayer99", StringComparison.Ordinal), "Windows username leaked into local report ZIP");
                        Assert(!content.Contains("1234567890ABCDEF", StringComparison.Ordinal), "POI key leaked into local report ZIP");
                        Assert(!content.Contains("tok_ultra_secret", StringComparison.Ordinal), "ReportToken leaked into local report ZIP");
                        Assert(content.Contains("<redacted>", StringComparison.Ordinal), "Sanitized placeholder expected in local report ZIP");
                    }
                }
            } finally {
                SafeCleanup.DeleteFile(zipPath);
            }

            // 2. P1 Navigation: O(1) RoadRouter.RemainingDistanceMeters shrinks smoothly along route without A* recompute
            var line = new System.Drawing.PointF[101];
            for (int i = 0; i <= 100; i++) line[i] = new System.Drawing.PointF(0.20f + i * 0.001f, 0.50f);
            var route = new RoadRoute { Success = true, Polyline = line, TotalDistanceMeters = 1521.6, ExitDistanceMeters = 10.0 };
            double dStart = RoadRouter.RemainingDistanceMeters(new System.Drawing.PointF(0.20f, 0.50f), route);
            double dMid = RoadRouter.RemainingDistanceMeters(new System.Drawing.PointF(0.25f, 0.50f), route);
            double dEnd = RoadRouter.RemainingDistanceMeters(new System.Drawing.PointF(0.295f, 0.50f), route);
            Assert(dStart > 1500.0 && dMid > 740.0 && dMid < 790.0 && dEnd > 75.0 && dEnd < 95.0, "RemainingDistanceMeters must count down smoothly along polyline");
            Assert(dStart > dMid && dMid > dEnd, "RemainingDistanceMeters must strictly decrease toward target");

            // 3. P1 Squad: RelayGroupStore.Dispose() must be idempotent and safe when called twice
            string tmpRg = Path.Combine(Path.GetTempPath(), "zlr-p8a-rg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpRg);
            try {
                var store = new RelayGroupStore(tmpRg);
                store.Dispose();
                store.Dispose();
            } finally {
                SafeCleanup.DeleteDirectory(tmpRg, true);
            }
        }

        static void TestPhase8BHotPathCacheModernNet10AndNullable() {
            // 1. Verify #nullable enable SquadState & SavedRouteLibrary null safety and roundtrip
            Assert(!SquadState.IsPing(null) && !SquadState.IsStop(null) && !SquadState.IsShared(null), "SquadState static predicates must safely reject null");
            Assert(SquadState.Routes(null).Length == 0 && SquadState.StopIndex(null) == int.MaxValue, "SquadState.Routes/StopIndex null guards failed");
            var st = new SquadState();
            Assert(!st.RemoveStop(null) && !st.MoveStop(null, 1) && !st.RenameStop(null, "X") && !st.UpdateStopPosition(null, 0.5, 0.5), "SquadState mutators must safely reject null id");
            Assert(!SavedRouteLibrary.TryDecodeShareCode(null, out var badTpl) && badTpl == null, "SavedRouteLibrary must reject null share code");
            var tpl = new SavedRouteTemplate { Name = "Bunker Run", Shared = true, Stops = new[] { new SharedMapMarker { Name = "S1", X = 0.25, Y = 0.35 }, new SharedMapMarker { Name = "S2", X = 0.65, Y = 0.75 } } };
            string encoded = SavedRouteLibrary.EncodeShareCode(tpl);
            Assert(SavedRouteLibrary.TryDecodeShareCode(encoded, out var decoded) && decoded != null && decoded.Name == "Bunker Run" && decoded.Stops.Length == 2, "SavedRouteLibrary roundtrip failed");

            // 2. Verify #nullable enable SpawnCart ArgumentNullException.ThrowIfNull contracts
            string nullParam = null;
            try { _ = new SpawnCart(null); } catch (ArgumentNullException ex) { nullParam = ex.ParamName; }
            Assert(nullParam == "catalog", "SpawnCart(null) must throw ArgumentNullException for catalog");

            // 3. Verify WpfSnapshot route summary cache returns stable reference across identical frames
            string tmp = Path.Combine(Path.GetTempPath(), "zlr-p8b-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try {
                using (var engine = new MapWindow(tmp, true, true)) {
                    engine.WpfPreviewPosition("{X=-336602.375 Y=-270302.625 Z=18853.264|P=0 Y=90 R=0}");
                    engine.WpfTarget(new MapZone { Name = "TargetAlpha", Points = new[] { new System.Drawing.PointF(0.6f, 0.6f) } });
                    var s1 = engine.WpfSnapshot(false);
                    var s2 = engine.WpfSnapshot(false);
                    Assert(!string.IsNullOrEmpty(s1.RouteSummary) && object.ReferenceEquals(s1.RouteSummary, s2.RouteSummary), "WpfSnapshot must reuse cached RouteSummary string on unchanged frames");
                }
            } finally {
                SafeCleanup.DeleteDirectory(tmp, true);
            }
        }

        static void TestPhase8CScumMapFrozenDictDeathPoolAndNullable() {
            // 1. Verify zero-alloc SectorOf lookup table & FrozenDictionary Singularize + cached GetMarkerDisplayName
            Assert(ScumMapStore.SectorOf(0.05f, 0.05f) == "D4" && ScumMapStore.SectorOf(0.95f, 0.95f) == "Z0", "ScumMapStore.SectorOf static table mismatch");
            Assert(ScumMapStore.Singularize("  POLICE STATIONS ") == "Police Station", "FrozenDictionary Singularize case-insensitive match failed");
            Assert(ScumMapStore.Singularize("Custom Watchtower") == "Custom Watchtower" && ScumMapStore.Singularize(" ") == "POI", "Singularize fallback failed");
            var mapStore = new ScumMapStore();
            mapStore.Markers.Add(new ScumMapMarker { Id = 1, CategoryId = 27, X = 0.40f, Y = 0.40f, Title = "Samobor" });
            var gasMarker = new ScumMapMarker { Id = 2, CategoryId = 10, X = 0.405f, Y = 0.405f, Title = "Gas Stations" };
            mapStore.Markers.Add(gasMarker);
            mapStore.IndexSettlements();
            string n1 = mapStore.GetMarkerDisplayName(gasMarker);
            string n2 = mapStore.GetMarkerDisplayName(gasMarker);
            Assert(n1 == "Samobor Gas Station" && object.ReferenceEquals(n1, n2), "GetMarkerDisplayName must resolve settlement and cache result");

            // 2. Verify pooled scratch surface for DeathBannerDetector eliminates per-probe Bitmap allocation
            System.Drawing.Bitmap refA = null, refB = null;
            GdiScreenCapturePool.WithScratchSurface("test-scratch-8c", 120, 80, System.Drawing.Imaging.PixelFormat.Format32bppArgb, (b, g) => { refA = b; });
            GdiScreenCapturePool.WithScratchSurface("test-scratch-8c", 120, 80, System.Drawing.Imaging.PixelFormat.Format32bppArgb, (b, g) => { refB = b; });
            Assert(refA != null && object.ReferenceEquals(refA, refB), "WithScratchSurface must reuse identical slot Bitmap");
            GdiScreenCapturePool.DisposeSlot("test-scratch-8c");
            Assert(!DeathBannerDetector.Matches(null, out string dbDiag) && dbDiag == "invalid-image", "DeathBannerDetector null guard failed");

            // 3. Verify #nullable enable SpawnModifiers null/normalization contracts
            Assert(SpawnModifiers.Normalize(null) == "" && SpawnModifiers.Parse(null).Count == 0, "SpawnModifiers null handling failed");
            Assert(SpawnModifiers.Normalize("uses 5 health 85 keycardsector b2") == "Health 85 Uses 5 KeyCardSector B2", "SpawnModifiers canonical ordering failed");
            Assert(SpawnModifiers.NormalizeLocation("76561198012345678") == "76561198012345678", "SpawnModifiers SteamID location failed");
        }

        static void TestPhase8DInputHooksZoneStorageAndSidebar() {
            // 1. Verify #nullable enable InputHooks contracts (GameFocusReturn, ChatState, PhysicalKeyCapture, MapMouseShield)
            Assert(GameFocusReturn.ShouldRestore(true, 100, 100, 200, 200) && !GameFocusReturn.ShouldRestore(true, 100, 100, 300, 200), "GameFocusReturn.ShouldRestore logic failed");
            var chat = new ChatState();
            var mon = new ChatKeyMonitor();
            Assert(mon.Observe(0x54, true, true, chat, 0x54) && chat.Paused, "ChatKeyMonitor must pause on T key");
            Assert(!mon.ObserveSample(0x1B, true, true, chat, 0x54) && chat.Paused, "ChatKeyMonitor.ObserveSample must ignore async Escape");
            Assert(mon.Observe(0x1B, true, true, chat, 0x54) && !chat.Paused, "ChatKeyMonitor physical Escape must resume chat");
            string conflictParam = null;
            try { PhysicalKeyCapture.HasConflicts(null, null); } catch (ArgumentException ex) { conflictParam = ex.ParamName; }
            Assert(conflictParam == "keys", "PhysicalKeyCapture.HasConflicts must report ParamName=keys");
            var shield = new MapMouseShield();
            Assert(shield.Suppress(0x201, 0, true, false, true) && shield.Suppress(0x202, 0, false, true, false), "MapMouseShield must pair blocked mouse-down with mouse-up");
            Assert(GameKeys.MouseActionBlocksCopy(0x201, 0) && !GameKeys.MouseActionBlocksCopy(0x20A, 0) && GameKeys.MouseActionBlocksCopy(0x20A, 0x11), "MouseActionBlocksCopy wheel rule failed");

            // 2. Verify FrozenSet FactionNames, cached MapZone.Centroid, zero-alloc ZoneStore.Classify & Save/Load roundtrip
            Assert(MapZone.IsFactionName("D4 City - Police Station") && !MapZone.IsFactionName("Unknown Shed"), "FrozenSet MapZone.IsFactionName failed");
            var zBunker = new MapZone { Name = "B3 Secret Bunker", Argb = -1, Points = new[] { new System.Drawing.PointF(0.2f, 0.2f), new System.Drawing.PointF(0.4f, 0.2f), new System.Drawing.PointF(0.4f, 0.4f), new System.Drawing.PointF(0.2f, 0.4f) } };
            var c1 = zBunker.Centroid; var c2 = zBunker.Centroid;
            Assert(Math.Abs(c1.X - 0.3f) < 0.001f && Math.Abs(c1.Y - 0.3f) < 0.001f && c1 == c2, "Cached MapZone.Centroid calculation mismatch");
            Assert(ZoneStore.Classify(new MapZone { Name = "Samobor", Argb = 0 }) == ZoneCategory.City, "ZoneStore.Classify City failed");
            Assert(ZoneStore.Classify(new MapZone { Name = "Alpha Bunker", Argb = 0 }) == ZoneCategory.Bunker, "ZoneStore.Classify Bunker failed");
            Assert(ZoneStore.Classify(null) == ZoneCategory.Unknown, "ZoneStore.Classify null guard failed");
            string tmpZ = Path.Combine(Path.GetTempPath(), "zlr-p8d-zones-" + Guid.NewGuid().ToString("N") + ".tsv");
            try {
                var list = new System.Collections.Generic.List<MapZone> {
                    new MapZone { Name = "North Outpost", Argb = -9843406, Category = ZoneCategory.Trader, Layer = "Traders", FillArgb = 123456, Points = new[] { new System.Drawing.PointF(0.1f, 0.2f), new System.Drawing.PointF(0.2f, 0.2f), new System.Drawing.PointF(0.15f, 0.3f) } }
                };
                ZoneStore.Save(tmpZ, list);
                var reloaded = ZoneStore.Load(tmpZ);
                Assert(reloaded.Count == 1 && reloaded[0].Name == "North Outpost" && reloaded[0].Category == ZoneCategory.Trader && reloaded[0].FillArgb == 123456 && reloaded[0].Points.Length == 3, "ZoneStore reusable StringBuilder Save/Load roundtrip failed");
            } finally {
                SafeCleanup.DeleteFile(tmpZ);
            }

            // 3. Verify single-pass ScumMapStore.SetSectionEnabled updates all section categories atomically
            var sm = new ScumMapStore();
            var catA = new ScumMapCategory { Id = 101, Section = "Loot", Name = "Armory", DefaultEnabled = true };
            var catB = new ScumMapCategory { Id = 102, Section = "Loot", Name = "Bunker", DefaultEnabled = true };
            sm.Categories.Add(catA); sm.Categories.Add(catB);
            sm.CategoryById[101] = catA; sm.CategoryById[102] = catB;
            sm.CategoriesBySection["Loot"] = new System.Collections.Generic.List<ScumMapCategory> { catA, catB };
            string hBefore = sm.GetFilterHashKey();
            sm.SetSectionEnabled("Loot", false);
            Assert(!sm.IsCategoryEnabled(101) && !sm.IsCategoryEnabled(102) && sm.GetFilterHashKey() != hBefore, "SetSectionEnabled(false) must disable whole section and invalidate hash");
            sm.SetSectionEnabled("Loot", true);
            Assert(sm.IsCategoryEnabled(101) && sm.IsCategoryEnabled(102), "SetSectionEnabled(true) must re-enable whole section");
        }

        static void TestPhase9BenchSuitePoiIndexAndAdminPlayers() {
            // 1. Verify deterministic headless runtime micro-benchmark (-Bench) completes cleanly
            var bench = CoreBenchmarks.RunAll(20);
            Assert(bench != null && bench.Iterations == 20 && bench.AvgFrameMs >= 0.0 && bench.P99FrameMs + 1e-6 >= bench.AvgFrameMs && bench.Checksum > 0, "CoreBenchmarks.RunAll returned invalid metrics");
            string bLine = bench.FormatLine();
            Assert(bLine.StartsWith("BENCH_RESULT avg_ms=", StringComparison.Ordinal) && bLine.Contains("p99_ms=", StringComparison.Ordinal), "BenchmarkSummary.FormatLine contract mismatch");

            // 2. Verify null-safe PlayerDirectoryQuery.Apply & zero-LINQ PlayerList.ValidId
            Assert(PlayerList.ValidId("76561198012345678") && !PlayerList.ValidId("7656119801234567a") && !PlayerList.ValidId("123") && !PlayerList.ValidId(null), "PlayerList.ValidId failed");
            var p1 = new AdminPlayer { Id = "76561198012345678", Name = "Bravo", PlatformName = "SteamBravo" };
            var p2 = new AdminPlayer { Id = "76561198087654321", Name = "Alpha", PlatformName = "SteamAlpha" };
            var emptyCounts = new System.Collections.Generic.Dictionary<string, int>();
            var allNullSearch = System.Linq.Enumerable.ToList(PlayerDirectoryQuery.Apply(new[] { p1, p2 }, null, 0, emptyCounts, emptyCounts, emptyCounts));
            Assert(allNullSearch.Count == 2 && allNullSearch[0].Name == "Alpha" && allNullSearch[1].Name == "Bravo", "PlayerDirectoryQuery.Apply(null search) must not throw and must sort by Name");
            var hitId = System.Linq.Enumerable.ToList(PlayerDirectoryQuery.Apply(new[] { p1, p2 }, "87654321", 0, emptyCounts, emptyCounts, emptyCounts));
            Assert(hitId.Count == 1 && hitId[0].Id == p2.Id, "PlayerDirectoryQuery.Apply must filter by SteamID substring");

            // 3. Verify packed-int PoiIndex preserves deterministic layout order on viewport queries
            var poiStore = new ScumMapStore { MasterEnabled = true };
            poiStore.CategoryById[1] = new ScumMapCategory { Id = 1, Section = "Outposts", Name = "Trader", DefaultEnabled = true };
            for (int i = 0; i < 40; i++) poiStore.Markers.Add(new ScumMapMarker { Id = i + 1, CategoryId = 1, X = 0.35f + (i % 8) * 0.02f, Y = 0.35f + (i / 8) * 0.02f, Title = "T" + i });
            var st = new WpfMapState { Store = poiStore, Zoom = 2.0f, Pan = new System.Drawing.PointF(0.42f, 0.42f), PoiClustering = false };
            var vis1 = System.Linq.Enumerable.ToList(WpfMapSurface.VisiblePoiClusters(st, 800, 600, false));
            var vis2 = System.Linq.Enumerable.ToList(WpfMapSurface.VisiblePoiClusters(st, 800, 600, false));
            Assert(vis1.Count == 40 && vis2.Count == 40 && vis1[0].Members[0].Id == vis2[0].Members[0].Id && vis1[39].Members[0].Id == vis2[39].Members[0].Id, "Packed-int PoiIndex must return deterministic ordered results");
        }

        static void TestPhase10EulaAgreementAndPersistence() {
            // 1. Embedded RU/EN EULA resources load with key legal clauses and support email
            string ru = EulaAgreement.GetText(true);
            string en = EulaAgreement.GetText(false);
            Assert(ru.Length > 1500 && ru.Contains("support@zilore.app", StringComparison.Ordinal) && ru.Contains("ЗАПРЕЩАЕТСЯ", StringComparison.Ordinal), "Embedded EULA-RU.txt content invalid");
            Assert(en.Length > 1500 && en.Contains("support@zilore.app", StringComparison.Ordinal) && en.Contains("PROHIBITED", StringComparison.Ordinal), "Embedded EULA-EN.txt content invalid");

            // 2. Version matching strictly enforces current edition
            Assert(EulaAgreement.IsVersionAccepted(EulaAgreement.CurrentVersion), "Current EULA version must be accepted");
            Assert(!EulaAgreement.IsVersionAccepted(null) && !EulaAgreement.IsVersionAccepted("") && !EulaAgreement.IsVersionAccepted("2025-01-01"), "Empty or outdated EULA version must be rejected");

            // 3. MapWindow persists EulaAcceptedVersion cleanly in settings.ini across reloads
            string tmp = Path.Combine(Path.GetTempPath(), "zlr-p10-eula-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try {
                using (var eng1 = new MapWindow(tmp, true, true)) {
                    Assert(eng1.WpfNeedsEulaAcceptance, "Fresh profile must require EULA acceptance in diagnostic mode");
                    Assert(eng1.WpfAcceptEula() && !eng1.WpfNeedsEulaAcceptance && eng1.WpfEulaAcceptedVersion == EulaAgreement.CurrentVersion, "WpfAcceptEula must update state");
                }
                File.WriteAllText(Path.Combine(tmp, "settings.ini"), "Welcomed=True\nEulaAcceptedVersion=" + EulaAgreement.CurrentVersion + "\n");
                using (var eng2 = new MapWindow(tmp, true, true)) {
                    Assert(!eng2.WpfNeedsEulaAcceptance && eng2.WpfEulaAcceptedVersion == EulaAgreement.CurrentVersion, "Reloaded settings.ini must preserve EulaAcceptedVersion");
                }
            } finally {
                SafeCleanup.DeleteDirectory(tmp, true);
            }
        }

        static void TestPhase11UserGuideEmbeddedAndSearch() {
            // 1. Both embedded RU & EN User Guides load all 10 chapters cleanly
            var ruChapters = UserGuideContent.GetChapters(true);
            var enChapters = UserGuideContent.GetChapters(false);
            Assert(ruChapters.Length == 10 && enChapters.Length == 10, "Embedded RU & EN User Guides must contain 10 chapters each");
            Assert(ruChapters[0].Number == 1 && ruChapters[9].Number == 10 && enChapters[0].Number == 1 && enChapters[9].Number == 10, "User Guide chapter numbering mismatch");

            // 2. Instant keyword search matches relevant chapters in both languages
            var ruHit = UserGuideContent.Search("Borderless", true);
            var ruSquad = UserGuideContent.Search("отряд", true);
            var enRoute = UserGuideContent.Search("ZLR-ROUTE", false);
            var noHit = UserGuideContent.Search("zz_no_such_topic_999", true);
            Assert(ruHit.Length >= 1 && ruSquad.Length >= 1 && enRoute.Length >= 1 && noHit.Length == 0, "UserGuideContent.Search keyword filtering failed");
            Assert(UserGuideContent.Search(null, true).Length == 10, "Empty/null guide search must return all chapters");

            // 3. Official web guide URLs point to minimap.zilore.app
            Assert(UserGuideContent.WebGuideUrl(true) == "https://minimap.zilore.app/guide/" && UserGuideContent.WebGuideUrl(false) == "https://minimap.zilore.app/en/guide/", "WebGuideUrl endpoints invalid");
        }
    }
}
