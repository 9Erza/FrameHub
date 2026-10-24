using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Core.Services.Network;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace FrameHub.App.ViewModels;

/// <summary>
/// Read-only DNS diagnostics panel (dyrektywa §6.1). Presentation-only adapter
/// over the existing <see cref="DnsDiagnosticsService"/>: shows adapters, current
/// resolvers and preset latency comparison. It NEVER mutates Windows network
/// settings — there is no Apply/Rollback/DHCP code path here by design.
/// </summary>
public sealed class DnsDiagnosticsPanelViewModel : ViewModelBase
{
    private readonly LocalizationService _localization;
    private readonly DnsDiagnosticsService _service = new();
    private readonly System.Windows.Threading.Dispatcher _dispatcher;
    private string _hostname = "example.com";
    private string _customResolver = string.Empty;
    private bool _isComparing;
    private string _statusText = string.Empty;
    private CancellationTokenSource? _compareCts;

    public sealed record AdapterRow(string Name, string Status, string Addresses, string DnsServers, string Source);
    public sealed record PresetRow(string Id, string DisplayName, string Servers, string? DohTemplate);
    public sealed record CompareRow(string Resolver, string Hostname, string Outcome, bool Success);

    public string Title => _localization.T("Dns.Title");
    public string Description => _localization.T("Dns.Description");
    public string ReadOnlyNotice => _localization.T("Dns.ReadOnlyNotice");
    public string AdaptersTitle => _localization.T("Dns.AdaptersTitle");
    public string NoAdaptersText => _localization.T("Dns.NoAdapters");
    public string AdapterNameHeader => _localization.T("Dns.AdapterName");
    public string AdapterStatusHeader => _localization.T("Dns.AdapterStatus");
    public string AdapterDnsHeader => _localization.T("Dns.AdapterDns");
    public string PresetsTitle => _localization.T("Dns.PresetsTitle");
    public string CompareTitle => _localization.T("Dns.CompareTitle");
    public string HostnameLabel => _localization.T("Dns.Hostname");
    public string CustomResolverLabel => _localization.T("Dns.CustomResolver");
    public string CompareButtonText => _localization.T("Dns.Compare");
    public string CancelButtonText => _localization.T("Dns.CancelCompare");
    public string RefreshAdaptersText => _localization.T("Dns.RefreshAdapters");
    public string StatusText => _statusText;
    public bool IsComparing => _isComparing;
    public bool IsCompareIdle => !_isComparing;

    public ObservableCollection<AdapterRow> Adapters { get; } = new();
    public ObservableCollection<PresetRow> Presets { get; } = new();
    public ObservableCollection<CompareRow> Results { get; } = new();

    public string Hostname
    {
        get => _hostname;
        set => SetProperty(ref _hostname, value ?? string.Empty);
    }

    public string CustomResolver
    {
        get => _customResolver;
        set => SetProperty(ref _customResolver, value ?? string.Empty);
    }

    public ICommand RefreshAdaptersCommand { get; }
    public ICommand CompareCommand { get; }
    public ICommand CancelCompareCommand { get; }

    public DnsDiagnosticsPanelViewModel(LocalizationService localization)
    {
        _localization = localization;
        // Captured on the UI thread (composition root creates VMs on the UI thread):
        // CompareAsync awaits with ConfigureAwait(false) and must marshal bound
        // collection/property updates back here.
        _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        RefreshAdaptersCommand = new RelayCommand(_ => LoadAdapters());
        CompareCommand = new RelayCommand(_ => _ = CompareAsync(), _ => !IsComparing);
        CancelCompareCommand = new RelayCommand(_ => _compareCts?.Cancel(), _ => IsComparing);
        LoadAdapters();
        LoadPresets();
        _statusText = _localization.T("Dns.StatusIdle");
    }

    public void LoadAdapters()
    {
        Adapters.Clear();
        IReadOnlyList<DnsDiagnosticsService.AdapterDnsInfo> infos;
        try
        {
            infos = _service.GetAdapters();
        }
        catch (Exception ex)
        {
            _statusText = string.Format(_localization.T("Dns.AdaptersFailed"), ex.Message);
            OnPropertyChanged(nameof(StatusText));
            return;
        }

        foreach (var info in infos.OrderByDescending(a => a.IsUp).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
        {
            string addresses = info.Ipv4Addresses.Concat(info.Ipv6Addresses).DefaultIfEmpty("—").Aggregate((a, b) => a + ", " + b);
            string servers = info.DnsServers.Length == 0
                ? _localization.T("Dns.NoResolver")
                : string.Join(", ", info.DnsServers);
            string source = info.DnsFromDhcp ? _localization.T("Dns.SourceDhcp") : _localization.T("Dns.SourceStatic");
            Adapters.Add(new AdapterRow(info.Name, info.Status.ToString(), addresses, servers, source));
        }

        _statusText = _localization.T("Dns.StatusIdle");
        OnPropertyChanged(nameof(StatusText));
    }

    public void RefreshTexts()
    {
        LoadPresets();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(ReadOnlyNotice));
        OnPropertyChanged(nameof(AdaptersTitle));
        OnPropertyChanged(nameof(NoAdaptersText));
        OnPropertyChanged(nameof(AdapterNameHeader));
        OnPropertyChanged(nameof(AdapterStatusHeader));
        OnPropertyChanged(nameof(AdapterDnsHeader));
        OnPropertyChanged(nameof(PresetsTitle));
        OnPropertyChanged(nameof(CompareTitle));
        OnPropertyChanged(nameof(HostnameLabel));
        OnPropertyChanged(nameof(CustomResolverLabel));
        OnPropertyChanged(nameof(CompareButtonText));
        OnPropertyChanged(nameof(CancelButtonText));
        OnPropertyChanged(nameof(RefreshAdaptersText));
        OnPropertyChanged(nameof(StatusText));
        LoadAdapters();
    }

    private void LoadPresets()
    {
        Presets.Clear();
        foreach (var preset in DnsDiagnosticsService.Presets)
        {
            string servers = string.Join(", ", preset.Ipv4Servers.Concat(preset.Ipv6Servers));
            Presets.Add(new PresetRow(preset.Id, preset.DisplayName, servers, preset.DohTemplate));
        }
    }

    private async Task CompareAsync()
    {
        if (IsComparing) return;
        _compareCts?.Dispose();
        _compareCts = new CancellationTokenSource();
        CancellationToken token = _compareCts.Token;

        string hostname = Hostname.Trim();
        if (string.IsNullOrWhiteSpace(hostname))
        {
            _statusText = _localization.T("Dns.HostnameRequired");
            OnPropertyChanged(nameof(StatusText));
            return;
        }

        var targets = new List<(string Label, string Ip)>();
        foreach (var preset in DnsDiagnosticsService.Presets)
        {
            if (preset.Ipv4Servers.Length > 0)
            {
                targets.Add((preset.DisplayName, preset.Ipv4Servers[0]));
            }
        }

        string custom = CustomResolver.Trim();
        if (!string.IsNullOrWhiteSpace(custom))
        {
            if (!System.Net.IPAddress.TryParse(custom, out _))
            {
                _statusText = _localization.T("Dns.CustomInvalid");
                OnPropertyChanged(nameof(StatusText));
                return;
            }

            targets.Add((_localization.T("Dns.CustomLabel"), custom));
        }

        SetComparing(true);
        PostToUi(() => Results.Clear());
        _statusText = string.Format(_localization.T("Dns.Comparing"), hostname);
        OnPropertyChanged(nameof(StatusText));

        var timeout = TimeSpan.FromSeconds(3);
        foreach (var (label, ip) in targets)
        {
            if (token.IsCancellationRequested) break;
            DnsDiagnosticsService.ResolverLatencyResult result;
            try
            {
                result = await _service.MeasureResolverLatencyAsync(hostname, ip, timeout, cancellationToken: token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                result = new DnsDiagnosticsService.ResolverLatencyResult(ip, hostname, false, 0, ex.Message);
            }

            string outcome = result.Success
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, _localization.T("Dns.LatencyValue"), result.LatencyMs)
                : result.Error ?? _localization.T("Dns.ProbeFailed");
            var row = new CompareRow($"{label} ({ip})", hostname, outcome, result.Success);
            PostToUi(() => Results.Add(row));
        }

        _statusText = token.IsCancellationRequested
            ? _localization.T("Dns.Cancelled")
            : string.Format(_localization.T("Dns.CompareDone"), Results.Count(r => r.Success), Results.Count);
        PostToUi(() => OnPropertyChanged(nameof(StatusText)));
        PostToUi(() => SetComparing(false));
    }

    private void PostToUi(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.Invoke(action);
        }
    }

    private void SetComparing(bool comparing)
    {
        if (!SetProperty(ref _isComparing, comparing)) return;
        OnPropertyChanged(nameof(IsComparing));
        OnPropertyChanged(nameof(IsCompareIdle));
    }
}
