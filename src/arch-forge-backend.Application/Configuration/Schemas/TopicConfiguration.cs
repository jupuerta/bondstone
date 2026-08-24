namespace arch_forge_backend.Application.Configuration.Schemas;

/// <summary>
/// V14 §3.1. Empty like the broker's nature is simple: a topic has no per-consumer redeliveries
/// (<c>maxRetries</c> is the Queue's, per competing consumer) and no dead-letter of its own in this
/// model. Registered so a configuration sent for one is validated rather than silently stored,
/// exactly like <see cref="ExternalServiceConfiguration"/>.
/// </summary>
public class TopicConfiguration
{
}
