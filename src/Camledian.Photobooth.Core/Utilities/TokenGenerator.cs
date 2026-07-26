using System.Security.Cryptography;

namespace Camledian.Photobooth.Core.Utilities;

/// <summary>Cryptographically-random, URL-safe/human-friendly identifiers. Used for QR download
/// tokens (must not be guessable, spec §40) and short device pairing codes (spec §36).</summary>
public static class TokenGenerator
{
    private const string UrlSafeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
    private const string PairingAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no ambiguous chars

    /// <summary>Long random token for photo download URLs, e.g. https://fotokoutek.camledian.art/foto/{token}.
    /// 32 chars from a 58-symbol alphabet is far beyond brute-forceable (~190 bits of entropy).</summary>
    public static string CreateDownloadToken(int length = 32) => Create(length, UrlSafeAlphabet);

    /// <summary>Short human-typeable pairing code, formatted like ABCD-9217.</summary>
    public static string CreatePairingCode()
    {
        var part1 = Create(4, PairingAlphabet);
        var part2 = Create(4, PairingAlphabet);
        return $"{part1}-{part2}";
    }

    private static string Create(int length, string alphabet)
    {
        Span<char> result = length <= 64 ? stackalloc char[length] : new char[length];
        var bytes = RandomNumberGenerator.GetBytes(length);
        for (var i = 0; i < length; i++)
        {
            result[i] = alphabet[bytes[i] % alphabet.Length];
        }

        return new string(result);
    }
}
