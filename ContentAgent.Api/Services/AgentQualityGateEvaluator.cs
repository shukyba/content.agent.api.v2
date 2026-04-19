using System.Text.Json;
using System.Text.RegularExpressions;
using ContentAgent.Api.Models;

namespace ContentAgent.Api.Services;

internal static class AgentQualityGateEvaluator
{
    private static readonly string[] DefaultConcreteRegexes =
    [
        @"\b\d{1,4}\b",
        @"https?://",
        @"[$€£]\s?\d",
    ];

    internal static AgentQualityGateEvaluation Evaluate(string agentId, AgentRepoSpec spec, IReadOnlyList<FileEdit> edits)
    {
        var gate = spec.QualityGate;
        if (gate == null || !gate.Enabled || gate.Rules == null || gate.Rules.Count == 0)
            return AgentQualityGateEvaluation.PassedEvaluation();

        var issues = new List<string>();
        foreach (var rule in gate.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Path))
                continue;

            var matches = edits.Where(e =>
                    string.Equals(NormalizePath(e.Path), NormalizePath(rule.Path), StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrWhiteSpace(rule.EditType) ||
                        string.Equals(e.EditType ?? string.Empty, rule.EditType, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (matches.Count == 0)
            {
                if (rule.RequireMatch)
                    issues.Add($"{RuleName(rule)}: no matching edit for path {rule.Path}.");
                continue;
            }

            if (string.Equals(rule.Type, "items", StringComparison.OrdinalIgnoreCase))
                EvaluateItemsRule(rule, matches, issues);
            else
                EvaluateTextRule(rule, matches, issues);
        }

        return issues.Count == 0
            ? AgentQualityGateEvaluation.PassedEvaluation()
            : AgentQualityGateEvaluation.FailedEvaluation(issues);
    }

    private static void EvaluateTextRule(AgentQualityRule rule, IReadOnlyList<FileEdit> edits, List<string> issues)
    {
        var source = rule.TextSource?.Trim().ToLowerInvariant() ?? "value";
        foreach (var edit in edits)
        {
            var text = source switch
            {
                "content" => edit.Content ?? string.Empty,
                "key" => edit.Key ?? string.Empty,
                _ => edit.Value ?? string.Empty,
            };

            if (!string.IsNullOrWhiteSpace(rule.ExtractRegex))
            {
                var m = Regex.Match(text, rule.ExtractRegex, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (m.Success)
                {
                    if (m.Groups["value"].Success)
                        text = m.Groups["value"].Value;
                    else if (m.Groups.Count > 1)
                        text = m.Groups[1].Value;
                }
            }

            var normalized = NormalizeText(text);
            if (rule.MinLength is > 0 && normalized.Length < rule.MinLength.Value)
                issues.Add($"{RuleName(rule)}: text too short ({normalized.Length} < {rule.MinLength.Value}).");

            if (rule.RequiredTokens is { Count: > 0 })
            {
                var minMatches = Math.Max(1, rule.MinTokenMatches ?? rule.RequiredTokens.Count);
                var matched = rule.RequiredTokens.Count(t =>
                    normalized.Contains(t, StringComparison.OrdinalIgnoreCase));
                if (matched < minMatches)
                    issues.Add($"{RuleName(rule)}: insufficient signal token matches ({matched} < {minMatches}).");
            }

            if (rule.ForbiddenPhrases is { Count: > 0 })
            {
                var hit = rule.ForbiddenPhrases.FirstOrDefault(p =>
                    normalized.Contains(p, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(hit))
                    issues.Add($"{RuleName(rule)}: forbidden phrase detected: \"{hit}\".");
            }

            if (!string.IsNullOrWhiteSpace(rule.RegexPattern) && rule.MinRegexMatches is > 0)
            {
                var count = Regex.Matches(normalized, rule.RegexPattern, RegexOptions.IgnoreCase).Count;
                if (count < rule.MinRegexMatches.Value)
                    issues.Add($"{RuleName(rule)}: regex matches too low ({count} < {rule.MinRegexMatches.Value}).");
            }
        }
    }

    private static void EvaluateItemsRule(AgentQualityRule rule, IReadOnlyList<FileEdit> edits, List<string> issues)
    {
        var qField = string.IsNullOrWhiteSpace(rule.QuestionField) ? "question" : rule.QuestionField!;
        var aField = string.IsNullOrWhiteSpace(rule.AnswerField) ? "answer" : rule.AnswerField!;

        foreach (var edit in edits)
        {
            if (!edit.Items.HasValue || edit.Items.Value.ValueKind != JsonValueKind.Array)
            {
                issues.Add($"{RuleName(rule)}: items array missing.");
                continue;
            }

            var items = edit.Items.Value;
            var itemCount = items.GetArrayLength();
            if (rule.MinItems is > 0 && itemCount < rule.MinItems.Value)
                issues.Add($"{RuleName(rule)}: too few items ({itemCount} < {rule.MinItems.Value}).");
            if (rule.MaxItems is > 0 && itemCount > rule.MaxItems.Value)
                issues.Add($"{RuleName(rule)}: too many items ({itemCount} > {rule.MaxItems.Value}).");

            var concreteRegexes = rule.ConcreteRegexes is { Count: > 0 }
                ? rule.ConcreteRegexes
                : DefaultConcreteRegexes.ToList();

            var concreteAnswers = 0;
            var topicCoverage = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var q = item.TryGetProperty(qField, out var qp) && qp.ValueKind == JsonValueKind.String
                    ? qp.GetString() ?? string.Empty
                    : string.Empty;
                var a = item.TryGetProperty(aField, out var ap) && ap.ValueKind == JsonValueKind.String
                    ? ap.GetString() ?? string.Empty
                    : string.Empty;

                var qn = NormalizeText(q);
                var an = NormalizeText(a);

                if (rule.MinQuestionLength is > 0 && qn.Length < rule.MinQuestionLength.Value)
                    issues.Add($"{RuleName(rule)}: question too short ({qn.Length} < {rule.MinQuestionLength.Value}).");
                if (rule.MinAnswerLength is > 0 && an.Length < rule.MinAnswerLength.Value)
                    issues.Add($"{RuleName(rule)}: answer too short ({an.Length} < {rule.MinAnswerLength.Value}).");

                if (concreteRegexes.Any(rx => Regex.IsMatch(an, rx, RegexOptions.IgnoreCase)))
                    concreteAnswers++;

                if (rule.RequiredTopicGroups is { Count: > 0 })
                {
                    foreach (var group in rule.RequiredTopicGroups)
                    {
                        if (group.Tokens == null || group.Tokens.Count == 0)
                            continue;

                        if (group.Tokens.Any(token =>
                                qn.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                                an.Contains(token, StringComparison.OrdinalIgnoreCase)))
                        {
                            topicCoverage.Add(group.Name);
                        }
                    }
                }
            }

            if (rule.MinConcreteAnswers is > 0 && concreteAnswers < rule.MinConcreteAnswers.Value)
                issues.Add($"{RuleName(rule)}: concrete answers too few ({concreteAnswers} < {rule.MinConcreteAnswers.Value}).");

            if (rule.RequiredTopicGroups is { Count: > 0 })
            {
                foreach (var group in rule.RequiredTopicGroups)
                {
                    if (!topicCoverage.Contains(group.Name))
                        issues.Add($"{RuleName(rule)}: missing topic coverage \"{group.Name}\".");
                }
            }
        }
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').Trim();
    private static string NormalizeText(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();
    private static string RuleName(AgentQualityRule rule) =>
        string.IsNullOrWhiteSpace(rule.Name) ? $"rule:{rule.Type}:{rule.Path}" : rule.Name;
}

internal sealed record AgentQualityGateEvaluation(bool Passed, IReadOnlyList<string> Issues)
{
    internal string FeedbackForModel =>
        "QUALITY GATE FEEDBACK (must satisfy before final JSON edits):\n"
        + string.Join('\n', Issues.Select((issue, idx) => $"{idx + 1}. {issue}"));

    internal static AgentQualityGateEvaluation PassedEvaluation() =>
        new(true, Array.Empty<string>());

    internal static AgentQualityGateEvaluation FailedEvaluation(IReadOnlyList<string> issues) =>
        new(false, issues);
}
