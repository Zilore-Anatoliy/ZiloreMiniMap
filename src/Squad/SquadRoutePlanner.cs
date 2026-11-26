#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZiloreMiniMap {
    internal sealed class SquadRoutePlan {
        internal int Revision;
        internal PointF[][] Legs = Array.Empty<PointF[]>();
        internal bool[] Verified = Array.Empty<bool>();
        internal double Distance, EstimatedSeconds;
        internal RoadRouteMode Mode;
        internal bool HasOrigin;

        internal int MissingRoads {
            get {
                if (Verified == null) return 0;
                int c = 0;
                for (int i = 0; i < Verified.Length; i++) if (!Verified[i]) c++;
                return c;
            }
        }
    }

    internal sealed class SquadRouteDrawing {
        internal string Key = "";
        internal string Name = "";
        internal bool Shared;
        internal SquadRoutePlan? Plan;
    }

    // A single bounded worker coalesces edits and abandons obsolete work between
    // legs. All road searches run away from the WPF dispatcher.
    internal sealed class SquadRoutePlanner : IDisposable {
        sealed class Request {
            internal int Revision;
            internal RoadRouteMode Mode;
            internal PointF? Origin;
            internal PointF[] Stops = Array.Empty<PointF>();
        }

        readonly object gate = new object();
        readonly Func<PointF, PointF, RoadRouteMode, RoadRoute?> calculate;
        static readonly SemaphoreSlim calculations = new SemaphoreSlim(2);
        static readonly object legCacheGate = new object();
        static readonly Dictionary<string, RoadRoute> legCache = new Dictionary<string, RoadRoute>(StringComparer.Ordinal);
        static readonly Queue<string> legCacheOrder = new Queue<string>();

        static SquadRoutePlanner() { RoadRouter.OnAvoidZonesChanged = ClearLegCache; }
        internal static void ClearLegCache() { lock (legCacheGate) { legCache.Clear(); legCacheOrder.Clear(); } }

        static string LegCacheKey(PointF a, PointF b, RoadRouteMode mode) {
            int ax = (int)Math.Round(a.X * 10000), ay = (int)Math.Round(a.Y * 10000);
            int bx = (int)Math.Round(b.X * 10000), by = (int)Math.Round(b.Y * 10000);
            return ((int)mode) + "|" + ax + "," + ay + ">" + bx + "," + by;
        }

        int revision;
        bool worker, disposed, busy;
        Request? pending;
        SquadRoutePlan? plan;

        internal SquadRoutePlanner(Func<PointF, PointF, RoadRoute?> calculate) : this((a, b, mode) => calculate(a, b)) { }
        internal SquadRoutePlanner(Func<PointF, PointF, RoadRouteMode, RoadRoute?> calculate) {
            ArgumentNullException.ThrowIfNull(calculate);
            this.calculate = calculate;
        }

        internal bool Busy { get { lock (gate) return busy; } }
        internal SquadRoutePlan? Plan { get { lock (gate) return plan; } }

        static bool Valid(PointF p) { return !float.IsNaN(p.X) && !float.IsNaN(p.Y) && p.X >= 0 && p.X <= 1 && p.Y >= 0 && p.Y <= 1; }

        internal void Update(PointF? origin, PointF[] stops, RoadRouteMode mode = RoadRouteMode.Shortest) {
            if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            if (stops == null || stops.Length < 1 || stops.Length > 16 || stops.Any(p => !Valid(p)) || (origin.HasValue && !Valid(origin.Value))) {
                throw new ArgumentException("Invalid route stops or origin.", nameof(stops));
            }
            lock (gate) {
                if (disposed) return;
                pending = new Request { Revision = ++revision, Mode = mode, Origin = origin, Stops = (PointF[])stops.Clone() };
                busy = true;
                if (!worker) { worker = true; Task.Run((Func<Task>)Work); }
            }
        }

        bool Current(Request request) { lock (gate) return !disposed && request.Revision == revision; }

        RoadRoute? CalculateBackground(PointF from, PointF to, RoadRouteMode mode) {
            string key = LegCacheKey(from, to, mode);
            lock (legCacheGate) {
                if (legCache.TryGetValue(key, out var cached)) return cached;
            }
            var thread = Thread.CurrentThread;
            var previous = thread.Priority;
            RoadRoute? result;
            try {
                thread.Priority = ThreadPriority.BelowNormal;
                result = calculate(from, to, mode);
            } finally {
                thread.Priority = previous;
            }
            if (result != null && result.Success && RoadRouter.Instance.IsLoaded) {
                lock (legCacheGate) {
                    if (!legCache.ContainsKey(key)) {
                        if (legCache.Count >= 256) legCache.Remove(legCacheOrder.Dequeue());
                        legCache[key] = result;
                        legCacheOrder.Enqueue(key);
                    }
                }
            }
            return result;
        }

        async Task Work() {
            while (true) {
                Request request;
                lock (gate) {
                    if (disposed || pending == null) { worker = false; return; }
                    request = pending;
                    pending = null;
                }
                await Task.Delay(60);
                if (!Current(request)) continue;
                var legs = new List<PointF[]>();
                var verified = new List<bool>();
                double distance = 0, seconds = 0;
                PointF from = request.Origin ?? request.Stops[0];
                int first = request.Origin.HasValue ? 0 : 1;
                bool isAir = WpfMapSurface.AviationModeEnabled;
                for (int i = first; i < request.Stops.Length; i++) {
                    if (!Current(request)) break;
                    PointF to = request.Stops[i];
                    RoadRoute? route = null;
                    bool isWaterLeg = !isAir && (RoadRouter.Instance.IsWaterAt(from) || RoadRouter.Instance.IsWaterAt(to));
                    if (!isAir && !isWaterLeg) {
                        await calculations.WaitAsync();
                        try {
                            if (Current(request)) route = await Task.Run(() => CalculateBackground(from, to, request.Mode));
                        } catch (Exception ex) {
                            Program.LogException("SquadRouteLeg", ex);
                        } finally {
                            calculations.Release();
                        }
                        if (!Current(request)) break;
                    }
                    bool good = route != null && route.Success && route.Polyline != null && route.Polyline.Length > 0;
                    if (good && route != null && route.Polyline != null) {
                        for (int pi = 0; pi < route.Polyline.Length; pi++) {
                            if (!Valid(route.Polyline[pi])) { good = false; break; }
                        }
                    }
                    if (good && route != null && route.Polyline != null) {
                        var poly = route.Polyline;
                        var combined = new PointF[poly.Length + 2];
                        combined[0] = from;
                        Array.Copy(poly, 0, combined, 1, poly.Length);
                        combined[poly.Length + 1] = to;
                        legs.Add(combined);
                    } else {
                        legs.Add(new[] { from, to });
                    }
                    verified.Add(good || isAir);
                    double directDist = RoadRouter.DistanceMeters(from, to);
                    double legDist = (good && route != null && !double.IsNaN(route.TotalDistanceMeters) && !double.IsInfinity(route.TotalDistanceMeters) && route.TotalDistanceMeters >= 0)
                        ? route.TotalDistanceMeters : directDist;
                    distance += legDist;
                    if (good && route != null && route.EstimatedSeconds > 0) seconds += route.EstimatedSeconds;
                    else seconds += legDist / (isAir ? 44.4 : (isWaterLeg ? 11.1 : 4.2));
                    from = to;
                }
                lock (gate) {
                    if (!disposed && request.Revision == revision) {
                        plan = new SquadRoutePlan {
                            Revision = request.Revision,
                            Legs = legs.ToArray(),
                            Verified = verified.ToArray(),
                            Distance = distance,
                            EstimatedSeconds = seconds,
                            Mode = request.Mode,
                            HasOrigin = request.Origin.HasValue
                        };
                        busy = false;
                    }
                }
            }
        }

        internal void Clear() { lock (gate) { revision++; pending = null; plan = null; busy = false; } }
        public void Dispose() { lock (gate) { disposed = true; revision++; pending = null; plan = null; busy = false; } }
    }
}
