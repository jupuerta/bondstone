using arch_forge_backend.Application.Analysis;

namespace arch_forge_backend.Application.Simulation;

/// <summary>
/// The board the fixed point plays on: one record per element of the graph, and the rules that stop
/// the iteration order of a dictionary from leaking into the answer (D5.6). Every element is here
/// from the start, healthy ones included - a green that does not travel in the contract cannot be
/// painted (D2.2).
/// </summary>
public sealed class PropagationState
{
    private sealed class Record
    {
        public HealthState State;
        public bool Contains;
        public string? Mechanism;
        public EffectReason Reason;
        public FindingElement? CausedBy;
        public ReasonContext Context = new();
        public int Step;
    }

    private readonly Dictionary<FindingElement, Record> _records = [];

    public PropagationState(ArchitectureGraph graph, InjectedFaults faults)
    {
        foreach (var node in graph.Nodes)
        {
            var state = faults.InitialStateOf(node.Id);
            _records[FindingElement.Node(node.Id)] = new Record
            {
                State = state,
                Reason = state == HealthState.Healthy ? EffectReason.Healthy : EffectReason.Injected
            };
        }

        foreach (var connection in graph.Connections)
        {
            var severed = faults.IsSevered(connection.Id);
            _records[FindingElement.Connection(connection.Id)] = new Record
            {
                State = severed ? HealthState.Down : HealthState.Healthy,
                Reason = severed ? EffectReason.Injected : EffectReason.Healthy
            };
        }
    }

    /// <summary>
    /// Applies one cause to one element and answers whether anything moved. Monotone by
    /// construction: a state can only go up, which is what makes the iteration converge over a
    /// finite lattice and cycles resolve themselves (D1.5).
    /// </summary>
    public bool Raise(
        FindingElement element,
        HealthState state,
        int step,
        bool contains,
        string? mechanism,
        EffectReason reason,
        FindingElement? causedBy,
        ReasonContext context)
    {
        if (!_records.TryGetValue(element, out var record) || state < record.State)
            return false;

        if (state > record.State)
        {
            Assign(record, state, step, contains, mechanism, reason, causedBy, context);
            return true;
        }

        // Same severity from a second dependency. A cause that did not contain beats one that did:
        // the element is degraded for a real reason as well, so the containment no longer holds and
        // the degradation has to keep climbing.
        if (record.Contains && !contains)
        {
            Assign(record, state, record.Step, false, null, reason, causedBy, context);
            return true;
        }

        if (!record.Contains && contains)
            return false;

        // D2.2: with several causes of the same severity, the one that got there first wins - lower
        // PropagationStep, and on a tie, lower id. Deterministic, and it means what it should:
        // what knocked this over.
        if (step < record.Step || (step == record.Step && IsBefore(causedBy, record.CausedBy)))
            Assign(record, state, Math.Min(step, record.Step), contains, mechanism, reason, causedBy, context);

        return false;
    }

    /// <summary>
    /// The photograph round n+1 is computed over. Taking it makes the order nodes are walked in
    /// within a round irrelevant by construction, rather than by care (D5.6).
    /// </summary>
    public Dictionary<Guid, (HealthState State, bool Contains)> Snapshot()
    {
        var snapshot = new Dictionary<Guid, (HealthState, bool)>();

        foreach (var (element, record) in _records)
        {
            if (element.Type == ElementType.Node)
                snapshot[element.Id] = (record.State, record.Contains);
        }

        return snapshot;
    }

    public Dictionary<Guid, HealthState> NodeStates() =>
        _records.Where(r => r.Key.Type == ElementType.Node)
            .ToDictionary(r => r.Key.Id, r => r.Value.State);

    public HealthState StateOf(FindingElement element) => _records[element].State;

    /// <summary>
    /// Ordered by <c>(PropagationStep, Type, Id)</c>, so the list already reads as the order of
    /// propagation and the animation of D6.3 only has to replay it.
    /// </summary>
    public IReadOnlyList<ElementState> ToOrderedList(ArchitectureGraph graph) => _records
        .Select(entry => new ElementState(
            entry.Key.Type,
            entry.Key.Id,
            entry.Value.State,
            entry.Key.Type == ElementType.Node
                ? SimulationReasons.ForNode(entry.Value.Reason, entry.Value.Context)
                : SimulationReasons.ForConnection(entry.Value.Reason, entry.Value.Context),
            entry.Value.Mechanism,
            entry.Value.CausedBy,
            entry.Value.Step))
        .OrderBy(s => s.PropagationStep)
        .ThenBy(s => s.Type)
        .ThenBy(s => s.Id)
        .ToList();

    private static void Assign(
        Record record,
        HealthState state,
        int step,
        bool contains,
        string? mechanism,
        EffectReason reason,
        FindingElement? causedBy,
        ReasonContext context)
    {
        record.State = state;
        record.Step = step;
        record.Contains = contains;
        record.Mechanism = mechanism;
        record.Reason = reason;
        record.CausedBy = causedBy;
        record.Context = context;
    }

    private static bool IsBefore(FindingElement? candidate, FindingElement? current) =>
        candidate is not null && (current is null || candidate.Id.CompareTo(current.Id) < 0);
}
