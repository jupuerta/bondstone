using System.Text.RegularExpressions;
using bondstone_backend.Application.Architectures;
using Xunit;

namespace bondstone_backend.Application.Tests.Architectures;

public class ArchitectureTokensTests
{
    // The columns are varchar(20) and varchar(32); the tokens fill them exactly, which is what
    // let S2 land without a migration. If either length changes, the schema has to move first.
    [Fact]
    public void Share_tokens_fill_their_column_exactly() =>
        Assert.Equal(20, ArchitectureTokens.NewShareToken().Length);

    [Fact]
    public void Edit_tokens_fill_their_column_exactly() =>
        Assert.Equal(32, ArchitectureTokens.NewEditToken().Length);

    // Both live as URL path segments, so nothing outside base64url's alphabet — and never the
    // padding '=' that a byte count off a multiple of 3 would smuggle in.
    [Fact]
    public void Tokens_are_url_safe_without_padding()
    {
        var urlSafe = new Regex("^[A-Za-z0-9_-]+$");

        for (var i = 0; i < 100; i++)
        {
            Assert.Matches(urlSafe, ArchitectureTokens.NewShareToken());
            Assert.Matches(urlSafe, ArchitectureTokens.NewEditToken());
        }
    }

    [Fact]
    public void Ten_thousand_mints_never_repeat()
    {
        var seen = new HashSet<string>();

        for (var i = 0; i < 10_000; i++)
        {
            Assert.True(seen.Add(ArchitectureTokens.NewShareToken()));
            Assert.True(seen.Add(ArchitectureTokens.NewEditToken()));
        }
    }
}
