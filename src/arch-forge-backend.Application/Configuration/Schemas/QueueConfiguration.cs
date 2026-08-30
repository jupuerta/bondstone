namespace arch_forge_backend.Application.Configuration.Schemas;

public class QueueConfiguration
{
    public int? MaxRetries { get; set; }

    public bool DeadLetterQueue { get; set; }

    // QA 30-08 §11.4. The brokers of the cluster: what `finding.SPOF.suggestion.queue` has always
    // advised —«mount the broker in a cluster, with the queue replicated»— and nobody could declare.
    public int? Instances { get; set; }
}
