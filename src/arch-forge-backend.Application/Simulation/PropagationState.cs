using arch_forge_backend.Application.Analysis;

namespace arch_forge_backend.Application.Simulation;

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

        if (record.Contains && !contains)
        {
            Assign(record, state, record.Step, false, null, reason, causedBy, context);
            return true;
        }

        if (!record.Contains && contains)
            return false;

        if (step < record.Step || (step == record.Step && IsBefore(causedBy, record.CausedBy)))
            Assign(record, state, Math.Min(step, record.Step), contains, mechanism, reason, causedBy, context);

        return false;
    }

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

    public IReadOnlyList<ElementState> ToOrderedList(ArchitectureGraph graph, LoadModel? load = null) => _records
        .Select(entry => new ElementState(
            entry.Key.Type,
            entry.Key.Id,
            entry.Value.State,
            entry.Key.Type == ElementType.Node
                ? SimulationReasons.ForNode(entry.Value.Reason, entry.Value.Context)
                : SimulationReasons.ForConnection(entry.Value.Reason, entry.Value.Context),
            entry.Value.Mechanism,
            entry.Value.CausedBy,
            entry.Value.Step,
            load is null || load.IsIdle || entry.Key.Type != ElementType.Node
                ? null
                : Math.Round(load.UtilizationOf(entry.Key.Id), 2)))
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
