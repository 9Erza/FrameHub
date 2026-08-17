using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Logging;

namespace FrameHub.Core.Services.Benchmarking;

public sealed class BenchmarkStorageService
{
    private readonly string _rootDirectory;
    private readonly ILogger _logger;

    public BenchmarkStorageService(string? rootDirectory = null, ILogger? logger = null)
    {
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory) ? AppPaths.BenchmarkDirectory : Path.GetFullPath(rootDirectory);
        _logger = logger ?? LoggerService.Instance;
    }

    public string RootDirectory => _rootDirectory;

    public BenchmarkSession CreateSession(
        BenchmarkTarget target,
        BenchmarkProcessIdentity process,
        string frameHubVersion,
        DateTime startUtc,
        string? activeCpuProfileId = null,
        string? activeCpuProfileName = null,
        bool? sessionOptimizationActive = null,
        double? requestedCaptureDurationSeconds = null,
        BenchmarkEnvironmentSnapshot? environment = null)
    {
        Guid sessionId = Guid.NewGuid();
        string gameDirectory = Path.Combine(_rootDirectory, CreateStableGameFolderKey(target));
        string sessionFolder = $"{startUtc.ToUniversalTime():yyyyMMddTHHmmssfffZ}_{sessionId:N}";
        string sessionDirectory = Path.Combine(gameDirectory, sessionFolder);
        Directory.CreateDirectory(sessionDirectory);

        var session = new BenchmarkSession
        {
            SessionDirectory = sessionDirectory,
            Metadata = new BenchmarkSessionMetadata
            {
                SessionId = sessionId,
                FrameHubVersion = frameHubVersion,
                Game = target,
                Process = process,
                StartUtc = startUtc.ToUniversalTime(),
                RequestedCaptureDurationSeconds = requestedCaptureDurationSeconds,
                Status = BenchmarkSessionStatus.Created,
                ActiveCpuProfileId = activeCpuProfileId,
                ActiveCpuProfileName = activeCpuProfileName,
                SessionOptimizationActive = sessionOptimizationActive,
                Environment = environment
            }
        };

        SaveSession(session);
        return session;
    }

    public void SaveSession(BenchmarkSession session) =>
        WriteJsonAtomic(Path.Combine(session.SessionDirectory, BenchmarkFormat.SessionFileName), session.Metadata);

    public void SaveSummary(BenchmarkSession session, BenchmarkSummary summary) =>
        WriteJsonAtomic(Path.Combine(session.SessionDirectory, BenchmarkFormat.SummaryFileName), summary);

    public BenchmarkSessionMetadata LoadSessionMetadata(string sessionDirectory)
    {
        string json = File.ReadAllText(Path.Combine(sessionDirectory, BenchmarkFormat.SessionFileName), Encoding.UTF8);
        return JsonSerializer.Deserialize<BenchmarkSessionMetadata>(json, JsonOptions)
            ?? throw new InvalidDataException("session.json contained no benchmark metadata.");
    }

    public BenchmarkSummary LoadSummary(string sessionDirectory)
    {
        string json = File.ReadAllText(Path.Combine(sessionDirectory, BenchmarkFormat.SummaryFileName), Encoding.UTF8);
        return JsonSerializer.Deserialize<BenchmarkSummary>(json, JsonOptions)
            ?? throw new InvalidDataException("summary.json contained no benchmark summary.");
    }

    public IReadOnlyList<BenchmarkFrameSample> LoadRawFrames(string sessionDirectory)
    {
        BenchmarkSessionMetadata metadata = LoadSessionMetadata(sessionDirectory);
        string path = Path.Combine(ValidateSessionDirectory(sessionDirectory), metadata.RawDataFile);
        string json = File.ReadAllText(path, Encoding.UTF8);
        return JsonSerializer.Deserialize<List<BenchmarkFrameSample>>(json, JsonOptions)
            ?? throw new InvalidDataException("Raw benchmark data contained no frame samples.");
    }

    public BenchmarkHistoryResult EnumerateSessions()
    {
        if (!Directory.Exists(_rootDirectory)) return new BenchmarkHistoryResult();
        var sessions = new List<BenchmarkHistoryEntry>();
        var warnings = new List<string>();
        foreach (string metadataPath in Directory.EnumerateFiles(_rootDirectory, BenchmarkFormat.SessionFileName, SearchOption.AllDirectories))
        {
            string directory = Path.GetDirectoryName(metadataPath)!;
            try
            {
                BenchmarkSessionMetadata metadata = LoadSessionMetadata(directory);
                BenchmarkSummary? summary = null;
                string? readError = null;
                string summaryPath = Path.Combine(directory, BenchmarkFormat.SummaryFileName);
                if (File.Exists(summaryPath))
                {
                    try { summary = LoadSummary(directory); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
                    {
                        readError = ex.Message;
                        warnings.Add($"Benchmark summary could not be read from '{directory}': {ex.Message}");
                    }
                }
                else if (metadata.Status == BenchmarkSessionStatus.Completed)
                {
                    readError = "Completed session is missing summary.json.";
                    warnings.Add($"{readError} Directory: '{directory}'.");
                }

                sessions.Add(new BenchmarkHistoryEntry { SessionDirectory = directory, Metadata = metadata, Summary = summary, ReadError = readError });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                string warning = $"Benchmark session could not be read from '{directory}': {ex.Message}";
                warnings.Add(warning);
                _logger.Warn(warning);
            }
        }

        foreach (string warning in warnings) _logger.Warn(warning);
        return new BenchmarkHistoryResult
        {
            Sessions = sessions.OrderByDescending(entry => entry.Metadata.StartUtc).ToList(),
            Warnings = warnings
        };
    }

    public void DeleteSession(string sessionDirectory)
    {
        string validated = ValidateSessionDirectory(sessionDirectory);
        if (!File.Exists(Path.Combine(validated, BenchmarkFormat.SessionFileName)))
        {
            throw new InvalidOperationException("The requested directory is not a FrameHub benchmark session.");
        }

        Directory.Delete(validated, recursive: true);
    }

    public string ValidateSessionDirectory(string sessionDirectory)
    {
        if (string.IsNullOrWhiteSpace(sessionDirectory)) throw new ArgumentException("Session directory is required.", nameof(sessionDirectory));
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_rootDirectory));
        string candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sessionDirectory));
        string prefix = root + Path.DirectorySeparatorChar;
        if (candidate.Equals(root, StringComparison.OrdinalIgnoreCase) || !candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Benchmark session path is outside the configured FrameHub Benchmarks root.");
        }

        return candidate;
    }

    public static string CreateStableGameFolderKey(BenchmarkTarget target)
    {
        string source = SanitizeSegment(target.LibrarySource, 20);
        string identity = !string.IsNullOrWhiteSpace(target.SourceId)
            ? target.SourceId
            : target.LibraryItemId;
        string visible = SanitizeSegment(identity, 45);
        string hashInput = $"{target.LibrarySource}\n{target.SourceId}\n{target.LibraryItemId}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)))[..12].ToLowerInvariant();
        return $"{source}-{visible}-{hash}";
    }

    private static string SanitizeSegment(string? value, int maximumLength)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder();
        foreach (char character in value?.Trim() ?? string.Empty)
        {
            builder.Append(invalid.Contains(character) || char.IsControl(character) ? '-' : character);
            if (builder.Length == maximumLength) break;
        }

        string result = builder.ToString().Trim(' ', '.', '-');
        return string.IsNullOrWhiteSpace(result) ? "unknown" : result;
    }

    private static void WriteJsonAtomic<T>(string path, T value)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        AtomicFileService.WriteAllTextAtomic(path, json);
    }

    private static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
