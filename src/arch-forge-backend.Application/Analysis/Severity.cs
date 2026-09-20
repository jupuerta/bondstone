namespace bondstone_backend.Application.Analysis;

// Ordered by how loud they are, because the order is load-bearing: `PenaltyForCode` sorts findings
// by it and the panel sorts sections by it. `Info` goes between `Good` and `Warning` so `Critical`
// stays the top of the scale.
//
// V13 §11 asked for `Info` and V20 added it: with three levels, `MISSING_CACHE` — a suggestion about
// speed — carried the same five points as `QUEUE_NO_DLQ`, which is silent data loss. `Info` is a
// finding with something to say and **no penalty**: it earns a card and a sentence, not a subtraction.
public enum Severity
{
    Good,
    Info,
    Warning,
    Critical
}
