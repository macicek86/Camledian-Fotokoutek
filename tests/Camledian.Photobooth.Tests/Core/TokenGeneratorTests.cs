using System.Text.RegularExpressions;
using Camledian.Photobooth.Core.Utilities;

namespace Camledian.Photobooth.Tests.Core;

public partial class TokenGeneratorTests
{
    [Fact]
    public void DownloadTokenHasDefaultLengthOf32()
    {
        var token = TokenGenerator.CreateDownloadToken();
        Assert.Equal(32, token.Length);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(64)]
    public void DownloadTokenRespectsRequestedLength(int length)
    {
        var token = TokenGenerator.CreateDownloadToken(length);
        Assert.Equal(length, token.Length);
    }

    [Fact]
    public void DownloadTokensAreNotGuessablyRepeated()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => TokenGenerator.CreateDownloadToken()).ToHashSet();
        Assert.Equal(200, tokens.Count);
    }

    [Fact]
    public void DownloadTokenOnlyContainsUrlSafeCharacters()
    {
        var token = TokenGenerator.CreateDownloadToken(200);
        Assert.Matches(UrlSafeTokenPattern(), token);
    }

    [Fact]
    public void PairingCodeMatchesAbcd1234Format()
    {
        var code = TokenGenerator.CreatePairingCode();
        Assert.Matches(PairingCodePattern(), code);
    }

    [Fact]
    public void PairingCodesAreReasonablyUnique()
    {
        var codes = Enumerable.Range(0, 100).Select(_ => TokenGenerator.CreatePairingCode()).ToHashSet();
        Assert.True(codes.Count > 95, "expected near-100% uniqueness across 100 generated pairing codes");
    }

    [GeneratedRegex("^[A-Za-z0-9]+$")]
    private static partial Regex UrlSafeTokenPattern();

    [GeneratedRegex("^[A-Z0-9]{4}-[A-Z0-9]{4}$")]
    private static partial Regex PairingCodePattern();
}
