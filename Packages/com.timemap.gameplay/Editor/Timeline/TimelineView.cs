using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimeMapGameplay.Editor
{
    /// <summary>
    /// Track headers, ruler, lanes (clips, markers, curves) and the playhead.
    /// Owns zoom; time is pushed in by the window, selection is shared through <see cref="TimelineSelection"/>.
    /// </summary>
    public sealed class TimelineView : VisualElement
    {
        public const float RulerHeight = 26f;
        public const float LaneHeight = 36f;
        public const float CollapsedHeight = 18f;
        const float EndPadding = 40f;
        const float WheelZoomFactor = 1.1f;
        const float MarqueeThreshold = 3f;


        readonly VisualElement _heads;
        readonly ScrollView _hScroll;
        readonly VisualElement _content;
        readonly RulerElement _ruler;
        readonly VisualElement _lanes;
        readonly VisualElement _done;
        readonly VisualElement _playhead;
        readonly VisualElement _marquee;
        readonly Dictionary<string, ItemElement> _items = new();
        readonly Dictionary<string, VisualElement> _headsByTrack = new();
        readonly List<(TrackDrawer drawer, VisualElement lane, TrackDrawerContext context)> _drawnLanes = new();
        readonly ScrollView _vScroll;
        readonly VisualElement _playheadB;
        readonly Dictionary<string, VisualElement> _lanesByTrack = new();
        readonly List<TelemetryOverlay> _overlays = new();
        readonly List<(TimelineChange change, VisualElement element)> _ghosts = new();
        IReadOnlyList<TimelineChange> _changes = Array.Empty<TimelineChange>();
        IReadOnlyDictionary<string, List<ValidationIssue>> _issues = new Dictionary<string, List<ValidationIssue>>();
        bool _issuesStale;
        readonly List<string> _collapsed;

        ProgressionTimeline _timeline;
        int _structureHash;
        float _time;

        // Drag state (move / resize).
        readonly Dictionary<TimelineItem, float> _dragOrigins = new();
        readonly List<(float start, float end)> _dragRanges = new();
        float _resizeOrigin;
        float _resizeOriginStart;

        public TimelineView(TimelineEditing editing, TimelineOperations operations, TimelineSelection selection, List<string> collapsedTracks,
            AnalysisState analysis = null)
        {
            Analysis = analysis;
            Editing = editing;
            Operations = operations;
            Selection = selection;
            _collapsed = collapsedTracks;
            AddToClassList("tmg-timeline");

            _vScroll = new ScrollView(ScrollViewMode.Vertical);
            _vScroll.AddToClassList("tmg-timeline__vscroll");
            Add(_vScroll);

            var row = new VisualElement();
            row.AddToClassList("tmg-timeline__row");
            _vScroll.Add(row);

            _heads = new VisualElement();
            _heads.AddToClassList("tmg-heads");
            row.Add(_heads);

            _hScroll = new ScrollView(ScrollViewMode.Horizontal);
            _hScroll.AddToClassList("tmg-timeline__hscroll");
            _hScroll.RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
            row.Add(_hScroll);

            _content = new VisualElement();
            _content.AddToClassList("tmg-content");
            _hScroll.Add(_content);

            _ruler = new RulerElement();
            _ruler.AddManipulator(new ScrubManipulator(this));
            _content.Add(_ruler);

            _lanes = new VisualElement();
            _lanes.AddToClassList("tmg-lanes");
            _lanes.generateVisualContent += DrawGrid;
            _lanes.AddManipulator(new MarqueeManipulator(this));
            _content.Add(_lanes);

            _done = new VisualElement { pickingMode = PickingMode.Ignore };
            _done.AddToClassList("tmg-done");
            _content.Add(_done);

            _marquee = new VisualElement { pickingMode = PickingMode.Ignore };
            _marquee.AddToClassList("tmg-marquee");
            _marquee.style.display = DisplayStyle.None;
            _lanes.Add(_marquee);

            _playhead = new VisualElement { pickingMode = PickingMode.Ignore };
            _playhead.AddToClassList("tmg-playhead");
            var cap = new VisualElement { pickingMode = PickingMode.Ignore };
            cap.AddToClassList("tmg-playhead__cap");
            _playhead.Add(cap);
            _content.Add(_playhead);

            _playheadB = new VisualElement { pickingMode = PickingMode.Ignore };
            _playheadB.AddToClassList("tmg-playhead");
            _playheadB.AddToClassList("tmg-playhead--b");
            var capB = new Label("B") { pickingMode = PickingMode.Ignore };
            capB.AddToClassList("tmg-playhead__cap");
            capB.AddToClassList("tmg-playhead__cap--b");
            _playheadB.Add(capB);
            _content.Add(_playheadB);
        }

        public TimelineEditing Editing { get; }
        public TimelineOperations Operations { get; }
        public TimelineSelection Selection { get; }
        public TimelineViewport Viewport { get; private set; } = new(62f);
        public ProgressionTimeline Timeline => _timeline;
        public float Time => _time;

        /// <summary>Raised when the user scrubs the ruler.</summary>
        public event Action<float> TimeScrubbed;

        /// <summary>Raised when the user sets playhead B (Alt + click or drag on the ruler).</summary>
        public event Action<float> TimeBScrubbed;

        public AnalysisState Analysis { get; }

        /// <summary>Raised when zoom changes from inside the view (Ctrl + wheel).</summary>
        public event Action<float> ZoomChanged;

        public void SetTimeline(ProgressionTimeline timeline)
        {
            _timeline = timeline;
            Rebuild();
        }

        public void SetZoom(float pixelsPerUnit)
        {
            var viewport = new TimelineViewport(pixelsPerUnit);
            if (Mathf.Approximately(viewport.PixelsPerUnit, Viewport.PixelsPerUnit)) return;

            // Keep the playhead in place while zooming from the toolbar slider.
            float anchorX = Viewport.TimeToPixel(_time) - _hScroll.scrollOffset.x;
            Viewport = viewport;
            LayoutAll();
            ScrollTo(viewport.ScrollOffsetKeepingAnchor(_time, anchorX));
        }

        public void SetTime(float time)
        {
            _time = time;
            LayoutPlayhead();
        }

        /// <summary>Re-reads the selection object and updates highlights.</summary>
        public void SyncSelection()
        {
            foreach (var pair in _items)
                pair.Value.Selected = Selection.Contains(pair.Key);
            foreach (var pair in _headsByTrack)
                pair.Value.EnableInClassList("tmg-head--selected", pair.Key == Selection.TrackId);
        }

        /// <summary>
        /// Re-reads the asset: rebuilds when tracks/items were added, removed or reordered, otherwise re-lays out.
        /// Returns true when the structure changed.
        /// </summary>
        public bool Refresh()
        {
            if (ComputeStructureHash() != _structureHash)
            {
                Rebuild();
                return true;
            }
            LayoutAll();
            return false;
        }

        /// <summary>Formats a time in the units of the current timeline axis.</summary>
        public string FormatTime(float time) => TimeMath.Format(time, _timeline != null ? _timeline.Axis.Units : AxisUnit.Hours);

        public bool IsCollapsed(TrackBase track) => _collapsed.Contains(track.Id);

        public void SetCollapsed(TrackBase track, bool collapsed)
        {
            if (collapsed == IsCollapsed(track)) return;
            if (collapsed) _collapsed.Add(track.Id);
            else _collapsed.Remove(track.Id);
            Rebuild();
        }

        internal void Scrub(float localX, bool snap, bool playheadB = false)
        {
            if (_timeline == null) return;
            float t = Viewport.PixelToTime(localX);
            if (snap) t = TimeMath.Snap(t, _timeline.Axis.SnapStep);
            t = Mathf.Clamp(t, 0f, _timeline.Axis.Length);
            if (playheadB) TimeBScrubbed?.Invoke(t);
            else TimeScrubbed?.Invoke(t);
        }

        // ---------- Item drag ----------

        /// <summary>Handles selection on pointer-down and captures drag origins. Returns false when no drag should start.</summary>
        public bool BeginItemDrag(ItemElement element, bool additive)
        {
            string id = element.Item.Id;
            if (additive)
            {
                Selection.Toggle(id);
                return false;
            }
            if (!Selection.Contains(id))
                Selection.Select(id);

            _dragOrigins.Clear();
            _dragRanges.Clear();
            foreach (var selectedId in Selection.Items)
            {
                if (!_timeline.TryFindItem(selectedId, out var item, out _)) continue;
                _dragOrigins[item] = item.Start;
                _dragRanges.Add((item.Start, item.End));
            }
            return _dragOrigins.ContainsKey(element.Item);
        }

        /// <summary>Moves all selected items so that the dragged one is at its origin + delta (snapped).</summary>
        public void DragItems(ItemElement element, float rawDelta, bool snap)
        {
            if (!_dragOrigins.TryGetValue(element.Item, out float origin)) return;
            float target = origin + rawDelta;
            if (snap) target = TimeMath.Snap(target, _timeline.Axis.SnapStep);
            float delta = TimelineOperations.ClampGroupDelta(_dragRanges, target - origin, _timeline.Axis.Length);
            Operations.MoveItems(_dragOrigins, delta);
        }

        public bool BeginResize(ItemElement element)
        {
            if (element.Item is not ClipBase clip) return false;
            if (!Selection.Contains(clip.Id)) Selection.Select(clip.Id);
            _resizeOrigin = clip.Duration;
            _resizeOriginStart = clip.Start;
            return true;
        }

        /// <summary>Drags the left edge: moves the start while the end stays in place.</summary>
        public void DragResizeStart(ItemElement element, float rawDelta, bool snap)
        {
            if (element.Item is not ClipBase clip) return;

            var axis = _timeline.Axis;
            float end = _resizeOriginStart + _resizeOrigin;
            float start = _resizeOriginStart + rawDelta;
            if (snap) start = TimeMath.Snap(start, axis.SnapStep);
            float minDuration = snap ? Mathf.Min(axis.SnapStep, _resizeOrigin) : ClipBase.MinDuration;
            start = Mathf.Clamp(start, 0f, Mathf.Max(0f, end - minDuration));
            if (Mathf.Approximately(start, clip.Start)) return;

            Editing.Apply("Resize Clip", _ =>
            {
                clip.Start = start;
                clip.Duration = end - start;
            });
        }

        public void DragResize(ItemElement element, float rawDelta, bool snap)
        {
            if (element.Item is not ClipBase clip) return;

            var axis = _timeline.Axis;
            float duration = _resizeOrigin + rawDelta;
            if (snap) duration = TimeMath.Snap(clip.Start + duration, axis.SnapStep) - clip.Start;
            float minDuration = snap ? axis.SnapStep : ClipBase.MinDuration;
            duration = TimeMath.ClampDuration(duration, clip.Start, axis.Length, minDuration);
            if (Mathf.Approximately(duration, clip.Duration)) return;

            Editing.Apply("Resize Clip", _ => clip.Duration = duration);
        }

        // ---------- Build / layout ----------

        void Rebuild()
        {
            _heads.Clear();
            _headsByTrack.Clear();
            _drawnLanes.Clear();
            _items.Clear();
            _lanesByTrack.Clear();
            _overlays.Clear();
            foreach (var lane in _lanes.Children().Where(c => c != _marquee).ToList())
                lane.RemoveFromHierarchy();
            _structureHash = ComputeStructureHash();

            if (_timeline == null) return;

            _heads.Add(BuildRulerHead());

            foreach (var track in _timeline.Tracks)
            {
                if (track == null) continue;
                bool collapsed = IsCollapsed(track);
                var drawer = TrackDrawers.For(track.GetType());
                VisualElement lane;
                if (collapsed)
                {
                    lane = new VisualElement();
                    lane.AddToClassList("tmg-lane");
                    lane.AddToClassList("tmg-lane--collapsed");
                }
                else
                {
                    var context = new TrackDrawerContext(this, track);
                    lane = drawer.CreateLane(context);
                    _drawnLanes.Add((drawer, lane, context));
                    if (Analysis != null && track.ItemType != null)
                    {
                        var overlay = new TelemetryOverlay(track, Analysis);
                        lane.Add(overlay);
                        _overlays.Add(overlay);
                    }
                }
                float height = collapsed ? CollapsedHeight : drawer.LaneHeight;
                lane.style.height = height;
                _lanes.Insert(_lanes.childCount - 1, lane);
                _lanesByTrack[track.Id] = lane;

                var head = BuildHead(track, collapsed);
                head.style.height = height;
                _heads.Add(head);
                _headsByTrack[track.Id] = head;
            }

            SyncSelection();
            UpdateHeadIssueCounts();
            BuildGhosts();
            LayoutAll();
        }

        /// <summary>Creates a draggable, selectable item element with the standard context menu (for drawers).</summary>
        public ItemElement CreateItemElement(TimelineItem item, TrackBase track)
        {
            var element = new ItemElement(item, track);
            element.AddManipulator(new ItemDragManipulator(this, element, ItemDragManipulator.Mode.Move));
            element.ResizeHandle?.AddManipulator(new ItemDragManipulator(this, element, ItemDragManipulator.Mode.Resize));
            element.ResizeStartHandle?.AddManipulator(new ItemDragManipulator(this, element, ItemDragManipulator.Mode.ResizeStart));
            element.RegisterCallback<ContextualMenuPopulateEvent>(evt => PopulateItemMenu(evt, element));
            element.SetIssues(_issues.TryGetValue(item.Id, out var issues) ? issues : null, _issuesStale);
            _items[item.Id] = element;
            return element;
        }

        /// <summary>Adds the standard "add item / paste here" context menu to a lane (for drawers).</summary>
        public void AttachLaneMenu(VisualElement lane, TrackBase track)
        {
            lane.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
            {
                if (evt.target != lane) return;
                float t = SnappedTimeAt(evt.localMousePosition.x);
                if (track.ItemType != null)
                    evt.menu.AppendAction($"Добавить «{ObjectNames.NicifyVariableName(track.ItemType.Name)}» в {FormatTime(t)}",
                        _ =>
                        {
                            var item = Operations.AddItem(track, t);
                            if (item != null) Selection.Select(item.Id);
                        });
                evt.menu.AppendAction($"Вставить в {FormatTime(t)}", _ => Selection.Set(Operations.Paste(t)),
                    TimelineOperations.HasClipboard ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            });
        }

        // ---------- Analysis: A/B, version ghosts, telemetry ----------

        /// <summary>Re-reads <see cref="Analysis"/> (playhead B, telemetry overlay).</summary>
        public void RefreshAnalysis()
        {
            LayoutPlayhead();
            foreach (var overlay in _overlays) overlay.Layout(Viewport);
            LayoutTelemetryNotes();
        }

        /// <summary>
        /// Shows baseline positions of moved, resized and removed items as ghosts (comparison with a saved version).
        /// Pass an empty list to hide them.
        /// </summary>
        public void SetGhosts(IReadOnlyList<TimelineChange> changes)
        {
            _changes = changes ?? Array.Empty<TimelineChange>();
            BuildGhosts();
            LayoutGhosts();
        }

        public int GhostCount => _ghosts.Count;

        void BuildGhosts()
        {
            foreach (var (_, element) in _ghosts) element.RemoveFromHierarchy();
            _ghosts.Clear();
            foreach (var change in _changes)
            {
                if (!change.HasGhost || change.TrackId == null || !_lanesByTrack.TryGetValue(change.TrackId, out var lane)) continue;
                if (lane.ClassListContains("tmg-lane--collapsed")) continue;
                var ghost = new VisualElement { pickingMode = PickingMode.Ignore, tooltip = change.Description };
                ghost.AddToClassList("tmg-ghost");
                ghost.AddToClassList(change.OldDuration > 0f ? "tmg-ghost--clip" : "tmg-ghost--marker");
                if (change.Kind == ChangeKind.Removed) ghost.AddToClassList("tmg-ghost--removed");
                var label = new Label(change.ItemName) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("tmg-ghost__label");
                ghost.Add(label);
                lane.Insert(0, ghost);
                _ghosts.Add((change, ghost));
            }
        }

        void LayoutGhosts()
        {
            foreach (var (change, element) in _ghosts)
            {
                element.style.left = Viewport.TimeToPixel(change.OldStart);
                if (change.OldDuration > 0f)
                    element.style.width = Mathf.Max(Viewport.TimeToPixel(change.OldDuration) - 2f, 14f);
            }
        }

        void LayoutTelemetryNotes()
        {
            if (_timeline == null) return;
            var units = _timeline.Axis.Units;
            foreach (var element in _items.Values)
            {
                string note = null;
                if (Analysis != null && Analysis.ShowTelemetry && Analysis.TryGetStats(element.Item, out var stats))
                    note = TelemetryOverlay.Describe(element.Item, stats, units);
                element.SetNote(note);
            }
        }

        // ---------- Validation marks ----------

        /// <summary>
        /// Shows validation results on items and track heads. <paramref name="stale"/> dims the marks
        /// (manual mode after changes: old marks stay until the next check).
        /// </summary>
        public void SetIssues(IReadOnlyDictionary<string, List<ValidationIssue>> issuesByItem, bool stale)
        {
            _issues = issuesByItem ?? new Dictionary<string, List<ValidationIssue>>();
            _issuesStale = stale;
            foreach (var pair in _items)
                pair.Value.SetIssues(_issues.TryGetValue(pair.Key, out var list) ? list : null, stale);
            UpdateHeadIssueCounts();
        }

        void UpdateHeadIssueCounts()
        {
            if (_timeline == null) return;
            foreach (var track in _timeline.Tracks)
            {
                if (track == null || !_headsByTrack.TryGetValue(track.Id, out var head)) continue;
                int count = track.Items.Count(i => i != null && _issues.ContainsKey(i.Id));
                var badge = head.Q<Label>("track-issues");
                badge.text = count > 0 ? count.ToString() : "";
                badge.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                badge.EnableInClassList("tmg-head__issues--stale", _issuesStale);
                badge.tooltip = $"Нарушений: {count}";
            }
        }

        /// <summary>Selects an item and scrolls it into view. Expands its track when collapsed.</summary>
        public void FocusItem(string id)
        {
            if (_timeline == null || !_timeline.TryFindItem(id, out _, out var track)) return;
            if (IsCollapsed(track)) SetCollapsed(track, false);
            Selection.Select(id);
            if (!_items.TryGetValue(id, out var element)) return;
            schedule.Execute(() =>
            {
                _hScroll.ScrollTo(element);
                _vScroll.ScrollTo(element);
            });
        }

        void PopulateItemMenu(ContextualMenuPopulateEvent evt, ItemElement element)
        {
            if (!Selection.Contains(element.Item.Id))
                Selection.Select(element.Item.Id);
            var ids = Selection.Items.ToList();
            string suffix = ids.Count > 1 ? $" ({ids.Count})" : "";
            evt.menu.AppendAction("Копировать" + suffix, _ => Operations.Copy(ids));
            evt.menu.AppendAction("Дублировать" + suffix, _ => Selection.Set(Operations.Duplicate(ids)));
            evt.menu.AppendAction("Удалить" + suffix, _ => Operations.DeleteItems(ids));
            evt.StopPropagation();
        }

        VisualElement BuildRulerHead()
        {
            var head = new VisualElement();
            head.AddToClassList("tmg-head");
            head.AddToClassList("tmg-head--ruler");

            var units = new Label(UnitsCaption(_timeline.Axis.Units));
            units.AddToClassList("tmg-head__units");
            head.Add(units);

            var add = new Button { text = "+ Трек", tooltip = "Добавить трек" };
            add.AddToClassList("tmg-head__add");
            add.clicked += () =>
            {
                var menu = new GenericDropdownMenu();
                foreach (var (type, name) in TimelineOperations.TrackTypes())
                    menu.AddItem(name, false, () => Selection.SelectTrack(Operations.AddTrack(type, name).Id));
                menu.DropDown(add.worldBound, add, DropdownMenuSizeMode.Auto);
            };
            head.Add(add);
            return head;
        }

        VisualElement BuildHead(TrackBase track, bool collapsed)
        {
            var head = new VisualElement();
            head.AddToClassList("tmg-head");
            head.EnableInClassList("tmg-head--collapsed", collapsed);

            var foldout = new Foldout { value = !collapsed, tooltip = collapsed ? "Развернуть" : "Свернуть" };
            foldout.AddToClassList("tmg-head__foldout");
            foldout.RegisterValueChangedCallback(e =>
            {
                e.StopPropagation();
                SetCollapsed(track, !e.newValue);
            });
            head.Add(foldout);

            var strip = new VisualElement { name = "track-strip" };
            strip.AddToClassList("tmg-head__strip");
            head.Add(strip);

            var texts = new VisualElement();
            texts.AddToClassList("tmg-head__texts");
            texts.Add(new Label { name = "track-name" });
            if (!collapsed)
            {
                var sub = new Label { name = "track-subtitle" };
                sub.AddToClassList("tmg-head__subtitle");
                texts.Add(sub);
            }
            head.Add(texts);

            var issues = new Label { name = "track-issues" };
            issues.AddToClassList("tmg-head__issues");
            issues.style.display = DisplayStyle.None;
            head.Add(issues);

            head.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0) Selection.SelectTrack(track.Id);
            });
            head.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
            {
                int index = _timeline.Tracks.IndexOf(track);
                evt.menu.AppendAction(collapsed ? "Развернуть" : "Свернуть", _ => SetCollapsed(track, !collapsed));
                evt.menu.AppendAction("Выше", _ => Operations.MoveTrack(track, -1),
                    index > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                evt.menu.AppendAction("Ниже", _ => Operations.MoveTrack(track, 1),
                    index < _timeline.Tracks.Count - 1 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                evt.menu.AppendSeparator();
                evt.menu.AppendAction("Удалить трек", _ =>
                {
                    if (EditorUtility.DisplayDialog("Удалить трек", $"Удалить трек «{track.Name}» со всеми элементами?", "Удалить", "Отмена"))
                        Operations.RemoveTrack(track);
                });
            });
            return head;
        }

        void LayoutAll()
        {
            if (_timeline == null) return;

            var axis = _timeline.Axis;
            _content.style.width = Viewport.TimeToPixel(axis.Length) + EndPadding;
            _ruler.Configure(Viewport, axis.Length, axis.Units);
            _lanes.MarkDirtyRepaint();

            foreach (var element in _items.Values)
                element.Layout(Viewport, axis.Units);
            foreach (var (drawer, lane, context) in _drawnLanes)
                drawer.Layout(lane, context);
            foreach (var overlay in _overlays)
                overlay.Layout(Viewport);
            LayoutGhosts();
            LayoutTelemetryNotes();
            foreach (var track in _timeline.Tracks)
                if (track != null && _headsByTrack.TryGetValue(track.Id, out var head))
                    LayoutHead(head, track);

            LayoutPlayhead();
        }

        static void LayoutHead(VisualElement head, TrackBase track)
        {
            head.Q<Label>("track-name").text = track.Name;
            head.Q("track-strip").style.backgroundColor = track.Color;
            var subtitle = head.Q<Label>("track-subtitle");
            if (subtitle == null) return;
            subtitle.text = track.Subtitle;
            subtitle.style.display = string.IsNullOrEmpty(track.Subtitle) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void LayoutPlayhead()
        {
            float x = Viewport.TimeToPixel(_time);
            _playhead.style.left = x;
            _done.style.width = x;

            bool showB = Analysis is { AbEnabled: true } && _timeline != null;
            _playheadB.style.display = showB ? DisplayStyle.Flex : DisplayStyle.None;
            if (showB) _playheadB.style.left = Viewport.TimeToPixel(Mathf.Clamp(Analysis.TimeB, 0f, _timeline.Axis.Length));
        }

        void DrawGrid(MeshGenerationContext mgc)
        {
            if (_timeline == null) return;

            float step = Viewport.MajorTickStep();
            float height = _lanes.contentRect.height;
            var painter = mgc.painter2D;
            painter.lineWidth = 1f;
            painter.strokeColor = EditorTheme.Grid;
            painter.BeginPath();
            for (int i = 0; i * step <= _timeline.Axis.Length + 0.0001f; i++)
            {
                float x = Mathf.Round(Viewport.TimeToPixel(i * step)) + 0.5f;
                painter.MoveTo(new Vector2(x, 0f));
                painter.LineTo(new Vector2(x, height));
            }
            painter.Stroke();
        }

        public float SnappedTimeAt(float localX)
        {
            float t = TimeMath.Snap(Viewport.PixelToTime(localX), _timeline.Axis.SnapStep);
            return Mathf.Clamp(t, 0f, _timeline.Axis.Length);
        }

        void OnWheel(WheelEvent evt)
        {
            if (!evt.actionKey || _timeline == null) return;

            float anchorX = _hScroll.contentViewport.WorldToLocal(evt.mousePosition).x;
            float anchorTime = Viewport.PixelToTime(anchorX + _hScroll.scrollOffset.x);
            float factor = evt.delta.y < 0f ? WheelZoomFactor : 1f / WheelZoomFactor;

            Viewport = new TimelineViewport(Viewport.PixelsPerUnit * factor);
            LayoutAll();
            ScrollTo(Viewport.ScrollOffsetKeepingAnchor(anchorTime, anchorX));
            ZoomChanged?.Invoke(Viewport.PixelsPerUnit);
            evt.StopPropagation();
        }

        void ScrollTo(float x)
        {
            // Content width changes are applied on the next layout pass, so defer the scroll.
            schedule.Execute(() => _hScroll.scrollOffset = new Vector2(x, _hScroll.scrollOffset.y));
        }

        int ComputeStructureHash()
        {
            if (_timeline == null) return 0;
            var hash = new HashCode();
            foreach (var track in _timeline.Tracks)
            {
                hash.Add(track?.Id);
                if (track == null) continue;
                hash.Add(IsCollapsed(track));
                foreach (var item in track.Items)
                    hash.Add(item?.Id);
                if (track is CurveTrack curve)
                    hash.Add(curve.Keys.Count);
            }
            hash.Add(_timeline.Axis.Units);
            return hash.ToHashCode();
        }

        static string UnitsCaption(AxisUnit units) => units switch
        {
            AxisUnit.Hours => "ч игры",
            AxisUnit.Sessions => "сессии",
            AxisUnit.Quests => "квесты",
            _ => ""
        };

        // ---------- Marquee ----------

        void UpdateMarquee(Vector2 from, Vector2 to)
        {
            var rect = Rect.MinMaxRect(Mathf.Min(from.x, to.x), Mathf.Min(from.y, to.y), Mathf.Max(from.x, to.x), Mathf.Max(from.y, to.y));
            _marquee.style.display = DisplayStyle.Flex;
            _marquee.style.left = rect.x;
            _marquee.style.top = rect.y;
            _marquee.style.width = rect.width;
            _marquee.style.height = rect.height;
        }

        void FinishMarquee(Vector2 from, Vector2 to, bool additive)
        {
            _marquee.style.display = DisplayStyle.None;
            if (Vector2.Distance(from, to) < MarqueeThreshold)
            {
                if (!additive) Selection.Clear();
                return;
            }

            var localRect = Rect.MinMaxRect(Mathf.Min(from.x, to.x), Mathf.Min(from.y, to.y), Mathf.Max(from.x, to.x), Mathf.Max(from.y, to.y));
            var worldRect = _lanes.LocalToWorld(localRect);
            var hits = _items.Where(p => p.Value.worldBound.Overlaps(worldRect)).Select(p => p.Key);
            Selection.Set(additive ? Selection.Items.Concat(hits).ToList() : hits.ToList());
        }

        /// <summary>Drag on empty lane space selects items inside the rectangle; a click clears the selection.</summary>
        sealed class MarqueeManipulator : PointerManipulator
        {
            readonly TimelineView _view;
            int _pointerId = -1;
            Vector2 _start;

            public MarqueeManipulator(TimelineView view) => _view = view;

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(OnDown);
                target.RegisterCallback<PointerMoveEvent>(OnMove);
                target.RegisterCallback<PointerUpEvent>(OnUp);
                target.RegisterCallback<PointerCaptureOutEvent>(_ => _pointerId = -1);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(OnDown);
                target.UnregisterCallback<PointerMoveEvent>(OnMove);
                target.UnregisterCallback<PointerUpEvent>(OnUp);
            }

            // Items and curve keys stop propagation of their pointer-down, so this only sees empty space.
            void OnDown(PointerDownEvent evt)
            {
                if (evt.button != 0 || _pointerId >= 0) return;
                _pointerId = evt.pointerId;
                _start = evt.localPosition;
                target.CapturePointer(_pointerId);
            }

            void OnMove(PointerMoveEvent evt)
            {
                if (evt.pointerId != _pointerId || !target.HasPointerCapture(_pointerId)) return;
                if (Vector2.Distance(_start, evt.localPosition) >= MarqueeThreshold)
                    _view.UpdateMarquee(_start, evt.localPosition);
            }

            void OnUp(PointerUpEvent evt)
            {
                if (evt.pointerId != _pointerId) return;
                target.ReleasePointer(_pointerId);
                _view.FinishMarquee(_start, evt.localPosition, evt.actionKey);
            }
        }

        /// <summary>Click or drag on the ruler moves the playhead. Shift disables snapping.</summary>
        sealed class ScrubManipulator : PointerManipulator
        {
            readonly TimelineView _view;
            int _pointerId = -1;
            bool _playheadB;

            public ScrubManipulator(TimelineView view) => _view = view;

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(OnDown);
                target.RegisterCallback<PointerMoveEvent>(OnMove);
                target.RegisterCallback<PointerUpEvent>(OnUp);
                target.RegisterCallback<PointerCaptureOutEvent>(_ => _pointerId = -1);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(OnDown);
                target.UnregisterCallback<PointerMoveEvent>(OnMove);
                target.UnregisterCallback<PointerUpEvent>(OnUp);
            }

            void OnDown(PointerDownEvent evt)
            {
                if (evt.button != 0) return;
                _pointerId = evt.pointerId;
                target.CapturePointer(_pointerId);
                _playheadB = evt.altKey;
                _view.Scrub(evt.localPosition.x, !evt.shiftKey, _playheadB);
                evt.StopPropagation();
            }

            void OnMove(PointerMoveEvent evt)
            {
                if (evt.pointerId != _pointerId || !target.HasPointerCapture(_pointerId)) return;
                _view.Scrub(evt.localPosition.x, !evt.shiftKey, _playheadB);
            }

            void OnUp(PointerUpEvent evt)
            {
                if (evt.pointerId != _pointerId) return;
                target.ReleasePointer(_pointerId);
            }
        }
    }
}
