using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.Core.Logging;
using FrameHub.Core.Services;
using LibreHardwareMonitor.Hardware;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class HardwareSensorReaderTests
{
    [TestMethod]
    public void SelectCpuTemperature_PrefersTctlTdie_WhenPresent()
    {
        var sensors = new IHardwareSensorData[]
        {
            new HardwareSensorData("CPU Package", SensorType.Temperature, 60.0f),
            new HardwareSensorData("Core (Tctl/Tdie)", SensorType.Temperature, 68.5f),
            new HardwareSensorData("Core #1", SensorType.Temperature, 58.0f)
        };

        double? result = HardwareSensorReader.SelectCpuTemperature(sensors);

        Assert.IsNotNull(result);
        Assert.AreEqual(68.5, result.Value, 0.01, "AMD Core (Tctl/Tdie) control temp must be preferred over Package and Core temps.");
    }

    [TestMethod]
    public void SelectCpuTemperature_UsesPackage_WhenTctlTdieAbsent()
    {
        var sensors = new IHardwareSensorData[]
        {
            new HardwareSensorData("CPU Package", SensorType.Temperature, 62.3f),
            new HardwareSensorData("Core #1", SensorType.Temperature, 55.0f),
            new HardwareSensorData("CCD1 Temperature", SensorType.Temperature, 59.0f)
        };

        double? result = HardwareSensorReader.SelectCpuTemperature(sensors);

        Assert.IsNotNull(result);
        Assert.AreEqual(62.3, result.Value, 0.01, "CPU Package temp must be used when Tctl/Tdie is absent.");
    }

    [TestMethod]
    public void SelectCpuTemperature_UsesMaxCoreCcd_WhenPackageAndTctlAbsent()
    {
        var sensors = new IHardwareSensorData[]
        {
            new HardwareSensorData("Core #1", SensorType.Temperature, 52.0f),
            new HardwareSensorData("Core #2", SensorType.Temperature, 64.5f),
            new HardwareSensorData("CCD1 Temperature", SensorType.Temperature, 61.0f)
        };

        double? result = HardwareSensorReader.SelectCpuTemperature(sensors);

        Assert.IsNotNull(result);
        Assert.AreEqual(64.5, result.Value, 0.01, "Maximum valid Core / CCD temperature must be selected when Package is absent.");
    }

    [TestMethod]
    public void SelectCpuTemperature_UsesAnyValidTemp_WhenCoreCcdAbsent()
    {
        var sensors = new IHardwareSensorData[]
        {
            new HardwareSensorData("Processor", SensorType.Temperature, 48.0f),
            new HardwareSensorData("Die Temperature", SensorType.Temperature, 51.5f)
        };

        double? result = HardwareSensorReader.SelectCpuTemperature(sensors);

        Assert.IsNotNull(result);
        Assert.AreEqual(51.5, result.Value, 0.01, "Any remaining valid temperature sensor must be used as fallback.");
    }

    [TestMethod]
    public void SelectCpuTemperature_NullSensorValue_DoesNotBecomeZero()
    {
        var sensors = new IHardwareSensorData[]
        {
            new HardwareSensorData("Core (Tctl/Tdie)", SensorType.Temperature, null),
            new HardwareSensorData("CPU Package", SensorType.Temperature, null)
        };

        double? result = HardwareSensorReader.SelectCpuTemperature(sensors);

        Assert.IsNull(result, "Null sensor values must never become 0.0.");
    }

    [TestMethod]
    public void SelectCpuTemperature_NoUsableTemp_ReturnsNull()
    {
        Assert.IsNull(HardwareSensorReader.SelectCpuTemperature(null));
        Assert.IsNull(HardwareSensorReader.SelectCpuTemperature(Array.Empty<IHardwareSensorData>()));
    }

    [TestMethod]
    public void SelectCpuTemperature_NegativeOrZeroTemp_Ignored()
    {
        var sensors = new IHardwareSensorData[]
        {
            new HardwareSensorData("CPU Package", SensorType.Temperature, 0.0f),
            new HardwareSensorData("Core (Tctl/Tdie)", SensorType.Temperature, -5.0f),
            new HardwareSensorData("Core #1", SensorType.Temperature, 45.0f)
        };

        double? result = HardwareSensorReader.SelectCpuTemperature(sensors);

        Assert.IsNotNull(result);
        Assert.AreEqual(45.0, result.Value, 0.01, "Zero and negative temperature values must be ignored in favor of valid temperatures.");
    }

    [TestMethod]
    public void ReadGpuVramBytes_SmallData_ConvertsMegabytesToBytes()
    {
        var sensors = new IHardwareSensorData[]
        {
            new HardwareSensorData("GPU Memory Used", SensorType.SmallData, 4096.0f),
            new HardwareSensorData("GPU Memory Total", SensorType.SmallData, 8192.0f)
        };

        var (usedBytes, totalBytes) = HardwareSensorReader.ReadGpuVramBytes(sensors);

        Assert.IsNotNull(usedBytes);
        Assert.IsNotNull(totalBytes);
        Assert.AreEqual(4294967296L, usedBytes.Value, "4096 MB must convert to 4,294,967,296 bytes.");
        Assert.AreEqual(8589934592L, totalBytes.Value, "8192 MB must convert to 8,589,934,592 bytes.");
    }

    [TestMethod]
    public void ReadGpuVramBytes_Data_ConvertsGigabytesToBytes()
    {
        var sensors = new IHardwareSensorData[]
        {
            new HardwareSensorData("GPU Memory Used", SensorType.Data, 6.0f),
            new HardwareSensorData("GPU Memory Total", SensorType.Data, 12.0f)
        };

        var (usedBytes, totalBytes) = HardwareSensorReader.ReadGpuVramBytes(sensors);

        Assert.IsNotNull(usedBytes);
        Assert.IsNotNull(totalBytes);
        Assert.AreEqual(6442450944L, usedBytes.Value, "6 GB must convert to 6,442,450,944 bytes.");
        Assert.AreEqual(12884901888L, totalBytes.Value, "12 GB must convert to 12,884,901,888 bytes.");
    }

    [TestMethod]
    public void ReadGpuVramBytes_D3DDedicatedMemory_Fallback()
    {
        var sensors = new IHardwareSensorData[]
        {
            new HardwareSensorData("D3D Dedicated Memory Used", SensorType.SmallData, 2048.0f),
            new HardwareSensorData("D3D Dedicated Memory Total", SensorType.SmallData, 4096.0f),
            new HardwareSensorData("D3D Shared Memory Used", SensorType.SmallData, 1024.0f)
        };

        var (usedBytes, totalBytes) = HardwareSensorReader.ReadGpuVramBytes(sensors);

        Assert.IsNotNull(usedBytes);
        Assert.IsNotNull(totalBytes);
        Assert.AreEqual(2147483648L, usedBytes.Value);
        Assert.AreEqual(4294967296L, totalBytes.Value);
    }

    [TestMethod]
    public void ReadGpuVramBytes_MissingUsedOrTotal_RemainsNull()
    {
        var sensors = new IHardwareSensorData[]
        {
            new HardwareSensorData("GPU Memory Used", SensorType.SmallData, 2048.0f)
        };

        var (usedBytes, totalBytes) = HardwareSensorReader.ReadGpuVramBytes(sensors);

        Assert.IsNotNull(usedBytes);
        Assert.IsNull(totalBytes, "Missing total sensor must remain null and not fabricated.");
    }

    [TestMethod]
    public void ReadGpuVramBytes_NullOrZeroValues_RemainNull()
    {
        var sensors = new IHardwareSensorData[]
        {
            new HardwareSensorData("GPU Memory Used", SensorType.SmallData, null),
            new HardwareSensorData("GPU Memory Total", SensorType.SmallData, 0.0f)
        };

        var (usedBytes, totalBytes) = HardwareSensorReader.ReadGpuVramBytes(sensors);

        Assert.IsNull(usedBytes);
        Assert.IsNull(totalBytes);
    }

    [TestMethod]
    public void ConvertToBytes_UnitConversion_Accurate()
    {
        Assert.AreEqual(1048576L, HardwareSensorReader.ConvertToBytes(1.0f, SensorType.SmallData));
        Assert.AreEqual(1073741824L, HardwareSensorReader.ConvertToBytes(1.0f, SensorType.Data));
        Assert.IsNull(HardwareSensorReader.ConvertToBytes(1.0f, SensorType.Temperature));
        Assert.IsNull(HardwareSensorReader.ConvertToBytes(0.0f, SensorType.SmallData));
        Assert.IsNull(HardwareSensorReader.ConvertToBytes(-1.0f, SensorType.SmallData));
        Assert.IsNull(HardwareSensorReader.ConvertToBytes(float.NaN, SensorType.SmallData));
    }

    [TestMethod]
    public void ReadGpuVramBytes_TotalOnly_D3D_DoesNotPopulateUsed()
    {
        var sensors = new IHardwareSensorData[]
        {
            new HardwareSensorData("D3D Dedicated Memory Total", SensorType.SmallData, 8192.0f)
        };

        var (usedBytes, totalBytes) = HardwareSensorReader.ReadGpuVramBytes(sensors);

        Assert.IsNull(usedBytes, "Total-only D3D sensor must not populate used bytes.");
        Assert.IsNotNull(totalBytes);
        Assert.AreEqual(8589934592L, totalBytes.Value, "8192 MB must convert to 8,589,934,592 bytes.");
    }

    [TestMethod]
    public void ReadGpuVramBytes_UsedOnly_D3D_DoesNotPopulateTotal()
    {
        var sensors = new IHardwareSensorData[]
        {
            new HardwareSensorData("D3D Dedicated Memory Used", SensorType.SmallData, 1024.0f)
        };

        var (usedBytes, totalBytes) = HardwareSensorReader.ReadGpuVramBytes(sensors);

        Assert.IsNotNull(usedBytes);
        Assert.IsNull(totalBytes, "Used-only D3D sensor must not populate total bytes.");
        Assert.AreEqual(1073741824L, usedBytes.Value, "1024 MB must convert to 1,073,741,824 bytes.");
    }

    [TestMethod]
    public void ReadGpuVramBytes_GenericDedicatedMemoryTotal_CannotPopulateUsed()
    {
        var sensors = new IHardwareSensorData[]
        {
            new HardwareSensorData("Dedicated Memory Total", SensorType.SmallData, 8192.0f)
        };

        var (usedBytes, totalBytes) = HardwareSensorReader.ReadGpuVramBytes(sensors);

        Assert.IsNull(usedBytes, "Generic Dedicated Memory Total sensor must not match as used bytes.");
        Assert.IsNotNull(totalBytes);
        Assert.AreEqual(8589934592L, totalBytes.Value);
    }

    [TestMethod]
    public void ReadGpuVramBytes_GenericDedicatedMemoryUsed_CannotPopulateTotal()
    {
        var sensors = new IHardwareSensorData[]
        {
            new HardwareSensorData("Dedicated Memory Used", SensorType.SmallData, 2048.0f)
        };

        var (usedBytes, totalBytes) = HardwareSensorReader.ReadGpuVramBytes(sensors);

        Assert.IsNotNull(usedBytes);
        Assert.IsNull(totalBytes, "Generic Dedicated Memory Used sensor must not match as total bytes.");
        Assert.AreEqual(2147483648L, usedBytes.Value);
    }

    [TestMethod]
    public void ReadGpuVramBytes_SharedAndSystemVariants_RemainExcluded()
    {
        var sensors = new IHardwareSensorData[]
        {
            new HardwareSensorData("D3D Shared Memory Used", SensorType.SmallData, 2048.0f),
            new HardwareSensorData("D3D Shared Memory Total", SensorType.SmallData, 8192.0f),
            new HardwareSensorData("System Memory Used", SensorType.SmallData, 4096.0f),
            new HardwareSensorData("System Memory Total", SensorType.SmallData, 16384.0f),
            new HardwareSensorData("Shared Dedicated Memory Used", SensorType.SmallData, 1024.0f),
            new HardwareSensorData("System Dedicated Memory Total", SensorType.SmallData, 4096.0f)
        };

        var (usedBytes, totalBytes) = HardwareSensorReader.ReadGpuVramBytes(sensors);

        Assert.IsNull(usedBytes, "Shared and System memory sensors must be completely excluded from VRAM Used.");
        Assert.IsNull(totalBytes, "Shared and System memory sensors must be completely excluded from VRAM Total.");
    }

    [TestMethod]
    public void HardwareViewModel_FormatCpuTemp_Unavailable_ShowsDashesNotZero()
    {
        Assert.AreEqual("-- °C", HardwareViewModel.FormatCpuTemp(isMonitorEnabled: true, cpuTemp: null), "Null temp must format as '-- °C'.");
        Assert.AreEqual("-- °C", HardwareViewModel.FormatCpuTemp(isMonitorEnabled: true, cpuTemp: 0.0), "Zero temp must format as '-- °C'.");
        Assert.AreEqual("-- °C", HardwareViewModel.FormatCpuTemp(isMonitorEnabled: true, cpuTemp: -5.0), "Negative temp must format as '-- °C'.");
        Assert.AreEqual("-- °C", HardwareViewModel.FormatCpuTemp(isMonitorEnabled: false, cpuTemp: 58.42), "Disabled monitor must format as '-- °C'.");
    }

    [TestMethod]
    public void HardwareViewModel_FormatCpuTemp_Valid_FormatsCorrectly()
    {
        Assert.AreEqual($"{58.42:N1} °C", HardwareViewModel.FormatCpuTemp(isMonitorEnabled: true, cpuTemp: 58.42));
    }

    [TestMethod]
    public void AppTelemetrySnapshotProvider_CreateHardwareSnapshot_PreservesNullsAndPropagatesVram()
    {
        var metricsWithNulls = new HardwareMetrics
        {
            CpuTemp = null,
            VramUsedBytes = null,
            VramTotalBytes = null
        };
        var snapshotNulls = AppTelemetrySnapshotProvider.CreateHardwareSnapshot(metricsWithNulls);
        Assert.IsNotNull(snapshotNulls);
        Assert.IsNull(snapshotNulls.CpuTemperatureCelsius, "Null CpuTemp must remain null.");
        Assert.IsNull(snapshotNulls.VramUsedBytes, "Null VramUsedBytes must remain null.");
        Assert.IsNull(snapshotNulls.VramTotalBytes, "Null VramTotalBytes must remain null.");

        var metricsWithValues = new HardwareMetrics
        {
            CpuTemp = 55.4,
            CpuLoad = 22.0,
            GpuTemp = 60.0,
            GpuLoad = 80.0,
            RamUsedGB = 8.0,
            RamAvailableGB = 8.0,
            VramUsedBytes = 4294967296L,
            VramTotalBytes = 8589934592L
        };
        var snapshotValues = AppTelemetrySnapshotProvider.CreateHardwareSnapshot(metricsWithValues);
        Assert.IsNotNull(snapshotValues);
        Assert.AreEqual(55.4, snapshotValues.CpuTemperatureCelsius);
        Assert.AreEqual(4294967296L, snapshotValues.VramUsedBytes);
        Assert.AreEqual(8589934592L, snapshotValues.VramTotalBytes);

        var metricsWithZeroTemp = new HardwareMetrics
        {
            CpuTemp = 0.0,
            GpuTemp = 0.0
        };
        var snapshotZero = AppTelemetrySnapshotProvider.CreateHardwareSnapshot(metricsWithZeroTemp);
        Assert.IsNotNull(snapshotZero);
        Assert.IsNull(snapshotZero.CpuTemperatureCelsius, "0.0 CpuTemp must be converted to null.");
        Assert.IsNull(snapshotZero.GpuTemperatureCelsius, "0.0 GpuTemp must be converted to null.");

        Assert.IsNull(AppTelemetrySnapshotProvider.CreateHardwareSnapshot(null));
    }
}
