using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V13 D1.3. What a component <em>is</em>, in the two or three distinctions the rules actually
/// argue from — kept in one place because three rules now ask, and three slightly different
/// answers to "is this a database-ish thing" is how a type stops meaning anything.
///
/// <strong>This is V2 D3.2 being paid rather than restated.</strong> That decision says a type earns
/// its place only if some rule looks at it, and until V13 three of the eleven were furniture:
/// <see cref="NodeType.Service"/>, <see cref="NodeType.Worker"/> and
/// <see cref="NodeType.ObjectStorage"/> were drawn, saved, exported and simulated without a single
/// rule ever reading them. They are read here, and through here by SPOF, HIGH_COUPLING and CYCLE.
///
/// The predicates are about <em>behaviour under failure</em>, never about the palette's categories:
/// what makes ObjectStorage sit next to Database is not that both hold bytes, it is that a fallback
/// in front of either one buys you degraded reads and never a write.
/// </summary>
public static class NodeRoles
{
    /// <summary>
    /// Components that are the truth. Losing one loses writes, and <strong>no fallback restores a
    /// write</strong> — it can only serve something stale or refuse. This is the asymmetry that
    /// keeps a fallback-protected <see cref="NodeType.Database"/> a single point of failure while
    /// the same wiring in front of a <see cref="NodeType.Cache"/> is simply a cache doing its job
    /// (D1.1).
    /// </summary>
    public static bool IsSystemOfRecord(NodeType type) =>
        type is NodeType.Database or NodeType.ObjectStorage;

    /// <summary>
    /// Components whose whole purpose is to sit in front of several others.
    /// <strong>Fan-out is their function</strong>, so what a finding has to say to one of them is a
    /// different sentence from what it says to a service that grew seven dependencies (D1.2).
    /// </summary>
    public static bool IsRouter(NodeType type) =>
        type is NodeType.Gateway or NodeType.LoadBalancer;

    /// <summary>
    /// Where state lives. <see cref="NodeType.Queue"/> is deliberately not here: a queue inside a
    /// cycle is the mechanism that breaks it, not the state two components are fighting over
    /// (D1.2, CYCLE).
    /// </summary>
    public static bool IsDataStore(NodeType type) =>
        IsSystemOfRecord(type) || type is NodeType.Cache;
}
