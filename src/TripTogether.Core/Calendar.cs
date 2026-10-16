namespace TripTogether.Core;
public sealed record Trip(Guid Id, string Name, string TimeZoneId);
public sealed record EventData(string Title, string Notes, string Location, DateTimeOffset Start,
    DateTimeOffset End, string TimeZoneId, bool AllDay, DateOnly? FirstDate, DateOnly? EndDateExclusive)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title) || Title.Length > 200 || Notes is null || Location is null || Notes.Length > 10000 || Location.Length > 1000)
            throw new InvalidDataException("Check event title and text lengths.");
        _ = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        if (AllDay ? FirstDate is null || EndDateExclusive is null || EndDateExclusive <= FirstDate : End <= Start)
            throw new InvalidDataException("Event end must follow its start.");
    }
}
public sealed record Change(Guid Id, Guid TripId, Guid EventId, Guid DeviceId, Guid[] Parents, EventData? Data);
public sealed record Snapshot(int Version, Trip Trip, Change[] Changes);
public sealed class CalendarLog
{
    private readonly Dictionary<Guid, Change> changes = new();
    public Trip Trip { get; }
    public CalendarLog(Trip trip) { Trip = trip; ValidateTrip(trip); }
    public static void ValidateTrip(Trip trip)
    {
        if (trip is null || trip.Id == Guid.Empty || string.IsNullOrWhiteSpace(trip.Name) || trip.Name.Length > 200)
            throw new InvalidDataException("Invalid trip.");
        _ = TimeZoneInfo.FindSystemTimeZoneById(trip.TimeZoneId);
    }
    public Change[] Heads(Guid eventId)
    {
        var rows = changes.Values.Where(x => x.EventId == eventId).ToArray();
        var ancestors = rows.SelectMany(x => x.Parents).ToHashSet();
        return rows.Where(x => !ancestors.Contains(x.Id)).OrderBy(x => x.Id).ToArray();
    }
    public Guid[] EventIds => changes.Values.Select(x => x.EventId).Distinct().ToArray();
    public Snapshot Export() => new(1, Trip, changes.Values.OrderBy(x => x.Id).ToArray());
    public void Merge(Snapshot incoming)
    {
        if (incoming is null || incoming.Version != 1 || incoming.Trip != Trip || incoming.Changes is null || incoming.Changes.Length > 10000)
            throw new InvalidDataException("Unsupported or mismatched trip snapshot.");
        var next = new Dictionary<Guid, Change>(changes);
        foreach (var c in incoming.Changes)
        {
            if (c is null || c.Parents is null || c.Id == Guid.Empty || c.EventId == Guid.Empty || c.DeviceId == Guid.Empty || c.TripId != Trip.Id || c.Parents.Length > 10000)
                throw new InvalidDataException("Invalid change.");
            c.Data?.Validate();
            if (c.Parents.Distinct().Count() != c.Parents.Length) throw new InvalidDataException("Duplicate parent.");
            if (next.TryGetValue(c.Id, out var old) && !Equivalent(old, c)) throw new InvalidDataException("Change ID collision.");
            next[c.Id] = c;
        }
        if (next.Count > 10000) throw new InvalidDataException("Trip change limit reached.");
        foreach (var c in next.Values)
            foreach (var parent in c.Parents)
                if (!next.TryGetValue(parent, out var p) || p.EventId != c.EventId || parent == c.Id)
                    throw new InvalidDataException("Missing or invalid parent.");
        // Kahn's algorithm rejects cycles without recursive stack growth.
        var degrees = next.ToDictionary(x => x.Key, x => x.Value.Parents.Length);
        var children = next.Keys.ToDictionary(x => x, _ => new List<Guid>());
        foreach (var c in next.Values) foreach (var p in c.Parents) children[p].Add(c.Id);
        var queue = new Queue<Guid>(degrees.Where(x => x.Value == 0).Select(x => x.Key));
        int visited = 0;
        while (queue.TryDequeue(out var id)) { visited++; foreach (var child in children[id]) if (--degrees[child] == 0) queue.Enqueue(child); }
        if (visited != next.Count) throw new InvalidDataException("Cyclic history.");
        changes.Clear(); foreach (var c in next) changes.Add(c.Key, c.Value);
    }
    private static bool Equivalent(Change a, Change b) => a.Id == b.Id && a.TripId == b.TripId && a.EventId == b.EventId &&
        a.DeviceId == b.DeviceId && a.Data == b.Data && a.Parents.SequenceEqual(b.Parents);
    public Change Edit(Guid eventId, Guid deviceId, EventData? data)
    {
        var c = new Change(Guid.NewGuid(), Trip.Id, eventId, deviceId, Heads(eventId).Select(x => x.Id).ToArray(), data);
        Merge(new Snapshot(1, Trip, [c])); return c;
    }
}
