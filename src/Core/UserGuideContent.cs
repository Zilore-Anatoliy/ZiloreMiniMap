#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ZiloreMiniMap {
    internal sealed class GuideChapter {
        internal int Number { get; init; }
        internal string Icon { get; init; } = "";
        internal string Title { get; init; } = "";
        internal string[] Lines { get; init; } = Array.Empty<string>();
        internal string SearchText { get; init; } = "";
    }

    /// <summary>
    /// Loads and indexes the embedded bilingual User Guide (USER-GUIDE-RU.md / USER-GUIDE-EN.md)
    /// for the built-in Pure WPF Help Center with instant chapter & keyword search.
    /// </summary>
    internal static class UserGuideContent {
        static readonly string[] ChapterIcons = {
            "🚀", "🧭", "🛣", "✈", "👥", "🕒", "🗺", "⚙", "🛡", "⌨"
        };

        static GuideChapter[]? cachedRu;
        static GuideChapter[]? cachedEn;

        internal static string WebGuideUrl(bool russian) {
            return russian ? "https://minimap.zilore.app/guide/" : "https://minimap.zilore.app/en/guide/";
        }

        internal static GuideChapter[] GetChapters(bool russian) {
            if (russian) return cachedRu ??= ParseMarkdown(LoadEmbedded("USER-GUIDE-RU.md", FallbackRu));
            return cachedEn ??= ParseMarkdown(LoadEmbedded("USER-GUIDE-EN.md", FallbackEn));
        }

        internal static GuideChapter[] Search(string? query, bool russian) {
            var all = GetChapters(russian);
            if (string.IsNullOrWhiteSpace(query)) return all;
            string q = query.Trim();
            var matches = new List<GuideChapter>(all.Length);
            for (int i = 0; i < all.Length; i++) {
                var ch = all[i];
                if (ch.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || ch.SearchText.Contains(q, StringComparison.OrdinalIgnoreCase)) {
                    matches.Add(ch);
                }
            }
            return matches.Count == 0 ? Array.Empty<GuideChapter>() : matches.ToArray();
        }

        static string LoadEmbedded(string resourceName, string fallback) {
            try {
                using (var stream = typeof(UserGuideContent).Assembly.GetManifestResourceStream(resourceName)) {
                    if (stream != null) {
                        using (var reader = new StreamReader(stream, Encoding.UTF8, true)) {
                            string text = reader.ReadToEnd();
                            if (!string.IsNullOrWhiteSpace(text)) return text;
                        }
                    }
                }
            } catch { }
            return fallback;
        }

        static GuideChapter[] ParseMarkdown(string markdown) {
            var chapters = new List<GuideChapter>(12);
            int curNum = 0;
            string curTitle = "";
            var curLines = new List<string>(32);

            Action flush = () => {
                if (string.IsNullOrEmpty(curTitle)) return;
                int num = curNum > 0 ? curNum : (chapters.Count + 1);
                string icon = (num >= 1 && num <= ChapterIcons.Length) ? ChapterIcons[num - 1] : "📘";
                var linesArr = curLines.ToArray();
                var sb = new StringBuilder(linesArr.Length * 48);
                for (int li = 0; li < linesArr.Length; li++) {
                    sb.AppendLine(linesArr[li]);
                }
                chapters.Add(new GuideChapter {
                    Number = num,
                    Icon = icon,
                    Title = curTitle,
                    Lines = linesArr,
                    SearchText = sb.ToString()
                });
                curLines.Clear();
            };

            using (var sr = new StringReader(markdown)) {
                string? raw;
                while ((raw = sr.ReadLine()) != null) {
                    string line = raw.TrimEnd();
                    if (line.StartsWith("## ", StringComparison.Ordinal)) {
                        flush();
                        string header = line.Substring(3).Trim();
                        int dot = header.IndexOf('.');
                        if (dot > 0 && dot <= 2 && int.TryParse(header.Substring(0, dot), out int parsedNum)) {
                            curNum = parsedNum;
                            curTitle = header.Substring(dot + 1).Trim();
                        } else {
                            curNum = chapters.Count + 1;
                            curTitle = header;
                        }
                    } else if (!string.IsNullOrEmpty(curTitle)) {
                        if (line.Trim() == "---") continue;
                        curLines.Add(line);
                    }
                }
            }
            flush();
            return chapters.Count == 0 ? Array.Empty<GuideChapter>() : chapters.ToArray();
        }

        const string FallbackRu =
            "## 1. Быстрый старт\n" +
            "Включите в SCUM режим «Окно без рамки» (Borderless), проверьте клавишу Copy Location (Num /) и нажмите M для открытия карты.\n" +
            "Поддержка: support@zilore.app · https://minimap.zilore.app/guide/";

        const string FallbackEn =
            "## 1. Quick Start\n" +
            "Set SCUM Display Mode to Windowed Fullscreen (Borderless), verify Copy Location (Num /), and press M for the tactical map.\n" +
            "Support: support@zilore.app · https://minimap.zilore.app/en/guide/";
    }
}
