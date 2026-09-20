namespace bondstone_backend.Application.Common.Exceptions;

public class InvalidSimulationScenarioException : Exception
{
    public InvalidSimulationScenarioException(string reason)
        : base($"Simulation scenario is invalid: {reason}")
    {
    }
}
