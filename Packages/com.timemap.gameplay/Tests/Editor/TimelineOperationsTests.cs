using System.Linq;
using NUnit.Framework;
using TimeMapGameplay.Editor;
using UnityEditor;
using UnityEngine;

namespace TimeMapGameplay.Tests
{
    public class TimelineOperationsTests
    {
        ProgressionTimeline _timeline;
        TimelineOperations _operations;

        [SetUp]
        public void SetUp()
        {
            _timeline = DemoTimelineFactory.CreateDemo();
            _operations = new TimelineOperations(new TimelineEditing(() => _timeline), () => _timeline);
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearUndo(_timeline);
            Object.DestroyImmediate(_timeline);
        }

        FabulaTrack Fabula => (FabulaTrack)_timeline.Tracks[0];
        SkillTrack Skills => (SkillTrack)_timeline.Tracks[2];
        LevelCurveTrack Level => (LevelCurveTrack)_timeline.Tracks[5];

        [Test]
        public void ClampGroupDelta_KeepsGroupOnAxis()
        {
            var ranges = new[] { (1f, 2f), (5f, 8f) };
            Assert.That(TimelineOperations.ClampGroupDelta(ranges, -3f, 20f), Is.EqualTo(-1f));
            Assert.That(TimelineOperations.ClampGroupDelta(ranges, 15f, 20f), Is.EqualTo(12f));
            Assert.That(TimelineOperations.ClampGroupDelta(ranges, 2f, 20f), Is.EqualTo(2f));
        }

        [Test]
        public void AddItem_UsesTrackItemType()
        {
            var item = _operations.AddItem(Fabula, 3f);
            Assert.That(item, Is.TypeOf<FabulaEvent>());
            Assert.That(Fabula.Clips, Does.Contain(item));
            Assert.That(item.Start, Is.EqualTo(3f));
        }

        [Test]
        public void Delete_ThenUndo_RestoresItem()
        {
            var skill = Skills.Markers[1];
            _operations.DeleteItems(new[] { skill.Id });
            Assert.That(_timeline.TryFindItem(skill.Id, out _, out _), Is.False);

            Undo.PerformUndo();
            Assert.That(_timeline.TryFindItem(skill.Id, out var restored, out var track), Is.True);
            Assert.That(track, Is.SameAs(_timeline.Tracks[2]));
            Assert.That(restored, Is.TypeOf<SkillMarker>());
        }

        [Test]
        public void CopyPaste_KeepsOffsetsTypesAndCreatesNewIds()
        {
            var a = (SkillMarker)Skills.Markers[0]; // 1.5
            var b = (SkillMarker)Skills.Markers[1]; // 4
            _operations.Copy(new[] { b.Id, a.Id });

            var pasted = _operations.Paste(10f);
            Assert.That(pasted, Has.Count.EqualTo(2));
            Assert.That(pasted, Has.None.EqualTo(a.Id).And.None.EqualTo(b.Id));

            var items = pasted.Select(id => _timeline.TryFindItem(id, out var i, out _) ? i : null).OrderBy(i => i.Start).ToList();
            Assert.That(items[0].Start, Is.EqualTo(10f).Within(1e-5f));
            Assert.That(items[1].Start, Is.EqualTo(12.5f).Within(1e-5f));
            Assert.That(items[0], Is.TypeOf<SkillMarker>());
            Assert.That(((SkillMarker)items[0]).MinLevel, Is.EqualTo(a.MinLevel));
            Assert.That(Skills.Markers, Has.Count.EqualTo(8));
        }

        [Test]
        public void Paste_NearAxisEnd_IsClamped()
        {
            var clip = Fabula.Clips[0]; // duration 1.25
            _operations.Copy(new[] { clip.Id });
            var pasted = _operations.Paste(19.5f);
            _timeline.TryFindItem(pasted[0], out var item, out _);
            Assert.That(item.End, Is.EqualTo(_timeline.Axis.Length).Within(1e-5f));
        }

        [Test]
        public void Duplicate_PlacesCopyAfterGroup()
        {
            var clip = Fabula.Clips[1]; // 2.5..3.75
            var ids = _operations.Duplicate(new[] { clip.Id });
            _timeline.TryFindItem(ids[0], out var copy, out var track);
            Assert.That(track, Is.SameAs(Fabula));
            Assert.That(copy.Start, Is.EqualTo(3.75f).Within(1e-5f));
            Assert.That(((FabulaEvent)copy).Text, Is.EqualTo(((FabulaEvent)clip).Text));
        }

        [Test]
        public void NudgeItems_ClampsGroup()
        {
            var first = Skills.Markers[0]; // 1.5
            _operations.NudgeItems(new[] { first.Id }, -5f);
            Assert.That(first.Start, Is.EqualTo(0f));
        }

        [Test]
        public void Tracks_AddMoveRemove()
        {
            var added = _operations.AddTrack(typeof(SkillTrack), "Пассивки");
            Assert.That(_timeline.Tracks.Last(), Is.SameAs(added));

            _operations.MoveTrack(added, -1);
            Assert.That(_timeline.Tracks.IndexOf(added), Is.EqualTo(_timeline.Tracks.Count - 2));

            _operations.RemoveTrack(added);
            Assert.That(_timeline.Tracks, Has.No.Member(added));
        }

        [Test]
        public void TrackTypes_IncludeBuiltInsWithMenuNames()
        {
            var names = TimelineOperations.TrackTypes().Select(t => t.name).ToList();
            Assert.That(names, Is.SupersetOf(new[] { "Фабула", "Сюжет", "Прокачка", "Локации", "Апгрейды", "Прогресс (уровень)" }));
        }

        [Test]
        public void CurveKey_StaysBetweenNeighboursAndSnaps()
        {
            // Keys: (0,1) (2,4) (5,8) ...
            _operations.SetCurveKey(Level, 1, 9f, 4.4f, snap: true);
            Assert.That(Level.Keys[1].time, Is.EqualTo(4.75f).Within(1e-5f), "clamped before next key minus one snap step");
            Assert.That(Level.Keys[1].value, Is.EqualTo(4f));

            _operations.SetCurveKey(Level, 1, 1f, 100f, snap: true);
            Assert.That(Level.Keys[1].time, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(Level.Keys[1].value, Is.EqualTo(Level.ValueMax));
        }

        [Test]
        public void CurveKey_AddInsertsSortedOnCurve()
        {
            int index = _operations.AddCurveKey(Level, 3f);
            Assert.That(index, Is.EqualTo(2));
            Assert.That(Level.Keys[2].time, Is.EqualTo(3f));
            Assert.That(Level.Keys[2].value, Is.EqualTo(5f), "4 + 1/3 * 4 ≈ 5.33, snapped to step 1");

            _operations.RemoveCurveKey(Level, index);
            Assert.That(Level.Keys.Any(k => Mathf.Approximately(k.time, 3f)), Is.False);
        }
    }
}
