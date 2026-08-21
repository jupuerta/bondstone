namespace arch_forge_backend.Application.Configuration.Schemas;

/// <summary>
/// V8 §6.2. Deliberately empty, and the emptiest schema in the product is the one that says the
/// most: <strong>there is nothing about somebody else's service that you get to configure.</strong>
///
/// No <c>instances</c> - that is the whole difference with <see cref="ServiceConfiguration"/>, and
/// giving it the field "for symmetry" would put a lever on screen that changes nothing, which is
/// worse than not having it. A provider is registered for it anyway so that a configuration sent
/// for one is validated rather than silently stored (see <c>UpdateArchitecture</c>).
/// </summary>
public class ExternalServiceConfiguration
{
}
