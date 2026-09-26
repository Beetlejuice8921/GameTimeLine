using System.Linq;
using NUnit.Framework;
using TimeMapGameplay.Editor;
using UnityEditor;
using UnityEngine;

namespace TimeMapGameplay.Tests
{
    public class ValidationTests
    {
        const string PrefsKey = "TimeMapGameplay.Tests.Validation.Auto";

        ProgressionTimeline _demo;

        [SetUp]
        public void SetUp()
        {
            _demo = DemoTimelineFactory.CreateDemo();
            EditorPrefs.DeleteKey(PrefsKey);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_demo);
            EditorPrefs.DeleteKey(PrefsKey);
        }

        PlotTrack Plot => (PlotTrack)_demo.Tracks[1];
        SkillTrack Skills => (SkillTrack)_demo.Tracks[2];
        LocationTrack Locations => (LocationTrack)_demo.Tracks[3];
        LevelCurveTrack Level => (LevelCurveTrack)_demo.Tracks[5];

        ValidationService NewService() => new(prefsKey: PrefsKey);

        [Test]
        public void Demo_HasExactlyTheIntendedViolation()
        {
            var issues = NewService().Run(_demo, logToConsole: false);
            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].ItemId, Is.EqualTo(Plot.Clips[2].Id), "Act III requires the Fortress (opens at 11:00, act at 10:30)");
            Assert.That(issues[0].Message, Does.Contain("Крепость").And.Contain("11:00"));
        }

        [Test]
        public void LocationRequirement_PassesWhenLocationOpensInTime()
        {
            Locations.Markers[4].Start = 10.5f; // Fortress now opens exactly when Act III starts
            Assert.That(NewService().Run(_demo, false), Is.Empty);
        }

        [Test]
        public void LocationRequirement_ReportsMissingTarget()
        {
            var act = Plot.Clips[0];
            act.Requirements.Clear();
            act.Requirements.Add(new LocationRequirement());
            act.Requirements.Add(new LocationRequirement("deleted-id"));
            var messages = new RequirementsRule().Validate(_demo).Where(i => i.ItemId == act.Id).Select(i => i.Message).ToList();
            Assert.That(messages, Has.Count.EqualTo(2));
            Assert.That(messages[0], Does.Contain("не выбрано"));
            Assert.That(messages[1], Does.Contain("удалённый"));
        }

        [Test]
        public void FabulaRequirement_FailsWhenFactRevealedLater()
        {
            var fact = ((FabulaTrack)_demo.Tracks[0]).Clips[4]; // "Истинный враг" at 16:45
            Plot.Clips[1].Requirements.Add(new FabulaRequirement(fact.Id));
            var issue = new RequirementsRule().Validate(_demo).Single(i => i.ItemId == Plot.Clips[1].Id);
            Assert.That(issue.Message, Does.Contain("Истинный враг"));
        }

        [Test]
        public void LevelRequirement_ReportsWhenLevelIsReached()
        {
            Plot.Clips[1].Requirements.Add(new LevelRequirement(12)); // act at 5:00, level 8 there; 12 at 8:00
            var issue = new RequirementsRule().Validate(_demo).Single(i => i.ItemId == Plot.Clips[1].Id);
            Assert.That(issue.Message, Does.Contain("12").And.Contain("8:00"));
        }

        [Test]
        public void MinLevelRule_FlagsSkillBeforeLevel()
        {
            var skill = (SkillMarker)Skills.Markers[1]; // Рывок, min 6, at 4:00 (level 6.67)
            Assert.That(new MinLevelRule().Validate(_demo), Is.Empty);

            skill.Start = 2f; // level 4 there
            var issue = new MinLevelRule().Validate(_demo).Single();
            Assert.That(issue.ItemId, Is.EqualTo(skill.Id));
            Assert.That(issue.Message, Does.Contain("Рывок"));
        }

        [Test]
        public void MinLevelRule_WithoutLevelTrack()
        {
            _demo.Tracks.Remove(Level);
            Assert.That(new MinLevelRule().Validate(_demo).Count(), Is.EqualTo(12), "every skill and upgrade reports the missing curve");
        }

        [Test]
        public void AxisBoundsRule_FlagsItemsBeyondEnd()
        {
            _demo.Axis.Length = 18f;
            var names = new AxisBoundsRule().Validate(_demo).Select(i => i.Message).ToList();
            Assert.That(names.Any(m => m.Contains("Акт IV")), Is.True);
            Assert.That(names.Any(m => m.Contains("Истинный враг")), Is.True);
        }

        [Test]
        public void BrokenReferencesRule_FlagsNullItemsAndTracks()
        {
            Skills.Markers.Add(null);
            _demo.Tracks.Add(null);
            var issues = new BrokenReferencesRule().Validate(_demo).ToList();
            Assert.That(issues, Has.Count.EqualTo(2));
            Assert.That(issues.All(i => i.Severity == IssueSeverity.Error), Is.True);
        }

        [Test]
        public void BrokenReferencesRule_DetectsDestroyedBinding()
        {
            var asset = ScriptableObject.CreateInstance<ProgressionTimeline>();
            Plot.Clips[0].Binding = asset;
            Object.DestroyImmediate(asset);
            Assert.That(BrokenReferencesRule.IsMissing(Plot.Clips[0].Binding), Is.True);
            Assert.That(new BrokenReferencesRule().Validate(_demo).Single().ItemId, Is.EqualTo(Plot.Clips[0].Id));
        }

        [Test]
        public void FailingRule_BecomesIssueInsteadOfException()
        {
            var service = new ValidationService(new IValidationRule[] { new ThrowingRule(0) }, PrefsKey);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("boom"));
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("ThrowingRule упало"));
            var issues = service.Run(_demo, false);
            Assert.That(issues.Single().Message, Does.Contain("ThrowingRule"));
        }

        [Test]
        public void AutoMode_RunsOnChange()
        {
            var service = NewService();
            service.SetAutoMode(true);
            service.Run(_demo, false);
            Assert.That(service.Issues, Has.Count.EqualTo(1));

            Locations.Markers[4].Start = 10f;
            service.NotifyChanged(_demo);
            Assert.That(service.Issues, Is.Empty);
            Assert.That(service.IsStale, Is.False);
        }

        [Test]
        public void ManualMode_KeepsOldResultsAndMarksStale()
        {
            var service = NewService();
            service.SetAutoMode(false);
            service.Run(_demo, false);
            int changes = 0;
            service.Changed += () => changes++;

            Locations.Markers[4].Start = 10f;
            service.NotifyChanged(_demo);
            Assert.That(service.IsStale, Is.True);
            Assert.That(service.Issues, Has.Count.EqualTo(1), "old marks stay until the next check");

            service.NotifyChanged(_demo);
            Assert.That(changes, Is.EqualTo(1), "stale is announced once");

            service.Run(_demo, false);
            Assert.That(service.IsStale, Is.False);
            Assert.That(service.Issues, Is.Empty);
        }

        [Test]
        public void DefaultMode_DependsOnSize()
        {
            var service = NewService();
            Assert.That(service.IsAutoMode(_demo), Is.True);
            for (int i = 0; i < ValidationService.AutoModeItemLimit; i++)
                Skills.Markers.Add(new SkillMarker());
            Assert.That(service.IsAutoMode(_demo), Is.False);

            service.SetAutoMode(true);
            Assert.That(service.IsAutoMode(_demo), Is.True, "explicit preference wins");
        }

        [Test]
        public void DiscoveredRules_IncludeBuiltIns()
        {
            var ids = new ValidationService(prefsKey: PrefsKey).Rules.Select(r => r.Id).ToList();
            Assert.That(ids, Is.SupersetOf(new[] { "requirements", "min-level", "broken-references", "axis-bounds" }));
        }

        [Test]
        public void Duplicate_CopiesRequirements()
        {
            var operations = new TimelineOperations(new TimelineEditing(() => _demo), () => _demo);
            var act = Plot.Clips[2];
            var copyId = operations.Duplicate(new[] { act.Id }).Single();
            _demo.TryFindItem(copyId, out var copy, out _);
            Assert.That(copy.Requirements, Has.Count.EqualTo(act.Requirements.Count));
            Assert.That(copy.Requirements[0], Is.TypeOf<LocationRequirement>());
            Assert.That(copy.Requirements[0], Is.Not.SameAs(act.Requirements[0]));
            Undo.ClearUndo(_demo);
        }

        // Constructor with a parameter keeps it out of automatic rule discovery in the real window.
        sealed class ThrowingRule : IValidationRule
        {
            public ThrowingRule(int _) { }

            public string Id => "throwing";
            public System.Collections.Generic.IEnumerable<ValidationIssue> Validate(ProgressionTimeline timeline)
                => throw new System.InvalidOperationException("boom");
        }
    }
}
