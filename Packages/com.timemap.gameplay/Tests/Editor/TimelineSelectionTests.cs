using NUnit.Framework;
using TimeMapGameplay.Editor;
using UnityEngine;

namespace TimeMapGameplay.Tests
{
    public class TimelineSelectionTests
    {
        [Test]
        public void ToggleAddsAndRemoves()
        {
            var selection = new TimelineSelection();
            int changes = 0;
            selection.Changed += () => changes++;

            selection.Toggle("a");
            selection.Toggle("b");
            Assert.That(selection.Items, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(selection.Primary, Is.EqualTo("b"));

            selection.Toggle("a");
            Assert.That(selection.Items, Is.EqualTo(new[] { "b" }));
            Assert.That(changes, Is.EqualTo(3));
        }

        [Test]
        public void SetIgnoresDuplicatesAndDoesNotNotifyWhenUnchanged()
        {
            var selection = new TimelineSelection();
            selection.Set(new[] { "a", "a", "b" });
            Assert.That(selection.Count, Is.EqualTo(2));

            int changes = 0;
            selection.Changed += () => changes++;
            selection.Set(new[] { "b", "a" });
            Assert.That(changes, Is.EqualTo(0));
        }

        [Test]
        public void TrackAndItemSelectionAreExclusive()
        {
            var selection = new TimelineSelection();
            selection.Select("item");
            selection.SelectTrack("track");
            Assert.That(selection.Count, Is.EqualTo(0));
            Assert.That(selection.TrackId, Is.EqualTo("track"));

            selection.Add("item");
            Assert.That(selection.TrackId, Is.Null);
        }

        [Test]
        public void PruneDropsMissingIds()
        {
            var timeline = ScriptableObject.CreateInstance<ProgressionTimeline>();
            var track = new SkillTrack();
            var skill = new SkillMarker();
            track.Markers.Add(skill);
            timeline.Tracks.Add(track);
            try
            {
                var selection = new TimelineSelection();
                selection.Set(new[] { skill.Id, "gone" });
                Assert.That(selection.Prune(timeline), Is.True);
                Assert.That(selection.Items, Is.EqualTo(new[] { skill.Id }));
                Assert.That(selection.Prune(timeline), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }
    }
}
