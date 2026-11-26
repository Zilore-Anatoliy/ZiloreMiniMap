#nullable enable
using System;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace ZiloreMiniMap {
    /// <summary>
    /// End User License Agreement (EULA) version metadata, embedded text loader,
    /// and installer clickwrap acceptance bridge (HKCU + settings.ini).
    /// </summary>
    internal static class EulaAgreement {
        internal const string CurrentVersion = "2026-11-26";
        internal const string SupportEmail = "support@zilore.app";
        internal const string OfficialWebsite = "https://minimap.zilore.app";
        const string RegistrySubKey = @"Software\Zilore\ZiloreMiniMap";
        const string RegistryValueName = "EulaAcceptedVersion";

        static string? cachedRu;
        static string? cachedEn;

        internal static bool IsVersionAccepted(string? acceptedVersion) {
            return !string.IsNullOrWhiteSpace(acceptedVersion)
                && string.Equals(acceptedVersion.Trim(), CurrentVersion, StringComparison.Ordinal);
        }

        internal static bool IsAcceptedInInstallerRegistry() {
            try {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistrySubKey)) {
                    if (key == null) return false;
                    return IsVersionAccepted(key.GetValue(RegistryValueName) as string);
                }
            } catch {
                return false;
            }
        }

        internal static string GetText(bool russian) {
            if (russian) {
                return cachedRu ??= LoadEmbeddedText("EULA-RU.txt", FallbackRu);
            }
            return cachedEn ??= LoadEmbeddedText("EULA-EN.txt", FallbackEn);
        }

        static string LoadEmbeddedText(string resourceName, string fallback) {
            try {
                using (var stream = typeof(EulaAgreement).Assembly.GetManifestResourceStream(resourceName)) {
                    if (stream != null) {
                        using (var reader = new StreamReader(stream, Encoding.UTF8, true)) {
                            string text = reader.ReadToEnd().Trim();
                            if (text.Length > 0) return text;
                        }
                    }
                }
            } catch { }
            return fallback;
        }

        const string FallbackRu =
            "ЛИЦЕНЗИОННОЕ СОГЛАШЕНИЕ С КОНЕЧНЫМ ПОЛЬЗОВАТЕЛЕМ (EULA) — ZiloreMiniMap (ред. 2026-11-26)\r\n\r\n" +
            "Модификация, декомпиляция, обход цифровой подписи и перераспространение программы строго запрещены.\r\n" +
            "Программа предоставляется «как есть» (AS IS). Все вопросы: support@zilore.app · https://minimap.zilore.app";

        const string FallbackEn =
            "END USER LICENSE AGREEMENT (EULA) — ZiloreMiniMap (Edition 2026-11-26)\r\n\r\n" +
            "Modification, decompilation, bypassing digital signature/integrity checks, and redistribution are strictly prohibited.\r\n" +
            "The Software is provided \"AS IS\". Inquiries: support@zilore.app · https://minimap.zilore.app";
    }
}
