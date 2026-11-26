#nullable enable
using System;
using System.Drawing;
using System.Threading;

namespace ZiloreMiniMap {
    /// <summary>
    /// Minimal WPF refresh dispatch and world-to-normalized map projection (0..1).
    /// All legacy GDI+ rendering has been permanently retired in favor of Pure WPF.
    /// </summary>
    public sealed partial class MapWindow {
        void RenderOverlay() {
            if (InvokeRequired) {
                if (Interlocked.Exchange(ref renderQueued, 1) != 0) return;
                try {
                    BeginInvoke(new Action(() => {
                        Interlocked.Exchange(ref renderQueued, 0);
                        RenderOverlay();
                    }));
                } catch (InvalidOperationException) { Interlocked.Exchange(ref renderQueued, 0); }
                return;
            }
            if (WpfFieldConsoleRuntime.Active != null) {
                WpfFieldConsoleRuntime.Active.Refresh();
            }
        }

        public static PointF ToMap(Position p) {
            return new PointF((float)((617718 - p.X) / 1521618), (float)((618618 - p.Y) / 1523618));
        }
    }
}
