using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FrameHub.Core.Logging;
using FrameHub.Core.Models;
using FrameHub.Core.Services;

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

    public bool AddDevice(PairedDeviceRecord record)
    {
        lock (_lock)
        {
            if (IsFaulted)
            {
                throw new InvalidOperationException("Cannot modify paired device store while in a faulted state.");
            }

            _devices.RemoveAll(d => d.Id == record.Id);
            _devices.Add(record);
            return SaveInternal();
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

            int removed = _devices.RemoveAll(d => d.Id == id);
            if (removed > 0)
            {
                return SaveInternal();
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
                byte[] storedHash = Encoding.UTF8.GetBytes(device.CredentialHash);
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
            _devices.Clear();
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

            SaveInternal();
        }
    }

    private bool SaveInternal()
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
            string json = JsonSerializer.Serialize(_devices, new JsonSerializerOptions
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
