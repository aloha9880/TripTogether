namespace TripTogether.App.Services;

// A bounded in-memory handoff: native URL activation can precede the MAUI window.
// All access occurs on the UI thread. Never log links: they carry bearer tokens.
public static class InvitationInbox
{
    public static string? Pending { get; private set; }
    public static event Action? Changed;
    public static void Receive(string? link)
    {
        if (string.IsNullOrWhiteSpace(link) || link.Length > 16384 ||
            !link.StartsWith("triptogether://join/", StringComparison.OrdinalIgnoreCase)) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Pending == link) return;
            Pending = link;
            Changed?.Invoke();
        });
    }
    public static void Clear() { Pending = null; }
}
