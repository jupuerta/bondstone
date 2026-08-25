namespace arch_forge_backend.Application.Common.Exceptions;

public class InvalidSimulationScenarioException : Exception
{
    public InvalidSimulationScenarioException(string reason)
        : base($"Simulation scenario is invalid: {reason}")
    {
    }
}
