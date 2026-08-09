namespace arch_forge_backend.Application.Configuration.Schemas;

public class QueueConfiguration
{
    public bool Durable { get; set; }
    public bool DeadLetterQueue { get; set; }
    public int? MaxRetries { get; set; }
}
