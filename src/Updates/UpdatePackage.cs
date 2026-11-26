#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ZiloreMiniMap {
    internal static partial class UpdatePackage {
        [GeneratedRegex(@"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant)]
        private static partial Regex RxLowerSha256();

        internal static string AssetDirectory(string staged) { return staged + ".assets"; }

        internal static string AssetIdentity(string staged) {
            string directory = AssetDirectory(staged);
            if (!Directory.Exists(directory)) return "";
            return DirectoryIdentity(directory);
        }

        static string DirectoryIdentity(string directory) {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Installation assets must not be a directory link.");
            var hashes = new string[InstallationAssets.Names.Length];
            for (int i = 0; i < InstallationAssets.Names.Length; i++) {
                hashes[i] = InstallationAssets.Digest(Path.Combine(directory, InstallationAssets.Names[i]));
            }
            return string.Join(":", hashes);
        }

        internal static void Cleanup(string staged) {
            string directory = AssetDirectory(staged);
            if (File.Exists(staged)) File.Delete(staged);
            if (Directory.Exists(directory)) {
                foreach (string name in InstallationAssets.Names) {
                    string assetPath = Path.Combine(directory, name);
                    if (File.Exists(assetPath)) File.Delete(assetPath);
                }
                Directory.Delete(directory, false);
            }
        }

        internal static void Extract(byte[] bytes, string staged, UpdateRelease release) {
            if (File.Exists(staged) || Directory.Exists(AssetDirectory(staged))) throw new IOException("Update staging already exists.");
            using (var memory = new MemoryStream(bytes, false)) {
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Read)) {
                    ExtractFromArchive(zip, staged, release);
                }
            }
        }

        internal static void ExtractFromFile(string zipPath, string staged, UpdateRelease release) {
            if (File.Exists(staged) || Directory.Exists(AssetDirectory(staged))) throw new IOException("Update staging already exists.");
            using (var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read)) {
                    ExtractFromArchive(zip, staged, release);
                }
            }
        }

        static void ExtractFromArchive(ZipArchive zip, string staged, UpdateRelease release) {
            bool complete = false;
            try {
                var allowed = new HashSet<string>(new[] {
                    "ZiloreMiniMap.exe", "ZiloreMiniMap.dll", "ZiloreCodeSigning.cer",
                    "ZiloreMiniMap.deps.json", "ZiloreMiniMap.runtimeconfig.json",
                    "D3DCompiler_47_cor3.dll", "PenImc_cor3.dll", "PresentationNative_cor3.dll",
                    "vcruntime140_cor3.dll", "wpfgfx_cor3.dll", "SkynettMiniMap.exe",
                    "package.txt", "README.md", "CHANGELOG.md", "ATTRIBUTION.md",
                    "detect_zones.py", "requirements.txt", "Start-MiniMap.cmd",
                    "Update-MiniMap.cmd", "keyboard-setup.png", "assets/",
                    "assets/map-tiles.bin", "assets/roads.bin"
                }, StringComparer.Ordinal);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long total = 0;
                foreach (var entry in zip.Entries) {
                    if (!allowed.Contains(entry.FullName) || !seen.Add(entry.FullName) || entry.Length > 192 * 1024 * 1024) throw new InvalidDataException("Unexpected update ZIP entry.");
                    total += entry.Length;
                    if (total > 384 * 1024 * 1024) throw new InvalidDataException("Update ZIP exceeds the unpacked size limit.");
                }
                string exeEntry = seen.Contains("ZiloreMiniMap.exe") ? "ZiloreMiniMap.exe" : "SkynettMiniMap.exe";
                foreach (string name in new[] { exeEntry, "package.txt", "assets/map-tiles.bin", "assets/roads.bin" }) {
                    if (!seen.Contains(name)) throw new InvalidDataException("Incomplete update package.");
                }
                string manifest;
                var manifestEntry = zip.GetEntry("package.txt");
                if (manifestEntry == null || manifestEntry.Length > 4096) throw new InvalidDataException("Oversized package manifest.");
                using (var input = manifestEntry.Open()) {
                    using (var memoryManifest = new MemoryStream()) {
                        byte[] buffer = new byte[1024];
                        int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) != 0) {
                            if (memoryManifest.Length + count > 4096) throw new InvalidDataException("Oversized package manifest.");
                            memoryManifest.Write(buffer, 0, count);
                        }
                        if (memoryManifest.Length != manifestEntry.Length) throw new InvalidDataException("Invalid package manifest length.");
                        manifest = Encoding.UTF8.GetString(memoryManifest.ToArray());
                    }
                }
                var expected = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string line in manifest.Trim().Split('\n')) {
                    string[] fields = line.Trim().Split('=');
                    if (fields.Length != 2 || expected.ContainsKey(fields[0])) throw new InvalidDataException("Invalid package manifest.");
                    expected.Add(fields[0], fields[1]);
                }
                if (expected.Count != 4 || !expected.TryGetValue("version", out var pkgVer) || pkgVer != release.VersionText || !expected.TryGetValue(exeEntry, out var pkgExeHash) || pkgExeHash != release.Sha256) {
                    throw new InvalidDataException("Package release identity mismatch.");
                }
                Directory.CreateDirectory(AssetDirectory(staged));
                foreach (string name in new[] { exeEntry, "assets/map-tiles.bin", "assets/roads.bin" }) {
                    if (!expected.TryGetValue(name, out var hash) || !RxLowerSha256().IsMatch(hash)) throw new InvalidDataException("Missing package checksum.");
                    string target = name == exeEntry ? staged : Path.Combine(AssetDirectory(staged), Path.GetFileName(name));
                    var zipEntry = zip.GetEntry(name);
                    if (zipEntry == null) throw new InvalidDataException("Incomplete update package.");
                    CopyEntry(zipEntry, target);
                    if (InstallationAssets.Digest(target) != hash) throw new InvalidDataException("Package file checksum mismatch.");
                }
                release.VerifyFile(staged);
                complete = true;
            } finally {
                if (!complete) Cleanup(staged);
            }
        }

        static void CopyEntry(ZipArchiveEntry entry, string target) {
            using (var input = entry.Open()) {
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    byte[] buffer = new byte[32768];
                    long copied = 0;
                    int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) != 0) {
                        copied += count;
                        if (copied > entry.Length || copied > 192 * 1024 * 1024) throw new InvalidDataException("Update entry exceeds its declared size.");
                        output.Write(buffer, 0, count);
                    }
                    if (copied != entry.Length) throw new InvalidDataException("Truncated update entry.");
                }
            }
        }

        internal static void ReplaceAndLaunch(string staged, string installed, string hash, string assetIdentity, Action<string> launch) {
            if (assetIdentity == "") { UpdateInstaller.ReplaceAndLaunch(staged, installed, hash, launch); return; }
            if (AssetIdentity(staged) != assetIdentity) throw new InvalidDataException("Staged assets changed.");
            string installDir = Path.GetDirectoryName(installed) ?? "";
            string target = Path.Combine(installDir, "assets");
            Directory.CreateDirectory(target);
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Installation assets must not be a directory link.");
            var changed = new List<string>();
            var backups = new Dictionary<string, string>();
            var tempIncoming = new List<string>();
            var crossSources = new List<string>();
            try {
                foreach (string name in InstallationAssets.Names) {
                    string destination = Path.Combine(target, name), source = Path.Combine(AssetDirectory(staged), name);
                    string localSource = source;
                    bool crossVolume = !string.Equals(Path.GetPathRoot(Path.GetFullPath(source)), Path.GetPathRoot(Path.GetFullPath(destination)), StringComparison.OrdinalIgnoreCase);
                    if (crossVolume) {
                        localSource = destination + "." + Guid.NewGuid().ToString("N") + ".incoming";
                        tempIncoming.Add(localSource);
                        File.Copy(source, localSource, true);
                        if (InstallationAssets.Digest(localSource) != InstallationAssets.Digest(source)) throw new InvalidDataException("Staged asset verification failed on target volume.");
                        crossSources.Add(source);
                    }
                    if (File.Exists(destination)) {
                        string backup = destination + "." + Guid.NewGuid().ToString("N") + ".previous";
                        File.Replace(localSource, destination, backup);
                        backups.Add(destination, backup);
                    } else {
                        File.Move(localSource, destination);
                    }
                    changed.Add(destination);
                }
                if (DirectoryIdentity(target) != assetIdentity) throw new InvalidDataException("Installed assets changed during replacement.");
                UpdateInstaller.ReplaceAndLaunch(staged, installed, hash, launch);
            } catch {
                foreach (string tmp in tempIncoming) SafeCleanup.DeleteFile(tmp);
                foreach (string destination in Enumerable.Reverse(changed)) {
                    if (backups.TryGetValue(destination, out var backup)) File.Replace(backup, destination, null);
                    else File.Delete(destination);
                }
                throw;
            }
            foreach (string backup in backups.Values) SafeCleanup.DeleteFile(backup);
            foreach (string src in crossSources) SafeCleanup.DeleteFile(src);
            SafeCleanup.DeleteDirectory(AssetDirectory(staged), false);
        }
    }
}
