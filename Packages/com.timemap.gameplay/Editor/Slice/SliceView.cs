using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimeMapGameplay.Editor
{
    /// <summary>Game slice above the timeline: header facts and discovered panels in three columns.</summary>
    public sealed class SliceView : VisualElement
    {
        readonly Label _facts;
        readonly VisualElement _abPanel;
        readonly List<ISlicePanel> _panels = new();

        public SliceView()
        {
            AddToClassList("tmg-slice__root");

            var header = new VisualElement();
            header.AddToClassList("tmg-slice__header");
            var eyebrow = new Label("Срез игры");
            eyebrow.AddToClassList("tmg-eyebrow");
            header.Add(eyebrow);
            _facts = new Label();
            _facts.AddToClassList("tmg-slice__facts");
            header.Add(_facts);
            Add(header);

            _abPanel = new VisualElement();
            _abPanel.AddToClassList("tmg-ab");
            _abPanel.style.display = DisplayStyle.None;
            Add(_abPanel);

            var columnsRoot = new VisualElement();
            columnsRoot.AddToClassList("tmg-slice__columns");
            Add(columnsRoot);

            var columns = new[] { Column(columnsRoot, "tmg-slice__column--wide"), Column(columnsRoot, null), Column(columnsRoot, null) };
            var counts = new int[3];
            foreach (var (type, attribute) in Discover())
            {
                ISlicePanel panel;
                try
                {
                    panel = (ISlicePanel)Activator.CreateInstance(type);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    continue;
                }
                int column = attribute.Column is >= 0 and <= 2 ? attribute.Column : counts[1] <= counts[2] ? 1 : 2;
                counts[column]++;
                _panels.Add(panel);
                columns[column].Add(panel.Root);
            }
        }

        public IReadOnlyList<ISlicePanel> Panels => _panels;

        /// <summary>Panel types to show, ordered by column and order; replaced panels are skipped.</summary>
        public static List<(Type type, SlicePanelAttribute attribute)> Discover()
            => Order(TypeCache.GetTypesWithAttribute<SlicePanelAttribute>()
                .Where(t => typeof(ISlicePanel).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => (t, (SlicePanelAttribute)t.GetCustomAttributes(typeof(SlicePanelAttribute), false)[0])));

        /// <summary>Drops replaced panels and sorts by order.</summary>
        public static List<(Type type, SlicePanelAttribute attribute)> Order(IEnumerable<(Type type, SlicePanelAttribute attribute)> panels)
        {
            var all = panels.ToList();
            var replaced = new HashSet<string>(all.Where(p => !string.IsNullOrEmpty(p.attribute.Replaces)).Select(p => p.attribute.Replaces));
            return all.Where(p => !replaced.Contains(p.attribute.Id))
                .OrderBy(p => p.attribute.Order)
                .ThenBy(p => p.type.FullName)
                .ToList();
        }

        /// <summary>Refreshes cards for time A; with <paramref name="timeB"/> also shows what changes from A to B.</summary>
        public void Refresh(ProgressionTimeline timeline, float time, float? timeB = null)
        {
            if (timeline == null) return;
            var state = timeline.Evaluate(time);
            RefreshAb(timeline, state, timeB);

            var facts = new List<string> { $"t {TimeMath.Format(time, timeline.Axis.Units)}" };
            if (state.FirstOf<PlotTrack, ClipTrackState>()?.Current is { } act) facts.Add(act.Name);
            if (state.Level is { } level) facts.Add($"ур. {Mathf.FloorToInt(level + 0.0001f)}");
            _facts.text = string.Join("   ·   ", facts);

            foreach (var panel in _panels)
            {
                try
                {
                    panel.Refresh(state);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        void RefreshAb(ProgressionTimeline timeline, ProgressionState stateA, float? timeB)
        {
            _abPanel.Clear();
            _abPanel.style.display = timeB.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            if (!timeB.HasValue) return;

            var stateB = timeline.Evaluate(timeB.Value);
            var units = timeline.Axis.Units;
            string F(float t) => TimeMath.Format(t, units);
            float span = timeB.Value - stateA.Time;

            var title = new Label($"Сравнение  A {F(stateA.Time)} → B {F(timeB.Value)}  ({(span >= 0 ? "+" : "−")}{F(Mathf.Abs(span))})");
            title.AddToClassList("tmg-ab__title");
            _abPanel.Add(title);

            var deltas = ProgressionDiff.Compare(stateA, stateB).Where(d => d.HasChanges).ToList();
            if (deltas.Count == 0)
            {
                var none = new Label("Между A и B ничего не меняется.");
                none.AddToClassList("tmg-muted");
                _abPanel.Add(none);
                return;
            }

            foreach (var delta in deltas)
            {
                var row = new VisualElement();
                row.AddToClassList("tmg-ab__row");
                var dot = new VisualElement();
                dot.AddToClassList("tmg-card__dot");
                dot.style.backgroundColor = delta.Track.Color;
                row.Add(dot);
                var name = new Label(delta.Track.Name);
                name.AddToClassList("tmg-ab__track");
                row.Add(name);
                var text = new Label(Describe(delta));
                text.AddToClassList("tmg-ab__text");
                row.Add(text);
                _abPanel.Add(row);
            }
        }

        /// <summary>Human-readable A → B change of one track.</summary>
        public static string Describe(TrackDelta delta)
        {
            var parts = new List<string>();
            if (delta.ValueChanged)
            {
                float a = delta.ValueA.Value, b = delta.ValueB.Value;
                string label = delta.Track is CurveTrack curve ? curve.ValueLabel : "";
                parts.Add($"{label}{a:0.#} → {label}{b:0.#} ({(b >= a ? "+" : "−")}{Mathf.Abs(b - a):0.#})");
            }
            if (delta.CurrentChanged)
                parts.Add($"{delta.CurrentA?.Name ?? "—"} → {delta.CurrentB?.Name ?? "—"}");
            if (delta.Gained.Count > 0)
                parts.Add($"+{delta.Gained.Count}: {string.Join(", ", delta.Gained.Select(i => i.Name))}");
            if (delta.Lost.Count > 0)
                parts.Add($"−{delta.Lost.Count}: {string.Join(", ", delta.Lost.Select(i => i.Name))}");
            return string.Join("   ·   ", parts);
        }

        static VisualElement Column(VisualElement parent, string extraClass)
        {
            var column = new VisualElement();
            column.AddToClassList("tmg-slice__column");
            if (extraClass != null) column.AddToClassList(extraClass);
            parent.Add(column);
            return column;
        }
    }
}
