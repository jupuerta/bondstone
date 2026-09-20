using bondstone_backend.Application.Analysis.Rules;
using bondstone_backend.Application.Localization;
using bondstone_backend.Application.Tests.Analysis.Rules.Support;
using bondstone_backend.Application.Tests.TestDoubles;
using bondstone_backend.Domain.Enums;
using Xunit;
using Xunit.Abstractions;

namespace bondstone_backend.Application.Tests.Analysis;

public class B1ReproTests
{
    private readonly ITestOutputHelper _out;
    public B1ReproTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void T1a_T1b()
    {
        var t1a = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Svc", NodeType.Service)
            .Node("Db", NodeType.Database, """{"readReplicas": 2}""")
            .Connect("Api", "Svc", timeoutSeconds: 5, fallback: true)
            .Connect("Svc", "Db", timeoutSeconds: 5, fallback: true);

        foreach (var f in new SpofRule().Evaluate(t1a.Build()).Findings)
            _out.WriteLine($"T1a: [{f.Severity}] {f.Message.Describe()}");

        var t1b = new GraphBuilder()
            .Node("Api", NodeType.Api)
            .Node("Svc", NodeType.Service)
            .Node("Db", NodeType.Database, """{"readReplicas": 2}""")
            .Node("Obs", NodeType.Observability)
            .Connect("Api", "Svc", timeoutSeconds: 5, fallback: true)
            .Connect("Svc", "Db", timeoutSeconds: 5, fallback: true)
            .Connect("Obs", "Api", synchronous: false)
            .Connect("Obs", "Svc", synchronous: false)
            .Connect("Obs", "Db", synchronous: false);

        var findings = new SpofRule().Evaluate(t1b.Build()).Findings;
        _out.WriteLine($"T1b findings: {findings.Count}");
        foreach (var f in findings)
            _out.WriteLine($"T1b: [{f.Severity}] {f.Message.Describe()} / {f.Explanation.Key}");
    }
}
