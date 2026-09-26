using UnityEditor;
using UnityEngine;

namespace TimeMapGameplay.Editor
{
    /// <summary>
    /// Colours drawn from code (Painter2D, inline styles) for the dark and light editor skins.
    /// Stylesheet colours live in USS variables (see .tmg-root / .tmg-root--light).
    /// </summary>
    public static class EditorTheme
    {
        public static bool IsDark => EditorGUIUtility.isProSkin;

        public const string LightRootClass = "tmg-root--light";

        public static Color MajorTick => IsDark ? new Color(0.353f, 0.380f, 0.420f) : new Color(0.35f, 0.35f, 0.35f);
        public static Color MinorTick => IsDark ? new Color(0.278f, 0.302f, 0.337f) : new Color(0.5f, 0.5f, 0.5f);
        public static Color Grid => IsDark ? new Color(1f, 1f, 1f, 0.045f) : new Color(0f, 0f, 0f, 0.07f);
        public static Color CurveGuide => IsDark ? new Color(1f, 1f, 1f, 0.07f) : new Color(0f, 0f, 0f, 0.1f);

        /// <summary>Base that clip fills are tinted from.</summary>
        public static Color ClipBase => IsDark ? new Color(0.141f, 0.153f, 0.173f) : new Color(0.93f, 0.93f, 0.93f);

        /// <summary>How strongly the track colour tints a clip.</summary>
        public static float ClipTint => IsDark ? 0.3f : 0.45f;

        public static Color ClipBorder(Color track) => IsDark ? Color.Lerp(Color.black, track, 0.55f) : Color.Lerp(Color.black, track, 0.7f);
        public static Color Selection => IsDark ? Color.white : Color.black;
        public static Color Warn => IsDark ? new Color(0.89f, 0.64f, 0.23f) : new Color(0.8f, 0.5f, 0.05f);
        public static Color StaleWarn => IsDark ? new Color(0.6f, 0.47f, 0.27f) : new Color(0.55f, 0.45f, 0.3f);
        public static Color MapDim => IsDark ? new Color(0.38f, 0.41f, 0.45f) : new Color(0.55f, 0.55f, 0.55f);
        public static Color MapLatestRing => IsDark ? Color.white : Color.black;
    }
}
