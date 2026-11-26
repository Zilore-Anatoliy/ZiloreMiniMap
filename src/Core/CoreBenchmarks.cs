#nullable enable
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;

namespace ZiloreMiniMap {
    internal sealed class BenchmarkSummary {
        internal double AvgFrameMs { get; init; }
        internal double P99FrameMs { get; init; }
        internal double TotalMs { get; init; }
        internal int Iterations { get; init; }
        internal int Gc0Delta { get; init; }
        internal int Gc1Delta { get; init; }
        internal int Checksum { get; init; }

        internal string FormatLine() {
            return string.Format(
                CultureInfo.InvariantCulture,
                "BENCH_RESULT avg_ms={0:F3} p99_ms={1:F3} total_ms={2:F1} iters={3} gc0={4} gc1={5} checksum={6}",
                AvgFrameMs, P99FrameMs, TotalMs, Iterations, Gc0Delta, Gc1Delta, Checksum);
        }
    }

    /// <summary>
    /// Deterministic headless runtime micro-benchmark for release quality gates (-Bench).
    /// Measures real hot-path latency across POI spatial queries, smooth auto-zoom clustering,
    /// road route tracking/turn projection, ScumMap lookups, and zone classification.
    /// </summary>
    internal static class CoreBenchmarks {
        internal static BenchmarkSummary RunAll(int frames = 60) {
            int count = Math.Max(16, Math.Min(240, frames));

            // Prepare realistic synthetic island workload (1,200 POIs + 300-segment route + zones)
            var store = new ScumMapStore { MasterEnabled = true };
            store.CategoryById[1] = new ScumMapCategory { Id = 1, Section = "Bunkers", Name = "Bunkers", DefaultEnabled = true };
            store.CategoryById[10] = new ScumMapCategory { Id = 10, Section = "Services", Name = "Gas Stations", DefaultEnabled = true };
            store.CategoryById[27] = new ScumMapCategory { Id = 27, Section = "Locations", Name = "Towns", DefaultEnabled = true };
            store.Markers.Add(new ScumMapMarker { Id = 1, CategoryId = 27, X = 0.40f, Y = 0.40f, Title = "Samobor" });
            store.Markers.Add(new ScumMapMarker { Id = 2, CategoryId = 27, X = 0.65f, Y = 0.35f, Title = "Novigrad" });
            for (int i = 0; i < 1198; i++) {
                float mx = 0.08f + (i % 40) * 0.021f;
                float my = 0.08f + (i / 40) * 0.026f;
                int cid = (i % 9 == 0) ? 1 : 10;
                store.Markers.Add(new ScumMapMarker { Id = i + 10, CategoryId = cid, X = mx, Y = my, Title = cid == 1 ? "Bunker" : "Gas Stations" });
            }
            store.IndexSettlements();

            var poly = new PointF[300];
            for (int i = 0; i <= 200; i++) poly[i] = new PointF(0.20f + i * 0.001f, 0.50f);
            for (int i = 201; i < 300; i++) poly[i] = new PointF(0.40f, 0.50f + (i - 200) * 0.001f);
            var route = new RoadRoute {
                Success = true,
                PlayerPoint = poly[0],
                TargetPoint = poly[299],
                Polyline = poly,
                JunctionIndices = new[] { 200 },
                TotalDistanceMeters = 4560.0,
                EstimatedSeconds = 240.0,
                ExitDistanceMeters = 12.0
            };

            var sampleZone = new MapZone {
                Name = "B3 Secret Bunker",
                Argb = -1,
                Points = new[] { new PointF(0.2f, 0.2f), new PointF(0.4f, 0.2f), new PointF(0.4f, 0.4f), new PointF(0.2f, 0.4f) }
            };

            var mapState = new WpfMapState {
                Store = store,
                Zoom = 8.0f,
                Pan = new PointF(0.40f, 0.40f),
                PoiClustering = true,
                Labels = true,
                LabelSize = 12,
                Saved = new[] { sampleZone }
            };

            // Warm up JIT & caches once before measuring steady-state frame latency
            _ = WpfMapSurface.VisiblePoiClusters(mapState, 240, 240, true);
            _ = WpfMapSurface.SelectLabels(mapState, 1280, 720, false, 1.5);
            _ = RoadRouter.RemainingDistanceMeters(new PointF(0.35f, 0.50f), route);
            _ = RoadRouter.GetNextTurn(new PointF(0.39f, 0.50f), route);
            _ = store.GetMarkerDisplayName(store.Markers[5]);

            int gc0Before = GC.CollectionCount(0);
            int gc1Before = GC.CollectionCount(1);
            var samples = new double[count];
            var totalWatch = Stopwatch.StartNew();
            var frameWatch = new Stopwatch();
            int checksum = 0;

            for (int f = 0; f < count; f++) {
                frameWatch.Restart();

                // 1. Smooth compact auto-zoom POI spatial query
                mapState.Zoom = 8.0f + (f % 30) * 0.004f;
                foreach (var cl in WpfMapSurface.VisiblePoiClusters(mapState, 240, 240, true)) {
                    checksum += cl.Members.Count;
                }

                // 2. O(1) road remaining distance + next turn projection
                float px = 0.25f + (f % 140) * 0.001f;
                var probe = new PointF(px, 0.50f);
                double rem = RoadRouter.RemainingDistanceMeters(probe, route);
                var turn = RoadRouter.GetNextTurn(probe, route);
                checksum += (int)rem + (turn.Valid ? 1 : 0);

                // 3. Cached marker display name & zone centroid/classification
                for (int k = 0; k < 16; k++) {
                    var mk = store.Markers[(f * 13 + k) % store.Markers.Count];
                    checksum += store.GetMarkerDisplayName(mk).Length;
                }
                var c = sampleZone.Centroid;
                checksum += (int)(c.X * 100f) + (int)ZoneStore.Classify(sampleZone);

                frameWatch.Stop();
                samples[f] = frameWatch.Elapsed.TotalMilliseconds;
            }

            totalWatch.Stop();
            int gc0Delta = GC.CollectionCount(0) - gc0Before;
            int gc1Delta = GC.CollectionCount(1) - gc1Before;

            double sum = 0.0;
            for (int i = 0; i < count; i++) sum += samples[i];
            Array.Sort(samples);
            int p99Idx = Math.Min(count - 1, (int)Math.Ceiling(count * 0.99) - 1);

            return new BenchmarkSummary {
                AvgFrameMs = sum / count,
                P99FrameMs = samples[Math.Max(0, p99Idx)],
                TotalMs = totalWatch.Elapsed.TotalMilliseconds,
                Iterations = count,
                Gc0Delta = gc0Delta,
                Gc1Delta = gc1Delta,
                Checksum = checksum
            };
        }
    }
}
