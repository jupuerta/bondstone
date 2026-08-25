using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V13 D3.1. The seventeenth rule, and the one that makes <see cref="ProtocolType"/> a field
/// somebody reads rather than a label. Until V13 the only line in the whole codebase that looked at
/// it was <see cref="Simulation.LatencyModel.Hop"/>, which turns it into milliseconds without ever
/// asking whether the protocol makes sense for what is at the other end.
///
/// <strong>It is the rule whose findings you have to fix before the rest mean anything.</strong>
/// Every other rule reasons about an architecture; this one reasons about whether the drawing says
/// what its author meant. An <c>Sql</c> edge into a Queue is not a design with a problem - it is a
/// diagram that has not been finished, and the latency, the timeouts and the blast radius computed
/// over it are answers to a question nobody asked.
///
/// <strong>What it deliberately does not check: an <c>Amqp</c> edge marked synchronous.</strong>
/// That looks like the same class of mistake and it is not one. The manual says so where it explains
/// the field - <em>"HTTP «dispara y olvida» y RPC sobre una cola existen los dos, y por eso esto se
/// marca a mano"</em> - so request/reply over a broker is a pattern this product teaches, and a rule
/// forbidding it would contradict its own documentation to flag a legitimate design. The synchrony
/// of an edge is a claim about behaviour that the protocol does not determine; only the destination
/// does, and that is what this rule reads.
/// </summary>
public class ProtocolMismatchRule : IArchitectureRule
{
    public string Code => RuleCodes.ProtocolMismatch;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.PROTOCOL_MISMATCH.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.PROTOCOL_MISMATCH.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        // V14 B9. The protocol of a scrape is not a claim about how this architecture reaches its
        // database. `Obs → Db` over Http was being told to consider Sql - advice to make the
        // metrics backend speak the database's wire protocol - and the graph that is nothing but
        // instrumentation has nothing to check rather than a clean bill of health (D1.4).
        var edges = graph.Connections.Where(edge => !NodeRoles.IsTelemetryEdge(graph, edge)).ToList();

        if (edges.Count == 0)
            return RuleResult.NotApplicable("rule.na.PROTOCOL_MISMATCH.noCalls");

        var findings = new List<RuleFinding>();

        foreach (var edge in edges)
        {
            if (Mismatch(graph, edge) is not { } mismatch)
                continue;

            var source = graph.NodesById.GetValueOrDefault(edge.SourceNodeId)?.Name ?? string.Empty;
            var target = graph.NodesById.GetValueOrDefault(edge.TargetNodeId)?.Name ?? string.Empty;

            findings.Add(new RuleFinding(
                Code,
                Area,
                mismatch.Severity,
                LocalizedText.Of($"finding.PROTOCOL_MISMATCH.{mismatch.Variant}.message",
                    ("source", source),
                    ("target", target)),

                // A whole sentence per case, never a shared stem plus a tail: the three say
                // different things and which one applies is the rule's decision (V7 D1.1).
                LocalizedText.Of($"finding.PROTOCOL_MISMATCH.{mismatch.Variant}.explanation",
                    ("target", target)),
                LocalizedText.Of($"finding.PROTOCOL_MISMATCH.{mismatch.Variant}.suggestion"),
                [FindingElement.Connection(edge.Id)]));
        }

        return RuleResult.Applicable(findings);
    }

    /// <summary>
    /// The three combinations that have no reading in which they are right, and nothing else.
    ///
    /// <strong>The severity is how unambiguous the mistake is</strong>, not how much damage it does -
    /// which is what makes this rule different from every other one in the analyzer. There is no
    /// blast radius to grade here; there is a drawing that either can or cannot be read two ways.
    ///
    /// <strong>What is deliberately allowed</strong> is as much of the decision as what is flagged.
    /// <c>Http</c> into a Queue is not a mistake - a hosted queue with an HTTP API is the most
    /// ordinary thing there is. <c>Http</c> into a Cache is not one either: the model has three
    /// protocols and <c>Http</c> is the general one, so being strict there would fire on half the
    /// diagrams to say nothing. And an <c>Amqp</c> edge <em>leaving</em> a Queue is the broker
    /// delivering to its consumer, which is the shape this analyzer spends its time recommending.
    /// </summary>
    private static (string Variant, Severity Severity)? Mismatch(ArchitectureGraph graph, Connection edge)
    {
        var target = graph.NodesById.GetValueOrDefault(edge.TargetNodeId)?.Type;
        var source = graph.NodesById.GetValueOrDefault(edge.SourceNodeId)?.Type;

        // An edge hanging off a component that is not there is not this rule's business - the graph
        // itself is broken, and saying so under a protocol heading would be the wrong sentence.
        if (target is null || source is null)
            return null;

        return edge.Protocol switch
        {
            // SQL is the protocol of a relational database and of nothing else. There is no
            // architecture in which this line is what its author meant.
            ProtocolType.Sql when target != NodeType.Database => ("sql", Severity.Critical),

            // AMQP with no broker at either end. Warning rather than Critical because drawing the
            // messaging without drawing the queue is a plausible shorthand rather than a slip.
            ProtocolType.Amqp when target is not (NodeType.Queue or NodeType.Topic)
                                   && source is not (NodeType.Queue or NodeType.Topic)
                => ("amqp", Severity.Warning),

            // A database with an HTTP API exists - a document store, a search engine, a REST layer
            // somebody did not draw - so this is the one of the three with an innocent reading, and
            // it is a Warning that asks rather than a Critical that asserts.
            ProtocolType.Http when target == NodeType.Database => ("http", Severity.Warning),

            _ => null
        };
    }
}
