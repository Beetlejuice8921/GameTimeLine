using UnityEngine;

namespace TimeMapGameplay.Samples.RPG
{
    /// <summary>
    /// Stand-in for a project's quest asset. Acts on the timeline are bound to these.
    /// Field names follow the preview convention (icon / title / description), so the timeline
    /// shows them in the inspector and the act card without any adapter code.
    /// </summary>
    [CreateAssetMenu(menuName = "TimeMapGameplay/Samples/RPG Quest", fileName = "Quest")]
    public sealed class QuestAsset : ScriptableObject
    {
        public string questId;
        public string title;
        [TextArea(2, 6)] public string description;
        public Sprite icon;
    }
}
