using System.Collections.Generic;
using System.Linq;
using TimeMapGameplay.Editor;

namespace TimeMapGameplay.Samples.RPG.Editor
{
    /// <summary>
    /// Example of a project validation rule: acts that are bound to something must be bound to a <see cref="QuestAsset"/>.
    /// Discovered automatically — no registration needed.
    /// </summary>
    public sealed class ActQuestBindingRule : IValidationRule
    {
        public string Id => "sample.rpg.act-quest";

        public IEnumerable<ValidationIssue> Validate(ProgressionTimeline timeline)
        {
            foreach (var plot in timeline.Tracks.OfType<PlotTrack>())
            foreach (var act in plot.Clips.Where(c => c != null && c.Binding != null))
            {
                if (act.Binding is not QuestAsset)
                    yield return new ValidationIssue(Id, IssueSeverity.Warning,
                        $"«{act.Name}»: акт привязан к {act.Binding.GetType().Name}, ожидается QuestAsset.", act.Id, plot.Id);
            }
        }
    }
}
