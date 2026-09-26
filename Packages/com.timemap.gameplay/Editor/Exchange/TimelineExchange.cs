using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimeMapGameplay.Editor
{
    /// <summary>Result of a CSV import.</summary>
    public sealed class ImportReport
    {
        public int Updated;
        public int Created;
        public int Unchanged;
        public int CurveKeys;
        public int NotInFile;
        public readonly List<string> Errors = new();

        public override string ToString()
            => $"Обновлено: {Updated}, создано: {Created}, без изменений: {Unchanged}, ключей кривых: {CurveKeys}" +
               (NotInFile > 0 ? $", нет в файле (оставлены как есть): {NotInFile}" : "") +
               (Errors.Count > 0 ? $"\nОшибки ({Errors.Count}):\n" + string.Join("\n", Errors.Take(20)) : "");
    }

    /// <summary>
    /// Spreadsheet exchange for balancing: one row per item (plus one per curve key), simple item fields as columns.
    /// Items are matched by id on import; rows without a known id create new items.
    /// </summary>
    public static class TimelineCsv
    {
        public const string CurveKeyType = "CurveKey";
        static readonly string[] Fixed = { "track", "track_type", "item_type", "id", "name", "start", "duration", "value", "binding" };
        static readonly HashSet<string> Handled = new() { "_id", "_name", "_start", "_duration", "_binding", "_requirements" };

        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static string Export(ProgressionTimeline timeline)
        {
            var so = new SerializedObject(timeline);
            var rows = new List<(string[] fixedCells, Dictionary<string, string> fields)>();
            var fieldColumns = new List<string>();

            foreach (var track in timeline.Tracks.Where(t => t != null))
            {
                foreach (var item in track.Items.Where(i => i != null).OrderBy(i => i.Start))
                {
                    var fields = new Dictionary<string, string>();
                    var property = InspectorPaths.FindManagedReference(so, item.Id);
                    if (property != null)
                        foreach (var child in SimpleFields(property))
                        {
                            string column = ColumnName(child.name);
                            fields[column] = Read(child);
                            if (!fieldColumns.Contains(column)) fieldColumns.Add(column);
                        }

                    rows.Add((new[]
                    {
                        track.Name, track.GetType().Name, item.GetType().Name, item.Id, item.Name,
                        Num(item.Start), item is ClipBase clip ? Num(clip.Duration) : "", "",
                        item.Binding != null ? AssetDatabase.GetAssetPath(item.Binding) : ""
                    }, fields));
                }

                if (track is CurveTrack curve)
                    for (int i = 0; i < curve.Keys.Count; i++)
                        rows.Add((new[]
                        {
                            track.Name, track.GetType().Name, CurveKeyType, $"{track.Id}#{i}", "",
                            Num(curve.Keys[i].time), "", Num(curve.Keys[i].value), ""
                        }, new Dictionary<string, string>()));
            }

            var header = Fixed.Concat(fieldColumns).ToArray();
            var table = new List<IReadOnlyList<string>> { header };
            table.AddRange(rows.Select(r => (IReadOnlyList<string>)r.fixedCells
                .Concat(fieldColumns.Select(c => r.fields.TryGetValue(c, out var v) ? v : "")).ToArray()));
            return Csv.Write(table);
        }

        public static ImportReport Import(ProgressionTimeline timeline, string csvText)
        {
            var report = new ImportReport();
            var rows = Csv.Parse(csvText);
            if (rows.Count == 0)
            {
                report.Errors.Add("Файл пустой.");
                return report;
            }

            var header = rows[0].Select(h => h.Trim()).ToList();
            int Col(string name) => header.FindIndex(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));
            int cTrack = Col("track"), cTrackType = Col("track_type"), cType = Col("item_type"), cId = Col("id"),
                cName = Col("name"), cStart = Col("start"), cDuration = Col("duration"), cValue = Col("value"), cBinding = Col("binding");
            if (cStart < 0 || (cId < 0 && cTrack < 0))
            {
                report.Errors.Add("Нужны колонки start и id (или track).");
                return report;
            }
            var fieldColumns = header.Select((h, i) => (h, i)).Where(p => !Fixed.Contains(p.h.ToLowerInvariant()) && p.h.Length > 0).ToList();

            Undo.RegisterCompleteObjectUndo(timeline, "Import CSV");
            var seen = new HashSet<string>();
            var touchedCurves = new HashSet<CurveTrack>();
            var pending = new List<(TimelineItem item, List<string> row, int line, string before, bool created)>();

            for (int r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                string Cell(int c) => c >= 0 && c < row.Count ? row[c].Trim() : "";
                int line = r + 1;

                try
                {
                    if (Cell(cType) == CurveKeyType)
                    {
                        ImportCurveKey(timeline, Cell(cId), Cell(cTrack), Cell(cStart), Cell(cValue), touchedCurves, report, line);
                        continue;
                    }

                    string id = Cell(cId);
                    bool created = false;
                    if (!(id.Length > 0 && timeline.TryFindItem(id, out var item, out _)))
                    {
                        item = CreateItem(timeline, Cell(cTrack), Cell(cTrackType), Cell(cType), report, line);
                        if (item == null) continue;
                        created = true;
                    }
                    seen.Add(item.Id);

                    string before = created ? null : EditorJsonUtility.ToJson(item);
                    if (cName >= 0 && Cell(cName).Length > 0) item.Name = Cell(cName);
                    if (TryNum(Cell(cStart), out float start)) item.Start = start;
                    if (item is ClipBase clip && TryNum(Cell(cDuration), out float duration)) clip.Duration = duration;
                    if (cBinding >= 0) item.Binding = ResolveBinding(Cell(cBinding), report, line);
                    pending.Add((item, row, line, before, created));
                }
                catch (Exception e)
                {
                    report.Errors.Add($"Строка {line}: {e.Message}");
                }
            }

            // Simple fields go through SerializedProperty so any project item type works.
            var so = new SerializedObject(timeline);
            foreach (var (item, row, line, _, _) in pending)
            {
                var property = InspectorPaths.FindManagedReference(so, item.Id);
                if (property == null) continue;
                var byColumn = SimpleFields(property).ToDictionary(p => ColumnName(p.name), p => p, StringComparer.OrdinalIgnoreCase);
                foreach (var (column, index) in fieldColumns)
                {
                    if (index >= row.Count || !byColumn.TryGetValue(column, out var field)) continue;
                    if (!Write(field, row[index], out string error))
                        report.Errors.Add($"Строка {line}, {column}: {error}");
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            foreach (var (item, _, _, before, created) in pending)
            {
                if (created) report.Created++;
                else if (before != EditorJsonUtility.ToJson(item)) report.Updated++;
                else report.Unchanged++;
            }

            foreach (var curve in touchedCurves) curve.SortKeys();
            report.NotInFile = timeline.Tracks.Where(t => t != null).SelectMany(t => t.Items).Count(i => i != null && !seen.Contains(i.Id));
            EditorUtility.SetDirty(timeline);
            return report;
        }

        static void ImportCurveKey(ProgressionTimeline timeline, string id, string trackName, string time, string value,
            HashSet<CurveTrack> touched, ImportReport report, int line)
        {
            CurveTrack track = null;
            int index = -1;
            int hash = id.LastIndexOf('#');
            if (hash > 0)
            {
                track = timeline.FindTrack(id.Substring(0, hash)) as CurveTrack;
                int.TryParse(id.Substring(hash + 1), NumberStyles.Integer, Invariant, out index);
            }
            track ??= timeline.Tracks.OfType<CurveTrack>().FirstOrDefault(t => t.Name == trackName);
            if (track == null)
            {
                report.Errors.Add($"Строка {line}: кривая «{trackName}» не найдена.");
                return;
            }
            if (!TryNum(time, out float t) || !TryNum(value, out float v))
            {
                report.Errors.Add($"Строка {line}: ключ кривой без start/value.");
                return;
            }

            var key = new CurveKey(t, v);
            if (index >= 0 && index < track.Keys.Count) track.Keys[index] = key;
            else track.Keys.Add(key);
            touched.Add(track);
            report.CurveKeys++;
        }

        static TimelineItem CreateItem(ProgressionTimeline timeline, string trackName, string trackType, string itemType,
            ImportReport report, int line)
        {
            var track = timeline.Tracks.FirstOrDefault(t => t != null && t.Name == trackName)
                        ?? timeline.Tracks.FirstOrDefault(t => t != null && t.GetType().Name == trackType && string.IsNullOrEmpty(trackName));
            if (track == null)
            {
                var type = FindType<TrackBase>(trackType);
                if (type == null)
                {
                    report.Errors.Add($"Строка {line}: трек «{trackName}» не найден, тип «{trackType}» неизвестен.");
                    return null;
                }
                track = (TrackBase)Activator.CreateInstance(type);
                track.Name = string.IsNullOrEmpty(trackName) ? ObjectNames.NicifyVariableName(type.Name) : trackName;
                timeline.Tracks.Add(track);
            }

            var itemTypeResolved = FindType<TimelineItem>(itemType);
            TimelineItem item = itemTypeResolved != null ? (TimelineItem)Activator.CreateInstance(itemTypeResolved) : track.CreateItem();
            if (item == null || !track.CanContain(item))
            {
                report.Errors.Add($"Строка {line}: трек «{track.Name}» не принимает «{itemType}».");
                return null;
            }
            track.AddItem(item);
            return item;
        }

        static Object ResolveBinding(string path, ImportReport report, int line)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null) report.Errors.Add($"Строка {line}: ассет «{path}» не найден, привязка снята.");
            return asset;
        }

        static Type FindType<TBase>(string name)
            => string.IsNullOrEmpty(name)
                ? null
                : TypeCache.GetTypesDerivedFrom<TBase>().FirstOrDefault(t => !t.IsAbstract && t.Name == name && t.GetConstructor(Type.EmptyTypes) != null);

        /// <summary>Direct children of a managed reference that fit in a spreadsheet cell.</summary>
        static IEnumerable<SerializedProperty> SimpleFields(SerializedProperty parent)
        {
            var iterator = parent.Copy();
            var end = parent.GetEndProperty();
            if (!iterator.Next(true)) yield break;
            while (!SerializedProperty.EqualContents(iterator, end))
            {
                if (!Handled.Contains(iterator.name) && IsSimple(iterator))
                    yield return iterator.Copy();
                if (!iterator.Next(false)) break;
            }
        }

        static bool IsSimple(SerializedProperty p) => p.propertyType is SerializedPropertyType.Integer or SerializedPropertyType.Float
            or SerializedPropertyType.Boolean or SerializedPropertyType.String or SerializedPropertyType.Enum;

        static string ColumnName(string propertyName)
        {
            string name = propertyName.StartsWith("m_") ? propertyName.Substring(2) : propertyName.TrimStart('_');
            return Fixed.Contains(name.ToLowerInvariant()) ? "f_" + name : name;
        }

        static string Read(SerializedProperty p) => p.propertyType switch
        {
            SerializedPropertyType.Integer => p.longValue.ToString(Invariant),
            SerializedPropertyType.Float => Num(p.floatValue),
            SerializedPropertyType.Boolean => p.boolValue ? "true" : "false",
            SerializedPropertyType.String => p.stringValue,
            SerializedPropertyType.Enum => p.enumValueIndex >= 0 && p.enumValueIndex < p.enumNames.Length ? p.enumNames[p.enumValueIndex] : "",
            _ => ""
        };

        static bool Write(SerializedProperty p, string cell, out string error)
        {
            error = null;
            cell = cell.Trim();
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                    if (cell.Length == 0) return true;
                    if (long.TryParse(cell, NumberStyles.Integer, Invariant, out long l)) { p.longValue = l; return true; }
                    break;
                case SerializedPropertyType.Float:
                    if (cell.Length == 0) return true;
                    if (TryNum(cell, out float f)) { p.floatValue = f; return true; }
                    break;
                case SerializedPropertyType.Boolean:
                    if (cell.Length == 0) return true;
                    if (bool.TryParse(cell, out bool b)) { p.boolValue = b; return true; }
                    if (cell is "0" or "1") { p.boolValue = cell == "1"; return true; }
                    break;
                case SerializedPropertyType.String:
                    p.stringValue = cell;
                    return true;
                case SerializedPropertyType.Enum:
                    if (cell.Length == 0) return true;
                    int index = Array.FindIndex(p.enumNames, n => string.Equals(n, cell, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0) { p.enumValueIndex = index; return true; }
                    break;
            }
            error = $"не удалось прочитать «{cell}»";
            return false;
        }

        static string Num(float value) => value.ToString("0.####", Invariant);

        static bool TryNum(string cell, out float value)
            => float.TryParse((cell ?? "").Trim().Replace(',', '.'), NumberStyles.Float, Invariant, out value);
    }

    /// <summary>Whole-asset JSON exchange (for diffs in VCS, backups and tools).</summary>
    public static class TimelineJson
    {
        public static string Export(ProgressionTimeline timeline) => EditorJsonUtility.ToJson(timeline, true);

        public static void Import(ProgressionTimeline timeline, string json)
        {
            Undo.RegisterCompleteObjectUndo(timeline, "Import JSON");
            string name = timeline.name;
            EditorJsonUtility.FromJsonOverwrite(json, timeline);
            timeline.name = name;
            EditorUtility.SetDirty(timeline);
        }
    }

    /// <summary>Saved snapshots of a timeline for comparing design iterations.</summary>
    public static class TimelineVersions
    {
        public static string FolderFor(ProgressionTimeline timeline)
        {
            string path = AssetDatabase.GetAssetPath(timeline);
            string directory = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
            return $"{directory}/{timeline.name}_Versions";
        }

        /// <summary>Copies the timeline into its versions folder. Returns the new asset.</summary>
        public static ProgressionTimeline Save(ProgressionTimeline timeline, string label = null)
        {
            string folder = FolderFor(timeline);
            if (!AssetDatabase.IsValidFolder(folder))
            {
                string parent = System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
            }

            var copy = Object.Instantiate(timeline);
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
            copy.name = string.IsNullOrWhiteSpace(label) ? $"{timeline.name} {stamp}" : $"{timeline.name} {stamp} {label.Trim()}";
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{copy.name}.asset");
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            return copy;
        }

        /// <summary>Saved versions, newest first.</summary>
        public static List<ProgressionTimeline> List(ProgressionTimeline timeline)
        {
            string folder = FolderFor(timeline);
            if (!AssetDatabase.IsValidFolder(folder)) return new List<ProgressionTimeline>();
            return AssetDatabase.FindAssets("t:ProgressionTimeline", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderByDescending(p => p, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<ProgressionTimeline>)
                .Where(t => t != null)
                .ToList();
        }
    }
}
