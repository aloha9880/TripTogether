using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
namespace TripTogether.Core;
public sealed record LocalAddress(string Address, string InterfaceName)
{
    public override string ToString() => $"{Address} ({InterfaceName})";
}
public static class LocalAddresses
{
    public static LocalAddress[] GetAvailable()
    {
        try { return FromInterfaces(); }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException)
        {
            // Some sandboxed platforms restrict interface enumeration. The caller can
            // still offer manual entry if hostname resolution also isn't available.
            return Dns.GetHostAddresses(Dns.GetHostName())
                .Where(a => !IPAddress.IsLoopback(a) && Invitation.IsUsableAddress(a.ToString()))
                .Select(a => new LocalAddress(a.ToString(), "local host"))
                .DistinctBy(a => a.Address).ToArray();
        }
    }
    private static LocalAddress[] FromInterfaces() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .OrderBy(n => n.Name is "en0" or "wlan0" ? 0 : n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 1 : 2)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address) && Invitation.IsUsableAddress(a.Address.ToString()))
            .Select(a => new LocalAddress(a.Address.ToString(), n.Name)))
        .DistinctBy(a => a.Address).ToArray();
}
