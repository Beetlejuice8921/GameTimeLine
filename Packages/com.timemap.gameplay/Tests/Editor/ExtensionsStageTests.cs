using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TimeMapGameplay.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimeMapGameplay.Tests
{
    public class ExtensionsStageTests
    {
        const string TempFolder = "Assets/__TimeMapGameplayTests";

        ProgressionTimeline _demo;

        [SetUp]
        public void SetUp()
        {
            _demo = DemoTimelineFactory.CreateDemo();
            _demo.name = "Demo";
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearUndo(_demo);
            if (AssetDatabase.Contains(_demo)) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(_demo));
            else Object.DestroyImmediate(_demo);
            if (AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.DeleteAsset(TempFolder);
        }

        SkillTrack Skills => (SkillTrack)_demo.Tracks[2];
        LevelCurveTrack Level => (LevelCurveTrack)_demo.Tracks[5];

        // ---------- CSV ----------

        [Test]
        public void Csv_ExportHasItemsCurveKeysAndFieldColumns()
        {
            var rows = Csv.Parse(TimelineCsv.Export(_demo));
            var header = rows[0];
            Assert.That(header.Take(9), Is.EqualTo(new[] { "track", "track_type", "item_type", "id", "name", "start", "duration", "value", "binding" }));
            Assert.That(header, Is.SupersetOf(new[] { "chronoIndex", "minLevel", "slot", "attackBonus", "synopsis" }));

            int items = _demo.Tracks.Sum(t => t.Items.Count());
            Assert.That(rows.Count - 1, Is.EqualTo(items + Level.Keys.Count));

            var dash = rows.Single(r => r[4] == "Рывок");
            Assert.That(dash[header.IndexOf("minLevel")], Is.EqualTo("6"));
            Assert.That(dash[5], Is.EqualTo("4"));
        }

        [Test]
        public void Csv_RoundTripWithoutChangesChangesNothing()
        {
            string before = EditorJsonUtility.ToJson(_demo);
            var report = TimelineCsv.Import(_demo, TimelineCsv.Export(_demo));
            Assert.That(report.Errors, Is.Empty);
            Assert.That(report.Updated, Is.EqualTo(0));
            Assert.That(report.Created, Is.EqualTo(0));
            Assert.That(EditorJsonUtility.ToJson(_demo), Is.EqualTo(before));
        }

        [Test]
        public void Csv_ImportAppliesSpreadsheetEdits()
        {
            var rows = Csv.Parse(TimelineCsv.Export(_demo));
            var header = rows[0];
            int start = header.IndexOf("start"), minLevel = header.IndexOf("minLevel"), name = header.IndexOf("name"),
                id = header.IndexOf("id"), value = header.IndexOf("value");

            var dash = rows.Single(r => r[name] == "Рывок");
            dash[start] = "4,5";                 // comma decimal from a Russian locale sheet
            dash[minLevel] = "7";

            var newSkill = Enumerable.Repeat("", header.Count).ToList();
            newSkill[header.IndexOf("track")] = "Прокачка";
            newSkill[name] = "Новый навык";
            newSkill[start] = "12";
            newSkill[minLevel] = "15";
            rows.Add(newSkill);

            var lastKey = rows.Last(r => r[header.IndexOf("item_type")] == TimelineCsv.CurveKeyType);
            lastKey[value] = "32";

            var report = TimelineCsv.Import(_demo, Csv.Write(rows.Cast<IReadOnlyList<string>>()));

            Assert.That(report.Errors, Is.Empty, string.Join("\n", report.Errors));
            Assert.That(report.Updated, Is.EqualTo(1));
            Assert.That(report.Created, Is.EqualTo(1));
            var skill = (SkillMarker)Skills.Markers.Single(m => m.Name == "Рывок");
            Assert.That(skill.Start, Is.EqualTo(4.5f));
            Assert.That(skill.MinLevel, Is.EqualTo(7));
            var created = (SkillMarker)Skills.Markers.Single(m => m.Name == "Новый навык");
            Assert.That(created.MinLevel, Is.EqualTo(15));
            Assert.That(created.Start, Is.EqualTo(12f));
            Assert.That(Level.Keys.Last().value, Is.EqualTo(32f));
            Assert.That(dash[id], Is.EqualTo(skill.Id));
        }

        [Test]
        public void Csv_ImportIsUndoable()
        {
            var rows = Csv.Parse(TimelineCsv.Export(_demo));
            int start = rows[0].IndexOf("start");
            rows.Single(r => r[4] == "Рывок")[start] = "9";
            TimelineCsv.Import(_demo, Csv.Write(rows.Cast<IReadOnlyList<string>>()));
            Assert.That(Skills.Markers.Single(m => m.Name == "Рывок").Start, Is.EqualTo(9f));

            Undo.PerformUndo();
            Assert.That(Skills.Markers.Single(m => m.Name == "Рывок").Start, Is.EqualTo(4f));
        }

        [Test]
        public void Csv_ReportsUnknownTrack()
        {
            var report = TimelineCsv.Import(_demo, "track,name,start\nНет такого,X,1\n");
            Assert.That(report.Errors.Single(), Does.Contain("Нет такого"));
        }

        // ---------- JSON ----------

        [Test]
        public void Json_RoundTripRestoresData()
        {
            string json = TimelineJson.Export(_demo);
            Skills.Markers.Clear();
            TimelineJson.Import(_demo, json);
            Assert.That(Skills.Markers, Has.Count.EqualTo(6));
            Assert.That(_demo.name, Is.EqualTo("Demo"));
        }

        // ---------- Versions ----------

        [Test]
        public void Versions_SaveListAndCompare()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.CreateFolder("Assets", "__TimeMapGameplayTests");
            AssetDatabase.CreateAsset(_demo, $"{TempFolder}/Demo.asset");

            var version = TimelineVersions.Save(_demo, "до правок");
            Assert.That(AssetDatabase.GetAssetPath(version), Does.StartWith($"{TempFolder}/Demo_Versions/"));
            Assert.That(version.name, Does.EndWith("до правок"));
            Assert.That(TimelineVersions.List(_demo), Is.EqualTo(new[] { version }));

            Skills.Markers[0].Start = 3f;
            var changes = TimelineDiff.Compare(version, _demo);
            Assert.That(changes.Single().Kind, Is.EqualTo(ChangeKind.Moved));
        }

        // ---------- Checkpoint saves ----------

        [Test]
        public void CheckpointSaves_WriteFilePerActAndManifest()
        {
            string folder = Path.Combine(Path.GetTempPath(), "tmg-checkpoints-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                string result = SaveService.CreateCheckpointSaves(_demo, new JsonSaveBuilder(), folder);
                Assert.That(result, Is.EqualTo(folder));
                var files = Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(f => f).ToList();
                Assert.That(files, Has.Count.EqualTo(5), string.Join(", ", files));
                Assert.That(files, Does.Contain("manifest.json"));
                Assert.That(files.First(), Does.StartWith("01_"));

                string manifest = File.ReadAllText(Path.Combine(folder, "manifest.json"));
                Assert.That(manifest, Does.Contain("10:30"));
                var third = JsonUtility.FromJson<ProgressionSnapshot>(File.ReadAllText(Path.Combine(folder, files[2])));
                Assert.That(third.time, Is.EqualTo(10.5f));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        // ---------- View / slice ----------

        [Test]
        public void Slice_ShowsAbDifference()
        {
            var slice = new SliceView();
            slice.Refresh(_demo, 1f, 8f);
            var rows = slice.Query(className: "tmg-ab__row").ToList();
            Assert.That(rows.Count, Is.GreaterThanOrEqualTo(5));
            var text = string.Join("\n", slice.Query<Label>(className: "tmg-ab__text").ToList().Select(l => l.text));
            Assert.That(text, Does.Contain("Рывок").And.Contain("Акт I · Пробуждение → Акт II · Дорога на север"));

            slice.Refresh(_demo, 1f);
            Assert.That(slice.Q(className: "tmg-ab").resolvedStyle.display == DisplayStyle.None
                        || slice.Q(className: "tmg-ab").style.display == DisplayStyle.None, Is.True);
        }

        [Test]
        public void View_ShowsGhostsTelemetryAndPlayheadB()
        {
            var analysis = new AnalysisState();
            var editing = new TimelineEditing(() => _demo);
            var view = new TimelineView(editing, new TimelineOperations(editing, () => _demo), new TimelineSelection(), new List<string>(), analysis);
            view.SetTimeline(_demo);

            var baseline = Object.Instantiate(_demo);
            try
            {
                Skills.Markers[0].Start = 5f;
                Skills.Markers.RemoveAt(1);
                view.Refresh();
                view.SetGhosts(TimelineDiff.Compare(baseline, _demo));
                Assert.That(view.GhostCount, Is.EqualTo(2));
                Assert.That(view.Query(className: "tmg-ghost--removed").ToList(), Has.Count.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(baseline);
            }

            var telemetry = TelemetryImporter.Create(new[]
            {
                new TelemetrySample("a", "Двойной прыжок", 2f),
                new TelemetrySample("b", "Двойной прыжок", 3f)
            }, "test");
            try
            {
                analysis.Telemetry = telemetry;
                view.RefreshAnalysis();
                Assert.That(view.Query<TelemetryOverlay>().ToList().Count, Is.GreaterThanOrEqualTo(5));
                var element = view.Query<ItemElement>().Where(e => e.Item.Name == "Двойной прыжок").First();
                Assert.That(element.tooltip, Does.Contain("медиана 2:30").And.Contain("n=2"));
            }
            finally
            {
                Object.DestroyImmediate(telemetry);
            }

            analysis.AbEnabled = true;
            analysis.TimeB = 6f;
            view.RefreshAnalysis();
            var playheadB = view.Q(className: "tmg-playhead--b");
            Assert.That(playheadB.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(playheadB.style.left.value.value, Is.EqualTo(view.Viewport.TimeToPixel(6f)).Within(0.01f));
        }
    }
}
