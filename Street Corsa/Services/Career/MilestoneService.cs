using StreetCorsa.Mods;
using StreetCorsa.Models.Career.Milestones;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Career
{
    /// <summary>
    /// Implementation of milestone management service
    /// </summary>
    public class MilestoneService : IMilestoneService
    {
        // The milestones given, or those of the content in play (read again once another plays)
        private readonly Dictionary<string, MilestoneDefinition>? _given;
        private readonly ContentCache<Dictionary<string, MilestoneDefinition>> _inPlay = new(() => Index(CareerContent.Installed));

        private Dictionary<string, MilestoneDefinition> _milestones => _given ?? _inPlay.Value;

        /// <param name="content">The milestones the mods define; the content in play's when null</param>
        public MilestoneService(CareerContent? content = null)
        {
            if (content != null) _given = Index(content);
        }

        private static Dictionary<string, MilestoneDefinition> Index(CareerContent content) =>
            new(content.Milestones, StringComparer.OrdinalIgnoreCase);

        public IEnumerable<MilestoneDefinition> GetAllMilestones()
        {
            return _milestones.Values;
        }

        public MilestoneDefinition? GetMilestone(string milestoneId)
        {
            return _milestones.TryGetValue(milestoneId, out var milestone) ? milestone : null;
        }

        public MilestoneProgress GetProgress(string milestoneId, CareerState career)
        {
            var milestone = GetMilestone(milestoneId);
            if (milestone == null)
            {
                return new MilestoneProgress
                {
                    Definition = new MilestoneDefinition { Id = milestoneId, Name = GameText.Say("career.milestone.unknown") },
                    CurrentValue = 0,
                    IsCompleted = false
                };
            }

            var currentValue = career.GetCounter(milestone.Trigger);
            var isCompleted = career.CompletedMilestones.Contains(milestoneId);

            return new MilestoneProgress
            {
                Definition = milestone,
                CurrentValue = currentValue,
                IsCompleted = isCompleted
            };
        }

        public IEnumerable<MilestoneProgress> GetAllProgress(CareerState career)
        {
            foreach (var milestone in _milestones.Values)
            {
                yield return GetProgress(milestone.Id, career);
            }
        }

        public List<MilestoneDefinition> CheckForCompletedMilestones(CareerState career)
        {
            var newlyCompleted = new List<MilestoneDefinition>();

            foreach (var milestone in _milestones.Values)
            {
                // Skip already completed milestones
                if (career.CompletedMilestones.Contains(milestone.Id))
                    continue;

                var currentValue = career.GetCounter(milestone.Trigger);

                if (currentValue >= milestone.TargetValue)
                {
                    career.CompletedMilestones.Add(milestone.Id);
                    newlyCompleted.Add(milestone);
                }
            }

            return newlyCompleted;
        }

        public List<string> GetUnlocksForMilestone(string milestoneId)
        {
            var milestone = GetMilestone(milestoneId);
            return milestone?.Unlocks ?? [];
        }

        public bool IsMilestoneCompleted(string milestoneId, CareerState career)
        {
            return career.CompletedMilestones.Contains(milestoneId);
        }
    }
}
