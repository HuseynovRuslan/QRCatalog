using System.Security.Cryptography;

namespace QrCatalog.Infrastructure.Qr;

/// <summary>
/// URL-dəki opak token: kriptoqrafik təsadüfi base62, 11 simvol (~65 bit entropiya).
/// Ardıcıl deyil — kimsə SZ-0142-dən SZ-0143-ü tapa bildiyi kimi token gəzə bilməz.
/// </summary>
public static class TokenGenerator
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    public const int Length = 11;

    public static string Next()
    {
        Span<char> result = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
            result[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(result);
    }
}
