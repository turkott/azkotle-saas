using System.Security.Cryptography;
using AzKotle.Application.Abstractions;

namespace AzKotle.Infrastructure.QrCodes;

public sealed class BoilerQrSlugGenerator : IBoilerQrSlugGenerator
{
    // Crockford Base32 alphabet (no I, L, O, U).
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public string Generate()
    {
        Span<char> buffer = stackalloc char[6];
        Span<byte> random = stackalloc byte[6];
        RandomNumberGenerator.Fill(random);
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = Alphabet[random[i] % Alphabet.Length];
        }

        return $"AK-{new string(buffer[..4])}-{new string(buffer[4..])}";
    }
}
