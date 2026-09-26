using UnityEngine;
using UnityEngine.UIElements;

namespace TimeMapGameplay.Editor
{
    /// <summary>Time ruler: major/minor ticks drawn with Painter2D and labels on major ticks.</summary>
    public sealed class RulerElement : VisualElement
    {
        TimelineViewport _viewport = new(62f);
        float _length = 20f;
        AxisUnit _units;

        public RulerElement()
        {
            AddToClassList("tmg-ruler");
            generateVisualContent += Draw;
        }

        public void Configure(TimelineViewport viewport, float length, AxisUnit units)
        {
            _viewport = viewport;
            _length = length;
            _units = units;
            RebuildLabels();
            MarkDirtyRepaint();
        }

        void RebuildLabels()
        {
            Clear();
            float major = _viewport.MajorTickStep();
            for (int i = 0; i * major <= _length + 0.0001f; i++)
            {
                float t = i * major;
                var label = new Label(TimeMath.Format(t, _units)) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("tmg-ruler__label");
                label.style.left = _viewport.TimeToPixel(t) + 4f;
                Add(label);
            }
        }

        void Draw(MeshGenerationContext mgc)
        {
            float height = contentRect.height;
            var painter = mgc.painter2D;
            painter.lineWidth = 1f;

            float minor = _viewport.MinorTickStep();
            if (minor > 0f)
                DrawTicks(painter, minor, height, 5f, EditorTheme.MinorTick);
            DrawTicks(painter, _viewport.MajorTickStep(), height, 10f, EditorTheme.MajorTick);
        }

        void DrawTicks(Painter2D painter, float step, float height, float tickHeight, Color color)
        {
            painter.strokeColor = color;
            painter.BeginPath();
            for (int i = 0; i * step <= _length + 0.0001f; i++)
            {
                float x = Mathf.Round(_viewport.TimeToPixel(i * step)) + 0.5f;
                painter.MoveTo(new Vector2(x, height));
                painter.LineTo(new Vector2(x, height - tickHeight));
            }
            painter.Stroke();
        }
    }
}
