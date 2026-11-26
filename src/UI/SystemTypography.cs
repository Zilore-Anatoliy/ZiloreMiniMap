#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ZiloreMiniMap {
    /// <summary>
    /// Centralized Windows 11 / Windows 10 system typography engine.
    /// Uses native "Segoe UI Variable" on Windows 11 with automatic graceful fallback to "Segoe UI" on Windows 10,
    /// and enforces ClearType subpixel rendering across all WPF windows, layered overlays, popups, and GDI+ surfaces.
    /// </summary>
    internal static class SystemTypography {
        internal const string WpfUiFontFamilyName = "Segoe UI Variable, Segoe UI Variable Text, Segoe UI";
        internal const string WpfClockFontFamilyName = "Segoe UI Variable, Segoe UI Variable Text, Segoe UI, Segoe UI Symbol";
        internal const string WpfSymbolFontFamilyName = "Segoe UI Symbol, Segoe UI Variable, Segoe UI";

        internal static readonly System.Windows.Media.FontFamily WpfUiFontFamily = new System.Windows.Media.FontFamily(WpfUiFontFamilyName);
        internal static readonly System.Windows.Media.FontFamily WpfClockFontFamily = new System.Windows.Media.FontFamily(WpfClockFontFamilyName);
        internal static readonly System.Windows.Media.FontFamily WpfSymbolFontFamily = new System.Windows.Media.FontFamily(WpfSymbolFontFamilyName);

        static readonly Lazy<string> GdiFamilyLazy = new Lazy<string>(ResolveInstalledGdiFamily, LazyThreadSafetyMode.ExecutionAndPublication);
        static readonly object GdiCacheGate = new object();
        static readonly Dictionary<long, Font> GdiFontCache = new Dictionary<long, Font>();
        static int popupHooksRegistered;

        internal static string GdiUiFontFamilyName { get { return GdiFamilyLazy.Value; } }

        static string ResolveInstalledGdiFamily() {
            try {
                using (var installed = new InstalledFontCollection()) {
                    bool hasVarText = false, hasVar = false;
                    foreach (var fam in installed.Families) {
                        if (string.Equals(fam.Name, "Segoe UI Variable Text", StringComparison.OrdinalIgnoreCase)) hasVarText = true;
                        else if (string.Equals(fam.Name, "Segoe UI Variable", StringComparison.OrdinalIgnoreCase)) hasVar = true;
                    }
                    if (hasVarText) return "Segoe UI Variable Text";
                    if (hasVar) return "Segoe UI Variable";
                }
            } catch { }
            return "Segoe UI";
        }

        internal static Font GetGdiFont(float size, System.Drawing.FontStyle style = System.Drawing.FontStyle.Regular) {
            float clamped = Math.Max(6f, Math.Min(48f, size));
            int sizeKey = (int)Math.Round(clamped * 4f);
            long key = ((long)sizeKey << 8) | (uint)style;
            lock (GdiCacheGate) {
                if (GdiFontCache.TryGetValue(key, out var cached)) return cached;
                var font = new Font(GdiUiFontFamilyName, clamped, style, GraphicsUnit.Point);
                GdiFontCache[key] = font;
                return font;
            }
        }

        internal static Font CreateOwnedGdiFont(float size, System.Drawing.FontStyle style = System.Drawing.FontStyle.Regular) {
            float clamped = Math.Max(6f, Math.Min(48f, size));
            return new Font(GdiUiFontFamilyName, clamped, style, GraphicsUnit.Point);
        }

        internal static void ApplyGdiClearType(Graphics? graphics) {
            if (graphics != null) {
                graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            }
        }

        internal static void ApplyClearType(DependencyObject? target) {
            if (target == null) return;
            TextOptions.SetTextRenderingMode(target, TextRenderingMode.ClearType);
            TextOptions.SetTextFormattingMode(target, TextFormattingMode.Display);
            TextOptions.SetTextHintingMode(target, TextHintingMode.Fixed);
            RenderOptions.SetClearTypeHint(target, ClearTypeHint.Enabled);
        }

        internal static Style CreateWindowStyle(System.Windows.Media.Brush foreground, System.Windows.Media.Brush background) {
            var style = new Style(typeof(Window));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, WpfUiFontFamily));
            style.Setters.Add(new Setter(TextElement.FontFamilyProperty, WpfUiFontFamily));
            style.Setters.Add(new Setter(Control.ForegroundProperty, foreground));
            style.Setters.Add(new Setter(Control.BackgroundProperty, background));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 15.0));
            style.Setters.Add(new Setter(FrameworkElement.UseLayoutRoundingProperty, true));
            style.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));
            style.Setters.Add(new Setter(TextOptions.TextRenderingModeProperty, TextRenderingMode.ClearType));
            style.Setters.Add(new Setter(TextOptions.TextFormattingModeProperty, TextFormattingMode.Display));
            style.Setters.Add(new Setter(TextOptions.TextHintingModeProperty, TextHintingMode.Fixed));
            style.Setters.Add(new Setter(RenderOptions.ClearTypeHintProperty, ClearTypeHint.Enabled));
            return style;
        }

        internal static void RegisterGlobalPopupClearType() {
            if (Interlocked.Exchange(ref popupHooksRegistered, 1) != 0) return;
            EventManager.RegisterClassHandler(typeof(ToolTip), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, e) => {
                if (s is ToolTip tip) {
                    tip.FontFamily = WpfUiFontFamily;
                    ApplyClearType(tip);
                }
            }));
            EventManager.RegisterClassHandler(typeof(ContextMenu), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, e) => {
                if (s is ContextMenu cm) {
                    cm.FontFamily = WpfUiFontFamily;
                    ApplyClearType(cm);
                }
            }));
        }
    }
}
