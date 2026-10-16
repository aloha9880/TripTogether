using TripTogether.Core;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;
int count=0;
void Check(bool condition,string name) {if(!condition)throw new Exception("FAILED: "+name);Console.WriteLine("PASS: "+name);count++;}
void Reject(Action action,string name) {try{action();}catch(InvalidDataException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
var trip=new Trip(Guid.NewGuid(),"Island trip","Pacific/Honolulu");var device=Guid.NewGuid();var id=Guid.NewGuid();
var data=new EventData("Beach","Bring water","Kona",DateTimeOffset.Parse("2026-12-27T10:00:00-10:00"),DateTimeOffset.Parse("2026-12-27T11:00:00-10:00"),"Pacific/Honolulu",false,null,null);
var a=new CalendarLog(trip);var root=a.Edit(id,device,data);var b=new CalendarLog(trip);b.Merge(a.Export());
a.Edit(id,device,data with {Title="Hike"});b.Edit(id,Guid.NewGuid(),data with {Title="Swim"});
a.Merge(b.Export());Check(a.Heads(id).Length==2,"concurrent edits preserve both heads");
b.Merge(a.Export());Check(b.Heads(id).Length==2,"reverse sync converges");a.Merge(b.Export());Check(a.Export().Changes.Length==3,"retry is idempotent");
a.Edit(id,device,data with {Title="Agreed plan"});b.Merge(a.Export());Check(b.Heads(id).Single().Data!.Title=="Agreed plan","resolution supersedes both versions");
b.Edit(id,device,null);a.Merge(b.Export());Check(a.Heads(id).Single().Data is null,"delete creates tombstone");
a.Merge(new Snapshot(1,trip,[root]));Check(a.Heads(id).Single().Data is null,"old device cannot resurrect deleted event");
var left=new CalendarLog(trip);left.Merge(new Snapshot(1,trip,[root]));var right=new CalendarLog(trip);right.Merge(left.Export());left.Edit(id,device,null);right.Edit(id,device,data with {Title="Later edit"});left.Merge(right.Export());Check(left.Heads(id).Length==2,"edit/delete requires explicit resolution");
var before=a.Export().Changes.Length;var bad=new Change(Guid.NewGuid(),trip.Id,id,device,[Guid.NewGuid()],data);
Reject(()=>a.Merge(new Snapshot(1,trip,[bad])),"reject missing parent");Check(a.Export().Changes.Length==before,"failed merge leaves state unchanged");
Reject(()=>a.Merge(new Snapshot(1,trip,[root with {Data=data with {Title="Changed immutable op"}}])),"reject immutable operation collision");
var x=Guid.NewGuid();var y=Guid.NewGuid();Reject(()=>a.Merge(new Snapshot(1,trip,[new(x,trip.Id,id,device,[y],data),new(y,trip.Id,id,device,[x],data)])),"reject cyclic history");
Reject(()=>a.Merge(new Snapshot(2,trip,[])),"reject unsupported protocol");
Reject(()=>a.Merge(new Snapshot(1,trip with {Id=Guid.NewGuid()},[])),"reject another trip");
var allDay=data with {AllDay=true,FirstDate=new(2026,12,27),EndDateExclusive=new(2026,12,28),Start=DateTimeOffset.UnixEpoch,End=DateTimeOffset.UnixEpoch};allDay.Validate();
Check(JsonSerializer.Deserialize<EventData>(JsonSerializer.Serialize(allDay))==allDay,"all-day dates survive serialization");Reject(()=>(allDay with {EndDateExclusive=allDay.FirstDate}).Validate(),"reject empty all-day interval");
Check(JsonSerializer.Deserialize<EventData>(JsonSerializer.Serialize(data))==data,"timed offset survives serialization");
if(args.Contains("--network"))
{
 var ip=Dns.GetHostAddresses(Dns.GetHostName()).First(p=>p.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(p)).ToString();
 using var host=new NearbySession(trip,ip);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
 var incoming=host.ReceiveAsync(a.Export(),timeout.Token);var received=await NearbySession.JoinAsync(Invitation.Decode(host.Invitation.Encode()),b.Export(),timeout.Token);var merged=await incoming;
 Check(received.Changes.Length==merged.Changes.Length,"real TLS socket session merges both logs");
 using var host2=new NearbySession(trip,ip);var waiting=host2.ReceiveAsync(a.Export(),timeout.Token);
 try {await NearbySession.JoinAsync(host2.Invitation with {Token=new string('0',64)},b.Export(),timeout.Token);throw new Exception("Bad token accepted");}catch(Exception ex) when(ex is IOException or AuthenticationException) {}
 try {await waiting;throw new Exception("Bad token accepted by host");}catch(AuthenticationException){Check(true,"reject incorrect invitation token");}
}
Console.WriteLine($"{count} checks passed.");
