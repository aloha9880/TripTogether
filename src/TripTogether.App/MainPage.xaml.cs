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
 public MainPage(TripStore store) { InitializeComponent(); this.store=store; }
 protected override async void OnAppearing() { base.OnAppearing(); await Run(async()=> { deviceId=await store.DeviceIdAsync(); await Refresh(); }); }
 private async Task Refresh(Guid? select=null)
 {
  var snapshots=await store.ReadAsync(); TripPicker.ItemsSource=snapshots.Select(x=>x.Trip).ToArray();
  var wanted=select ?? current?.Trip.Id;
  TripPicker.SelectedIndex=Array.FindIndex(snapshots,x=>x.Trip.Id==wanted);
  if(TripPicker.SelectedIndex<0 && snapshots.Length>0) TripPicker.SelectedIndex=0;
 }
 private async void TripChanged(object? sender,EventArgs e)
 {
  if(TripPicker.SelectedItem is not Trip trip) return;
  await Run(async()=> { var saved=(await store.ReadAsync()).Single(x=>x.Trip.Id==trip.Id); current=new(trip); current.Merge(saved); Render(); });
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
 private void RequireEditable() {if(busy)throw new InvalidOperationException("Cancel or finish sharing before editing.");if(current is null)throw new InvalidOperationException("Choose or create a trip first.");}
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
  current.Edit(id,deviceId,data);await Save();
 }
 private async void Host(object? sender,EventArgs e)=>await Run(async()=>
 {
  RequireEditable(); var log=current!;
  using var session=new NearbySession(log.Trip,HostAddress.Text?.Trim() ?? "");
  busy=true;TripPicker.IsEnabled=false;sharing=new();
  try {InvitationText.Text=session.Invitation.Encode();Status.Text="Waiting up to 10 minutes. Share the invitation privately.";
   var result=await session.ReceiveAsync(log.Export(),sharing.Token);log.Merge(result);await store.SaveAsync(log.Export());Render();Status.Text="Trip synchronized. Invitation is no longer valid.";
  } finally {busy=false;TripPicker.IsEnabled=true;sharing.Dispose();sharing=null;InvitationText.Text="";}
 });
 private async void Join(object? sender,EventArgs e)=>await Run(async()=>
 {
  if(busy)throw new InvalidOperationException("Finish the current session first.");
  var invitation=Invitation.Decode(InvitationText.Text ?? "");
  if(!await DisplayAlertAsync("Accept shared trip?",$"{invitation.Trip.Name}\n{invitation.Host}:{invitation.Port}\nOnly proceed if this invitation came privately from someone you trust. Existing changes in this trip will also be sent.","Accept","Cancel"))return;
  var saved=(await store.ReadAsync()).FirstOrDefault(x=>x.Trip.Id==invitation.Trip.Id);var log=new CalendarLog(invitation.Trip);if(saved is not null)log.Merge(saved);
  busy=true;TripPicker.IsEnabled=false;sharing=new();
  try {Status.Text="Connecting…";log.Merge(await NearbySession.JoinAsync(invitation,log.Export(),sharing.Token));await store.SaveAsync(log.Export());current=log;await Refresh(log.Trip.Id);Render();Status.Text="Trip synchronized.";InvitationText.Text="";}
  finally {busy=false;TripPicker.IsEnabled=true;sharing.Dispose();sharing=null;}
 });
 private async void CopyInvitation(object? sender,EventArgs e)=>await Run(async()=> {if(string.IsNullOrWhiteSpace(InvitationText.Text))return;await Clipboard.Default.SetTextAsync(InvitationText.Text);Status.Text="Copied. Treat the invitation as a temporary secret.";});
 private void Cancel(object? sender,EventArgs e)=>sharing?.Cancel();
 protected override void OnDisappearing() {sharing?.Cancel();base.OnDisappearing();}
 private async Task Run(Func<Task> action) {try {await action();}catch(OperationCanceledException){Status.Text="Sharing canceled or timed out. Your saved changes remain local.";}catch(Exception ex){Status.Text="Operation failed";await DisplayAlertAsync("TripTogether",ex.Message,"OK");}}

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
