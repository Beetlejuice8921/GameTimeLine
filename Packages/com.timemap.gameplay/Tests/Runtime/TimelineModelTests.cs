using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TimeMapGameplay.Tests
{
    public class TimelineModelTests
    {
        ProgressionTimeline _timeline;
        ClipTrack _acts;
        MarkerTrack _skills;

        [SetUp]
        public void SetUp()
        {
            _timeline = ScriptableObject.CreateInstance<ProgressionTimeline>();
            _acts = new ClipTrack { Name = "Acts" };
            _acts.Clips.Add(new ClipBase { Name = "Act I", Start = 0f, Duration = 5f });
            _acts.Clips.Add(new ClipBase { Name = "Act II", Start = 5f, Duration = 5f });
            _skills = new MarkerTrack { Name = "Skills" };
            _skills.Markers.Add(new MarkerBase { Name = "Dash", Start = 4f });
            _skills.Markers.Add(new MarkerBase { Name = "Parry", Start = 7.5f });
            _timeline.Tracks.Add(_acts);
            _timeline.Tracks.Add(_skills);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_timeline);

        [Test]
        public void ClipsAt_UsesHalfOpenInterval()
        {
            Assert.That(_acts.ClipsAt(0f).Single().Name, Is.EqualTo("Act I"));
            Assert.That(_acts.ClipsAt(5f).Single().Name, Is.EqualTo("Act II"));
            Assert.That(_acts.ClipsAt(10f), Is.Empty);
        }

        [Test]
        public void MarkersReachedBy_IncludesMarkerAtExactTime()
        {
            Assert.That(_skills.MarkersReachedBy(3.9f), Is.Empty);
            Assert.That(_skills.MarkersReachedBy(4f).Select(m => m.Name), Is.EqualTo(new[] { "Dash" }));
            Assert.That(_skills.MarkersReachedBy(20f).Count(), Is.EqualTo(2));
        }

        [Test]
        public void TryFindItem_FindsAcrossTracks()
        {
            var parry = _skills.Markers[1];
            Assert.That(_timeline.TryFindItem(parry.Id, out var item, out var track), Is.True);
            Assert.That(item, Is.SameAs(parry));
            Assert.That(track, Is.SameAs(_skills));
            Assert.That(_timeline.TryFindItem("missing", out _, out _), Is.False);
        }

        [Test]
        public void Items_HaveUniqueIds()
        {
            var ids = _timeline.Tracks.SelectMany(t => t.Items).Select(i => i.Id).ToList();
            Assert.That(ids, Is.Unique);
        }

        [Test]
        public void ClipDuration_CannotGoBelowMinimum()
        {
            var clip = new ClipBase { Duration = -3f };
            Assert.That(clip.Duration, Is.EqualTo(ClipBase.MinDuration));
        }

        [Test]
        public void SerializeReferenceTracks_SurviveJsonRoundTrip()
        {
            string json = JsonUtility.ToJson(_timeline);
            var copy = ScriptableObject.CreateInstance<ProgressionTimeline>();
            try
            {
                JsonUtility.FromJsonOverwrite(json, copy);
                Assert.That(copy.Tracks, Has.Count.EqualTo(2));
                Assert.That(copy.Tracks[0], Is.TypeOf<ClipTrack>());
                Assert.That(((MarkerTrack)copy.Tracks[1]).Markers[1].Start, Is.EqualTo(7.5f));
                Assert.That(copy.Tracks[1].Items.First().Id, Is.EqualTo(_skills.Markers[0].Id));
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
        }
    }
}
