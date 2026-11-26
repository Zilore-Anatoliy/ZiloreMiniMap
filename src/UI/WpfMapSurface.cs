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
    internal sealed partial class WpfMapSurface : FrameworkElement {
        // Layered windows pass fully transparent pixels to the game before WPF
        // hit testing runs. One alpha step keeps the tactical map clickable at 0%.
        static readonly Brush tacticalHitSurface = WpfFieldTheme.ColorBrush("#01000000");
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DrawingPoint[], StreamGeometry> zoneGeometryCache = new System.Runtime.CompilerServices.ConditionalWeakTable<DrawingPoint[], StreamGeometry>();
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DrawingPoint[][], StreamGeometry> habitatGeometryCache = new System.Runtime.CompilerServices.ConditionalWeakTable<DrawingPoint[][], StreamGeometry>();
        sealed class GeometryBounds {
            internal Rect Value; internal GeometryBounds(StreamGeometry geometry) {
                Value = geometry.Bounds;
            }
        }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StreamGeometry, GeometryBounds> geometryBoundsCache = new System.Runtime.CompilerServices.ConditionalWeakTable<StreamGeometry, GeometryBounds>();
        static StreamGeometry WorldGeometry(DrawingPoint[][] rings) {
            var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (var context = geometry.Open()) {
                for (int ri = 0; ri < rings.Length; ri++) {
                    var ring = rings[ri];
                    if (ring == null || ring.Length < 3)
                        continue;
                    context.BeginFigure(new Point(ring[0].X, ring[0].Y), true, true);
                    for (int pi = 1; pi < ring.Length; pi++)
                        context.LineTo(new Point(ring[pi].X, ring[pi].Y), true, false);
                }
            }
            geometry.Freeze();
            return geometry;
        }
        internal static volatile bool MinimapPoiClusteringEnabled = true;
        internal static volatile bool AviationModeEnabled = false;
        internal static volatile bool AutoDetectAviationEnabled = false;
        internal static volatile bool DebugProfilerEnabled = false;
        internal static volatile bool AltLayerHotkeysEnabled = false;
        internal static volatile int AviationAutoSpeedKmh = 110;
        internal static volatile int AviationArrivalRadiusM = 140;
        internal static volatile int ActiveRouteStopIndex = -1;
        internal static volatile string SquadLeadPeerId = null;
        internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, double> PeerSpeedKmh = new System.Collections.Concurrent.ConcurrentDictionary<string, double>(StringComparer.Ordinal);
        internal const double ScumMapWidthMetres = 15216.18;
        internal static double CompassHeading(double yaw) {
            return MapCameraService.ComputeCompassHeading(yaw);
        }
        static MatrixTransform FrozenMatrix(Matrix m) {
            var t = new MatrixTransform(m);
            t.Freeze();
            return t;
        }
        static MatrixTransform FrozenMatrix(double m11, double m12, double m21, double m22, double ox, double oy) {
            var t = new MatrixTransform(m11, m12, m21, m22, ox, oy);
            t.Freeze();
            return t;
        }
        static RotateTransform FrozenRotate(double angle, double cx, double cy) {
            var t = new RotateTransform(angle, cx, cy);
            t.Freeze();
            return t;
        }
        static TranslateTransform FrozenTranslate(double x, double y) {
            var t = new TranslateTransform(x, y);
            t.Freeze();
            return t;
        }
        static RectangleGeometry FrozenRect(Rect r) {
            var g = new RectangleGeometry(r);
            g.Freeze();
            return g;
        }
        static CombinedGeometry FrozenExclude(Rect outer, Rect inner) {
            var g = new CombinedGeometry(GeometryCombineMode.Exclude, FrozenRect(outer), FrozenRect(inner));
            g.Freeze();
            return g;
        }
        internal WpfMapState State;
        internal bool Compact;
        internal bool DirectTerrain;
        internal double RightOverlayInset;
        enum MapPass {
            All, Terrain, Trail, Annotations, Shared, Live
        }
        readonly VisualCollection mapLayers;
        readonly DrawingVisual terrainLayer = new DrawingVisual(), trailLayer = new DrawingVisual(), annotationLayer = new DrawingVisual(), sharedLayer = new DrawingVisual(), liveLayer = new DrawingVisual();
        ulong sharedLayerHash;
        bool sharedLayerHashValid;
        internal int SharedLayerBuilds;
        string terrainLayerKey, annotationLayerKey, trailLayerKey, terrainDrawingKey, queuedTerrainKey;
        DrawingGroup terrainDrawing, trailDrawing, annotationDrawing, staticDrawing;
        CameraScene presentedCameraScene;
        bool layersDrawn, layersCameraRasterized, compactCleared;
        static readonly string[,] GridSectorNames = CreateGridSectorNames();
        static readonly double[] CalibratedGridX = CreateCalibratedGrid(true);
        static readonly double[] CalibratedGridY = CreateCalibratedGrid(false);
        static double[] CreateCalibratedGrid(bool isX) {
            var pts = new double[6];
            for (int i = 0; i <= 5; i++) {
                if (isX) {
                    double wx = 617505.0 - i * 304132.0;
                    pts[i] = Math.Max(0.0, Math.Min(1.0, (617718.0 - wx) / 1521618.0));
                } else {
                    double wy = 617953.0 - i * 304356.0;
                    pts[i] = Math.Max(0.0, Math.Min(1.0, (618618.0 - wy) / 1523618.0));
                }
            }
            return pts;
        }
        static string[,] CreateGridSectorNames() {
            var g = new string[5, 5];
            for (int r = 0; r < 5; r++)
            for (int c = 0; c < 5; c++)
                g[r, c] = "" + "DCBAZ"[r] + (4 - c);
            return g;
        }
        Geometry cachedClipGeo; double cachedClipW, cachedClipH; string cachedClipShape;
        Rect cachedWorldClipRect; RectangleGeometry cachedWorldClipGeo;
        RotateTransform cachedRotateTransform; double cachedRotAngle, cachedRotW, cachedRotH;
        DrawingPoint[] cachedTrailSource; int cachedTrailLen; StreamGeometry cachedTrailGeo;
        MatrixTransform cachedTrailXform; double cachedTrailSide, cachedTrailLeft, cachedTrailTop;
        struct ZoneBoundsEntry {
            internal StreamGeometry Geo; internal Rect WorldBounds; internal int FillArgb, StrokeArgb;
        }
        MapZone[] cachedZoneSource; int cachedZoneRev; ZoneBoundsEntry[] cachedZoneEntries;
        readonly Dictionary<string, DateTime> pingFirstSeenUtc = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        readonly List<string> expiredPingsBuffer = new List<string>(32);
        const double PingTtlSeconds = 90.0;
        double cachedDpiScaleX;
        double CurrentDpiScaleX {
            get {
                if (cachedDpiScaleX <= 0)
                    cachedDpiScaleX = VisualTreeHelper.GetDpi(this).DpiScaleX;
                return cachedDpiScaleX;
            }
        }
        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) {
            base.OnDpiChanged(oldDpi, newDpi);
            cachedDpiScaleX = newDpi.DpiScaleX;
            ClearLabelCache();
            poiIcons.Clear();
            poiIconOrder.Clear();
        }
        internal static void ClearLabelCache() {
            labelTexts.Clear();
            labelTextOrder.Clear();
            dynamicPeerLabels.Clear();
            poiOutlinePens.Clear();
        }
        static readonly double[] CompassTickCos = CreateCompassTicks(true), CompassTickSin = CreateCompassTicks(false);
        static double[] CreateCompassTicks(bool isCos) {
            var res = new double[8];
            int idx = 0;
            for (int deg = 0; deg < 360; deg += 30) {
                if (deg % 90 == 0)
                    continue;
                double rad = (deg - 90.0) * Math.PI / 180.0;
                res[idx++] = isCos ? Math.Cos(rad) : Math.Sin(rad);
            }
            return res;
        }
        internal int TerrainRevision, TerrainLayerBuilds, AnnotationLayerBuilds, LiveLayerBuilds;
        protected override int VisualChildrenCount {
            get {
                return mapLayers == null ? 0 : mapLayers.Count;
            }
        }
        protected override Visual GetVisualChild(int index) {
            return mapLayers[index];
        }
        void ClearTacticalLayers() {
            if (!layersDrawn)
                return;
            foreach (DrawingVisual layer in mapLayers)
            using (layer.RenderOpen()) {
            }
            layersDrawn = layersCameraRasterized = false;
            sharedLayerHashValid = false;
            terrainLayerKey = annotationLayerKey = trailLayerKey = terrainDrawingKey = queuedTerrainKey = null;
            terrainDrawing = trailDrawing = annotationDrawing = staticDrawing = null;
            presentedCameraScene = null;
        }
        internal void PublishState(WpfMapState state) {
            Dispatcher.VerifyAccess();
            State = state;
            if (ActualWidth < 1 || ActualHeight < 1 || CameraInteracting) {
                RequestRedraw();
                return;
            }
            UpdateTacticalLayers();
        }
        readonly System.Text.StringBuilder cameraKeyBuilder = new System.Text.StringBuilder(128);
        string CameraLayerKey() {
            var centre = CameraCentre(State, ActualWidth, ActualHeight, Compact);
            var sb = cameraKeyBuilder;
            sb.Length = 0;
            sb.Append(ActualWidth).Append('|').Append(ActualHeight).Append('|').Append(Compact).Append('|').Append(State.Shape).Append('|')
              .Append(State.Zoom).Append('|').Append(centre.X).Append('|').Append(centre.Y).Append('|').Append(Rotation(State, Compact)).Append('|')
              .Append(CurrentDpiScaleX);
            return sb.ToString();
        }
        static int Identity(object value) {
            return value == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
        void UpdateTacticalLayers() {
            if (ActualWidth < 1 || ActualHeight < 1)
                return;
            if (State == null) {
                ClearTacticalLayers();
                using (var dc = terrainLayer.RenderOpen())
                    dc.DrawRectangle(tacticalHitSurface, null, new Rect(0, 0, ActualWidth, ActualHeight));
                layersDrawn = true;
                return;
            }
            if (CameraInteracting && !Compact && !DirectTerrain) {
                // During active wheel zoom or pan, NEVER decode 8K tiles or rebuild POI/label layers.
                // Reproject the cached snapshot or fast 1536px BaseMap overview in a single draw call.
                string preview = "preview|" + CameraLayerKey() + "|" + State.Opacity;
                if (terrainLayerKey != preview || !object.ReferenceEquals(presentedCameraScene, cameraScene)) {
                    using (var dc = terrainLayer.RenderOpen())
                        DrawFastInteractivePreview(dc, ActualWidth, ActualHeight);
                    terrainLayerKey = preview;
                    presentedCameraScene = cameraScene;
                }
                if (!layersCameraRasterized) {
                    using (trailLayer.RenderOpen()) {
                    }
                    using (annotationLayer.RenderOpen()) {
                    }
                }
                layersCameraRasterized = true;
                UpdateSharedLayer(preview);
                using (var dc = liveLayer.RenderOpen())
                    DrawMapPass(dc, MapPass.Live);
                LiveLayerBuilds++;
                layersDrawn = true;
                return;
            }
            if (Compact) {
                // Fast single-pass render for 60 FPS minimap HUD without allocating/freezing 4 DrawingGroups per frame.
                using (var dc = terrainLayer.RenderOpen())
                    DrawMapPass(dc, MapPass.All);
                if (layersDrawn && !compactCleared) {
                    using (trailLayer.RenderOpen()) {
                    }
                    using (annotationLayer.RenderOpen()) {
                    }
                    using (sharedLayer.RenderOpen()) {
                    }
                    using (liveLayer.RenderOpen()) {
                    }
                    compactCleared = true;
                }
                LiveLayerBuilds++;
                layersDrawn = true;
                return;
            }
            compactCleared = false;
            string camera = CameraLayerKey();
            var kb = cameraKeyBuilder;
            kb.Length = 0;
            kb.Append(Identity(State.Store)).Append('|');
            if (State.Store != null)
                kb.Append(State.Store.GetFilterHashKey()).Append('|').Append(State.Store.MasterEnabled);
            string store = kb.ToString();
            kb.Length = 0;
            kb.Append(camera).Append('|').Append(store).Append('|').Append(State.Opacity).Append('|').Append(State.Grid).Append('|').Append(State.GridLabels).Append('|').Append(State.GridOpacity).Append('|').Append(State.LabelSize).Append('|').Append(TerrainRevision).Append('|').Append(Identity(Terrain)).Append('|').Append(Identity(BaseMap)).Append('|').Append(Localization.CurrentCode);
            string terrainKey = kb.ToString();
            if (terrainDrawing == null || terrainDrawingKey != terrainKey) {
                terrainDrawing = new DrawingGroup();
                using (var dc = terrainDrawing.Open())
                    DrawMapPass(dc, MapPass.Terrain);
                terrainDrawing.Freeze();
                terrainDrawingKey = terrainKey;
                TerrainLayerBuilds++;
            }
            string trailKey = camera + "|" + State.History + "|" + State.HistoryColor + "|" + Identity(State.Trail);
            bool trailChanged = trailLayerKey != trailKey;
            if (trailChanged) {
                trailDrawing = new DrawingGroup();
                using (var dc = trailDrawing.Open())
                    DrawMapPass(dc, MapPass.Trail);
                trailDrawing.Freeze();
                trailLayerKey = trailKey;
            }
            kb.Length = 0;
            kb.Append(camera).Append('|').Append(store).Append('|').Append(State.Zones).Append('|').Append(State.Waypoints).Append('|').Append(State.Labels).Append('|').Append(State.PoiClustering).Append('|').Append(State.LabelSize).Append('|').Append(State.ZoneRevision).Append('|').Append(Identity(State.Saved)).Append('|').Append(Localization.CurrentCode).Append('|').Append(CameraInteracting);
            string annotation = kb.ToString();
            bool annotationChanged = annotationLayerKey != annotation + "|" + PoiSceneBuilds;
            if (annotationChanged) {
                annotationDrawing = new DrawingGroup();
                using (var dc = annotationDrawing.Open())
                    DrawMapPass(dc, MapPass.Annotations);
                annotationDrawing.Freeze();
                annotationLayerKey = annotation + "|" + PoiSceneBuilds;
                AnnotationLayerBuilds++;
            }
            string staticKey = terrainKey + "|" + trailKey + "|" + annotationLayerKey;
            if (!Compact && !DirectTerrain && queuedTerrainKey != staticKey && PoiViewportReady) {
                // Software composition must not replay dense annotation vectors on
                // every gesture frame. Only build the snapshot once POIs settle to avoid CPU spikes on M-key open.
                staticDrawing = new DrawingGroup();
                staticDrawing.Children.Add(terrainDrawing);
                staticDrawing.Children.Add(trailDrawing);
                staticDrawing.Children.Add(annotationDrawing);
                staticDrawing.Freeze();
                QueueCameraScene(staticDrawing, ActualWidth, ActualHeight);
                queuedTerrainKey = staticKey;
            }
            bool raster = CameraInteracting && !DirectTerrain;
            string presentation = (raster ? staticKey : terrainKey) + "|" + raster;
            if (terrainLayerKey != presentation || raster && !object.ReferenceEquals(presentedCameraScene, cameraScene)) {
                using (var dc = terrainLayer.RenderOpen()) {
                    if (raster)
                        DrawCameraScene(dc, staticDrawing, ActualWidth, ActualHeight);
                    else
                        dc.DrawDrawing(terrainDrawing);
                }
                terrainLayerKey = presentation;
                presentedCameraScene = cameraScene;
            }
            if (trailChanged || layersCameraRasterized != raster)
            using (var dc = trailLayer.RenderOpen()) {
                if (!raster)
                    dc.DrawDrawing(trailDrawing);
            }
            if (annotationChanged || layersCameraRasterized != raster)
            using (var dc = annotationLayer.RenderOpen()) {
                if (!raster)
                    dc.DrawDrawing(annotationDrawing);
            }
            layersCameraRasterized = raster;
            UpdateSharedLayer(camera);
            using (var dc = liveLayer.RenderOpen())
                DrawMapPass(dc, MapPass.Live);
            LiveLayerBuilds++;
            layersDrawn = true;
        }
        internal DrawingGroup BaseMap;
        internal WpfTerrainTiles Terrain;
        internal Action<DrawingPoint> PanChanged;
        internal Action ClearRouteRequested, StopNavigationRequested;
        internal Action<MapZone> DeleteWaypointRequested;
        internal Action<string> HoverChanged;
        internal void ClearHover() {
            ToolTip = null;
            hoverText = null;
            if (HoverChanged != null)
                HoverChanged(null);
        }
        long lastHover;
        string hoverText;
        DrawingPoint? cursorMapPoint;
        readonly List<DrawingPoint> measurePoints = new List<DrawingPoint>();
        bool measureClosed;
        readonly System.Diagnostics.Stopwatch pulseClock = System.Diagnostics.Stopwatch.StartNew();
        long playerPulseStartMs = -10000;
        string pulsePeerId;
        long peerPulseStartMs = -10000;
        static double currentGroundSpeedMps = 0.0;
        internal static double CurrentGroundSpeedMps {
            get {
                return System.Threading.Volatile.Read(ref currentGroundSpeedMps);
            }
            set {
                System.Threading.Volatile.Write(ref currentGroundSpeedMps, value);
            }
        }
        internal bool HasActivePulse {
            get {
                long now = pulseClock.ElapsedMilliseconds;
                return (now - playerPulseStartMs >= 0 && now - playerPulseStartMs < 1250) || (now - peerPulseStartMs >= 0 && now - peerPulseStartMs < 1450);
            }
        }
        internal void TriggerPlayerPulse() {
            playerPulseStartMs = pulseClock.ElapsedMilliseconds;
            if (Compact)
                InvalidateVisual();
            else
                RedrawLiveLayerOnly();
        }
        internal void TriggerPeerPulse(string peerId) {
            pulsePeerId = peerId;
            peerPulseStartMs = pulseClock.ElapsedMilliseconds;
            if (Compact)
                InvalidateVisual();
            else
                RedrawLiveLayerOnly();
        }
        internal bool RulerModeActive {
            get; private set;
        }
        internal Action<bool> RulerModeChanged;
        internal bool ToggleRulerMode() {
            if (Compact)
                return false;
            if (RulerModeActive) {
                ClearRuler();
                return false;
            }
            measurePoints.Clear();
            measureClosed = false;
            RulerModeActive = true;
            Cursor = Cursors.Cross;
            RedrawLiveLayerOnly();
            if (RulerModeChanged != null)
                RulerModeChanged(true);
            return true;
        }
        internal void ClearRuler() {
            bool changed = measurePoints.Count > 0 || RulerModeActive;
            measurePoints.Clear();
            measureClosed = false;
            if (RulerModeActive) {
                RulerModeActive = false;
                Cursor = null;
                if (RulerModeChanged != null)
                    RulerModeChanged(false);
            }
            if (changed) {
                if (Compact)
                    InvalidateVisual();
                else
                    RedrawLiveLayerOnly();
            }
        }
        internal string HoverDetails(DrawingPoint at) {
            if (State == null)
                return null;
            var store = State.Store;
            var parts = new List<string>();
            double side = MapSide(State, ActualWidth, ActualHeight, Compact);
            foreach (var shared in State.SharedMarkers.Where(sharedItem => MarkerDistance(new DrawingPoint((float)sharedItem.X, (float)sharedItem.Y), at) <= 14 / side))
                parts.Add(SharedMarkerLabel(shared));
            if (store == null || !store.MasterEnabled)
                return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts);
            double hit = 18 / side;
            var cluster = Index(PoiClusters(State, ActualWidth, ActualHeight, Compact)).Query(new Rect(at.X - hit, at.Y - hit, hit * 2, hit * 2)).Where(c => MarkerDistance(c.Point, at) <= (c.Radius + 3) / side).OrderBy(c => MarkerDistance(c.Point, at)).FirstOrDefault();
            var marker = cluster == null ? null : cluster.Members[0];
            if (cluster != null && cluster.Members.Count > 1) {
                parts.Add(cluster.Members.Count.ToString(CultureInfo.CurrentCulture) + " × " + string.Join(", ", cluster.Members.Select(m => Localization.GetMapObjectName(m.Title)).Distinct().Take(6)) + (cluster.Members.Count > 6 ? " …" : ""));
                marker = null;
            }
            if (marker != null) {
                ScumMapCategory category;
                store.CategoryById.TryGetValue(marker.CategoryId, out category);
                parts.Add(Localization.GetMapObjectName(marker.Title));
                if (category != null)
                    parts.Add(Localization.GetCategoryName(category.Name));
            }
            foreach (var habitat in store.Habitats.Where(h => store.IsHabitatEnabled(h) && h.Contains(at))) {
                var species = habitat.CategoryIds.Where(store.IsCategoryEnabled).Where(store.CategoryById.ContainsKey).Select(id => Localization.GetCategoryName(store.CategoryById[id].Name)).Distinct().OrderBy(n => n);
                parts.Add(Localization.GetMapObjectName(habitat.Name) + ": " + string.Join(", ", species));
            }
            return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts.Distinct());
        }
        static readonly Brush compassNorthInk = WpfFieldTheme.ColorBrush("#FF6F59");
        static readonly StreamGeometry VisionConeGeometry = CreateVisionConeGeometry();
        static StreamGeometry CreateVisionConeGeometry() {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open()) {
                double dist = 40.0;
                double halfAngle = 36.0 * Math.PI / 180.0;
                int steps = 14;
                ctx.BeginFigure(new Point(0, 0), true, true);
                for (int i = 0; i <= steps; i++) {
                    double a = -Math.PI / 2.0 - halfAngle + (2.0 * halfAngle * i / steps);
                    ctx.LineTo(new Point(dist * Math.Cos(a), dist * Math.Sin(a)), true, false);
                }
            }
            geometry.Freeze();
            return geometry;
        }
        static readonly StreamGeometry PlayerArrowGeometry = CreatePlayerArrowGeometry();
        static StreamGeometry CreatePlayerArrowGeometry() {
            var g = new StreamGeometry();
            using (var ctx = g.Open()) {
                ctx.BeginFigure(new Point(0, -8), true, true);
                ctx.LineTo(new Point(5.5, 6), true, false);
                ctx.LineTo(new Point(0, 3), true, false);
                ctx.LineTo(new Point(-5.5, 6), true, false);
            }
            g.Freeze();
            return g;
        }
        internal const double RouteThickness = 4;
        internal static string PaletteSeed(int index) {
            if (index < 0 || index >= 16)
                throw new ArgumentOutOfRangeException("index");
            // Pick evenly spaced existing identity hues. Old clients render the
            // same selected colour without a new protocol or extra map markers.
            uint target = (uint)(index * 225);
            for (uint value = 0; value < 1000000; value++) {
                string seed = value.ToString("x8", CultureInfo.InvariantCulture);
                uint hash = 2166136261;
                unchecked {
                    foreach (char c in seed)
                        hash = (hash ^ c) * 16777619;
                }
                if (hash % 3600 == target)
                    return seed;
            }
            throw new InvalidOperationException("Member palette unavailable");
        }
        internal static Brush MemberInk(string ownerId) {
            if (string.IsNullOrEmpty(ownerId))
                return WpfFieldTheme.Amber;
            Brush cached;
            if (memberInkCache.TryGetValue(ownerId, out cached))
                return cached;
            // Relay rosters prefix the transport identity with the group ID.
            // Hash only the member so every viewer uses the same color.
            string member = ownerId.Substring(ownerId.LastIndexOf(':') + 1);
            if (member.Length == 32 && member.All(Uri.IsHexDigit))
                member = member.Substring(0, 8).ToLowerInvariant();
            uint hash = 2166136261;
            unchecked {
                foreach (char c in member)
                    hash = (hash ^ c) * 16777619;
            }
            double hue = (hash % 3600) / 600.0, chroma = .68, x = chroma * (1 - Math.Abs(hue % 2 - 1)), m = .30;
            double r = 0, g = 0, b = 0;
            if (hue < 1) {
                r = chroma;
                g = x;
            } else if (hue < 2) {
                r = x;
                g = chroma;
            } else if (hue < 3) {
                g = chroma;
                b = x;
            } else if (hue < 4) {
                g = x;
                b = chroma;
            } else if (hue < 5) {
                r = x;
                b = chroma;
            } else {
                r = chroma;
                b = x;
            }
            var ink = new SolidColorBrush(Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255)));
            ink.Freeze();
            if (memberInkCache.Count >= 256)
                memberInkCache.Clear();
            memberInkCache[ownerId] = ink;
            return ink;
        }
        Brush OwnerInk(string ownerId) {
            return string.IsNullOrEmpty(ownerId) ? (string.IsNullOrEmpty(State.LocalPeerId) ? CachedBrush(State.RouteColor) : MemberInk(State.LocalPeerId)) : MemberInk(ownerId);
        }
        Brush RouteInk(string key) {
            if (string.IsNullOrEmpty(key))
                return OwnerInk(null);
            int sep = key.LastIndexOf(':');
            return OwnerInk(sep > 0 ? key.Substring(0, sep) : null);
        }
        void DrawRoutePlan(DrawingContext dc, SquadRoutePlan plan, Brush ink) {
            if (plan == null || plan.Legs == null || plan.Verified == null)
                return;
            int count = Math.Min(plan.Legs.Length, plan.Verified.Length);
            for (int i = 0; i < count; i++)
            if (plan.Legs[i] != null)
                DrawStreamedRoute(dc, plan.Legs[i], ink, plan.Verified[i]);
        }
        static string SharedMarkerLabel(SharedMapMarker marker) {
            int kind;
            string name = SquadState.IsPing(marker) && int.TryParse(marker.Id.AsSpan(8, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out kind) && kind >= 0 && kind <= SquadStatusKind.MaxKind ? SquadStatusKind.GetDisplayLabel(kind, Localization.Current == AppLanguage.Russian) : marker.Death ? Localization.Get("DeathName") : marker.Name;
            return (SquadState.IsStop(marker) ? Localization.Get(SquadState.IsShared(marker) ? "SquadRoute" : "SoloRoute") + " · " : marker.Id == SharedMapMarker.GpsId ? Localization.Get("SoloRoute") + " · " : "") + marker.Owner + " · " + (SquadState.IsStop(marker) ? (SquadState.StopIndex(marker) + 1) + ". " : "") + name;
        }
        SharedMapMarker SharedMarkerAt(DrawingPoint at) {
            if (State == null || State.SharedMarkers == null || State.SharedMarkers.Length == 0)
                return null;
            double maxR = 14 / MapSide(State, ActualWidth, ActualHeight, Compact), bestSq = maxR * maxR;
            SharedMapMarker best = null;
            for (int i = 0; i < State.SharedMarkers.Length; i++) {
                var m = State.SharedMarkers[i];
                double dSq = MarkerDistanceSq(new DrawingPoint((float)m.X, (float)m.Y), at);
                if (dSq <= bestSq) {
                    bestSq = dSq;
                    best = m;
                }
            }
            return best;
        }
        MapZone SharedDestination(DrawingPoint at) {
            var marker = SharedMarkerAt(at);
            return marker == null ? null : new MapZone { Name = SharedMarkerLabel(marker), Category = ZoneCategory.Custom, Layer = "Shared markers", Points = new[] { new DrawingPoint((float)marker.X, (float)marker.Y) }, Argb = System.Drawing.Color.Cyan.ToArgb() };
        }
        void NavigateTo(DrawingPoint at) {
            var destination = SharedDestination(at);
            if (destination != null && NavigateShared != null)
                NavigateShared(destination);
            else if (NavigateAt != null)
                NavigateAt(at);
        }
        internal WpfMapSurface() {
            mapLayers = new VisualCollection(this) { terrainLayer, trailLayer, annotationLayer, sharedLayer, liveLayer };
            FlowDirection = FlowDirection.LeftToRight;
            SystemTypography.ApplyClearType(this);
            ClipToBounds = true;
            Focusable = true;
            PreviewMouseRightButtonDown += (sender, args) => { if (BeginContextPointer(args.GetPosition(this))) args.Handled = true; };
            PreviewMouseRightButtonUp += (sender, args) => { if (EndContextPointer(args.GetPosition(this))) args.Handled = true; };
            MouseWheel += (s, e) => { if (ZoomRequested != null) ZoomRequested(e.Delta > 0 ? 1 : -1); e.Handled = true; };
            MouseLeftButtonDown += (s, e) => { if (Compact || State == null) return; BeginPointer(e.GetPosition(this)); CaptureMouse(); e.Handled = true; };
            MouseMove += (s, e) => { if (Compact) return; var pos = e.GetPosition(this); if (drag.HasValue && e.LeftButton != MouseButtonState.Pressed) CancelPointer(); else MovePointer(pos); UpdateHover(pos); };
            MouseLeave += (s, e) => { ToolTip = null; hoverText = null; if (cursorMapPoint.HasValue) { cursorMapPoint = null; RedrawLiveLayerOnly(); } };
            ToolTipService.SetInitialShowDelay(this, 250);
            ToolTipService.SetShowDuration(this, 30000);
            MouseLeftButtonUp += (s, e) => { if (!drag.HasValue) return; EndPointer(e.GetPosition(this)); ReleaseMouseCapture(); e.Handled = true; };
            LostMouseCapture += (s, e) => { CancelPointer(); contextPointer = null; };
            IsVisibleChanged += (s, e) => { if (!IsVisible) { CloseContextMenu(); CancelPointer(); if (!measureClosed) measurePoints.Clear(); EndCameraFrames(); } };
            Unloaded += (s, e) => { EndCameraFrames(); ResetPoiStream(); ClearTacticalLayers(); };
        }
        protected override void OnRender(DrawingContext dc) {
            base.OnRender(dc);
            UpdateTacticalLayers();
        }
        void DrawMap(DrawingContext dc) {
            DrawMapPass(dc, MapPass.All);
        }
        static Point ToScreen(DrawingPoint p, double left, double top, double side) {
            return new Point(left + p.X * side, top + p.Y * side);
        }
        static Point ToScreen(double x, double y, double left, double top, double side) {
            return new Point(left + x * side, top + y * side);
        }
        void DrawMapPass(DrawingContext dc, MapPass pass) {
            double w = ActualWidth, h = ActualHeight;
            if (w < 1 || h < 1)
                return;
            string clipShape = (Compact && State != null) ? State.Shape : "Rect";
            if (cachedClipGeo == null || cachedClipW != w || cachedClipH != h || cachedClipShape != clipShape) {
                cachedClipW = w;
                cachedClipH = h;
                cachedClipShape = clipShape;
                cachedClipGeo = clipShape == "Circle" ? (Geometry)new EllipseGeometry(new Point(w / 2, h / 2), Math.Min(w, h) / 2, Math.Min(w, h) / 2) : new RectangleGeometry(new Rect(0, 0, w, h), Compact ? 10 : 0, Compact ? 10 : 0);
                cachedClipGeo.Freeze();
            }
            dc.PushClip(cachedClipGeo);
            if (pass == MapPass.All || pass == MapPass.Terrain) {
                if (Compact && State != null && State.Opacity > 0) {
                    dc.PushOpacity(State.Opacity / 100.0);
                    dc.DrawRectangle(CachedBrush(255, 18, 26, 34), null, new Rect(0, 0, w, h));
                    dc.Pop();
                } else
                    dc.DrawRectangle(Compact ? Brushes.Transparent : tacticalHitSurface, null, new Rect(0, 0, w, h));
            }
            if (State == null) {
                dc.Pop();
                return;
            }
            Brush routeInk = CachedBrush(State.RouteColor);
            var playerInk = CachedBrush(State.PlayerColor);
            double side = MapSide(State, w, h, Compact);
            DrawingPoint centre = CameraCentre(State, w, h, Compact);
            double left = w / 2 - side * centre.X, top = h / 2 - side * centre.Y;
            double rotation = renderRotation = Rotation(State, Compact);
            double overscan = rotation == 0 ? 0 : Math.Max(w, h);
            var layerViewport = new Rect(-overscan, -overscan, w + 2 * overscan, h + 2 * overscan);
            if (rotation != 0) {
                if (cachedRotateTransform == null || cachedRotAngle != rotation || cachedRotW != w || cachedRotH != h) {
                    cachedRotAngle = rotation;
                    cachedRotW = w;
                    cachedRotH = h;
                    cachedRotateTransform = new RotateTransform(rotation, w / 2, h / 2);
                    cachedRotateTransform.Freeze();
                }
                dc.PushTransform(cachedRotateTransform);
            }
            bool worldClipped = !(left <= layerViewport.Left && top <= layerViewport.Top && left + side >= layerViewport.Right && top + side >= layerViewport.Bottom);
            if (worldClipped) {
                var visibleWorld = new Rect(left, top, side, side);
                visibleWorld.Intersect(layerViewport);
                if (cachedWorldClipGeo == null || cachedWorldClipRect != visibleWorld) {
                    cachedWorldClipRect = visibleWorld;
                    cachedWorldClipGeo = new RectangleGeometry(visibleWorld);
                    cachedWorldClipGeo.Freeze();
                }
                dc.PushClip(cachedWorldClipGeo);
            }
            if (pass == MapPass.All || pass == MapPass.Terrain) {
                if (State.Opacity > 0) {
                    dc.PushOpacity(State.Opacity / 100.0);
                    if (Terrain != null)
                        Terrain.DrawRotated(dc, w, h, left, top, side, CurrentDpiScaleX, rotation == 0 ? 0 : Math.Max(w, h), Compact);
                    else {
                        dc.PushTransform(FrozenMatrix(side / 1024, 0, 0, side / 1024, left, top));
                        if (BaseMap != null)
                            dc.DrawDrawing(BaseMap);
                        dc.Pop();
                    }
                    dc.Pop();
                }
                if (State.Grid || State.GridLabels) {
                    double gridAlpha = Math.Max(0.0, Math.Min(1.0, State.GridOpacity / 100.0));
                    var mapLightInk = CachedBrush(255, 248, 250, 252);
                    if (State.Grid && gridAlpha > 0.005) {
                        dc.PushOpacity(gridAlpha);
                        var gridPen = CachedPen(mapLightInk, 1.6);
                        for (int i = 1; i < 5; i++) {
                            double x = left + CalibratedGridX[i] * side, y = top + CalibratedGridY[i] * side;
                            if (x >= layerViewport.Left - 1 && x <= layerViewport.Right + 1)
                                dc.DrawLine(gridPen, new Point(x, Math.Max(top, layerViewport.Top - 1)), new Point(x, Math.Min(top + side, layerViewport.Bottom + 1)));
                            if (y >= layerViewport.Top - 1 && y <= layerViewport.Bottom + 1)
                                dc.DrawLine(gridPen, new Point(Math.Max(left, layerViewport.Left - 1), y), new Point(Math.Min(left + side, layerViewport.Right + 1), y));
                        }
                        dc.Pop();
                    }
                    if (State.GridLabels) {
                        double labelAlpha = Math.Max(0.45, gridAlpha);
                        dc.PushOpacity(labelAlpha);
                        double gSz = Math.Max(10, State.LabelSize);
                        var sectorBg = CachedBrush(215, 10, 14, 20);
                        var sectorBorder = CachedPen(CachedBrush(130, 148, 163, 184), 0.9);
                        double gDpi = CurrentDpiScaleX;
                        for (int row = 0; row < 5; row++)
                        for (int col = 0; col < 5; col++) {
                            double gx = left + CalibratedGridX[col] * side + 8, gy = top + CalibratedGridY[row] * side + 8;
                            if (gx >= layerViewport.Left - 40 && gx <= layerViewport.Right + 10 && gy >= layerViewport.Top - 30 && gy <= layerViewport.Bottom + 10) {
                                var ft = LabelText(GridSectorNames[row, col], gSz, mapLightInk, gDpi);
                                if (renderRotation != 0)
                                    dc.PushTransform(FrozenRotate(-renderRotation, gx, gy));
                                dc.DrawRoundedRectangle(sectorBg, sectorBorder, new Rect(gx - 5, gy - 1.5, ft.Width + 10, ft.Height + 3), 4, 4);
                                dc.DrawText(ft, new Point(gx, gy));
                                if (renderRotation != 0)
                                    dc.Pop();
                            }
                        }
                        dc.Pop();
                    }
                }
                if (State.Store != null && State.Store.MasterEnabled && State.Store.Habitats != null && State.Store.Habitats.Count > 0 && side > 0) {
                    var habWorldView = new Rect((layerViewport.Left - left) / side, (layerViewport.Top - top) / side, layerViewport.Width / side, layerViewport.Height / side);
                    bool habPushed = false;
                    for (int hi = State.Store.Habitats.Count - 1; hi >= 0; hi--) {
                        var habitat = State.Store.Habitats[hi];
                        if (habitat == null || habitat.Rings == null || !State.Store.IsHabitatEnabled(habitat))
                            continue;
                        var geometry = habitatGeometryCache.GetValue(habitat.Rings, WorldGeometry);
                        var bounds = geometryBoundsCache.GetValue(geometry, g => new GeometryBounds(g)).Value;
                        if (!bounds.IntersectsWith(habWorldView))
                            continue;
                        if (!habPushed) {
                            if (cachedTrailXform == null || cachedTrailSide != side || cachedTrailLeft != left || cachedTrailTop != top) {
                                cachedTrailSide = side;
                                cachedTrailLeft = left;
                                cachedTrailTop = top;
                                cachedTrailXform = new MatrixTransform(side, 0, 0, side, left, top);
                                cachedTrailXform.Freeze();
                            }
                            dc.PushTransform(cachedTrailXform);
                            habPushed = true;
                        }
                        var color = Color.FromRgb((byte)(habitat.Color >> 16), (byte)(habitat.Color >> 8), (byte)habitat.Color);
                        dc.DrawGeometry(CachedBrush(12, color.R, color.G, color.B), CachedPen(CachedBrush(100, color.R, color.G, color.B), .8 / side), geometry);
                    }
                    if (habPushed)
                        dc.Pop();
                }
            }
            if ((pass == MapPass.All || pass == MapPass.Trail) && State.History && State.Trail != null && State.Trail.Length > 1) {
                int tc = State.Trail.Length;
                if (cachedTrailGeo == null || !object.ReferenceEquals(cachedTrailSource, State.Trail) || cachedTrailLen != tc) {
                    var tg = new StreamGeometry();
                    using (var ctx = tg.Open()) {
                        ctx.BeginFigure(new Point(State.Trail[0].X, State.Trail[0].Y), false, false);
                        for (int ti = 1; ti < tc; ti++)
                            ctx.LineTo(new Point(State.Trail[ti].X, State.Trail[ti].Y), true, false);
                    }
                    tg.Freeze();
                    cachedTrailSource = State.Trail;
                    cachedTrailLen = tc;
                    cachedTrailGeo = tg;
                }
                if (cachedTrailXform == null || cachedTrailSide != side || cachedTrailLeft != left || cachedTrailTop != top) {
                    cachedTrailSide = side;
                    cachedTrailLeft = left;
                    cachedTrailTop = top;
                    cachedTrailXform = new MatrixTransform(side, 0, 0, side, left, top);
                    cachedTrailXform.Freeze();
                }
                dc.PushTransform(cachedTrailXform);
                int hArgb = State.HistoryColor;
                int glowArgb = (55 << 24) | (hArgb & 0xFFFFFF);
                dc.DrawGeometry(null, CachedPen(CachedBrush(glowArgb), 6.5 / Math.Max(1.0, side), true), cachedTrailGeo);
                dc.DrawGeometry(null, CachedPen(CachedBrush(hArgb), 2.0 / Math.Max(1.0, side), true), cachedTrailGeo);
                dc.Pop();
            }
            if (pass == MapPass.All || pass == MapPass.Annotations) {
                if ((State.Zones || State.Waypoints) && State.Saved != null) {
                    if (State.Zones) {
                        if (cachedZoneEntries == null || !object.ReferenceEquals(cachedZoneSource, State.Saved) || cachedZoneRev != State.ZoneRevision) {
                            var list = new List<ZoneBoundsEntry>();
                            for (int zi = 0; zi < State.Saved.Length; zi++) {
                                var zone = State.Saved[zi];
                                if (zone == null || zone.Points == null || zone.Points.Length <= 2)
                                    continue;
                                int fillArgb = zone.FillArgb ?? (((zone.IsFaction || zone.Category == ZoneCategory.Faction ? 75 : 40) << 24) | (zone.Argb & 0xFFFFFF));
                                var geometry = zoneGeometryCache.GetValue(zone.Points, points => WorldGeometry(new[] { points }));
                                var wb = geometryBoundsCache.GetValue(geometry, g => new GeometryBounds(g)).Value;
                                list.Add(new ZoneBoundsEntry { Geo = geometry, WorldBounds = wb, FillArgb = fillArgb, StrokeArgb = zone.Argb });
                            }
                            cachedZoneSource = State.Saved;
                            cachedZoneRev = State.ZoneRevision;
                            cachedZoneEntries = list.ToArray();
                        }
                        if (cachedZoneEntries.Length > 0 && side > 0) {
                            var worldView = new Rect((layerViewport.Left - left) / side, (layerViewport.Top - top) / side, layerViewport.Width / side, layerViewport.Height / side);
                            bool zonePushed = false;
                            for (int zi = 0; zi < cachedZoneEntries.Length; zi++) {
                                var entry = cachedZoneEntries[zi];
                                if (!entry.WorldBounds.IntersectsWith(worldView))
                                    continue;
                                if (!zonePushed) {
                                    if (cachedTrailXform == null || cachedTrailSide != side || cachedTrailLeft != left || cachedTrailTop != top) {
                                        cachedTrailSide = side;
                                        cachedTrailLeft = left;
                                        cachedTrailTop = top;
                                        cachedTrailXform = new MatrixTransform(side, 0, 0, side, left, top);
                                        cachedTrailXform.Freeze();
                                    }
                                    dc.PushTransform(cachedTrailXform);
                                    zonePushed = true;
                                }
                                dc.DrawGeometry(CachedBrush(entry.FillArgb), CachedPen(CachedBrush(entry.StrokeArgb), 1.8 / side), entry.Geo);
                            }
                            if (zonePushed)
                                dc.Pop();
                        }
                    }
                    for (int zi = 0; zi < State.Saved.Length; zi++) {
                        var zone = State.Saved[zi];
                        if (zone == null || zone.Points == null || zone.Points.Length == 0)
                            continue;
                        int plen = zone.Points.Length;
                        if (State.Zones && MapWindow.WpfCustomZone(zone) && plen < 3) {
                            var at = ToScreen(zone.Points[0], left, top, side);
                            if (VisibleMarker(at, w, h, rotation))
                                dc.DrawEllipse(CachedBrush(zone.Argb), CachedPen(MapMarkerBg, 1.5), at, 6, 6);
                        } else if (State.Waypoints && plen == 1 && !zone.IsDeathMarker && !MapWindow.WpfCustomZone(zone)) {
                            var at = ToScreen(zone.Centroid, left, top, side);
                            if (VisibleMarker(at, w, h, rotation))
                                dc.DrawEllipse(null, CachedPen(WpfFieldTheme.Cyan, 2), at, 5, 5);
                        }
                    }
                }
                if (State.Store != null && State.Store.MasterEnabled) {
                    if (!Compact)
                        DrawRetainedPois(dc, side, left, top, w, h);
                    else
                    foreach (var cluster in VisiblePoiClusters(State, w, h, Compact)) {
                        var at = ToScreen(cluster.Point, left, top, side);
                        var screen = RotatePoint(at, rotation, w, h);
                        if (screen.X < -14 || screen.Y < -14 || screen.X > w + 14 || screen.Y > h + 14)
                            continue;
                        ScumMapCategory category;
                        State.Store.CategoryById.TryGetValue(cluster.Members[0].CategoryId, out category);
                        DrawPoi(dc, at, category, cluster);
                    }

                }
                var mapLabelMajor = CachedBrush(255, 248, 250, 252);
                double minorAlpha = State.SmartLabels ? Math.Max(0.0, Math.Min(1.0, (State.Zoom - 1.15) / 1.35)) : 1.0;
                var mapLabelMinor = CachedBrush((byte)Math.Round(255 * Math.Max(0.45, minorAlpha)), 203, 213, 225);
                bool drawMinor = !State.SmartLabels || Compact || State.Zoom >= 1.20;
                if (!Compact || rotation == 0) {
                    var retained = RetainedLabels(w, h);
                    for (int li = 0; li < retained.Length; li++) {
                        var label = retained[li];
                        if (!label.Major && !drawMinor)
                            continue;
                        LabelScreen(dc, label.Text, label.At, label.Major ? mapLabelMajor : mapLabelMinor, Math.Max(8, State.LabelSize) + (label.Major ? 2 : 0));
                    }
                }
            }
            if (pass == MapPass.All || pass == MapPass.Live) {
                routeInk = RouteInk(State.ActiveSquadRoute);
                if (AviationModeEnabled)
                    DrawAviationRunways(dc, side, left, top, w, h, rotation);
                if (AviationModeEnabled && State.Target != null && State.Position != null) {
                    dc.DrawLine(CachedPen(AviationVectorBrush, 3.2, true), ToScreen(MapWindow.ToMap(State.Position), left, top, side), ToScreen(State.Target.Centroid, left, top, side));
                } else if (State.Route != null && State.Route.Success && State.Position != null && (State.SquadPlan == null || !State.SquadPlan.HasOrigin)) {
                    var path = State.Route.Polyline;
                    var routePen = CachedPen(routeInk, RouteThickness, true);
                    if (path != null && path.Length > 0)
                        dc.DrawLine(routePen, ToScreen(MapWindow.ToMap(State.Position), left, top, side), ToScreen(path[0], left, top, side));
                    else if (State.Target != null)
                        dc.DrawLine(routePen, ToScreen(MapWindow.ToMap(State.Position), left, top, side), ToScreen(State.Target.Centroid, left, top, side));
                } else if (State.Target != null && State.Position != null && (State.SquadPlan == null || !State.SquadPlan.HasOrigin)) {
                    dc.DrawLine(CachedPen(routeInk, RouteThickness, true, true), ToScreen(MapWindow.ToMap(State.Position), left, top, side), ToScreen(State.Target.Centroid, left, top, side));
                }
                if (State.Target != null) {
                    var p = ToScreen(State.Target.Centroid, left, top, side);
                    dc.DrawEllipse(null, CachedPen(routeInk, 3), p, 9, 9);
                    if (!Compact)
                        Label(dc, State.Target.DisplayName, p + new Vector(12, -30), routeInk, 13);
                }
                if (draggingPreviewPoint.HasValue && !Compact) {
                    var dp = ToScreen(draggingPreviewPoint.Value, left, top, side);
                    var rbPen = CachedPen(WpfFieldTheme.Amber, 2.2, true, false, true);
                    if (draggingSingleTarget && State.Position != null) {
                        dc.DrawLine(rbPen, ToScreen(MapWindow.ToMap(State.Position), left, top, side), dp);
                    } else if (draggingStopId != null && State.LocalRoute != null && State.LocalRoute.Length > 0) {
                        int dIdx = -1;
                        for (int ri = 0; ri < State.LocalRoute.Length; ri++)
                        if (State.LocalRoute[ri].Id == draggingStopId) {
                            dIdx = ri;
                            break;
                        }
                        if (dIdx >= 0) {
                            if (dIdx > 0)
                                dc.DrawLine(rbPen, ToScreen(State.LocalRoute[dIdx - 1].X, State.LocalRoute[dIdx - 1].Y, left, top, side), dp);
                            else if (State.Position != null)
                                dc.DrawLine(rbPen, ToScreen(MapWindow.ToMap(State.Position), left, top, side), dp);
                            if (dIdx + 1 < State.LocalRoute.Length)
                                dc.DrawLine(rbPen, dp, ToScreen(State.LocalRoute[dIdx + 1].X, State.LocalRoute[dIdx + 1].Y, left, top, side));
                        }
                    }
                    dc.DrawEllipse(null, CachedPen(WpfFieldTheme.Amber, 2.5, false, false, true), dp, 12, 12);
                }
                if (State.Position != null) {
                    var playerScreen = ToScreen(MapWindow.ToMap(State.Position), left, top, side);
                    DrawPlayerPulseAndVector(dc, playerScreen, State.Position.Yaw, side);
                    DrawPlayer(dc, playerScreen, State.Position.Yaw, string.IsNullOrEmpty(State.LocalPeerId) ? playerInk : MemberInk(State.LocalPeerId), State.Heading, true);
                }
                DateTime peerNowUtc = DateTime.UtcNow;
                DrawingPoint? localMapPt = State.Position == null ? (DrawingPoint?)null : MapWindow.ToMap(State.Position);
                long peerPulseElapsed = pulseClock.ElapsedMilliseconds - peerPulseStartMs;
                if (State.Peers != null)
                foreach (var peer in State.Peers) {
                    if (peer.Position == null)
                        continue;
                    var peerMapPt = MapWindow.ToMap(peer.Position);
                    var p = ToScreen(peerMapPt, left, top, side);
                    if (!VisibleMarker(p, w, h, rotation))
                        continue;
                    var ink = MemberInk(peer.Id);
                    bool stalePeer = peer.Updated != DateTime.MinValue && (peerNowUtc - peer.Updated).TotalSeconds > 15.0;
                    if (stalePeer)
                        dc.PushOpacity(0.48);
                    if (peer.Id == pulsePeerId && peerPulseElapsed >= 0 && peerPulseElapsed < 1400) {
                        double pt = peerPulseElapsed / 1400.0;
                        dc.PushOpacity(Math.Max(0.08, 1.0 - pt));
                        dc.DrawEllipse(null, CachedPen(ink, 2.6), p, 10.0 + pt * (Compact ? 32.0 : 54.0), 10.0 + pt * (Compact ? 32.0 : 54.0));
                        dc.Pop();
                    }
                    DrawPlayer(dc, p, peer.Position.Yaw, ink, State.Heading);
                    double pSpd;
                    PeerSpeedKmh.TryGetValue(peer.Id, out pSpd);
                    bool isLead = peer.Id == SquadLeadPeerId;
                    string prefix = (isLead ? "★ " : "") + (pSpd >= 110.0 ? "✈ " : (pSpd >= 25.0 ? "🚗 " : ""));
                    string peerLbl = prefix + peer.Name;
                    if ((AviationModeEnabled || pSpd >= 110.0) && !double.IsNaN(peer.Position.Z))
                        peerLbl += " · " + (int)Math.Round(peer.Position.Z / 100.0) + (Localization.Current == AppLanguage.Russian ? "м" : "m");
                    if (!Compact && localMapPt.HasValue)
                        peerLbl += " · " + DestinationSearch.Distance(DestinationSearch.Metres(localMapPt.Value, peerMapPt));
                    if (stalePeer)
                        peerLbl += " ⚠";
                    DrawDynamicPeerLabel(dc, "peer:" + peer.Id, peerLbl, p + new Vector(11, -18), isLead ? WpfFieldTheme.Amber : ink, isLead ? 13 : 12);
                    if (stalePeer)
                        dc.Pop();
                }
            }
            if (pass == MapPass.All || pass == MapPass.Shared) {
                RouteVerticesDrawn = 0;
                string ownKey = State.LocalRoute.Length > 0 ? SquadState.RouteKey(State.LocalRoute[0]) : null;
                if (AviationModeEnabled) {
                    var activeFlightPen = CachedPen(AviationVectorBrush, 2.8, true, false, true);
                    var passedFlightPen = CachedPen(CachedBrush(110, 56, 189, 248), 2.0, true, false, true);
                    if (State.SharedMarkers != null && State.SharedMarkers.Length > 0)
                    foreach (var route in SquadState.Routes(State.SharedMarkers)) {
                        if (route.Length < 2)
                            continue;
                        string rKey = SquadState.RouteKey(route[0]);
                        bool isActive = rKey == State.ActiveSquadRoute;
                        var legPen = isActive ? activeFlightPen : CachedPen(OwnerInk(route[0].OwnerId), 2.2, true, false, true);
                        for (int fi = 1; fi < route.Length; fi++) {
                            bool passed = isActive && ActiveRouteStopIndex > 0 && fi <= ActiveRouteStopIndex;
                            dc.DrawLine(passed ? passedFlightPen : legPen,
                                ToScreen(route[fi - 1].X, route[fi - 1].Y, left, top, side),
                                ToScreen(route[fi].X, route[fi].Y, left, top, side));
                        }
                    }
                } else {
                    if (State.Route != null && State.Route.Success && State.Position != null && State.Route.Polyline != null && State.Route.Polyline.Length > 0 && (State.SquadPlan == null || !State.SquadPlan.HasOrigin)) {
                        var path = State.Route.Polyline;
                        var ink = RouteInk(State.ActiveSquadRoute);
                        DrawStreamedRoute(dc, path, ink, true);
                        if (State.Target != null)
                            dc.DrawLine(CachedPen(ink, RouteThickness, true), ToScreen(path[path.Length - 1], left, top, side), ToScreen(State.Target.Centroid, left, top, side));
                    }
                    if (State.OwnRoutePlan != null && ownKey != State.ActiveSquadRoute)
                        DrawRoutePlan(dc, State.OwnRoutePlan, OwnerInk(null));
                    if (State.SquadPlan != null)
                        DrawRoutePlan(dc, State.SquadPlan, RouteInk(State.ActiveSquadRoute));
                }
                if (!AviationModeEnabled && State.OtherRoutePlans != null)
                for (int pi = 0; pi < State.OtherRoutePlans.Length; pi++) {
                    var plan = State.OtherRoutePlans[pi];
                    if (plan != null && plan.Plan != null && plan.Key != State.ActiveSquadRoute)
                        DrawRoutePlan(dc, plan.Plan, RouteInk(plan.Key));
                }
                if (!AviationModeEnabled && State.SharedMarkers != null && State.SharedMarkers.Length > 0)
                foreach (var route in SquadState.Routes(State.SharedMarkers)) {
                    string key = SquadState.RouteKey(route[0]);
                    bool otherReady = false;
                    if (State.OtherRoutePlans != null)
                    for (int oi = 0; oi < State.OtherRoutePlans.Length; oi++)
                    if (State.OtherRoutePlans[oi].Key == key) {
                        otherReady = true;
                        break;
                    }
                    bool ready = key == ownKey && State.OwnRoutePlan != null || key == State.ActiveSquadRoute && State.SquadPlan != null || otherReady;
                    if (!ready) {
                        var dotPen = CachedPen(OwnerInk(route[0].OwnerId), RouteThickness, false, true);
                        for (int i = 1; i < route.Length; i++)
                            dc.DrawLine(dotPen, ToScreen(route[i - 1].X, route[i - 1].Y, left, top, side), ToScreen(route[i].X, route[i].Y, left, top, side));
                    }
                }
                DateTime pingNowUtc = DateTime.UtcNow;
                if (pingFirstSeenUtc.Count > 128) {
                    expiredPingsBuffer.Clear();
                    foreach (var kv in pingFirstSeenUtc)
                    if ((pingNowUtc - kv.Value).TotalSeconds > PingTtlSeconds)
                        expiredPingsBuffer.Add(kv.Key);
                    for (int ei = 0; ei < expiredPingsBuffer.Count; ei++)
                        pingFirstSeenUtc.Remove(expiredPingsBuffer[ei]);
                    expiredPingsBuffer.Clear();
                    if (pingFirstSeenUtc.Count > 256)
                        pingFirstSeenUtc.Clear();
                }
                if (State.SharedMarkers != null)
                foreach (var marker in State.SharedMarkers) {
                    double pingOpacity = 1.0;
                    int pingRemainingSec = -1;
                    if (SquadState.IsPing(marker)) {
                        DateTime firstSeen;
                        if (!pingFirstSeenUtc.TryGetValue(marker.Id, out firstSeen)) {
                            firstSeen = pingNowUtc;
                            pingFirstSeenUtc[marker.Id] = firstSeen;
                        }
                        double age = (pingNowUtc - firstSeen).TotalSeconds;
                        if (age > PingTtlSeconds)
                            continue;
                        pingRemainingSec = Math.Max(1, (int)Math.Ceiling(PingTtlSeconds - age));
                        if (age > PingTtlSeconds - 15.0)
                            pingOpacity = Math.Max(0.15, (PingTtlSeconds - age) / 15.0);
                    } else if (SquadState.IsStop(marker) && ActiveRouteStopIndex > 0 && State.ActiveSquadRoute != null && SquadState.RouteKey(marker) == State.ActiveSquadRoute && SquadState.StopIndex(marker) < ActiveRouteStopIndex) {
                        pingOpacity = 0.40;
                    }
                    var at = ToScreen(marker.X, marker.Y, left, top, side);
                    if (!VisibleMarker(at, w, h, rotation))
                        continue;
                    var ink = marker.Death ? DangerBrush : OwnerInk(marker.OwnerId);
                    if (SquadState.IsPing(marker)) {
                        string pk = marker.Id.Substring(8, 2);
                        ink = (pk == "00" || pk == "03" || pk == "05") ? DangerBrush : (pk == "01" || pk == "04") ? WpfFieldTheme.Amber : WpfFieldTheme.Cyan;
                    }
                    var markerPen = CachedPen(ink, 2);
                    if (pingOpacity < 0.99)
                        dc.PushOpacity(pingOpacity);
                    if (marker.Death) {
                        dc.DrawRoundedRectangle(MapMarkerBg, markerPen, new Rect(at.X - 7, at.Y - 10, 14, 19), 3, 3);
                        dc.DrawLine(markerPen, at + new Vector(0, -5), at + new Vector(0, 4));
                        dc.DrawLine(markerPen, at + new Vector(-4, -1), at + new Vector(4, -1));
                    } else if (SquadState.IsStop(marker)) {
                        double r = Compact ? 7.5 : 9.5;
                        dc.DrawEllipse(MapMarkerBg, markerPen, at, r, r);
                        string num = (SquadState.StopIndex(marker) + 1).ToString(CultureInfo.InvariantCulture);
                        var numFt = LabelText(num, Compact ? 9.5 : 11.0, ink, CurrentDpiScaleX);
                        if (renderRotation != 0)
                            dc.PushTransform(FrozenRotate(-renderRotation, at.X, at.Y));
                        dc.DrawText(numFt, new Point(at.X - numFt.Width / 2.0, at.Y - numFt.Height / 2.0));
                        if (renderRotation != 0)
                            dc.Pop();
                    } else {
                        dc.PushTransform(FrozenTranslate(at.X, at.Y));
                        dc.DrawGeometry(MapMarkerBg, markerPen, SharedDiamondGeometry);
                        dc.Pop();
                    }
                    // Route names belong on the tactical map; the HUD keeps their symbols and guidance.
                    if (!Compact || !SquadState.IsStop(marker) && marker.Id != SharedMapMarker.GpsId) {
                        string mLabel = SharedMarkerLabel(marker);
                        if (!Compact && pingRemainingSec > 0) {
                            mLabel += " · " + pingRemainingSec.ToString(CultureInfo.InvariantCulture) + "s";
                            DrawDynamicPeerLabel(dc, "ping:" + marker.Id, mLabel, at + new Vector(11, -19), ink, 12);
                        } else {
                            Label(dc, mLabel, at + new Vector(11, -19), ink, 12);
                        }
                    }
                    if (pingOpacity < 0.99)
                        dc.Pop();
                }
                if (State.Saved != null)
                for (int di = 0; di < State.Saved.Length; di++) {
                    var death = State.Saved[di];
                    if (death == null || !death.IsDeathMarker || death.Points == null || death.Points.Length != 1)
                        continue;
                    var p = ToScreen(death.Centroid, left, top, side);
                    if (!VisibleMarker(p, w, h, rotation))
                        continue;
                    var ink = DangerBrush;
                    var deathPen = CachedPen(ink, 2);
                    dc.DrawRoundedRectangle(MapMarkerBg, deathPen, new Rect(p.X - 8, p.Y - 11, 16, 20), 4, 4);
                    dc.DrawLine(deathPen, new Point(p.X, p.Y - 6), new Point(p.X, p.Y + 4));
                    dc.DrawLine(deathPen, new Point(p.X - 4, p.Y - 2), new Point(p.X + 4, p.Y - 2));
                    Label(dc, death.DisplayName, p + new Vector(12, -21), ink, Compact ? 11 : 13);
                }
            }
            if (worldClipped) dc.Pop(); // Map bounds: edge guidance and compass belong to the viewport, including ocean/transparent margins.
            if ((pass == MapPass.All || pass == MapPass.Live) && State.Compass && Compact && State.Shape == "Circle") {
                double cx = w / 2.0, cy = h / 2.0, outerR = Math.Min(w, h) / 2.0 - 3.0;
                var tickPen = CachedPen(WpfFieldTheme.Muted, 1.1);
                for (int ti = 0; ti < 8; ti++) {
                    double cos = CompassTickCos[ti], sin = CompassTickSin[ti];
                    dc.DrawLine(tickPen, new Point(cx + cos * (outerR - 5.0), cy + sin * (outerR - 5.0)), new Point(cx + cos * outerR, cy + sin * outerR));
                }
            }
            if (rotation != 0)
                dc.Pop(); // Map rotation.
            if (Compact && rotation != 0 && (pass == MapPass.All || pass == MapPass.Annotations)) {
                var mapLabelMajor = CachedBrush(255, 248, 250, 252);
                var mapLabelMinor = CachedBrush(255, 203, 213, 225);
                var retained = RetainedLabels(w, h);
                for (int li = 0; li < retained.Length; li++) {
                    var label = retained[li];
                    LabelScreen(dc, label.Text, label.At, label.Major ? mapLabelMajor : mapLabelMinor, Math.Max(8, State.LabelSize) + (label.Major ? 2 : 0));
                }
            }
            if ((pass == MapPass.All || pass == MapPass.Live) && State.Compass) {
                double compassRadius = Math.Min(w, h) / 2.0 - 15.0, cx = w / 2.0, cy = h / 2.0, fontSize = Compact ? 11 : 13, cDpi = CurrentDpiScaleX;
                var compassLightInk = CachedBrush(255, 241, 245, 249);
                double rad = rotation * Math.PI / 180.0, rc = rotation == 0 ? 1.0 : Math.Cos(rad), rs = rotation == 0 ? 0.0 : Math.Sin(rad);
                var ftN = LabelText("N", fontSize, compassNorthInk, cDpi);
                var ftS = LabelText("S", fontSize, compassLightInk, cDpi);
                var ftW = LabelText("W", fontSize, compassLightInk, cDpi);
                var ftE = LabelText("E", fontSize, compassLightInk, cDpi);
                double nx = cx + compassRadius * rs, ny = cy - compassRadius * rc;
                double sx = cx - compassRadius * rs, sy = cy + compassRadius * rc;
                double wx = cx - compassRadius * rc, wy = cy - compassRadius * rs;
                double ex = cx + compassRadius * rc, ey = cy + compassRadius * rs;
                dc.DrawRoundedRectangle(MapMarkerBg, null, new Rect(nx - ftN.Width / 2.0 - 4, ny - ftN.Height / 2.0 - 1, ftN.Width + 8, ftN.Height + 2), 3, 3);
                dc.DrawText(ftN, new Point(nx - ftN.Width / 2.0, ny - ftN.Height / 2.0));
                dc.DrawRoundedRectangle(MapMarkerBg, null, new Rect(sx - ftS.Width / 2.0 - 4, sy - ftS.Height / 2.0 - 1, ftS.Width + 8, ftS.Height + 2), 3, 3);
                dc.DrawText(ftS, new Point(sx - ftS.Width / 2.0, sy - ftS.Height / 2.0));
                dc.DrawRoundedRectangle(MapMarkerBg, null, new Rect(wx - ftW.Width / 2.0 - 4, wy - ftW.Height / 2.0 - 1, ftW.Width + 8, ftW.Height + 2), 3, 3);
                dc.DrawText(ftW, new Point(wx - ftW.Width / 2.0, wy - ftW.Height / 2.0));
                dc.DrawRoundedRectangle(MapMarkerBg, null, new Rect(ex - ftE.Width / 2.0 - 4, ey - ftE.Height / 2.0 - 1, ftE.Width + 8, ftE.Height + 2), 3, 3);
                dc.DrawText(ftE, new Point(ex - ftE.Width / 2.0, ey - ftE.Height / 2.0));
            }
            if (pass == MapPass.All || pass == MapPass.Live) {
                DrawDestinationEdge(dc, w, h, left, top, side, rotation, routeInk);
                DrawPeerEdges(dc, w, h, left, top, side, rotation);
                if (!Compact)
                    DrawRulerOverlay(dc, left, top, side);
                DrawScaleBar(dc, side, w, h);
                if (!Compact)
                    DrawCursorReadout(dc, w, h);
                if (DebugProfilerEnabled)
                    DrawDebugProfiler(dc, w, h);
            }
            dc.Pop();
        }
    }
}
