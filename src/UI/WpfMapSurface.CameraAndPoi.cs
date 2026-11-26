using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DrawingPoint = System.Drawing.PointF;

namespace ZiloreMiniMap {
    internal sealed partial class WpfMapSurface {
        internal bool CameraInteracting;
        DispatcherTimer cameraFrame;
        bool cameraFramePending;
        TimeSpan lastCameraComposition = TimeSpan.MinValue;
        internal int CameraFramesPresented;
        void CancelCameraFrame() {
            if (cameraFrame != null)
                cameraFrame.Stop();
            if (cameraFramePending)
                CompositionTarget.Rendering -= CameraCompositionFrame;
            cameraFramePending = false;
        }
        void PresentCameraFrame() {
            if (!cameraFramePending)
                return;
            CancelCameraFrame();
            if (!CameraInteracting)
                return;
            CameraFramesPresented++;
            InvalidateVisual();
        }
        void CameraCompositionFrame(object sender, EventArgs args) {
            var frame = args as RenderingEventArgs;
            // WPF can raise Rendering more than once for the same composition
            // timestamp. Keep one invalidation per distinct frame.
            if (frame != null) {
                if (lastCameraComposition == frame.RenderingTime)
                    return;
                lastCameraComposition = frame.RenderingTime;
            }
            PresentCameraFrame();
        }
        sealed class CameraScene {
            internal DrawingGroup Drawing; internal BitmapSource Image; internal double Width, Height, Side; internal DrawingPoint Centre; internal int Generation, Opacity;
        }
        readonly object cameraGate = new object();
        CameraScene pendingCameraScene, completedCameraScene, cameraScene, idleCameraScene;
        bool cameraWorker, cameraApplyPosted;
        int cameraGeneration;
        internal int CameraSceneBuilds;
        internal void BeginCameraFrames() {
            if (!CameraInteracting)
                cameraScene = idleCameraScene;
            CameraInteracting = true;
        }
        internal void EndCameraFrames() {
            CameraInteracting = false;
            CancelCameraFrame();
            lastCameraComposition = TimeSpan.MinValue;
            lock (cameraGate) {
                cameraGeneration++;
                pendingCameraScene = null;
                completedCameraScene = null;
            }
            cameraScene = null;
            idleCameraScene = null;
            queuedTerrainKey = null;
        }
        void QueueCameraScene(DrawingGroup drawing, double width, double height) {
            drawing.Freeze();
            var scene = new CameraScene { Drawing = drawing, Width = width, Height = height, Side = MapSide(State, width, height, false), Centre = CameraCentre(State, width, height, false), Opacity = State.Opacity };
            lock (cameraGate) {
                scene.Generation = cameraGeneration;
                pendingCameraScene = scene;
                if (cameraWorker)
                    return;
                cameraWorker = true;
            }
            var dispatcher = Dispatcher;
            System.Threading.Tasks.Task.Run(() => {
                var thread = System.Threading.Thread.CurrentThread;
                var priority = thread.Priority;
                try {
                    thread.Priority = System.Threading.ThreadPriority.BelowNormal;
                    while (true) {
                        CameraScene next;
                        lock (cameraGate) {
                            next = pendingCameraScene;
                            pendingCameraScene = null;
                            if (next == null) {
                                cameraWorker = false;
                                return;
                            }
                        }
                        try {
                            // Bound software sampling even on a 4K/high-DPI layered window.
                            // Only immutable drawings cross to this worker.
                            double scale = Math.Min(1, 1280 / Math.Max(next.Width, next.Height));
                            int pixelsWide = Math.Max(1, (int)Math.Ceiling(next.Width * scale)), pixelsHigh = Math.Max(1, (int)Math.Ceiling(next.Height * scale));
                            var visual = new DrawingVisual();
                            using (var dc = visual.RenderOpen()) {
                                dc.PushTransform(FrozenMatrix(scale, 0, 0, scale, 0, 0));
                                dc.DrawDrawing(next.Drawing);
                                dc.Pop();
                            }
                            var bitmap = new RenderTargetBitmap(pixelsWide, pixelsHigh, 96, 96, PixelFormats.Pbgra32);
                            bitmap.Render(visual);
                            bitmap.Freeze();
                            next.Image = bitmap;
                            next.Drawing = null;
                            bool post = false;
                            lock (cameraGate) {
                                if (next.Generation != cameraGeneration)
                                    continue;
                                completedCameraScene = next;
                                if (!cameraApplyPosted) {
                                    cameraApplyPosted = true;
                                    post = true;
                                }
                            }
                            if (post && !dispatcher.HasShutdownStarted)
                                dispatcher.BeginInvoke(new Action(() => {
                                    CameraScene completed;
                                    lock (cameraGate) {
                                        completed = completedCameraScene;
                                        completedCameraScene = null;
                                        cameraApplyPosted = false;
                                    }
                                    if (completed == null || completed.Generation != cameraGeneration)
                                        return;
                                    CameraSceneBuilds++;
                                    if (CameraInteracting) {
                                        cameraScene = completed;
                                        RequestRedraw();
                                    } else
                                        idleCameraScene = completed;
                                }), DispatcherPriority.Render);
                        } catch (Exception error) { Program.LogException("CameraScene", error); }
                    }
                } finally { thread.Priority = priority; }
            });
        }
        void DrawFastInteractivePreview(DrawingContext dc, double width, double height) {
            var scene = cameraScene;
            if (scene != null && scene.Width == width && scene.Height == height && scene.Opacity > 0 && State.Opacity <= scene.Opacity) {
                double opScale = Math.Max(0.0, Math.Min(1.0, State.Opacity / (double)scene.Opacity));
                if (opScale < 0.999)
                    dc.PushOpacity(opScale);
                DrawCameraScene(dc, staticDrawing, width, height, true);
                if (opScale < 0.999)
                    dc.Pop();
                return;
            }
            var viewport = new Rect(0, 0, width, height);
            dc.PushClip(FrozenRect(viewport));
            dc.DrawRectangle(tacticalHitSurface, null, viewport);
            if (State.Opacity > 0 && BaseMap != null) {
                double side = MapSide(State, width, height, false);
                var centre = CameraCentre(State, width, height, false);
                dc.PushOpacity(State.Opacity / 100d);
                dc.PushTransform(FrozenMatrix(side / 1024, 0, 0, side / 1024, width / 2 - centre.X * side, height / 2 - centre.Y * side));
                dc.DrawDrawing(BaseMap);
                dc.Pop();
                dc.Pop();
            }
            dc.Pop();
        }
        void DrawCameraScene(DrawingContext dc, DrawingGroup fallback, double width, double height, bool ignoreOpacity = false) {
            var scene = cameraScene;
            if (scene == null || scene.Width != width || scene.Height != height || (!ignoreOpacity && scene.Opacity != State.Opacity)) {
                if (fallback != null)
                    dc.DrawDrawing(fallback);
                return;
            }
            double side = MapSide(State, width, height, false), scale = side / scene.Side;
            var centre = CameraCentre(State, width, height, false);
            var bounds = new Rect(width / 2 + (scene.Centre.X - centre.X) * side - width * scale / 2, height / 2 + (scene.Centre.Y - centre.Y) * side - height * scale / 2, width * scale, height * scale);
            var viewport = new Rect(0, 0, width, height);
            dc.PushClip(FrozenRect(viewport));
            dc.DrawRectangle(tacticalHitSurface, null, viewport);
            // Fill newly exposed margins only, avoiding double-opacity over the frame.
            if (!bounds.Contains(viewport) && BaseMap != null) {
                dc.PushClip(FrozenExclude(viewport, bounds));
                dc.PushOpacity(State.Opacity / 100d);
                dc.PushTransform(FrozenMatrix(side / 1024, 0, 0, side / 1024, width / 2 - centre.X * side, height / 2 - centre.Y * side));
                dc.DrawDrawing(BaseMap);
                dc.Pop();
                dc.Pop();
                dc.Pop();
            }
            dc.DrawImage(scene.Image, bounds);
            dc.Pop();
        }
        internal void RequestRedraw() {
            if (!CameraInteracting) {
                CancelCameraFrame();
                InvalidateVisual();
                return;
            }
            // Input can arrive much faster than a transparent window can present.
            // Present the latest state on the next composition frame instead of
            // waiting an additional 33 ms after every presented drag frame. Keep
            // a render-priority timer as a fallback when composition is suspended.
            if (cameraFramePending)
                return;
            cameraFramePending = true;
            CompositionTarget.Rendering += CameraCompositionFrame;
            if (cameraFrame == null) {
                cameraFrame = new DispatcherTimer(DispatcherPriority.Render, Dispatcher) { Interval = TimeSpan.FromMilliseconds(33) };
                cameraFrame.Tick += (s, e) => PresentCameraFrame();
            }
            cameraFrame.Start();
        }
        sealed class PoiStamp {
            internal Point At; internal double Radius, Scale; internal DrawingGroup Icon; internal Pen Outline;
        }
        sealed class PoiSceneTile {
            internal Rect Bounds; internal DrawingGroup Drawing;
        }
        static readonly System.Threading.SemaphoreSlim poiBuildSlots = new System.Threading.SemaphoreSlim(1);
        System.Threading.CancellationTokenSource poiBuild;
        WpfPoiCluster[] requestedPoiLayout, drawnPoiLayout;
        double requestedPoiSide, drawnPoiSide;
        PoiSceneTile[] poiSceneTiles;
        internal int PoiTilesDrawn;
        static DrawingGroup RecordPois(IEnumerable<PoiStamp> stamps, System.Threading.CancellationToken cancel) {
            var drawing = new DrawingGroup();
            int index = 0;
            using (var context = drawing.Open())
            foreach (var stamp in stamps) {
                if ((index++ & 127) == 0)
                    cancel.ThrowIfCancellationRequested();
                context.DrawEllipse(MapMarkerBg, stamp.Outline, stamp.At, stamp.Radius, stamp.Radius);
                context.PushTransform(FrozenMatrix(stamp.Scale, 0, 0, stamp.Scale, stamp.At.X, stamp.At.Y));
                context.DrawDrawing(stamp.Icon);
                context.Pop();
            }
            drawing.Freeze();
            return drawing;
        }
        long poiGeneration;
        string labelLayoutKey; MapZone[] labelLayoutZones; ScumMapStore labelLayoutStore; WpfMapLabel[] labelLayout, interactiveLabelBuffer;
        double labelLayoutSide, labelLayoutWidth, labelLayoutHeight; DrawingPoint labelLayoutCentre;
        long lastCompactLabelBuildMs = -1000;
        internal int LabelLayoutBuilds;
        WpfMapLabel[] RetainedLabels(double width, double height) {
            if (!State.Labels)
                return Array.Empty<WpfMapLabel>();
            var centre = CameraCentre(State, width, height, Compact);
            if (!Compact && CameraInteracting && labelLayout != null && labelLayoutSide > 0 && labelLayoutWidth == width && labelLayoutHeight == height && labelLayoutZones == State.Saved && labelLayoutStore == State.Store) {
                double side = MapSide(State, width, height, false), scale = side / labelLayoutSide;
                if (interactiveLabelBuffer == null || interactiveLabelBuffer.Length != labelLayout.Length) {
                    interactiveLabelBuffer = new WpfMapLabel[labelLayout.Length];
                    for (int li = 0; li < labelLayout.Length; li++)
                        interactiveLabelBuffer[li] = new WpfMapLabel();
                }
                double dx = (labelLayoutCentre.X - centre.X) * side, dy = (labelLayoutCentre.Y - centre.Y) * side, hw = width * 0.5, hh = height * 0.5;
                for (int li = 0; li < labelLayout.Length; li++) {
                    var src = labelLayout[li];
                    var dst = interactiveLabelBuffer[li];
                    dst.Text = src.Text;
                    dst.Major = src.Major;
                    dst.At = new Point(hw + (src.At.X - hw) * scale + dx, hh + (src.At.Y - hh) * scale + dy);
                }
                return interactiveLabelBuffer;
            }
            if (Compact && State.RotateMap && labelLayout != null && labelLayoutWidth == width && labelLayoutHeight == height && labelLayoutZones == State.Saved && labelLayoutStore == State.Store) {
                long nowMs = pulseClock.ElapsedMilliseconds;
                if (nowMs - lastCompactLabelBuildMs < 33)
                    return labelLayout;
            }
            var lk = cameraKeyBuilder;
            lk.Length = 0;
            double qx = Compact ? Math.Round(centre.X * 1000.0) : centre.X, qy = Compact ? Math.Round(centre.Y * 1000.0) : centre.Y, qr = Compact ? Math.Round(Rotation(State, Compact) / 5.0) * 5.0 : Rotation(State, Compact);
            double curDpi = CurrentDpiScaleX;
            lk.Append(width).Append('|').Append(height).Append('|').Append(Compact).Append('|').Append(MapSide(State, width, height, Compact)).Append('|').Append(qx).Append('|').Append(qy).Append('|').Append(qr).Append('|').Append((int)Math.Round(curDpi * 100.0)).Append('|').Append(State.Labels).Append('|').Append(State.LabelSize).Append('|').Append(State.Zones).Append('|').Append(State.Waypoints).Append('|').Append(State.PoiClustering).Append('|').Append(State.Store == null ? "" : State.Store.GetFilterHashKey()).Append('|').Append(Localization.CurrentCode);
            string key = lk.ToString();
            if (labelLayout == null || labelLayoutKey != key || labelLayoutZones != State.Saved || labelLayoutStore != State.Store) {
                labelLayout = SelectLabels(State, width, height, Compact, curDpi);
                labelLayoutKey = key;
                labelLayoutZones = State.Saved;
                labelLayoutStore = State.Store;
                labelLayoutSide = MapSide(State, width, height, Compact);
                labelLayoutCentre = centre;
                labelLayoutWidth = width;
                labelLayoutHeight = height;
                LabelLayoutBuilds++;
                if (Compact)
                    lastCompactLabelBuildMs = pulseClock.ElapsedMilliseconds;
            }
            return labelLayout;
        }
        internal int PoiSceneBuilds;
        internal bool PoiSceneRasterized;
        static void PostPoiScene(Dispatcher dispatcher, Action apply) {
            if (!dispatcher.HasShutdownStarted)
                dispatcher.BeginInvoke(apply, DispatcherPriority.Background);
        }
        void DrawRetainedPois(DrawingContext dc, double side, double left, double top, double width, double height) {
            StreamPois(dc, side, left, top, width, height);
        }
        double renderRotation;
        internal static double Rotation(WpfMapState state, bool compact) {
            return compact && state.RotateMap && state.Position != null ? 90 - state.Position.Yaw : 0;
        }
        internal static Point RotatePoint(Point p, double angle, double width, double height) {
            if (angle == 0)
                return p;
            double radians = angle * Math.PI / 180, c = Math.Cos(radians), s = Math.Sin(radians), x = p.X - width / 2, y = p.Y - height / 2;
            return new Point(width / 2 + x * c - y * s, height / 2 + x * s + y * c);
        }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScumMapStore, WpfPoiCache[]> poiCaches = new System.Runtime.CompilerServices.ConditionalWeakTable<ScumMapStore, WpfPoiCache[]>();
        sealed class PoiCellComparer : IEqualityComparer<Point> {
            public bool Equals(Point a, Point b) {
                return a.X == b.X && a.Y == b.Y;
            }
            public int GetHashCode(Point point) {
                unchecked {
                    return (int)point.X * 73856093 ^ (int)point.Y * 19349663;
                }
            }
        }
        static readonly PoiCellComparer poiCellComparer = new PoiCellComparer();
        internal static double PoiRadius(double zoom, bool compact) {
            return Math.Min(compact ? 7.5 : 9, (compact ? 4.5 : 5.5) + Math.Log(Math.Max(1, zoom), 2) * .8);
        }
        internal static int PoiClusterRebuilds;
        internal static WpfPoiCluster[] PoiClusters(WpfMapState state, double width, double height, bool compact) {
            if (state == null || state.Store == null || !state.Store.MasterEnabled || width <= 0 || height <= 0)
                return Array.Empty<WpfPoiCluster>();
            double rawSide = MapSide(state, width, height, compact), spacing = Math.Max(compact ? 36 : 42, Math.Min(compact ? 64 : 80, Math.Min(width, height) / (compact ? 5 : 12)));
            double side = compact ? Math.Pow(2.0, Math.Round(Math.Log(Math.Max(1.0, rawSide), 2.0) * 8.0) / 8.0) : rawSide;
            var cache = poiCaches.GetValue(state.Store, store => new[] { new WpfPoiCache(), new WpfPoiCache() })[compact ? 0 : 1];
            bool useClustering = compact ? (state.PoiClustering && MinimapPoiClusteringEnabled) : state.PoiClustering;
            string filter = state.Store.GetFilterHashKey();
            double singleRadius = Math.Round(PoiRadius(state.Zoom, compact) * 4.0) / 4.0;
            lock (cache) {
                int markerCount = state.Store.Markers.Count;
                if (!useClustering && cache.Clusters != null && !cache.Clustering && cache.Filter == filter && cache.Count == markerCount) {
                    if (cache.Clusters.Length > 0 && Math.Abs(cache.Clusters[0].Radius - singleRadius) >= 0.01) {
                        for (int ci = 0; ci < cache.Clusters.Length; ci++)
                            cache.Clusters[ci].Radius = singleRadius;
                    }
                    cache.Zoom = state.Zoom;
                    cache.Side = side;
                    cache.Spacing = spacing;
                    return cache.Clusters;
                }
                // Quantized side in Compact mode prevents rebuilding 8,000+ POIs on every smooth auto-zoom tick.
                if (cache.Clusters != null && cache.Clustering == useClustering && cache.Side == side && cache.Spacing == spacing && cache.Filter == filter && cache.Count == markerCount) {
                    if (Math.Abs(cache.Zoom - state.Zoom) >= 0.25) {
                        for (int ci = 0; ci < cache.Clusters.Length; ci++) {
                            var cl = cache.Clusters[ci];
                            if (cl.Members.Count == 1) cl.Radius = singleRadius;
                        }
                        cache.Zoom = state.Zoom;
                    }
                    return cache.Clusters;
                }
                var bins = new Dictionary<Point, List<WpfPoiCluster>>(poiCellComparer);
                var clusters = new List<WpfPoiCluster>();
                if (cache.OrderedMarkers == null || cache.Filter != filter || cache.Count != markerCount) {
                    cache.Labels = null;
                    var priorities = new Dictionary<int, int>(state.Store.CategoryById.Count);
                    foreach (var entry in state.Store.CategoryById) {
                        string symbol = PoiSymbol(entry.Value);
                        priorities[entry.Key] = symbol == "$" ? 0 : symbol == "B" ? 1 : symbol == "⌂" ? 2 : 3;
                    }
                    var allMarkers = state.Store.Markers;
                    var enabledList = new List<ScumMapMarker>(allMarkers.Count);
                    for (int mi = 0; mi < allMarkers.Count; mi++) {
                        var m = allMarkers[mi];
                        if (state.Store.IsCategoryEnabled(m.CategoryId))
                            enabledList.Add(m);
                    }
                    var ordered = enabledList.ToArray();
                    Array.Sort(ordered, (a, b) => {
                        int pa, pb;
                        if (!priorities.TryGetValue(a.CategoryId, out pa)) pa = 3;
                        if (!priorities.TryGetValue(b.CategoryId, out pb)) pb = 3;
                        int cmp = pa.CompareTo(pb);
                        if (cmp != 0) return cmp;
                        cmp = a.X.CompareTo(b.X);
                        if (cmp != 0) return cmp;
                        cmp = a.Y.CompareTo(b.Y);
                        if (cmp != 0) return cmp;
                        return a.Id.CompareTo(b.Id);
                    });
                    cache.OrderedMarkers = ordered;
                }
                double spacingSq = spacing * spacing;
                var orderedArr = cache.OrderedMarkers;
                for (int mi = 0; mi < orderedArr.Length; mi++) {
                    var marker = orderedArr[mi];
                    if (!useClustering) {
                        var single = new WpfPoiCluster { Point = marker.Point };
                        single.Members.Add(marker);
                        clusters.Add(single);
                        continue;
                    }
                    double x = marker.X * side, y = marker.Y * side;
                    int bx = (int)Math.Floor(x / spacing), by = (int)Math.Floor(y / spacing);
                    WpfPoiCluster nearest = null;
                    double distance = spacingSq;
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++) {
                        List<WpfPoiCluster> candidates;
                        if (!bins.TryGetValue(new Point(bx + dx, by + dy), out candidates))
                            continue;
                        for (int ci = 0; ci < candidates.Count; ci++) {
                            var candidate = candidates[ci];
                            double cx = x - candidate.Point.X * side, cy = y - candidate.Point.Y * side, d = cx * cx + cy * cy;
                            if (d < distance) {
                                nearest = candidate;
                                distance = d;
                            }
                        }
                    }
                    if (nearest == null) {
                        nearest = new WpfPoiCluster { Point = marker.Point };
                        clusters.Add(nearest);
                        var cell = new Point(bx, by);
                        List<WpfPoiCluster> bucket;
                        if (!bins.TryGetValue(cell, out bucket))
                            bins.Add(cell, bucket = new List<WpfPoiCluster>(4));
                        bucket.Add(nearest);
                    }
                    nearest.Members.Add(marker);
                }
                for (int ci = 0; ci < clusters.Count; ci++) {
                    var cluster = clusters[ci];
                    int cnt = cluster.Members.Count;
                    cluster.Radius = cnt == 1 ? singleRadius : Math.Min(compact ? 10 : 13, (compact ? 7.5 : 9.5) + Math.Log10(cnt) * 1.5);
                    bool mixed = false;
                    int firstCat = cluster.Members[0].CategoryId;
                    for (int k = 1; k < cnt; k++) {
                        if (cluster.Members[k].CategoryId != firstCat) {
                            mixed = true;
                            break;
                        }
                    }
                    cluster.Mixed = mixed;
                }
                cache.Clustering = useClustering;
                cache.Side = side;
                cache.Spacing = spacing;
                cache.Zoom = state.Zoom;
                cache.Filter = filter;
                cache.Count = markerCount;
                cache.Clusters = clusters.ToArray();
                System.Threading.Interlocked.Increment(ref PoiClusterRebuilds);
                return cache.Clusters;
            }
        }
    }
}
