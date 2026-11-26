#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using System.Threading;

namespace ZiloreMiniMap {
    internal sealed class ErrorReportPayload {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("os")] public string Os { get; set; } = "";
        [JsonPropertyName("level")] public string Level { get; set; } = "";
        [JsonPropertyName("source")] public string Source { get; set; } = "";
        [JsonPropertyName("message")] public string Message { get; set; } = "";
        [JsonPropertyName("logs")] public string Logs { get; set; } = "";
    }

    [JsonSerializable(typeof(ErrorReportPayload))]
    internal sealed partial class ErrorReportJsonContext : JsonSerializerContext {
    }

    /// <summary>
    /// Automatically captures WARNING and ERROR events, packages a local diagnostic ZIP archive,
    /// notifies the user via tray balloon, supports opt-in cloud delivery (report.zilore.app via telemetry.ini),
    /// and opens Explorer + mailto:support@zilore.app on demand.
    /// </summary>
    internal static partial class ErrorReportService {
        const int MaxStoredReports = 10;
        internal const string CloudReportEndpoint = "https://report.zilore.app";
        internal const string CloudReportToken = "";
        static readonly HttpClient ReportHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        static readonly TimeSpan MinIntervalBetweenReports = TimeSpan.FromSeconds(60);
        static readonly TimeSpan DuplicateCooldown = TimeSpan.FromMinutes(10);

        static readonly object Gate = new object();
        static readonly Dictionary<string, DateTime> RecentSignatures = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        static DateTime lastReportUtc = DateTime.MinValue;
        static bool initialized;
        static bool cloudOptInEnabled;
        static string configuredToken = "";
        static string? telemetryIniPath;

        internal static event Action<string, string, string>? ReportCreated;

        internal static string ReportsDirectory {
            get {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Program.DataFolderName,
                    "reports");
            }
        }

        internal static bool CloudTelemetryOptIn {
            get { lock (Gate) return cloudOptInEnabled; }
        }

        internal static string EffectiveCloudToken {
            get {
                lock (Gate) {
                    if (!string.IsNullOrWhiteSpace(configuredToken)) return configuredToken;
                }
                string? envToken = Environment.GetEnvironmentVariable("ZILORE_REPORT_TOKEN");
                if (!string.IsNullOrWhiteSpace(envToken)) return envToken.Trim();
                return CloudReportToken;
            }
        }

        internal static bool IsCloudTelemetryActive {
            get {
                return CloudTelemetryOptIn
                    && !string.IsNullOrWhiteSpace(CloudReportEndpoint)
                    && !string.IsNullOrWhiteSpace(EffectiveCloudToken);
            }
        }

        internal static void LoadPreferences(string? dataFolder) {
            if (string.IsNullOrWhiteSpace(dataFolder)) return;
            string path = Path.Combine(dataFolder, "telemetry.ini");
            lock (Gate) {
                telemetryIniPath = path;
                cloudOptInEnabled = false;
                configuredToken = "";
                SafeCleanup.Try(() => {
                    if (!File.Exists(path)) return;
                    var ini = SettingsStore.ReadIni(path);
                    if (ini.TryGetValue("CloudReportsEnabled", out var en)) {
                        cloudOptInEnabled = en == "1" || string.Equals(en, "true", StringComparison.OrdinalIgnoreCase);
                    }
                    if (ini.TryGetValue("ReportToken", out var tok) && !string.IsNullOrWhiteSpace(tok)) {
                        configuredToken = tok.Trim();
                    }
                }, "ErrorReport", "Could not load telemetry.ini");
            }
        }

        internal static void SetCloudTelemetryOptIn(bool enabled, string? customToken = null) {
            string? pathSnapshot;
            string tokenSnapshot;
            lock (Gate) {
                cloudOptInEnabled = enabled;
                if (customToken != null) configuredToken = customToken.Trim();
                pathSnapshot = telemetryIniPath;
                tokenSnapshot = configuredToken;
            }
            if (!string.IsNullOrWhiteSpace(pathSnapshot)) {
                string savePath = pathSnapshot;
                var sb = new StringBuilder(128);
                sb.Append("CloudReportsEnabled=").Append(enabled ? "1" : "0").AppendLine();
                if (!string.IsNullOrEmpty(tokenSnapshot)) {
                    sb.Append("ReportToken=").Append(tokenSnapshot).AppendLine();
                }
                string body = sb.ToString();
                SafeCleanup.Try(() => SettingsStore.WriteAtomic(savePath, body), "ErrorReport", "Could not save telemetry.ini");
            }
        }

        internal static void Initialize() {
            lock (Gate) {
                if (initialized) return;
                initialized = true;
            }
            AppLogBuffer.EntryAdded += OnLogEntryAdded;
        }

        static void OnLogEntryAdded(AppLogEntry entry) {
            if (entry == null) return;
            bool isWarn = string.Equals(entry.Level, "WARNING", StringComparison.OrdinalIgnoreCase);
            bool isErr = string.Equals(entry.Level, "ERROR", StringComparison.OrdinalIgnoreCase);
            if (!isWarn && !isErr) return;

            if (string.Equals(entry.Source, "ErrorReport", StringComparison.OrdinalIgnoreCase)) return;

            DateTime now = DateTime.UtcNow;
            string signature = entry.Level + "|" + entry.Source + "|" + entry.Message;

            lock (Gate) {
                if (now - lastReportUtc < MinIntervalBetweenReports) return;
                if (RecentSignatures.TryGetValue(signature, out var seenAt) && now - seenAt < DuplicateCooldown) return;
                RecentSignatures[signature] = now;
                if (RecentSignatures.Count > 128) RecentSignatures.Clear();
                lastReportUtc = now;
            }

            ThreadPool.QueueUserWorkItem(_ => {
                SafeCleanup.Try(() => {
                    bool sentToCloud = TrySendCloudReport(entry.Level, entry.Source, entry.Message);
                    string zipPath = CreateReportArchive(entry.Level, entry.Source, entry.Message);
                    if (!string.IsNullOrEmpty(zipPath)) {
                        var handler = ReportCreated;
                        if (handler != null) handler(zipPath, entry.Level, sentToCloud ? (entry.Source + " · sent") : entry.Source);
                    }
                });
            });
        }

        [GeneratedRegex(@"([A-Za-z]:\\Users\\)([^\\\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex RxUserProfilePath();
        [GeneratedRegex(@"ZLR-POI-[A-Za-z0-9_-]{8,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex RxPoiKey();
        [GeneratedRegex(@"(ReportToken\s*=\s*)([^\r\n\s]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex RxReportToken();

        internal static bool IsSafeTelemetryEndpoint(string? url) {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(uri.UserInfo)
                && !string.IsNullOrWhiteSpace(uri.Host);
        }

        internal static string SanitizeTelemetryText(string? text, int maxLength = 24000) {
            if (string.IsNullOrEmpty(text)) return "";
            string s = text.Length > maxLength ? text.Substring(0, maxLength) : text;
            s = RxUserProfilePath().Replace(s, "$1<redacted>");
            s = RxPoiKey().Replace(s, "ZLR-POI-<redacted>");
            s = RxReportToken().Replace(s, "$1<redacted>");
            return s;
        }

        internal static string SerializeCloudPayload(string? level, string? source, string? summary, string? logs) {
            var payload = new ErrorReportPayload {
                Version = MapWindow.DisplayVersion,
                Os = Environment.OSVersion.ToString(),
                Level = SanitizeTelemetryText(level ?? "ERROR", 16),
                Source = SanitizeTelemetryText(source ?? "App", 64),
                Message = SanitizeTelemetryText(summary ?? "", 1024),
                Logs = SanitizeTelemetryText(logs ?? "", 24000)
            };
            return JsonSerializer.Serialize(payload, ErrorReportJsonContext.Default.ErrorReportPayload);
        }

        static bool TrySendCloudReport(string? level, string? source, string? summary) {
            if (!IsCloudTelemetryActive) return false;
            string token = EffectiveCloudToken;
            try {
                var all = AppLogBuffer.Snapshot("ALL");
                int start = Math.Max(0, all.Length - 25);
                var sb = new StringBuilder(1024);
                for (int i = start; i < all.Length; i++) sb.AppendLine(all[i].FormatLine());

                string json = SerializeCloudPayload(level, source, summary, sb.ToString());

                if (!IsSafeTelemetryEndpoint(CloudReportEndpoint)) return false;
                using (var req = new HttpRequestMessage(HttpMethod.Post, CloudReportEndpoint)) {
                    req.Headers.Add("X-Zilore-Token", token);
                    req.Headers.UserAgent.ParseAdd("ZiloreMiniMap/" + MapWindow.VersionString);
                    req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    using (var resp = ReportHttp.Send(req)) {
                        return resp.IsSuccessStatusCode;
                    }
                }
            } catch {
                return false;
            }
        }

        internal static string CreateReportArchive(string? level, string? source, string? summary) {
            string dir = ReportsDirectory;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 4);
            string zipPath = Path.Combine(dir, "error-report-" + stamp + ".zip");

            var sb = new StringBuilder(2048);
            sb.AppendLine("=== ZiloreMiniMap Diagnostic Report ===");
            sb.AppendLine("Version:   " + MapWindow.DisplayVersion);
            sb.AppendLine("OS:        " + Environment.OSVersion);
            sb.AppendLine("64-bit OS: " + Environment.Is64BitOperatingSystem);
            sb.AppendLine("Time UTC:  " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            sb.AppendLine("Level:     " + (level ?? "INFO"));
            sb.AppendLine("Source:    " + (source ?? "Manual"));
            sb.AppendLine();
            sb.AppendLine("=== Summary ===");
            sb.AppendLine(SanitizeTelemetryText(summary ?? "", 4096));
            sb.AppendLine();
            sb.AppendLine("=== Recent Journal (last 40 entries) ===");
            var all = AppLogBuffer.Snapshot("ALL");
            int start = Math.Max(0, all.Length - 40);
            for (int i = start; i < all.Length; i++) {
                sb.AppendLine(SanitizeTelemetryText(all[i].FormatLine(), 2048));
            }

            using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create)) {
                var reportEntry = archive.CreateEntry("report.txt", CompressionLevel.Optimal);
                using (var writer = new StreamWriter(reportEntry.Open(), new UTF8Encoding(true))) {
                    writer.Write(sb.ToString());
                }

                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Program.DataFolderName);
                string autoLog = Path.Combine(appData, "automatic.log");
                if (File.Exists(autoLog)) {
                    SafeCleanup.Try(() => {
                        var logEntry = archive.CreateEntry("automatic.log", CompressionLevel.Optimal);
                        using (var src = new FileStream(autoLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var reader = new StreamReader(src, Encoding.UTF8, true))
                        using (var writer = new StreamWriter(logEntry.Open(), new UTF8Encoding(true))) {
                            const int maxLogChars = 524288;
                            int written = 0;
                            string? line;
                            while (written < maxLogChars && (line = reader.ReadLine()) != null) {
                                string cleanLine = SanitizeTelemetryText(line, 4096);
                                writer.WriteLine(cleanLine);
                                written += cleanLine.Length + 2;
                            }
                        }
                    });
                }
            }

            PruneOldReports(dir);
            return zipPath;
        }

        static void PruneOldReports(string dir) {
            SafeCleanup.Try(() => {
                var files = new DirectoryInfo(dir).GetFiles("error-report-*.zip")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Skip(MaxStoredReports)
                    .ToArray();
                foreach (var f in files) {
                    SafeCleanup.DeleteFile(f.FullName);
                }
            });
        }

        internal static void OpenMailtoAndSelectReport(string? zipPath, string? subjectHint = null) {
            if (!string.IsNullOrEmpty(zipPath) && File.Exists(zipPath)) {
                WpfFieldTheme.OpenExplorer(zipPath, true);
            }

            try {
                string fileName = string.IsNullOrEmpty(zipPath) ? "error-report.zip" : Path.GetFileName(zipPath);
                string subject = string.Format(
                    CultureInfo.InvariantCulture,
                    "[{0}] {1}",
                    MapWindow.DisplayVersion,
                    string.IsNullOrWhiteSpace(subjectHint) ? "Diagnostic Report" : subjectHint);

                string body = Localization.Current == AppLanguage.Russian
                    ? "Здравствуйте!\r\n\r\nПрикрепляю архив отчёта об ошибке: " + fileName + "\r\n(Файл выделен в открывшемся окне Проводника — просто перетащите его в это письмо.)\r\n\r\nОписание проблемы:\r\n"
                    : "Hello!\r\n\r\nAttaching diagnostic report archive: " + fileName + "\r\n(The file is highlighted in the opened Explorer window — please drag it into this email.)\r\n\r\nIssue description:\r\n";

                string mailto = "mailto:" + MapWindow.SupportEmail
                    + "?subject=" + Uri.EscapeDataString(subject)
                    + "&body=" + Uri.EscapeDataString(body);

                WpfFieldTheme.OpenShellUri(mailto);
            } catch (Exception ex) {
                Program.LogException("ErrorReport", ex);
            }
        }
    }
}
