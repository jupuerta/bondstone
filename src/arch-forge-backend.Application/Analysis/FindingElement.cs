namespace arch_forge_backend.Application.Analysis;

/// <summary>
/// One element a finding points at. A single ordered list of these (V2 D2.2) is what lets the
/// canvas highlight by iterating, without knowing which rule produced the finding.
/// </summary>
public sealed record FindingElement(ElementType Type, Guid Id)
{
    public static FindingElement Node(Guid id) => new(ElementType.Node, id);
    public static FindingElement Connection(Guid id) => new(ElementType.Connection, id);
}
