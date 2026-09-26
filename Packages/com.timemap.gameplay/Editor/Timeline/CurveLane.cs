using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimeMapGameplay.Editor
{
    /// <summary>Lane of a <see cref="CurveTrack"/>: filled polyline with keys draggable along both axes.</summary>
    public sealed class CurveLane : VisualElement
    {
        public const float Height = 86f;
        const float Padding = 10f;
        const float HandleSize = 10f;


        readonly TimelineView _view;
        readonly List<VisualElement> _handles = new();
        readonly List<Label> _guideLabels = new();

        TimelineViewport _viewport;
        float _length;

        public CurveLane(TimelineView view, CurveTrack track)
        {
            _view = view;
            Track = track;
            AddToClassList("tmg-lane");
            AddToClassList("tmg-lane--curve");
            style.height = Height;
            generateVisualContent += Draw;
        }

        public CurveTrack Track { get; }

        public void Layout(TimelineViewport viewport, float length)
        {
            _viewport = viewport;
            _length = length;

            SyncHandles();
            var keys = Track.Keys;
            for (int i = 0; i < keys.Count; i++)
            {
                var handle = _handles[i];
                handle.style.left = viewport.TimeToPixel(keys[i].time) - HandleSize / 2f;
                handle.style.top = ValueToY(keys[i].value) - HandleSize / 2f;
                handle.tooltip = $"{_view.FormatTime(keys[i].time)} → {Track.ValueLabel}{keys[i].value:0.##}";
            }

            LayoutGuides();
            MarkDirtyRepaint();
        }

        public float ValueToY(float value)
        {
            float range = Track.ValueMax - Track.ValueMin;
            float t = Mathf.Approximately(range, 0f) ? 0.5f : (value - Track.ValueMin) / range;
            return Height - Padding - t * (Height - Padding * 2f);
        }

        /// <summary>Value change corresponding to a vertical pixel delta (positive = up).</summary>
        public float PixelsToValue(float pixelsUp) => pixelsUp / (Height - Padding * 2f) * (Track.ValueMax - Track.ValueMin);

        void SyncHandles()
        {
            int count = Track.Keys.Count;
            while (_handles.Count > count)
            {
                _handles[^1].RemoveFromHierarchy();
                _handles.RemoveAt(_handles.Count - 1);
            }
            while (_handles.Count < count)
            {
                int index = _handles.Count;
                var handle = new VisualElement();
                handle.AddToClassList("tmg-curve-key");
                handle.style.borderTopColor = handle.style.borderRightColor =
                    handle.style.borderBottomColor = handle.style.borderLeftColor = Track.Color;
                handle.AddManipulator(new KeyDragManipulator(_view, this, index));
                handle.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
                {
                    evt.menu.AppendAction("Удалить ключ", _ => _view.Operations.RemoveCurveKey(Track, index),
                        Track.Keys.Count > 1 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                    evt.StopPropagation();
                });
                _handles.Add(handle);
                Add(handle);
            }
        }

        void LayoutGuides()
        {
            var values = GuideValues();
            while (_guideLabels.Count < values.Count)
            {
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.AddToClassList("tmg-curve-guide");
                Insert(0, label);
                _guideLabels.Add(label);
            }
            for (int i = 0; i < _guideLabels.Count; i++)
            {
                bool used = i < values.Count;
                _guideLabels[i].style.display = used ? DisplayStyle.Flex : DisplayStyle.None;
                if (!used) continue;
                _guideLabels[i].text = $"{Track.ValueLabel}{values[i]:0.##}";
                _guideLabels[i].style.top = ValueToY(values[i]) - 13f;
            }
        }

        List<float> GuideValues()
        {
            var values = new List<float>();
            float min = Track.ValueMin, max = Track.ValueMax;
            if (max <= min) return values;
            for (int i = 1; i <= 2; i++)
            {
                float v = Mathf.Lerp(min, max, i / 3f);
                values.Add(Track.ValueStep > 0f ? TimeMath.Snap(v, Track.ValueStep) : v);
            }
            return values;
        }

        void Draw(MeshGenerationContext mgc)
        {
            var painter = mgc.painter2D;
            float width = _viewport.TimeToPixel(_length);

            painter.lineWidth = 1f;
            painter.strokeColor = EditorTheme.CurveGuide;
            painter.BeginPath();
            foreach (var v in GuideValues())
            {
                float y = Mathf.Round(ValueToY(v)) + 0.5f;
                painter.MoveTo(new Vector2(0f, y));
                painter.LineTo(new Vector2(width, y));
            }
            painter.Stroke();

            var keys = Track.Keys.OrderBy(k => k.time).ToList();
            if (keys.Count == 0) return;

            var points = new List<Vector2>(keys.Count + 2) { new(0f, ValueToY(Track.EvaluateValue(0f))) };
            foreach (var key in keys)
                points.Add(new Vector2(_viewport.TimeToPixel(key.time), ValueToY(key.value)));
            points.Add(new Vector2(width, ValueToY(Track.EvaluateValue(_length))));

            var fill = Track.Color;
            fill.a = 0.12f;
            painter.fillColor = fill;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, Height));
            foreach (var p in points) painter.LineTo(p);
            painter.LineTo(new Vector2(width, Height));
            painter.ClosePath();
            painter.Fill();

            painter.strokeColor = Track.Color;
            painter.lineWidth = 1.6f;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            painter.MoveTo(points[0]);
            for (int i = 1; i < points.Count; i++) painter.LineTo(points[i]);
            painter.Stroke();
        }

        /// <summary>Drags a curve key in time and value. Shift disables snapping.</summary>
        sealed class KeyDragManipulator : PointerManipulator
        {
            readonly TimelineView _view;
            readonly CurveLane _lane;
            readonly int _index;
            int _pointerId = -1;
            Vector2 _startPointer;
            CurveKey _startKey;

            public KeyDragManipulator(TimelineView view, CurveLane lane, int index)
            {
                _view = view;
                _lane = lane;
                _index = index;
            }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(OnDown);
                target.RegisterCallback<PointerMoveEvent>(OnMove);
                target.RegisterCallback<PointerUpEvent>(OnUp);
                target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(OnDown);
                target.UnregisterCallback<PointerMoveEvent>(OnMove);
                target.UnregisterCallback<PointerUpEvent>(OnUp);
                target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            }

            void OnDown(PointerDownEvent evt)
            {
                if (evt.button != 0 || _pointerId >= 0 || _index >= _lane.Track.Keys.Count) return;
                evt.StopPropagation();
                _view.Selection.SelectTrack(_lane.Track.Id);
                _pointerId = evt.pointerId;
                _startPointer = evt.position;
                _startKey = _lane.Track.Keys[_index];
                target.CapturePointer(_pointerId);
                _view.Editing.BeginGesture("Move Curve Key");
            }

            void OnMove(PointerMoveEvent evt)
            {
                if (evt.pointerId != _pointerId || !target.HasPointerCapture(_pointerId)) return;
                var delta = (Vector2)evt.position - _startPointer;
                float time = _startKey.time + _view.Viewport.PixelToTime(delta.x);
                float value = _startKey.value + _lane.PixelsToValue(-delta.y);
                _view.Operations.SetCurveKey(_lane.Track, _index, time, value, snap: !evt.shiftKey);
                evt.StopPropagation();
            }

            void OnUp(PointerUpEvent evt)
            {
                if (evt.pointerId != _pointerId) return;
                target.ReleasePointer(_pointerId);
                evt.StopPropagation();
            }

            void OnCaptureOut(PointerCaptureOutEvent evt)
            {
                if (_pointerId < 0) return;
                _pointerId = -1;
                _view.Editing.EndGesture();
            }
        }
    }
}
