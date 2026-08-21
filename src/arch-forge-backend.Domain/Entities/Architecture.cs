namespace arch_forge_backend.Domain.Entities;

public class Architecture
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The read handle. Grants everything that does not write: view, analyze, simulate, fork,
    /// history. Safe to publish, safe to log, safe to hand to a stranger.
    /// </summary>
    public string ShareToken { get; set; } = string.Empty;

    /// <summary>
    /// The write handle. The only thing that grants <c>PUT</c>/<c>DELETE</c> — the <see cref="Id"/>
    /// used to do that job and, because every read handed it back, doubled as a write credential
    /// nobody meant to publish. The backend mints one exactly when it mints an architecture (create,
    /// fork, start a challenge) and never returns it on any other response - a value that leaks back
    /// out through a read endpoint is a value somebody will eventually treat as if it were public.
    /// </summary>
    public string EditToken { get; set; } = string.Empty;

    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<Node> Nodes { get; set; } = new();
    public List<Connection> Connections { get; set; } = new();
}
