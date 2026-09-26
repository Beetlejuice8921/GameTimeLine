using System.Linq;
using NUnit.Framework;
using TimeMapGameplay.Editor;
using UnityEditor;
using UnityEngine;

namespace TimeMapGameplay.Tests
{
    public class EvaluateTests
    {
        ProgressionTimeline _demo;

        [SetUp]
        public void SetUp() => _demo = DemoTimelineFactory.CreateDemo();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_demo);

        [Test]
        public void Evaluate_ReturnsStatePerTrack()
        {
            var state = _demo.Evaluate(3.5f);
            Assert.That(state.Time, Is.EqualTo(3.5f));
            Assert.That(state.Tracks, Has.Count.EqualTo(_demo.Tracks.Count));
            Assert.That(state.Of(_demo.Tracks[2]).Track, Is.SameAs(_demo.Tracks[2]));
        }

        [Test]
        public void Evaluate_DemoAt3h30()
        {
            var state = _demo.Evaluate(3.5f);

            var plot = state.FirstOf<PlotTrack, ClipTrackState>();
            Assert.That(plot.Current.Name, Is.EqualTo("Акт I · Пробуждение"));
            Assert.That(plot.CurrentProgress, Is.EqualTo(0.7f).Within(1e-5f));

            // Level keys (2 → 4) and (5 → 8): 4 + 1.5/3 * 4 = 6.
            Assert.That(state.Level, Is.EqualTo(6f).Within(1e-4f));

            var skills = state.FirstOf<SkillTrack, MarkerTrackState>();
            Assert.That(skills.Reached.Select(s => s.Name), Is.EqualTo(new[] { "Двойной прыжок" }));
            Assert.That(skills.Next.Name, Is.EqualTo("Рывок"));

            var locations = state.FirstOf<LocationTrack, MarkerTrackState>();
            Assert.That(locations.Reached.Select(l => l.Name), Is.EqualTo(new[] { "Деревня", "Лес" }));
            Assert.That(locations.Latest.Name, Is.EqualTo("Лес"));

            var fabula = state.FirstOf<FabulaTrack, ClipTrackState>();
            Assert.That(fabula.Started.Select(f => f.Name), Is.EqualTo(new[] { "Пробуждение", "Детство героя" }));
            Assert.That(fabula.Current.Name, Is.EqualTo("Детство героя"));
        }

        [Test]
        public void Evaluate_BetweenClips_HasNoCurrent()
        {
            var fabula = _demo.Evaluate(2f).FirstOf<FabulaTrack, ClipTrackState>();
            Assert.That(fabula.Current, Is.Null);
            Assert.That(fabula.CurrentProgress, Is.EqualTo(0f));
        }

        [Test]
        public void Evaluate_AtEnd_EverythingReached()
        {
            var state = _demo.Evaluate(_demo.Axis.Length);
            Assert.That(state.FirstOf<SkillTrack, MarkerTrackState>().Upcoming, Is.Empty);
            Assert.That(state.FirstOf<FabulaTrack, ClipTrackState>().Started, Has.Count.EqualTo(5));
            Assert.That(state.Level, Is.EqualTo(30f));
        }

        [Test]
        public void Evaluate_DoesNotModifyAsset()
        {
            string before = EditorJsonUtility.ToJson(_demo);
            for (float t = 0f; t <= 20f; t += 0.5f)
                _demo.Evaluate(t);
            Assert.That(EditorJsonUtility.ToJson(_demo), Is.EqualTo(before));
        }

        [Test]
        public void Equipped_IsLatestPerSlot()
        {
            var equipped = EquipmentPanel.Equipped(_demo.Evaluate(13f));
            Assert.That(equipped.Select(u => u.Name), Is.EquivalentTo(new[] { "Меч III", "Броня III", "Конь" }));
            Assert.That(equipped.Sum(u => u.AttackBonus), Is.EqualTo(7));
        }

        [Test]
        public void MissingTrackType_YieldsNullState()
        {
            var empty = ScriptableObject.CreateInstance<ProgressionTimeline>();
            try
            {
                var state = empty.Evaluate(1f);
                Assert.That(state.FirstOf<PlotTrack, ClipTrackState>(), Is.Null);
                Assert.That(state.Level, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(empty);
            }
        }
    }
}
