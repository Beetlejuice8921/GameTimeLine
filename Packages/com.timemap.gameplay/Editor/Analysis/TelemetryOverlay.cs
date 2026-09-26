using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimeMapGameplay.Editor
{
    /// <summary>
    /// Draws playtest distributions under the items of one lane: sample dots, the p25–p75 band, the median tick
    /// and a line from the planned time to the median (orange = later than planned, blue = earlier).
    /// </summary>
    public sealed class TelemetryOverlay : VisualElement
    {
        static readonly Color Early = new(0.31f, 0.56f, 0.85f);

        readonly TrackBase _track;
        readonly AnalysisState _analysis;
        TimelineViewport _viewport;

        public TelemetryOverlay(TrackBase track, AnalysisState analysis)
        {
            _track = track;
            _analysis = analysis;
            pickingMode = PickingMode.Ignore;
            AddToClassList("tmg-telemetry");
            generateVisualContent += Draw;
        }

        public void Layout(TimelineViewport viewport)
        {
            _viewport = viewport;
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext mgc)
        {
            if (_analysis == null || !_analysis.ShowTelemetry || _analysis.TelemetryIndex == null) return;

            float height = contentRect.height;
            float y = height - 4f;
            var painter = mgc.painter2D;

            foreach (var item in _track.Items.Where(i => i != null))
            {
                if (!_analysis.TryGetStats(item, out var stats)) continue;

                float planned = _viewport.TimeToPixel(item.Start);
                float median = _viewport.TimeToPixel(stats.Median);
                var deviation = stats.Median >= item.Start ? EditorTheme.Warn : Early;

                // Planned → median.
                painter.lineWidth = 1f;
                painter.strokeColor = deviation;
                painter.BeginPath();
                painter.MoveTo(new Vector2(planned, y));
                painter.LineTo(new Vector2(median, y));
                painter.Stroke();

                // Interquartile band.
                var band = deviation;
                band.a = 0.45f;
                painter.strokeColor = band;
                painter.lineWidth = 4f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(_viewport.TimeToPixel(stats.P25), y));
                painter.LineTo(new Vector2(Mathf.Max(_viewport.TimeToPixel(stats.P75), _viewport.TimeToPixel(stats.P25) + 1f), y));
                painter.Stroke();

                // Samples.
                var dot = EditorTheme.IsDark ? new Color(1f, 1f, 1f, 0.55f) : new Color(0f, 0f, 0f, 0.5f);
                painter.fillColor = dot;
                foreach (float t in stats.Times)
                {
                    painter.BeginPath();
                    painter.Arc(new Vector2(_viewport.TimeToPixel(t), y - 4f), 1.5f, Angle.Degrees(0f), Angle.Degrees(360f));
                    painter.Fill();
                }

                // Median tick.
                painter.strokeColor = deviation;
                painter.lineWidth = 2f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(median, y - 3f));
                painter.LineTo(new Vector2(median, y + 3f));
                painter.Stroke();
            }
        }

        /// <summary>One-line summary for tooltips and the inspector.</summary>
        public static string Describe(TimelineItem item, TelemetryStats stats, AxisUnit units)
        {
            string F(float t) => TimeMath.Format(t, units);
            float delta = stats.Median - item.Start;
            string sign = delta >= 0f ? "+" : "−";
            return $"Факт (n={stats.Count}): медиана {F(stats.Median)} ({sign}{F(Mathf.Abs(delta))} к плану), " +
                   $"половина игроков {F(stats.P25)}–{F(stats.P75)}, разброс {F(stats.Min)}–{F(stats.Max)}";
        }
    }
}
