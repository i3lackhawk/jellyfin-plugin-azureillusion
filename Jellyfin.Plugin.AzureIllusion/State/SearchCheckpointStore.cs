using System.Text.Json;

namespace Jellyfin.Plugin.AzureIllusion.State;

/// <summary>Durable rotation of search candidates. Contains no credentials.</summary>
internal sealed class SearchCheckpointStore
{
    internal sealed record Entry(DateTimeOffset CheckedAt, DateTimeOffset RetryAt);
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _path;
    public SearchCheckpointStore(string path) => _path = path;
    public async Task LoadAsync(CancellationToken token)
    {
        if (!File.Exists(_path)) return;
        try
        {
            await using var stream = File.OpenRead(_path);
            var saved = await JsonSerializer.DeserializeAsync<Dictionary<string, Entry>>(stream, cancellationToken: token).ConfigureAwait(false);
            if (saved is not null) foreach (var pair in saved) _entries[pair.Key] = pair.Value;
        }
        catch (JsonException) { File.Copy(_path, _path + ".corrupt-" + Guid.NewGuid().ToString("N")); }
    }
    public bool IsDue(string key) => !_entries.TryGetValue(key, out var entry) || entry.RetryAt <= DateTimeOffset.UtcNow;
    public DateTimeOffset LastChecked(string key) => _entries.GetValueOrDefault(key)?.CheckedAt ?? DateTimeOffset.MinValue;
    public void Record(string key, bool failed)
    {
        var now = DateTimeOffset.UtcNow;
        _entries[key] = new(now, now.Add(failed ? TimeSpan.FromMinutes(15) : TimeSpan.FromHours(24)));
    }
    public async Task SaveAsync(CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = File.Create(temp))
                await JsonSerializer.SerializeAsync(stream, _entries, cancellationToken: token).ConfigureAwait(false);
            File.Move(temp, _path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
