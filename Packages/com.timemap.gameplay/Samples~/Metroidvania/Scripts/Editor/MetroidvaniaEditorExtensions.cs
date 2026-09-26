using System.Linq;
using TimeMapGameplay.Editor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimeMapGameplay.Samples.Metroidvania.Editor
{
    /// <summary>Example track drawer: boss markers show their hit points next to the name.</summary>
    [CustomTrackDrawer(typeof(BossTrack))]
    public sealed class BossTrackDrawer : ItemTrackDrawer
    {
        protected override void UpdateItem(ItemElement element, TrackDrawerContext context)
            => element.SetTag(element.Item is BossMarker boss ? $"{boss.HitPoints} HP" : null);
    }

    /// <summary>
    /// Example slice card: defeated and upcoming bosses. Hidden for timelines without a boss track,
    /// so importing the sample does not clutter other timelines.
    /// </summary>
    [SlicePanel("Metroidvania.Bosses", 20, Column = 2)]
    public sealed class BossesPanel : SlicePanelBase
    {
        public BossesPanel() : base("Боссы")
        {
        }

        public override void Refresh(ProgressionState state)
        {
            Body.Clear();
            var bosses = state.FirstOf<BossTrack, MarkerTrackState>();
            Root.style.display = bosses == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (bosses == null) return;

            SetHeader(bosses.Track.Color, $"{bosses.Reached.Count}/{bosses.Total}");
            foreach (var boss in bosses.Reached.OfType<BossMarker>())
                Body.Add(KeyValue($"✓ {boss.Name}", string.IsNullOrEmpty(boss.Reward) ? "" : boss.Reward));

            if (bosses.Next is BossMarker next)
            {
                var label = AddLabel(Body, $"Далее: {next.Name} в {state.Format(next.Start)} ({next.HitPoints} HP)", "tmg-card__footnote");
                label.style.marginTop = 4f;
            }
        }
    }
}
