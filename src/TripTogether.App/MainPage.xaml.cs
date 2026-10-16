using TripTogether.Core;
using TripTogether.App.Services;
namespace TripTogether.App;
public partial class MainPage : ContentPage
{
 private readonly TripStore store;
 private CalendarLog? current;
 private Guid deviceId;
 private CancellationTokenSource? sharing;
 private bool busy;
 private bool initialized;
 private bool refreshing;
 private bool acceptingInvitation;
 private NearbySession? hostSession;
 private Invitation? incomingInvitation;
 private int joinedCount;
 private string? notifiedInvitation;
 public MainPage(TripStore store)
 {
  InitializeComponent(); this.store=store;
  InvitationInbox.Changed += ShowPendingInvitation;
 }
 protected override async void OnAppearing()
 {
  base.OnAppearing();
  await Run(async () =>
  {
   if (!initialized)
   {
    deviceId = await store.DeviceIdAsync();
    DeviceName.Text = Preferences.Default.Get("device-name", Microsoft.Maui.Devices.DeviceInfo.Current.Name);
    initialized = true;
    await Refresh();
    try { HostAddress.Text = LocalAddresses.GetAvailable().FirstOrDefault()?.Address; }
    catch (Exception ex) when (ex is System.Net.NetworkInformation.NetworkInformationException or PlatformNotSupportedException)
    { Status.Text = "Enter your Wi-Fi IP address manually."; }
   }
   ShowPendingInvitation();
  });
 }
 private async Task Refresh(Guid? select = null)
 {
  var snapshots = await store.ReadAsync();
  var wanted = select ?? current?.Trip.Id;
  refreshing = true;
  try
  {
   TripPicker.ItemsSource = snapshots.Select(x => x.Trip).ToArray();
   var index = Array.FindIndex(snapshots, x => x.Trip.Id == wanted);
   TripPicker.SelectedIndex = index >= 0 ? index : snapshots.Length > 0 ? 0 : -1;
   current = TripPicker.SelectedIndex >= 0 ? new CalendarLog(snapshots[TripPicker.SelectedIndex].Trip) : null;
   if (current is not null) current.Merge(snapshots[TripPicker.SelectedIndex]);
  }
  finally { refreshing = false; }
  Render();
 }
 private async void TripChanged(object? sender, EventArgs e)
 {
  if (refreshing || busy || TripPicker.SelectedItem is not Trip trip) return;
  await Run(async () =>
  {
   var saved = (await store.ReadAsync()).Single(x => x.Trip.Id == trip.Id);
   // Ignore a stale asynchronous selection completion.
   if (busy || (TripPicker.SelectedItem as Trip)?.Id != trip.Id) return;
   current = new CalendarLog(trip); current.Merge(saved); Render();
  });
 }
 private void Render()
 {
  Events.Children.Clear(); if(current is null) return;
  foreach(var id in current.EventIds)
  {
   var heads=current.Heads(id); if(heads.Length==1 && heads[0].Data is null) continue;
   var panel=new VerticalStackLayout {Padding=12,BackgroundColor=Colors.White,Spacing=8};
   if(heads.Length>1) panel.Children.Add(new Label {Text="Conflicting changes — choose a version",FontAttributes=FontAttributes.Bold});
   foreach(var head in heads)
   {
    var data=head.Data;
    panel.Children.Add(new Label {Text=data is null ? "Deleted version" : $"{data.Title}\n{(data.AllDay ? $"{data.FirstDate} (all day)" : data.Start.ToString("f"))}\n{data.Location}",FontSize=16});
    if(heads.Length>1) {var choose=new Button {Text=data is null ? "Resolve as deleted" : "Keep this version"}; choose.Clicked+=async(_,_)=>await Run(async()=> {RequireEditable(); current!.Edit(id,deviceId,data); await Save();});panel.Children.Add(choose);}
   }
   if(heads.Length==1)
   {
    var edit=new Button {Text="Edit"};edit.Clicked+=async(_,_)=>await Run(async()=>await EditEvent(id,heads[0].Data));panel.Children.Add(edit);
    var delete=new Button {Text="Delete"};delete.Clicked+=async(_,_)=>await Run(async()=> {RequireEditable();if(await DisplayAlertAsync("Delete event?","This deletion will be shared in your next session.","Delete","Cancel")){current!.Edit(id,deviceId,null);await Save();}});panel.Children.Add(delete);
   }
   Events.Children.Add(panel);
  }
  if(Events.Children.Count==0) Events.Children.Add(new Label {Text="No events yet. Add your first stop."});
 }
 private void RequireEditable() {if(!initialized)throw new InvalidOperationException("The app is still loading.");if(busy)throw new InvalidOperationException("Cancel or finish sharing before editing.");if(current is null)throw new InvalidOperationException("Choose or create a trip first.");}
 private async Task Save() { await store.SaveAsync(current!.Export()); Render(); }
 private async void CreateTrip(object? sender,EventArgs e)=>await Run(async()=>
 {
  if(busy)throw new InvalidOperationException("Finish sharing first.");
  var name=await DisplayPromptAsync("New trip","Trip name",maxLength:200);if(string.IsNullOrWhiteSpace(name))return;
  var zone=await DisplayPromptAsync("Trip time zone","IANA time zone",initialValue:TimeZoneInfo.Local.Id);if(zone is null)return;
  current=new CalendarLog(new(Guid.NewGuid(),name.Trim(),zone));await Save();await Refresh(current.Trip.Id);
 });
 private async void AddEvent(object? sender,EventArgs e)=>await Run(async()=>await EditEvent(Guid.NewGuid(),null));
 private async Task EditEvent(Guid id,EventData? old)
 {
  RequireEditable(); var title=await DisplayPromptAsync("Event","Title",initialValue:old?.Title,maxLength:200);if(title is null)return;
  var allDay=await DisplayAlertAsync("Event type","Is this an all-day event?","All day","Timed");
        var zoneId = current!.Trip.TimeZoneId;
        var defaultStart = old?.Start ?? DateTimeOffset.Now;

        var start = await PickDateTimeAsync(
            "Start",
            allDay,
            old?.FirstDate,
            defaultStart,
            zoneId);

        if (start is null)
            return;

        var end = await PickDateTimeAsync(
            allDay ? "End date (exclusive)" : "End",
            allDay,
            old?.EndDateExclusive ?? DateOnly.ParseExact(start[..10], "yyyy-MM-dd")
                .AddDays(1),
            old?.End ?? defaultStart.AddHours(1),
            zoneId);

        if (end is null)
            return;
  var location=await DisplayPromptAsync("Location","Where?",initialValue:old?.Location,maxLength:1000);if(location is null)return;
  var notes=await DisplayPromptAsync("Notes","Optional notes",initialValue:old?.Notes,maxLength:10000);if(notes is null)return;
  var data=new EventData(title.Trim(),notes,location,allDay?DateTimeOffset.UnixEpoch:DateTimeOffset.ParseExact(start,"yyyy-MM-ddTHH:mm:sszzz",System.Globalization.CultureInfo.InvariantCulture),allDay?DateTimeOffset.UnixEpoch:DateTimeOffset.ParseExact(end,"yyyy-MM-ddTHH:mm:sszzz",System.Globalization.CultureInfo.InvariantCulture),current!.Trip.TimeZoneId,allDay,allDay?DateOnly.ParseExact(start,"yyyy-MM-dd"):null,allDay?DateOnly.ParseExact(end,"yyyy-MM-dd"):null);
  RequireEditable(); current.Edit(id,deviceId,data);await Save();
 }

 private void SetBusy(bool value)
 {
  busy = value;
  TripControls.IsEnabled = !value;
  HostAddress.IsEnabled = DeviceName.IsEnabled = DetectAddressButton.IsEnabled = HostButton.IsEnabled = JoinButton.IsEnabled = !value;
  AcceptInvitationButton.IsEnabled = !value;
  CancelButton.IsEnabled = value;
  ShareInvitationButton.IsEnabled = hostSession is not null;
  InvitationText.IsReadOnly = hostSession is not null;
 }
 private async void DetectAddress(object? sender, EventArgs e) => await Run(DetectAddressAsync);
 private async Task DetectAddressAsync()
 {
  var addresses = LocalAddresses.GetAvailable();
  if (addresses.Length == 0) throw new InvalidOperationException("No Wi-Fi IPv4 address found. Connect to Wi-Fi or enter the address from Settings.");
  if (addresses.Length == 1) { HostAddress.Text = addresses[0].Address; return; }
  var chosen = await DisplayActionSheetAsync("Select this phone's Wi-Fi interface", "Cancel", null, addresses.Select(x => x.ToString()).ToArray());
  var selected = addresses.FirstOrDefault(x => x.ToString() == chosen);
  if (selected is not null) HostAddress.Text = selected.Address;
 }
 private async void Host(object? sender, EventArgs e) => await Run(async () =>
 {
  RequireEditable();
  if (string.IsNullOrWhiteSpace(HostAddress.Text)) await DetectAddressAsync();
  if (string.IsNullOrWhiteSpace(HostAddress.Text)) return;
  var log = current!;
  var session = new NearbySession(log.Trip, HostAddress.Text.Trim());
  hostSession = session;
  sharing = new CancellationTokenSource();
  joinedCount = 0;
  SetBusy(true);
  InvitationText.Text = session.Invitation.ToLink();
  HostingDetails.Text = $"Hosting {log.Trip.Name} at {session.Invitation.Host}:{session.Invitation.Port}. Expires {session.Invitation.Expires.ToLocalTime():t}.";
  HostingDetails.IsVisible = true;
  Status.Text = "Hosting started. Tap Send invitation to choose a recipient.";
  try
  {
   await session.HostAsync(
    (request, ct) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
     if (ct.IsCancellationRequested) return false;
     var accepted = await DisplayAlertAsync("Join request", $"{request.DeviceName} wants to join {log.Trip.Name}.\n\nAccepting sends this trip and accepts their changes. The device name is self-reported; verify the person with you.", "Allow join", "Decline");
     return accepted && !ct.IsCancellationRequested;
    }).WaitAsync(ct),
    async (remote, ct) =>
    {
     ct.ThrowIfCancellationRequested();
     // Build a separate candidate so failed persistence never mutates the live log.
     var candidate = new CalendarLog(log.Trip); candidate.Merge(log.Export()); candidate.Merge(remote);
     await store.SaveAsync(candidate.Export());
     log.Merge(candidate.Export());
     await MainThread.InvokeOnMainThreadAsync(Render);
     return log.Export();
    },
    update => MainThread.BeginInvokeOnMainThread(() =>
    {
     if (!ReferenceEquals(hostSession, session)) return;
     Status.Text = update.Message;
     if (update.Stage == HostStage.Failed) LastJoin.Text = update.Message;
     if (update.Stage == HostStage.Joined)
     {
      joinedCount++;
      LastJoin.Text = $"{update.Message} {DateTime.Now:t} · {joinedCount} completed join(s).";
      SemanticScreenReader.Default.Announce(update.Message);
     }
    }), sharing.Token);
   Status.Text = "Hosting invitation expired. Start hosting again for a new invitation.";
  }
  finally
  {
   session.Dispose(); hostSession = null;
   sharing.Dispose(); sharing = null;
   InvitationText.Text = ""; HostingDetails.IsVisible = false;
   SetBusy(false); ShowPendingInvitation();
  }
 });
 private async void ShareInvitation(object? sender, EventArgs e) => await Run(async () =>
 {
  var invitation = hostSession?.Invitation ?? throw new InvalidOperationException("Start hosting before sending an invitation.");
  invitation.Validate();
  await Share.Default.RequestAsync(new ShareTextRequest
  {
   Title = "Invite someone to your trip",
   Text = $"Join my TripTogether trip: {invitation.Trip.Name}. Connect to the same Wi-Fi/hotspot, open the link, and request to join. The invitation expires in 10 minutes. If the link isn't tappable, copy it into TripTogether.",
   Uri = invitation.ToLink()
  });
  // A share-sheet return means neither message delivery nor acceptance is confirmed.
  if (hostSession is not null) Status.Text = "Return to TripTogether and keep it open. Waiting for a join request; delivery is not confirmed.";
 });
 private void ShowPendingInvitation()
 {
  if (!initialized || InvitationInbox.Pending is not string text) return;
  try
  {
   incomingInvitation = Invitation.Decode(text);
   if (notifiedInvitation != incomingInvitation.Token)
   {
    notifiedInvitation = incomingInvitation.Token;
    SemanticScreenReader.Default.Announce($"Invitation received for {incomingInvitation.Trip.Name}. Review or decline the invitation.");
   }
   IncomingDetails.Text = $"{incomingInvitation.Trip.Name}\nHost: {incomingInvitation.Host}\nExpires {incomingInvitation.Expires.ToLocalTime():t}" +
    (busy ? "\nStop the current sharing session before joining." : "\nReview and accept to send your existing changes for this trip.");
   IncomingPanel.IsVisible = true;
   AcceptInvitationButton.IsEnabled = !busy;
  }
  catch (InvalidDataException ex)
  {
   incomingInvitation = null; InvitationInbox.Clear();
   IncomingPanel.IsVisible = true; IncomingDetails.Text = ex.Message;
   AcceptInvitationButton.IsEnabled = false;
  }
 }
 private async void Join(object? sender, EventArgs e) => await Run(async () =>
 {
  if (busy) throw new InvalidOperationException("Stop hosting or finish the current join first.");
  var invitation = Invitation.Decode(InvitationText.Text ?? "");
  await JoinInvitationAsync(invitation);
 });
 private async void AcceptInvitation(object? sender, EventArgs e) => await Run(async () =>
 {
  if (incomingInvitation is null) return;
  await JoinInvitationAsync(incomingInvitation);
 });
 private void DeclineInvitation(object? sender, EventArgs e)
 {
  incomingInvitation = null; InvitationInbox.Clear(); IncomingPanel.IsVisible = false;
 }
 private async Task JoinInvitationAsync(Invitation invitation)
 {
  if (busy || acceptingInvitation) return;
  acceptingInvitation = true;
  SetBusy(true);
  try
  {
   invitation.Validate();
   if (!await DisplayAlertAsync("Accept trip invitation?", $"{invitation.Trip.Name}\nHost: {invitation.Host}:{invitation.Port}\n\nProceed only if this invitation came from someone you trust. Your existing changes for this trip will be shared after host approval.", "Request to join", "Cancel")) return;
   var name = DeviceName.Text?.Trim() ?? "";
   if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(char.IsControl)) throw new InvalidDataException("Enter your device name (1–80 characters).");
   Preferences.Default.Set("device-name", name);
   var saved = (await store.ReadAsync()).FirstOrDefault(x => x.Trip.Id == invitation.Trip.Id);
   var log = new CalendarLog(invitation.Trip); if (saved is not null) log.Merge(saved);
   sharing = new CancellationTokenSource(); SetBusy(true);
   var operation = sharing;
   try
   {
    await NearbySession.JoinAsync(invitation, log.Export(), deviceId, name,
     async (snapshot, ct) => { ct.ThrowIfCancellationRequested(); await store.SaveAsync(snapshot); },
     message => MainThread.BeginInvokeOnMainThread(() =>
     {
      if (ReferenceEquals(sharing, operation)) Status.Text = message;
     }), sharing.Token);
    await Refresh(invitation.Trip.Id);
    LastJoin.Text = $"Joined {invitation.Trip.Name} at {DateTime.Now:t}.";
    Status.Text = "Joined successfully. Trip saved on this phone.";
    // Do not discard a different invitation received during this join.
    if (incomingInvitation?.Token == invitation.Token) DeclineInvitation(null, EventArgs.Empty);
    InvitationText.Text = "";
   }
   finally { sharing.Dispose(); sharing = null; SetBusy(false); ShowPendingInvitation(); }
  }
  finally { acceptingInvitation = false; SetBusy(false); }
 }
 private async void CopyInvitation(object? sender, EventArgs e) => await Run(async () =>
 {
  var text = hostSession?.Invitation.ToLink() ?? InvitationText.Text;
  if (string.IsNullOrWhiteSpace(text)) return;
  _ = Invitation.Decode(text);
  await Clipboard.Default.SetTextAsync(text);
  Status.Text = "Invitation link copied. Share it privately; it grants access until hosting stops or expires.";
 });
 public void StopSharing() => sharing?.Cancel();
 private void Cancel(object? sender, EventArgs e)
 {
  StopSharing(); Status.Text = "Stopping sharing…";
 }
 // Do not cancel when a date modal or system share sheet covers this page.
 // Background hosting is best effort only; both phones must return to the app.
 private async Task Run(Func<Task> action)
 {
  try { await action(); }
  catch (OperationCanceledException) { Status.Text = "Sharing stopped or timed out. Saved changes remain on this phone."; }
  catch (System.Net.Sockets.SocketException)
  {
   Status.Text = "Connection failed.";
   await DisplayAlertAsync("Unable to connect", "Check that both phones are on the same Wi-Fi/hotspot, local-network access is allowed, and the host is open. Guest Wi-Fi may isolate devices. Ask for a new invitation if the address changed.", "OK");
  }
  catch (Exception ex)
  {
   Status.Text = "Operation failed. Saved changes remain local.";
   // Avoid exposing frame contents or invitation tokens in exception text.
   await DisplayAlertAsync("TripTogether", ex is InvalidDataException or InvalidOperationException ? ex.Message : "Sharing could not finish. Check permissions and connectivity, then retry.", "OK");
  }
 }

private async Task<string?> PickDateTimeAsync(
    string title,
    bool allDay,
    DateOnly? initialDate,
    DateTimeOffset initialTime,
    string timeZoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var zonedTime = TimeZoneInfo.ConvertTime(initialTime, zone);

        var initialValue = allDay
            ? (initialDate ?? DateOnly.FromDateTime(zonedTime.DateTime))
                .ToDateTime(TimeOnly.MinValue)
            : zonedTime.DateTime;

        while (true)
        {
            var page = new DateTimePickerPage(
                title, initialValue, allDay, timeZoneId);

            await Navigation.PushModalAsync(page);

            var selection = await page.Result;
            if (selection is null)
                return null;

            var selected = selection.Value;

            if (allDay)
            {
                return selected.ToString(
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture);
            }

            if (zone.IsInvalidTime(selected) ||
                zone.IsAmbiguousTime(selected))
            {
                await DisplayAlertAsync(
                    "Choose another time",
                    "This time is skipped or occurs twice because of a " +
                    "daylight-saving change. Choose an unambiguous time.",
                    "OK");

                initialValue = selected;
                continue;
            }

            var value = new DateTimeOffset(
                selected,
                zone.GetUtcOffset(selected));

            return value.ToString(
                "yyyy-MM-ddTHH:mm:sszzz",
                System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
