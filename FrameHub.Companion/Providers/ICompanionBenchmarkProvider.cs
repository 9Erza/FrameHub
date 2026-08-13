using FrameHub.Companion.Models;

namespace FrameHub.Companion.Providers;

public interface ICompanionBenchmarkProvider
{
    CompanionBenchmarkStatusDto GetStatus();
    IReadOnlyList<CompanionBenchmarkTargetDto> GetEligibleTargets();
    Task<CompanionBenchmarkStartResultDto> StartBenchmarkAsync(CompanionBenchmarkStartRequestDto request);
    Task<CompanionBenchmarkStopResultDto> StopBenchmarkAsync();
    CompanionBenchmarkHistoryListDto GetHistory(int limit);
    CompanionBenchmarkHistoryDetailDto? GetHistoryDetail(Guid sessionId);
    CompanionBenchmarkChartDto? GetHistoryChart(Guid sessionId, int buckets);
    CompanionBenchmarkComparisonDto CompareHistorySessions(Guid sessionAId, Guid sessionBId);
}
