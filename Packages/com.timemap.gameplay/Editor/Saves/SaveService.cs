using System.IO;
using UnityEditor;
using UnityEngine;

namespace TimeMapGameplay.Editor
{
    /// <summary>"Create Save" and "Play Mode from t".</summary>
    [InitializeOnLoad]
    public static class SaveService
    {
        const string PlayRequestedKey = "TimeMapGameplay.PlayRequested";

        static SaveService()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>Builds a save of <paramref name="timeline"/> at <paramref name="time"/> with the given builder.</summary>
        public static byte[] Build(ProgressionTimeline timeline, float time, ISaveBuilder builder)
        {
            var context = new SaveBuildContext(timeline, AssetKey);
            return builder.Build(timeline.Evaluate(time), context);
        }

        /// <summary>Writes a save file into the configured folder. Returns its absolute path.</summary>
        public static string CreateSave(ProgressionTimeline timeline, float time, ISaveBuilder builder = null)
        {
            builder ??= SaveBuilders.Active();
            var settings = TimeMapGameplaySettings.instance;
            string fileName = TimeMapGameplaySettings.FormatFileName(settings.FileNamePattern, timeline.name, time,
                timeline.Axis.Units, builder.Id);
            string folder = Path.GetFullPath(Path.Combine(ProjectRoot, settings.SaveFolder));
            string path = Path.Combine(folder, $"{fileName}.{builder.FileExtension}");

            Directory.CreateDirectory(folder);
            File.WriteAllBytes(path, Build(timeline, time, builder));
            Debug.Log($"[TimeMapGameplay] Сейв {timeline.name} @ {TimeMath.Format(time, timeline.Axis.Units)} " +
                      $"({builder.DisplayName}): {path}", timeline);
            return path;
        }

        /// <summary>
        /// Writes one save per checkpoint (act starts and checkpoint markers) plus manifest.json for QA.
        /// Returns the output folder, or null when the timeline has no checkpoints.
        /// </summary>
        public static string CreateCheckpointSaves(ProgressionTimeline timeline, ISaveBuilder builder = null, string folder = null)
        {
            var checkpoints = Checkpoints.Collect(timeline);
            if (checkpoints.Count == 0) return null;

            builder ??= SaveBuilders.Active();
            folder ??= Path.GetFullPath(Path.Combine(ProjectRoot, TimeMapGameplaySettings.instance.SaveFolder, $"{Safe(timeline.name)}_checkpoints"));
            Directory.CreateDirectory(folder);

            var manifest = new CheckpointManifest
            {
                timeline = timeline.name,
                saveBuilderId = builder.Id,
                units = timeline.Axis.Units.ToString(),
                created = System.DateTime.Now.ToString("s")
            };
            for (int i = 0; i < checkpoints.Count; i++)
            {
                var checkpoint = checkpoints[i];
                string file = $"{i + 1:00}_{Safe(checkpoint.Name)}.{builder.FileExtension}";
                File.WriteAllBytes(Path.Combine(folder, file), Build(timeline, checkpoint.Time, builder));
                manifest.checkpoints.Add(new CheckpointEntry
                {
                    index = i + 1,
                    name = checkpoint.Name,
                    source = checkpoint.Source,
                    time = checkpoint.Time,
                    timeText = TimeMath.Format(checkpoint.Time, timeline.Axis.Units),
                    file = file
                });
            }
            File.WriteAllText(Path.Combine(folder, "manifest.json"), JsonUtility.ToJson(manifest, true));
            Debug.Log($"[TimeMapGameplay] Сейвы по контрольным точкам ({checkpoints.Count}, {builder.DisplayName}): {folder}", timeline);
            return folder;
        }

        static string Safe(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }

        [System.Serializable]
        sealed class CheckpointManifest
        {
            public string timeline;
            public string saveBuilderId;
            public string units;
            public string created;
            public System.Collections.Generic.List<CheckpointEntry> checkpoints = new();
        }

        [System.Serializable]
        sealed class CheckpointEntry
        {
            public int index;
            public string name;
            public string source;
            public float time;
            public string timeText;
            public string file;
        }

        /// <summary>Writes a temporary save and a play request, then enters Play Mode.</summary>
        public static void PlayFromTime(ProgressionTimeline timeline, float time, ISaveBuilder builder = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            builder ??= SaveBuilders.Active();

            string requestPath = PlayRequest.FilePath;
            string folder = Path.GetDirectoryName(requestPath);
            Directory.CreateDirectory(folder);
            string savePath = Path.Combine(folder, $"playmode.{builder.FileExtension}");
            File.WriteAllBytes(savePath, Build(timeline, time, builder));

            var request = new PlayRequest
            {
                saveBuilderId = builder.Id,
                savePath = savePath,
                timeline = timeline.name,
                time = time,
                units = timeline.Axis.Units
            };
            File.WriteAllText(requestPath, JsonUtility.ToJson(request, true));

            SessionState.SetBool(PlayRequestedKey, true);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(PlayRequestedKey, false)) return;
            SessionState.EraseBool(PlayRequestedKey);

            // The runtime deletes the request when it reads it; clean up if Play Mode was aborted before that.
            string folder = Path.GetDirectoryName(PlayRequest.FilePath);
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }

        static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        static string AssetKey(Object asset)
            => AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId)
                ? (AssetDatabase.IsMainAsset(asset) ? guid : $"{guid}:{localId}")
                : asset.name;
    }
}
