using System.Globalization;
using System.Text;
using FrameHub.Core.Models.Benchmarking;

namespace FrameHub.Core.Services.Benchmarking;

/// <summary>
/// Premium PAKIET A1 / dyrektywa §D (Researcher R3): eksport porównania benchmarków
/// do CSV/Markdown. Czysty konsument <see cref="BenchmarkStorageService"/> (dane)
/// i <see cref="BenchmarkComparisonService"/> (matematyka) — bez zmian obliczeń.
/// <para>
/// Granica Free/Premium: funkcja w pełni addytywna. Nic istniejącego nie jest
/// gate'owane, nie ma serwera licencji, płatności ani DRM (jawnie odrzucone).
/// Eksport jest dostępny dla wszystkich; ewentualny przyszły podział pakietów
/// to decyzja produktowa, nie stub w tym kodzie.
/// </para>
/// <para>
/// Determinizm: ten sam wkład daje bajt-identyczny wynik (InvariantCulture,
/// stała kolejność wierszy z <c>Compare()</c>, daty w UTC ISO-8601). CSV niesie
/// wyłącznie tabelę metryk o stabilnym schemacie (do arkuszy/skryptów);
/// Markdown dokłada nagłówki sesji i tabelę różnic środowiska.
/// </para>
/// </summary>
public static class BenchmarkComparisonExportService
{
    public enum ReportFormat
    {
        Csv,
        Markdown
    }

    public static string Export(BenchmarkHistoryEntry first, BenchmarkHistoryEntry second, ReportFormat format) =>
        format == ReportFormat.Csv
            ? ExportCsv(first, second)
            : ExportMarkdown(first, second);

    public static string ExportCsv(BenchmarkHistoryEntry first, BenchmarkHistoryEntry second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var metrics = BenchmarkComparisonService.Compare(first, second);

        var builder = new StringBuilder();
        builder.AppendLine("metric,session_a,session_b,delta,delta_pct,direction");
        foreach (BenchmarkComparisonMetric metric in metrics)
        {
            builder.Append(CsvCell(metric.Key)).Append(',');
            builder.Append(CsvCell(FormatNumber(metric.SessionA))).Append(',');
            builder.Append(CsvCell(FormatNumber(metric.SessionB))).Append(',');
            builder.Append(CsvCell(FormatNumber(metric.Delta))).Append(',');
            builder.Append(CsvCell(FormatPercent(metric.PercentageDelta))).Append(',');
            builder.AppendLine(CsvCell(FormatDirection(metric.Direction)));
        }

        return builder.ToString();
    }

    public static string ExportMarkdown(BenchmarkHistoryEntry first, BenchmarkHistoryEntry second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var metrics = BenchmarkComparisonService.Compare(first, second);
        var differences = BenchmarkComparisonService.CompareEnvironments(first, second);

        var builder = new StringBuilder();
        builder.AppendLine("# Benchmark comparison");
        builder.AppendLine();
        builder.AppendLine($"- Session A: {MarkdownCell(SessionLabel(first))}");
        builder.AppendLine($"- Session B: {MarkdownCell(SessionLabel(second))}");
        builder.AppendLine();
        builder.AppendLine("## Metrics");
        builder.AppendLine();
        builder.AppendLine("| Metric | Session A | Session B | Delta | Delta % | Direction |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (BenchmarkComparisonMetric metric in metrics)
        {
            builder.Append("| ").Append(MarkdownCell(metric.Key)).Append(" | ")
                .Append(MarkdownCell(FormatNumber(metric.SessionA))).Append(" | ")
                .Append(MarkdownCell(FormatNumber(metric.SessionB))).Append(" | ")
                .Append(MarkdownCell(FormatNumber(metric.Delta))).Append(" | ")
                .Append(MarkdownCell(FormatPercent(metric.PercentageDelta))).Append(" | ")
                .Append(MarkdownCell(FormatDirection(metric.Direction))).AppendLine(" |");
        }

        builder.AppendLine();
        builder.AppendLine("## Environment differences");
        builder.AppendLine();
        if (differences.Count == 0)
        {
            builder.AppendLine("_No environment differences._");
        }
        else
        {
            builder.AppendLine("| Setting | Session A | Session B |");
            builder.AppendLine("| --- | --- | --- |");
            foreach (BenchmarkEnvironmentDifference difference in differences)
            {
                builder.Append("| ").Append(MarkdownCell(difference.Key)).Append(" | ")
                    .Append(MarkdownCell(difference.FirstValue)).Append(" | ")
                    .Append(MarkdownCell(difference.SecondValue)).AppendLine(" |");
            }
        }

        return builder.ToString();
    }

    private static string SessionLabel(BenchmarkHistoryEntry entry)
    {
        string game = string.IsNullOrWhiteSpace(entry.Metadata.Game.DisplayName)
            ? entry.Metadata.Game.LibraryItemId
            : entry.Metadata.Game.DisplayName;
        return $"{game} @ {entry.Metadata.StartUtc:yyyy-MM-ddTHH:mm:ssZ} ({entry.Metadata.SessionId:D})";
    }

    private static string FormatNumber(double? value) =>
        value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "n/a";

    private static string FormatPercent(double? value) =>
        value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : "n/a";

    private static string FormatDirection(BenchmarkMetricDirection direction) => direction switch
    {
        BenchmarkMetricDirection.HigherIsBetter => "higher_is_better",
        BenchmarkMetricDirection.LowerIsBetter => "lower_is_better",
        _ => "neutral"
    };

    private static string CsvCell(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    private static string MarkdownCell(string value) =>
        value.Replace("|", "\\|").Replace("\r\n", " ").Replace('\n', ' ').Replace("\r", " ");
}
