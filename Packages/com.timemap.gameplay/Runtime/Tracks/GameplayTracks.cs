using System;
using UnityEngine;

namespace TimeMapGameplay
{
    // Built-in MVP tracks. They describe design concepts only; project systems plug in through adapters.

    /// <summary>A world event. Placed where the player learns it; <see cref="ChronoIndex"/> is its place in world history.</summary>
    [Serializable]
    public class FabulaEvent : ClipBase
    {
        [SerializeField, Tooltip("Position of the event in world chronology (1 = earliest).")]
        int _chronoIndex = 1;
        [SerializeField, TextArea(2, 5)] string _text = "";

        public int ChronoIndex { get => _chronoIndex; set => _chronoIndex = value; }
        public string Text { get => _text; set => _text = value; }
    }

    [Serializable, TrackMenu("Фабула")]
    public class FabulaTrack : ClipTrack
    {
        public override Type ItemType => typeof(FabulaEvent);
    }

    /// <summary>An act, chapter or quest in presentation order.</summary>
    [Serializable]
    public class PlotClip : ClipBase
    {
        [SerializeField, TextArea(2, 6)] string _synopsis = "";
        [SerializeField] Texture2D _art;
        [SerializeField, Tooltip("Fallback art colour when no texture is set.")]
        Color _tint = new(0.25f, 0.35f, 0.5f);

        public string Synopsis { get => _synopsis; set => _synopsis = value; }
        public Texture2D Art { get => _art; set => _art = value; }
        public Color Tint { get => _tint; set => _tint = value; }
    }

    [Serializable, TrackMenu("Сюжет")]
    public class PlotTrack : ClipTrack
    {
        public override Type ItemType => typeof(PlotClip);
    }

    /// <summary>A skill unlock.</summary>
    [Serializable]
    public class SkillMarker : MarkerBase, ILevelGated
    {
        [SerializeField, Min(0)] int _minLevel;

        public int MinLevel { get => _minLevel; set => _minLevel = value; }
    }

    [Serializable, TrackMenu("Прокачка")]
    public class SkillTrack : MarkerTrack
    {
        public override Type ItemType => typeof(SkillMarker);
    }

    /// <summary>A location opening.</summary>
    [Serializable]
    public class LocationMarker : MarkerBase
    {
        [SerializeField, Tooltip("Position on the mini-map, normalised 0..1 (0,0 = bottom-left).")]
        Vector2 _mapPosition = new(0.5f, 0.5f);

        public Vector2 MapPosition { get => _mapPosition; set => _mapPosition = value; }
    }

    [Serializable, TrackMenu("Локации")]
    public class LocationTrack : MarkerTrack
    {
        [SerializeField, Tooltip("Optional mini-map background.")]
        Texture2D _mapBackground;

        public Texture2D MapBackground { get => _mapBackground; set => _mapBackground = value; }
        public override Type ItemType => typeof(LocationMarker);
    }

    /// <summary>An item or upgrade. The latest reached upgrade per slot is considered equipped.</summary>
    [Serializable]
    public class UpgradeMarker : MarkerBase, ILevelGated
    {
        [SerializeField, Tooltip("Equipment slot, e.g. Weapon, Armor.")]
        string _slot = "";
        [SerializeField, Min(0)] int _minLevel;
        [SerializeField] int _attackBonus;
        [SerializeField] int _healthBonus;

        public string Slot { get => _slot; set => _slot = value; }
        public int MinLevel { get => _minLevel; set => _minLevel = value; }
        public int AttackBonus { get => _attackBonus; set => _attackBonus = value; }
        public int HealthBonus { get => _healthBonus; set => _healthBonus = value; }
    }

    [Serializable, TrackMenu("Апгрейды")]
    public class UpgradeTrack : MarkerTrack
    {
        public override Type ItemType => typeof(UpgradeMarker);
    }
}
