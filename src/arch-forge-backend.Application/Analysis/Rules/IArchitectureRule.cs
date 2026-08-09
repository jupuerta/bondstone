namespace arch_forge_backend.Application.Analysis.Rules;

public interface IArchitectureRule
{
    string Code { get; }
    Severity Severity { get; }
    IEnumerable<RuleFinding> Evaluate(ArchitectureGraph graph);
}
