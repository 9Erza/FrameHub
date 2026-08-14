using FrameHub.Core.Logging;
using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services;
using System;
using System.IO;
using System.Text.Json;

namespace FrameHub.Core.Services.SessionOptimization;

public class SessionStateService
{
    private readonly string _filePath;
    private readonly ILogger _logger = LoggerService.Instance;

    public SessionStateService(string? filePath = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath)
            ? AppPaths.GetUserDataFilePath("active_session.json")
            : Path.GetFullPath(filePath);
    }

    public virtual ActiveSessionState? Load()
    {
        if (TryLoadFile(_filePath, out ActiveSessionState? primary, out string? primaryError))
        {
            return primary;
        }

        string backupPath = _filePath + ".bak";
        if (TryLoadFile(backupPath, out ActiveSessionState? backup, out string? backupError))
        {
            if (!string.IsNullOrWhiteSpace(primaryError))
            {
                _logger.Warn($"Failed to load primary active session state; using valid backup. {primaryError}");
            }
            return backup;
        }

        if (!string.IsNullOrWhiteSpace(primaryError) || !string.IsNullOrWhiteSpace(backupError))
        {
            _logger.Warn($"Failed to load active session state. Primary: {primaryError ?? "missing"}; Backup: {backupError ?? "missing"}");
        }
        return null;
    }

    private static bool TryLoadFile(string path, out ActiveSessionState? state, out string? error)
    {
        state = null;
        error = null;
        if (!File.Exists(path)) return false;

        try
        {
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "file is empty";
                return false;
            }

            using JsonDocument document = JsonDocument.Parse(json);
            if (!HasRequiredPersistedMembers(document.RootElement, out error))
            {
                return false;
            }

            state = JsonSerializer.Deserialize<ActiveSessionState>(json);
            if (state == null || !IsValid(state, out error))
            {
                state = null;
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool HasRequiredPersistedMembers(JsonElement root, out string? error)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "root value is not an object";
            return false;
        }

        if (!root.TryGetProperty(nameof(ActiveSessionState.IsActive), out JsonElement isActive)
            || isActive.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            error = "required active-session flag is missing or invalid";
            return false;
        }

        if (!root.TryGetProperty(nameof(ActiveSessionState.SessionId), out JsonElement sessionId)
            || sessionId.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(sessionId.GetString()))
        {
            error = "required session identity is missing or invalid";
            return false;
        }

        if (!root.TryGetProperty(nameof(ActiveSessionState.SuspendedProcesses), out JsonElement suspendedProcesses)
            || suspendedProcesses.ValueKind != JsonValueKind.Array)
        {
            error = "required suspended-process collection is missing or invalid";
            return false;
        }

        error = null;
        return true;
    }

    private static bool IsValid(ActiveSessionState state, out string? error)
    {
        if (!state.IsActive)
        {
            error = "active-session journal is not active";
            return false;
        }
        if (string.IsNullOrWhiteSpace(state.SessionId))
        {
            error = "session id is missing";
            return false;
        }
        if (!Enum.IsDefined(state.RecoveryPhase))
        {
            error = $"unknown recovery phase {(int)state.RecoveryPhase}";
            return false;
        }
        if (state.PlannedProcesses == null || state.SuspendedProcesses == null || state.AmbiguousProcesses == null)
        {
            error = "process recovery collection is null";
            return false;
        }
        if (state.OriginalTaskbarVisibility != null && state.OriginalTaskbarVisibility.SecondaryTaskbarsVisible == null)
        {
            error = "taskbar visibility collection is null";
            return false;
        }

        error = null;
        return true;
    }

    public virtual bool Save(ActiveSessionState state)
    {
        try
        {
            string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            AtomicFileService.WriteAllTextAtomic(_filePath, json);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to save active session state: {ex.Message}");
            return false;
        }
    }

    public virtual bool Clear()
    {
        try
        {
            string backupPath = _filePath + ".bak";
            if (System.IO.File.Exists(backupPath))
            {
                // Delete the fallback first. If clearing the primary then fails, restart recovery
                // still sees the authoritative current journal and can retry safely.
                System.IO.File.Delete(backupPath);
            }
            if (System.IO.File.Exists(_filePath))
            {
                System.IO.File.Delete(_filePath);
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to clear active session state: {ex.Message}");
            return false;
        }
    }
}
