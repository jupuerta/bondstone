using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

public interface IArchitectureRule
{
    string Code { get; }
    Area Area { get; }
    Severity DefaultSeverity { get; }
    RulePhase Phase { get; }

    LocalizedText PassedMessage { get; }

    LocalizedText PassedExplanation { get; }

    RuleResult Evaluate(ArchitectureGraph graph);
}
