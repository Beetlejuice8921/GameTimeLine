using UnityEngine;

namespace TimeMapGameplay.Editor
{
    /// <summary>Conversion between timeline time and pixels, plus ruler tick selection.</summary>
    public readonly struct TimelineViewport
    {
        public const float MinPixelsPerUnit = 12f;
        public const float MaxPixelsPerUnit = 400f;

        static readonly float[] TickSteps = { 0.25f, 0.5f, 1f, 2f, 5f, 10f, 20f, 50f, 100f, 250f, 500f, 1000f };

        public readonly float PixelsPerUnit;

        public TimelineViewport(float pixelsPerUnit)
        {
            PixelsPerUnit = ClampZoom(pixelsPerUnit);
        }

        public float TimeToPixel(float time) => time * PixelsPerUnit;
        public float PixelToTime(float pixel) => pixel / PixelsPerUnit;

        public static float ClampZoom(float pixelsPerUnit)
            => Mathf.Clamp(pixelsPerUnit, MinPixelsPerUnit, MaxPixelsPerUnit);

        /// <summary>
        /// Smallest "nice" major tick step whose on-screen spacing is at least <paramref name="minMajorPixels"/>.
        /// </summary>
        public float MajorTickStep(float minMajorPixels = 56f)
        {
            foreach (var step in TickSteps)
                if (step * PixelsPerUnit >= minMajorPixels)
                    return step;
            return TickSteps[TickSteps.Length - 1];
        }

        /// <summary>Minor tick step (a quarter of the major one), or 0 when minor ticks would be too dense.</summary>
        public float MinorTickStep(float minMajorPixels = 56f, float minMinorPixels = 10f)
        {
            float minor = MajorTickStep(minMajorPixels) / 4f;
            return minor * PixelsPerUnit >= minMinorPixels ? minor : 0f;
        }

        /// <summary>
        /// Horizontal scroll offset that keeps <paramref name="anchorTime"/> under the same viewport x
        /// after zooming to this viewport.
        /// </summary>
        public float ScrollOffsetKeepingAnchor(float anchorTime, float anchorViewportX)
            => Mathf.Max(0f, TimeToPixel(anchorTime) - anchorViewportX);
    }
}
