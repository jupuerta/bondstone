using System.Security.Cryptography;

namespace bondstone_backend.Application.Architectures;

// The one place tokens are minted, because the alternative was three copies of
// `Guid.NewGuid().ToString("N")` drifting apart — and because that habit had two problems the
// copies shared. `Guid.NewGuid` is documented as *not* a security primitive (that today it draws
// from a CSPRNG is an implementation detail, not a promise), and the share token kept only the
// first 10 hex characters: 40 bits, which is not capability-token territory once the rate limiter
// can be reasoned with.
//
// The widths fill the columns the schema already grants — `varchar(20)` and `varchar(32)` — so no
// migration was needed. Base64url of 15 bytes is exactly 20 characters (120 bits) and of 24 bytes
// exactly 32 (192 bits); both byte counts are multiples of 3, so there is never padding to trim.
// The alphabet (A-Za-z0-9, '-', '_') is inert in a URL path segment, which is where both tokens
// live.
//
// At these widths the unique indexes stop being a hazard and become a tripwire: a collision was
// a plausible-someday 500 at 40 bits and is a cosmic-ray event at 120, so there is deliberately
// no retry-on-collision here. Old 10-character share tokens stay valid — lookup is by equality
// and nothing rewrites stored rows.
public static class ArchitectureTokens
{
    public static string NewShareToken() => Encode(RandomNumberGenerator.GetBytes(15));

    public static string NewEditToken() => Encode(RandomNumberGenerator.GetBytes(24));

    private static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_');
}
