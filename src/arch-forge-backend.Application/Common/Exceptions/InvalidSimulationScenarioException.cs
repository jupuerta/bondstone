namespace arch_forge_backend.Application.Common.Exceptions;

/// <summary>
/// V3 D2.1. Front and back deploy together and a fault kind is never reused, so a scenario the
/// backend does not understand can only have come from a malformed request - never from a client
/// that has not reloaded since the deploy. That is what separates this 400 from the id that no
/// longer exists, which is dropped silently and announced in the UI.
/// </summary>
public class InvalidSimulationScenarioException : Exception
{
    public InvalidSimulationScenarioException(string reason)
        : base($"Simulation scenario is invalid: {reason}")
    {
    }
}
