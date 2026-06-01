using FrameHub.Core.Logging;
using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services;
using System;
using System.Text.Json;

namespace FrameHub.Core.Services.SessionOptimization;

public sealed class SessionOptimizationSettingsService
{
    private readonly string _filePath = AppPaths.GetUserDataFilePath("session_optimization.json");
    private readonly ILogger _logger = LoggerService.Instance;

    public SessionOptimizationSettings Load()
    {
        try
        {
            string? json = AtomicFileService.ReadAllTextWithBackup(_filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new SessionOptimizationSettings();
            }

            return JsonSerializer.Deserialize<SessionOptimizationSettings>(json) ?? new SessionOptimizationSettings();
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to load session optimization settings: {ex.Message}");
            return new SessionOptimizationSettings();
        }
    }

    public void Save(SessionOptimizationSettings settings)
    {
        try
        {
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            AtomicFileService.WriteAllTextAtomic(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to save session optimization settings: {ex.Message}");
        }
    }
}
