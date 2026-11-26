using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace ZiloreMiniMap {
    /// <summary>
    /// Full GitHub / Mirror updater, rollback, ACL, process-identity, and ZIP-package regression suite
    /// integrated directly into CoreSelfTests so Stage 3/9 executes it on every release build.
    /// </summary>
    internal static partial class CoreSelfTests {
        static void Reject(Action action, string name) {
            try {
                action();
            } catch {
                return;
            }
            throw new InvalidOperationException("CoreSelfTest expected failure did not throw: " + name);
        }

        static string ManifestV1(string version, string hash) {
            return "schema=1\nversion=" + version + "\nsha256=" + hash + "\n";
        }

        static string Base64Sha256(byte[] bytes) {
            return Convert.ToBase64String(SHA256.HashData(bytes));
        }

        static void AssertBytes(string path, byte[] expected, string name) {
            Assert(File.Exists(path) && Convert.ToBase64String(File.ReadAllBytes(path)) == Convert.ToBase64String(expected), name);
        }

        static byte[] BuildTestPackageZip(byte[] exe, byte[] tiles, byte[] roads, string extraEntry, bool corruptManifest) {
            Func<byte[], string> hex = bytes => {
                using (var stream = new MemoryStream(bytes)) { return InstallationAssets.Digest(stream); }
            };
            using (var memory = new MemoryStream()) {
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true)) {
                    var entries = new Dictionary<string, byte[]> {
                        { "ZiloreMiniMap.exe", exe },
                        { "assets/map-tiles.bin", tiles },
                        { "assets/roads.bin", roads },
                        { "package.txt", Encoding.UTF8.GetBytes("version=1.2.10\nZiloreMiniMap.exe=" + hex(exe) + "\nassets/map-tiles.bin=" + (corruptManifest ? new string('0', 64) : hex(tiles)) + "\nassets/roads.bin=" + hex(roads) + "\n") }
                    };
                    if (extraEntry != null) entries.Add(extraEntry, new byte[] { 1 });
                    foreach (var entry in entries) {
                        using (var stream = zip.CreateEntry(entry.Key).Open()) {
                            stream.Write(entry.Value, 0, entry.Value.Length);
                        }
                    }
                }
                return memory.ToArray();
            }
        }

        sealed class UpdateFixtureProcess : IUpdateProcess {
            internal int ProcessId = 17;
            internal long Started = 1234567;
            internal string Path;
            internal bool Exited, Disposed, ExitOnClose, ExitOnKill = true;
            internal int CloseCalls, KillCalls;
            internal readonly List<int> Waits = new List<int>();
            internal Func<UpdateFixtureProcess, int, bool> DuringWait;
            internal Exception KillFailure;

            public int Id { get { return ProcessId; } }
            public long StartTimeUtcTicks { get { return Started; } }
            public string ExecutablePath { get { return Path; } }
            public bool HasExited { get { return Exited; } }
            public bool CloseMainWindow() { CloseCalls++; if (ExitOnClose) Exited = true; return true; }
            public bool WaitForExit(int milliseconds) {
                Waits.Add(milliseconds);
                if (DuringWait != null && DuringWait(this, milliseconds)) Exited = true;
                return Exited;
            }
            public void Kill() { KillCalls++; if (KillFailure != null) throw KillFailure; if (ExitOnKill) Exited = true; }
            public void Dispose() { Disposed = true; }
        }

        static void RunUpdateRegressionSuite() {
            CheckUpdateScheduleContract();
            CheckUpdateSecurityPolicyAndAcl();

            string root = Path.Combine(Path.GetTempPath(), "zlr-upd-suite-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                CheckUpdateManifestAndCacheContracts(root);
                CheckUpdateReplacementAndRollback(root);
                CheckUpdatePackageZipSecurityAndRollback(root);
                CheckUpdateHelperCleanupContract(root);
                CheckUpdateProcessIdentityPolicy(root);
                CheckUpdateHelperLaunchAndCancellation(root);
                CheckUpdateBootstrapSelectionContract(root);
            } finally {
                try {
                    if (Directory.Exists(root)) Directory.Delete(root, true);
                } catch { }
            }
        }

        static void CheckUpdateScheduleContract() {
            DateTime start = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
            var schedule = new UpdateCheckSchedule(start);
            Assert(!schedule.TryBegin(start.AddMinutes(59), true), "Periodic update must not run before hourly interval");
            Assert(!schedule.TryBegin(start.AddHours(1), false), "Busy UI must defer a due update check");
            Assert(schedule.TryBegin(start.AddHours(1).AddMinutes(1), true), "Deferred update must run when UI is idle");
            Assert(!schedule.TryBegin(start.AddHours(1).AddMinutes(1), true), "Repeated timer ticks must not duplicate checks");
            Assert(schedule.TryBegin(start.AddHours(5), true), "Long pause resume must perform one overdue check");
            Assert(!schedule.TryBegin(start.AddHours(5), true), "Resume must not replay missed intervals");
            schedule.RecordCheck(start.AddHours(5).AddMinutes(20));
            Assert(!schedule.TryBegin(start.AddHours(6), true), "Manual check must postpone next periodic check");
            Assert(schedule.TryBegin(start.AddHours(6).AddMinutes(20), true), "Periodic checks must resume after postponement");
        }

        static void CheckUpdateSecurityPolicyAndAcl() {
            foreach (string url in new[] { "https://github.com/o/r", "https://release-assets.githubusercontent.com/a", "https://objects.githubusercontent.com/a", "https://minimap.zilore.app/update.txt" }) {
                Assert(UpdateService.AllowedDownloadUri(new Uri(url), false), "Official HTTPS release endpoint must be accepted: " + url);
            }
            foreach (string url in new[] { "http://github.com/a", "https://github.com.evil.example/a", "https://github.com:444/a", "https://user@github.com/a", "https://steamcommunity.com/a", "https://127.0.0.1/a" }) {
                Assert(!UpdateService.AllowedDownloadUri(new Uri(url), false), "Unapproved endpoint must be rejected: " + url);
            }
            Assert(UpdateService.AllowedDownloadUri(new Uri("https://steamcommunity.com/profiles/1"), true), "Steam HTTPS endpoint accepted");
            Assert(!UpdateService.AllowedDownloadUri(new Uri("https://github.com/a"), true), "Steam redirects must not cross to GitHub");

            var user = new SecurityIdentifier("S-1-5-21-1-2-3-1001");
            var admin = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var elevated = UpdateInstaller.HelperDirectorySecurity(true, user);
            Assert(elevated.AreAccessRulesProtected && elevated.GetOwner(typeof(SecurityIdentifier)).Equals(admin), "Elevated helper ACL must be protected and owned by Administrators");
            var rules = elevated.GetAccessRules(true, true, typeof(SecurityIdentifier));
            Assert(rules.Count == 2, "Elevated helper must grant exactly Administrators and SYSTEM");
            foreach (FileSystemAccessRule rule in rules) {
                Assert(!rule.IdentityReference.Equals(user) && rule.InheritanceFlags == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit), "Unelevated user must have no write grant on elevated helper");
            }
        }

        static void CheckUpdateManifestAndCacheContracts(string root) {
            byte[] binary = Encoding.UTF8.GetBytes("MZtest executable fixture");
            string hash;
            hash = Convert.ToHexStringLower(SHA256.HashData(binary));
            string manifest = ManifestV1("1.2.10", hash);
            UpdateRelease release = UpdateRelease.Parse(manifest);
            Assert(release.Version > new Version(1, 2, 9, 0) && release.Version == new Version(1, 2, 10, 0) && release.Version < new Version(1, 3, 0, 0), "UpdateRelease numeric version comparison");
            Assert(UpdateRelease.Parse("\uFEFF" + manifest.Replace("\n", "\r\n")).Version == release.Version, "BOM and CRLF manifest parsing");

            foreach (string bad in new[] { "", "<html>version 9.0.0</html>", manifest.Replace("schema=1", "schema=2"), manifest + "version=9.0.0\n", manifest + "url=https://evil.example/file.exe", ManifestV1("1.2.3-beta", hash), ManifestV1("1.2", hash), ManifestV1("01.2.3", hash), ManifestV1("1.2.3.4", hash), ManifestV1("999999999999.2.3", hash), ManifestV1("1.2.3", "wrong") }) {
                Reject(() => UpdateRelease.Parse(bad), "Invalid manifest accepted");
            }
            Reject(() => { var s = new UpdateService("", root, null); Assert(s != null, "unreachable"); }, "Empty repository accepted");
            Reject(() => { var s = new UpdateService("owner/repo/../../evil", root, null); Assert(s != null, "unreachable"); }, "Traversal repository accepted");

            int requests = 0;
            Func<Uri, int, byte[]> transport = (url, limit) => {
                requests++;
                return url.AbsolutePath.EndsWith("update.txt", StringComparison.OrdinalIgnoreCase) ? Encoding.UTF8.GetBytes(manifest) : binary;
            };
            var service = new UpdateService("owner/releases", root, transport);
            Assert(service.ManifestUri.AbsoluteUri == "https://github.com/owner/releases/releases/latest/download/update.txt", "Manifest URI contract");
            Assert(service.DownloadUri(release).AbsoluteUri == "https://github.com/owner/releases/releases/download/v1.2.10/ZiloreMiniMap.exe", "Pinned download URI contract");
            service.Check(false);
            service.Check(false);
            service.Check(true);
            Assert(requests == 1, "Fresh cache must avoid repeated requests");
            string cache = Path.Combine(root, "owner_releases-release.txt");
            File.SetLastWriteTimeUtc(cache, DateTime.UtcNow.AddMinutes(-2));
            service.Check(false);
            Assert(requests == 1, "Startup cache must last 60 minutes");
            service.Check(true);
            Assert(requests == 2, "Manual check must refresh after 1 minute");
            File.WriteAllText(cache, "broken");
            service.Check(true);
            Assert(requests == 3, "Corrupt cache must trigger refresh");
            File.SetLastWriteTimeUtc(cache, DateTime.UtcNow.AddHours(-2));
            var offline = new UpdateService("owner/releases", root, (url, limit) => { throw new WebException("offline"); });
            Reject(() => offline.Check(false), "Stale cache must not be returned when offline");
            File.WriteAllText(Path.Combine(root, "owner_releases-retry-after.txt"), DateTime.UtcNow.AddMinutes(5).ToString("o"));
            Reject(() => service.Check(true), "Persisted cooldown must block requests");
            Assert(requests == 3, "Cooldown must not invoke transport");

            var now = DateTime.UtcNow;
            Assert(UpdateService.RetryAfter("120", now) == now.AddMinutes(2), "Retry seconds");
            Assert(UpdateService.RetryAfter("0", now) == now.AddMinutes(1), "Minimum retry cooldown");
            Assert(UpdateService.RetryAfter("bad", now) == now.AddMinutes(5), "Default retry cooldown");
            Assert(Math.Abs((UpdateService.RetryAfter(now.AddMinutes(10).ToString("r"), now) - now.AddMinutes(10)).TotalSeconds) < 1, "HTTP date retry cooldown");

            string destination = Path.Combine(root, "download.exe");
            service.Download(release, destination);
            Assert(File.Exists(destination), "Verified download missing");
            var poisoned = UpdateRelease.Parse(ManifestV1("1.2.10", new string('0', 64)));
            Reject(() => service.Download(poisoned, Path.Combine(root, "poisoned.exe")), "Locally forged release hash must be rejected");
            Assert(!File.Exists(Path.Combine(root, "poisoned.exe")), "Forged download must not write file");
            Reject(() => service.Download(release, destination), "Existing executable must not be overwritten");
            var corrupt = new UpdateService("owner/releases", root, (url, limit) => Encoding.UTF8.GetBytes(url.AbsolutePath.EndsWith("update.txt") ? manifest : "MZcorrupt"));
            Reject(() => corrupt.Download(release, Path.Combine(root, "corrupt.exe")), "Bad checksum must be rejected");
            Assert(!File.Exists(Path.Combine(root, "corrupt.exe")) && Directory.GetFiles(root, "*.partial").Length == 0, "Corrupt/partial downloads must be cleaned");
        }

        static void CheckUpdateReplacementAndRollback(string root) {
            byte[] oldBytes = Encoding.UTF8.GetBytes("MZ original MiniMap fixture"), newBytes = Encoding.UTF8.GetBytes("MZ updated MiniMap fixture");
            string success = Path.Combine(root, "replace-success");
            Directory.CreateDirectory(success);
            string installed = Path.Combine(success, "MiniMap user & test.exe"), staged = Path.Combine(success, "new MiniMap.exe");
            File.WriteAllBytes(installed, oldBytes);
            File.WriteAllBytes(staged, newBytes);
            int launches = 0;
            UpdateInstaller.ReplaceAndLaunch(staged, installed, Base64Sha256(newBytes), path => {
                launches++;
                Assert(path == installed, "Successful install must launch approved path");
                AssertBytes(installed, newBytes, "Launch callback must see verified new bytes");
                Assert(Directory.GetFiles(success, "*.previous").Length == 1, "Backup must stay recoverable until launch succeeds");
            });
            Assert(launches == 1 && !File.Exists(staged) && Directory.GetFiles(success, "*.previous").Length == 0, "Successful replace must consume staging and delete backup");

            string rollback = Path.Combine(root, "replace-rollback");
            Directory.CreateDirectory(rollback);
            installed = Path.Combine(rollback, "MiniMap.exe");
            staged = Path.Combine(rollback, "new.exe");
            File.WriteAllBytes(installed, oldBytes);
            File.WriteAllBytes(staged, newBytes);
            Reject(() => UpdateInstaller.ReplaceAndLaunch(staged, installed, Base64Sha256(newBytes), path => { throw new IOException("restart fail"); }), "Launch failure must abort and roll back");
            AssertBytes(installed, oldBytes, "Failed launch must restore original executable");
            Assert(Directory.GetFiles(rollback, "*.previous").Length == 0, "Rollback must clean backup file");

            string locked = Path.Combine(root, "replace-locked");
            Directory.CreateDirectory(locked);
            installed = Path.Combine(locked, "MiniMap.exe");
            staged = Path.Combine(locked, "new.exe");
            File.WriteAllBytes(installed, oldBytes);
            File.WriteAllBytes(staged, newBytes);
            using (var ownLock = new FileStream(installed, FileMode.Open, FileAccess.Read, FileShare.None)) {
                Reject(() => UpdateInstaller.ReplaceAndLaunch(staged, installed, Base64Sha256(newBytes), path => { }), "Locked executable must fail closed");
            }
            AssertBytes(installed, oldBytes, "Locked failure must preserve original executable");
        }

        static void CheckUpdatePackageZipSecurityAndRollback(string root) {
            byte[] exe = Encoding.UTF8.GetBytes("MZ package fixture"), tiles = Encoding.UTF8.GetBytes("MTL2 tiles fixture"), roads = Encoding.UTF8.GetBytes("roads fixture");
            string exeHash;
            using (var stream = new MemoryStream(exe)) exeHash = InstallationAssets.Digest(stream);
            byte[] package = BuildTestPackageZip(exe, tiles, roads, null, false);
            string packageHash;
            using (var stream = new MemoryStream(package)) packageHash = InstallationAssets.Digest(stream);
            string releaseManifest = "schema=2\nversion=1.2.10\nsha256=" + exeHash + "\npackage-sha256=" + packageHash + "\n";
            var release = UpdateRelease.Parse(releaseManifest);
            var service = new UpdateService("owner/releases", root, (uri, limit) => uri.AbsolutePath.EndsWith("update.txt") ? Encoding.UTF8.GetBytes(releaseManifest) : package);
            Assert(release.IsPackage && service.DownloadUri(release).AbsolutePath.EndsWith("/ZiloreMiniMap.zip"), "Schema 2 must use ZIP endpoint");

            string staged = Path.Combine(root, "pkg.update.exe");
            service.Download(release, staged);
            AssertBytes(staged, exe, "Package exe verified");
            AssertBytes(Path.Combine(UpdatePackage.AssetDirectory(staged), "map-tiles.bin"), tiles, "Package tiles verified");

            foreach (string badEntry in new[] { "../outside.txt", "assets/../outside.txt", "assets\\roads.bin", "assets/MAP-TILES.BIN", "unapproved.exe" }) {
                string rej = Path.Combine(root, "bad-entry.update.exe");
                Reject(() => UpdatePackage.Extract(BuildTestPackageZip(exe, tiles, roads, badEntry, false), rej, release), "Unexpected/traversal ZIP entry rejected: " + badEntry);
                Assert(!File.Exists(rej) && !Directory.Exists(UpdatePackage.AssetDirectory(rej)), "Rejected ZIP must leave no staged files");
            }
            string corrupt = Path.Combine(root, "bad-hash.update.exe");
            Reject(() => UpdatePackage.Extract(BuildTestPackageZip(exe, tiles, roads, null, true), corrupt, release), "Asset checksum mismatch must reject package");
            Assert(!File.Exists(corrupt) && !Directory.Exists(UpdatePackage.AssetDirectory(corrupt)), "Corrupt package must be cleaned");

            // Full package rollback test on restart failure & locked asset
            string installDir = Path.Combine(root, "pkg-install");
            Directory.CreateDirectory(Path.Combine(installDir, "assets"));
            string installed = Path.Combine(installDir, "MiniMap.exe"), oldRoads = Path.Combine(installDir, "assets", "roads.bin");
            byte[] oldExe = Encoding.UTF8.GetBytes("MZ prev"), oldRoadBytes = Encoding.UTF8.GetBytes("old roads");
            File.WriteAllBytes(installed, oldExe);
            File.WriteAllBytes(oldRoads, oldRoadBytes);
            File.WriteAllText(Path.Combine(installDir, "assets", "keep.txt"), "keep");
            string identity = UpdatePackage.AssetIdentity(staged);
            Reject(() => UpdatePackage.ReplaceAndLaunch(staged, installed, Base64Sha256(exe), identity, p => { throw new IOException("boom"); }), "Restart failure must roll back entire package");
            AssertBytes(installed, oldExe, "Package rollback restores old EXE");
            AssertBytes(oldRoads, oldRoadBytes, "Package rollback restores old roads");
            Assert(!File.Exists(Path.Combine(installDir, "assets", "map-tiles.bin")) && File.ReadAllText(Path.Combine(installDir, "assets", "keep.txt")) == "keep", "Package rollback removes only new assets");

            UpdatePackage.Cleanup(staged);
            service.Download(release, staged);
            identity = UpdatePackage.AssetIdentity(staged);
            int launches = 0;
            using (var lockStream = new FileStream(oldRoads, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                Reject(() => UpdatePackage.ReplaceAndLaunch(staged, installed, Base64Sha256(exe), identity, p => launches++), "Locked asset must roll back partial replacement");
            }
            Assert(launches == 0 && !File.Exists(Path.Combine(installDir, "assets", "map-tiles.bin")), "Locked asset rollback must not launch");
        }

        static void CheckUpdateHelperCleanupContract(string root) {
            string status = Path.Combine(root, "cleanup-status.txt"), outside = Path.Combine(root, "unrelated.exe");
            File.WriteAllText(outside, "keep");
            File.WriteAllText(status + ".helper", outside);
            Assert(!UpdateInstaller.CleanupHelper(status) && File.Exists(outside), "CleanupHelper must refuse paths outside private temp helper dir");

            string helperDir = Path.Combine(Path.GetTempPath(), "ScumMiniMap-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(helperDir);
            try {
                string helper = Path.Combine(helperDir, "Updater.exe");
                File.WriteAllText(helper, "fixture");
                File.WriteAllText(Path.Combine(helperDir, "request.bin"), "req");
                File.WriteAllText(status + ".helper", helper);
                Assert(UpdateInstaller.CleanupHelper(status) && !Directory.Exists(helperDir) && !File.Exists(status + ".helper"), "CleanupHelper removes owned files cleanly");

                Directory.CreateDirectory(helperDir);
                File.WriteAllText(helper, "fixture");
                File.WriteAllText(Path.Combine(helperDir, "unrelated.txt"), "keep");
                File.WriteAllText(status + ".helper", helper);
                Assert(!UpdateInstaller.CleanupHelper(status) && File.Exists(Path.Combine(helperDir, "unrelated.txt")), "CleanupHelper must never recursively delete unexpected files");
                File.Delete(Path.Combine(helperDir, "unrelated.txt"));
                Assert(UpdateInstaller.CleanupHelper(status) && !Directory.Exists(helperDir), "CleanupHelper finishes cleanly once unexpected file is gone");
            } finally {
                if (Directory.Exists(helperDir)) try { Directory.Delete(helperDir, true); } catch { }
            }
        }

        static void CheckUpdateProcessIdentityPolicy(string root) {
            string approvedPath = Path.Combine(root, "approved MiniMap.exe");
            var approved = new UpdateProcessIdentity(17, 1234567, approvedPath);
            UpdateInstaller.CloseApprovedProcess(approved, pid => { throw new ArgumentException("exited"); }, 20, 10);
            var exited = new UpdateFixtureProcess { Path = approvedPath, Exited = true };
            UpdateInstaller.CloseApprovedProcess(approved, pid => exited, 20, 10);
            Assert(exited.CloseCalls == 0 && exited.KillCalls == 0 && exited.Disposed, "Exited process must not be closed/killed");

            foreach (var wrong in new[] {
                new UpdateFixtureProcess { Path = approvedPath, ProcessId = 18 },
                new UpdateFixtureProcess { Path = approvedPath, Started = 1234568 },
                new UpdateFixtureProcess { Path = approvedPath + ".unrelated.exe" }
            }) {
                Reject(() => UpdateInstaller.CloseApprovedProcess(approved, pid => wrong, 20, 10), "Mismatched process identity must abort");
                Assert(wrong.CloseCalls == 0 && wrong.KillCalls == 0 && wrong.Disposed, "Mismatched process must be disposed untouched");
            }

            var graceful = new UpdateFixtureProcess { Path = approvedPath, ExitOnClose = true };
            UpdateInstaller.CloseApprovedProcess(approved, pid => graceful, 1000, 10);
            Assert(graceful.CloseCalls == 1 && graceful.KillCalls == 0 && graceful.Exited && graceful.Disposed, "Graceful exit avoids Kill");

            var stubborn = new UpdateFixtureProcess { Path = approvedPath };
            UpdateInstaller.CloseApprovedProcess(approved, pid => stubborn, 500, 10);
            Assert(stubborn.CloseCalls > 0 && stubborn.KillCalls == 1 && stubborn.Exited && stubborn.Disposed, "Stubborn matching process force-closed after grace");

            var reused = new UpdateFixtureProcess { Path = approvedPath };
            reused.DuringWait = (proc, ms) => { if (proc.CloseCalls > 0) proc.Started++; return false; };
            Reject(() => UpdateInstaller.CloseApprovedProcess(approved, pid => reused, 500, 10), "PID reuse during wait must abort Kill");
            Assert(reused.KillCalls == 0 && reused.Disposed, "Reused PID must never be killed");

            var denied = new UpdateFixtureProcess { Path = approvedPath, KillFailure = new Win32Exception(5, "Access denied") };
            Reject(() => UpdateInstaller.CloseApprovedProcess(approved, pid => denied, 200, 10), "Denied Kill must abort installation");
            Assert(denied.KillCalls == 1 && denied.Disposed, "Denied Kill must dispose process handle");
        }

        static void CheckUpdateHelperLaunchAndCancellation(string root) {
            string helper = Path.Combine(root, "private helper", "Updater.exe");
            string cfg = Path.Combine(root, "private helper", "request ü & %.bin");
            foreach (bool elev in new[] { false, true }) {
                ProcessStartInfo start = UpdateInstaller.CreateHelperStartInfo(helper, cfg, elev);
                Assert(start.FileName == helper && start.UseShellExecute == !elev && start.Verb == (elev ? "" : "runas"), "Helper start info elevation contract");
                string prefix = "-ApplyVerifiedUpdate ";
                Assert(start.Arguments.StartsWith(prefix, StringComparison.Ordinal) && Encoding.UTF8.GetString(Convert.FromBase64String(start.Arguments.Substring(prefix.Length))) == cfg, "Base64 config path roundtrip");
            }

            string dir = Path.Combine(root, "cancel-helper");
            Directory.CreateDirectory(dir);
            string installed = Path.Combine(dir, "MiniMap.exe"), staged = Path.Combine(dir, "new.exe"), status = Path.Combine(dir, "status.txt");
            File.WriteAllBytes(installed, Encoding.UTF8.GetBytes("MZ old"));
            File.WriteAllBytes(staged, Encoding.UTF8.GetBytes("MZ new"));
            string stagedAssets = UpdatePackage.AssetDirectory(staged);
            Directory.CreateDirectory(stagedAssets);
            File.WriteAllBytes(Path.Combine(stagedAssets, "map-tiles.bin"), Encoding.UTF8.GetBytes("MTL2-test"));
            File.WriteAllBytes(Path.Combine(stagedAssets, "roads.bin"), Encoding.UTF8.GetBytes("ROAD-test"));
            var approved = new UpdateProcessIdentity(17, 1234567, installed);
            string helperSource = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ZiloreMiniMap.exe");
            Func<string, Version> ver = p => string.Equals(Path.GetFullPath(p), staged, StringComparison.OrdinalIgnoreCase) ? new Version(1, 2, 10, 0) : new Version(1, 2, 4, 0);
            ProcessStartInfo requested = null;
            IOException uacEx = null;
            try {
                UpdateInstaller.StartForTarget(staged, installed, status, approved, false, start => {
                    requested = start;
                    throw new Win32Exception(1223, "UAC cancelled");
                }, ver, helperSource);
            } catch (IOException ex) { uacEx = ex; }
            Assert(uacEx != null && uacEx.InnerException is Win32Exception && ((Win32Exception)uacEx.InnerException).NativeErrorCode == 1223, "Cancelled UAC must report IOException wrapping 1223");
            Assert(requested != null && !Directory.Exists(requested.WorkingDirectory) && !File.Exists(status + ".helper"), "Cancelled helper must clean its temp dir & marker");
        }

        static void CheckUpdateBootstrapSelectionContract(string root) {
            string source = Path.Combine(root, "downloaded.exe"), oldPath = Path.Combine(root, "previous.exe");
            byte[] candBytes = Encoding.UTF8.GetBytes("MZ candidate");
            File.WriteAllBytes(source, candBytes);
            File.WriteAllBytes(oldPath, Encoding.UTF8.GetBytes("MZ old"));
            string hex;
            hex = Convert.ToHexStringLower(SHA256.HashData(candBytes));
            string manifest = ManifestV1("1.2.10", hex);
            Version candVer = new Version(1, 2, 10, 0);
            int reads = 0;
            var bundled = UpdateBootstrap.VerifyBundle(source, manifest, p => { reads++; return candVer; });
            Assert(bundled.Version == candVer && reads == 1, "VerifyBundle contract");
            reads = 0;
            Reject(() => UpdateBootstrap.VerifyBundle(source, ManifestV1("1.2.10", new string('0', 64)), p => { reads++; return candVer; }), "Hash mismatch must reject before metadata read");
            Assert(reads == 0, "Hash check must happen before version read");

            var one = new UpdateInstallation(oldPath, new Version(1, 2, 4, 0), new UpdateProcessIdentity(17, 1234567, oldPath));
            var two = new UpdateInstallation(Path.Combine(root, "other.exe"), new Version(1, 2, 3, 0), new UpdateProcessIdentity(18, 7654321, Path.Combine(root, "other.exe")));
            Assert(object.ReferenceEquals(UpdateBootstrap.SelectInstallation(new List<UpdateInstallation> { one }, null, candVer, source), one), "Single installation auto-selected");
            Reject(() => UpdateBootstrap.SelectInstallation(new List<UpdateInstallation> { one, two }, null, candVer, source), "Ambiguous installations require explicit path");
            Assert(object.ReferenceEquals(UpdateBootstrap.SelectInstallation(new List<UpdateInstallation> { one, two }, oldPath.ToUpperInvariant(), candVer, source), one), "Explicit path resolves case-insensitively");
            Reject(() => UpdateBootstrap.SelectInstallation(new List<UpdateInstallation> { one }, source, candVer, source), "Cannot select newly downloaded file as target");
        }
    }
}
