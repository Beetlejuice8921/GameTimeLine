using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace TimeMapGameplay
{
    /// <summary>One observation from a playtest: a session reached an item at a game time.</summary>
    [Serializable]
    public struct TelemetrySample
    {
        public string session;

        /// <summary>Item id, item name or bound asset key.</summary>
        public string item;

        /// <summary>Game time in the timeline's axis units.</summary>
        public float time;

        public TelemetrySample(string session, string item, float time)
        {
            this.session = session;
            this.item = item;
            this.time = time;
        }
    }

    /// <summary>Imported playtest data ("actual") to overlay on a timeline ("plan").</summary>
    [CreateAssetMenu(menuName = "TimeMapGameplay/Telemetry Data", fileName = "Telemetry", order = 20)]
    public sealed class TelemetryData : ScriptableObject
    {
        [SerializeField] string _source = "";
        [SerializeField] List<TelemetrySample> _samples = new();

        public string Source { get => _source; set => _source = value; }
        public List<TelemetrySample> Samples => _samples;

        public int SessionCount => _samples.Select(s => s.session).Distinct().Count();
    }

    /// <summary>Distribution of actual times for one item.</summary>
    public readonly struct TelemetryStats
    {
        public TelemetryStats(IReadOnlyList<float> sortedTimes)
        {
            Times = sortedTimes;
            Count = sortedTimes.Count;
            Min = Count > 0 ? sortedTimes[0] : 0f;
            Max = Count > 0 ? sortedTimes[Count - 1] : 0f;
            P25 = Percentile(sortedTimes, 0.25f);
            Median = Percentile(sortedTimes, 0.5f);
            P75 = Percentile(sortedTimes, 0.75f);
        }

        public IReadOnlyList<float> Times { get; }
        public int Count { get; }
        public float Min { get; }
        public float P25 { get; }
        public float Median { get; }
        public float P75 { get; }
        public float Max { get; }

        /// <summary>Linear-interpolated percentile of sorted values.</summary>
        public static float Percentile(IReadOnlyList<float> sorted, float p)
        {
            if (sorted.Count == 0) return 0f;
            float position = (sorted.Count - 1) * Mathf.Clamp01(p);
            int lower = Mathf.FloorToInt(position);
            int upper = Mathf.Min(lower + 1, sorted.Count - 1);
            return Mathf.Lerp(sorted[lower], sorted[upper], position - lower);
        }
    }

    /// <summary>Samples grouped by key (case-insensitive) for fast lookup per timeline item.</summary>
    public sealed class TelemetryIndex
    {
        readonly Dictionary<string, List<float>> _byKey = new(StringComparer.OrdinalIgnoreCase);

        public TelemetryIndex(TelemetryData data)
        {
            Data = data;
            if (data == null) return;
            foreach (var sample in data.Samples)
            {
                if (string.IsNullOrWhiteSpace(sample.item)) continue;
                string key = sample.item.Trim();
                if (!_byKey.TryGetValue(key, out var list)) _byKey[key] = list = new List<float>();
                list.Add(sample.time);
            }
            foreach (var list in _byKey.Values) list.Sort();
        }

        public TelemetryData Data { get; }

        /// <summary>
        /// Stats for an item, matched by id, then by any of <paramref name="extraKeys"/> (e.g. asset GUID), then by name.
        /// Returns false when there are no samples.
        /// </summary>
        public bool TryGet(TimelineItem item, out TelemetryStats stats, params string[] extraKeys)
        {
            foreach (var key in new[] { item.Id }.Concat(extraKeys ?? Array.Empty<string>()).Append(item.Name))
            {
                if (string.IsNullOrEmpty(key) || !_byKey.TryGetValue(key.Trim(), out var times)) continue;
                stats = new TelemetryStats(times);
                return true;
            }
            stats = default;
            return false;
        }
    }

    /// <summary>Parses playtest exports: CSV (session,item,time) or JSON ({"samples":[...]} / [...]).</summary>
    public static class TelemetryImport
    {
        static readonly string[] SessionColumns = { "session", "player", "user", "run", "сессия", "игрок" };
        static readonly string[] ItemColumns = { "item", "id", "event", "name", "элемент", "событие" };
        static readonly string[] TimeColumns = { "time", "hours", "t", "время" };

        /// <summary>
        /// Time cells accept "3.5", "3,5" and "3:30" (hours:minutes). Rows with unparsable time are skipped and counted.
        /// </summary>
        public static List<TelemetrySample> ParseCsv(string text, out int skipped)
        {
            skipped = 0;
            var rows = Csv.Parse(text);
            var samples = new List<TelemetrySample>();
            if (rows.Count == 0) return samples;

            var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
            int session = header.FindIndex(SessionColumns.Contains);
            int item = header.FindIndex(ItemColumns.Contains);
            int time = header.FindIndex(TimeColumns.Contains);
            if (item < 0 || time < 0)
                throw new FormatException("CSV телеметрии должен содержать колонки item и time (и опционально session).");

            foreach (var row in rows.Skip(1))
            {
                if (row.Count <= Math.Max(item, time) || !TryParseTime(row[time], out float t))
                {
                    skipped++;
                    continue;
                }
                samples.Add(new TelemetrySample(session >= 0 && session < row.Count ? row[session] : "", row[item], t));
            }
            return samples;
        }

        public static List<TelemetrySample> ParseJson(string text)
        {
            text = text.Trim();
            if (text.StartsWith("[")) text = "{\"samples\":" + text + "}";
            return JsonUtility.FromJson<Wrapper>(text)?.samples ?? new List<TelemetrySample>();
        }

        public static bool TryParseTime(string cell, out float time)
        {
            cell = (cell ?? "").Trim();
            int colon = cell.IndexOf(':');
            if (colon > 0 && int.TryParse(cell.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)
                          && int.TryParse(cell.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int m))
            {
                time = h + m / 60f;
                return true;
            }
            return float.TryParse(cell.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out time);
        }

        [Serializable]
        sealed class Wrapper
        {
            public List<TelemetrySample> samples;
        }
    }
}
