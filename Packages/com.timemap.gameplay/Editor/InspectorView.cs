using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimeMapGameplay.Editor
{
    /// <summary>
    /// Inspector panel. Fields are PropertyFields bound to the asset, so project-specific item and track
    /// types get their fields (and Undo) automatically. Also shows requirements, validation issues and asset previews.
    /// </summary>
    public sealed class InspectorView : VisualElement
    {
        readonly TimelineOperations _operations;
        readonly TimelineSelection _selection;
        readonly ValidationService _validation;
        readonly Action<string> _focusItem;
        readonly AnalysisState _analysis;
        readonly Func<IReadOnlyList<TimelineChange>> _changes;
        readonly Label _kindLabel;
        readonly ScrollView _body;

        ProgressionTimeline _timeline;
        SerializedObject _serializedObject;
        TimelineItem _item;
        TrackBase _track;
        float _time;
        Label _rangeLabel;
        VisualElement _issuesBox;
        VisualElement _requirementsBox;
        VisualElement _previewBox;

        public InspectorView(TimelineOperations operations, TimelineSelection selection, ValidationService validation, Action<string> focusItem,
            AnalysisState analysis = null, Func<IReadOnlyList<TimelineChange>> changes = null)
        {
            _analysis = analysis;
            _changes = changes;
            _operations = operations;
            _selection = selection;
            _validation = validation;
            _focusItem = focusItem;
            AddToClassList("tmg-inspector__root");

            var header = new VisualElement();
            header.AddToClassList("tmg-inspector__header");
            header.Add(new Label("Inspector"));
            _kindLabel = new Label();
            _kindLabel.AddToClassList("tmg-muted");
            header.Add(_kindLabel);
            Add(header);

            _body = new ScrollView();
            _body.AddToClassList("tmg-inspector__body");
            Add(_body);
        }

        /// <summary>Rebuilds the panel for the current selection.</summary>
        public void Show(ProgressionTimeline timeline, float time)
        {
            _body.Unbind();
            _body.Clear();
            _timeline = timeline;
            _time = time;
            _item = null;
            _track = null;
            _rangeLabel = null;
            _issuesBox = null;
            _requirementsBox = null;
            _previewBox = null;
            _serializedObject = timeline != null ? new SerializedObject(timeline) : null;
            _kindLabel.text = "";
            if (timeline == null) return;

            if (_selection.Count == 1 && timeline.TryFindItem(_selection.Primary, out var item, out var track))
                BuildItem(item, track);
            else if (_selection.Count > 1)
                BuildMultiple();
            else if (_selection.TrackId != null && timeline.FindTrack(_selection.TrackId) is { } selectedTrack)
                BuildTrack(selectedTrack);
            else
                BuildTimeline();

            _body.Bind(_serializedObject);
        }

        /// <summary>Updates values that are not bound (bound fields refresh themselves).</summary>
        public void RefreshValues()
        {
            if (_item == null) return;
            if (_rangeLabel != null) _rangeLabel.text = RangeText(_item);
            RefreshRequirementResults();
            RefreshPreview();
        }

        /// <summary>Re-renders the validation section (after a check or when results become stale).</summary>
        public void RefreshIssues()
        {
            if (_issuesBox == null) return;
            _issuesBox.Clear();

            IEnumerable<ValidationIssue> issues;
            if (_item != null) issues = _validation.IssuesFor(_item.Id);
            else if (_track != null) issues = _validation.IssuesForTrack(_track.Id);
            else issues = _validation.Issues;
            var list = issues.ToList();

            bool report = _item == null && _track == null;
            if (!_validation.HasRun)
            {
                AddNote(_issuesBox, "Проверка ещё не запускалась — нажмите «Проверить».");
                return;
            }
            if (_validation.IsStale)
                AddNote(_issuesBox, "Не проверено после изменений: пометки ниже — с прошлой проверки.");
            if (list.Count == 0)
            {
                var ok = new Label(report ? "Нарушений нет." : "Нарушений нет.");
                ok.AddToClassList("tmg-okmsg");
                _issuesBox.Add(ok);
                return;
            }

            foreach (var issue in list)
            {
                var row = new Label(issue.Message);
                row.AddToClassList("tmg-wmsg");
                if (issue.Severity == IssueSeverity.Error) row.AddToClassList("tmg-wmsg--error");
                if (report && issue.ItemId != null)
                {
                    row.AddToClassList("tmg-wmsg--link");
                    row.tooltip = "Перейти к элементу";
                    string id = issue.ItemId;
                    row.RegisterCallback<ClickEvent>(_ => _focusItem(id));
                }
                _issuesBox.Add(row);
            }
        }

        // ---------- Item ----------

        void BuildItem(TimelineItem item, TrackBase track)
        {
            _item = item;
            _kindLabel.text = ObjectNames.NicifyVariableName(item.GetType().Name);
            AddTitle(track);

            _rangeLabel = new Label(RangeText(item));
            _rangeLabel.AddToClassList("tmg-inspector__range");
            _body.Add(_rangeLabel);

            var property = InspectorPaths.FindManagedReference(_serializedObject, item.Id);
            if (property != null)
                AddChildFields(property);

            _previewBox = new VisualElement();
            _previewBox.AddToClassList("tmg-preview");
            _body.Add(_previewBox);
            RefreshPreview();

            AddSection("Требования");
            _requirementsBox = new VisualElement();
            _body.Add(_requirementsBox);
            if (property != null)
                BuildRequirements(item, property.FindPropertyRelative("_requirements"));

            AddSection("Нарушения");
            _issuesBox = new VisualElement();
            _body.Add(_issuesBox);
            RefreshIssues();

            if (_analysis != null && _analysis.TryGetStats(item, out var stats))
            {
                AddSection("Телеметрия");
                var line = new Label(TelemetryOverlay.Describe(item, stats, _timeline.Axis.Units));
                line.AddToClassList("tmg-hint");
                _body.Add(line);
            }

            var change = _changes?.Invoke()?.FirstOrDefault(c => c.ItemId == item.Id);
            if (change != null)
            {
                AddSection("Относительно версии");
                foreach (var c in _changes().Where(c => c.ItemId == item.Id))
                    AddHint(c.Description);
            }
        }

        void BuildRequirements(TimelineItem item, SerializedProperty list)
        {
            for (int i = 0; i < item.Requirements.Count && list != null && i < list.arraySize; i++)
            {
                int index = i;
                var requirement = item.Requirements[i];
                var box = new VisualElement();
                box.AddToClassList("tmg-requirement");

                var header = new VisualElement();
                header.AddToClassList("tmg-requirement__header");
                var name = new Label(requirement == null ? "Неизвестный тип" : TimelineOperations.DisplayName(requirement.GetType()));
                name.AddToClassList("tmg-requirement__name");
                header.Add(name);
                var remove = new Button(() =>
                {
                    _operations.RemoveRequirement(item, index);
                    Show(_timeline, _time);
                }) { text = "✕", tooltip = "Удалить требование" };
                remove.AddToClassList("tmg-requirement__remove");
                header.Add(remove);
                box.Add(header);

                var element = list.GetArrayElementAtIndex(i);
                AddChildFields(element, box);

                var result = new Label { name = "requirement-result" };
                result.AddToClassList("tmg-requirement__result");
                result.userData = requirement;
                box.Add(result);

                _requirementsBox.Add(box);
            }

            var add = new Button { text = "+ Требование" };
            add.AddToClassList("tmg-requirement__add");
            add.clicked += () =>
            {
                var menu = new GenericDropdownMenu();
                foreach (var (type, name) in TimelineOperations.RequirementTypes())
                    menu.AddItem(name, false, () =>
                    {
                        _operations.AddRequirement(item, type);
                        Show(_timeline, _time);
                    });
                menu.DropDown(add.worldBound, add, DropdownMenuSizeMode.Auto);
            };
            _requirementsBox.Add(add);
            RefreshRequirementResults();
        }

        /// <summary>Live result of each requirement, independent of the validation mode.</summary>
        void RefreshRequirementResults()
        {
            if (_requirementsBox == null || _item == null) return;
            var context = new RequirementContext(_timeline, _item);
            foreach (var label in _requirementsBox.Query<Label>("requirement-result").ToList())
            {
                if (label.userData is not Requirement requirement) continue;
                bool ok = requirement.Check(context, out string message);
                label.text = ok ? "✓ выполняется" : message;
                label.EnableInClassList("tmg-requirement__result--fail", !ok);
            }
        }

        void RefreshPreview()
        {
            if (_previewBox == null || _item == null) return;
            _previewBox.Clear();
            var preview = PreviewSources.Get(_item.Binding);
            _previewBox.style.display = preview == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (preview == null) return;

            if (preview.Image != null)
            {
                var image = new Image { image = preview.Image, scaleMode = ScaleMode.ScaleToFit };
                image.AddToClassList("tmg-preview__image");
                _previewBox.Add(image);
            }
            var texts = new VisualElement();
            texts.AddToClassList("tmg-preview__texts");
            var title = new Label(preview.Title);
            title.AddToClassList("tmg-preview__title");
            texts.Add(title);
            if (!string.IsNullOrEmpty(preview.Description))
            {
                var description = new Label(preview.Description);
                description.AddToClassList("tmg-preview__description");
                texts.Add(description);
            }
            _previewBox.Add(texts);
        }

        // ---------- Multi-selection ----------

        void BuildMultiple()
        {
            var resolved = _selection.Items
                .Select(id => _timeline.TryFindItem(id, out var i, out var t) ? (item: i, track: t) : default)
                .Where(r => r.item != null)
                .OrderBy(r => r.item.Start)
                .ToList();
            _kindLabel.text = $"Выбрано: {resolved.Count}";

            foreach (var (item, track) in resolved)
            {
                var row = new VisualElement();
                row.AddToClassList("tmg-inspector__list-row");
                var swatch = new VisualElement();
                swatch.AddToClassList("tmg-inspector__swatch");
                swatch.style.backgroundColor = track.Color;
                row.Add(swatch);
                row.Add(new Label(item.Name));
                if (_validation.IssuesFor(item.Id).Any())
                {
                    var warn = new Label("⚠");
                    warn.AddToClassList("tmg-inspector__list-warn");
                    row.Add(warn);
                }
                var at = new Label(TimeMath.Format(item.Start, _timeline.Axis.Units));
                at.AddToClassList("tmg-muted");
                at.AddToClassList("tmg-inspector__list-time");
                row.Add(at);
                _body.Add(row);
            }

            var ids = resolved.Select(r => r.item.Id).ToList();
            var buttons = new VisualElement();
            buttons.AddToClassList("tmg-inspector__buttons");
            buttons.Add(new Button(() => _selection.Set(_operations.Duplicate(ids))) { text = "Дублировать" });
            buttons.Add(new Button(() => _operations.Copy(ids)) { text = "Копировать" });
            buttons.Add(new Button(() => _operations.DeleteItems(ids)) { text = "Удалить" });
            _body.Add(buttons);

            AddHint("Перетаскивание любого из выбранных двигает всю группу. Alt+←/→ — сдвиг на шаг снэпа.");
        }

        // ---------- Track ----------

        void BuildTrack(TrackBase track)
        {
            _track = track;
            _kindLabel.text = "Трек · " + ObjectNames.NicifyVariableName(track.GetType().Name);
            AddTitle(track);

            var property = InspectorPaths.FindManagedReference(_serializedObject, track.Id);
            if (property != null)
                AddChildFields(property);

            var buttons = new VisualElement();
            buttons.AddToClassList("tmg-inspector__buttons");
            if (track.ItemType != null)
            {
                buttons.Add(new Button(() =>
                {
                    var item = _operations.AddItem(track, _time);
                    if (item != null) _selection.Select(item.Id);
                }) { text = $"Добавить в {TimeMath.Format(_time, _timeline.Axis.Units)}" });
            }
            else if (track is CurveTrack curve)
            {
                buttons.Add(new Button(() => _operations.AddCurveKey(curve, TimeMath.Snap(_time, _timeline.Axis.SnapStep)))
                    { text = $"Ключ в {TimeMath.Format(_time, _timeline.Axis.Units)}" });
            }
            buttons.Add(new Button(() =>
            {
                if (EditorUtility.DisplayDialog("Удалить трек", $"Удалить трек «{track.Name}» со всеми элементами?", "Удалить", "Отмена"))
                    _operations.RemoveTrack(track);
            }) { text = "Удалить трек" });
            _body.Add(buttons);

            AddSection("Нарушения на треке");
            _issuesBox = new VisualElement();
            _body.Add(_issuesBox);
            RefreshIssues();
        }

        // ---------- Timeline ----------

        void BuildTimeline()
        {
            _kindLabel.text = "Таймлайн";

            AddChildFields(_serializedObject.FindProperty("_axis"));

            int items = _timeline.Tracks.Where(t => t != null).Sum(t => t.Items.Count());
            AddHint($"Треков: {_timeline.Tracks.Count(t => t != null)}, элементов: {items}.");

            AddSection("Отчёт проверки");
            _issuesBox = new VisualElement();
            _body.Add(_issuesBox);
            RefreshIssues();

            if (_analysis != null)
                _body.Add(new AnalysisSection(_analysis, _timeline, _changes?.Invoke(), _focusItem));

            AddHint("Мышь: тянуть — перенос (всей выделенной группы), края клипа — начало и длительность, Shift — без снэпа, " +
                    "Ctrl+клик — добавить к выделению, рамка по пустому месту — выделение, ПКМ — меню.\n" +
                    "Клавиши: Space — play, ←/→ — плейхед, Alt+←/→ — сдвиг выделенного, Esc — снять выделение, " +
                    "Ctrl+C/V/D, Delete, Ctrl+A. Ctrl+колесо — зум.");
        }

        // ---------- Helpers ----------

        void AddTitle(TrackBase track)
        {
            var title = new VisualElement();
            title.AddToClassList("tmg-inspector__title");
            var swatch = new VisualElement();
            swatch.AddToClassList("tmg-inspector__swatch");
            swatch.style.backgroundColor = track.Color;
            title.Add(swatch);
            title.Add(new Label(track.Name));
            _body.Add(title);
        }

        void AddSection(string text)
        {
            var label = new Label(text);
            label.AddToClassList("tmg-sub-h");
            _body.Add(label);
        }

        void AddChildFields(SerializedProperty parent, VisualElement target = null)
        {
            target ??= _body;
            var iterator = parent.Copy();
            var end = parent.GetEndProperty();
            if (!iterator.NextVisible(true)) return;
            while (!SerializedProperty.EqualContents(iterator, end))
            {
                target.Add(new PropertyField(iterator.Copy()));
                if (!iterator.NextVisible(false)) break;
            }
        }

        void AddHint(string text)
        {
            var hint = new Label(text);
            hint.AddToClassList("tmg-hint");
            _body.Add(hint);
        }

        static void AddNote(VisualElement parent, string text)
        {
            var note = new Label(text);
            note.AddToClassList("tmg-hint");
            parent.Add(note);
        }

        string RangeText(TimelineItem item)
        {
            var units = _timeline.Axis.Units;
            return item is ClipBase clip
                ? $"{TimeMath.Format(clip.Start, units)} – {TimeMath.Format(clip.End, units)}  ({TimeMath.Format(clip.Duration, units)})"
                : TimeMath.Format(item.Start, units);
        }
    }

    public static class InspectorPaths
    {
        /// <summary>Finds the managed-reference property (track or item) whose "_id" equals <paramref name="id"/>.</summary>
        public static SerializedProperty FindManagedReference(SerializedObject serializedObject, string id)
        {
            var iterator = serializedObject.GetIterator();
            while (iterator.Next(true))
            {
                if (iterator.propertyType != SerializedPropertyType.ManagedReference) continue;
                var idProperty = iterator.FindPropertyRelative("_id");
                if (idProperty != null && idProperty.propertyType == SerializedPropertyType.String && idProperty.stringValue == id)
                    return iterator.Copy();
            }
            return null;
        }
    }
}
