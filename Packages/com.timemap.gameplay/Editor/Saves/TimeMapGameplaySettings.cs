using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimeMapGameplay.Editor
{
    /// <summary>Project-wide settings (ProjectSettings/TimeMapGameplaySettings.asset, shared through VCS).</summary>
    [FilePath("ProjectSettings/TimeMapGameplaySettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class TimeMapGameplaySettings : ScriptableSingleton<TimeMapGameplaySettings>
    {
        public const string DefaultFolder = "Saves/Timeline";
        public const string DefaultPattern = "{timeline}_{hh}-{mm}";

        [SerializeField] string _saveBuilderId = JsonSaveBuilder.BuilderId;
        [SerializeField] string _saveFolder = DefaultFolder;
        [SerializeField] string _fileNamePattern = DefaultPattern;
        [SerializeField] bool _confirmPlayWithIssues = true;

        public string SaveBuilderId { get => _saveBuilderId; set => _saveBuilderId = value; }

        /// <summary>Folder for "Create Save", relative to the project root (or absolute).</summary>
        public string SaveFolder { get => _saveFolder; set => _saveFolder = value; }

        /// <summary>File name pattern: {timeline}, {hh}, {mm}, {t} (time with units), {builder}.</summary>
        public string FileNamePattern { get => _fileNamePattern; set => _fileNamePattern = value; }

        public bool ConfirmPlayWithIssues { get => _confirmPlayWithIssues; set => _confirmPlayWithIssues = value; }

        public void Save() => Save(true);

        /// <summary>Resolves the file name (without extension) for a timeline at a time.</summary>
        public static string FormatFileName(string pattern, string timelineName, float time, AxisUnit units, string builderId)
        {
            int minutes = Mathf.RoundToInt(time * 60f);
            string name = (string.IsNullOrWhiteSpace(pattern) ? DefaultPattern : pattern)
                .Replace("{timeline}", timelineName)
                .Replace("{hh}", (minutes / 60).ToString("00"))
                .Replace("{mm}", (minutes % 60).ToString("00"))
                .Replace("{t}", TimeMath.Format(time, units).Replace(':', '-'))
                .Replace("{builder}", builderId);
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }

    /// <summary>Available <see cref="ISaveBuilder"/> implementations (TypeCache).</summary>
    public static class SaveBuilders
    {
        public static IReadOnlyList<ISaveBuilder> All()
            => TypeCache.GetTypesDerivedFrom<ISaveBuilder>()
                .Where(t => !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => (ISaveBuilder)Activator.CreateInstance(t))
                .OrderBy(b => b.Id == JsonSaveBuilder.BuilderId ? 1 : 0)
                .ThenBy(b => b.DisplayName)
                .ToList();

        /// <summary>Builder selected in settings; falls back to the built-in JSON one.</summary>
        public static ISaveBuilder Active()
        {
            string id = TimeMapGameplaySettings.instance.SaveBuilderId;
            var all = All();
            return all.FirstOrDefault(b => b.Id == id) ?? all.First(b => b.Id == JsonSaveBuilder.BuilderId);
        }
    }

    static class TimeMapGameplaySettingsProvider
    {
        [SettingsProvider]
        static SettingsProvider Create() => new("Project/TimeMapGameplay", SettingsScope.Project)
        {
            label = "TimeMapGameplay",
            keywords = new[] { "timeline", "progression", "save", "сейв" },
            activateHandler = (_, root) => Build(root)
        };

        static void Build(VisualElement root)
        {
            var settings = TimeMapGameplaySettings.instance;
            root.style.paddingLeft = 10;
            root.style.paddingTop = 4;

            var title = new Label("TimeMapGameplay");
            title.style.fontSize = 19;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            root.Add(title);

            var builders = SaveBuilders.All();
            var choices = builders.Select(b => $"{b.DisplayName}  [{b.Id}]").ToList();
            int index = Mathf.Max(0, builders.ToList().FindIndex(b => b.Id == settings.SaveBuilderId));
            var builderField = new PopupField<string>("Формат сейва", choices, index)
            {
                tooltip = "Реализация ISaveBuilder. Свои форматы: класс с ISaveBuilder и конструктором без параметров."
            };
            var preview = new Label();
            preview.style.marginTop = 6;
            preview.style.color = new Color(0.6f, 0.65f, 0.7f);

            void UpdatePreview()
            {
                var builder = SaveBuilders.Active();
                string file = TimeMapGameplaySettings.FormatFileName(settings.FileNamePattern, "MainCampaign", 3.5f, AxisUnit.Hours, builder.Id);
                preview.text = $"Пример: {Path.Combine(settings.SaveFolder, file)}.{builder.FileExtension}";
            }

            builderField.RegisterValueChangedCallback(e =>
            {
                settings.SaveBuilderId = builders[choices.IndexOf(e.newValue)].Id;
                settings.Save();
                UpdatePreview();
            });
            root.Add(builderField);

            var folder = new TextField("Папка сейвов") { value = settings.SaveFolder, isDelayed = true };
            folder.RegisterValueChangedCallback(e =>
            {
                settings.SaveFolder = string.IsNullOrWhiteSpace(e.newValue) ? TimeMapGameplaySettings.DefaultFolder : e.newValue;
                settings.Save();
                UpdatePreview();
            });
            root.Add(folder);

            var pattern = new TextField("Имя файла") { value = settings.FileNamePattern, isDelayed = true,
                tooltip = "{timeline}, {hh}, {mm}, {t}, {builder}" };
            pattern.RegisterValueChangedCallback(e =>
            {
                settings.FileNamePattern = e.newValue;
                settings.Save();
                UpdatePreview();
            });
            root.Add(pattern);

            var confirm = new Toggle("Спрашивать при нарушениях") { value = settings.ConfirmPlayWithIssues,
                tooltip = "Перед «Play Mode с t» предупреждать, если проверка нашла нарушения." };
            confirm.RegisterValueChangedCallback(e =>
            {
                settings.ConfirmPlayWithIssues = e.newValue;
                settings.Save();
            });
            root.Add(confirm);

            root.Add(preview);
            UpdatePreview();

            var help = new Label(
                "Свой формат сейва: реализуйте ISaveBuilder (ProgressionState → байты). " +
                "Чтобы «Play Mode с t» загружал его, реализуйте ITimelineBootstrap в runtime-сборке проекта. " +
                "Встроенный JSON-формат читается из игры через TimelineLaunch.TryReadSnapshot.");
            help.style.whiteSpace = WhiteSpace.Normal;
            help.style.marginTop = 10;
            help.style.maxWidth = 640;
            root.Add(help);
        }
    }
}
