using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrameHub.Core.Models.Benchmarking;

namespace FrameHub.Core.Services.Benchmarking;

public sealed class BenchmarkStorageService
{
    private readonly string _rootDirectory;

    public BenchmarkStorageService(string? rootDirectory = null)
    {
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory) ? AppPaths.BenchmarkDirectory : Path.GetFullPath(rootDirectory);
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
        double? requestedCaptureDurationSeconds = null)
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
                SessionOptimizationActive = sessionOptimizationActive
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
