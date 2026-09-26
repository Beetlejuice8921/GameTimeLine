using UnityEditor;
using UnityEngine;

namespace TimeMapGameplay.Editor
{
    /// <summary>Creates a demo timeline matching the interface concept (RPG campaign, 20 game hours).</summary>
    public static class DemoTimelineFactory
    {
        const string Folder = "Assets/TimeMapGameplay";

        [MenuItem("Tools/TimeMapGameplay/Create Demo Timeline")]
        static void CreateAndOpen()
        {
            ProgressionTimelineWindow.Open(CreateDemoAsset());
        }

        /// <summary>Creates a new demo asset under Assets/TimeMapGameplay and selects it.</summary>
        public static ProgressionTimeline CreateDemoAsset()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets", "TimeMapGameplay");

            var timeline = CreateDemo();
            timeline.name = "DemoProgression";
            string path = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/DemoProgression.asset");
            AssetDatabase.CreateAsset(timeline, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = timeline;
            return timeline;
        }

        /// <summary>Builds an in-memory demo timeline (not saved as an asset).</summary>
        public static ProgressionTimeline CreateDemo()
        {
            var timeline = ScriptableObject.CreateInstance<ProgressionTimeline>();
            timeline.Axis.Units = AxisUnit.Hours;
            timeline.Axis.Length = 20f;
            timeline.Axis.SnapStep = 0.25f;

            var fabula = Track<FabulaTrack>("Фабула", "события мира", new Color32(160, 127, 224, 255));
            Fabula(fabula, "Пробуждение", 0f, 1.25f, 4, "Герой приходит в себя в разрушенном храме.");
            Fabula(fabula, "Детство героя", 2.5f, 1.25f, 2, "Сирота, выросший при монастыре Ордена.");
            Fabula(fabula, "Падение Ордена", 6.5f, 1.5f, 1, "Двадцать лет назад Орден пал за одну ночь.");
            Fabula(fabula, "Предательство", 11.75f, 1.25f, 3, "Наставник сам открыл ворота врагу.");
            Fabula(fabula, "Истинный враг", 16.75f, 1.5f, 5, "За всем стоит тот, кто когда-то спас героя.");
            timeline.Tracks.Add(fabula);

            var plot = Track<PlotTrack>("Сюжет", "акты", new Color32(224, 143, 85, 255));
            Act(plot, "Акт I · Пробуждение", 0f, 5f, 205, "Герой без памяти выбирается из руин храма и находит приют в деревне у подножия гор.");
            Act(plot, "Акт II · Дорога на север", 5f, 5.5f, 150, "Через лес и старые шахты к северному перевалу — по следам уцелевших рыцарей Ордена.");
            Act(plot, "Акт III · Осада", 10.5f, 5f, 12, "Крепость в осаде. Герой узнаёт, кто открыл ворота, и теряет последнего союзника.");
            Act(plot, "Акт IV · Цитадель", 15.5f, 4.5f, 275, "Подъём в Цитадель и встреча с тем, кто всё это начал.");
            timeline.Tracks.Add(plot);

            var skills = Track<SkillTrack>("Прокачка", "навыки", new Color32(95, 185, 142, 255));
            Skill(skills, "Двойной прыжок", 1.5f, 3);
            Skill(skills, "Рывок", 4f, 6);
            Skill(skills, "Парирование", 7.5f, 10);
            Skill(skills, "Огненный клинок", 10f, 14);
            Skill(skills, "Ярость", 14f, 20);
            Skill(skills, "Последний довод", 18f, 26);
            timeline.Tracks.Add(skills);

            // Map coordinates come from the concept's 360×220 mini-map (y down) and are normalised here.
            var locations = Track<LocationTrack>("Локации", "открытие", new Color32(74, 169, 209, 255));
            Location(locations, "Деревня", 0f, 42, 150);
            Location(locations, "Лес", 1.5f, 112, 104);
            Location(locations, "Шахты", 4f, 104, 188);
            Location(locations, "Северный перевал", 6.5f, 190, 128);
            Location(locations, "Крепость", 11f, 262, 92);
            Location(locations, "Цитадель", 15.5f, 318, 38);
            timeline.Tracks.Add(locations);

            var upgrades = Track<UpgradeTrack>("Апгрейды", "снаряжение", new Color32(209, 181, 71, 255));
            Upgrade(upgrades, "Меч II", 3f, "Оружие", 5, 4, 0);
            Upgrade(upgrades, "Броня II", 5.5f, "Броня", 7, 0, 40);
            Upgrade(upgrades, "Конь", 7f, "Транспорт", 9, 0, 0);
            Upgrade(upgrades, "Меч III", 9f, "Оружие", 12, 7, 0);
            Upgrade(upgrades, "Броня III", 12.5f, "Броня", 17, 0, 90);
            Upgrade(upgrades, "Меч IV", 16f, "Оружие", 23, 12, 0);
            timeline.Tracks.Add(upgrades);

            var level = Track<LevelCurveTrack>("Прогресс", "уровень героя", new Color32(224, 112, 143, 255));
            level.ValueMin = 1f;
            level.ValueMax = 30f;
            level.ValueStep = 1f;
            level.ValueLabel = "ур.";
            foreach (var (t, v) in new[] { (0f, 1f), (2f, 4f), (5f, 8f), (8f, 12f), (11f, 16f), (14f, 20f), (17f, 25f), (20f, 30f) })
                level.Keys.Add(new CurveKey(t, v));
            timeline.Tracks.Add(level);

            // Act requirements. Act III needs the Fortress, which opens at 11:00 — half an hour after the act starts,
            // so the demo shows one violation, as in the concept.
            var acts = plot.Clips;
            var map = locations.Markers;
            acts[0].Requirements.Add(new LocationRequirement(map[0].Id));
            acts[1].Requirements.Add(new LocationRequirement(map[2].Id));
            acts[2].Requirements.Add(new LocationRequirement(map[4].Id));
            acts[2].Requirements.Add(new FabulaRequirement(fabula.Clips[2].Id));
            acts[3].Requirements.Add(new LocationRequirement(map[5].Id));
            acts[3].Requirements.Add(new LevelRequirement(20));

            return timeline;
        }

        static T Track<T>(string name, string subtitle, Color color) where T : TrackBase, new()
            => new() { Name = name, Subtitle = subtitle, Color = color };

        static void Fabula(FabulaTrack track, string name, float start, float duration, int chrono, string text)
            => track.Clips.Add(new FabulaEvent { Name = name, Start = start, Duration = duration, ChronoIndex = chrono, Text = text });

        static void Act(PlotTrack track, string name, float start, float duration, int hue, string synopsis)
            => track.Clips.Add(new PlotClip
            {
                Name = name, Start = start, Duration = duration, Synopsis = synopsis,
                Tint = Color.HSVToRGB(hue / 360f, 0.45f, 0.6f)
            });

        static void Skill(SkillTrack track, string name, float start, int minLevel)
            => track.Markers.Add(new SkillMarker { Name = name, Start = start, MinLevel = minLevel });

        static void Location(LocationTrack track, string name, float start, float x, float y)
            => track.Markers.Add(new LocationMarker { Name = name, Start = start, MapPosition = new Vector2(x / 360f, 1f - y / 220f) });

        static void Upgrade(UpgradeTrack track, string name, float start, string slot, int minLevel, int attack, int health)
            => track.Markers.Add(new UpgradeMarker
            {
                Name = name, Start = start, Slot = slot, MinLevel = minLevel, AttackBonus = attack, HealthBonus = health
            });
    }
}
