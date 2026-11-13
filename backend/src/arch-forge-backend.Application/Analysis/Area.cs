namespace bondstone_backend.Application.Analysis;

public enum Area
{
    Availability,
    Resilience,
    Coupling,
    Scalability,
    Security,
    Observability,

    // Last on purpose (V20 plan, contrato 3): anywhere else reorders the postcard, the six lenses
    // and the /rules page at once, and none of the three is this version's business.
    Cost
}
