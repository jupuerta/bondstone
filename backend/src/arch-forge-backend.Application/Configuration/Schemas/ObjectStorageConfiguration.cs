namespace bondstone_backend.Application.Configuration.Schemas;

public class ObjectStorageConfiguration
{
    // QA 30-08 §11.4. A switch and not a counter, because that is the shape of the real thing: a
    // bucket does not serve from «three copies you sized», it keeps every object in several places at
    // once. It buys redundancy and no capacity — a saturated bucket is not answered with more copies,
    // it is answered with a CDN in front or fewer requests.
    public bool? Replicated { get; set; }
}
