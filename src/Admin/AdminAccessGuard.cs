#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;

namespace ZiloreMiniMap {
    internal static class AdminAccessGuard {
        const string Salt = "ZiloreGrandAdmin::2026::";
        const string ExpectedHash = "19a8df0388cae463118ddd65428144c8f97c143e7ce1b9e7e15b6b1f716e111a";
        static readonly byte[] ExpectedHashBytes = Convert.FromHexString(ExpectedHash);
        static volatile bool unlocked;

        internal static bool IsUnlocked {
            get { return unlocked && IntegrityGuard.IsRuntimeSafe(); }
        }

        internal static bool TryUnlock(string? password) {
            if (!IntegrityGuard.IsRuntimeSafe()) return false;
            if (string.IsNullOrEmpty(password)) return false;
            byte[] input = Encoding.UTF8.GetBytes(Salt + password);
            Span<byte> digest = stackalloc byte[32];
            try {
                SHA256.HashData(input, digest);
                if (CryptographicOperations.FixedTimeEquals(digest, ExpectedHashBytes)) {
                    unlocked = true;
                    return true;
                }
                return false;
            } finally {
                CryptographicOperations.ZeroMemory(input);
                CryptographicOperations.ZeroMemory(digest);
            }
        }
    }
}
