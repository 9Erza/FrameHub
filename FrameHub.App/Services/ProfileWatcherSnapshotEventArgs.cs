using System;
using System.Collections.Generic;

namespace FrameHub.App.Services;

public sealed class ProfileWatcherSnapshotEventArgs : EventArgs
{
    public IReadOnlySet<string> ActiveProcessNames { get; }

    public ProfileWatcherSnapshotEventArgs(IEnumerable<string> activeProcessNames)
    {
        ActiveProcessNames = new HashSet<string>(activeProcessNames, StringComparer.OrdinalIgnoreCase);
    }
}
