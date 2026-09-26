using System.Globalization;
using UnityEngine;

namespace TimeMapGameplay
{
    /// <summary>Pure helpers for time snapping, clamping and formatting.</summary>
    public static class TimeMath
    {
        /// <summary>Rounds <paramref name="value"/> to the nearest multiple of <paramref name="step"/>.</summary>
        public static float Snap(float value, float step)
        {
            if (step <= 0f) return value;
            return Mathf.Round(value / step) * step;
        }

        /// <summary>Formats time as "h:mm" for hours, or as a plain number for other units.</summary>
        public static string Format(float time, AxisUnit units = AxisUnit.Hours)
        {
            if (units != AxisUnit.Hours)
                return time.ToString("0.##", CultureInfo.InvariantCulture);

            int totalMinutes = Mathf.RoundToInt(time * 60f);
            string sign = totalMinutes < 0 ? "-" : "";
            totalMinutes = Mathf.Abs(totalMinutes);
            return $"{sign}{totalMinutes / 60}:{totalMinutes % 60:00}";
        }

        /// <summary>Clamps a clip start so that the whole clip stays inside [0, length].</summary>
        public static float ClampStart(float start, float duration, float length)
            => Mathf.Clamp(start, 0f, Mathf.Max(0f, length - duration));

        /// <summary>Clamps a clip duration to [minDuration, length - start].</summary>
        public static float ClampDuration(float duration, float start, float length, float minDuration)
            => Mathf.Clamp(duration, minDuration, Mathf.Max(minDuration, length - start));
    }
}
