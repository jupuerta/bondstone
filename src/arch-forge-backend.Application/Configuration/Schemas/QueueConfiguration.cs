namespace arch_forge_backend.Application.Configuration.Schemas;

/// <summary>
/// V3 D3.5. Durable means "messages survive a broker restart", which is recovery over time, and
/// V3 has no time (D4.7). DeadLetterQueue is material for the QUEUE_NO_DLQ rule and comes back
/// with the rule that justifies it (D6.6).
/// </summary>
public class QueueConfiguration
{
    /// <summary>
    /// D4.7: consumer redeliveries, with the same amplification arithmetic as the edge's retries.
    /// The asynchronous sibling of D4.3, and it was going uncounted.
    /// </summary>
    public int? MaxRetries { get; set; }

    /// <summary>
    /// V4 D5.2, and the debt V3 D6.6 wrote a promissory note for: "DeadLetterQueue is material for
    /// the QUEUE_NO_DLQ rule and comes back with the rule that justifies it". This is that day.
    /// Without one, a message that cannot be processed is a message that is lost in silence.
    /// </summary>
    public bool DeadLetterQueue { get; set; }
}
