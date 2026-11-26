#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;

namespace ZiloreMiniMap {
    /// <summary>
    /// Thread-safe reusable GDI screen-capture surface pool with cooperative DWM BitBlt pacing.
    /// Eliminates repeated Bitmap/Graphics handle allocations and prevents overlapping BitBlt bursts
    /// across passive HUD, navigation-tab, and death-banner scanners.
    /// </summary>
    internal static class GdiScreenCapturePool {
        const int MaxSlots = 16;

        sealed class CaptureSlot : IDisposable {
            internal Bitmap Bitmap;
            internal Graphics Graphics;
            internal PixelFormat Format;

            internal CaptureSlot(int width, int height, PixelFormat format) {
                Format = format;
                Bitmap = new Bitmap(width, height, format);
                Graphics = Graphics.FromImage(Bitmap);
            }

            public void Dispose() {
                try { Graphics.Dispose(); } catch { }
                try { Bitmap.Dispose(); } catch { }
            }
        }

        static readonly object CaptureGate = new object();
        static readonly Dictionary<string, CaptureSlot> Slots = new Dictionary<string, CaptureSlot>(StringComparer.Ordinal);
        static long lastCaptureTicks;

        internal static int ActiveSlotCount {
            get { lock (CaptureGate) return Slots.Count; }
        }

        static CaptureSlot AcquireSlotLocked(string slotKey, int width, int height, PixelFormat format) {
            if (Slots.TryGetValue(slotKey, out var existing)) {
                if (existing.Bitmap.Width == width && existing.Bitmap.Height == height && existing.Format == format) {
                    return existing;
                }
                existing.Dispose();
                Slots.Remove(slotKey);
            } else if (Slots.Count >= MaxSlots) {
                foreach (var kv in Slots) kv.Value.Dispose();
                Slots.Clear();
            }

            var created = new CaptureSlot(width, height, format);
            Slots[slotKey] = created;
            return created;
        }

        internal static bool TryCaptureAndInspect(
            string slotKey,
            Rectangle region,
            PixelFormat format,
            int minPacingMs,
            Action<Bitmap> inspect,
            Func<bool>? preCaptureCheck = null) {
            if (string.IsNullOrEmpty(slotKey) || inspect == null || region.Width <= 0 || region.Height <= 0) {
                return false;
            }

            lock (CaptureGate) {
                long nowTicks = Stopwatch.GetTimestamp();
                if (minPacingMs > 0 && lastCaptureTicks != 0) {
                    long elapsedMs = ((nowTicks - lastCaptureTicks) * 1000L) / Stopwatch.Frequency;
                    if (elapsedMs >= 0 && elapsedMs < minPacingMs) {
                        return false;
                    }
                }
                if (preCaptureCheck != null && !preCaptureCheck()) {
                    return false;
                }

                var slot = AcquireSlotLocked(slotKey, region.Width, region.Height, format);
                slot.Graphics.CopyFromScreen(region.Left, region.Top, 0, 0, region.Size, CopyPixelOperation.SourceCopy);
                lastCaptureTicks = Stopwatch.GetTimestamp();
                inspect(slot.Bitmap);
                return true;
            }
        }

        internal static bool WithScratchSurface(
            string slotKey,
            int width,
            int height,
            PixelFormat format,
            Action<Bitmap, Graphics> action) {
            if (string.IsNullOrEmpty(slotKey) || action == null || width <= 0 || height <= 0) return false;
            lock (CaptureGate) {
                var slot = AcquireSlotLocked(slotKey, width, height, format);
                action(slot.Bitmap, slot.Graphics);
                return true;
            }
        }

        internal static T InspectPooledSurfaceForTest<T>(
            string slotKey,
            int width,
            int height,
            PixelFormat format,
            Func<Bitmap, Graphics, T> action) {
            lock (CaptureGate) {
                var slot = AcquireSlotLocked(slotKey, width, height, format);
                return action(slot.Bitmap, slot.Graphics);
            }
        }

        internal static void DisposeSlot(string slotKey) {
            if (string.IsNullOrEmpty(slotKey)) return;
            lock (CaptureGate) {
                if (Slots.TryGetValue(slotKey, out var slot)) {
                    slot.Dispose();
                    Slots.Remove(slotKey);
                }
            }
        }

        internal static void DisposeAll() {
            lock (CaptureGate) {
                foreach (var kv in Slots) kv.Value.Dispose();
                Slots.Clear();
                lastCaptureTicks = 0;
            }
        }
    }
}
