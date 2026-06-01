using FrameHub.App.Services;

namespace FrameHub.App.ViewModels;

internal static class SessionDisplayText
{
    public static bool IsPolish(LocalizationService localization)
        => localization.CurrentLanguage.Equals("pl", StringComparison.OrdinalIgnoreCase);

    public static string RuleName(string? ruleId, string? fallback, LocalizationService localization)
    {
        bool pl = IsPolish(localization);
        string id = NormalizeRuleId(ruleId);

        return id switch
        {
            "explorer" => pl ? "Interfejs Windows" : "Windows interface",
            "browsers" => pl ? "Przeglądarki" : "Browsers",
            "spotify" => "Spotify",
            "discord" => "Discord",
            "teamspeak" => "TeamSpeak",
            "steamwebhelper" => "Steam WebHelper",
            "custom" => pl ? "Wybór ręczny" : "Manual selection",
            _ => string.IsNullOrWhiteSpace(fallback) ? (pl ? "Reguła" : "Rule") : fallback!
        };
    }

    public static string RuleDescription(string? ruleId, string? fallback, LocalizationService localization)
    {
        bool pl = IsPolish(localization);
        string id = NormalizeRuleId(ruleId);

        return id switch
        {
            "explorer" => pl
                ? "Wstrzymuje explorer.exe. Pasek zadań i menu Start wrócą po przywróceniu sesji."
                : "Suspends explorer.exe. Taskbar and Start menu return after session restore.",
            "browsers" => pl
                ? "Chrome, Edge, Brave, Firefox, Opera i Vivaldi."
                : "Chrome, Edge, Brave, Firefox, Opera and Vivaldi.",
            "spotify" => pl
                ? "Procesy Spotify działające w tle."
                : "Spotify background processes.",
            "discord" => pl
                ? "Discord, Discord PTB i Discord Canary."
                : "Discord, Discord PTB and Discord Canary.",
            "teamspeak" => pl
                ? "Klienty TeamSpeak."
                : "TeamSpeak clients.",
            "steamwebhelper" => pl
                ? "Procesy Steam WebHelper. Steam.exe nie jest wstrzymywany."
                : "Steam WebHelper processes. Steam.exe is not suspended.",
            _ => string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback!
        };
    }

    public static string Badge(string? ruleId, bool defaultEnabled, bool isAdvanced, LocalizationService localization)
    {
        bool pl = IsPolish(localization);
        if (isAdvanced)
        {
            return pl ? "ZAAWANSOWANE" : "ADVANCED";
        }

        return defaultEnabled
            ? (pl ? "DOMYŚLNIE" : "DEFAULT")
            : (pl ? "OPCJONALNE" : "OPTIONAL");
    }

    private static string NormalizeRuleId(string? ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId)) return string.Empty;
        string id = ruleId.Trim();
        if (id.StartsWith("custom:", StringComparison.OrdinalIgnoreCase)) return "custom";
        return id.ToLowerInvariant();
    }
}
