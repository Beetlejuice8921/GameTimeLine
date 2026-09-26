using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TimeMapGameplay.Editor
{
    /// <summary>
    /// Runs validation rules. Auto mode re-checks after every change; manual mode only on <see cref="Run"/>,
    /// and marks results as stale after changes (old marks stay until the next check).
    /// The mode is stored per user in EditorPrefs.
    /// </summary>
    public sealed class ValidationService
    {
        public const string AutoModePrefsKey = "TimeMapGameplay.Validation.Auto";

        /// <summary>Above this many items the default (no saved preference) is manual mode.</summary>
        public const int AutoModeItemLimit = 500;

        readonly string _prefsKey;
        readonly List<IValidationRule> _rules;
        List<ValidationIssue> _issues = new();
        HashSet<string> _previousKeys = new();

        public ValidationService(IEnumerable<IValidationRule> rules = null, string prefsKey = AutoModePrefsKey)
        {
            _prefsKey = prefsKey;
            _rules = (rules ?? DiscoverRules()).ToList();
        }

        /// <summary>Raised after a run or when results become stale.</summary>
        public event Action Changed;

        public IReadOnlyList<ValidationIssue> Issues => _issues;
        public IReadOnlyList<IValidationRule> Rules => _rules;

        /// <summary>True when the timeline changed after the last run in manual mode (or no run happened yet).</summary>
        public bool IsStale { get; private set; } = true;

        public bool HasRun { get; private set; }

        public bool IsAutoMode(ProgressionTimeline timeline)
        {
            if (EditorPrefs.HasKey(_prefsKey)) return EditorPrefs.GetBool(_prefsKey);
            int items = timeline == null ? 0 : timeline.Tracks.Where(t => t != null).Sum(t => t.Items.Count());
            return items <= AutoModeItemLimit;
        }

        public void SetAutoMode(bool auto) => EditorPrefs.SetBool(_prefsKey, auto);

        public void ClearPreference() => EditorPrefs.DeleteKey(_prefsKey);

        /// <summary>Forgets results (e.g. when another timeline is opened).</summary>
        public void Reset()
        {
            _issues = new List<ValidationIssue>();
            _previousKeys = new HashSet<string>();
            IsStale = true;
            HasRun = false;
        }

        /// <summary>Call after any change of the timeline.</summary>
        public void NotifyChanged(ProgressionTimeline timeline)
        {
            if (timeline == null) return;
            if (IsAutoMode(timeline))
            {
                Run(timeline, logToConsole: false);
                return;
            }
            if (IsStale) return;
            IsStale = true;
            Changed?.Invoke();
        }

        /// <summary>
        /// Runs all rules. With <paramref name="logToConsole"/> every issue is logged; otherwise only issues that were
        /// not present in the previous run (so auto mode does not flood the Console).
        /// </summary>
        public IReadOnlyList<ValidationIssue> Run(ProgressionTimeline timeline, bool logToConsole)
        {
            var issues = new List<ValidationIssue>();
            if (timeline != null)
            {
                foreach (var rule in _rules)
                {
                    try
                    {
                        issues.AddRange(rule.Validate(timeline).Where(i => i != null));
                    }
                    catch (Exception e)
                    {
                        issues.Add(new ValidationIssue(rule.Id, IssueSeverity.Error, $"Правило {rule.GetType().Name} упало: {e.Message}"));
                        Debug.LogException(e);
                    }
                }
            }

            var keys = new HashSet<string>(issues.Select(i => i.Key));
            var toLog = logToConsole ? issues : issues.Where(i => !_previousKeys.Contains(i.Key)).ToList();
            if (timeline != null) Log(timeline, toLog, issues.Count, logToConsole);

            _issues = issues;
            _previousKeys = keys;
            IsStale = false;
            HasRun = true;
            Changed?.Invoke();
            return _issues;
        }

        public IEnumerable<ValidationIssue> IssuesFor(string itemId) => _issues.Where(i => i.ItemId == itemId);

        public IEnumerable<ValidationIssue> IssuesForTrack(string trackId) => _issues.Where(i => i.TrackId == trackId);

        /// <summary>Issue count per item id.</summary>
        public Dictionary<string, List<ValidationIssue>> IssuesByItem()
            => _issues.Where(i => i.ItemId != null).GroupBy(i => i.ItemId).ToDictionary(g => g.Key, g => g.ToList());

        static void Log(ProgressionTimeline timeline, IReadOnlyCollection<ValidationIssue> issues, int total, bool summary)
        {
            if (summary)
            {
                if (total == 0) Debug.Log($"[TimeMapGameplay] {timeline.name}: нарушений нет.", timeline);
                else Debug.LogWarning($"[TimeMapGameplay] {timeline.name}: нарушений — {total}.", timeline);
            }
            foreach (var issue in issues)
            {
                string text = $"[TimeMapGameplay] {timeline.name}: {issue.Message}";
                if (issue.Severity == IssueSeverity.Error) Debug.LogError(text, timeline);
                else Debug.LogWarning(text, timeline);
            }
        }

        static IEnumerable<IValidationRule> DiscoverRules()
            => TypeCache.GetTypesDerivedFrom<IValidationRule>()
                .Where(t => !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.FullName)
                .Select(t => (IValidationRule)Activator.CreateInstance(t));
    }
}
