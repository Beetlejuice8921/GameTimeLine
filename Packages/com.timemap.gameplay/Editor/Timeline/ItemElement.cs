using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimeMapGameplay.Editor
{
    /// <summary>Visual for a clip or marker on a lane.</summary>
    public sealed class ItemElement : VisualElement
    {
        const float MinClipWidth = 14f;

        readonly Label _label;
        readonly VisualElement _diamond;
        Label _tag;
        bool _selected;
        List<ValidationIssue> _issues;
        bool _issuesStale;
        AxisUnit _units;
        string _note;

        public TimelineItem Item { get; }
        public TrackBase Track { get; }
        public bool IsClip => Item is ClipBase;
        public bool HasIssues => _issues is { Count: > 0 };

        /// <summary>Right-edge handle: changes the duration, keeping the start; null for markers.</summary>
        public VisualElement ResizeHandle { get; }

        /// <summary>Left-edge handle: moves the start, keeping the end; null for markers.</summary>
        public VisualElement ResizeStartHandle { get; }

        public ItemElement(TimelineItem item, TrackBase track)
        {
            Item = item;
            Track = track;
            AddToClassList("tmg-item");

            if (IsClip)
            {
                AddToClassList("tmg-clip");
                _label = new Label { pickingMode = PickingMode.Ignore };
                _label.AddToClassList("tmg-clip__name");
                Add(_label);

                ResizeHandle = new VisualElement { tooltip = "Длительность" };
                ResizeHandle.AddToClassList("tmg-clip__resize");
                Add(ResizeHandle);

                ResizeStartHandle = new VisualElement { tooltip = "Начало (конец остаётся на месте)" };
                ResizeStartHandle.AddToClassList("tmg-clip__resize");
                ResizeStartHandle.AddToClassList("tmg-clip__resize--start");
                Add(ResizeStartHandle);
            }
            else
            {
                AddToClassList("tmg-marker");
                _diamond = new VisualElement { pickingMode = PickingMode.Ignore };
                _diamond.AddToClassList("tmg-marker__diamond");
                Add(_diamond);

                _label = new Label { pickingMode = PickingMode.Ignore };
                _label.AddToClassList("tmg-marker__name");
                Add(_label);
            }

            ApplyColors();
        }

        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                EnableInClassList("tmg-item--selected", value);
                ApplyColors();
            }
        }

        /// <summary>Small secondary text next to the name (e.g. "#3", "ур.10"); null hides it.</summary>
        public void SetTag(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                if (_tag != null) _tag.style.display = DisplayStyle.None;
                return;
            }
            if (_tag == null)
            {
                _tag = new Label { pickingMode = PickingMode.Ignore };
                _tag.AddToClassList("tmg-item__tag");
                if (IsClip) Insert(0, _tag);
                else Insert(IndexOf(_label) + 1, _tag);
            }
            _tag.style.display = DisplayStyle.Flex;
            _tag.text = text;
        }

        /// <summary>Extra tooltip line (e.g. telemetry summary); null removes it.</summary>
        public void SetNote(string note)
        {
            if (_note == note) return;
            _note = note;
            UpdateTooltip();
        }

        /// <summary>Validation issues of this item; <paramref name="stale"/> dims the mark.</summary>
        public void SetIssues(List<ValidationIssue> issues, bool stale)
        {
            _issues = issues;
            _issuesStale = stale;
            EnableInClassList("tmg-item--warn", HasIssues);
            EnableInClassList("tmg-item--warn-stale", HasIssues && stale);
            ApplyColors();
            UpdateTooltip();
        }

        /// <summary>Updates position, size and label from the underlying item.</summary>
        public void Layout(TimelineViewport viewport, AxisUnit units = AxisUnit.Hours)
        {
            _units = units;
            _label.text = Item.Name;
            ApplyColors();
            style.left = viewport.TimeToPixel(Item.Start);
            if (Item is ClipBase clip)
                style.width = Mathf.Max(viewport.TimeToPixel(clip.Duration) - 2f, MinClipWidth);
            UpdateTooltip();
        }

        void UpdateTooltip()
        {
            string text = Item is ClipBase c
                ? $"{Item.Name}\n{TimeMath.Format(c.Start, _units)} – {TimeMath.Format(c.End, _units)}"
                : $"{Item.Name}\n{TimeMath.Format(Item.Start, _units)}";
            if (Item.Binding != null) text += $"\nАссет: {Item.Binding.name}";
            if (!string.IsNullOrEmpty(_note)) text += "\n" + _note;
            if (HasIssues)
                text += (_issuesStale ? "\n\nНарушения (не проверено после изменений):\n" : "\n\nНарушения:\n")
                        + string.Join("\n", _issues.Select(i => "• " + i.Message));
            tooltip = text;
        }

        void ApplyColors()
        {
            var color = Track.Color;
            var warn = _issuesStale ? EditorTheme.StaleWarn : EditorTheme.Warn;
            if (!IsClip)
            {
                _diamond.style.backgroundColor = HasIssues ? warn : color;
                return;
            }

            var border = _selected ? EditorTheme.Selection : HasIssues ? warn : EditorTheme.ClipBorder(color);
            style.backgroundColor = Color.Lerp(EditorTheme.ClipBase, HasIssues ? warn : color, HasIssues ? 0.22f : EditorTheme.ClipTint);
            style.borderTopColor = border;
            style.borderRightColor = border;
            style.borderBottomColor = border;
            style.borderLeftColor = HasIssues ? warn : color;
        }
    }
}
