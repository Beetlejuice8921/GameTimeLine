using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TimeMapGameplay.Tests
{
    public class AnalysisTests
    {
        ProgressionTimeline _timeline;
        PlotTrack _plot;
        SkillTrack _skills;
        LevelCurveTrack _level;

        [SetUp]
        public void SetUp()
        {
            _timeline = ScriptableObject.CreateInstance<ProgressionTimeline>();
            _plot = new PlotTrack { Name = "Plot" };
            _plot.Clips.Add(new PlotClip { Name = "Act 1", Start = 0f, Duration = 5f });
            _plot.Clips.Add(new PlotClip { Name = "Act 2", Start = 5f, Duration = 5f });
            _skills = new SkillTrack { Name = "Skills" };
            _skills.Markers.Add(new SkillMarker { Name = "Dash", Start = 2f, MinLevel = 2 });
            _skills.Markers.Add(new SkillMarker { Name = "Parry", Start = 7f, MinLevel = 5 });
            _level = new LevelCurveTrack { Name = "Level" };
            _level.Keys.Add(new CurveKey(0f, 1f));
            _level.Keys.Add(new CurveKey(10f, 11f));
            _timeline.Tracks.Add(_plot);
            _timeline.Tracks.Add(_skills);
            _timeline.Tracks.Add(_level);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_timeline);

        // ---------- CSV ----------

        [Test]
        public void Csv_RoundTripsQuotesSeparatorsAndNewlines()
        {
            var rows = new List<IReadOnlyList<string>>
            {
                new[] { "name", "text" },
                new[] { "Акт I, начало", "Он сказал \"нет\"\nи ушёл" },
                new[] { "", "x" }
            };
            var parsed = Csv.Parse(Csv.Write(rows));
            Assert.That(parsed, Has.Count.EqualTo(3));
            Assert.That(parsed[1][0], Is.EqualTo("Акт I, начало"));
            Assert.That(parsed[1][1], Is.EqualTo("Он сказал \"нет\"\nи ушёл"));
            Assert.That(parsed[2], Is.EqualTo(new[] { "", "x" }));
        }

        [Test]
        public void Csv_DetectsSemicolonAndSkipsBomAndEmptyLines()
        {
            var parsed = Csv.Parse("﻿a;b\r\n1;2,5\r\n\r\n");
            Assert.That(parsed, Has.Count.EqualTo(2));
            Assert.That(parsed[0], Is.EqualTo(new[] { "a", "b" }));
            Assert.That(parsed[1][1], Is.EqualTo("2,5"));
        }

        // ---------- A/B ----------

        [Test]
        public void ProgressionDiff_ReportsGainedCurrentAndValue()
        {
            var deltas = ProgressionDiff.Compare(_timeline.Evaluate(1f), _timeline.Evaluate(8f));

            var plot = deltas.Single(d => d.Track == _plot);
            Assert.That(plot.CurrentA.Name, Is.EqualTo("Act 1"));
            Assert.That(plot.CurrentB.Name, Is.EqualTo("Act 2"));
            Assert.That(plot.Gained.Select(i => i.Name), Is.EqualTo(new[] { "Act 2" }));

            var skills = deltas.Single(d => d.Track == _skills);
            Assert.That(skills.Gained.Select(i => i.Name), Is.EqualTo(new[] { "Dash", "Parry" }));
            Assert.That(skills.Lost, Is.Empty);

            var level = deltas.Single(d => d.Track == _level);
            Assert.That(level.ValueA, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(level.ValueB, Is.EqualTo(9f).Within(1e-4f));
        }

        [Test]
        public void ProgressionDiff_BackwardsReportsLost()
        {
            var skills = ProgressionDiff.Compare(_timeline.Evaluate(8f), _timeline.Evaluate(3f)).Single(d => d.Track == _skills);
            Assert.That(skills.Lost.Select(i => i.Name), Is.EqualTo(new[] { "Parry" }));
            Assert.That(ProgressionDiff.Compare(_timeline.Evaluate(3f), _timeline.Evaluate(3f)).Any(d => d.HasChanges), Is.False);
        }

        // ---------- Versions ----------

        [Test]
        public void TimelineDiff_DetectsAllKindsOfChanges()
        {
            var baseline = Object.Instantiate(_timeline);
            try
            {
                _plot.Clips[1].Start = 6f;                                   // moved
                _plot.Clips[0].Duration = 4f;                                // resized
                _skills.Markers[0].Name = "Dash+";                           // renamed
                ((SkillMarker)_skills.Markers[1]).MinLevel = 7;              // edited
                _skills.Markers.Add(new SkillMarker { Name = "New" });       // added
                _plot.Clips[0].Requirements.Add(new LevelRequirement(3));    // edited (requirements)
                _level.Keys[1] = new CurveKey(10f, 12f);                     // curve
                var removed = new SkillTrack { Name = "Gone" };
                baseline.Tracks.Add(removed);                                // track removed

                var changes = TimelineDiff.Compare(baseline, _timeline);
                var kinds = changes.Select(c => c.Kind).ToList();

                Assert.That(kinds, Does.Contain(ChangeKind.Moved));
                Assert.That(kinds, Does.Contain(ChangeKind.Resized));
                Assert.That(kinds, Does.Contain(ChangeKind.Renamed));
                Assert.That(kinds, Does.Contain(ChangeKind.Added));
                Assert.That(kinds, Does.Contain(ChangeKind.CurveChanged));
                Assert.That(kinds, Does.Contain(ChangeKind.TrackRemoved));
                Assert.That(changes.Count(c => c.Kind == ChangeKind.Edited), Is.EqualTo(2));

                var moved = changes.Single(c => c.Kind == ChangeKind.Moved);
                Assert.That(moved.OldStart, Is.EqualTo(5f));
                Assert.That(moved.NewStart, Is.EqualTo(6f));
                Assert.That(moved.HasGhost, Is.True);
                Assert.That(moved.Description, Does.Contain("+1:00"));
            }
            finally
            {
                Object.DestroyImmediate(baseline);
            }
        }

        [Test]
        public void TimelineDiff_IdenticalCopyHasNoChanges()
        {
            var copy = Object.Instantiate(_timeline);
            try
            {
                Assert.That(TimelineDiff.Compare(copy, _timeline), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
        }

        [Test]
        public void TimelineDiff_RemovedItemKeepsBaselinePosition()
        {
            var baseline = Object.Instantiate(_timeline);
            try
            {
                _skills.Markers.RemoveAt(1);
                var removed = TimelineDiff.Compare(baseline, _timeline).Single();
                Assert.That(removed.Kind, Is.EqualTo(ChangeKind.Removed));
                Assert.That(removed.ItemName, Is.EqualTo("Parry"));
                Assert.That(removed.OldStart, Is.EqualTo(7f));
                Assert.That(removed.TrackId, Is.EqualTo(_skills.Id));
            }
            finally
            {
                Object.DestroyImmediate(baseline);
            }
        }

        // ---------- Checkpoints ----------

        [Test]
        public void Checkpoints_CollectActsAndMarkersMergingDuplicates()
        {
            var markers = new CheckpointTrack();
            markers.Markers.Add(new CheckpointMarker { Name = "Boss", Start = 3f });
            markers.Markers.Add(new CheckpointMarker { Name = "Act 2 start", Start = 5f });
            _timeline.Tracks.Add(markers);

            var points = Checkpoints.Collect(_timeline);
            Assert.That(points.Select(p => p.Name), Is.EqualTo(new[] { "Act 1", "Boss", "Act 2 start" }));
            Assert.That(points.Select(p => p.Time), Is.EqualTo(new[] { 0f, 3f, 5f }));
            Assert.That(Checkpoints.Collect(_timeline, includeMarkers: false).Select(p => p.Name), Is.EqualTo(new[] { "Act 1", "Act 2" }));
        }

        // ---------- Telemetry ----------

        [Test]
        public void Telemetry_Percentiles()
        {
            var stats = new TelemetryStats(new[] { 1f, 2f, 3f, 4f, 5f });
            Assert.That(stats.Median, Is.EqualTo(3f));
            Assert.That(stats.P25, Is.EqualTo(2f));
            Assert.That(stats.P75, Is.EqualTo(4f));
            Assert.That(new TelemetryStats(new[] { 1f, 2f }).Median, Is.EqualTo(1.5f));
            Assert.That(new TelemetryStats(new float[0]).Count, Is.EqualTo(0));
        }

        [Test]
        public void Telemetry_MatchesByIdNameOrExtraKey()
        {
            var data = ScriptableObject.CreateInstance<TelemetryData>();
            try
            {
                var dash = _skills.Markers[0];
                data.Samples.Add(new TelemetrySample("s1", dash.Id, 3f));
                data.Samples.Add(new TelemetrySample("s2", "dash", 2.5f));     // name, case-insensitive
                data.Samples.Add(new TelemetrySample("s1", "guid-123", 8f));    // extra key
                var index = new TelemetryIndex(data);

                Assert.That(index.TryGet(dash, out var byId), Is.True);
                Assert.That(byId.Count, Is.EqualTo(1), "id wins over name");
                Assert.That(index.TryGet(_skills.Markers[1], out var byKey, "guid-123"), Is.True);
                Assert.That(byKey.Median, Is.EqualTo(8f));
                Assert.That(index.TryGet(_plot.Clips[0], out _), Is.False);
                Assert.That(data.SessionCount, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void TelemetryImport_ParsesCsvVariants()
        {
            var samples = TelemetryImport.ParseCsv("Player;Event;Time\np1;Dash;2:30\np2;Dash;2,75\np3;Dash;oops\n", out int skipped);
            Assert.That(samples.Select(s => s.time), Is.EqualTo(new[] { 2.5f, 2.75f }));
            Assert.That(samples[0].session, Is.EqualTo("p1"));
            Assert.That(skipped, Is.EqualTo(1));
            Assert.Throws<System.FormatException>(() => TelemetryImport.ParseCsv("a,b\n1,2", out _));
        }

        [Test]
        public void TelemetryImport_ParsesJsonArrayAndObject()
        {
            var fromArray = TelemetryImport.ParseJson("[{\"session\":\"a\",\"item\":\"Dash\",\"time\":1.5}]");
            var fromObject = TelemetryImport.ParseJson("{\"samples\":[{\"session\":\"b\",\"item\":\"Parry\",\"time\":7}]}");
            Assert.That(fromArray.Single().item, Is.EqualTo("Dash"));
            Assert.That(fromObject.Single().time, Is.EqualTo(7f));
        }
    }
}
