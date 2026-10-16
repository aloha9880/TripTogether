using SQLite;
using System.Text.Json;
using TripTogether.Core;
namespace TripTogether.App.Services;
public sealed class TripRow { [PrimaryKey] public string Id { get; set; } = ""; public string Json { get; set; } = ""; }
public sealed class TripStore
{
 private readonly SemaphoreSlim gate = new(1);
 private SQLiteAsyncConnection? db;
 private async Task<SQLiteAsyncConnection> Database()
 {
  if(db is not null) return db;
  var connection=new SQLiteAsyncConnection(Path.Combine(FileSystem.AppDataDirectory,"trips.db3"));
  await connection.CreateTableAsync<TripRow>(); db=connection; return connection;
 }
 public async Task<Snapshot[]> ReadAsync()
 {
  await gate.WaitAsync(); try { return (await (await Database()).Table<TripRow>().ToListAsync()).Select(x => JsonSerializer.Deserialize<Snapshot>(x.Json) ?? throw new InvalidDataException("Invalid saved trip.")).ToArray(); } finally { gate.Release(); }
 }
 public async Task SaveAsync(Snapshot snapshot)
 {
  var log=new CalendarLog(snapshot.Trip); log.Merge(snapshot);
  await gate.WaitAsync(); try { await (await Database()).InsertOrReplaceAsync(new TripRow {Id=snapshot.Trip.Id.ToString(),Json=JsonSerializer.Serialize(snapshot)}); } finally { gate.Release(); }
 }
 public async Task<Guid> DeviceIdAsync()
 {
  var value=await SecureStorage.Default.GetAsync("device-id"); if(Guid.TryParse(value,out var id)) return id;
  id=Guid.NewGuid(); await SecureStorage.Default.SetAsync("device-id",id.ToString()); return id;
 }
}
