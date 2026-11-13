using System.Text.Json;
using bondstone_backend.Application.Analysis;
using bondstone_backend.Application.Architectures;
using bondstone_backend.Application.Architectures.Queries;
using bondstone_backend.Application.Tests.TestDoubles;
using bondstone_backend.Domain.Entities;
using Xunit;

namespace bondstone_backend.Application.Tests.Architectures.Queries;

public class GetArchitectureHistoryQueryHandlerTests
{
    private const string Scoring = "v4";

    private readonly FakeArchitectureRepository _repository = new();
    private readonly GetArchitectureHistoryQueryHandler _handler;
    private readonly Architecture _architecture;

    public GetArchitectureHistoryQueryHandlerTests()
    {
        _handler = new GetArchitectureHistoryQueryHandler(_repository);
        _architecture = new Architecture
        {
            Id = Guid.NewGuid(),
            Name = "Arquitectura",
            ShareToken = "share-token",
            EditToken = "edit-token",
        };
        _repository.Seed(_architecture);
    }

    private void Record(
        int version,
        int? score,
        string scoringVersion = Scoring,
        string? summary = null,
        string? areas = null)
        => _repository.Snapshots.Add(new ArchitectureSnapshot
        {
            Id = Guid.NewGuid(),
            ArchitectureId = _architecture.Id,
            Version = version,
            ScoringVersion = scoringVersion,
            Score = score,
            Summary = summary ?? JsonSerializer.Serialize(new AnalysisSummary(1, 2, 0, 9)),
            Areas = areas ?? SnapshotAreas.Empty,
            CreatedAt = new DateTime(2026, 8, 22, 0, 0, 0, DateTimeKind.Utc).AddMinutes(version),
        });

    private Task<IReadOnlyList<ArchitectureSnapshotDto>?> Handle()
        => _handler.Handle(new GetArchitectureHistoryQuery { ShareToken = "share-token" });

    [Fact]
    public async Task Handle_UnknownShareToken_ReturnsNull()
    {
        var result = await _handler.Handle(new GetArchitectureHistoryQuery { ShareToken = "nope" });

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_NoSnapshots_ReturnsEmpty()
    {
        Assert.Empty((await Handle())!);
    }

    [Fact]
    public async Task Handle_RunsOfTheSameScore_CollapseToTheVersionThatChangedIt()
    {
        Record(1, 61);
        Record(2, 61);
        Record(3, 61);
        Record(4, 72);
        Record(5, 72);
        Record(6, 72);
        Record(7, 78);
        Record(8, 78);
        Record(9, 78);

        var result = (await Handle())!;

        Assert.Equal([1, 4, 7, 9], result.Select(s => s.Version));
        Assert.Equal([61, 72, 78, 78], result.Select(s => s.Score));
    }

    [Fact]
    public async Task Handle_LastSnapshot_IsAlwaysAPointEvenWhenItChangedNothing()
    {
        Record(1, 61);
        Record(2, 78);
        for (var version = 3; version <= 20; version++)
            Record(version, 78);

        var result = (await Handle())!;

        Assert.Equal([1, 2, 20], result.Select(s => s.Version));
    }

    [Fact]
    public async Task Handle_SingleSnapshot_IsBothEndsAndAppearsOnce()
    {
        Record(1, 61);

        var result = (await Handle())!;

        Assert.Equal(1, Assert.Single(result).Version);
    }

    [Fact]
    public async Task Handle_ScoringVersionChange_IsAPointEvenAtTheSameScore()
    {
        Record(1, 72, "v3");
        Record(2, 72, "v3");
        Record(3, 72, "v4");
        Record(4, 72, "v4");

        var result = (await Handle())!;

        Assert.Equal([1, 3, 4], result.Select(s => s.Version));
        Assert.Equal(["v3", "v4", "v4"], result.Select(s => s.ScoringVersion));
    }

    [Fact]
    public async Task Handle_UnscoredVersion_IsAChangeAndStaysNull()
    {
        Record(1, 61);
        Record(2, null);
        Record(3, null);
        Record(4, 67);

        var result = (await Handle())!;

        Assert.Equal([1, 2, 4], result.Select(s => s.Version));
        Assert.Null(result[1].Score);
    }

    [Fact]
    public async Task Handle_Summary_TravelsWithEachPoint()
    {
        Record(1, 61, summary: JsonSerializer.Serialize(new AnalysisSummary(2, 4, 0, 6)));
        Record(2, 78, summary: JsonSerializer.Serialize(new AnalysisSummary(1, 2, 0, 9)));

        var result = (await Handle())!;

        Assert.Equal(new AnalysisSummary(2, 4, 0, 6), result[0].Summary);
        Assert.Equal(new AnalysisSummary(1, 2, 0, 9), result[1].Summary);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("")]
    [InlineData("no soy json")]
    public async Task Handle_SummaryThatSaysNothing_IsNullAndNotThreeZeroes(string stored)
    {
        Record(1, 61, summary: stored);

        var result = (await Handle())!;

        Assert.Null(Assert.Single(result).Summary);
    }

    [Fact]
    public async Task Handle_MoreChangesThanTheCap_KeepsTheFirstAndTheMostRecent()
    {
        for (var version = 1; version <= 200; version++)
            Record(version, version % 100);

        var result = (await Handle())!;

        Assert.Equal(GetArchitectureHistoryQueryHandler.MaxPoints, result.Count);
        Assert.Equal(1, result[0].Version);
        Assert.Equal(200, result[^1].Version);
    }

    [Fact]
    public async Task Handle_HistoryLongerThanTheOldReadCap_StillEndsAtTheCurrentVersion()
    {
        Record(1, 61);
        for (var version = 2; version <= 150; version++)
            Record(version, 61);
        Record(151, 78);

        var result = (await Handle())!;

        Assert.Equal(151, result[^1].Version);
        Assert.Equal(78, result[^1].Score);
    }


    [Fact]
    public async Task Handle_Areas_TravelWithEachPoint()
    {
        Record(1, 61, areas: SnapshotAreas.Write([
            Scored(Area.Availability, 70),
            Scored(Area.Resilience, 52),
        ]));
        Record(2, 78, areas: SnapshotAreas.Write([
            Scored(Area.Availability, 70),
            Scored(Area.Resilience, 86),
        ]));

        var result = (await Handle())!;

        Assert.Equal(52, result[0].Areas![Area.Resilience]);
        Assert.Equal(86, result[1].Areas![Area.Resilience]);
        Assert.Equal(70, result[0].Areas![Area.Availability]);
        Assert.Equal(70, result[1].Areas![Area.Availability]);
    }

    [Fact]
    public async Task Handle_UnscoredArea_IsAbsentAndNotAZero()
    {
        Record(1, 61, areas: SnapshotAreas.Write([
            Scored(Area.Availability, 70),
            new AreaScore(Area.Observability, null, AreaState.NoRules),
            new AreaScore(Area.Security, null, AreaState.NotApplicable),
        ]));

        var areas = Assert.Single((await Handle())!).Areas!;

        Assert.Equal([Area.Availability], areas.Keys);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("")]
    [InlineData("no soy json")]
    public async Task Handle_AreasThatSayNothing_IsNullAndNotSixZeroes(string stored)
    {
        Record(1, 61, areas: stored);

        Assert.Null(Assert.Single((await Handle())!).Areas);
    }

    [Fact]
    public async Task Handle_AreaNames_AreWhatIsStored()
    {
        Record(1, 61, areas: SnapshotAreas.Write([Scored(Area.Coupling, 74)]));

        Assert.Contains("\"Coupling\":74", _repository.Snapshots[0].Areas);
        Assert.Equal(74, Assert.Single((await Handle())!).Areas![Area.Coupling]);
    }

    private static AreaScore Scored(Area area, int score) => new(area, score, AreaState.Scored);
}
