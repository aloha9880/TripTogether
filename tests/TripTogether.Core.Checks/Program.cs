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

var invite = new Invitation(Invitation.CurrentVersion,"192.168.1.10",5000,new string('A',64),new string('B',64),DateTimeOffset.UtcNow.AddMinutes(5),trip);
Check(Invitation.Decode(invite.Encode()) == invite,"raw invitation round trip");
Check(Invitation.Decode(invite.ToLink()) == invite,"URL-safe invitation link round trip");
Reject(()=>Invitation.Decode("triptogether://other/"+invite.Encode()),"reject incorrect link destination");
Reject(()=>Invitation.Decode(invite.ToLink()+"?token=oops"),"reject unexpected query");
Reject(()=>Invitation.Decode((invite with {Expires=DateTimeOffset.UtcNow.AddSeconds(-1)}).Encode()),"reject expired invitation");
Reject(()=>Invitation.Decode((invite with {Version=1}).Encode()),"reject old handshake version");
Reject(()=>Invitation.Decode((invite with {Token="no"}).Encode()),"reject malformed token");
Reject(()=>Invitation.Decode((invite with {Host="0.0.0.0"}).Encode()),"reject wildcard host");
Reject(()=>Invitation.Decode((invite with {Host="224.0.0.1"}).Encode()),"reject multicast host");
Reject(()=>Invitation.Decode((invite with {Trip=null!}).Encode()),"reject null trip");
Reject(()=>Invitation.Decode("not an invitation"),"reject invalid base64");
Reject(()=>a.Merge(new Snapshot(1,trip,null!)),"reject null change collection");
Reject(()=>a.Merge(new Snapshot(1,trip,[null!])),"reject null change");
Reject(()=>a.Merge(new Snapshot(1,trip,[root with {Parents=null!}])),"reject null parent list");
if(args.Contains("--network"))
{
 var ip=Environment.GetEnvironmentVariable("TRIP_TEST_IP") ?? LocalAddresses.GetAvailable().First().Address;
 using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(40));
 using var host=new NearbySession(trip,ip);
 var hostLog=new CalendarLog(trip);hostLog.Merge(a.Export());
 bool allow=true;int approvals=0,completed=0;var updates=new List<HostUpdate>();
 var running=host.HostAsync((request,ct)=>{approvals++;return Task.FromResult(allow);},
  (remote,ct)=>{hostLog.Merge(remote);return Task.FromResult(hostLog.Export());},
  update=>{updates.Add(update);if(update.Stage==HostStage.Joined)completed++;},timeout.Token);
 async Task MustFail(Func<Task> action,string name)
 {
  try{await action();}catch(Exception ex) when(ex is IOException or AuthenticationException or InvalidDataException){Check(true,name);return;}
  throw new Exception("Expected failure: "+name);
 }
 var guestLog=new CalendarLog(trip);guestLog.Merge(b.Export());
 Snapshot? saved=null;
 Task Join(Invitation invitation)=>NearbySession.JoinAsync(invitation,guestLog.Export(),Guid.NewGuid(),"Test phone",
  (snapshot,ct)=>{saved=snapshot;return Task.CompletedTask;},null,timeout.Token);
 await MustFail(()=>Join(host.Invitation with {Token=new string('0',64)}),"incorrect token rejected");
 Check(approvals==0,"unauthenticated guest never prompts host");
 await MustFail(()=>Join(host.Invitation with {Fingerprint=new string('0',64)}),"incorrect certificate pin rejected");
 Check(approvals==0,"wrong certificate never prompts host");
 allow=false;await MustFail(()=>Join(host.Invitation),"declined guest receives rejection");
 Check(saved is null,"decline does not persist guest trip");
 allow=true;await Join(host.Invitation);
 Check(saved is not null && saved.Changes.Length==hostLog.Export().Changes.Length,"approved join persists merged trip");
 await Join(host.Invitation);
 Check(approvals==3,"host accepts another join without restarting");
 await MustFail(()=>NearbySession.JoinAsync(host.Invitation,guestLog.Export(),Guid.NewGuid(),"Disk failure phone",
  (_,_)=>throw new IOException("Simulated storage failure"),null,timeout.Token),"guest persistence failure is surfaced");
 await Join(host.Invitation);
 // Let the host read the final receipt before cancellation.
 var receiptDeadline=DateTime.UtcNow.AddSeconds(3);
 while(completed<3 && DateTime.UtcNow<receiptDeadline)await Task.Delay(10);
 Check(completed==3,"only guests that save and acknowledge count as joined");
 Check(updates.Any(x=>x.Stage==HostStage.Failed),"failed peer is reported without terminating host");
 timeout.Cancel();try{await running;}catch(OperationCanceledException){}
 Check(running.IsCompleted,"cancellation terminates waiting host");
 // Stop invalidates the old listening endpoint.
 await MustFail(async()=>
 {
  try
  {
   using var stoppedTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
   await NearbySession.JoinAsync(host.Invitation,guestLog.Export(),Guid.NewGuid(),"Stopped test",(_,_)=>Task.CompletedTask,null,stoppedTimeout.Token);
  }
  catch(System.Net.Sockets.SocketException ex){throw new IOException("Stopped host",ex);}
 },"stopped host no longer accepts joins");
}
Console.WriteLine($"{count} checks passed.");
