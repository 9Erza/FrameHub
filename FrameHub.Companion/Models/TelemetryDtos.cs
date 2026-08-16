using FrameHub.Core.Models.Benchmarking;

namespace FrameHub.Companion.Models;

public sealed record CompanionTelemetrySnapshot(
    DateTimeOffset CapturedAtUtc,
    HardwareTelemetrySnapshot? Hardware,
    CurrentGameSnapshot? CurrentGame,
    LivePerformanceSnapshot? LivePerformance = null,
    HardwareMonitoringStatusDto? HardwareMonitor = null
);

public sealed record HardwareMonitoringStatusDto(
    bool Enabled,
    bool Active
);

public sealed record SetHardwareMonitoringRequestDto(
    bool Enabled
);

public sealed record HardwareTelemetrySnapshot(
    double? CpuUtilizationPercent,
    double? CpuTemperatureCelsius,
    double? GpuUtilizationPercent,
    double? GpuTemperatureCelsius,
    long? RamUsedBytes,
    long? RamTotalBytes,
    long? VramUsedBytes,
    long? VramTotalBytes
);

public sealed record CurrentGameSnapshot(
    string? LibraryItemId,
    string DisplayName,
    bool IsRunning,
    DateTimeOffset? ProcessStartTimeUtc
);

public sealed record WebSocketTicketResponseDto(
    string Ticket,
    int ExpiresInSeconds
);
