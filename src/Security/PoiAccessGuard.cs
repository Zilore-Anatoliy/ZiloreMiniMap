#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ZiloreMiniMap {
    /// <summary>
    /// Offline ECDSA P-256 verifier and DPAPI-protected local license store for POI & Zilore AI access.
    /// Allowed durations: 1, 3, 6, 12, 24 hours | 30, 90, 180, 365 days (+ unlimited MASTER).
    /// </summary>
    internal static class PoiAccessGuard {
        const string KeyPrefix = "ZLR-POI-";
        const byte FormatVersion = 0x01;
        const byte KindTimedDays = 0x01;
        const byte KindTimedHours = 0x02;
        const byte KindMaster = 0xFF;
        const int PayloadLength = 10;
        const int SignatureLength = 64; // IEEE P1363 (r || s) for P-256
        const int TotalBinaryLength = PayloadLength + SignatureLength;

        // Replace this placeholder by running: .\scripts\Init-PoiKeyPair.ps1 -Inject
        // Format: Base64 of 65-byte uncompressed EC point (0x04 || X[32] || Y[32])
        const string EmbeddedPublicKeyUncompressedBase64 = "BKY4vEeBnI5CO0/rYAskzKm2IyVx9VnjlY73dWUX4HiZuWDZnD/PT+LzuCebMb4WK9IHjb0FwtvptI3SIIY0BvQ=";

        static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("ZiloreMiniMap.PoiAccess.v2.ECDSA.P256");
        static readonly object gate = new object();
        static readonly object fileIoGate = new object();

        static bool loaded;
        static bool isMaster;
        static DateTime expiresUtc = DateTime.MinValue;
        static DateTime lastSeenUtc = DateTime.MinValue;
        static readonly HashSet<string> redeemedHashes = new HashSet<string>(StringComparer.Ordinal);

        static string StateFilePath {
            get {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ZiloreMiniMap");
                return Path.Combine(dir, "poi-access.bin");
            }
        }

        internal static bool IsMaster {
            get {
                lock (gate) {
                    EnsureLoaded();
                    return isMaster;
                }
            }
        }

        internal static bool IsUnlocked {
            get {
                byte[]? snapToPersist = null;
                bool result;
                lock (gate) {
                    EnsureLoaded();
                    if (isMaster) return true;
                    DateTime now = DateTime.UtcNow;
                    if (lastSeenUtc != DateTime.MinValue && now < lastSeenUtc.AddMinutes(-10)) {
                        // System clock rolled back: deny timed access until clock is restored.
                        return false;
                    }
                    if (now > lastSeenUtc) {
                        bool shouldPersist = (now - lastSeenUtc).TotalHours >= 1;
                        lastSeenUtc = now;
                        if (shouldPersist) snapToPersist = SerializeStateLocked();
                    }
                    result = expiresUtc > now;
                }
                if (snapToPersist != null) {
                    byte[] payload = snapToPersist;
                    Task.Run(() => PersistProtectedState(payload));
                }
                return result;
            }
        }

        internal static int RemainingDays {
            get {
                lock (gate) {
                    EnsureLoaded();
                    if (isMaster) return 9999;
                    DateTime now = DateTime.UtcNow;
                    if (lastSeenUtc != DateTime.MinValue && now < lastSeenUtc.AddMinutes(-10)) return 0;
                    if (expiresUtc <= now) return 0;
                    return Math.Max(1, (int)Math.Ceiling((expiresUtc - now).TotalDays));
                }
            }
        }

        internal static string FormatRemaining(bool ru) {
            lock (gate) {
                EnsureLoaded();
                if (isMaster) return "MASTER";
                DateTime now = DateTime.UtcNow;
                if (lastSeenUtc != DateTime.MinValue && now < lastSeenUtc.AddMinutes(-10)) return ru ? "0 ч." : "0h";
                if (expiresUtc <= now) return ru ? "0 ч." : "0h";
                TimeSpan left = expiresUtc - now;
                if (left.TotalHours < 1.0) {
                    int mins = Math.Max(1, (int)Math.Ceiling(left.TotalMinutes));
                    return mins + (ru ? " мин." : "m");
                }
                if (left.TotalHours < 24.0) {
                    int hrs = Math.Max(1, (int)Math.Ceiling(left.TotalHours));
                    return hrs + (ru ? " ч." : "h");
                }
                int days = Math.Max(1, (int)Math.Ceiling(left.TotalDays));
                return days + (ru ? " дн." : "d");
            }
        }

        internal static bool TryActivate(string? rawInput, out string error) {
            bool ru = Localization.Current == AppLanguage.Russian;
            error = "";
            if (string.IsNullOrWhiteSpace(rawInput)) {
                error = ru ? "Введите код доступа." : "Enter an access code.";
                return false;
            }

            string trimmed = rawInput.Trim();
            if (!trimmed.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase)) {
                error = ru ? "Неверный формат кода (ожидается ZLR-POI-...)." : "Invalid code format (expected ZLR-POI-...).";
                return false;
            }

            if (!TryDecodeBase64Url(trimmed.Substring(KeyPrefix.Length), out var raw) || raw.Length != TotalBinaryLength) {
                error = ru ? "Код повреждён или имеет неверную длину." : "Code is corrupted or has invalid length.";
                return false;
            }

            byte version = raw[0];
            byte kind = raw[1];
            int durationValue = (raw[2] << 8) | raw[3];

            if (version != FormatVersion) {
                error = ru ? "Неподдерживаемая версия кода." : "Unsupported code version.";
                return false;
            }

            bool keyIsMaster = (kind == KindMaster);
            bool keyIsHours = (kind == KindTimedHours);
            if (keyIsMaster) {
                if (durationValue != 0) {
                    error = ru ? "Некорректный формат мастер-ключа." : "Invalid master key payload.";
                    return false;
                }
            } else if (kind == KindTimedDays) {
                if (!IsAllowedDurationDays(durationValue)) {
                    error = ru ? "Недопустимый срок действия ключа (разрешены: 30, 90, 180, 365 дней)." : "Invalid key duration (allowed: 30, 90, 180, 365 days).";
                    return false;
                }
            } else if (keyIsHours) {
                if (!IsAllowedDurationHours(durationValue)) {
                    error = ru ? "Недопустимый часовой срок ключа (разрешены: 1, 3, 6, 12, 24 часа)." : "Invalid hourly duration (allowed: 1, 3, 6, 12, 24 hours).";
                    return false;
                }
            } else {
                error = ru ? "Неизвестный тип ключа доступа." : "Unknown access key type.";
                return false;
            }

            byte[] payload = new byte[PayloadLength];
            byte[] signature = new byte[SignatureLength];
            Buffer.BlockCopy(raw, 0, payload, 0, PayloadLength);
            Buffer.BlockCopy(raw, PayloadLength, signature, 0, SignatureLength);

            if (!VerifySignature(payload, signature)) {
                error = ru ? "Недействительная цифровая подпись кода." : "Invalid digital signature.";
                return false;
            }

            string keyHash = ComputeKeyHash(raw);
            byte[] snapToSave;
            lock (gate) {
                EnsureLoaded();
                if (!keyIsMaster && redeemedHashes.Contains(keyHash)) {
                    error = ru ? "Этот код уже был активирован на данном ПК." : "This code has already been redeemed on this PC.";
                    return false;
                }

                DateTime now = DateTime.UtcNow;
                if (lastSeenUtc != DateTime.MinValue && now < lastSeenUtc.AddMinutes(-10)) {
                    error = ru ? "Обнаружен перевод системных часов назад. Синхронизируйте время Windows." : "System clock rollback detected. Please sync Windows time.";
                    return false;
                }

                if (keyIsMaster) {
                    isMaster = true;
                    expiresUtc = DateTime.MaxValue;
                } else {
                    DateTime baseTime = expiresUtc > now ? expiresUtc : now;
                    expiresUtc = keyIsHours ? baseTime.AddHours(durationValue) : baseTime.AddDays(durationValue);
                    redeemedHashes.Add(keyHash);
                    if (redeemedHashes.Count > 128) {
                        var keep = new List<string>(redeemedHashes);
                        redeemedHashes.Clear();
                        for (int i = keep.Count - 64; i < keep.Count; i++) redeemedHashes.Add(keep[i]);
                    }
                }
                lastSeenUtc = now;
                snapToSave = SerializeStateLocked();
            }
            // Persist outside gate lock so disk I/O never stalls UI reads
            PersistProtectedState(snapToSave);
            return true;
        }

        internal static bool IsAllowedDuration(int days) { return IsAllowedDurationDays(days); }
        internal static bool IsAllowedDurationDays(int days) { return days == 30 || days == 90 || days == 180 || days == 365; }
        internal static bool IsAllowedDurationHours(int hours) { return hours == 1 || hours == 3 || hours == 6 || hours == 12 || hours == 24; }

        static bool VerifySignature(byte[] payload, byte[] signature) {
            try {
                if (string.IsNullOrWhiteSpace(EmbeddedPublicKeyUncompressedBase64) || EmbeddedPublicKeyUncompressedBase64.StartsWith("__", StringComparison.Ordinal))
                    return false;
                byte[] pub = Convert.FromBase64String(EmbeddedPublicKeyUncompressedBase64);
                if (pub.Length != 65 || pub[0] != 0x04) return false;

                var ecParams = new ECParameters {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint {
                        X = Slice(pub, 1, 32),
                        Y = Slice(pub, 33, 32)
                    }
                };
                using (var ecdsa = ECDsa.Create(ecParams)) {
                    return ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
                }
            } catch (Exception ex) {
                Program.LogException("PoiAccessVerify", ex);
                return false;
            }
        }

        static byte[] Slice(byte[] src, int offset, int length) {
            byte[] dst = new byte[length];
            Buffer.BlockCopy(src, offset, dst, 0, length);
            return dst;
        }

        static string ComputeKeyHash(byte[] raw) {
            return Convert.ToBase64String(SHA256.HashData(raw));
        }

        static bool TryDecodeBase64Url(string? s, [NotNullWhen(true)] out byte[]? bytes) {
            bytes = null;
            if (string.IsNullOrWhiteSpace(s)) return false;
            try {
                string b64 = s.Replace('-', '+').Replace('_', '/');
                switch (b64.Length % 4) {
                    case 2: b64 += "=="; break;
                    case 3: b64 += "="; break;
                    case 1: return false;
                }
                bytes = Convert.FromBase64String(b64);
                return true;
            } catch {
                return false;
            }
        }

        static bool TryReadStateFileLocked(string filePath) {
            if (!File.Exists(filePath)) return false;
            byte[] protectedBytes = File.ReadAllBytes(filePath);
            byte[] plain = ProtectedData.Unprotect(protectedBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
            try {
                using (var ms = new MemoryStream(plain))
                using (var br = new BinaryReader(ms, Encoding.UTF8)) {
                    byte ver = br.ReadByte();
                    if (ver != 1) return false;
                    bool loadedMaster = br.ReadBoolean();
                    DateTime loadedExp = new DateTime(br.ReadInt64(), DateTimeKind.Utc);
                    DateTime loadedSeen = new DateTime(br.ReadInt64(), DateTimeKind.Utc);
                    int count = Math.Max(0, Math.Min(256, br.ReadInt32()));
                    var hashes = new List<string>(count);
                    for (int i = 0; i < count; i++) {
                        string h = br.ReadString();
                        if (!string.IsNullOrEmpty(h)) hashes.Add(h);
                    }
                    isMaster = loadedMaster;
                    expiresUtc = loadedExp;
                    lastSeenUtc = loadedSeen;
                    redeemedHashes.Clear();
                    for (int i = 0; i < hashes.Count; i++) redeemedHashes.Add(hashes[i]);
                    return true;
                }
            } finally {
                CryptographicOperations.ZeroMemory(plain);
            }
        }

        static void EnsureLoaded() {
            if (loaded) return;
            loaded = true;
            string path = StateFilePath;
            string bakPath = path + ".bak";
            try {
                if (TryReadStateFileLocked(path)) return;
            } catch (Exception ex) {
                Program.LogException("PoiAccessLoad", ex);
            }
            // Fallback to atomic backup file if primary was corrupted mid-power-loss
            try {
                if (TryReadStateFileLocked(bakPath)) {
                    Program.LogWarn("PoiAccessLoad", "Recovered license state from backup (.bak).");
                    return;
                }
            } catch (Exception ex) {
                Program.LogException("PoiAccessLoadBackup", ex);
            }
            isMaster = false;
            expiresUtc = DateTime.MinValue;
        }

        static byte[] SerializeStateLocked() {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms, Encoding.UTF8)) {
                bw.Write((byte)1);
                bw.Write(isMaster);
                bw.Write(expiresUtc.Ticks);
                bw.Write(lastSeenUtc.Ticks);
                bw.Write(redeemedHashes.Count);
                foreach (string h in redeemedHashes) bw.Write(h);
                bw.Flush();
                return ms.ToArray();
            }
        }

        static void PersistProtectedState(byte[]? plain) {
            if (plain == null) return;
            string? tmp = null;
            try {
                byte[] protectedBytes = ProtectedData.Protect(plain, DpapiEntropy, DataProtectionScope.CurrentUser);
                lock (fileIoGate) {
                    string path = StateFilePath;
                    string bakPath = path + ".bak";
                    string? dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None)) {
                        fs.Write(protectedBytes, 0, protectedBytes.Length);
                        fs.Flush(true);
                    }
                    if (File.Exists(path)) File.Replace(tmp, path, bakPath);
                    else File.Move(tmp, path);
                    tmp = null;
                }
            } catch (Exception ex) {
                Program.LogException("PoiAccessSave", ex);
            } finally {
                CryptographicOperations.ZeroMemory(plain);
                SafeCleanup.DeleteFile(tmp);
            }
        }
    }
}
