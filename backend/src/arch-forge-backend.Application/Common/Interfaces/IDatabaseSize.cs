namespace bondstone_backend.Application.Common.Interfaces;

/// <summary>How much disk the database is using right now.</summary>
public interface IDatabaseSize
{
    Task<long> BytesAsync(CancellationToken cancellationToken);
}
