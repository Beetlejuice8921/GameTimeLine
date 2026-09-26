using System;
using System.Collections.Generic;
using UnityEngine;

namespace TimeMapGameplay
{
    /// <summary>Base class for all timeline tracks. Projects may derive their own track types.</summary>
    [Serializable]
    public abstract class TrackBase
    {
        [SerializeField, HideInInspector] string _id = Guid.NewGuid().ToString("N");
        [SerializeField] string _name = "Track";
        [SerializeField] string _subtitle = "";
        [SerializeField] Color _color = new Color(0.31f, 0.56f, 0.85f);

        public string Id => _id;
        public string Name { get => _name; set => _name = value; }
        public string Subtitle { get => _subtitle; set => _subtitle = value; }
        public Color Color { get => _color; set => _color = value; }

        /// <summary>All items on this track in storage order. Empty for tracks without items (curves).</summary>
        public abstract IEnumerable<TimelineItem> Items { get; }

        /// <summary>Concrete item type created by <see cref="CreateItem"/>; null when the track has no items.</summary>
        public virtual Type ItemType => null;

        /// <summary>True when <paramref name="item"/> can be placed on this track.</summary>
        public virtual bool CanContain(TimelineItem item) => item != null && ItemType != null && ItemType.IsInstanceOfType(item);

        /// <summary>Creates a new item of <see cref="ItemType"/>, not yet added to the track.</summary>
        public TimelineItem CreateItem() => ItemType == null ? null : (TimelineItem)Activator.CreateInstance(ItemType);

        /// <summary>Adds an item. Callers must check <see cref="CanContain"/> first.</summary>
        public virtual void AddItem(TimelineItem item) => throw new NotSupportedException($"{GetType().Name} has no items.");

        /// <summary>Removes an item; returns false when it is not on this track.</summary>
        public virtual bool RemoveItem(TimelineItem item) => false;

        /// <summary>State of this track at <paramref name="time"/>. Must be a pure function of the track data.</summary>
        public abstract TrackState Evaluate(float time);

        /// <summary>Assigns a fresh unique id (used when duplicating tracks).</summary>
        public void RegenerateId() => _id = Guid.NewGuid().ToString("N");
    }
}
