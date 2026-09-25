using bondstone_backend.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace bondstone_backend.Infrastructure.Persistence;

/// <summary>Asks postgres how big the database is.</summary>
public sealed class DatabaseSize(BondstoneDbContext dbContext) : IDatabaseSize
{
    // The only raw SQL in the codebase, and it stays reviewable by being a constant: there is no
    // interpolation, no parameter and no caller-supplied fragment, so there is nothing to inject.
    // It is here because the question has no answer in EF — the size of a database is a property of
    // the server, not of the model — and because counting rows instead would mean guessing a size
    // per row that varies by two orders of magnitude between a three-box sketch and a full one.
    private const string Query = "SELECT pg_database_size(current_database())";

    public async Task<long> BytesAsync(CancellationToken cancellationToken)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = Query;

        await dbContext.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is long bytes ? bytes : 0;
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }
}
