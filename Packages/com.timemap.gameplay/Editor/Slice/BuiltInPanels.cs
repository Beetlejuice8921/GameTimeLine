using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimeMapGameplay.Editor
{
    /// <summary>Current act: art, title, synopsis and progress through the act.</summary>
    [SlicePanel("Act", 0, Column = 0)]
    public sealed class ActPanel : SlicePanelBase
    {
        readonly ActArt _art;
        readonly Label _number;
        readonly Label _title;
        readonly VisualElement _text;

        public ActPanel() : base("Акт")
        {
            Root.AddToClassList("tmg-card--act");

            _art = new ActArt();
            _number = AddLabel(_art, "", "tmg-act__number");
            _title = AddLabel(_art, "", "tmg-act__title");
            Body.Add(_art);

            _text = new VisualElement();
            _text.AddToClassList("tmg-act__text");
            Body.Add(_text);
        }

        public override void Refresh(ProgressionState state)
        {
            _text.Clear();
            var plot = state.FirstOf<PlotTrack, ClipTrackState>();
            if (plot == null)
            {
                SetHeader(Color.gray, "");
                _art.style.display = DisplayStyle.None;
                _text.Add(Missing("Сюжет"));
                return;
            }

            _art.style.display = DisplayStyle.Flex;
            var act = plot.Current as PlotClip;
            var ordered = ((PlotTrack)plot.Track).Clips.Where(c => c != null).OrderBy(c => c.Start).ToList();
            int number = act != null ? ordered.IndexOf(act) + 1 : 0;
            SetHeader(plot.Track.Color, act != null ? $"{number} из {ordered.Count}" : $"— из {ordered.Count}");

            if (act == null)
            {
                _art.Show(null, plot.Track.Color, null);
                _number.text = "";
                _title.text = plot.Started.Count == ordered.Count && ordered.Count > 0 ? "Сюжет завершён" : "Между актами";
                return;
            }

            // Art and text come from the clip first, then from the bound project asset (IPreviewSource).
            var preview = PreviewSources.Get(act.Binding);
            _art.Show(act, act.Tint, act.Art != null ? act.Art : preview?.Image as Texture2D);
            _number.text = $"АКТ {number}";
            _title.text = act.Name;

            string synopsis = !string.IsNullOrEmpty(act.Synopsis) ? act.Synopsis : preview?.Description;
            if (!string.IsNullOrEmpty(synopsis))
                AddLabel(_text, synopsis, "tmg-act__synopsis");
            float progress = plot.CurrentProgress;
            _text.Add(KeyValue("Пройдено акта", $"{Mathf.RoundToInt(progress * 100f)}%"));
            _text.Add(Bar(plot.Track.Color, progress));
            _text.Add(KeyValue("До конца акта", state.Format(act.End - state.Time)));
        }

        /// <summary>Act art: the clip texture, or a procedural landscape tinted with the clip colour.</summary>
        sealed class ActArt : VisualElement
        {
            Color _tint;
            int _seed;
            bool _procedural;

            public ActArt()
            {
                AddToClassList("tmg-act__art");
                generateVisualContent += Draw;
            }

            public void Show(PlotClip clip, Color tint, Texture2D image)
            {
                _tint = tint;
                _seed = clip != null ? clip.Id.GetHashCode() : 0;
                _procedural = image == null;
                style.backgroundImage = _procedural ? StyleKeyword.None : new StyleBackground(image);
                style.backgroundColor = Color.Lerp(Color.black, tint, 0.35f);
                MarkDirtyRepaint();
            }

            void Draw(MeshGenerationContext mgc)
            {
                if (!_procedural) return;
                var rect = contentRect;
                if (rect.width <= 0f) return;

                var painter = mgc.painter2D;
                var random = new System.Random(_seed);
                for (int layer = 0; layer < 3; layer++)
                {
                    float baseY = rect.height * (0.45f + layer * 0.15f);
                    painter.fillColor = Color.Lerp(_tint, Color.black, 0.25f + layer * 0.22f);
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(0f, rect.height));
                    const int peaks = 7;
                    for (int i = 0; i <= peaks; i++)
                    {
                        float x = rect.width * i / peaks;
                        float y = baseY - (float)random.NextDouble() * rect.height * 0.22f;
                        painter.LineTo(new Vector2(x, y));
                    }
                    painter.LineTo(new Vector2(rect.width, rect.height));
                    painter.ClosePath();
                    painter.Fill();
                }
            }
        }
    }

    /// <summary>Hero: level from the level curve, stats from equipped upgrades, unlocked skills.</summary>
    [SlicePanel("Hero", 0, Column = 1)]
    public sealed class HeroPanel : SlicePanelBase
    {
        public HeroPanel() : base("Герой")
        {
        }

        public override void Refresh(ProgressionState state)
        {
            Body.Clear();
            var levelState = state.FirstOf<LevelCurveTrack, CurveTrackState>();
            if (levelState == null)
            {
                SetHeader(Color.gray, "");
                Body.Add(Missing("Прогресс (уровень)"));
            }
            else
            {
                float level = levelState.Value;
                int whole = Mathf.FloorToInt(level + 0.0001f);
                SetHeader(levelState.Track.Color, $"t {state.Format(state.Time)}");

                var row = new VisualElement();
                row.AddToClassList("tmg-hero__level");
                var number = AddLabel(row, whole.ToString(), "tmg-hero__number");
                number.style.color = levelState.Track.Color;
                AddLabel(row, "уровень", "tmg-muted");
                Body.Add(row);
                Body.Add(Bar(levelState.Track.Color, level - whole));
            }

            var equipped = EquipmentPanel.Equipped(state);
            Body.Add(KeyValue("Атака", $"+{equipped.Sum(u => u.AttackBonus)}"));
            Body.Add(KeyValue("Здоровье", $"+{equipped.Sum(u => u.HealthBonus)}"));

            var skills = state.FirstOf<SkillTrack, MarkerTrackState>();
            if (skills == null) return;

            var chips = new VisualElement();
            chips.AddToClassList("tmg-chips");
            foreach (var skill in skills.Reached)
                AddLabel(chips, skill.Name, "tmg-chip");
            foreach (var skill in skills.Upcoming)
                AddLabel(chips, skill.Name, "tmg-chip").AddToClassList("tmg-chip--off");
            Body.Add(chips);
        }
    }

    /// <summary>Mini-map: opened locations in order of opening, the latest highlighted.</summary>
    [SlicePanel("Map", 0, Column = 2)]
    public sealed class MapPanel : SlicePanelBase
    {
        readonly MapView _map = new();

        public MapPanel() : base("Карта мира")
        {
            Body.Add(_map);
        }

        public override void Refresh(ProgressionState state)
        {
            var locations = state.FirstOf<LocationTrack, MarkerTrackState>();
            Body.Clear();
            if (locations == null)
            {
                SetHeader(Color.gray, "");
                Body.Add(Missing("Локации"));
                return;
            }
            Body.Add(_map);
            SetHeader(locations.Track.Color, $"{locations.Reached.Count}/{locations.Total}");
            _map.Show(locations);
        }

        sealed class MapView : VisualElement
        {
            MarkerTrackState _state;
            readonly List<Label> _labels = new();

            public MapView()
            {
                AddToClassList("tmg-map");
                generateVisualContent += Draw;
                RegisterCallback<GeometryChangedEvent>(_ => LayoutLabels());
            }

            public void Show(MarkerTrackState state)
            {
                _state = state;
                var track = (LocationTrack)state.Track;
                style.backgroundImage = track.MapBackground != null ? new StyleBackground(track.MapBackground) : StyleKeyword.None;

                foreach (var label in _labels) label.RemoveFromHierarchy();
                _labels.Clear();
                foreach (var marker in state.Reached)
                {
                    var label = new Label(marker.Name) { pickingMode = PickingMode.Ignore, userData = marker };
                    label.AddToClassList("tmg-map__label");
                    if (marker == state.Latest) label.AddToClassList("tmg-map__label--latest");
                    _labels.Add(label);
                    Add(label);
                }
                LayoutLabels();
                MarkDirtyRepaint();
            }

            Vector2 ToLocal(MarkerBase marker)
            {
                var p = marker is LocationMarker location ? location.MapPosition : new Vector2(0.5f, 0.5f);
                var rect = contentRect;
                const float pad = 12f;
                return new Vector2(pad + p.x * (rect.width - pad * 2f), rect.height - pad - p.y * (rect.height - pad * 2f));
            }

            void LayoutLabels()
            {
                foreach (var label in _labels)
                {
                    var p = ToLocal((MarkerBase)label.userData);
                    label.style.left = p.x + 7f;
                    label.style.top = p.y - 8f;
                }
            }

            void Draw(MeshGenerationContext mgc)
            {
                if (_state == null || contentRect.width <= 0f) return;
                var painter = mgc.painter2D;
                var color = _state.Track.Color;
                var all = _state.Reached.Concat(_state.Upcoming).ToList();

                painter.lineWidth = 1.5f;
                for (int i = 1; i < all.Count; i++)
                {
                    bool opened = i < _state.Reached.Count;
                    var dim = EditorTheme.MapDim;
                    painter.strokeColor = opened ? color : new Color(dim.r, dim.g, dim.b, 0.35f);
                    painter.BeginPath();
                    painter.MoveTo(ToLocal(all[i - 1]));
                    painter.LineTo(ToLocal(all[i]));
                    painter.Stroke();
                }

                for (int i = 0; i < all.Count; i++)
                {
                    bool opened = i < _state.Reached.Count;
                    var center = ToLocal(all[i]);
                    if (all[i] == _state.Latest)
                    {
                        painter.strokeColor = EditorTheme.MapLatestRing;
                        painter.lineWidth = 1.5f;
                        painter.BeginPath();
                        painter.Arc(center, 8f, Angle.Degrees(0f), Angle.Degrees(360f));
                        painter.Stroke();
                    }
                    painter.fillColor = opened ? color : EditorTheme.MapDim;
                    painter.BeginPath();
                    painter.Arc(center, opened ? 4.5f : 3f, Angle.Degrees(0f), Angle.Degrees(360f));
                    painter.Fill();
                }
            }
        }
    }

    /// <summary>Fabula: what the player knows, in world chronological order. Unrevealed events stay hidden.</summary>
    [SlicePanel("Fabula", 10, Column = 1)]
    public sealed class FabulaPanel : SlicePanelBase
    {
        public FabulaPanel() : base("Фабула")
        {
        }

        public override void Refresh(ProgressionState state)
        {
            Body.Clear();
            var fabula = state.FirstOf<FabulaTrack, ClipTrackState>();
            if (fabula == null)
            {
                SetHeader(Color.gray, "");
                Body.Add(Missing("Фабула"));
                return;
            }

            var track = (FabulaTrack)fabula.Track;
            var events = track.Clips.OfType<FabulaEvent>().OrderBy(e => e.ChronoIndex).ThenBy(e => e.Start).ToList();
            var revealed = new HashSet<ClipBase>(fabula.Started);
            var newest = fabula.Started.Count > 0 ? fabula.Started[^1] : null;
            SetHeader(track.Color, $"раскрыто {revealed.Count}/{events.Count}");

            foreach (var e in events)
            {
                bool known = revealed.Contains(e);
                var row = new VisualElement();
                row.AddToClassList("tmg-fabula__row");
                AddLabel(row, $"#{e.ChronoIndex}", "tmg-fabula__index");

                var texts = new VisualElement();
                texts.AddToClassList("tmg-fabula__texts");
                var name = AddLabel(texts, known ? e.Name : "• • • • •", "tmg-fabula__name");
                if (known && !string.IsNullOrEmpty(e.Text))
                    AddLabel(texts, e.Text, "tmg-fabula__text");
                row.Add(texts);

                AddLabel(row, known ? state.Format(e.Start) : "", "tmg-fabula__meta");

                if (!known) row.AddToClassList("tmg-fabula__row--hidden");
                if (e == newest)
                {
                    row.AddToClassList("tmg-fabula__row--new");
                    name.AddToClassList("tmg-fabula__name--new");
                }
                Body.Add(row);
            }
        }
    }

    /// <summary>Equipment: latest reached upgrade per slot, plus the next upcoming one.</summary>
    [SlicePanel("Equipment", 10, Column = 2)]
    public sealed class EquipmentPanel : SlicePanelBase
    {
        public EquipmentPanel() : base("Снаряжение")
        {
        }

        /// <summary>Latest reached upgrade per slot (empty slot = each upgrade is its own slot).</summary>
        public static List<UpgradeMarker> Equipped(ProgressionState state)
        {
            var upgrades = state.FirstOf<UpgradeTrack, MarkerTrackState>();
            if (upgrades == null) return new List<UpgradeMarker>();
            return upgrades.Reached.OfType<UpgradeMarker>()
                .GroupBy(u => string.IsNullOrEmpty(u.Slot) ? u.Id : u.Slot)
                .Select(g => g.Last())
                .ToList();
        }

        public override void Refresh(ProgressionState state)
        {
            Body.Clear();
            var upgrades = state.FirstOf<UpgradeTrack, MarkerTrackState>();
            if (upgrades == null)
            {
                SetHeader(Color.gray, "");
                Body.Add(Missing("Апгрейды"));
                return;
            }

            SetHeader(upgrades.Track.Color, $"{upgrades.Reached.Count}/{upgrades.Total}");
            var equipped = Equipped(state);
            if (equipped.Count == 0)
                AddLabel(Body, "Пока ничего", "tmg-muted");
            foreach (var item in equipped)
            {
                string bonus = string.Join(", ", new[]
                {
                    item.AttackBonus != 0 ? $"атк +{item.AttackBonus}" : null,
                    item.HealthBonus != 0 ? $"hp +{item.HealthBonus}" : null
                }.Where(s => s != null));
                Body.Add(KeyValue(string.IsNullOrEmpty(item.Slot) ? item.Name : $"{item.Slot}: {item.Name}", bonus));
            }

            if (upgrades.Next is UpgradeMarker next)
            {
                var label = AddLabel(Body, $"Далее: {next.Name} в {state.Format(next.Start)} (ур. {next.MinLevel})", "tmg-card__footnote");
                label.style.marginTop = 4f;
            }
        }
    }
}
