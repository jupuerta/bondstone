namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V2 D5.2. <see cref="DefaultSeverity"/> is a default, not the final word: a finding carries its
/// own severity (D1.3), so one rule class can escalate by how bad the concrete case is.
/// </summary>
public interface IArchitectureRule
{
    string Code { get; }
    Area Area { get; }
    Severity DefaultSeverity { get; }
    RulePhase Phase { get; }

    /// <summary>Text of the Good finding the analyzer synthesizes when the rule applies and finds nothing (D1.4).</summary>
    string PassedMessage { get; }

    /// <summary>What was checked. Mandatory on every finding, Good included (D2.1, rule 2).</summary>
    string PassedExplanation { get; }

    RuleResult Evaluate(ArchitectureGraph graph);
}
