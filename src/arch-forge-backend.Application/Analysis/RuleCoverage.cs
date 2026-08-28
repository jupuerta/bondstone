namespace arch_forge_backend.Application.Analysis;

public enum RuleState
{
    Fired,

    Passed,

    NotApplicable
}

public sealed record RuleCoverage(
    string Code,
    Area Area,
    RuleState State,
    Localization.LocalizedText? Reason = null,
    bool DescribesTheDrawing = false);
