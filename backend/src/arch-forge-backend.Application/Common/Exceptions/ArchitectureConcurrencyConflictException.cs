namespace bondstone_backend.Application.Common.Exceptions;

public class ArchitectureConcurrencyConflictException : Exception
{
    public ArchitectureConcurrencyConflictException(int currentVersion)
        : base("Architecture was modified by another session.")
    {
        CurrentVersion = currentVersion;
    }

    public int CurrentVersion { get; }
}
