namespace arch_forge_backend.Application.Configuration.Schemas;

/// <summary>
/// QA §3.3. The internal twin of <see cref="ClientConfiguration"/>: how much work the clock fires.
/// </summary>
public class SchedulerConfiguration
{
    /// <summary>
    /// Jobs triggered per second, feeding the load model exactly as declared RPS does - which is
    /// what lets <c>UNDERSIZED_COMPONENT</c> catch a batch overloading its Worker before anybody
    /// simulates anything.
    /// </summary>
    public int? JobsPerSecond { get; set; }
}
