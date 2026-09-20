using System.Text.Json;
using bondstone_backend.Application.Challenges;
using bondstone_backend.Application.Tests.TestDoubles;
using bondstone_backend.Domain.Enums;
using Xunit;

namespace bondstone_backend.Application.Tests.Challenges;

// ChallengeDefinitionsTests proves the pair every challenge owes: the reference solution passes and
// the starter does not. That pair says nothing about the third board that matters — the one that
// applies a mechanism the model treats as free instead of the lesson: unticking «synchronous», a
// rate limit at half the traffic, a fallback on a router, a zone left blank, the call deleted instead
// of protected. Every case here is a starter plus exactly one shortcut, as measured in
// docs/QA-consistencia-2026-08-30.md §1 and re-measured in §9. A closed case asserts that the guard
// requirement is the only thing left red, so the guard is not decoration and the shortcut fails for
// the reason the catalogue's «anti-atajo» paragraph gives. An open case asserts that it still passes:
// this file goes red the day the engine closes it, and that is the moment to move it across and ask
// whether the challenge still needs the guard it carries.
public class ChallengeShortcutTests
{
    private sealed record Shortcut(
        string Slug,
        Func<ArchitectureBlueprint> Board,
        string[] Guards,
        bool OnlyTheGuard = true,
        bool WithOptionalGoals = false);

    private static readonly Dictionary<string, Shortcut> Closed = new(StringComparer.Ordinal)
    {
        // A — unticking «synchronous»
        ["thirty-seconds · Storefront → Orders asynchronous, 60 s timeout intact"] = new(
            "thirty-seconds",
            () => Asynchronous(Starter("thirty-seconds"), ("Storefront", "Orders")),
            ["requirement.2"]),

        ["latency-budget · Storefront → Catalog asynchronous"] = new(
            "latency-budget",
            () => Asynchronous(Starter("latency-budget"), ("Storefront", "Catalog")),
            ["requirement.5"]),

        ["third-party-outage · Checkout → Payments asynchronous, no fallback nor breaker"] = new(
            "third-party-outage",
            () => Asynchronous(Starter("third-party-outage"), ("Checkout", "Payments")),
            ["requirement.5"]),

        ["already-signed-in · Storefront → Auth0 asynchronous"] = new(
            "already-signed-in",
            () => Asynchronous(Starter("already-signed-in"), ("Storefront", "Auth0")),
            ["requirement.2"]),

        ["retry-storm · the three calls asynchronous, retries intact"] = new(
            "retry-storm",
            () => Asynchronous(Starter("retry-storm"), ("Edge", "Orders"), ("Orders", "Inventory"), ("Inventory", "Pricing")),
            ["requirement.4", "requirement.5"]),

        ["timeout-was-enough · the four calls asynchronous, 1 s timeouts intact"] = new(
            "timeout-was-enough",
            () => Asynchronous(Starter("timeout-was-enough"), ("Edge", "Storefront"), ("Storefront", "Checkout"), ("Checkout", "Billing"), ("Billing", "Ledger")),
            ["requirement.3", "requirement.7"]),

        ["nobody-noticed · both reads of Products asynchronous, no fallback"] = new(
            "nobody-noticed",
            () => Asynchronous(Starter("nobody-noticed"), ("Catalog", "Products"), ("Reviews", "Products")),
            ["requirement.4", "requirement.6", "requirement.7"]),

        ["single-point-of-failure · a balancer and Front → Api asynchronous, one Api"] = new(
            "single-point-of-failure",
            () => WithBalancer(Starter("single-point-of-failure"), synchronous: false, """{"timeoutSeconds":5}"""),
            ["requirement.4"]),

        ["payments-friday · Orders → Payments and Orders → Db asynchronous, fallback upstream"] = new(
            "payments-friday",
            () => WithEdge(
                Asynchronous(Starter("payments-friday"), ("Orders", "Payments"), ("Orders", "Db")),
                "Storefront", "Orders", """{"timeoutSeconds":5,"fallback":true}"""),
            ["requirement.2", "requirement.4"]),

        // B — a rate limit just under the traffic. TrafficRejectedRule warned from 50 % rejected
        // (inclusive) until 7.3.2, and from 20 % since; the two at 48 % are further down.
        ["no-new-hardware · rateLimit 155 of 350 rps"] = new(
            "no-new-hardware",
            () => WithNode(Starter("no-new-hardware"), "Storefront", """{"rateLimit":155}"""),
            ["requirement.5"]),

        ["latency-budget · rateLimit 100 of 300 rps"] = new(
            "latency-budget",
            () => WithNode(Starter("latency-budget"), "Storefront", """{"rateLimit":100}"""),
            ["requirement.6"]),

        ["timeout-was-enough · rateLimit 300 of 600 rps"] = new(
            "timeout-was-enough",
            () => WithNode(Starter("timeout-was-enough"), "Edge", """{"rateLimit":300}"""),
            ["requirement.8"]),

        ["backlog · rateLimit 200 of 500 rps, a second worker and a DLQ"] = new(
            "backlog",
            () => BacklogShortcut(),
            ["requirement.6"]),

        // The one load challenge that was guarded before the pass: the limit sheds 40 %, which the
        // budget refused on its own — the starter already costs more than the 24 the brief allows and
        // a limit does not make a box cheaper — and which the rule now names as well.
        ["it-does-not-fit · rateLimit 300 of 500 rps"] = new(
            "it-does-not-fit",
            () => WithNode(Starter("it-does-not-fit"), "Storefront", """{"rateLimit":300}"""),
            ["requirement.4", "requirement.5"]),

        // C — a fallback where there is nothing to answer with. Since 7.4 the model does not believe
        // a router's fallback either, so the scenario fails on its own and the two-Apis guard is the
        // second red, not the only one.
        ["single-point-of-failure · a balancer without health check, fallback on Front → Api, one Api"] = new(
            "single-point-of-failure",
            () => WithBalancer(Starter("single-point-of-failure"), synchronous: true, """{"timeoutSeconds":5,"fallback":true}"""),
            ["requirement.3", "requirement.4"]),

        // D — deleting the call instead of protecting it
        ["nobody-noticed · both reads of Products deleted"] = new(
            "nobody-noticed",
            () => WithoutEdges(Starter("nobody-noticed"), ("Catalog", "Products"), ("Reviews", "Products")),
            ["requirement.6", "requirement.7"]),

        // Until 7.2 these two failed by accident (QA §1.4, §3.6): the orphaned box was a root, so the
        // metrics counted it as an entry point and the scenario came out affected. A loose box is no
        // longer where requests enter, so each challenge carries a path requirement of its own — by
        // type, so the call may still go through a queue.
        ["payments-friday · Orders → Payments deleted"] = new(
            "payments-friday",
            () => WithoutEdges(Starter("payments-friday"), ("Orders", "Payments")),
            ["requirement.5"]),

        ["third-party-outage · Checkout → Payments deleted"] = new(
            "third-party-outage",
            () => WithoutEdges(Starter("third-party-outage"), ("Checkout", "Payments")),
            ["requirement.6"]),

        // E — leaving the zone blank
        ["both-in-the-same-zone · no zone on Edge, Front, Api 2 and Telemetry, nothing moves"] = new(
            "both-in-the-same-zone",
            () => Rezoned(Starter("both-in-the-same-zone"), name => name is "Api" or "Cache" or "Db" ? "a" : null),
            ["requirement.5"]),

        ["both-in-the-same-zone · no zone on Edge, Front and Telemetry, Front → Api/Api 2 asynchronous, both Apis in a"] = new(
            "both-in-the-same-zone",
            () => Asynchronous(
                Rezoned(Starter("both-in-the-same-zone"), name => name is "Api" or "Api 2" or "Cache" or "Db" ? "a" : null),
                ("Front", "Api"), ("Front", "Api 2")),
            ["requirement.5"]),

        // Until 7.3.3 the door also came out affected here, only because the telemetry push
        // propagated (QA §4.1); with Telemetry out of the propagation, spansZones stops it alone.
        ["both-in-the-same-zone · no zone on Edge, Front and Api 2, Telemetry stays in a"] = new(
            "both-in-the-same-zone",
            () => Rezoned(Starter("both-in-the-same-zone"), name => name is "Api" or "Cache" or "Db" or "Telemetry" ? "a" : null),
            ["requirement.5"]),

        // Closed by 7.3: the engine now speaks where it was silent. A cache miss that does not wait
        // is an arrow that cannot be what it says (IMPLAUSIBLE_DEPENDENCY, §3.1), and shedding 48 %
        // of the customers is past the 20 % TrafficRejectedRule now warns from (§3.4).
        ["database-overload · a cache in front, Cache → Db asynchronous"] = new(
            "database-overload",
            () => CacheInFront(Starter("database-overload"), synchronousMiss: false),
            ["requirement.6"]),

        ["database-overload · rateLimit 155 of 300 rps (48 %)"] = new(
            "database-overload",
            () => WithNode(Starter("database-overload"), "Storefront", """{"rateLimit":155}"""),
            ["requirement.5"]),

        ["million-a-day · rateLimit 60 of 116 rps (48 %), no read replica"] = new(
            "million-a-day",
            () => WithNode(WithNode(Solution("million-a-day"), "Gateway", """{"rateLimit":60}"""), "Db", "{}", replace: true),
            ["requirement.7"]),

        // Closed by 7.4 (QA §1.6): a reporting panel that calls itself a gateway and still runs SQL
        // is a router querying a database, which IMPLAUSIBLE_DEPENDENCY now names, and the challenge
        // asks for silence on it.
        ["exposed-database · Reports becomes a Gateway with authentication, still SQL to Db"] = new(
            "exposed-database",
            () => WithNode(
                Retyped(Starter("exposed-database"), "Reports", NodeType.Gateway, """{"requestsPerSecond":20,"authentication":true}"""),
                "Edge", """{"authentication":true}"""),
            ["requirement.4"]),

        // F — rubbing out the traffic. Since QA §10.2 (point 1) a door nobody calls is not asked
        // whether it authenticates: with no rps reaching it and no client drawn, UNAUTHENTICATED_ENTRY
        // stands down, so a `noFinding` on it passes by deleting the number instead of answering the
        // question. The load guard is what stops it; exposed-database gained one for this.
        ["who-is-calling · requestsPerSecond deleted, authentication still no"] = new(
            "who-is-calling",
            () => Stripped(Starter("who-is-calling"), "requestsPerSecond"),
            ["requirement.1"]),

        ["million-a-day · the solution with no traffic and no authentication on the gateway"] = new(
            "million-a-day",
            () => Stripped(Solution("million-a-day"), "requestsPerSecond", "authentication"),
            ["requirement.1"]),

        ["exposed-database · the solution with no traffic and no authentication anywhere"] = new(
            "exposed-database",
            () => Stripped(Solution("exposed-database"), "requestsPerSecond", "authentication"),
            ["requirement.5"]),
    };

    // Empty since 7.4 closed the last of the measured shortcuts. It stays: the next pass that finds
    // one the engine cannot yet close writes it here, and the test below keeps it honest until it can.
    private static readonly Dictionary<string, Shortcut> Open = new(StringComparer.Ordinal);

    public static TheoryData<string> ClosedCases() => Names(Closed);

    [Theory]
    [MemberData(nameof(ClosedCases))]
    public void The_shortcut_does_not_pass_and_the_guard_is_what_stops_it(string name)
    {
        var shortcut = Closed[name];
        var result = ChallengeHarness.Evaluate(shortcut.Slug, shortcut.Board());

        var unmet = Unmet(result);
        var guards = shortcut.Guards
            .Select(guard => $"challenge.{shortcut.Slug}.{guard}")
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.False(result.Passed, $"'{shortcut.Slug}' is passed by a board that applies the shortcut and not the lesson: {name}.");

        if (shortcut.OnlyTheGuard)
            Assert.Equal(guards, unmet);
        else
            Assert.All(guards, guard => Assert.Contains(guard, unmet));
    }

    [Fact]
    public void Every_open_shortcut_still_passes_until_the_engine_closes_it()
    {
        Assert.All(Open, entry =>
        {
            var (name, shortcut) = entry;
            var result = ChallengeHarness.Evaluate(shortcut.Slug, shortcut.Board());

            Assert.True(
                result.Passed,
                $"'{shortcut.Slug}' no longer passes with the shortcut ({name}) — the engine closed it. Move the case to " +
                $"Closed with the requirement that stops it, and check whether the challenge still needs the guard it " +
                $"carries. Unmet now: {string.Join(", ", Unmet(result))}.");

            if (shortcut.WithOptionalGoals)
                Assert.All(result.OptionalGoals, goal => Assert.True(goal.Met));
        });
    }

    [Fact]
    public void Every_case_names_a_challenge_that_exists()
    {
        Assert.All(
            Closed.Values.Concat(Open.Values),
            shortcut => Assert.NotNull(ChallengeHarness.Catalog.Find(shortcut.Slug)));
    }

    private static TheoryData<string> Names(Dictionary<string, Shortcut> cases)
    {
        var data = new TheoryData<string>();
        foreach (var name in cases.Keys.OrderBy(k => k, StringComparer.Ordinal))
            data.Add(name);
        return data;
    }

    private static List<string> Unmet(ChallengeResult result) =>
        result.Requirements
            .Where(r => !r.Met)
            .Select(r => r.Label.Key())
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();


    private static ArchitectureBlueprint Starter(string slug) => ChallengeHarness.Catalog.StarterOf(slug)!;

    private static ArchitectureBlueprint Solution(string slug) => ChallengeHarness.Catalog.ReferenceSolutionOf(slug)!;

    private static ArchitectureBlueprint Copy(ArchitectureBlueprint blueprint) => new()
    {
        Name = blueprint.Name,
        Nodes = blueprint.Nodes
            .Select(n => new BlueprintNode { Name = n.Name, Type = n.Type, X = n.X, Y = n.Y, Configuration = n.Configuration })
            .ToList(),
        Connections = blueprint.Connections
            .Select(c => new BlueprintConnection { From = c.From, To = c.To, Synchronous = c.Synchronous, Configuration = c.Configuration })
            .ToList()
    };

    private static ArchitectureBlueprint Asynchronous(ArchitectureBlueprint blueprint, params (string From, string To)[] edges)
    {
        var copy = Copy(blueprint);
        foreach (var edge in copy.Connections.Where(c => edges.Contains((c.From, c.To))))
            edge.Synchronous = false;
        return copy;
    }

    private static ArchitectureBlueprint WithEdge(ArchitectureBlueprint blueprint, string from, string to, string configuration)
    {
        var copy = Copy(blueprint);
        copy.Connections.Single(c => c.From == from && c.To == to).Configuration = Json(configuration);
        return copy;
    }

    private static ArchitectureBlueprint WithoutEdges(ArchitectureBlueprint blueprint, params (string From, string To)[] edges)
    {
        var copy = Copy(blueprint);
        copy.Connections = copy.Connections.Where(c => !edges.Contains((c.From, c.To))).ToList();
        return copy;
    }

    private static ArchitectureBlueprint WithNode(ArchitectureBlueprint blueprint, string name, string patch, bool replace = false)
    {
        var copy = Copy(blueprint);
        var node = copy.Nodes.Single(n => n.Name == name);
        node.Configuration = Json(replace ? patch : Merged(node.Configuration, patch));
        return copy;
    }

    private static ArchitectureBlueprint Retyped(ArchitectureBlueprint blueprint, string name, NodeType type, string configuration)
    {
        var copy = Copy(blueprint);
        var node = copy.Nodes.Single(n => n.Name == name);
        node.Type = type;
        node.Configuration = Json(configuration);
        return copy;
    }

    private static ArchitectureBlueprint WithBalancer(ArchitectureBlueprint blueprint, bool synchronous, string edgeConfiguration)
    {
        var copy = WithoutEdges(blueprint, ("Edge", "Api"));
        copy.Nodes.Add(new BlueprintNode { Name = "Front", Type = NodeType.LoadBalancer, Configuration = Json("{}") });
        copy.Connections.Add(new BlueprintConnection { From = "Edge", To = "Front", Synchronous = true, Configuration = Json("""{"timeoutSeconds":5}""") });
        copy.Connections.Add(new BlueprintConnection { From = "Front", To = "Api", Synchronous = synchronous, Configuration = Json(edgeConfiguration) });
        return copy;
    }

    private static ArchitectureBlueprint CacheInFront(ArchitectureBlueprint blueprint, bool synchronousMiss)
    {
        var copy = WithoutEdges(blueprint, ("Storefront", "Db"));
        copy.Nodes.Add(new BlueprintNode { Name = "Cache", Type = NodeType.Cache, Configuration = Json("""{"hitRatio":0.8}""") });
        copy.Connections.Add(new BlueprintConnection { From = "Storefront", To = "Cache", Synchronous = true, Configuration = Json("""{"timeoutSeconds":5,"fallback":true}""") });
        copy.Connections.Add(new BlueprintConnection { From = "Cache", To = "Db", Synchronous = synchronousMiss, Configuration = Json("""{"timeoutSeconds":5}""") });
        return copy;
    }

    private static ArchitectureBlueprint BacklogShortcut()
    {
        var copy = WithNode(
            WithNode(Starter("backlog"), "Storefront", """{"rateLimit":200}"""),
            "Jobs", """{"deadLetterQueue":true,"maxRetries":3}""");
        copy.Nodes.Add(new BlueprintNode { Name = "Worker 2", Type = NodeType.Worker, Configuration = Json("{}") });
        copy.Connections.Add(new BlueprintConnection { From = "Jobs", To = "Worker 2", Synchronous = false, Configuration = Json("{}") });
        copy.Connections.Add(new BlueprintConnection { From = "Worker 2", To = "Db", Synchronous = true, Configuration = Json("""{"timeoutSeconds":5}""") });
        return copy;
    }

    private static ArchitectureBlueprint Stripped(ArchitectureBlueprint blueprint, params string[] fields)
    {
        var copy = Copy(blueprint);
        foreach (var node in copy.Nodes)
            node.Configuration = Json(Object(
                Fields(node.Configuration).Where(f => !fields.Contains(f.Key)).ToDictionary(f => f.Key, f => f.Value)));
        return copy;
    }

    private static ArchitectureBlueprint Rezoned(ArchitectureBlueprint blueprint, Func<string, string?> zone)
    {
        var copy = Copy(blueprint);
        foreach (var node in copy.Nodes)
        {
            var fields = Fields(node.Configuration).Where(f => f.Key != "zone").ToDictionary(f => f.Key, f => f.Value);
            if (zone(node.Name) is { } placed)
                fields["zone"] = JsonSerializer.Serialize(placed);
            node.Configuration = Json(Object(fields));
        }
        return copy;
    }

    private static string Merged(JsonElement? existing, string patch)
    {
        var fields = Fields(existing);
        foreach (var field in Fields(Json(patch)))
            fields[field.Key] = field.Value;
        return Object(fields);
    }

    private static Dictionary<string, string> Fields(JsonElement? configuration)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (configuration is { ValueKind: JsonValueKind.Object } element)
            foreach (var property in element.EnumerateObject())
                fields[property.Name] = property.Value.GetRawText();
        return fields;
    }

    private static string Object(Dictionary<string, string> fields) =>
        "{" + string.Join(",", fields.Select(f => $"{JsonSerializer.Serialize(f.Key)}:{f.Value}")) + "}";

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
