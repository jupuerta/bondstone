namespace arch_forge_backend.Application.Configuration.Schemas;

/// <summary>V4 D5.3. Like a Service, it is what it is asked to do that multiplies its capacity (DT.4).</summary>
public class WorkerConfiguration
{
    public int? Instances { get; set; }
}
