using UnityEngine.UIElements;

namespace TimeMapGameplay.Editor
{
    /// <summary>
    /// Drags selected items horizontally (move) or a clip edge: the right edge changes the duration,
    /// the left edge moves the start keeping the end. Shift disables snapping,
    /// Ctrl/Cmd-click toggles the item in the selection without dragging.
    /// </summary>
    public sealed class ItemDragManipulator : PointerManipulator
    {
        public enum Mode
        {
            Move,
            Resize,
            ResizeStart
        }

        readonly TimelineView _view;
        readonly ItemElement _element;
        readonly Mode _mode;

        int _pointerId = -1;
        float _startPointerX;

        public ItemDragManipulator(TimelineView view, ItemElement element, Mode mode)
        {
            _view = view;
            _element = element;
            _mode = mode;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || _pointerId >= 0) return;
            evt.StopPropagation();

            bool startDrag = _mode != Mode.Move
                ? _view.BeginResize(_element)
                : _view.BeginItemDrag(_element, additive: evt.actionKey);
            if (!startDrag) return;

            _pointerId = evt.pointerId;
            _startPointerX = evt.position.x;
            target.CapturePointer(_pointerId);
            _view.Editing.BeginGesture(_mode != Mode.Move ? "Resize Clip" : "Move Items");
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != _pointerId || !target.HasPointerCapture(_pointerId)) return;

            float delta = _view.Viewport.PixelToTime(evt.position.x - _startPointerX);
            if (_mode == Mode.Resize)
                _view.DragResize(_element, delta, snap: !evt.shiftKey);
            else if (_mode == Mode.ResizeStart)
                _view.DragResizeStart(_element, delta, snap: !evt.shiftKey);
            else
                _view.DragItems(_element, delta, snap: !evt.shiftKey);
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
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
