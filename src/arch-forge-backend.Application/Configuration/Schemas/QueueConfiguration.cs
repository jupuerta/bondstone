namespace arch_forge_backend.Application.Configuration.Schemas;

public class QueueConfiguration
{
    public int? MaxRetries { get; set; }

    public bool DeadLetterQueue { get; set; }
}
