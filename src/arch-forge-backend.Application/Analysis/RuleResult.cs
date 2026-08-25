using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis;

public sealed record RuleResult(bool IsApplicable, IReadOnlyList<RuleFinding> Findings)
{
    public LocalizedText? NotApplicableReason { get; private init; }

    public static RuleResult NotApplicable(LocalizedText reason) => new(false, []) { NotApplicableReason = reason };

    public static RuleResult NotApplicable(string reasonKey) => NotApplicable(LocalizedText.Of(reasonKey));

    public static RuleResult Applicable(IReadOnlyList<RuleFinding> findings) => new(true, findings);

    public static RuleResult Applicable(params RuleFinding[] findings) => new(true, findings);
}
