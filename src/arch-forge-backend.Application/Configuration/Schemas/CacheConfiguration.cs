namespace arch_forge_backend.Application.Configuration.Schemas;

public class CacheConfiguration
{
    public decimal? HitRatio { get; set; }

    // QA 30-08 §11.4. A cache is a pool of nodes —Redis Cluster, a Memcached ring—: the copies share
    // out the keys instead of each holding them all, which the model does not draw. What it does draw
    // is the half that matters here: more nodes are more capacity, and losing one is not losing the cache.
    public int? Instances { get; set; }
}
