using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
namespace TripTogether.Core;

public sealed record JoinRequest(int Version, string Token, Guid DeviceId, string DeviceName, Guid TripId);
public sealed record JoinDecision(bool Accepted, string Message);
public sealed record TransferReply(bool Success, string Message, Snapshot? Snapshot);
public sealed record TransferReceipt(bool Saved);
public enum HostStage { Waiting, ApprovalRequested, Sharing, Joined, Failed }
public sealed record HostUpdate(HostStage Stage, string Message);

public sealed class NearbySession : IDisposable
{
    private const int Limit = 4 * 1024 * 1024;
    private readonly TcpListener listener;
    private readonly X509Certificate2 certificate;
    private bool started;
    public Invitation Invitation { get; }

    public NearbySession(Trip trip, string host)
    {
        CalendarLog.ValidateTrip(trip);
        if (!Invitation.IsUsableAddress(host))
            throw new InvalidDataException("Enter this phone's Wi-Fi IPv4 address, such as 192.168.1.12.");
        // ECDSA is supported by the native TLS stacks on iOS and Android.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=TripTogether", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
        listener = new TcpListener(IPAddress.Parse(host), 0);
        try { listener.Start(8); }
        catch { certificate.Dispose(); throw; }
        Invitation = new(Invitation.CurrentVersion, host, ((IPEndPoint)listener.LocalEndpoint).Port,
            Convert.ToHexString(SHA256.HashData(certificate.RawData)),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), DateTimeOffset.UtcNow.AddMinutes(10), trip);
    }

    // Handles one peer at a time. Invalid/declined/failed peers never terminate hosting.
    public async Task HostAsync(
        Func<JoinRequest, CancellationToken, Task<bool>> approve,
        Func<Snapshot, CancellationToken, Task<Snapshot>> mergeAndSave,
        Action<HostUpdate>? progress,
        CancellationToken ct)
    {
        if (started) throw new InvalidOperationException("This host is already running.");
        started = true;
        using var expiry = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var remaining = Invitation.Expires - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) return;
        expiry.CancelAfter(remaining);
        try
        {
            while (!expiry.IsCancellationRequested)
            {
                progress?.Invoke(new(HostStage.Waiting, "Hosting. Send an invitation; keep this app open."));
                using var client = await listener.AcceptTcpClientAsync(expiry.Token);
                using var peerTimeout = CancellationTokenSource.CreateLinkedTokenSource(expiry.Token);
                peerTimeout.CancelAfter(TimeSpan.FromSeconds(90));
                try
                {
                    using var stream = new SslStream(client.GetStream(), false);
                    await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = certificate,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                    }, peerTimeout.Token);
                    var hello = await ReadAsync<JoinRequest>(stream, peerTimeout.Token);
                    ValidateHello(hello);
                    progress?.Invoke(new(HostStage.ApprovalRequested, $"{hello.DeviceName} wants to join."));
                    var accepted = await approve(hello, peerTimeout.Token);
                    await WriteAsync(stream, new JoinDecision(accepted,
                        accepted ? "Host accepted your request." : "The host declined this request."), peerTimeout.Token);
                    if (!accepted) continue;
                    progress?.Invoke(new(HostStage.Sharing, $"Sharing with {hello.DeviceName}…"));
                    var remote = await ReadAsync<Snapshot>(stream, peerTimeout.Token);
                    var validated = new CalendarLog(Invitation.Trip); validated.Merge(remote);
                    // Persist on the host BEFORE claiming success or returning merged data.
                    var merged = await mergeAndSave(validated.Export(), peerTimeout.Token);
                    var output = new CalendarLog(Invitation.Trip); output.Merge(merged);
                    await WriteAsync(stream, new TransferReply(true, "Trip synchronized.", output.Export()), peerTimeout.Token);
                    var receipt = await ReadAsync<TransferReceipt>(stream, peerTimeout.Token);
                    if (!receipt.Saved) throw new IOException("The guest did not save the trip.");
                    progress?.Invoke(new(HostStage.Joined, $"{hello.DeviceName} joined and saved the trip."));
                }
                catch (OperationCanceledException) when (expiry.IsCancellationRequested) { break; }
                catch (Exception ex) when (ex is IOException or AuthenticationException or InvalidDataException or JsonException or FormatException or ArgumentException or OperationCanceledException or SocketException or TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                    progress?.Invoke(new(HostStage.Failed, "A join failed or timed out. Hosting is still available; the guest can retry."));
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        finally { listener.Stop(); }
        ct.ThrowIfCancellationRequested();
    }

    private void ValidateHello(JoinRequest hello)
    {
        if (hello.Version != Invitation.CurrentVersion || hello.TripId != Invitation.Trip.Id ||
            hello.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(hello.DeviceName) || hello.DeviceName.Length > 80 ||
            hello.DeviceName.Any(char.IsControl) || hello.Token is null || hello.Token.Length != 64)
            throw new InvalidDataException("Invalid join request.");
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hello.Token), Convert.FromHexString(Invitation.Token)))
            throw new AuthenticationException("Invitation authentication failed.");
        Invitation.Validate();
    }

    // Success is returned only after local persistence completes; the host receives a receipt.
    public static async Task JoinAsync(Invitation invitation, Snapshot local, Guid deviceId, string deviceName,
        Func<Snapshot, CancellationToken, Task> save, Action<string>? progress, CancellationToken ct)
    {
        invitation.Validate();
        if (deviceId == Guid.Empty || string.IsNullOrWhiteSpace(deviceName) || deviceName.Length > 80 || deviceName.Any(char.IsControl))
            throw new InvalidDataException("Enter a device name of 1–80 characters.");
        var log = new CalendarLog(invitation.Trip); log.Merge(local);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        using var client = new TcpClient();
        progress?.Invoke("Connecting to the host…");
        await client.ConnectAsync(invitation.Host, invitation.Port, timeout.Token);
        using var stream = new SslStream(client.GetStream(), false, (_, cert, _, _) => cert is not null &&
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(cert.GetRawCertData()), Convert.FromHexString(invitation.Fingerprint)));
        await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "TripTogether", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        }, timeout.Token);
        await WriteAsync(stream, new JoinRequest(Invitation.CurrentVersion, invitation.Token, deviceId, deviceName, invitation.Trip.Id), timeout.Token);
        progress?.Invoke("Waiting for the host to approve your join…");
        var decision = await ReadAsync<JoinDecision>(stream, timeout.Token);
        if (!decision.Accepted) throw new InvalidDataException("The host declined your join request.");
        progress?.Invoke("Host accepted. Synchronizing trip…");
        await WriteAsync(stream, log.Export(), timeout.Token);
        var reply = await ReadAsync<TransferReply>(stream, timeout.Token);
        if (!reply.Success || reply.Snapshot is null) throw new InvalidDataException("The host could not synchronize the trip.");
        log.Merge(reply.Snapshot);
        await save(log.Export(), timeout.Token);
        // If acknowledgment is interrupted, data is safe locally; a new join is idempotent.
        await WriteAsync(stream, new TransferReceipt(true), timeout.Token);
        progress?.Invoke("Joined. The trip is saved on this phone.");
    }

    private static async Task WriteAsync<T>(Stream stream, T value, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > Limit) throw new InvalidDataException("Snapshot exceeds 4 MiB.");
        var size = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(size, bytes.Length);
        await stream.WriteAsync(size, ct); await stream.WriteAsync(bytes, ct); await stream.FlushAsync(ct);
    }
    private static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct)
    {
        var size = new byte[4]; await stream.ReadExactlyAsync(size, ct);
        int count = BinaryPrimitives.ReadInt32BigEndian(size);
        if (count is < 1 or > Limit) throw new InvalidDataException("Invalid frame length.");
        var data = new byte[count]; await stream.ReadExactlyAsync(data, ct);
        return JsonSerializer.Deserialize<T>(data) ?? throw new InvalidDataException("Empty message.");
    }
    public void Dispose() { listener.Stop(); certificate.Dispose(); }
}
