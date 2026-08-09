using System.Diagnostics;

namespace FrameHub.App.Services;

public enum FrameHubExternalLink
{
    Repository,
    Author,
    Website,
    Support
}

public static class ExternalLinkService
{
    private static readonly IReadOnlyDictionary<FrameHubExternalLink, Uri> OwnedLinks =
        new Dictionary<FrameHubExternalLink, Uri>
        {
            [FrameHubExternalLink.Repository] = new("https://github.com/9Erza/FrameHub"),
            [FrameHubExternalLink.Author] = new("https://github.com/9Erza"),
            [FrameHubExternalLink.Website] = new("https://dobrypc.pl"),
            [FrameHubExternalLink.Support] = new("https://buymeacoffee.com/9erza")
        };

    public static bool TryOpen(FrameHubExternalLink link)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = OwnedLinks[link].AbsoluteUri,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
