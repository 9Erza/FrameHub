using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FrameHub.Core.Logging;
using FrameHub.Core.Models;
using FrameHub.Core.Services;

using FrameHub.Companion.Authentication;

namespace FrameHub.Companion.Persistence;

public sealed class DeviceRecordStore
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private List<PairedDeviceRecord> _devices = new();

    public string FilePath => _filePath;
    public bool IsFaulted { get; private set; }
    public string? FaultMessage { get; private set; }

    public IReadOnlyList<PairedDeviceRecord> Devices
    {
        get
        {
            lock (_lock)
            {
                return _devices.ToList();
            }
        }
    }

    public DeviceRecordStore(string? filePath = null)
    {
        _filePath = !string.IsNullOrWhiteSpace(filePath)
            ? filePath
            : AppPaths.GetUserDataFilePath("paired-devices.json");

        Load();
    }

    public void Load()
    {
        lock (_lock)
        {
            _devices.Clear();
            IsFaulted = false;
            FaultMessage = null;

            if (!File.Exists(_filePath))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(_filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                var list = JsonSerializer.Deserialize<List<PairedDeviceRecord>>(json);
                if (list == null)
                {
                    IsFaulted = true;
                    FaultMessage = "Deserialization returned null object.";
                    LoggerService.Instance.Error("Paired devices store file corrupted: deserialized null.");
                    return;
                }

                _devices = list;
            }
            catch (Exception ex)
            {
                IsFaulted = true;
                FaultMessage = ex.Message;
                LoggerService.Instance.Error($"Paired devices store file corrupted or unreadable at '{_filePath}': {ex.Message}");
            }
        }
    }

    public PairedDeviceRecord? GetDeviceById(Guid id)
    {
        lock (_lock)
        {
            if (IsFaulted) return null;
            return _devices.FirstOrDefault(d => d.Id == id);
        }
    }

    public bool GrantScope(Guid id, string scope)
    {
        if (!CompanionScopes.IsValidScope(scope)) return false;

        lock (_lock)
        {
            if (IsFaulted)
            {
                throw new InvalidOperationException("Cannot modify paired device store while in a faulted state.");
            }

            int index = _devices.FindIndex(d => d.Id == id);
            if (index < 0) return false;

            var existing = _devices[index];
            if (existing.Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase))
            {
                return true; // Idempotent success
            }

            var updatedScopes = existing.Scopes.ToList();
            updatedScopes.Add(scope.Trim());
            var updatedDevices = _devices.ToList();
            updatedDevices[index] = existing with { Scopes = updatedScopes };
            return CommitInternal(updatedDevices);
        }
    }

    public bool RevokeScope(Guid id, string scope)
    {
        if (!CompanionScopes.IsValidScope(scope)) return false;

        lock (_lock)
        {
            if (IsFaulted)
            {
                throw new InvalidOperationException("Cannot modify paired device store while in a faulted state.");
            }

            int index = _devices.FindIndex(d => d.Id == id);
            if (index < 0) return false;

            var existing = _devices[index];
            if (!existing.Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase))
            {
                return true; // Idempotent success
            }

            var updatedScopes = existing.Scopes.Where(s => !s.Equals(scope.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            var updatedDevices = _devices.ToList();
            updatedDevices[index] = existing with { Scopes = updatedScopes };
            return CommitInternal(updatedDevices);
        }
    }

    public bool AddDevice(PairedDeviceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_lock)
        {
            if (IsFaulted)
            {
                throw new InvalidOperationException("Cannot modify paired device store while in a faulted state.");
            }

            var updatedDevices = _devices.Where(d => d.Id != record.Id).ToList();
            updatedDevices.Add(record);
            return CommitInternal(updatedDevices);
        }
    }

    public bool RevokeDevice(Guid id)
    {
        lock (_lock)
        {
            if (IsFaulted)
            {
                throw new InvalidOperationException("Cannot modify paired device store while in a faulted state.");
            }

            var updatedDevices = _devices.Where(d => d.Id != id).ToList();
            if (updatedDevices.Count != _devices.Count)
            {
                return CommitInternal(updatedDevices);
            }
            return false;
        }
    }

    public bool UpdateLastUsed(Guid id, DateTimeOffset lastUsedAtUtc)
    {
        lock (_lock)
        {
            if (IsFaulted) return false;

            int index = _devices.FindIndex(d => d.Id == id);
            if (index >= 0)
            {
                _devices[index] = _devices[index] with { LastUsedAtUtc = lastUsedAtUtc };
                return true;
            }
            return false;
        }
    }

    public PairedDeviceRecord? FindByCredentialHash(string credentialHash)
    {
        lock (_lock)
        {
            if (IsFaulted) return null;

            byte[] inputHash = Encoding.UTF8.GetBytes(credentialHash);
            foreach (var device in _devices)
            {
                if (string.IsNullOrWhiteSpace(device.CredentialHash)) continue;
                byte[] storedHash = Encoding.UTF8.GetBytes(device.CredentialHash);
                if (inputHash.Length != storedHash.Length) continue;
                if (CryptographicOperations.FixedTimeEquals(inputHash, storedHash))
                {
                    return device;
                }
            }
            return null;
        }
    }

    public void ResetStore()
    {
        lock (_lock)
        {
            IsFaulted = false;
            FaultMessage = null;

            try
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"Failed to delete paired devices store file during reset: {ex.Message}");
            }

            CommitInternal(new List<PairedDeviceRecord>());
        }
    }

    private bool CommitInternal(List<PairedDeviceRecord> updatedDevices)
    {
        if (!SaveInternal(updatedDevices))
        {
            return false;
        }

        _devices = updatedDevices;
        return true;
    }

    private bool SaveInternal(IReadOnlyList<PairedDeviceRecord> devices)
    {
        if (IsFaulted)
        {
            return false;
        }

        string? tempFile = null;
        try
        {
            string? dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            tempFile = Path.Combine(dir ?? AppContext.BaseDirectory, $"{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
            string json = JsonSerializer.Serialize(devices, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(tempFile, json);
            File.Move(tempFile, _filePath, overwrite: true);
            tempFile = null;
            return true;
        }
        catch (Exception ex)
        {
            IsFaulted = true;
            FaultMessage = $"Failed to persist paired device changes: {ex.Message}";
            LoggerService.Instance.Error($"Failed to atomically write paired device store to '{_filePath}': {ex.Message}");
            return false;
        }
        finally
        {
            if (tempFile != null && File.Exists(tempFile))
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch
                {
                    // Ignore failure to delete temp file during cleanup
                }
            }
        }
    }
}
