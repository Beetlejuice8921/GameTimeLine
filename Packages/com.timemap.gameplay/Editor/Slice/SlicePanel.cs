using UnityEngine;
using UnityEngine.UIElements;

namespace TimeMapGameplay.Editor
{
    /// <summary>
    /// Registers an <see cref="ISlicePanel"/> in the game slice. The class needs a public parameterless constructor.
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false)]
    public sealed class SlicePanelAttribute : System.Attribute
    {
        public SlicePanelAttribute(string id, int order = 100)
        {
            Id = id;
            Order = order;
        }

        public string Id { get; }

        /// <summary>Order inside the column (smaller is higher).</summary>
        public int Order { get; }

        /// <summary>Column 0 (wide), 1 or 2; -1 puts the panel into the shortest of columns 1–2.</summary>
        public int Column { get; set; } = -1;

        /// <summary>Id of a panel this one replaces (e.g. "Hero" to swap the built-in hero card).</summary>
        public string Replaces { get; set; }
    }

    /// <summary>
    /// A card of the game slice. Built once, then refreshed with the state at the playhead.
    /// Mark implementations with <see cref="SlicePanelAttribute"/> to have them discovered.
    /// </summary>
    public interface ISlicePanel
    {
        /// <summary>Card root, created once.</summary>
        VisualElement Root { get; }

        /// <summary>Updates the card for <paramref name="state"/>. Called on every playhead or data change.</summary>
        void Refresh(ProgressionState state);
    }

    /// <summary>Card chrome shared by built-in panels: coloured title, meta text on the right, body.</summary>
    public abstract class SlicePanelBase : ISlicePanel
    {
        readonly VisualElement _dot;
        readonly Label _meta;

        protected SlicePanelBase(string title)
        {
            Root = new VisualElement();
            Root.AddToClassList("tmg-card");

            var header = new VisualElement();
            header.AddToClassList("tmg-card__header");
            _dot = new VisualElement();
            _dot.AddToClassList("tmg-card__dot");
            header.Add(_dot);
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("tmg-card__title");
            header.Add(titleLabel);
            _meta = new Label();
            _meta.AddToClassList("tmg-card__meta");
            header.Add(_meta);
            Root.Add(header);

            Body = new VisualElement();
            Body.AddToClassList("tmg-card__body");
            Root.Add(Body);
        }

        public VisualElement Root { get; }
        protected VisualElement Body { get; }

        public abstract void Refresh(ProgressionState state);

        protected void SetHeader(Color color, string meta)
        {
            _dot.style.backgroundColor = color;
            _meta.text = meta;
        }

        protected static Label AddLabel(VisualElement parent, string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            parent.Add(label);
            return label;
        }

        protected static VisualElement Bar(Color color, float fill)
        {
            var bar = new VisualElement();
            bar.AddToClassList("tmg-bar");
            var inner = new VisualElement();
            inner.AddToClassList("tmg-bar__fill");
            inner.style.backgroundColor = color;
            inner.style.width = Length.Percent(Mathf.Clamp01(fill) * 100f);
            bar.Add(inner);
            return bar;
        }

        protected static VisualElement KeyValue(string key, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("tmg-kv");
            AddLabel(row, key, "tmg-kv__key");
            AddLabel(row, value, "tmg-kv__value");
            return row;
        }

        protected static Label Missing(string trackName)
        {
            var label = new Label($"Нет трека «{trackName}». Добавьте его через «+ Трек».");
            label.AddToClassList("tmg-card__empty");
            return label;
        }
    }
}
