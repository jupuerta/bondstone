using System;
namespace arch_forge_backend.Application.Configuration.Schemas;

/// <summary>
/// The single schema of <see cref="Domain.Entities.Connection.Configuration"/> (V2 D3.1). Unlike
/// nodes, an edge has one schema regardless of protocol, so it is deserialized directly instead of
/// going through <see cref="NodeConfigurationRegistry"/>.
///
/// V3 D4.6 dropped Bulkhead: it is the only one of the five whose meaning - capping the resources
/// a call may hold - needs a model of resources, which D3.3 ruled out. It was persisted, validated
/// and painted, and not a single rule read it. What is left is the four mechanisms the simulator
/// actually executes, and this is now the only surface where resilience is configured.
/// </summary>
public class ResilienceConfiguration
{
    /// <summary>
    /// D4.2, the key to all the others: without a timeout no mechanism of the edge can act, because
    /// there is no error to catch - the call simply never returns.
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    public int? Retries { get; set; }
    public bool CircuitBreaker { get; set; }
    public bool Fallback { get; set; }

    /// <summary>
    /// V14 §4.2. What the call does with the data at the far end: <c>"read"</c>, <c>"write"</c>,
    /// or absent - which means <em>both</em>, the DT.6 trick again ("absent changes nothing"), so
    /// every edge drawn before v7 behaves exactly as it did. It is the one addition to the edge
    /// that unlocks several rules at once: MISSING_CACHE stops advising a cache in front of
    /// declared writes, and SHARED_DATABASE can say the real sentence - two services *writing*
    /// one schema - when the drawing declares it. A string and not an enum for the same reason
    /// the protocol travels as one: the schema is JSON somebody typed, and an unknown value must
    /// read as "nothing declared", never as a failed save.
    /// </summary>
    public string? Access { get; set; }

    /// <summary>The call reads (declared, or nothing declared - both includes read).</summary>
    public bool Reads => !string.Equals(Access, "write", StringComparison.OrdinalIgnoreCase);

    /// <summary>The call writes (declared, or nothing declared - both includes write).</summary>
    public bool Writes => !string.Equals(Access, "read", StringComparison.OrdinalIgnoreCase);

    /// <summary>Somebody said "write" (or "both") out loud - the declared claim, not the default.</summary>
    public bool DeclaresWrite => string.Equals(Access, "write", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(Access, "both", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// V15 E3. Somebody said "read" out loud - the declared claim, never the default. Absent is
    /// both, and both may write, so an undeclared edge is no more a reader than it is a writer.
    /// SHARED_DATABASE reads it for its read lesson the way it reads <see cref="DeclaresWrite"/>
    /// for its strong one.
    /// </summary>
    public bool DeclaresRead => string.Equals(Access, "read", StringComparison.OrdinalIgnoreCase);
}
