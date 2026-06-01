using FrameHub.Core.Logging;
using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services;
using System;
using System.Text.Json;

namespace FrameHub.Core.Services.SessionOptimization;

public sealed class SessionStateService
{
    private readonly string _filePath = AppPaths.GetUserDataFilePath("active_session.json");
    private readonly ILogger _logger = LoggerService.Instance;

    public ActiveSessionState? Load()
    {
        try
        {
            string? json = AtomicFileService.ReadAllTextWithBackup(_filePath);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<ActiveSessionState>(json);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to load active session state: {ex.Message}");
            return null;
        }
    }

    public void Save(ActiveSessionState state)
    {
        try
        {
            string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            AtomicFileService.WriteAllTextAtomic(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to save active session state: {ex.Message}");
        }
    }

    public void Clear()
    {
        try
        {
            if (System.IO.File.Exists(_filePath))
            {
                System.IO.File.Delete(_filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to clear active session state: {ex.Message}");
        }
    }
}
