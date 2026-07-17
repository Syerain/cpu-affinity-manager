namespace CpuAffinityManager.Engine;

/// <summary>
/// Implements first-match-wins rule matching with wildcard support.
/// Thread-safe for reads; writes are serialized via lock.
/// </summary>
public class RuleEngine : IRuleEngine
{
    private readonly object _lock = new();
    private List<RuleEntry> _rules = new();

    public IReadOnlyList<RuleEntry> Rules
    {
        get
        {
            lock (_lock)
            {
                return _rules.AsReadOnly();
            }
        }
    }

    /// <summary>
    /// Matches a process against rules in order. Returns the first matching rule,
    /// or null if no rule matches. Disabled rules are skipped.
    /// </summary>
    public RuleEntry? Match(string processName, string fullPath)
    {
        if (string.IsNullOrEmpty(processName))
            return null;

        List<RuleEntry> rulesCopy;
        lock (_lock)
        {
            rulesCopy = new List<RuleEntry>(_rules);
        }

        foreach (var rule in rulesCopy)
        {
            if (!rule.Enabled)
                continue;

            // Process name is always required
            if (string.IsNullOrEmpty(rule.Match.Process))
                continue;

            if (!Wildcard.Match(processName, rule.Match.Process, ignoreCase: true))
                continue;

            // Path match is optional — if specified, must match
            if (!string.IsNullOrEmpty(rule.Match.Path) &&
                !Wildcard.MatchPath(fullPath, rule.Match.Path, ignoreCase: true))
                continue;

            // Exclude patterns — if any match, skip this rule
            if (rule.Match.Exclude != null && rule.Match.Exclude.Count > 0)
            {
                bool excluded = rule.Match.Exclude.Any(ex =>
                    Wildcard.Match(processName, ex, ignoreCase: true));
                if (excluded)
                    continue;
            }

            // All conditions satisfied — first match wins
            return rule;
        }

        return null;
    }

    /// <summary>
    /// Loads rules from a JSON file.
    /// </summary>
    public void Load(string configPath)
    {
        var config = RuleConfig.Load(configPath);
        lock (_lock)
        {
            _rules = config.Rules ?? new List<RuleEntry>();
        }
    }

    /// <summary>
    /// Saves current rules to a JSON file.
    /// </summary>
    public void Save(string configPath)
    {
        RuleConfig config;
        lock (_lock)
        {
            config = new RuleConfig
            {
                Version = 2,
                Rules = new List<RuleEntry>(_rules)
            };
        }
        config.Save(configPath);
    }

    /// <summary>
    /// Adds a rule. If a rule with the same ID exists, it is replaced.
    /// </summary>
    public void AddRule(RuleEntry rule)
    {
        lock (_lock)
        {
            int existingIndex = _rules.FindIndex(r => r.Id == rule.Id);
            if (existingIndex >= 0)
                _rules[existingIndex] = rule;
            else
                _rules.Add(rule);
        }
    }

    /// <summary>
    /// Removes a rule by ID. Returns true if a rule was removed.
    /// </summary>
    public bool RemoveRule(string ruleId)
    {
        lock (_lock)
        {
            int removed = _rules.RemoveAll(r => r.Id == ruleId);
            return removed > 0;
        }
    }
}
