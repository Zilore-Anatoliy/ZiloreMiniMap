#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ZiloreMiniMap {
    internal static class VoicePacks {
        internal const string DefaultName = "Lyan (Female US)";
        internal const string RussianName = "Mira (Russian)";
        internal const string GermanName = "Mila (German)";
        internal const string SpanishName = "Isabel (Spanish Argentine)";
        internal const string FinnishName = "Noora (Suomi)";
        internal const string LithuanianName = "Ona (Lietuvių)";
        internal const string SwahiliName = "Zuri (Kiswahili)";
        internal static readonly string[] BundledNames = { DefaultName, RussianName, GermanName, SpanishName, FinnishName, LithuanianName, SwahiliName };

        static bool DistanceOnly(string? name) {
            if (string.IsNullOrEmpty(name)) return false;
            return name == RussianName || name == GermanName || name == FinnishName || name == LithuanianName || name == SwahiliName
                || name.Contains("Russian", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Русский", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Alice", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Алиса", StringComparison.OrdinalIgnoreCase);
        }

        internal static string[] RequiredClips(string? name) { return Clips.Where(clip => clip != "11_in.mp3" || !DistanceOnly(name)).ToArray(); }

        internal static string[] Compose(string? voiceName, IEnumerable<string> clips) {
            return clips.Where(clip => !DistanceOnly(voiceName) || clip != "11_in.mp3").ToArray();
        }

        internal const string RecalculatingClip = "13_recalculating.mp3";
        internal static readonly string[] Clips = {
            "01_500_meters.mp3", "02_250_meters.mp3", "03_100_meters.mp3", "04_50_meters.mp3",
            "05_continue_straight.mp3", "06_turn_left.mp3", "07_turn_right.mp3", "08_keep_left.mp3",
            "09_keep_right.mp3", "10_make_a_u_turn.mp3", "11_in.mp3", "12_you_have_arrived.mp3"
        };
        static readonly HashSet<string> ValidClipSet = new HashSet<string>(Clips.Concat(new[] { RecalculatingClip }), StringComparer.OrdinalIgnoreCase);

        internal static bool IsClip(string? name) { return !string.IsNullOrEmpty(name) && ValidClipSet.Contains(name); }

        internal static bool IsSafePath(string? path) {
            if (string.IsNullOrWhiteSpace(path)) return false;
            for (int i = 0; i < path.Length; i++) {
                char ch = path[i];
                if (ch == '"' || ch == '\r' || ch == '\n' || ch == '\0') return false;
            }
            return true;
        }

        internal static string[] Discover(string? root) {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return Array.Empty<string>();
            return Directory.GetDirectories(root)
                .Where(dir => RequiredClips(Path.GetFileName(dir)).All(file => File.Exists(Path.Combine(dir, file))))
                .Select(dir => Path.GetFileName(dir) ?? "")
                .Where(name => name.Length > 0)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }

        internal static void InstallBundled(string root) {
            Assembly assembly = typeof(VoicePacks).Assembly;
            foreach (string resource in assembly.GetManifestResourceNames()) {
                if (!resource.StartsWith("voice-navigation/", StringComparison.Ordinal)) continue;
                string[] parts = resource.Split('/');
                if (parts.Length != 3 || parts[1] != Path.GetFileName(parts[1]) || !IsClip(parts[2])) continue;
                string directory = Path.Combine(root, parts[1]);
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, parts[2]);
                if (File.Exists(path)) continue;
                using (Stream? source = assembly.GetManifestResourceStream(resource)) {
                    if (source == null) continue;
                    using (FileStream destination = File.Create(path)) source.CopyTo(destination);
                }
            }
        }
    }

    internal sealed class VoiceCue {
        internal PointF Point;
        internal double Distance;
        internal string Action = "";
    }

    internal sealed class VoiceNavigator {
        static readonly int[] FootThresholds = { 50, 100, 250 };
        static readonly int[] VehicleThresholds = { 50, 100, 250, 500 };

        sealed class Spoken { internal PointF Point; internal string Action = ""; internal int Stage; }

        readonly List<Spoken> spoken = new List<Spoken>();
        DateTime lastSpoken = DateTime.MinValue, lastWrongWarning = DateTime.MinValue;
        RoadRoute? route;
        PointF[]? path;
        double[]? distances;
        int junctionOffset;
        int deviationSamples;
        PointF deviationPoint;
        DateTime deviationSince = DateTime.MinValue;
        bool outsideCorridor;
        PointF previousPlayer;
        bool hasPrevious;
        DateTime previousTime;
        double backwardsMetres;
        VoiceCue? previousCue;

        internal VoiceCue? CurrentCue { get; private set; }
        internal bool NeedsReroute { get; private set; }
        internal bool WrongWay { get; private set; }
        internal double Speed { get; private set; }

        double nearest, along;
        int nearestSegment;
        double previousAlong;
        PointF projectedPlayer;
        bool hasProjection;

        internal void Reset() {
            spoken.Clear(); route = null; path = null; distances = null;
            lastSpoken = lastWrongWarning = DateTime.MinValue;
            hasPrevious = false; backwardsMetres = 0; previousCue = CurrentCue = null;
            NeedsReroute = WrongWay = false; Speed = 0;
            deviationSamples = 0; deviationSince = DateTime.MinValue; outsideCorridor = false;
            hasProjection = false; nearestSegment = 1;
        }

        internal void RouteReplaced() {
            route = null; previousCue = CurrentCue = null; spoken.Clear(); lastSpoken = DateTime.MinValue;
            hasPrevious = false; backwardsMetres = 0; WrongWay = NeedsReroute = false;
            deviationSamples = 0; deviationSince = DateTime.MinValue; outsideCorridor = false;
            hasProjection = false; nearestSegment = 1;
        }

        static double SegmentProjection(PointF p, PointF a, PointF b, out double t) {
            double dx = (b.X - a.X) * 15216.18, dy = (b.Y - a.Y) * 15236.18;
            double px = (p.X - a.X) * 15216.18, py = (p.Y - a.Y) * 15236.18;
            double l2 = dx * dx + dy * dy;
            t = l2 < .0001 ? 0 : Math.Max(0, Math.Min(1, (px * dx + py * dy) / l2));
            double ex = px - dx * t, ey = py - dy * t;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        internal static string? Turn(double degrees) {
            if (Math.Abs(degrees) < 15) return null;
            if (Math.Abs(degrees) >= 150) return VoicePacks.Clips[9];
            return VoicePacks.Clips[degrees < 0 ? 5 : 6];
        }

        PointF At(double distance) {
            var p = path;
            var d = distances;
            if (p == null || d == null || p.Length == 0) return PointF.Empty;
            if (distance <= 0) return p[0];
            int last = p.Length - 1;
            if (distance >= d[last]) return p[last];
            int idx = Array.BinarySearch(d, distance);
            if (idx >= 0) return p[idx];
            int i = ~idx;
            if (i <= 0) return p[0];
            if (i > last) return p[last];
            double length = d[i] - d[i - 1];
            double t = length < .001 ? 0 : (distance - d[i - 1]) / length;
            return new PointF((float)(p[i - 1].X + (p[i].X - p[i - 1].X) * t), (float)(p[i - 1].Y + (p[i].Y - p[i - 1].Y) * t));
        }

        double ProjectWindow(PointF player, PointF[] p, double[] d, int lo, int hi, bool continuous, out double bestDist, out double bestAlong, out int bestSeg) {
            double closest = double.MaxValue;
            bestDist = double.MaxValue;
            bestAlong = 0;
            bestSeg = lo;
            for (int i = lo; i <= hi; i++) {
                double dist = SegmentProjection(player, p[i - 1], p[i], out _);
                if (dist < closest) closest = dist;
            }
            double bestContinuity = double.MaxValue;
            double threshold = closest + 8.0;
            for (int i = lo; i <= hi; i++) {
                double dist = SegmentProjection(player, p[i - 1], p[i], out double t);
                if (dist > threshold) continue;
                double candidateAlong = d[i - 1] + t * (d[i] - d[i - 1]);
                double continuity = continuous ? Math.Abs(candidateAlong - previousAlong) : 0;
                if (continuity < bestContinuity || (continuity == bestContinuity && dist < bestDist)) {
                    bestDist = dist;
                    bestAlong = candidateAlong;
                    bestSeg = i;
                    bestContinuity = continuity;
                }
            }
            return closest;
        }

        internal VoiceCue? NextCue(RoadRoute? current, PointF player) {
            NeedsReroute = false; CurrentCue = null; outsideCorridor = false;
            if (current == null || !current.Success || current.Polyline == null || current.Polyline.Length < 2) return null;
            if (!object.ReferenceEquals(route, current) || path == null || distances == null) {
                route = current;
                hasProjection = false;
                var complete = new List<PointF>(current.Polyline.Length + 2);
                junctionOffset = 0;
                if (current.EntryDistanceMeters > 15 && RoadRouter.DistanceMeters(current.PlayerPoint, current.Polyline[0]) > 1) {
                    complete.Add(current.PlayerPoint); junctionOffset = 1;
                }
                complete.AddRange(current.Polyline);
                if (current.ExitDistanceMeters > 15 && RoadRouter.DistanceMeters(current.TargetPoint, current.Polyline[current.Polyline.Length - 1]) > 1) {
                    complete.Add(current.TargetPoint);
                }
                path = complete.ToArray();
                distances = new double[path.Length];
                for (int i = 1; i < path.Length; i++) distances[i] = distances[i - 1] + RoadRouter.DistanceMeters(path[i - 1], path[i]);
            }

            var activePath = path;
            var activeDist = distances;
            bool continuous = hasProjection && RoadRouter.DistanceMeters(projectedPlayer, player) < 100;
            double closest;
            if (continuous && activePath.Length > 96 && nearestSegment >= 1 && nearestSegment < activePath.Length) {
                int lo = Math.Max(1, nearestSegment - 24);
                int hi = Math.Min(activePath.Length - 1, nearestSegment + 64);
                closest = ProjectWindow(player, activePath, activeDist, lo, hi, true, out nearest, out along, out nearestSegment);
                if (closest > 35.0) {
                    closest = ProjectWindow(player, activePath, activeDist, 1, activePath.Length - 1, true, out nearest, out along, out nearestSegment);
                }
            } else {
                closest = ProjectWindow(player, activePath, activeDist, 1, activePath.Length - 1, continuous, out nearest, out along, out nearestSegment);
            }

            outsideCorridor = closest > 35;
            if (outsideCorridor) return null;
            nearest = closest;
            previousAlong = along; projectedPlayer = player; hasProjection = true;

            foreach (int junction in current.JunctionIndices ?? Array.Empty<int>()) {
                int i = junction + junctionOffset;
                if (i <= 0 || i >= activePath.Length - 1 || activeDist[i] <= along + 3) continue;
                PointF before = At(activeDist[i] - 35), after = At(activeDist[i] + 35);
                double ax = (activePath[i].X - before.X) * 15216.18, ay = (activePath[i].Y - before.Y) * 15236.18;
                double bx = (after.X - activePath[i].X) * 15216.18, by = (after.Y - activePath[i].Y) * 15236.18;
                string? action = Turn(Math.Atan2(ax * by - ay * bx, ax * bx + ay * by) * 180 / Math.PI);
                if (action != null) return CurrentCue = new VoiceCue { Point = activePath[i], Distance = activeDist[i] - along, Action = action };
            }
            return CurrentCue = new VoiceCue { Point = activePath[activePath.Length - 1], Distance = activeDist[activePath.Length - 1] - along, Action = VoicePacks.Clips[4] };
        }

        void TrackMovement(PointF player, DateTime now, bool usableRoute) {
            if (!hasPrevious) { previousPlayer = player; previousTime = now; hasPrevious = true; return; }
            double travel = RoadRouter.DistanceMeters(previousPlayer, player), dt = (now - previousTime).TotalSeconds;
            double rawSpd = dt > 0 ? travel / dt : 0;
            if (dt > 5 || rawSpd > 125.0) { previousPlayer = player; previousTime = now; backwardsMetres = 0; WrongWay = false; Speed = 0; return; }
            if (travel < 3 || dt <= 0) return;
            Speed = Math.Min(100, travel / dt);
            var activePath = path;
            if (usableRoute && nearest < 35 && activePath != null && nearestSegment >= 1 && nearestSegment < activePath.Length) {
                PointF a = activePath[nearestSegment - 1], b = activePath[nearestSegment];
                double mx = (player.X - previousPlayer.X) * 15216.18, my = (player.Y - previousPlayer.Y) * 15236.18;
                double dx = (b.X - a.X) * 15216.18, dy = (b.Y - a.Y) * 15236.18;
                double length = Math.Sqrt(dx * dx + dy * dy);
                double alignment = length < .001 ? 0 : (mx * dx + my * dy) / (travel * length);
                backwardsMetres = alignment < -.6 ? backwardsMetres + travel : 0;
                WrongWay = backwardsMetres >= 10;
            } else {
                WrongWay = false; backwardsMetres = 0;
            }
            previousPlayer = player; previousTime = now;
        }

        internal string[]? Update(RoadRoute? current, PointF player, DateTime now, bool busy = false) {
            VoiceCue? cue = NextCue(current, player);
            if (current == null || !current.Success || current.Polyline == null || current.Polyline.Length < 2) {
                outsideCorridor = false; WrongWay = false; backwardsMetres = 0;
            }
            if (!outsideCorridor) { deviationSamples = 0; deviationSince = DateTime.MinValue; }
            else if (deviationSamples == 0 || RoadRouter.DistanceMeters(deviationPoint, player) >= 2) {
                if (deviationSamples == 0) deviationSince = now;
                deviationPoint = player; deviationSamples++;
            }
            NeedsReroute = outsideCorridor && deviationSamples >= 3 && (now - deviationSince).TotalSeconds >= .5;
            TrackMovement(player, now, current != null && current.Success && current.Polyline != null && current.Polyline.Length > 1);
            if (WrongWay) {
                NeedsReroute = true;
                if ((now - lastWrongWarning).TotalSeconds >= 12) {
                    lastWrongWarning = now; lastSpoken = now; spoken.Clear(); previousCue = null;
                    return new[] { VoicePacks.Clips[9] };
                }
                return null;
            }
            if (cue == null) return null;
            double leadDistance = Math.Max(0, cue.Distance - Speed * 1.5);
            int stage = -1;
            bool onFoot = Speed < 6.5;
            double finalTrigger = onFoot ? Math.Max(18, Speed * 2.5) : Math.Max(28, Speed * 3.0);
            int[] thresholds = onFoot ? FootThresholds : VehicleThresholds;
            if (cue.Action != VoicePacks.Clips[4] && cue.Distance <= finalTrigger) stage = 0;
            else {
                for (int ti = 0; ti < thresholds.Length; ti++) {
                    int threshold = thresholds[ti];
                    bool crossed = previousCue != null && previousCue.Action == cue.Action && RoadRouter.DistanceMeters(previousCue.Point, cue.Point) < 30
                        && previousCue.Distance - Speed * 1.5 > threshold && leadDistance <= threshold && leadDistance >= threshold * .65;
                    if (Math.Abs(leadDistance - threshold) <= Math.Max(12, threshold * .12) || crossed) { stage = threshold; break; }
                }
            }
            if (!busy) previousCue = cue;
            if (cue.Action == VoicePacks.Clips[4] || cue.Distance > 600) {
                if (busy || (now - lastSpoken).TotalSeconds < 90) return null;
                lastSpoken = now; return new[] { VoicePacks.Clips[4] };
            }
            if (stage < 0 || (busy && stage != 0) || (now - lastSpoken).TotalSeconds < (stage == 0 ? .5 : 1.2)) return null;
            for (int si = 0; si < spoken.Count; si++) {
                var item = spoken[si];
                if (item.Action == cue.Action && item.Stage <= stage && RoadRouter.DistanceMeters(item.Point, cue.Point) < 30) return null;
            }
            spoken.Add(new Spoken { Point = cue.Point, Action = cue.Action, Stage = stage });
            if (spoken.Count > 256) spoken.RemoveAt(0);
            lastSpoken = now;
            if (stage == 0) return new[] { cue.Action };
            int clip = stage == 500 ? 0 : stage == 250 ? 1 : stage == 100 ? 2 : 3;
            return new[] { VoicePacks.Clips[10], VoicePacks.Clips[clip], cue.Action };
        }
    }
}
