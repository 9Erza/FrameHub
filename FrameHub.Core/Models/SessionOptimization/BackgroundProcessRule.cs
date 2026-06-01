using System;
using System.Collections.Generic;

namespace FrameHub.Core.Models.SessionOptimization;

public sealed class BackgroundProcessRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Background";
    public List<string> ProcessNames { get; set; } = new();
    public List<string> PathContains { get; set; } = new();
    public bool IsEnabled { get; set; }
    public bool DefaultEnabled { get; set; }
    public bool IsAdvanced { get; set; }
    public bool RequiresExtraConfirmation { get; set; }
}
