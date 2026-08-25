using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Tests.Analysis.Rules.Support;

public class GraphBuilder
{
    private readonly Dictionary<string, Node> _nodes = [];

    private readonly List<(string Source, string Target, Connection Connection)> _connections = [];
    private int _nextId;

    public GraphBuilder Node(string name, NodeType type = NodeType.Service, string configuration = "{}")
    {
        _nodes.Add(name, new Node
        {
            Id = NextId(),
            Type = type,
            Name = name,
            Configuration = configuration
        });

        return this;
    }

    public GraphBuilder Nodes(NodeType type, params string[] names)
    {
        foreach (var name in names)
            Node(name, type);

        return this;
    }

    public GraphBuilder Connect(
        string source,
        string target,
        bool synchronous = true,
        int? timeoutSeconds = null,
        int? retries = null,
        bool circuitBreaker = false,
        bool fallback = false,
        ProtocolType protocol = ProtocolType.Http,
        string? access = null)
    {
        _connections.Add((source, target, new Connection
        {
            Id = NextId(),
            SourceNodeId = IdOf(source),
            TargetNodeId = IdOf(target),
            Protocol = protocol,
            Synchronous = synchronous,
            Configuration = Resilience(timeoutSeconds, retries, circuitBreaker, fallback, access)
        }));

        return this;
    }

    public Guid IdOf(string name) => _nodes[name].Id;

    public Guid EdgeId(string source, string target) =>
        _connections.First(c => c.Source == source && c.Target == target).Connection.Id;

    public FindingElement NodeElement(string name) => FindingElement.Node(IdOf(name));

    public FindingElement EdgeElement(string source, string target) => FindingElement.Connection(EdgeId(source, target));

    public IEnumerable<string> Names(IEnumerable<FindingElement> elements) => elements.Select(element =>
    {
        if (element.Type == ElementType.Node)
            return _nodes.Values.Single(n => n.Id == element.Id).Name;

        var edge = _connections.Single(c => c.Connection.Id == element.Id);
        return $"{edge.Source}->{edge.Target}";
    });

    public ArchitectureGraph Build() => new(_nodes.Values.ToList(), _connections.Select(c => c.Connection).ToList());

    private Guid NextId() => new($"00000000-0000-0000-0000-{++_nextId:000000000000}");

    private static string Resilience(int? timeoutSeconds, int? retries, bool circuitBreaker, bool fallback, string? access = null)
    {
        var fields = new List<string>();

        if (timeoutSeconds is not null)
            fields.Add($"\"timeoutSeconds\":{timeoutSeconds}");

        if (retries is not null)
            fields.Add($"\"retries\":{retries}");

        fields.Add($"\"circuitBreaker\":{(circuitBreaker ? "true" : "false")}");
        fields.Add($"\"fallback\":{(fallback ? "true" : "false")}");

        if (access is not null)
            fields.Add($"\"access\":\"{access}\"");

        return $"{{{string.Join(",", fields)}}}";
    }
}
