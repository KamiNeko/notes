using System.Security.Cryptography;
using System.Text;

namespace Notes.Api.Auth;

/// <summary>
/// Time-based one-time passwords (RFC 6238) as authenticator apps use them: HMAC-SHA1, 30-second
/// steps, 6 digits, a 160-bit secret shared as Base32.
/// </summary>
public static class Totp
{
    public const int Digits = 6;

    public static readonly TimeSpan Step = TimeSpan.FromSeconds(30);

    /// <summary>Steps before and after the current one that are still accepted (clock drift, slow typing).</summary>
    public const int Window = 1;

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>A new random secret.</summary>
    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);

    /// <summary>The time step a moment falls into.</summary>
    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / (long)Step.TotalSeconds;

    /// <summary>The code for one time step (RFC 4226 dynamic truncation).</summary>
    public static string Code(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(secret, counter, hash);
        var offset = hash[^1] & 0x0f;
        var value = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The step of the window around <paramref name="now"/> whose code matches, or null. Steps up to
    /// <paramref name="lastStep"/> were used already and never match again (no replay).
    /// </summary>
    public static long? Match(byte[] secret, string code, DateTimeOffset now, long lastStep)
    {
        if (code.Length != Digits || !code.All(char.IsAsciiDigit)) return null;
        var current = StepAt(now);
        long? found = null;
        // Every step is checked, so the time taken doesn't tell which one matched
        for (var step = current - Window; step <= current + Window; step++)
        {
            var match = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(secret, step)), Encoding.ASCII.GetBytes(code));
            if (match && step > lastStep) found ??= step;
        }
        return found;
    }

    /// <summary>The secret as Base32 without padding, as authenticator apps expect it.</summary>
    public static string ToBase32(byte[] data)
    {
        var result = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                result.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) result.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        return result.ToString();
    }

    /// <summary>The <c>otpauth://</c> URI that authenticator apps read from a QR code.</summary>
    public static string Uri(string issuer, string account, byte[] secret) =>
        $"otpauth://totp/{System.Uri.EscapeDataString(issuer)}:{System.Uri.EscapeDataString(account)}" +
        $"?secret={ToBase32(secret)}&issuer={System.Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={(int)Step.TotalSeconds}";
}
