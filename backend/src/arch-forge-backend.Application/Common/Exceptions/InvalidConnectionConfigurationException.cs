namespace bondstone_backend.Application.Common.Exceptions;

public class InvalidConnectionConfigurationException : Exception
{
    public InvalidConnectionConfigurationException(Guid connectionId, string reason)
        : base($"Configuration for connection '{connectionId}' is invalid: {reason}")
    {
    }
}
