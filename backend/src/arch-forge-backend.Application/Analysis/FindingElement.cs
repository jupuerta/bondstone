namespace bondstone_backend.Application.Analysis;

public sealed record FindingElement(ElementType Type, Guid Id)
{
    public static FindingElement Node(Guid id) => new(ElementType.Node, id);
    public static FindingElement Connection(Guid id) => new(ElementType.Connection, id);
}
