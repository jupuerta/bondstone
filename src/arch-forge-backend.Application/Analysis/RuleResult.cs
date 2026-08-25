using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis;

/// <summary>
/// V2 D5.2. A rule returns this instead of an enumerable of findings, because with
/// <c>yield return</c> there is no way to tell "applicable but found nothing" - which is a passed
/// check - from "not applicable", which is nothing at all.
///
/// V16 C7b. And a not-applicable now says <em>why</em>. Until the catalogue of C7 existed the
/// distinction was spent on the score and dropped, so "there was nothing here to check" never
/// reached a screen and the reason never had to be written down. It does now, and it is the most
/// useful sentence of the three states: it tells the user what their drawing is missing for the
/// analyzer to be able to have an opinion at all.
/// </summary>
public sealed record RuleResult(bool IsApplicable, IReadOnlyList<RuleFinding> Findings)
{
    /// <summary>
    /// V16 C7b. Why the rule had nothing to look at - null when it did apply.
    ///
    /// <strong>A key, like every other sentence</strong> (V7 D1.1): this side does not know what
    /// language it is talking to. One key per <em>situation</em> and not per rule, because a rule
    /// can stand down for more than one reason - <c>MISSING_CDN</c> says "you have not declared
    /// any load" and "no door of yours is busy enough" - and a single sentence per code would have
    /// to be vague enough to cover both, which is exactly the sentence nobody can act on.
    /// </summary>
    public LocalizedText? NotApplicableReason { get; private init; }

    /// <summary>
    /// The reason is a parameter and not an optional extra: a rule that stands down without saying
    /// why is a blank card on the catalogue, and the twenty-four had to be visited one by one
    /// anyway. The compiler asking for it is what keeps the twenty-fifth rule from forgetting.
    /// </summary>
    public static RuleResult NotApplicable(LocalizedText reason) => new(false, []) { NotApplicableReason = reason };

    public static RuleResult NotApplicable(string reasonKey) => NotApplicable(LocalizedText.Of(reasonKey));

    public static RuleResult Applicable(IReadOnlyList<RuleFinding> findings) => new(true, findings);

    public static RuleResult Applicable(params RuleFinding[] findings) => new(true, findings);
}
