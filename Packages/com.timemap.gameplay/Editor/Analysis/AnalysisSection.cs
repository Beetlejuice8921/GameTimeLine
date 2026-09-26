using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimeMapGameplay.Editor
{
    /// <summary>Imports playtest files into a <see cref="TelemetryData"/> asset.</summary>
    public static class TelemetryImporter
    {
        /// <summary>Asks for a CSV/JSON file and where to save the asset. Returns null when cancelled.</summary>
        public static TelemetryData ImportInteractive(ProgressionTimeline timeline)
        {
            string file = EditorUtility.OpenFilePanelWithFilters("Импорт телеметрии", "", new[] { "CSV / JSON", "csv,json,txt", "All", "*" });
            if (string.IsNullOrEmpty(file)) return null;

            List<TelemetrySample> samples;
            int skipped = 0;
            try
            {
                string text = File.ReadAllText(file, Encoding.UTF8);
                samples = file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    ? TelemetryImport.ParseJson(text)
                    : TelemetryImport.ParseCsv(text, out skipped);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Импорт телеметрии", e.Message, "OK");
                return null;
            }

            string folder = timeline != null ? Path.GetDirectoryName(AssetDatabase.GetAssetPath(timeline))?.Replace('\\', '/') : "Assets";
            string path = EditorUtility.SaveFilePanelInProject("Сохранить телеметрию", Path.GetFileNameWithoutExtension(file), "asset",
                "Где сохранить ассет телеметрии", folder);
            if (string.IsNullOrEmpty(path)) return null;

            var data = Create(samples, Path.GetFileName(file));
            AssetDatabase.CreateAsset(data, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TimeMapGameplay] Телеметрия: {samples.Count} событий, {data.SessionCount} сессий" +
                      (skipped > 0 ? $", пропущено строк: {skipped}" : "") + $" → {path}", data);
            return data;
        }

        public static TelemetryData Create(IEnumerable<TelemetrySample> samples, string source)
        {
            var data = ScriptableObject.CreateInstance<TelemetryData>();
            data.Source = source;
            data.Samples.AddRange(samples);
            return data;
        }
    }

    /// <summary>"Analysis" part of the inspector when nothing is selected: telemetry and version comparison.</summary>
    public sealed class AnalysisSection : VisualElement
    {
        const int MaxRows = 30;

        readonly AnalysisState _analysis;
        readonly ProgressionTimeline _timeline;
        readonly Action<string> _focusItem;
        readonly IReadOnlyList<TimelineChange> _changes;

        public AnalysisSection(AnalysisState analysis, ProgressionTimeline timeline, IReadOnlyList<TimelineChange> changes, Action<string> focusItem)
        {
            _analysis = analysis;
            _timeline = timeline;
            _changes = changes ?? Array.Empty<TimelineChange>();
            _focusItem = focusItem;
            BuildTelemetry();
            BuildVersions();
        }

        void BuildTelemetry()
        {
            Section("Телеметрия: план против факта");

            var field = new ObjectField("Данные") { objectType = typeof(TelemetryData), allowSceneObjects = false, value = _analysis.Telemetry };
            field.RegisterValueChangedCallback(e => _analysis.Telemetry = e.newValue as TelemetryData);
            Add(field);

            var show = new Toggle("На таймлайне") { value = _analysis.ShowTelemetry };
            show.RegisterValueChangedCallback(e => _analysis.ShowTelemetry = e.newValue);
            Add(show);

            var buttons = Buttons();
            buttons.Add(new Button(() =>
            {
                var data = TelemetryImporter.ImportInteractive(_timeline);
                if (data != null) _analysis.Telemetry = data;
            }) { text = "Импорт CSV / JSON…" });

            var index = _analysis.TelemetryIndex;
            if (index == null)
            {
                Hint("CSV с колонками session, item, time (время в единицах оси, «3.5» или «3:30»). " +
                     "item — id, имя элемента или GUID привязанного ассета.");
                return;
            }

            var items = _timeline.Tracks.Where(t => t != null).SelectMany(t => t.Items).Where(i => i != null).ToList();
            var matched = items.Select(i => (item: i, ok: _analysis.TryGetStats(i, out var s), stats: s)).Where(p => p.ok).ToList();
            Hint($"{_analysis.Telemetry.Samples.Count} событий, {_analysis.Telemetry.SessionCount} сессий. " +
                 $"Сопоставлено элементов: {matched.Count} из {items.Count}.");

            var units = _timeline.Axis.Units;
            foreach (var (item, _, stats) in matched.OrderByDescending(p => Mathf.Abs(p.stats.Median - p.item.Start)).Take(8))
            {
                float delta = stats.Median - item.Start;
                var row = Link($"{item.Name}: план {TimeMath.Format(item.Start, units)}, факт {TimeMath.Format(stats.Median, units)} " +
                               $"({(delta >= 0 ? "+" : "−")}{TimeMath.Format(Mathf.Abs(delta), units)}, n={stats.Count})", item.Id);
                row.EnableInClassList("tmg-wmsg", Mathf.Abs(delta) > 0.0001f && delta > 0);
                Add(row);
            }
        }

        void BuildVersions()
        {
            Section("Версии");

            var buttons = Buttons();
            buttons.Add(new Button(() =>
            {
                var version = TimelineVersions.Save(_timeline);
                Debug.Log($"[TimeMapGameplay] Версия сохранена: {AssetDatabase.GetAssetPath(version)}", version);
            }) { text = "Сохранить версию", tooltip = $"Копия в {TimelineVersions.FolderFor(_timeline)}" });

            var versions = TimelineVersions.List(_timeline);
            if (versions.Count > 0)
                buttons.Add(new Button(() => _analysis.Baseline = versions[0]) { text = "Сравнить с последней" });
            if (_analysis.Baseline != null)
                buttons.Add(new Button(() => _analysis.Baseline = null) { text = "Сбросить" });

            var field = new ObjectField("Сравнить с") { objectType = typeof(ProgressionTimeline), allowSceneObjects = false, value = _analysis.Baseline };
            field.RegisterValueChangedCallback(e => _analysis.Baseline = e.newValue as ProgressionTimeline == _timeline ? null : e.newValue as ProgressionTimeline);
            Add(field);

            if (_analysis.Baseline == null)
            {
                Hint("Сохраняйте версии перед большими правками, затем сравнивайте: сдвинутые и удалённые элементы " +
                     "покажутся на таймлайне пунктиром на прежнем месте.");
                return;
            }

            if (_changes.Count == 0)
            {
                Hint($"Отличий от «{_analysis.Baseline.name}» нет.");
                return;
            }
            Hint($"Отличий от «{_analysis.Baseline.name}»: {_changes.Count}.");
            foreach (var change in _changes.Take(MaxRows))
            {
                bool exists = change.ItemId != null && _timeline.TryFindItem(change.ItemId, out _, out _);
                Add(exists ? Link(change.Description, change.ItemId) : Note(change.Description));
            }
            if (_changes.Count > MaxRows) Hint($"… и ещё {_changes.Count - MaxRows}.");
        }

        void Section(string text)
        {
            var label = new Label(text);
            label.AddToClassList("tmg-sub-h");
            Add(label);
        }

        VisualElement Buttons()
        {
            var row = new VisualElement();
            row.AddToClassList("tmg-inspector__buttons");
            Add(row);
            return row;
        }

        void Hint(string text)
        {
            var hint = new Label(text);
            hint.AddToClassList("tmg-hint");
            Add(hint);
        }

        static Label Note(string text)
        {
            var label = new Label(text);
            label.AddToClassList("tmg-analysis__row");
            return label;
        }

        Label Link(string text, string itemId)
        {
            var label = Note(text);
            label.AddToClassList("tmg-wmsg--link");
            label.tooltip = "Перейти к элементу";
            label.RegisterCallback<ClickEvent>(_ => _focusItem(itemId));
            return label;
        }
    }
}
