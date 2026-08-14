namespace FrameHub.Companion.Providers;

public interface ICompanionPresentationPreferencesProvider
{
    string DesktopLanguage { get; }
}

public sealed class NullCompanionPresentationPreferencesProvider : ICompanionPresentationPreferencesProvider
{
    public string DesktopLanguage => "en";
}
