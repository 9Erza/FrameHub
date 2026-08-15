namespace FrameHub.Core.Services.Library;

/// <summary>
/// Curated public knowledge about supported Riot games: product ids used by official Riot-created
/// Start Menu shortcuts, the actual game process names as shown by Windows, and the Riot-owned
/// process names FrameHub must never mutate (games, client, launcher, and anti-cheat included).
/// </summary>
public static class RiotGameProcesses
{
    public sealed record RiotProductKnowledge(
        string ProductId,
        string DisplayName,
        string GameProcessName,
        string RelativeGameExecutablePath);

    public static readonly IReadOnlyList<RiotProductKnowledge> SupportedProducts =
    [
        new RiotProductKnowledge(
            "league_of_legends",
            "League of Legends",
            "League of Legends",
            Path.Combine("League of Legends", "Game", "League of Legends.exe")),
        new RiotProductKnowledge(
            "valorant",
            "VALORANT",
            "VALORANT-Win64-Shipping",
            Path.Combine("VALORANT", "ShooterGame", "Binaries", "Win64", "VALORANT-Win64-Shipping.exe"))
    ];

    /// <summary>Riot Client executable name used by official Riot-created shortcuts.</summary>
    public const string RiotClientExecutableName = "RiotClientServices";

    /// <summary>
    /// Riot-owned process names that FrameHub must never suspend, resume, kill, reprioritize,
    /// pin to cores/CPU Sets, or otherwise mutate. Games, League client, Riot Client, crash
    /// handlers, and Vanguard (vgc/vgtray/vgk) are all included.
    /// </summary>
    public static readonly IReadOnlySet<string> ProtectedProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "league of legends", "leagueclient", "leagueclientux", "leagueclientuxrender", "leaguecrashhandler64",
        "valorant-win64-shipping", "valorant", "riotclientservices", "riotclientcrashhandler",
        "vgc", "vgtray", "vgk"
    };

    public static bool IsProtectedProcessName(string? processName)
    {
        return !string.IsNullOrWhiteSpace(processName) && ProtectedProcessNames.Contains(processName.Trim());
    }

    public static RiotProductKnowledge? FindProduct(string? productId)
    {
        if (string.IsNullOrWhiteSpace(productId)) return null;
        return SupportedProducts.FirstOrDefault(product => product.ProductId.Equals(productId.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
