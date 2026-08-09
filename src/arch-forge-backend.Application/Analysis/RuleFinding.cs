namespace arch_forge_backend.Application.Analysis;

public class RuleFinding
{
    public string RuleCode { get; init; } = string.Empty;
    public Severity Severity { get; init; }
    public string Message { get; init; } = string.Empty;
    public Guid? NodeId { get; init; }
}
