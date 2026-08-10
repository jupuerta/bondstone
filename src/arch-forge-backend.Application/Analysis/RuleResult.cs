namespace arch_forge_backend.Application.Analysis;

/// <summary>
/// V2 D5.2. A rule returns this instead of an enumerable of findings, because with
/// <c>yield return</c> there is no way to tell "applicable but found nothing" - which is a passed
/// check - from "not applicable", which is nothing at all.
/// </summary>
public sealed record RuleResult(bool IsApplicable, IReadOnlyList<RuleFinding> Findings)
{
    public static readonly RuleResult NotApplicable = new(false, []);

    public static RuleResult Applicable(IReadOnlyList<RuleFinding> findings) => new(true, findings);

    public static RuleResult Applicable(params RuleFinding[] findings) => new(true, findings);
}
