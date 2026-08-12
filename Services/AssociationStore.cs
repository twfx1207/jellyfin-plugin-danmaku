using System.Text.Json;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Danmaku.Services;

internal sealed class AssociationStore
{
    private readonly string _filePath;
    private readonly ILogger<AssociationStore> _logger;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Models.AssociationRecord> _records = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public AssociationStore(IApplicationPaths applicationPaths, ILogger<AssociationStore> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(
            applicationPaths.PluginConfigurationsPath,
            "Jellyfin.Plugin.Danmaku.associations.json");
        Load();
    }

    public bool TryGet(Guid itemId, out Models.AssociationRecord? association)
    {
        lock (_sync)
        {
            return _records.TryGetValue(itemId, out association);
        }
    }

    public Task SetAsync(Guid itemId, Models.AssociationRecord association, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _records[itemId] = association;
            Save();
        }

        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(Guid itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_records.Remove(itemId))
            {
                return Task.FromResult(false);
            }

            Save();
            return Task.FromResult(true);
        }
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var serialized = File.ReadAllText(_filePath);
            var records = JsonSerializer.Deserialize<Dictionary<string, Models.AssociationRecord>>(
                serialized,
                _jsonOptions);
            if (records is null)
            {
                return;
            }

            foreach (var pair in records)
            {
                if (Guid.TryParse(pair.Key, out var itemId))
                {
                    _records[itemId] = pair.Value;
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to load persisted danmaku matches from {Path}", _filePath);
        }
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("The plugin configuration directory is unavailable.");
        Directory.CreateDirectory(directory);

        var serializable = _records.ToDictionary(
            pair => pair.Key.ToString("N"),
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        var serialized = JsonSerializer.Serialize(serializable, _jsonOptions);
        var temporaryPath = _filePath + ".tmp";

        try
        {
            File.WriteAllText(temporaryPath, serialized);
            File.Move(temporaryPath, _filePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
