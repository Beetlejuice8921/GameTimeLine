using System.Linq;
using System.Text;
using NUnit.Framework;
using TimeMapGameplay.Editor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimeMapGameplay.Tests
{
    public class SaveAndExtensionTests
    {
        ProgressionTimeline _demo;

        [SetUp]
        public void SetUp()
        {
            _demo = DemoTimelineFactory.CreateDemo();
            _demo.name = "Demo";
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_demo);

        [Test]
        public void JsonSaveBuilder_WritesReadableSnapshot()
        {
            byte[] bytes = SaveService.Build(_demo, 3.5f, new JsonSaveBuilder());
            var snapshot = JsonUtility.FromJson<ProgressionSnapshot>(Encoding.UTF8.GetString(bytes));

            Assert.That(snapshot.timeline, Is.EqualTo("Demo"));
            Assert.That(snapshot.time, Is.EqualTo(3.5f));
            Assert.That(snapshot.hasLevel, Is.True);
            Assert.That(snapshot.level, Is.EqualTo(6f).Within(1e-4f));

            var plot = snapshot.FindTrack("PlotTrack");
            Assert.That(plot.active.Single().name, Is.EqualTo("Акт I · Пробуждение"));
            var locations = snapshot.FindTrack("Локации");
            Assert.That(locations.reached.Select(i => i.name), Is.EqualTo(new[] { "Деревня", "Лес" }));
            Assert.That(snapshot.FindTrack("Прогресс").value, Is.EqualTo(6f).Within(1e-4f));
        }

        [Test]
        public void Snapshot_UsesAssetKeyResolver()
        {
            var binding = ScriptableObject.CreateInstance<ProgressionTimeline>();
            binding.name = "Quest_A";
            try
            {
                ((PlotTrack)_demo.Tracks[1]).Clips[0].Binding = binding;
                var context = new SaveBuildContext(_demo, o => "key:" + o.name);
                var snapshot = ProgressionSnapshot.From(_demo.Evaluate(1f), context);
                Assert.That(snapshot.FindTrack("Сюжет").active[0].asset, Is.EqualTo("key:Quest_A"));
                Assert.That(snapshot.FindTrack("Сюжет").reached.Count, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(binding);
            }
        }

        [TestCase("{timeline}_{hh}-{mm}", 3.5f, "Demo_03-30")]
        [TestCase("{timeline}_{t}", 11.75f, "Demo_11-45")]
        [TestCase("save:{builder}", 0f, "save_timemap.json")]
        public void FileNamePattern(string pattern, float time, string expected)
        {
            Assert.That(TimeMapGameplaySettings.FormatFileName(pattern, "Demo", time, AxisUnit.Hours, "timemap.json"), Is.EqualTo(expected));
        }

        [Test]
        public void SaveBuilders_IncludeJsonAndActiveFallsBack()
        {
            Assert.That(SaveBuilders.All().Any(b => b.Id == JsonSaveBuilder.BuilderId), Is.True);
            var settings = TimeMapGameplaySettings.instance;
            string previous = settings.SaveBuilderId;
            try
            {
                settings.SaveBuilderId = "missing.format";
                Assert.That(SaveBuilders.Active().Id, Is.EqualTo(JsonSaveBuilder.BuilderId));
            }
            finally
            {
                settings.SaveBuilderId = previous;
            }
        }

        [Test]
        public void PlayRequest_RoundTrips()
        {
            var request = new PlayRequest { saveBuilderId = "x", savePath = "C:/a.sav", timeline = "Demo", time = 4.25f };
            var copy = JsonUtility.FromJson<PlayRequest>(JsonUtility.ToJson(request));
            Assert.That(copy.time, Is.EqualTo(4.25f));
            Assert.That(copy.savePath, Is.EqualTo("C:/a.sav"));
            StringAssert.EndsWith("play-request.json", PlayRequest.FilePath);
        }

        [Test]
        public void TrackDrawers_ResolveMostSpecific()
        {
            Assert.That(TrackDrawers.For(typeof(FabulaTrack)), Is.TypeOf<FabulaTrackDrawer>());
            Assert.That(TrackDrawers.For(typeof(SkillTrack)), Is.TypeOf<LevelGatedTrackDrawer>());
            Assert.That(TrackDrawers.For(typeof(UpgradeTrack)), Is.TypeOf<LevelGatedTrackDrawer>());
            Assert.That(TrackDrawers.For(typeof(LevelCurveTrack)), Is.TypeOf<CurveTrackDrawer>());
            Assert.That(TrackDrawers.For(typeof(PlotTrack)), Is.TypeOf<ItemTrackDrawer>());
            Assert.That(TrackDrawers.For(typeof(TestTrack)), Is.TypeOf<TestTrackDrawer>());
        }

        [Test]
        public void SlicePanels_DiscoveredWithReplacement()
        {
            var discovered = SliceView.Discover();
            Assert.That(discovered.Select(p => p.attribute.Id), Is.SupersetOf(new[] { "Act", "Hero", "Map", "Fabula", "Equipment" }),
                "built-in panels (project panels may add more)");

            var withReplacement = SliceView.Order(discovered.Append((typeof(TestHeroPanel),
                new SlicePanelAttribute("TestHero", 0) { Column = 1, Replaces = "Hero" })));
            var ids = withReplacement.Select(p => p.attribute.Id).ToList();
            Assert.That(ids, Does.Contain("TestHero"));
            Assert.That(ids, Has.No.Member("Hero"), "TestHeroPanel replaces the built-in hero card");
        }

        [Test]
        public void PreviewSources_UseConventionsAndImages()
        {
            var asset = ScriptableObject.CreateInstance<TestQuestAsset>();
            asset.name = "Q1";
            var texture = new Texture2D(2, 2);
            asset.icon = texture;
            asset.description = "Find the sword";
            asset.title = "The Sword";
            try
            {
                var preview = PreviewSources.Get(asset);
                Assert.That(preview.Image, Is.SameAs(texture));
                Assert.That(preview.Title, Is.EqualTo("The Sword"));
                Assert.That(preview.Description, Is.EqualTo("Find the sword"));
                Assert.That(PreviewSources.Get(null), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(texture);
            }
        }

        [System.Serializable]
        public sealed class TestTrack : MarkerTrack
        {
            // No parameterless constructor: keeps it out of the "+ Track" menu in the real window.
            public TestTrack(int _) { }
        }

        [CustomTrackDrawer(typeof(TestTrack))]
        public sealed class TestTrackDrawer : ItemTrackDrawer
        {
        }

        public sealed class TestHeroPanel : ISlicePanel
        {
            public VisualElement Root { get; } = new();
            public void Refresh(ProgressionState state) { }
        }
    }

    public sealed class TestQuestAsset : ScriptableObject
    {
        public Texture2D icon;
        public string title;
        public string description;
    }
}
