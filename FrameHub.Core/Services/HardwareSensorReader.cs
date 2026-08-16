using LibreHardwareMonitor.Hardware;

namespace FrameHub.Core.Services;

public interface IHardwareSensorData
{
    string Name { get; }
    SensorType SensorType { get; }
    float? Value { get; }
}

public sealed record HardwareSensorData(string Name, SensorType SensorType, float? Value) : IHardwareSensorData;

internal sealed class SensorAdapter : IHardwareSensorData
{
    private readonly ISensor _sensor;

    public SensorAdapter(ISensor sensor)
    {
        _sensor = sensor ?? throw new ArgumentNullException(nameof(sensor));
    }

    public string Name => _sensor.Name;
    public SensorType SensorType => _sensor.SensorType;
    public float? Value => _sensor.Value;
}

public static class HardwareSensorReader
{
    public static double? SelectCpuTemperature(IEnumerable<IHardwareSensorData>? sensors)
    {
        if (sensors == null) return null;

        var validTempSensors = sensors
            .Where(s => s.SensorType == SensorType.Temperature &&
                        s.Value.HasValue &&
                        s.Value.Value > 0 &&
                        !float.IsNaN(s.Value.Value) &&
                        !float.IsInfinity(s.Value.Value))
            .ToList();

        if (validTempSensors.Count == 0) return null;

        // 1. Exact/preferred AMD control temperature: "Core (Tctl/Tdie)"
        var tctlSensor = validTempSensors.FirstOrDefault(s =>
            s.Name.Equals("Core (Tctl/Tdie)", StringComparison.OrdinalIgnoreCase) ||
            s.Name.Contains("Tctl/Tdie", StringComparison.OrdinalIgnoreCase));
        if (tctlSensor != null && tctlSensor.Value.HasValue)
        {
            return tctlSensor.Value.Value;
        }

        // 2. CPU Package
        var packageSensor = validTempSensors.FirstOrDefault(s =>
            s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase));
        if (packageSensor != null && packageSensor.Value.HasValue)
        {
            return packageSensor.Value.Value;
        }

        // 3. Maximum valid CPU temperature sensor value from relevant Core / CCD temperature sensors
        var coreCcdSensors = validTempSensors
            .Where(s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                        s.Name.Contains("CCD", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (coreCcdSensors.Count > 0)
        {
            return coreCcdSensors.Max(s => (double)s.Value!.Value);
        }

        // 4. Any remaining valid CPU temperature sensor value
        return validTempSensors.Max(s => (double)s.Value!.Value);
    }

    public static (long? UsedBytes, long? TotalBytes) ReadGpuVramBytes(IEnumerable<IHardwareSensorData>? sensors)
    {
        if (sensors == null) return (null, null);

        var dataSensors = sensors
            .Where(s => (s.SensorType is SensorType.SmallData or SensorType.Data) &&
                        s.Value.HasValue &&
                        s.Value.Value > 0 &&
                        !float.IsNaN(s.Value.Value) &&
                        !float.IsInfinity(s.Value.Value))
            .ToList();

        long? usedBytes = null;
        long? totalBytes = null;

        // Used sensors: prefer "GPU Memory Used", then "D3D Dedicated Memory Used", then other dedicated memory used
        var usedSensor = dataSensors.FirstOrDefault(s => s.Name.Equals("GPU Memory Used", StringComparison.OrdinalIgnoreCase))
            ?? dataSensors.FirstOrDefault(s => s.Name.Equals("D3D Dedicated Memory Used", StringComparison.OrdinalIgnoreCase))
            ?? dataSensors.FirstOrDefault(s => (s.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase) ||
                                                (s.Name.Contains("Dedicated Memory", StringComparison.OrdinalIgnoreCase) && s.Name.Contains("Used", StringComparison.OrdinalIgnoreCase)))
                                               && !s.Name.Contains("Shared", StringComparison.OrdinalIgnoreCase)
                                               && !s.Name.Contains("System", StringComparison.OrdinalIgnoreCase));

        if (usedSensor != null && usedSensor.Value.HasValue)
        {
            usedBytes = ConvertToBytes(usedSensor.Value.Value, usedSensor.SensorType);
        }

        // Total sensors: prefer "GPU Memory Total", then "D3D Dedicated Memory Total", then other dedicated memory total
        var totalSensor = dataSensors.FirstOrDefault(s => s.Name.Equals("GPU Memory Total", StringComparison.OrdinalIgnoreCase))
            ?? dataSensors.FirstOrDefault(s => s.Name.Equals("D3D Dedicated Memory Total", StringComparison.OrdinalIgnoreCase))
            ?? dataSensors.FirstOrDefault(s => (s.Name.Contains("Memory Total", StringComparison.OrdinalIgnoreCase) ||
                                                (s.Name.Contains("Dedicated Memory", StringComparison.OrdinalIgnoreCase) && s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)))
                                                && !s.Name.Contains("Shared", StringComparison.OrdinalIgnoreCase)
                                                && !s.Name.Contains("System", StringComparison.OrdinalIgnoreCase));

        if (totalSensor != null && totalSensor.Value.HasValue)
        {
            totalBytes = ConvertToBytes(totalSensor.Value.Value, totalSensor.SensorType);
        }

        return (usedBytes, totalBytes);
    }

    public static long? ConvertToBytes(float value, SensorType sensorType)
    {
        if (value <= 0 || float.IsNaN(value) || float.IsInfinity(value)) return null;

        return sensorType switch
        {
            SensorType.SmallData => (long)Math.Round(value * 1024.0 * 1024.0), // MB -> Bytes (1 MB = 1048576 B)
            SensorType.Data => (long)Math.Round(value * 1024.0 * 1024.0 * 1024.0), // GB -> Bytes (1 GB = 1073741824 B)
            _ => null
        };
    }
}
