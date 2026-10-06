using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using ResourceReservation.Application.Common.Exceptions;

namespace ResourceReservation.Application.Reservations;

internal static class IdempotencyKeys
{
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    private static readonly Regex ValidKey = new("^[A-Za-z0-9._:-]{8,100}$", RegexOptions.Compiled);

    public static string Validate(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new BadRequestException("The Idempotency-Key header is required.");

        key = key.Trim();
        if (!ValidKey.IsMatch(key))
            throw new BadRequestException(
                "The Idempotency-Key must be 8-100 characters: letters, digits, '.', '_', ':' or '-'.");

        return key;
    }

    // Hash of the NORMALIZED request (UTC ticks), not the raw body: equivalent requests hash equally.
    public static string Hash(Guid resourceId, DateTime startUtc, DateTime endUtc)
    {
        var canonical = $"{resourceId:N}|{startUtc.Ticks}|{endUtc.Ticks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
