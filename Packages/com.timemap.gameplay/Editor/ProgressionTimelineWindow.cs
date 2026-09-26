using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimeMapGameplay.Editor
{
    /// <summary>Main editor window: toolbar, game slice, timeline and inspector.</summary>
    public sealed class ProgressionTimelineWindow : EditorWindow
    {
        const string UiRoot = "Packages/com.timemap.gameplay/Editor/UI/";
        const float PlaybackUnitsPerSecond = 1f;

        [SerializeField] ProgressionTimeline _timeline;
        [SerializeField] float _time;
        [SerializeField] float _zoom = 62f;
        [SerializeField] TimelineSelection _selection = new();
        [SerializeField] List<string> _collapsedTracks = new();
        [SerializeField] AnalysisState _analysis = new();

        IReadOnlyList<TimelineChange> _changes = System.Array.Empty<TimelineChange>();
        bool _changesPending;
        ToolbarToggle _abToggle;

        TimelineEditing _editing;
        TimelineOperations _operations;
        TimelineView _timelineView;
        SliceView _slice;
        InspectorView _inspector;

        VisualElement _main;
        VisualElement _emptyState;
        VisualElement _inspectorHost;
        VisualElement _changeTracker;
        ToolbarButton _playButton;
        Label _timeLabel;
        Label _snapLabel;
        Slider _zoomSlider;
        ObjectField _assetField;
        ToolbarButton _playModeButton;
        ToolbarButton _saveButton;
        ToolbarButton _checkButton;
        ToolbarToggle _autoToggle;
        Button _issuesBadge;
        ValidationService _validation;
        bool _validationPending;

        IVisualElementScheduledItem _playback;
        double _lastTick;
        bool _playing;
        bool _inspectorDirty;

        [MenuItem("Window/TimeMapGameplay/Progression Timeline")]
        static void OpenFromMenu() => Open();

        public static ProgressionTimelineWindow Open()
        {
            var window = GetWindow<ProgressionTimelineWindow>();
            window.titleContent = new GUIContent("Progression Timeline");
            window.minSize = new Vector2(900f, 560f);
            return window;
        }

        public static void Open(ProgressionTimeline timeline)
        {
            Open().SetTimeline(timeline);
        }

        [OnOpenAsset]
        static bool OnOpenAsset(EntityId entityId, int line)
        {
            if (EditorUtility.EntityIdToObject(entityId) is not ProgressionTimeline timeline) return false;
            Open(timeline);
            return true;
        }

        void CreateGUI()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UiRoot + "ProgressionTimelineWindow.uxml");
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(UiRoot + "ProgressionTimelineWindow.uss");
            var root = rootVisualElement;
            root.styleSheets.Add(style);
            tree.CloneTree(root);
            root.AddToClassList("tmg-root");
            root.EnableInClassList(EditorTheme.LightRootClass, !EditorTheme.IsDark);

            _editing = new TimelineEditing(() => _timeline);
            _editing.Changed += OnTimelineEdited;
            _operations = new TimelineOperations(_editing, () => _timeline);
            _selection.Changed += OnSelectionChanged;
            _analysis.Changed += OnAnalysisChanged;
            _validation = new ValidationService();
            _validation.Changed += OnValidationChanged;

            _main = root.Q("main");
            _emptyState = root.Q("empty-state");
            _inspectorHost = root.Q("inspector-host");

            _slice = new SliceView();
            root.Q("slice-host").Add(_slice);

            _timelineView = new TimelineView(_editing, _operations, _selection, _collapsedTracks, _analysis);
            _timelineView.TimeScrubbed += t => SetTime(t);
            _timelineView.TimeBScrubbed += t =>
            {
                _analysis.TimeB = t;
                _analysis.AbEnabled = true;
            };
            _timelineView.ZoomChanged += z =>
            {
                _zoom = z;
                _zoomSlider.SetValueWithoutNotify(z);
            };
            root.Q("timeline-host").Add(_timelineView);

            _inspector = new InspectorView(_operations, _selection, _validation, id => _timelineView.FocusItem(id), _analysis, () => _changes);
            _inspectorHost.Add(_inspector);

            BindToolbar(root);
            root.Q<Button>("create-demo-button").clicked += () => SetTimeline(DemoTimelineFactory.CreateDemoAsset());

            root.focusable = true;
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            root.RegisterCallback<ValidateCommandEvent>(OnValidateCommand);
            root.RegisterCallback<ExecuteCommandEvent>(OnExecuteCommand);
            root.RegisterCallback<PointerDownEvent>(evt =>
            {
                // Clicks outside input fields return keyboard focus to the window for shortcuts.
                if (evt.target is VisualElement target && !IsInputElement(target)) root.Focus();
            }, TrickleDown.TrickleDown);

            _playback = root.schedule.Execute(TickPlayback).Every(16);
            _playback.Pause();
            // Inspector rebuilds are coalesced: many selection changes per frame cause a single rebuild.
            root.schedule.Execute(() =>
            {
                if (_validationPending)
                {
                    // Debounced: a drag produces many edits per second, validation runs at most every 50 ms.
                    _validationPending = false;
                    _validation.NotifyChanged(_timeline);
                }
                if (_changesPending)
                {
                    _changesPending = false;
                    RecomputeChanges();
                }
                if (!_inspectorDirty) return;
                _inspectorDirty = false;
                _inspector.Show(_timeline, _time);
            }).Every(50);

            _timelineView.SetZoom(_zoom);
            ApplyTimeline();
        }

        void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            _editing?.EndGesture();
            if (_selection != null) _selection.Changed -= OnSelectionChanged;
            if (_analysis != null) _analysis.Changed -= OnAnalysisChanged;
        }

        void BindToolbar(VisualElement root)
        {
            _playButton = root.Q<ToolbarButton>("play-button");
            _playButton.clicked += TogglePlayback;

            _timeLabel = root.Q<Label>("time-label");
            _snapLabel = root.Q<Label>("snap-label");

            _zoomSlider = root.Q<Slider>("zoom-slider");
            _zoomSlider.lowValue = TimelineViewport.MinPixelsPerUnit;
            _zoomSlider.highValue = TimelineViewport.MaxPixelsPerUnit;
            _zoomSlider.SetValueWithoutNotify(_zoom);
            _zoomSlider.RegisterValueChangedCallback(e =>
            {
                _zoom = e.newValue;
                _timelineView.SetZoom(_zoom);
            });

            _assetField = root.Q<ObjectField>("asset-field");
            _assetField.objectType = typeof(ProgressionTimeline);
            _assetField.allowSceneObjects = false;
            _assetField.RegisterValueChangedCallback(e => SetTimeline(e.newValue as ProgressionTimeline));

            _playModeButton = root.Q<ToolbarButton>("playmode-button");
            _playModeButton.clicked += PlayFromTime;
            _saveButton = root.Q<ToolbarButton>("save-button");
            _saveButton.clicked += CreateSave;
            _checkButton = root.Q<ToolbarButton>("check-button");
            _checkButton.clicked += () => _validation.Run(_timeline, logToConsole: true);
            _autoToggle = root.Q<ToolbarToggle>("auto-toggle");
            _autoToggle.RegisterValueChangedCallback(e =>
            {
                _validation.SetAutoMode(e.newValue);
                if (e.newValue) _validation.Run(_timeline, logToConsole: false);
                UpdateBadge();
            });
            _issuesBadge = root.Q<Button>("issues-badge");
            _issuesBadge.clicked += FocusFirstIssue;

            _abToggle = root.Q<ToolbarToggle>("ab-toggle");
            _abToggle.SetValueWithoutNotify(_analysis.AbEnabled);
            _abToggle.RegisterValueChangedCallback(e =>
            {
                if (e.newValue && _timeline != null && Mathf.Approximately(_analysis.TimeB, 0f))
                    _analysis.TimeB = Mathf.Min(_time + _timeline.Axis.Length * 0.25f, _timeline.Axis.Length);
                _analysis.AbEnabled = e.newValue;
            });
            BuildToolsMenu(root.Q<ToolbarMenu>("tools-menu"));
        }

        public void SetTimeline(ProgressionTimeline timeline)
        {
            if (_timeline == timeline && _timelineView?.Timeline == timeline) return;

            StopPlayback();
            _editing?.EndGesture();
            if (_timeline != timeline && _analysis.Baseline != null) _analysis.Baseline = null;
            _timeline = timeline;
            _selection.Clear();
            if (_timeline != null)
                _time = Mathf.Clamp(_time, 0f, _timeline.Axis.Length);
            ApplyTimeline();
        }

        void ApplyTimeline()
        {
            if (_timelineView == null) return;

            bool hasTimeline = _timeline != null;
            _main.style.display = hasTimeline ? DisplayStyle.Flex : DisplayStyle.None;
            _emptyState.style.display = hasTimeline ? DisplayStyle.None : DisplayStyle.Flex;
            _assetField.SetValueWithoutNotify(_timeline);

            _selection.Prune(_timeline);

            _changeTracker?.RemoveFromHierarchy();
            _changeTracker = null;
            if (hasTimeline)
            {
                _changeTracker = new VisualElement { name = "change-tracker" };
                _changeTracker.style.display = DisplayStyle.None;
                _changeTracker.TrackSerializedObjectValue(new SerializedObject(_timeline), _ => OnExternalChange());
                rootVisualElement.Add(_changeTracker);
            }

            _timelineView.SetTimeline(_timeline);
            ResetValidation();
            RecomputeChanges();
            _inspector.Show(_timeline, _time);
            _inspectorDirty = false;
            SetTime(_time);
            UpdateToolbar();
        }

        // ---------- Validation ----------

        ProgressionTimeline _validatedTimeline;

        /// <summary>New timeline → fresh results; same timeline (undo, external change) → treated as an edit.</summary>
        void ResetValidation()
        {
            if (_validatedTimeline == _timeline && _timeline != null)
            {
                _validation.NotifyChanged(_timeline);
                return;
            }
            _validatedTimeline = _timeline;
            _validation.Reset();
            if (_timeline != null && _validation.IsAutoMode(_timeline))
                _validation.Run(_timeline, logToConsole: false);
            else
                OnValidationChanged();
        }

        void OnValidationChanged()
        {
            if (_timelineView == null) return;
            _timelineView.SetIssues(_validation.IssuesByItem(), _validation.IsStale && _validation.HasRun);
            _inspector.RefreshIssues();
            UpdateBadge();
        }

        void UpdateBadge()
        {
            if (_issuesBadge == null || _timeline == null) return;
            bool auto = _validation.IsAutoMode(_timeline);
            _autoToggle.SetValueWithoutNotify(auto);

            int count = _validation.Issues.Count;
            bool stale = _validation.IsStale || !_validation.HasRun;
            _issuesBadge.RemoveFromClassList("tmg-badge--ok");
            _issuesBadge.RemoveFromClassList("tmg-badge--warn");
            _issuesBadge.RemoveFromClassList("tmg-badge--stale");

            if (!_validation.HasRun)
            {
                _issuesBadge.text = "не проверено";
                _issuesBadge.AddToClassList("tmg-badge--stale");
            }
            else if (count == 0)
            {
                _issuesBadge.text = stale ? "OK · не проверено" : "OK";
                _issuesBadge.AddToClassList(stale ? "tmg-badge--stale" : "tmg-badge--ok");
            }
            else
            {
                _issuesBadge.text = $"⚠ {count}" + (stale ? " · не проверено" : "");
                _issuesBadge.AddToClassList(stale ? "tmg-badge--stale" : "tmg-badge--warn");
            }
            _issuesBadge.tooltip = count > 0 ? "Перейти к первому нарушению" : "Нарушений нет";
        }

        void FocusFirstIssue()
        {
            if (_timeline == null) return;
            var first = _validation.Issues
                .Where(i => i.ItemId != null && _timeline.TryFindItem(i.ItemId, out _, out _))
                .Select(i => { _timeline.TryFindItem(i.ItemId, out var item, out _); return item; })
                .OrderBy(i => i.Start)
                .FirstOrDefault();
            if (first == null)
            {
                _selection.Clear();
                return;
            }
            _timelineView.FocusItem(first.Id);
            SetTime(first.Start);
        }

        // ---------- Analysis ----------

        void RefreshSlice()
        {
            if (_timeline == null) return;
            float? timeB = _analysis.AbEnabled ? Mathf.Clamp(_analysis.TimeB, 0f, _timeline.Axis.Length) : null;
            _slice.Refresh(_timeline, _time, timeB);
        }

        void OnAnalysisChanged()
        {
            if (_timelineView == null) return;
            _abToggle?.SetValueWithoutNotify(_analysis.AbEnabled);
            RecomputeChanges();
            _timelineView.RefreshAnalysis();
            RefreshSlice();
            _inspectorDirty = true;
        }

        void RecomputeChanges()
        {
            var baseline = _analysis.Baseline;
            _changes = baseline != null && _timeline != null && baseline != _timeline
                ? TimelineDiff.Compare(baseline, _timeline)
                : System.Array.Empty<TimelineChange>();
            _timelineView?.SetGhosts(_changes);
        }

        void BuildToolsMenu(ToolbarMenu menu)
        {
            var m = menu.menu;
            DropdownMenuAction.Status Enabled(DropdownMenuAction _) =>
                _timeline != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;

            m.AppendAction("Сейвы по контрольным точкам", _ => CreateCheckpointSaves(), Enabled);
            m.AppendSeparator();
            m.AppendAction("Таблица/Экспорт CSV…", _ => ExportCsv(), Enabled);
            m.AppendAction("Таблица/Импорт CSV…", _ => ImportCsv(), Enabled);
            m.AppendAction("Таблица/Экспорт JSON…", _ => ExportJson(), Enabled);
            m.AppendAction("Таблица/Импорт JSON…", _ => ImportJson(), Enabled);
            m.AppendAction("Версии/Сохранить версию", _ =>
            {
                var version = TimelineVersions.Save(_timeline);
                ShowNotification(new GUIContent($"Версия сохранена: {version.name}"));
            }, Enabled);
            m.AppendAction("Версии/Сравнить с последней", _ =>
            {
                var versions = TimelineVersions.List(_timeline);
                if (versions.Count == 0) ShowNotification(new GUIContent("Сохранённых версий нет"));
                else _analysis.Baseline = versions[0];
            }, Enabled);
            m.AppendAction("Версии/Сбросить сравнение", _ => _analysis.Baseline = null,
                _ => _analysis.Baseline != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            m.AppendAction("Телеметрия/Импорт CSV · JSON…", _ =>
            {
                var data = TelemetryImporter.ImportInteractive(_timeline);
                if (data != null) _analysis.Telemetry = data;
            }, Enabled);
            m.AppendAction("Телеметрия/Показывать на таймлайне", _ => _analysis.ShowTelemetry = !_analysis.ShowTelemetry,
                _ => _analysis.Telemetry == null ? DropdownMenuAction.Status.Disabled
                    : _analysis.ShowTelemetry ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            m.AppendAction("Телеметрия/Отключить", _ => _analysis.Telemetry = null,
                _ => _analysis.Telemetry != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        }

        void CreateCheckpointSaves()
        {
            try
            {
                string folder = SaveService.CreateCheckpointSaves(_timeline);
                if (folder == null)
                {
                    ShowNotification(new GUIContent("Нет контрольных точек: нужны акты на треке «Сюжет» или трек «Контрольные точки»"));
                    return;
                }
                EditorUtility.RevealInFinder(System.IO.Path.Combine(folder, "manifest.json"));
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                ShowNotification(new GUIContent("Не удалось создать сейвы — см. Console"));
            }
        }

        void ExportCsv()
        {
            string path = EditorUtility.SaveFilePanel("Экспорт CSV", "", _timeline.name + ".csv", "csv");
            if (string.IsNullOrEmpty(path)) return;
            // UTF-8 with BOM so that Excel opens Cyrillic correctly; Google Sheets ignores the BOM.
            System.IO.File.WriteAllText(path, TimelineCsv.Export(_timeline), new System.Text.UTF8Encoding(true));
            ShowNotification(new GUIContent($"Экспортировано: {System.IO.Path.GetFileName(path)}"));
        }

        void ImportCsv()
        {
            string path = EditorUtility.OpenFilePanel("Импорт CSV", "", "csv");
            if (string.IsNullOrEmpty(path)) return;
            var report = TimelineCsv.Import(_timeline, System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8));
            OnTimelineEdited();
            if (report.Errors.Count > 0) Debug.LogWarning($"[TimeMapGameplay] Импорт CSV: {report}", _timeline);
            else Debug.Log($"[TimeMapGameplay] Импорт CSV: {report}", _timeline);
            EditorUtility.DisplayDialog("Импорт CSV", report.ToString(), "OK");
        }

        void ExportJson()
        {
            string path = EditorUtility.SaveFilePanel("Экспорт JSON", "", _timeline.name + ".json", "json");
            if (string.IsNullOrEmpty(path)) return;
            System.IO.File.WriteAllText(path, TimelineJson.Export(_timeline), new System.Text.UTF8Encoding(false));
            ShowNotification(new GUIContent($"Экспортировано: {System.IO.Path.GetFileName(path)}"));
        }

        void ImportJson()
        {
            string path = EditorUtility.OpenFilePanel("Импорт JSON", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            if (!EditorUtility.DisplayDialog("Импорт JSON", $"Заменить содержимое «{_timeline.name}» данными из файла? Отменить можно через Undo.",
                    "Заменить", "Отмена"))
                return;
            TimelineJson.Import(_timeline, System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8));
            ApplyTimeline();
        }

        // ---------- Saves ----------

        void CreateSave()
        {
            if (_timeline == null) return;
            try
            {
                string path = SaveService.CreateSave(_timeline, _time);
                ShowNotification(new GUIContent($"Сейв создан: {System.IO.Path.GetFileName(path)}"));
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                ShowNotification(new GUIContent("Не удалось создать сейв — см. Console"));
            }
        }

        void PlayFromTime()
        {
            if (_timeline == null || EditorApplication.isPlayingOrWillChangePlaymode) return;

            if (TimeMapGameplaySettings.instance.ConfirmPlayWithIssues)
            {
                if (_validation.IsStale || !_validation.HasRun) _validation.Run(_timeline, logToConsole: false);
                int count = _validation.Issues.Count;
                if (count > 0 && !EditorUtility.DisplayDialog("Play Mode с t",
                        $"Проверка нашла нарушений: {count}. Всё равно запустить с {TimeMath.Format(_time, _timeline.Axis.Units)}?",
                        "Запустить", "Отмена"))
                    return;
            }

            StopPlayback();
            try
            {
                SaveService.PlayFromTime(_timeline, _time);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                ShowNotification(new GUIContent("Не удалось подготовить сейв — см. Console"));
            }
        }

        void SetTime(float time)
        {
            if (_timeline == null) return;
            _time = Mathf.Clamp(time, 0f, _timeline.Axis.Length);
            _timelineView.SetTime(_time);
            RefreshSlice();
            UpdateTimeLabel();
        }

        void OnSelectionChanged()
        {
            _timelineView?.SyncSelection();
            _inspectorDirty = true;
        }

        void OnTimelineEdited()
        {
            if (_analysis.Baseline != null) _changesPending = true;
            _validationPending = true;
            // Structural edits (delete, reorder) invalidate selected ids and inspector property paths,
            // so the inspector is rebuilt right away rather than on the next coalesced tick.
            bool pruned = _selection.Prune(_timeline);
            bool rebuilt = _timelineView.Refresh();
            _timelineView.SyncSelection();
            if (pruned || rebuilt)
            {
                _inspector.Show(_timeline, _time);
                _inspectorDirty = false;
            }
            else
            {
                _inspector.RefreshValues();
            }
            RefreshSlice();
            UpdateToolbar();
        }

        void OnExternalChange()
        {
            // Changes made in inspector fields, the regular Inspector or by scripts; our gestures refresh directly.
            if (_timeline == null || _editing.InGesture) return;
            _time = Mathf.Clamp(_time, 0f, _timeline.Axis.Length);
            OnTimelineEdited();
            SetTime(_time);
        }

        void OnUndoRedo()
        {
            if (_timelineView == null) return;
            ApplyTimeline();
        }

        void UpdateToolbar()
        {
            if (_timeline == null) return;
            var axis = _timeline.Axis;
            _snapLabel.text = axis.Units == AxisUnit.Hours
                ? $"Snap {Mathf.RoundToInt(axis.SnapStep * 60f)} мин"
                : $"Snap {TimeMath.Format(axis.SnapStep, axis.Units)}";
            UpdateTimeLabel();
        }

        void UpdateTimeLabel()
        {
            if (_timeline == null) return;
            var axis = _timeline.Axis;
            _timeLabel.text = $"{TimeMath.Format(_time, axis.Units)} / {TimeMath.Format(axis.Length, axis.Units)}";
            _playModeButton.text = $"▶ Play Mode с {TimeMath.Format(_time, axis.Units)}";
            _saveButton.tooltip = $"Сейв в момент {TimeMath.Format(_time, axis.Units)} — формат: {SaveBuilders.Active().DisplayName}. Настройки: Project Settings → TimeMapGameplay.";
        }

        // ---------- Playback ----------

        void TogglePlayback()
        {
            if (_playing) StopPlayback();
            else StartPlayback();
        }

        void StartPlayback()
        {
            if (_timeline == null) return;
            if (_time >= _timeline.Axis.Length) SetTime(0f);
            _playing = true;
            _lastTick = EditorApplication.timeSinceStartup;
            _playback.Resume();
            _playButton.text = "Pause";
        }

        void StopPlayback()
        {
            _playing = false;
            _playback?.Pause();
            if (_playButton != null) _playButton.text = "Play";
        }

        void TickPlayback()
        {
            if (!_playing || _timeline == null) return;
            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - _lastTick);
            _lastTick = now;

            SetTime(_time + dt * PlaybackUnitsPerSecond);
            if (_time >= _timeline.Axis.Length) StopPlayback();
        }

        // ---------- Keyboard & commands ----------

        void OnKeyDown(KeyDownEvent evt)
        {
            if (_timeline == null || evt.keyCode == KeyCode.None || IsFieldFocused()) return;

            float step = _timeline.Axis.SnapStep;
            switch (evt.keyCode)
            {
                case KeyCode.Space:
                    TogglePlayback();
                    break;
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                    int dir = evt.keyCode == KeyCode.RightArrow ? 1 : -1;
                    if (evt.altKey && _selection.Count > 0)
                        _operations.NudgeItems(_selection.Items, dir * step);
                    else
                        SetTime(TimeMath.Snap(_time, step) + dir * step);
                    break;
                case KeyCode.Home:
                    SetTime(0f);
                    break;
                case KeyCode.End:
                    SetTime(_timeline.Axis.Length);
                    break;
                case KeyCode.Escape:
                    _selection.Clear();
                    break;
                case KeyCode.B when !evt.actionKey:
                    _analysis.TimeB = _time;
                    _analysis.AbEnabled = true;
                    break;
                case KeyCode.Delete:
                case KeyCode.Backspace when evt.actionKey:
                    _operations.DeleteItems(_selection.Items.ToList());
                    break;
                default:
                    return;
            }
            evt.StopPropagation();
        }

        static readonly HashSet<string> Commands = new()
        {
            "Copy", "Cut", "Paste", "Duplicate", "Delete", "SoftDelete", "SelectAll"
        };

        void OnValidateCommand(ValidateCommandEvent evt)
        {
            if (_timeline == null || IsFieldFocused() || !Commands.Contains(evt.commandName)) return;
            evt.StopPropagation();
        }

        void OnExecuteCommand(ExecuteCommandEvent evt)
        {
            if (_timeline == null || IsFieldFocused() || !Commands.Contains(evt.commandName)) return;

            var ids = _selection.Items.ToList();
            switch (evt.commandName)
            {
                case "Copy":
                    _operations.Copy(ids);
                    break;
                case "Cut":
                    _operations.Copy(ids);
                    _operations.DeleteItems(ids);
                    break;
                case "Paste":
                    _selection.Set(_operations.Paste(_time));
                    break;
                case "Duplicate":
                    _selection.Set(_operations.Duplicate(ids));
                    break;
                case "Delete":
                case "SoftDelete":
                    _operations.DeleteItems(ids);
                    break;
                case "SelectAll":
                    _selection.Set(_timeline.Tracks.Where(t => t != null).SelectMany(t => t.Items).Where(i => i != null).Select(i => i.Id));
                    break;
            }
            evt.StopPropagation();
        }

        bool IsFieldFocused()
            => rootVisualElement.focusController?.focusedElement is VisualElement focused && IsInputElement(focused);

        bool IsInputElement(VisualElement element)
            => IsSelfOrDescendant(_inspectorHost, element)
               || IsSelfOrDescendant(_assetField, element)
               || IsSelfOrDescendant(_zoomSlider, element);

        static bool IsSelfOrDescendant(VisualElement parent, VisualElement element)
            => parent != null && (parent == element || parent.Contains(element));
    }
}
