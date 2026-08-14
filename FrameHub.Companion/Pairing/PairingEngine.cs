using System.Security.Cryptography;
using System.Text;
using FrameHub.Companion.Models;
using FrameHub.Companion.Persistence;
using FrameHub.Core.Logging;

using FrameHub.Companion.Authentication;

namespace FrameHub.Companion.Pairing;

public sealed class PairingEngine
{
    private readonly DeviceRecordStore _deviceStore;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();

    private string? _activeToken;
    private bool _isClaimed;
    private DateTimeOffset? _expiresAtUtc;
    private PendingPairingRequest? _pendingRequest;
    private TaskCompletionSource<PairingApprovalResult>? _pendingTcs;
    private CancellationTokenSource? _timeoutCts;
    private CancellationToken _clientCancellationToken;

    public event EventHandler<PairingSessionStatus>? SessionStatusChanged;

    public PairingEngine(DeviceRecordStore deviceStore, Func<DateTimeOffset>? clock = null)
    {
        _deviceStore = deviceStore ?? throw new ArgumentNullException(nameof(deviceStore));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public PairingSessionStatus GetCurrentStatus()
    {
        lock (_gate)
        {
            bool active = _activeToken != null || _pendingRequest != null;
            if (_expiresAtUtc.HasValue && _clock() >= _expiresAtUtc.Value && _pendingRequest == null)
            {
                active = false;
            }

            return new PairingSessionStatus
            {
                IsActive = active,
                PairingToken = _activeToken,
                PairingUrl = active && _activeToken != null ? _currentPairingUrl : null,
                ExpiresAtUtc = _expiresAtUtc,
                PendingRequest = _pendingRequest
            };
        }
    }

    private string? _currentPairingUrl;

    public PairingSessionStatus StartPairingSession(string lanIp, int port)
    {
        lock (_gate)
        {
            CleanupSessionInternal(PairingResultStatus.Cancelled);

            byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);
            _activeToken = EncodeBase64Url(tokenBytes);
            _isClaimed = false;
            _expiresAtUtc = _clock().AddMinutes(3);
            _currentPairingUrl = $"http://{lanIp}:{port}/pair#v=1&t={_activeToken}";

            var status = GetCurrentStatus();
            NotifySessionStatusChanged(status);
            return status;
        }
    }

    public void CancelPairingSession()
    {
        lock (_gate)
        {
            CleanupSessionInternal(PairingResultStatus.Cancelled);
            NotifySessionStatusChanged(GetCurrentStatus());
        }
    }

    public async Task<PairingApprovalResult> SubmitPairingRequestAsync(
        string pairingToken,
        string displayName,
        string sourceIp,
        CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<PairingApprovalResult> tcs;
        CancellationTokenSource timeoutCts;

        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(pairingToken) || _activeToken == null || _isClaimed)
            {
                return new PairingApprovalResult(PairingResultStatus.InvalidToken);
            }

            if (_expiresAtUtc.HasValue && _clock() >= _expiresAtUtc.Value)
            {
                CleanupSessionInternal(PairingResultStatus.Timeout);
                NotifySessionStatusChanged(GetCurrentStatus());
                return new PairingApprovalResult(PairingResultStatus.Timeout);
            }

            byte[] submittedHash = SHA256.HashData(Encoding.UTF8.GetBytes(pairingToken.Trim()));
            byte[] activeHash = SHA256.HashData(Encoding.UTF8.GetBytes(_activeToken));

            if (!CryptographicOperations.FixedTimeEquals(submittedHash, activeHash))
            {
                return new PairingApprovalResult(PairingResultStatus.InvalidToken);
            }

            // IMMEDIATELY claim token & destroy pairing token secret
            _isClaimed = true;
            _activeToken = null;
            _clientCancellationToken = cancellationToken;

            _pendingRequest = new PendingPairingRequest(
                RequestId: Guid.NewGuid(),
                DisplayName: string.IsNullOrWhiteSpace(displayName) ? "Mobile Device" : displayName.Trim(),
                SourceIp: sourceIp,
                RequestedAtUtc: _clock(),
                RequestedScopes: new List<string> { CompanionScopes.ReadStatus }
            );

            _pendingTcs = new TaskCompletionSource<PairingApprovalResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs = _pendingTcs;

            _timeoutCts = new CancellationTokenSource();
            timeoutCts = _timeoutCts;

            TimeSpan remaining = _expiresAtUtc.HasValue ? _expiresAtUtc.Value - _clock() : TimeSpan.Zero;
            if (remaining <= TimeSpan.Zero)
            {
                CleanupSessionInternal(PairingResultStatus.Timeout);
                return new PairingApprovalResult(PairingResultStatus.Timeout);
            }

            timeoutCts.CancelAfter(remaining);
            timeoutCts.Token.Register(() =>
            {
                lock (_gate)
                {
                    if (_pendingTcs == tcs && !tcs.Task.IsCompleted)
                    {
                        CleanupSessionInternal(PairingResultStatus.Timeout);
                        NotifySessionStatusChanged(GetCurrentStatus());
                    }
                }
            });

            NotifySessionStatusChanged(GetCurrentStatus());
        }

        using var clientReg = cancellationToken.Register(() =>
        {
            lock (_gate)
            {
                if (_pendingTcs == tcs && !tcs.Task.IsCompleted)
                {
                    CleanupSessionInternal(PairingResultStatus.Disconnected);
                    NotifySessionStatusChanged(GetCurrentStatus());
                }
            }
        });

        return await tcs.Task.ConfigureAwait(false);
    }

    public bool AllowPendingRequest(out string? plaintextCredential, out PairedDeviceRecord? record)
    {
        lock (_gate)
        {
            plaintextCredential = null;
            record = null;

            if (_pendingRequest == null || _pendingTcs == null || _pendingTcs.Task.IsCompleted)
            {
                return false;
            }

            if (_clientCancellationToken.IsCancellationRequested)
            {
                CleanupSessionInternal(PairingResultStatus.Disconnected);
                NotifySessionStatusChanged(GetCurrentStatus());
                return false;
            }

            if (_deviceStore.IsFaulted)
            {
                var faultResult = new PairingApprovalResult(PairingResultStatus.StoreFaulted);
                _pendingTcs.TrySetResult(faultResult);
                CleanupSessionInternal(PairingResultStatus.StoreFaulted);
                NotifySessionStatusChanged(GetCurrentStatus());
                return false;
            }

            byte[] credentialBytes = RandomNumberGenerator.GetBytes(32);
            plaintextCredential = EncodeBase64Url(credentialBytes);
            string hash = HashCredential(plaintextCredential);

            record = new PairedDeviceRecord
            {
                Id = Guid.NewGuid(),
                DisplayName = _pendingRequest.DisplayName,
                CredentialHash = hash,
                CreatedAtUtc = _clock(),
                LastUsedAtUtc = null,
                Scopes = new List<string> { CompanionScopes.ReadStatus }
            };

            if (!_deviceStore.AddDevice(record))
            {
                plaintextCredential = null;
                record = null;
                _pendingTcs.TrySetResult(new PairingApprovalResult(PairingResultStatus.StoreFaulted));
                CleanupSessionInternal(PairingResultStatus.StoreFaulted);
                NotifySessionStatusChanged(GetCurrentStatus());
                return false;
            }

            var approvedResult = new PairingApprovalResult(PairingResultStatus.Approved, plaintextCredential, record);
            _pendingTcs.TrySetResult(approvedResult);

            CleanupSessionInternal(PairingResultStatus.Approved);
            NotifySessionStatusChanged(GetCurrentStatus());
            return true;
        }
    }

    public bool DenyPendingRequest()
    {
        lock (_gate)
        {
            if (_pendingRequest == null || _pendingTcs == null)
            {
                return false;
            }

            var deniedResult = new PairingApprovalResult(PairingResultStatus.Denied);
            _pendingTcs.TrySetResult(deniedResult);
            CleanupSessionInternal(PairingResultStatus.Denied);
            NotifySessionStatusChanged(GetCurrentStatus());
            return true;
        }
    }

    private void CleanupSessionInternal(PairingResultStatus defaultStatus)
    {
        _activeToken = null;
        _isClaimed = true;
        _expiresAtUtc = null;
        _currentPairingUrl = null;
        _clientCancellationToken = default;

        if (_timeoutCts != null)
        {
            var cts = _timeoutCts;
            _timeoutCts = null;
            try { cts.CancelAfter(Timeout.InfiniteTimeSpan); } catch { }
            try { cts.Dispose(); } catch { }
        }

        if (_pendingTcs != null && !_pendingTcs.Task.IsCompleted)
        {
            _pendingTcs.TrySetResult(new PairingApprovalResult(defaultStatus));
        }
        _pendingTcs = null;
        _pendingRequest = null;
    }

    private void NotifySessionStatusChanged(PairingSessionStatus status)
    {
        var handlers = SessionStatusChanged;
        if (handlers == null) return;
        foreach (EventHandler<PairingSessionStatus> handler in handlers.GetInvocationList())
        {
            try { handler(this, status); }
            catch (Exception ex) { LoggerService.Instance.Warn($"Pairing status subscriber failed: {ex.Message}"); }
        }
    }

    public static string EncodeBase64Url(byte[] data)
    {
        return Convert.ToBase64String(data)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public static string HashCredential(string credential)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(credential.Trim());
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
