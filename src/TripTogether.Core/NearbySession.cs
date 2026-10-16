using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
namespace TripTogether.Core;
public sealed record Invitation(int Version, string Host, int Port, string Fingerprint, string Token,
    DateTimeOffset Expires, Trip Trip)
{
    public string Encode() => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this));
    public static Invitation Decode(string text)
    {
        if (text.Length > 8192) throw new InvalidDataException("Invitation too large.");
        var i = JsonSerializer.Deserialize<Invitation>(Convert.FromBase64String(text.Trim())) ?? throw new InvalidDataException("Invalid invitation.");
        if (i.Version != 1 || i.Port is < 1024 or > 65535 || !IPAddress.TryParse(i.Host, out var address) ||
            IPAddress.IsLoopback(address) || i.Expires <= DateTimeOffset.UtcNow || i.Expires > DateTimeOffset.UtcNow.AddMinutes(11) ||
            i.Fingerprint.Length != 64 || Convert.FromHexString(i.Token).Length != 32)
            throw new InvalidDataException("Invalid or expired invitation.");
        _ = Convert.FromHexString(i.Fingerprint); CalendarLog.ValidateTrip(i.Trip); return i;
    }
}
public sealed class NearbySession : IDisposable
{
    private const int Limit = 4 * 1024 * 1024;
    private readonly TcpListener listener;
    private readonly X509Certificate2 certificate;
    public Invitation Invitation { get; }
    public NearbySession(Trip trip, string host)
    {
        if (!IPAddress.TryParse(host, out _)) throw new InvalidDataException("Enter this phone's Wi-Fi IP address.");
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=TripTogether", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
        listener = new TcpListener(IPAddress.Any, 0); listener.Start(4);
        Invitation = new(1, host, ((IPEndPoint)listener.LocalEndpoint).Port,
            Convert.ToHexString(SHA256.HashData(certificate.RawData)), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            DateTimeOffset.UtcNow.AddMinutes(10), trip);
    }
    // One accepted session per invitation; no persistent pairing is implied.
    public async Task<Snapshot> ReceiveAsync(Snapshot local, CancellationToken ct)
    {
        using var expiry = CancellationTokenSource.CreateLinkedTokenSource(ct);
        expiry.CancelAfter(Invitation.Expires - DateTimeOffset.UtcNow);
        using var client = await listener.AcceptTcpClientAsync(expiry.Token);
        listener.Stop();
        using var stream = new SslStream(client.GetStream(), false);
        await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
            ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, expiry.Token);
        var token = await ReadAsync<string>(stream, expiry.Token);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(Invitation.Token)))
            throw new AuthenticationException("Invitation authentication failed.");
        var remote = await ReadAsync<Snapshot>(stream, expiry.Token);
        var validated = new CalendarLog(local.Trip); validated.Merge(local); validated.Merge(remote);
        await WriteAsync(stream, validated.Export(), expiry.Token);
        return validated.Export();
    }
    public static async Task<Snapshot> JoinAsync(Invitation invitation, Snapshot local, CancellationToken ct)
    {
        _ = Invitation.Decode(invitation.Encode());
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(45));
        using var client = new TcpClient(); await client.ConnectAsync(invitation.Host, invitation.Port, timeout.Token);
        using var stream = new SslStream(client.GetStream(), false, (_, cert, _, _) => cert is not null &&
            Convert.ToHexString(SHA256.HashData(cert.GetRawCertData())) == invitation.Fingerprint);
        await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
            TargetHost = "TripTogether", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, timeout.Token);
        await WriteAsync(stream, invitation.Token, timeout.Token); await WriteAsync(stream, local, timeout.Token);
        var result = await ReadAsync<Snapshot>(stream, timeout.Token);
        var validated = new CalendarLog(local.Trip); validated.Merge(local); validated.Merge(result); return validated.Export();
    }
    private static async Task WriteAsync<T>(Stream stream, T value, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > Limit) throw new InvalidDataException("Snapshot exceeds 4 MiB.");
        var size = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(size, bytes.Length);
        await stream.WriteAsync(size, ct); await stream.WriteAsync(bytes, ct); await stream.FlushAsync(ct);
    }
    private static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct)
    {
        var size = new byte[4]; await stream.ReadExactlyAsync(size, ct);
        int count = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(size);
        if (count is < 1 or > Limit) throw new InvalidDataException("Invalid frame length.");
        var data = new byte[count]; await stream.ReadExactlyAsync(data, ct);
        return JsonSerializer.Deserialize<T>(data) ?? throw new InvalidDataException("Empty message.");
    }
    public void Dispose() { listener.Stop(); certificate.Dispose(); }
}
