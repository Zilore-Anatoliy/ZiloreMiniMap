#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ZiloreMiniMap {
    // Optional marker identities keep the encrypted v3 transport interoperable.
    // Older clients show ordinary named markers; newer clients also understand
    // ping expiry and route ordering. No new relay messages or services are used.
    internal sealed class SquadState {
        internal const int PingSeconds = 60, MaximumStops = 16;
        readonly Func<DateTime> clock;
        readonly Dictionary<string, DateTime> firstPings = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        readonly List<SharedMapMarker> pings = new List<SharedMapMarker>();
        readonly List<SharedMapMarker> stops = new List<SharedMapMarker>();
        DateTime lastPing = DateTime.MinValue;
        string route = Guid.NewGuid().ToString("N");
        internal bool Shared { get; private set; }

        internal SquadState(Func<DateTime>? clock = null) { this.clock = clock ?? (() => DateTime.UtcNow); }

        internal static bool IsPing(SharedMapMarker? m) {
            return m != null && m.Id != null && m.Id.StartsWith("50494E47", StringComparison.Ordinal) && m.Id.Length == 64;
        }

        internal static bool IsStop(SharedMapMarker? m) {
            return m != null && m.Id != null && m.Id.Length == 64
                && (m.Id.StartsWith("524F5554", StringComparison.Ordinal) || m.Id.StartsWith("534F4C4F", StringComparison.Ordinal) || m.Id.StartsWith("53485244", StringComparison.Ordinal))
                && StopIndex(m) < MaximumStops;
        }

        internal static bool IsShared(SharedMapMarker? m) {
            return IsStop(m) && m != null && m.Id != null && !m.Id.StartsWith("534F4C4F", StringComparison.Ordinal);
        }

        string StopId(int index, string? identity = null) {
            return (Shared ? "53485244" : "534F4C4F") + route + index.ToString("X2", CultureInfo.InvariantCulture) + (identity ?? Guid.NewGuid().ToString("N").Substring(0, 22));
        }

        internal void SetShared(bool shared) {
            Shared = shared;
            for (int i = 0; i < stops.Count; i++)
                stops[i].Id = StopId(i, (stops[i].Id != null && stops[i].Id.Length >= 42) ? stops[i].Id.Substring(42) : null);
        }

        internal static int StopIndex(SharedMapMarker? m) {
            return m != null && m.Id != null && m.Id.Length == 64
                && int.TryParse(m.Id.AsSpan(40, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value)
                ? value : int.MaxValue;
        }

        internal static string RouteKey(SharedMapMarker? m) {
            if (m == null || m.Id == null || m.Id.Length < 40) return (m == null ? "" : (m.OwnerId ?? "")) + ":";
            return (m.OwnerId ?? "") + ":" + m.Id.Substring(8, 32);
        }

        internal bool Ping(int kind, string? name, double x, double y) {
            if (kind < 0 || kind > SquadStatusKind.MaxKind || clock() - lastPing < TimeSpan.FromSeconds(2)) return false;
            var m = new SharedMapMarker {
                Id = "50494E47" + kind.ToString("X2", CultureInfo.InvariantCulture) + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N").Substring(0, 22),
                Name = name ?? "",
                X = x,
                Y = y
            };
            if (!SharedMapMarker.Valid(m)) return false;
            lastPing = clock();
            pings.RemoveAll(p => !Alive(p, "local"));
            if (pings.Count >= 8) pings.RemoveAt(0);
            pings.Add(m);
            firstPings["local:" + m.Id] = clock();
            return true;
        }

        bool Alive(SharedMapMarker m, string owner) {
            string key = owner + ":" + m.Id;
            if (!firstPings.TryGetValue(key, out DateTime first)) {
                if (firstPings.Count >= 2048) return false;
                firstPings[key] = first = clock();
            }
            DateTime now = clock();
            return now - first < TimeSpan.FromSeconds(PingSeconds) && now >= first;
        }

        internal SharedMapMarker[] Receive(IEnumerable<SharedMapMarker>? markers) {
            DateTime now = clock();
            if (firstPings.Count > 0) {
                List<string>? expired = null;
                foreach (var kv in firstPings) {
                    if (now - kv.Value > TimeSpan.FromMinutes(5)) {
                        if (expired == null) expired = new List<string>(8);
                        expired.Add(kv.Key);
                    }
                }
                if (expired != null) for (int ei = 0; ei < expired.Count; ei++) firstPings.Remove(expired[ei]);
            }
            if (markers == null) return Array.Empty<SharedMapMarker>();
            var list = new List<SharedMapMarker>(16);
            foreach (var m in markers) {
                if (!SharedMapMarker.Valid(m)) continue;
                if (IsPing(m) && !Alive(m, m.OwnerId ?? "")) continue;
                list.Add(m.Copy());
            }
            return list.Count == 0 ? Array.Empty<SharedMapMarker>() : list.ToArray();
        }

        internal SharedMapMarker[] Local() {
            if (pings.Count > 0) pings.RemoveAll(p => !Alive(p, "local"));
            int total = pings.Count + stops.Count;
            if (total == 0) return Array.Empty<SharedMapMarker>();
            var arr = new SharedMapMarker[total];
            int idx = 0;
            for (int i = 0; i < pings.Count; i++) arr[idx++] = pings[i].Copy();
            for (int i = 0; i < stops.Count; i++) arr[idx++] = stops[i].Copy();
            return arr;
        }

        internal SharedMapMarker[] Stops {
            get {
                if (stops.Count == 0) return Array.Empty<SharedMapMarker>();
                var arr = new SharedMapMarker[stops.Count];
                for (int i = 0; i < stops.Count; i++) arr[i] = stops[i].Copy();
                return arr;
            }
        }

        internal bool AddStop(string? name, double x, double y) {
            if (stops.Count >= MaximumStops) return false;
            var m = new SharedMapMarker { Id = StopId(stops.Count), Name = name ?? "", X = x, Y = y };
            if (!SharedMapMarker.Valid(m)) return false;
            stops.Add(m);
            return true;
        }

        internal void ClearRoute() { stops.Clear(); route = Guid.NewGuid().ToString("N"); }
        internal void RemoveLast() { if (stops.Count > 0) stops.RemoveAt(stops.Count - 1); }

        void ReindexStops() {
            for (int i = 0; i < stops.Count; i++)
                stops[i].Id = StopId(i, (stops[i].Id != null && stops[i].Id.Length >= 42) ? stops[i].Id.Substring(42) : null);
        }

        internal bool RemoveStop(string? id) {
            if (string.IsNullOrEmpty(id)) return false;
            int index = stops.FindIndex(m => m.Id == id);
            if (index < 0) return false;
            stops.RemoveAt(index);
            ReindexStops();
            return true;
        }

        internal bool MoveStop(string? id, int delta) {
            if (string.IsNullOrEmpty(id) || delta == 0) return false;
            int index = stops.FindIndex(m => m.Id == id);
            int target = index + delta;
            if (index < 0 || target < 0 || target >= stops.Count) return false;
            var item = stops[index];
            stops.RemoveAt(index);
            stops.Insert(target, item);
            ReindexStops();
            return true;
        }

        internal bool UpdateStopPosition(string? id, double x, double y) {
            if (string.IsNullOrEmpty(id) || double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y) || x < 0 || x > 1 || y < 0 || y > 1) return false;
            int index = stops.FindIndex(m => m.Id == id);
            if (index < 0) return false;
            stops[index].X = x;
            stops[index].Y = y;
            return true;
        }

        internal bool InsertStop(int index, string? name, double x, double y) {
            if (stops.Count >= MaximumStops || double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y) || x < 0 || x > 1 || y < 0 || y > 1) return false;
            int pos = Math.Max(0, Math.Min(stops.Count, index));
            if (stops.Count == 0) route = Guid.NewGuid().ToString("N");
            string clean = string.IsNullOrWhiteSpace(name) ? "Stop" : name.Trim();
            if (clean.Length > 48) clean = clean.Substring(0, 48);
            var m = new SharedMapMarker { Id = StopId(pos), Name = clean, X = x, Y = y };
            if (!SharedMapMarker.Valid(m)) return false;
            stops.Insert(pos, m);
            ReindexStops();
            return true;
        }

        internal bool RenameStop(string? id, string? newName) {
            if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(newName)) return false;
            int i = stops.FindIndex(m => m.Id == id);
            if (i < 0) return false;
            string clean = newName.Trim();
            if (clean.Length > 48) clean = clean.Substring(0, 48);
            stops[i].Name = clean;
            return true;
        }

        internal bool ReverseRoute() {
            if (stops.Count < 2) return false;
            stops.Reverse();
            ReindexStops();
            return true;
        }

        internal void ReplaceStops(IEnumerable<SharedMapMarker>? items) {
            stops.Clear();
            route = Guid.NewGuid().ToString("N");
            if (items == null) return;
            foreach (var item in items) {
                if (item == null) continue;
                if (stops.Count >= MaximumStops) break;
                AddStop(item.Name, item.X, item.Y);
            }
        }

        internal static SharedMapMarker[][] Routes(IEnumerable<SharedMapMarker>? markers) {
            if (markers == null) return Array.Empty<SharedMapMarker[]>();
            var arr = markers as SharedMapMarker[];
            if (arr != null && arr.Length == 0) return Array.Empty<SharedMapMarker[]>();
            Dictionary<string, List<SharedMapMarker>>? grouped = null;
            List<string>? keyOrder = null;
            if (arr != null) {
                for (int i = 0; i < arr.Length; i++) {
                    var m = arr[i];
                    if (!IsStop(m)) continue;
                    if (grouped == null) { grouped = new Dictionary<string, List<SharedMapMarker>>(4, StringComparer.Ordinal); keyOrder = new List<string>(4); }
                    string k = RouteKey(m);
                    if (!grouped.TryGetValue(k, out var bucket)) { bucket = new List<SharedMapMarker>(8); grouped.Add(k, bucket); keyOrder!.Add(k); }
                    bucket.Add(m);
                }
            } else {
                foreach (var m in markers) {
                    if (!IsStop(m)) continue;
                    if (grouped == null) { grouped = new Dictionary<string, List<SharedMapMarker>>(4, StringComparer.Ordinal); keyOrder = new List<string>(4); }
                    string k = RouteKey(m);
                    if (!grouped.TryGetValue(k, out var bucket)) { bucket = new List<SharedMapMarker>(8); grouped.Add(k, bucket); keyOrder!.Add(k); }
                    bucket.Add(m);
                }
            }
            if (grouped == null || keyOrder == null) return Array.Empty<SharedMapMarker[]>();
            var validRoutes = new List<SharedMapMarker[]>(keyOrder.Count);
            for (int ki = 0; ki < keyOrder.Count; ki++) {
                var bucket = grouped[keyOrder[ki]];
                if (bucket.Count > 1) bucket.Sort((a, b) => StopIndex(a).CompareTo(StopIndex(b)));
                int take = Math.Min(bucket.Count, MaximumStops);
                bool contiguous = true;
                for (int si = 0; si < take; si++) { if (StopIndex(bucket[si]) != si) { contiguous = false; break; } }
                if (contiguous) {
                    var routeArr = new SharedMapMarker[take];
                    for (int si = 0; si < take; si++) routeArr[si] = bucket[si];
                    validRoutes.Add(routeArr);
                }
            }
            return validRoutes.Count == 0 ? Array.Empty<SharedMapMarker[]>() : validRoutes.ToArray();
        }
    }

    internal sealed class MapPreset {
        internal string Name = "", Map = "", Categories = "", ZoneLayers = "", Zones = "";
        internal bool Grid, Waypoints, History, Labels, ShowZones, Poi, Rotate, Cluster;
        internal int Opacity;
        internal float Zoom;
        internal int TacticalOpacity;
        internal float TacticalZoom = 1;
    }

    internal static class MapPresetFile {
        internal static string Recover(string file) {
            string backup = file + ".recovery-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".bak";
            File.Copy(file, backup, false);
            Save(file, Array.Empty<MapPreset>());
            return backup;
        }

        internal static MapPreset[] Load(string file) {
            if (!File.Exists(file)) return Array.Empty<MapPreset>();
            if (new FileInfo(file).Length > 262144) throw new InvalidDataException();
            using (var r = new BinaryReader(File.OpenRead(file), Encoding.UTF8)) {
                int version = r.ReadInt32();
                if (version != 1 && version != 2) throw new InvalidDataException();
                int count = r.ReadInt32();
                if (count < 0 || count > 24) throw new InvalidDataException();
                var list = new List<MapPreset>(count);
                for (int i = 0; i < count; i++) {
                    var p = new MapPreset {
                        Name = r.ReadString(), Map = r.ReadString(), Categories = r.ReadString(), ZoneLayers = r.ReadString(), Zones = r.ReadString(),
                        Grid = r.ReadBoolean(), Waypoints = r.ReadBoolean(), History = r.ReadBoolean(), Labels = r.ReadBoolean(), ShowZones = r.ReadBoolean(),
                        Poi = r.ReadBoolean(), Rotate = r.ReadBoolean(), Cluster = r.ReadBoolean(), Opacity = r.ReadInt32(), Zoom = r.ReadSingle()
                    };
                    if (version >= 2) { p.TacticalOpacity = r.ReadInt32(); p.TacticalZoom = r.ReadSingle(); }
                    else { p.TacticalOpacity = 85; p.TacticalZoom = 1f; }
                    if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 64 || p.Opacity < 30 || p.Opacity > 100 || float.IsNaN(p.Zoom) || p.Zoom < 1 || p.Zoom > 100)
                        throw new InvalidDataException();
                    if (p.TacticalOpacity < 0 || p.TacticalOpacity > 100 || float.IsNaN(p.TacticalZoom) || p.TacticalZoom < 1 || p.TacticalZoom > 100)
                        throw new InvalidDataException();
                    list.Add(p);
                }
                if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException();
                return list.ToArray();
            }
        }

        internal static void Save(string file, IEnumerable<MapPreset> presets) {
            ArgumentNullException.ThrowIfNull(presets);
            var values = presets.ToArray();
            if (values.Length > 24) throw new InvalidOperationException();
            string? dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            bool moved = false;
            try {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) {
                    using (var w = new BinaryWriter(stream, Encoding.UTF8, true)) {
                        w.Write(2);
                        w.Write(values.Length);
                        foreach (var p in values) {
                            w.Write(p.Name ?? ""); w.Write(p.Map ?? ""); w.Write(p.Categories ?? ""); w.Write(p.ZoneLayers ?? ""); w.Write(p.Zones ?? "");
                            w.Write(p.Grid); w.Write(p.Waypoints); w.Write(p.History); w.Write(p.Labels); w.Write(p.ShowZones);
                            w.Write(p.Poi); w.Write(p.Rotate); w.Write(p.Cluster); w.Write(p.Opacity); w.Write(p.Zoom);
                            w.Write(p.TacticalOpacity); w.Write(p.TacticalZoom);
                        }
                        w.Flush();
                    }
                    stream.Flush(true);
                }
                if (File.Exists(file)) File.Replace(temp, file, null);
                else File.Move(temp, file);
                moved = true;
            } finally {
                if (!moved) try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }
    }

    internal sealed class SavedRouteTemplate {
        internal string Name = "";
        internal bool Shared;
        internal SharedMapMarker[] Stops = Array.Empty<SharedMapMarker>();
    }

    internal static class SavedRouteLibrary {
        const int MaxTemplates = 32;

        internal static List<SavedRouteTemplate> Load(string? file) {
            var list = new List<SavedRouteTemplate>();
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return list;
            try {
                if (new FileInfo(file).Length > 512 * 1024) return list;
                SavedRouteTemplate? current = null;
                var stops = new List<SharedMapMarker>();
                foreach (string raw in File.ReadAllLines(file, Encoding.UTF8)) {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    if (raw.StartsWith("ROUTE\t", StringComparison.Ordinal)) {
                        if (current != null && stops.Count > 0) { current.Stops = stops.ToArray(); list.Add(current); }
                        var p = raw.Split('\t');
                        current = new SavedRouteTemplate { Name = p.Length > 1 ? p[1].Trim() : "Route", Shared = p.Length > 2 && p[2] == "1" };
                        stops.Clear();
                    } else if (raw.StartsWith("STOP\t", StringComparison.Ordinal) && current != null && stops.Count < SquadState.MaximumStops) {
                        var p = raw.Split('\t');
                        if (p.Length >= 3
                            && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                            && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                            && !double.IsNaN(x) && !double.IsNaN(y) && !double.IsInfinity(x) && !double.IsInfinity(y)
                            && x >= 0 && x <= 1 && y >= 0 && y <= 1) {
                            stops.Add(new SharedMapMarker { Name = p.Length > 3 ? p[3] : "", X = x, Y = y });
                        }
                    }
                }
                if (current != null && stops.Count > 0) { current.Stops = stops.ToArray(); list.Add(current); }
            } catch (Exception ex) { Program.LogWarn("SavedRoutes", "Could not load saved routes: " + ex.Message); }
            return list;
        }

        internal static void Save(string file, IEnumerable<SavedRouteTemplate>? templates) {
            var sb = new StringBuilder(1024);
            int count = 0;
            if (templates != null) {
                foreach (var t in templates) {
                    if (t == null || string.IsNullOrWhiteSpace(t.Name) || t.Stops == null || t.Stops.Length == 0) continue;
                    if (++count > MaxTemplates) break;
                    string safeTitle = t.Name.Trim().Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
                    sb.Append("ROUTE\t").Append(safeTitle).Append('\t').Append(t.Shared ? "1" : "0").Append('\n');
                    foreach (var s in t.Stops) {
                        string safeStop = (s.Name ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
                        sb.Append("STOP\t").Append(s.X.ToString("R", CultureInfo.InvariantCulture)).Append('\t').Append(s.Y.ToString("R", CultureInfo.InvariantCulture)).Append('\t').Append(safeStop).Append('\n');
                    }
                }
            }
            SettingsStore.WriteAtomic(file, sb.ToString());
        }

        internal const string SharePrefix = "ZLR-ROUTE:";

        internal static string EncodeShareCode(SavedRouteTemplate? tpl) {
            if (tpl == null || tpl.Stops == null || tpl.Stops.Length == 0) return "";
            var sb = new StringBuilder(256);
            string safeTitle = (tpl.Name ?? "Route").Trim().Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
            sb.Append(safeTitle).Append('\t').Append(tpl.Shared ? "1" : "0").Append('\n');
            int count = Math.Min(tpl.Stops.Length, SquadState.MaximumStops);
            for (int i = 0; i < count; i++) {
                var s = tpl.Stops[i];
                string safeStop = (s.Name ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
                sb.Append(s.X.ToString("F5", CultureInfo.InvariantCulture)).Append('\t').Append(s.Y.ToString("F5", CultureInfo.InvariantCulture)).Append('\t').Append(safeStop).Append('\n');
            }
            return SharePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        internal static bool TryDecodeShareCode(string? code, [NotNullWhen(true)] out SavedRouteTemplate? template) {
            template = null;
            if (string.IsNullOrWhiteSpace(code) || code.Length > 32768) return false;
            string trimmed = code.Trim();
            if (!trimmed.StartsWith(SharePrefix, StringComparison.OrdinalIgnoreCase)) return false;
            try {
                byte[] bytes = Convert.FromBase64String(trimmed.Substring(SharePrefix.Length).Trim());
                if (bytes.Length == 0 || bytes.Length > 16384) return false;
                string text = Encoding.UTF8.GetString(bytes);
                var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length < 2) return false;
                var header = lines[0].Split('\t');
                string name = header[0].Trim();
                if (name.Length == 0) name = "Imported Route";
                if (name.Length > 48) name = name.Substring(0, 48);
                bool shared = header.Length > 1 && header[1] == "1";
                var stops = new List<SharedMapMarker>();
                for (int i = 1; i < lines.Length && stops.Count < SquadState.MaximumStops; i++) {
                    var p = lines[i].Split('\t');
                    if (p.Length < 2) continue;
                    if (!double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                        || !double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)) continue;
                    if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y) || x < 0 || x > 1 || y < 0 || y > 1) continue;
                    string sname = p.Length >= 3 ? p[2].Trim() : "";
                    if (sname.Length > 64) sname = sname.Substring(0, 64);
                    stops.Add(new SharedMapMarker { Name = sname, X = x, Y = y });
                }
                if (stops.Count == 0) return false;
                template = new SavedRouteTemplate { Name = name, Shared = shared, Stops = stops.ToArray() };
                return true;
            } catch {
                return false;
            }
        }
    }
}
