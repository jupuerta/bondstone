using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Challenges;

public sealed class ChallengeRequirement
{
    public string Kind { get; set; } = string.Empty;

    public string LabelKey { get; set; } = string.Empty;


    public NodeType? NodeType { get; set; }
    public NodeType? FromType { get; set; }
    public NodeType? ToType { get; set; }

    public int? Min { get; set; }

    public int? Max { get; set; }

    public string? Mechanism { get; set; }

    public string? From { get; set; }

    public string? To { get; set; }

    public bool? Synchronous { get; set; }


    public string? Code { get; set; }
    public Severity? Severity { get; set; }


    public List<ScenarioFault>? Faults { get; set; }
    public string? Metric { get; set; }
    public string? Operator { get; set; }
    public decimal? Value { get; set; }
}

// A fault a challenge poses carries a Node or a Zone, exactly one of the two, checked when the
// catalogue loads. The zone travels as a name and is expanded against the graph by
// SimulationScenario.Resolve, so a board that gains a component later keeps meaning what it said.
public sealed record ScenarioFault(string? Node, string Kind, int? Value = null, string? Zone = null)
{
    public bool NamesANode => !string.IsNullOrWhiteSpace(Node);

    public bool NamesAZone => !string.IsNullOrWhiteSpace(Zone);
}
