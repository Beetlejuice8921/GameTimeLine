using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TimeMapGameplay.Tests
{
    public class CurveAndRequirementTests
    {
        static CurveTrack Curve(params (float t, float v)[] keys)
        {
            var track = new CurveTrack { ValueMin = 0f, ValueMax = 100f };
            foreach (var (t, v) in keys) track.Keys.Add(new CurveKey(t, v));
            return track;
        }

        [Test]
        public void Curve_InterpolatesLinearly()
        {
            var curve = Curve((0f, 1f), (2f, 5f), (4f, 5f));
            Assert.That(curve.EvaluateValue(1f), Is.EqualTo(3f).Within(1e-5f));
            Assert.That(curve.EvaluateValue(2f), Is.EqualTo(5f).Within(1e-5f));
            Assert.That(curve.EvaluateValue(3f), Is.EqualTo(5f).Within(1e-5f));
        }

        [Test]
        public void Curve_ClampsOutsideKeys()
        {
            var curve = Curve((1f, 2f), (3f, 6f));
            Assert.That(curve.EvaluateValue(-5f), Is.EqualTo(2f));
            Assert.That(curve.EvaluateValue(99f), Is.EqualTo(6f));
        }

        [Test]
        public void Curve_WorksWithUnsortedKeys()
        {
            var curve = Curve((4f, 10f), (0f, 0f), (2f, 2f));
            Assert.That(curve.EvaluateValue(1f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(curve.EvaluateValue(3f), Is.EqualTo(6f).Within(1e-5f));
        }

        [Test]
        public void Curve_EmptyAndSingleKey()
        {
            var empty = new CurveTrack { ValueMin = 7f };
            Assert.That(empty.EvaluateValue(3f), Is.EqualTo(7f));
            Assert.That(Curve((5f, 12f)).EvaluateValue(0f), Is.EqualTo(12f));
        }

        [Test]
        public void Curve_FirstTimeReaching()
        {
            var curve = Curve((0f, 1f), (2f, 5f), (4f, 9f));
            Assert.That(curve.FirstTimeReaching(3f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(curve.FirstTimeReaching(1f), Is.EqualTo(0f));
            Assert.That(curve.FirstTimeReaching(10f), Is.Null);
        }

        [Test]
        public void Requirements_CheckAgainstItemStart()
        {
            var timeline = ScriptableObject.CreateInstance<ProgressionTimeline>();
            try
            {
                var locations = new LocationTrack();
                var cave = new LocationMarker { Name = "Cave", Start = 4f };
                locations.Markers.Add(cave);
                var level = new LevelCurveTrack();
                level.Keys.Add(new CurveKey(0f, 1f));
                level.Keys.Add(new CurveKey(10f, 11f));
                var plot = new PlotTrack();
                var act = new PlotClip { Name = "Act", Start = 3f, Duration = 2f };
                plot.Clips.Add(act);
                timeline.Tracks.Add(locations);
                timeline.Tracks.Add(level);
                timeline.Tracks.Add(plot);

                var context = new RequirementContext(timeline, act);
                Assert.That(new LocationRequirement(cave.Id).Check(context, out var message), Is.False);
                Assert.That(message, Does.Contain("Cave").And.Contain("4:00"));
                Assert.That(new LevelRequirement(4).Check(context, out _), Is.True);
                Assert.That(new LevelRequirement(5).Check(context, out message), Is.False);
                Assert.That(message, Does.Contain("4:00"), "level 5 is reached at 4:00");

                act.Start = 4f;
                Assert.That(new LocationRequirement(cave.Id).Check(new RequirementContext(timeline, act), out _), Is.True);
                Assert.That(new LocationRequirement().Describe(timeline), Is.EqualTo("Локация: —"));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Evaluate_WithoutEditor()
        {
            var timeline = ScriptableObject.CreateInstance<ProgressionTimeline>();
            try
            {
                var skills = new SkillTrack();
                skills.Markers.Add(new SkillMarker { Name = "B", Start = 2f });
                skills.Markers.Add(new SkillMarker { Name = "A", Start = 1f });
                timeline.Tracks.Add(skills);

                var state = timeline.Evaluate(1.5f).FirstOf<SkillTrack, MarkerTrackState>();
                Assert.That(state.Reached.Select(m => m.Name), Is.EqualTo(new[] { "A" }));
                Assert.That(state.Next.Name, Is.EqualTo("B"));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }
    }
}
