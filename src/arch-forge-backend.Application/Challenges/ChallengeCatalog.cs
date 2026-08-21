using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using arch_forge_backend.Application.Simulation;

namespace arch_forge_backend.Application.Challenges;

/// <summary>
/// V4 D1.5. Loads every challenge definition and its two blueprints once, at startup, and
/// <strong>refuses to start if any of them is malformed</strong>.
///
/// Failing loudly at boot rather than at evaluation time is the whole point of the catalogue being
/// a closed one: a kind that does not exist, a metric that is not in
/// <see cref="RequirementMetrics"/>, a starter that wires an edge to a node nobody declared - all
/// of those are mistakes made while writing content, and the person who can fix them is looking at
/// a deploy, not at a challenge that mysteriously cannot be passed.
///
/// The files are embedded resources rather than copied content: a definition that is missing at
/// runtime is not a state this class should be able to reach.
/// </summary>
public sealed class ChallengeCatalog
{
    private const string Prefix = "arch_forge_backend.Application.Challenges.Definitions.";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Dictionary<string, LoadedChallenge> _bySlug;

    public ChallengeCatalog()
    {
        var assembly = typeof(ChallengeCatalog).Assembly;
        var loaded = new List<LoadedChallenge>();

        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
                     .Where(n => n.EndsWith(".challenge.json", StringComparison.Ordinal))
                     .OrderBy(n => n, StringComparer.Ordinal))
        {
            var definition = Read<ChallengeDefinition>(assembly, resource)
                             ?? throw new InvalidChallengeDefinitionException($"'{resource}' is empty.");

            var starter = ReadBlueprint(assembly, definition, definition.Starter);
            var solution = ReadBlueprint(assembly, definition, definition.ReferenceSolution);

            Validate(definition, starter, solution);
            loaded.Add(new LoadedChallenge(definition, starter, solution));
        }

        _bySlug = loaded.ToDictionary(c => c.Definition.Slug, StringComparer.Ordinal);
    }

    /// <summary>
    /// V8 §6.5. Ordered as an itinerary - difficulty band first, then the author's <c>order</c>
    /// inside the band, then the slug so two deploys never disagree. It used to be by slug alone,
    /// which with four challenges was arbitrary and harmless and with fourteen puts the advanced
    /// one first.
    /// </summary>
    public IReadOnlyList<ChallengeDefinition> All =>
        _bySlug.Values.Select(c => c.Definition)
            .OrderBy(d => Difficulties.Rank(d.Difficulty))
            .ThenBy(d => d.Order)
            .ThenBy(d => d.Slug, StringComparer.Ordinal)
            .ToList();

    public ChallengeDefinition? Find(string slug) =>
        _bySlug.TryGetValue(slug, out var loaded) ? loaded.Definition : null;

    public ArchitectureBlueprint? StarterOf(string slug) =>
        _bySlug.TryGetValue(slug, out var loaded) ? loaded.Starter : null;

    /// <summary>
    /// The regression net of D3.5 - the tests evaluate it on every build - and, since it is handed
    /// to whoever passes the challenge (<see cref="OpenChallengeSolutionCommandHandler"/>), the one
    /// piece of content that is verified to work before anybody reads it.
    /// </summary>
    public ArchitectureBlueprint? ReferenceSolutionOf(string slug) =>
        _bySlug.TryGetValue(slug, out var loaded) ? loaded.Solution : null;

    // ------------------------------------------------------------------------

    private static void Validate(ChallengeDefinition definition, ArchitectureBlueprint starter, ArchitectureBlueprint solution)
    {
        if (string.IsNullOrWhiteSpace(definition.Slug))
            throw new InvalidChallengeDefinitionException("A challenge has no slug.");

        if (definition.Requirements.Count == 0)
            throw new InvalidChallengeDefinitionException($"'{definition.Slug}' has no mandatory requirements.");

        if (!Difficulties.IsKnown(definition.Difficulty))
            throw new InvalidChallengeDefinitionException(
                $"'{definition.Slug}' is '{definition.Difficulty}', which is not one of beginner, intermediate or advanced.");

        foreach (var problem in starter.Validate())
            throw new InvalidChallengeDefinitionException($"The starter of '{definition.Slug}' {problem}.");

        foreach (var problem in solution.Validate())
            throw new InvalidChallengeDefinitionException($"The reference solution of '{definition.Slug}' {problem}.");

        foreach (var requirement in definition.Requirements.Concat(definition.OptionalGoals))
            Validate(definition.Slug, requirement);

        // D3.2: three scenarios per challenge, counted here and not at run time. Requirements
        // sharing a scenario share one simulation, so what is capped is distinct scenarios.
        var scenarios = definition.Requirements.Concat(definition.OptionalGoals)
            .Where(r => r.Kind == RequirementKinds.UnderScenario)
            .Select(r => ScenarioKey(r.Faults!))
            .Distinct(StringComparer.Ordinal)
            .Count();

        if (scenarios > MaxScenarios)
            throw new InvalidChallengeDefinitionException(
                $"'{definition.Slug}' needs {scenarios} distinct scenarios; the cap is {MaxScenarios}.");
    }

    public const int MaxScenarios = 3;

    private static void Validate(string slug, ChallengeRequirement requirement)
    {
        void Require(bool condition, string what)
        {
            if (!condition)
                throw new InvalidChallengeDefinitionException(
                    $"'{slug}': requirement '{requirement.Kind}' {what}.");
        }

        Require(RequirementKinds.IsKnown(requirement.Kind), "is not a known kind");
        Require(!string.IsNullOrWhiteSpace(requirement.LabelKey), "has no label key");

        switch (requirement.Kind)
        {
            case RequirementKinds.ContainsNode:
            case RequirementKinds.AbsentNode:
                Require(requirement.NodeType is not null, "needs a nodeType");
                break;

            case RequirementKinds.MaxFanOut:
                Require(requirement.Max is >= 0, "needs a max of 0 or more");
                break;

            case RequirementKinds.MaxNodes:
                Require(requirement.Max is >= 1, "needs a max of 1 or more");
                break;

            case RequirementKinds.MaxInstances:
                // No nodeType means every component, which is how "nothing is scaled out" is said
                // in one line instead of ten.
                Require(requirement.Max is >= 1, "needs a max of 1 or more");
                break;

            case RequirementKinds.Edge:
                Require(!string.IsNullOrWhiteSpace(requirement.From) && !string.IsNullOrWhiteSpace(requirement.To),
                    "needs the names of the two ends of the call");
                Require(requirement.Synchronous is not null || requirement.Mechanism is not null,
                    "asks nothing of the call: it needs a synchronous or a mechanism");
                Require(requirement.Mechanism is null || RequirementMechanisms.IsKnown(requirement.Mechanism),
                    "needs a known mechanism");
                break;

            case RequirementKinds.AllEdgesTo:
                Require(requirement.NodeType is not null, "needs a nodeType");
                Require(requirement.Mechanism is not null && RequirementMechanisms.IsKnown(requirement.Mechanism),
                    "needs a known mechanism");
                break;

            case RequirementKinds.PathExists:
                Require(requirement.FromType is not null && requirement.ToType is not null,
                    "needs fromType and toType");
                break;

            case RequirementKinds.NoFinding:
            case RequirementKinds.HasFinding:
                Require(!string.IsNullOrWhiteSpace(requirement.Code), "needs a code");
                break;

            case RequirementKinds.NoSeverity:
                Require(requirement.Severity is not null, "needs a severity");
                break;

            case RequirementKinds.DeclaresLoad:
                Require(requirement.Min is > 0, "needs a min above 0");
                break;

            case RequirementKinds.UnderScenario:
                // An empty list is allowed and means the architecture as it stands, under its
                // declared load: V3 D2.1 already settled that the empty scenario is a valid
                // scenario, and a latency budget is asked without breaking anything.
                Require(requirement.Faults is not null, "needs a list of faults, even an empty one");
                Require(requirement.Metric is not null && RequirementMetrics.IsKnown(requirement.Metric),
                    "needs a known metric");
                Require(requirement.Operator is not null && RequirementOperators.IsKnown(requirement.Operator),
                    "needs a known operator");
                Require(requirement.Value is not null, "needs a value");

                foreach (var fault in requirement.Faults!)
                {
                    Require(!string.IsNullOrWhiteSpace(fault.Node), "names a fault with no node");
                    Require(FaultKinds.IsKnown(fault.Kind), $"uses '{fault.Kind}', which is not a fault kind");
                    Require(!FaultKinds.RequiresValue(fault.Kind) || fault.Value is > 0,
                        $"uses '{fault.Kind}' without a value");
                }

                break;
        }
    }

    internal static string ScenarioKey(IEnumerable<ScenarioFault> faults) =>
        string.Join("|", faults
            .Select(f => $"{f.Node}:{f.Kind}:{f.Value}")
            .OrderBy(s => s, StringComparer.Ordinal));

    private static ArchitectureBlueprint ReadBlueprint(Assembly assembly, ChallengeDefinition definition, string file)
    {
        if (string.IsNullOrWhiteSpace(file))
            throw new InvalidChallengeDefinitionException($"'{definition.Slug}' does not name a starter and a reference solution.");

        return Read<ArchitectureBlueprint>(assembly, Prefix + file)
               ?? throw new InvalidChallengeDefinitionException($"'{file}' is empty or missing.");
    }

    private static T? Read<T>(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
                           ?? throw new InvalidChallengeDefinitionException($"'{resource}' is not embedded in the assembly.");

        return JsonSerializer.Deserialize<T>(stream, Options);
    }

    private sealed record LoadedChallenge(
        ChallengeDefinition Definition,
        ArchitectureBlueprint Starter,
        ArchitectureBlueprint Solution);
}

/// <summary>Thrown while the catalogue loads, which means at startup. It is never an HTTP status.</summary>
public sealed class InvalidChallengeDefinitionException(string message) : Exception(message);
