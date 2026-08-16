'use strict';

    let lastTelemetrySnapshot = null;
    let currentHardwareMonitorState = null;
    let isTogglingHardwareMonitor = false;

    function formatPercent(val) {
        if (typeof val === 'number' && isFinite(val)) return Math.round(val) + '%';
        return '--';
    }

    function formatTemp(val) {
        if (typeof val === 'number' && isFinite(val) && val > 0) return Math.round(val) + '°C';
        return '--';
    }

    function formatRam(usedBytes, totalBytes) {
        if (typeof usedBytes === 'number' && isFinite(usedBytes) && usedBytes > 0) {
            const usedGb = (usedBytes / (1024 * 1024 * 1024)).toFixed(1);
            if (typeof totalBytes === 'number' && isFinite(totalBytes) && totalBytes > 0) {
                const totalGb = (totalBytes / (1024 * 1024 * 1024)).toFixed(1);
                return usedGb + ' / ' + totalGb + ' GB';
            }
            return usedGb + ' GB';
        }
        return '--';
    }

    function resetLivePerformanceMetrics() {
        if (elements.liveFps) elements.liveFps.textContent = '--';
        if (elements.liveFrametime) elements.liveFrametime.textContent = '--';
        if (elements.liveOneLow) elements.liveOneLow.textContent = '--';
        if (elements.livePointOneLow) elements.livePointOneLow.textContent = '--';
    }

    function resetHardwareMetrics() {
        if (elements.hwCpuLoad) elements.hwCpuLoad.textContent = '--';
        if (elements.hwCpuTemp) elements.hwCpuTemp.textContent = '--';
        if (elements.hwGpuLoad) elements.hwGpuLoad.textContent = '--';
        if (elements.hwGpuTemp) elements.hwGpuTemp.textContent = '--';
        if (elements.hwRamUsage) elements.hwRamUsage.textContent = '--';
        if (elements.hwVramUsage) elements.hwVramUsage.textContent = '--';
        if (elements.hwCpuElevationHint) elements.hwCpuElevationHint.classList.add('hidden');
    }

    function updateHardwareMonitorUi(enabled) {
        const i18n = window.FrameHubI18n;
        const isEnabled = Boolean(enabled);

        if (elements.hwToggleDot) {
            elements.hwToggleDot.className = 'status-dot ' + (isEnabled ? 'connected' : 'disconnected');
        }
        if (elements.hwToggleText) {
            elements.hwToggleText.textContent = i18n
                ? i18n.t(isEnabled ? 'home.hwStatusOn' : 'home.hwStatusOff')
                : (isEnabled ? 'Enabled' : 'Disabled');
        }
        if (elements.hwMonitorToggleBtn) {
            elements.hwMonitorToggleBtn.title = i18n
                ? i18n.t(isEnabled ? 'home.hwDisableBtn' : 'home.hwEnableBtn')
                : (isEnabled ? 'Disable monitoring' : 'Enable monitoring');
        }

        if (isEnabled) {
            if (elements.hwGridContainer) elements.hwGridContainer.classList.remove('hidden');
            if (elements.hwDisabledNotice) elements.hwDisabledNotice.classList.add('hidden');
        } else {
            if (elements.hwGridContainer) elements.hwGridContainer.classList.add('hidden');
            if (elements.hwDisabledNotice) elements.hwDisabledNotice.classList.remove('hidden');
            if (elements.hwCpuElevationHint) elements.hwCpuElevationHint.classList.add('hidden');
            resetHardwareMetrics();
        }
    }

    function showHardwarePermissionNotice() {
        if (elements.hwPermissionNotice) {
            elements.hwPermissionNotice.classList.remove('hidden');
        }
    }

    function hideHardwarePermissionNotice() {
        if (elements.hwPermissionNotice) {
            elements.hwPermissionNotice.classList.add('hidden');
        }
    }

    async function handleToggleHardwareMonitor(targetEnabled) {
        if (isTogglingHardwareMonitor) return;
        isTogglingHardwareMonitor = true;
        if (elements.hwMonitorToggleBtn) elements.hwMonitorToggleBtn.disabled = true;
        if (elements.hwEnableBtn) elements.hwEnableBtn.disabled = true;
        hideHardwarePermissionNotice();

        try {
            const resp = await fetch('/api/v1/telemetry/hardware-monitor', {
                method: 'POST',
                headers: getAuthHeaders(),
                body: JSON.stringify({ enabled: targetEnabled })
            });

            if (resp.status === 401) {
                clearStoredCredential();
                updateAuthUi(false, 'Pairing Required');
                return;
            }

            if (resp.status === 403) {
                showHardwarePermissionNotice();
                return;
            }

            if (resp.ok) {
                const status = await resp.json();
                currentHardwareMonitorState = status;
                updateHardwareMonitorUi(status.enabled);
            }
        } catch (_) {
        } finally {
            isTogglingHardwareMonitor = false;
            if (elements.hwMonitorToggleBtn) elements.hwMonitorToggleBtn.disabled = false;
            if (elements.hwEnableBtn) elements.hwEnableBtn.disabled = false;
        }
    }

    function resetTelemetryPresentation() {
        const i18n = window.FrameHubI18n;
        lastTelemetrySnapshot = null;
        resetLivePerformanceMetrics();
        resetHardwareMetrics();
        if (elements.liveGameName) elements.liveGameName.textContent = i18n ? i18n.t('home.noGame') : 'No Game Detected';
        if (elements.liveGameBadge) {
            elements.liveGameBadge.textContent = i18n ? i18n.t('home.gameNotRunning') : 'Not Running';
            elements.liveGameBadge.className = 'badge badge-secondary';
        }
        if (elements.liveStatusDot) elements.liveStatusDot.className = 'live-indicator-dot';
    }

    function renderTelemetry(telemetry) {
        if (!telemetry) return;
        lastTelemetrySnapshot = telemetry;
        resetStaleTimer();

        const i18n = window.FrameHubI18n;
        const isBenchmarkActive = currentStatus && (currentStatus.isActive || ['Waiting', 'Capturing', 'Completing', 'Stopping'].includes(currentStatus.state));

        // Active Game Presentation
        const currentGame = telemetry.currentGame;
        if (currentGame && currentGame.isRunning) {
            if (elements.liveGameName) elements.liveGameName.textContent = currentGame.displayName || (i18n ? i18n.t('home.gameRunning') : 'Running Game');
            if (elements.liveGameBadge) {
                elements.liveGameBadge.textContent = i18n ? i18n.t('home.gameRunning') : 'Running';
                elements.liveGameBadge.className = 'badge badge-success';
            }
            if (elements.liveStatusDot) {
                elements.liveStatusDot.className = 'live-indicator-dot ' + (isBenchmarkActive ? 'benchmark-active' : 'active');
            }
        } else {
            if (elements.liveGameName) elements.liveGameName.textContent = i18n ? i18n.t('home.noGame') : 'No Game Detected';
            if (elements.liveGameBadge) {
                elements.liveGameBadge.textContent = i18n ? i18n.t('home.gameNotRunning') : 'Not Running';
                elements.liveGameBadge.className = 'badge badge-secondary';
            }
            if (elements.liveStatusDot) {
                elements.liveStatusDot.className = 'live-indicator-dot';
            }
        }

        // Benchmark vs Live Mode
        if (isBenchmarkActive) {
            if (elements.benchmarkActiveNotice) elements.benchmarkActiveNotice.classList.remove('hidden');
            resetLivePerformanceMetrics();
        } else {
            if (elements.benchmarkActiveNotice) elements.benchmarkActiveNotice.classList.add('hidden');

            const lp = telemetry.livePerformance;
            if (lp && typeof lp.currentFps === 'number' && isFinite(lp.currentFps)) {
                if (elements.liveFps) elements.liveFps.textContent = lp.currentFps.toFixed(1);
                if (elements.liveFrametime) elements.liveFrametime.textContent = typeof lp.currentFrametimeMs === 'number' ? lp.currentFrametimeMs.toFixed(1) + ' ms' : '--';
                if (elements.liveOneLow) elements.liveOneLow.textContent = typeof lp.onePercentLowFps === 'number' ? lp.onePercentLowFps.toFixed(1) : '--';
                if (elements.livePointOneLow) elements.livePointOneLow.textContent = typeof lp.pointOnePercentLowFps === 'number' ? lp.pointOnePercentLowFps.toFixed(1) : '--';
            } else {
                resetLivePerformanceMetrics();
            }
        }

        // Hardware Monitor State Sync
        if (telemetry.hardwareMonitor) {
            currentHardwareMonitorState = telemetry.hardwareMonitor;
            updateHardwareMonitorUi(telemetry.hardwareMonitor.enabled);
        }

        // Hardware Telemetry
        const isMonitorEnabled = currentHardwareMonitorState ? currentHardwareMonitorState.enabled : Boolean(telemetry.hardware);
        const hw = telemetry.hardware;
        if (isMonitorEnabled && hw) {
            if (elements.hwCpuLoad) elements.hwCpuLoad.textContent = formatPercent(hw.cpuUtilizationPercent);
            if (elements.hwCpuTemp) elements.hwCpuTemp.textContent = formatTemp(hw.cpuTemperatureCelsius);
            if (elements.hwGpuLoad) elements.hwGpuLoad.textContent = formatPercent(hw.gpuUtilizationPercent);
            if (elements.hwGpuTemp) elements.hwGpuTemp.textContent = formatTemp(hw.gpuTemperatureCelsius);
            if (elements.hwRamUsage) elements.hwRamUsage.textContent = formatRam(hw.ramUsedBytes, hw.ramTotalBytes);
            if (elements.hwVramUsage) elements.hwVramUsage.textContent = formatRam(hw.vramUsedBytes, hw.vramTotalBytes);

            // CPU Elevation Hint
            if (elements.hwCpuElevationHint) {
                const hasValidCpuTemp = typeof hw.cpuTemperatureCelsius === 'number' && isFinite(hw.cpuTemperatureCelsius) && hw.cpuTemperatureCelsius > 0;
                elements.hwCpuElevationHint.classList.toggle('hidden', hasValidCpuTemp);
            }
        } else if (isMonitorEnabled) {
            resetHardwareMetrics();
            if (elements.hwCpuElevationHint) elements.hwCpuElevationHint.classList.remove('hidden');
        } else {
            resetHardwareMetrics();
            if (elements.hwCpuElevationHint) elements.hwCpuElevationHint.classList.add('hidden');
        }
    }

    function resetStaleTimer() {
        if (telemetryStaleTimeout) clearTimeout(telemetryStaleTimeout);
        telemetryStaleTimeout = setTimeout(function () {
            resetTelemetryPresentation();
        }, 3500);
    }
