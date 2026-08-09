namespace arch_forge_backend.Application.Common.Exceptions;

public class InvalidNodeConfigurationException : Exception
{
    public InvalidNodeConfigurationException(string nodeName, string reason)
        : base($"Configuration for node '{nodeName}' is invalid: {reason}")
    {
    }
}
