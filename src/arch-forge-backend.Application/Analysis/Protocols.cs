using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Analysis;

// The protocol of a call is a fact about its two ends, not a choice. Measured over the fifty drawings
// of the catalogue (259 calls) before the selector went away: every one of them followed this table.
// A store speaks Sql; a broker at either end speaks Amqp; telemetry is scraped and pushed over Http,
// even from a queue; everything else is Http.
public static class Protocols
{
    public static ProtocolType Between(NodeType? source, NodeType? target)
    {
        if (target == NodeType.Observability)
            return ProtocolType.Http;

        if (target == NodeType.Database)
            return ProtocolType.Sql;

        if (IsBroker(source) || IsBroker(target))
            return ProtocolType.Amqp;

        return ProtocolType.Http;
    }

    private static bool IsBroker(NodeType? type) => type is NodeType.Queue or NodeType.Topic;
}
