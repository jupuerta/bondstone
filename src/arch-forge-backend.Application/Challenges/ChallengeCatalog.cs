using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using arch_forge_backend.Application.Configuration;
using arch_forge_backend.Application.Simulation;

namespace arch_forge_backend.Application.Challenges;

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

    public ArchitectureBlueprint? ReferenceSolutionOf(string slug) =>
        _bySlug.TryGetValue(slug, out var loaded) ? loaded.Solution : null;


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
            ValidateRequirement(definition.Slug, requirement);

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

    // Public because it is the deploy-time gate: a malformed definition has to bring the process down
    // on the way up, not surface in the user's face, and that promise is worth a test of its own.
    public static void ValidateRequirement(string slug, ChallengeRequirement requirement)
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
                Require(requirement.Max is >= 1, "needs a max of 1 or more");
                break;

            case RequirementKinds.MaxCost:
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
                Require(requirement.Faults is not null, "needs a list of faults, even an empty one");
                Require(requirement.Metric is not null && RequirementMetrics.IsKnown(requirement.Metric),
                    "needs a known metric");
                Require(requirement.Operator is not null && RequirementOperators.IsKnown(requirement.Operator),
                    "needs a known operator");
                Require(requirement.Value is not null, "needs a value");

                foreach (var fault in requirement.Faults!)
                {
                    Require(fault.NamesANode || fault.NamesAZone, "names a fault on neither a node nor a zone");
                    Require(!fault.NamesANode || !fault.NamesAZone,
                        $"names a fault on both the node '{fault.Node}' and the zone '{fault.Zone}'; it is one or the other");
                    Require(FaultKinds.IsKnown(fault.Kind), $"uses '{fault.Kind}', which is not a fault kind");
                    Require(!FaultKinds.RequiresValue(fault.Kind) || fault.Value is > 0,
                        $"uses '{fault.Kind}' without a value");

                    if (!fault.NamesAZone)
                        continue;

                    Require(ConfigurationRanges.Zones.Contains(fault.Zone!),
                        $"names the zone '{fault.Zone}', which is not one of {string.Join(", ", ConfigurationRanges.Zones)}");
                    Require(FaultKinds.AppliesToZone(fault.Kind),
                        $"asks a zone for '{fault.Kind}', and the only thing that happens to a zone is that it goes down");
                }

                break;
        }
    }

    internal static string ScenarioKey(IEnumerable<ScenarioFault> faults) =>
        string.Join("|", faults
            .Select(f => $"{f.Node}:{f.Zone}:{f.Kind}:{f.Value}")
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

public sealed class InvalidChallengeDefinitionException(string message) : Exception(message);
