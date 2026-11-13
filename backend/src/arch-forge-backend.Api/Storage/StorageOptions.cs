namespace bondstone_backend.Api.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Above this, the four endpoints that create an architecture answer 503. Zero disables the
    /// ceiling, which is what the tests and local work want.
    /// </summary>
    /// <remarks>
    /// Two gigabytes, against a 38 GB server holding about eight megabytes today. The number is not
    /// the disk: the daily backups keep thirty days of dumps on that same disk, so whatever the
    /// database grows, the machine carries several times over. Leaving the ceiling well below the
    /// free space is what keeps a full database from also being a server nobody can log into to fix
    /// it.
    /// </remarks>
    public long MaxDatabaseBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>How long a measurement is trusted before asking postgres again.</summary>
    public int MeasureEverySeconds { get; set; } = 30;
}
