using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TimeMapGameplay.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimeMapGameplay.Tests
{
    public class TimelineViewTests
    {
        ProgressionTimeline _timeline;
        TimelineEditing _editing;
        TimelineSelection _selection;
        TimelineView _view;

        [SetUp]
        public void SetUp()
        {
            _timeline = DemoTimelineFactory.CreateDemo();
            _editing = new TimelineEditing(() => _timeline);
            _selection = new TimelineSelection();
            var operations = new TimelineOperations(_editing, () => _timeline);
            _view = new TimelineView(_editing, operations, _selection, new List<string>());
            _selection.Changed += _view.SyncSelection;
            _editing.Changed += () => _view.Refresh();
            _view.SetTimeline(_timeline);
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearUndo(_timeline);
            Object.DestroyImmediate(_timeline);
        }

        ItemElement ElementOf(TimelineItem item) => _view.Query<ItemElement>().Where(e => e.Item == item).First();

        FabulaTrack Fabula => (FabulaTrack)_timeline.Tracks[0];
        SkillTrack Skills => (SkillTrack)_timeline.Tracks[2];

        [Test]
        public void Rebuild_CreatesElementPerItemAndCurveLane()
        {
            int expected = _timeline.Tracks.Sum(t => t.Items.Count());
            Assert.That(_view.Query<ItemElement>().ToList(), Has.Count.EqualTo(expected));
            Assert.That(_view.Query<CurveLane>().ToList(), Has.Count.EqualTo(1));
        }

        [Test]
        public void DragItems_SnapsAndClamps()
        {
            var clip = Fabula.Clips[0];
            var element = ElementOf(clip);

            Assert.That(_view.BeginItemDrag(element, additive: false), Is.True);
            _view.DragItems(element, 3.13f, snap: true);
            Assert.That(clip.Start, Is.EqualTo(3.25f).Within(1e-5f));

            _view.DragItems(element, 3.13f, snap: false);
            Assert.That(clip.Start, Is.EqualTo(3.13f).Within(1e-5f));

            _view.DragItems(element, 100f, snap: true);
            Assert.That(clip.End, Is.EqualTo(_timeline.Axis.Length).Within(1e-5f));
        }

        [Test]
        public void DragItems_MovesWholeSelectionAndStopsAtAxisStart()
        {
            var a = Skills.Markers[0]; // 1.5
            var b = Skills.Markers[1]; // 4
            _selection.Set(new[] { a.Id, b.Id });

            var element = ElementOf(b);
            Assert.That(_view.BeginItemDrag(element, additive: false), Is.True);
            _view.DragItems(element, 1f, snap: true);
            Assert.That(a.Start, Is.EqualTo(2.5f).Within(1e-5f));
            Assert.That(b.Start, Is.EqualTo(5f).Within(1e-5f));

            // The group cannot move further left than the earliest item allows.
            _view.DragItems(element, -10f, snap: true);
            Assert.That(a.Start, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(b.Start, Is.EqualTo(2.5f).Within(1e-5f));
        }

        [Test]
        public void AdditiveClick_TogglesWithoutDrag()
        {
            var a = Skills.Markers[0];
            var b = Skills.Markers[1];
            _selection.Select(a.Id);

            Assert.That(_view.BeginItemDrag(ElementOf(b), additive: true), Is.False);
            Assert.That(_selection.Items, Is.EquivalentTo(new[] { a.Id, b.Id }));

            _view.BeginItemDrag(ElementOf(a), additive: true);
            Assert.That(_selection.Items, Is.EquivalentTo(new[] { b.Id }));
        }

        [Test]
        public void DragResize_SnapsEndAndKeepsMinimumStep()
        {
            var clip = Fabula.Clips[0]; // 0..1.25
            var element = ElementOf(clip);

            Assert.That(_view.BeginResize(element), Is.True);
            _view.DragResize(element, 0.85f, snap: true);
            Assert.That(clip.Duration, Is.EqualTo(2f).Within(1e-5f));

            _view.DragResize(element, -5f, snap: true);
            Assert.That(clip.Duration, Is.EqualTo(_timeline.Axis.SnapStep).Within(1e-5f));
        }

        [Test]
        public void DragResizeStart_MovesStartAndKeepsEnd()
        {
            var clip = Fabula.Clips[1]; // 2.5..3.75
            var element = ElementOf(clip);

            Assert.That(_view.BeginResize(element), Is.True);
            _view.DragResizeStart(element, -1.1f, snap: true);
            Assert.That(clip.Start, Is.EqualTo(1.5f).Within(1e-5f));
            Assert.That(clip.End, Is.EqualTo(3.75f).Within(1e-5f));

            _view.DragResizeStart(element, 5f, snap: true);
            Assert.That(clip.Start, Is.EqualTo(3.5f).Within(1e-5f), "keeps at least one snap step");
            Assert.That(clip.End, Is.EqualTo(3.75f).Within(1e-5f));

            _view.DragResizeStart(element, -10f, snap: true);
            Assert.That(clip.Start, Is.EqualTo(0f));
            Assert.That(clip.Duration, Is.EqualTo(3.75f).Within(1e-5f));
        }

        [Test]
        public void Clip_HasHandlesOnBothEdges()
        {
            var element = ElementOf(Fabula.Clips[0]);
            Assert.That(element.ResizeHandle, Is.Not.Null);
            Assert.That(element.ResizeStartHandle, Is.Not.Null);
            Assert.That(ElementOf(Skills.Markers[0]).ResizeStartHandle, Is.Null);
        }

        [Test]
        public void Gesture_IsSingleUndoStep()
        {
            var marker = Skills.Markers[0];
            float original = marker.Start;
            var element = ElementOf(marker);

            _view.BeginItemDrag(element, additive: false);
            _editing.BeginGesture("Move Items");
            _view.DragItems(element, 1f, snap: true);
            _view.DragItems(element, 2f, snap: true);
            _view.DragItems(element, 3f, snap: true);
            _editing.EndGesture();
            Assert.That(marker.Start, Is.EqualTo(original + 3f).Within(1e-5f));

            Undo.PerformUndo();
            Assert.That(marker.Start, Is.EqualTo(original).Within(1e-5f));
        }

        [Test]
        public void Selection_HighlightsItemsAndTrackHead()
        {
            var marker = ((MarkerTrack)_timeline.Tracks[3]).Markers[2];
            _selection.Select(marker.Id);

            var selected = _view.Query<ItemElement>().Where(e => e.Selected).ToList();
            Assert.That(selected, Has.Count.EqualTo(1));
            Assert.That(selected[0].Item, Is.SameAs(marker));

            _selection.SelectTrack(Skills.Id);
            Assert.That(_view.Query<ItemElement>().Where(e => e.Selected).ToList(), Is.Empty);
            Assert.That(_view.Query(className: "tmg-head--selected").ToList(), Has.Count.EqualTo(1));
        }

        [Test]
        public void Collapse_HidesItemsOfTrack()
        {
            int before = _view.Query<ItemElement>().ToList().Count;
            _view.SetCollapsed(Skills, true);
            Assert.That(_view.Query<ItemElement>().ToList(), Has.Count.EqualTo(before - Skills.Markers.Count));
            _view.SetCollapsed(Skills, false);
            Assert.That(_view.Query<ItemElement>().ToList(), Has.Count.EqualTo(before));
        }

        [Test]
        public void Refresh_RebuildsOnlyOnStructureChange()
        {
            Assert.That(_view.Refresh(), Is.False);
            Fabula.Clips[0].Start = 2f;
            Fabula.Name = "Renamed";
            Assert.That(_view.Refresh(), Is.False, "moves and renames only re-layout");
            Fabula.Clips.RemoveAt(0);
            Assert.That(_view.Refresh(), Is.True);
        }

        [Test]
        public void WindowLayout_ContainsRequiredElements()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.timemap.gameplay/Editor/UI/ProgressionTimelineWindow.uxml");
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.timemap.gameplay/Editor/UI/ProgressionTimelineWindow.uss");
            Assert.That(tree, Is.Not.Null);
            Assert.That(style, Is.Not.Null);

            var root = tree.Instantiate();
            foreach (var name in new[]
                     {
                         "play-button", "time-label", "zoom-slider", "snap-label", "asset-field",
                         "playmode-button", "save-button", "check-button", "auto-toggle",
                         "empty-state", "create-demo-button", "main", "slice-host", "timeline-host", "inspector-host"
                     })
                Assert.That(root.Q(name), Is.Not.Null, name);
        }

        [Test]
        public void Slice_BuildsFiveCards()
        {
            var slice = new SliceView();
            slice.Refresh(_timeline, 3.5f);
            Assert.That(slice.Query(className: "tmg-card").ToList(), Has.Count.GreaterThanOrEqualTo(5), "five built-in cards plus project ones");
            Assert.That(slice.Query(className: "tmg-card__empty").ToList(), Is.Empty);
        }

        [Test]
        public void Inspector_FindsItemAndTrackProperties()
        {
            var so = new SerializedObject(_timeline);
            var clip = Fabula.Clips[2];

            var itemProperty = InspectorPaths.FindManagedReference(so, clip.Id);
            Assert.That(itemProperty, Is.Not.Null);
            Assert.That(itemProperty.FindPropertyRelative("_chronoIndex").intValue, Is.EqualTo(((FabulaEvent)clip).ChronoIndex));

            var trackProperty = InspectorPaths.FindManagedReference(so, Skills.Id);
            Assert.That(trackProperty.FindPropertyRelative("_name").stringValue, Is.EqualTo(Skills.Name));
        }
    }
}
