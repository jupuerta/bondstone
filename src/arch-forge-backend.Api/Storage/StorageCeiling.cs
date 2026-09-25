using bondstone_backend.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace bondstone_backend.Api.Storage;

// The backstop for the one cost this product cannot take back: a row.
//
// Every other defence here is **per client** — twenty mints per ten minutes, six hundred requests a
// minute — and per-client defences do not fail against a determined attacker, they get walked
// around. One address is something you have a few of; a hundred addresses cost a few euros, and a
// botnet costs nothing. So the question this answers is not "who is doing it" but "is there still
// room", which nobody can rotate their way out of.
//
// **What it buys is the failure mode, not the prevention.** Full, without it: postgres stops
// accepting writes, the site dies, and it takes the existing architectures with it — everybody's
// work, not just the attacker's. Full, with it: the four endpoints that create a row answer 503 and
// everything else keeps working. Reading, editing what already exists, analysing, simulating and —
// this one matters — **deleting** all stay open, because the way out of a full database is for
// people to be able to remove things from it.
//
// The measurement is cached: this sits in front of endpoints somebody may be hammering, and asking
// postgres its size on every request would turn the defence into a second way to make it work hard.
public sealed class StorageCeiling(
    IServiceScopeFactory scopes,
    IOptions<StorageOptions> options,
    TimeProvider clock,
    ILogger<StorageCeiling> log)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _bytes;
    private DateTimeOffset _measuredAt = DateTimeOffset.MinValue;

    public long MaxBytes => options.Value.MaxDatabaseBytes;

    public async Task<bool> HasRoomAsync(CancellationToken cancellationToken)
    {
        if (MaxBytes <= 0)
            return true;

        return await BytesAsync(cancellationToken) < MaxBytes;
    }

    public async Task<long> BytesAsync(CancellationToken cancellationToken)
    {
        var freshUntil = _measuredAt + TimeSpan.FromSeconds(options.Value.MeasureEverySeconds);

        if (clock.GetUtcNow() < freshUntil)
            return _bytes;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            // Re-checked inside the gate: while this request queued, another one may have measured,
            // and a queue of requests all measuring in turn is exactly the pile-on the cache exists
            // to avoid.
            if (clock.GetUtcNow() < _measuredAt + TimeSpan.FromSeconds(options.Value.MeasureEverySeconds))
                return _bytes;

            using var scope = scopes.CreateScope();
            var size = scope.ServiceProvider.GetRequiredService<IDatabaseSize>();

            _bytes = await size.BytesAsync(cancellationToken);
            _measuredAt = clock.GetUtcNow();

            if (_bytes >= MaxBytes && MaxBytes > 0)
                log.LogWarning("Storage ceiling reached: {Bytes} of {Max} bytes. Minting is closed.", _bytes, MaxBytes);

            return _bytes;
        }
        catch (Exception exception)
        {
            // A database that cannot be measured is not a reason to stop serving. If it is genuinely
            // full the writes themselves will fail, loudly, and that is the honest signal; refusing
            // every creation because one query failed would turn a blip into an outage.
            log.LogError(exception, "Could not measure the database size. Minting stays open.");
            _measuredAt = clock.GetUtcNow();
            _bytes = 0;
            return 0;
        }
        finally
        {
            _gate.Release();
        }
    }
}
