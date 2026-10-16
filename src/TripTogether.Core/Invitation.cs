using System.Net;
using System.Net.Sockets;
using System.Text.Json;
namespace TripTogether.Core;

public sealed record Invitation(int Version, string Host, int Port, string Fingerprint, string Token,
    DateTimeOffset Expires, Trip Trip)
{
    public const int CurrentVersion = 2;
    public string Encode() => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this));
    public string ToLink() => "triptogether://join/" + Encode().TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Accept either the entire link or a legacy-style raw encoded invitation.
    public static Invitation Decode(string text)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 16384)
                throw new InvalidDataException("The invitation is empty or too large.");
            var payload = text.Trim();
            if (payload.Contains("://", StringComparison.Ordinal))
            {
                if (!Uri.TryCreate(payload, UriKind.Absolute, out var uri) || uri.Scheme != "triptogether" ||
                    uri.Host != "join" || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                    uri.UserInfo.Length != 0 || !uri.IsDefaultPort)
                    throw new InvalidDataException("This is not a TripTogether invitation link.");
                payload = uri.AbsolutePath.TrimStart('/');
                if (payload.Contains('/')) throw new InvalidDataException("Invalid invitation path.");
                payload = payload.Replace('-', '+').Replace('_', '/');
                payload += new string('=', (4 - payload.Length % 4) % 4);
            }
            var value = JsonSerializer.Deserialize<Invitation>(Convert.FromBase64String(payload))
                ?? throw new InvalidDataException("Invalid invitation.");
            value.Validate();
            return value;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidDataException("The invitation is malformed. Ask the host to send a new one.", ex);
        }
    }

    public void Validate()
    {
        if (Version != CurrentVersion)
            throw new InvalidDataException("This invitation uses another app version. Update both phones and create a new invitation.");
        if (Port is < 1024 or > 65535 || !IsUsableAddress(Host) || Fingerprint is null || Token is null ||
            Fingerprint.Length != 64 || Token.Length != 64 || Trip is null)
            throw new InvalidDataException("Invalid invitation details.");
        if (Expires <= DateTimeOffset.UtcNow)
            throw new InvalidDataException("This invitation has expired. Ask the host for a new one.");
        if (Expires > DateTimeOffset.UtcNow.AddMinutes(11))
            throw new InvalidDataException("Invalid invitation expiration.");
        _ = Convert.FromHexString(Fingerprint); _ = Convert.FromHexString(Token);
        CalendarLog.ValidateTrip(Trip);
    }

    public static bool IsUsableAddress(string? host)
    {
        if (!IPAddress.TryParse(host, out var address) || address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] is > 0 and < 224 && !address.Equals(IPAddress.Broadcast);
    }
}
