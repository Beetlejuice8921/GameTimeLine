using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TimeMapGameplay
{
    /// <summary>Written by the editor before entering Play Mode from time t.</summary>
    [Serializable]
    public sealed class PlayRequest
    {
        public string saveBuilderId;
        public string savePath;
        public string timeline;
        public float time;
        public AxisUnit units;

        /// <summary>Absolute path of the request file inside the project's Temp folder.</summary>
        public static string FilePath
            => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "TimeMapGameplay", "play-request.json"));
    }

    /// <summary>
    /// Loads a save written by an <see cref="ISaveBuilder"/> when Play Mode starts from the timeline.
    /// Implemented by the project for its save format; needs a public parameterless constructor.
    /// </summary>
    public interface ITimelineBootstrap
    {
        bool CanLoad(PlayRequest request);
        void Load(PlayRequest request);
    }

    /// <summary>
    /// Runtime side of "Play Mode from t". In the editor, reads the one-shot request before the first scene loads
    /// and hands it to a matching <see cref="ITimelineBootstrap"/>. Game code can also query it directly.
    /// </summary>
    public static class TimelineLaunch
    {
        /// <summary>Request of the current play session, or null when Play Mode was started normally.</summary>
        public static PlayRequest Request { get; private set; }

        public static bool IsActive => Request != null;

        /// <summary>Reads the save as a <see cref="ProgressionSnapshot"/> (only for the built-in JSON format).</summary>
        public static bool TryReadSnapshot(out ProgressionSnapshot snapshot)
        {
            snapshot = null;
            if (Request == null || Request.saveBuilderId != JsonSaveBuilder.BuilderId || !File.Exists(Request.savePath))
                return false;
            snapshot = JsonUtility.FromJson<ProgressionSnapshot>(File.ReadAllText(Request.savePath, Encoding.UTF8));
            return snapshot != null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Request = null;

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void OnBeforeSceneLoad()
        {
            string path = PlayRequest.FilePath;
            if (!File.Exists(path)) return;

            try
            {
                Request = JsonUtility.FromJson<PlayRequest>(File.ReadAllText(path, Encoding.UTF8));
            }
            finally
            {
                File.Delete(path); // one-shot: the next normal Play Mode must not pick it up
            }
            if (Request == null) return;

            var bootstrap = FindBootstrap(Request);
            string time = TimeMath.Format(Request.time, Request.units);
            if (bootstrap == null)
            {
                Debug.Log($"[TimeMapGameplay] Play Mode from {time} ({Request.timeline}). No ITimelineBootstrap for " +
                          $"'{Request.saveBuilderId}' — read TimelineLaunch.Request / TryReadSnapshot from game code. Save: {Request.savePath}");
                return;
            }

            Debug.Log($"[TimeMapGameplay] Play Mode from {time} ({Request.timeline}): loading via {bootstrap.GetType().Name}.");
            bootstrap.Load(Request);
        }

        static ITimelineBootstrap FindBootstrap(PlayRequest request)
        {
            var types = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch (System.Reflection.ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); }
                })
                .Where(t => typeof(ITimelineBootstrap).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface
                            && t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.FullName);

            foreach (var type in types)
            {
                var bootstrap = (ITimelineBootstrap)Activator.CreateInstance(type);
                if (bootstrap.CanLoad(request)) return bootstrap;
            }
            return null;
        }
#endif
    }
}
