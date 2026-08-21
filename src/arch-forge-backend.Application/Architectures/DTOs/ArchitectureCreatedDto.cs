namespace arch_forge_backend.Application.Architectures.DTOs;

/// <summary>
/// The one extra field, and the only three responses that ever carry it: creating an architecture,
/// forking one, and starting a challenge. Those are the only moments the backend has just picked an
/// edit token and nobody holds it yet - every other response resolves through a token the caller
/// already has, so echoing it back would only be a second way to leak it.
/// </summary>
public class ArchitectureCreatedDto : ArchitectureDto
{
    public string EditToken { get; set; } = string.Empty;
}
