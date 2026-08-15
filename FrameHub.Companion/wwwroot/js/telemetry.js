'use strict';

    let lastTelemetrySnapshot = null;
    function formatPercent(val) {
        if (typeof val === 'number' && isFinite(val)) return Math.round(val) + '%';
        return '--';
    }

    function formatTemp(val) {
        if (typeof val === 'number' && isFinite(val) && val > 0) return Math.round(val) + 'Â°C';
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

        // Hardware Telemetry
        const hw = telemetry.hardware;
        if (hw) {
            if (elements.hwCpuLoad) elements.hwCpuLoad.textContent = formatPercent(hw.cpuUtilizationPercent);
            if (elements.hwCpuTemp) elements.hwCpuTemp.textContent = formatTemp(hw.cpuTemperatureCelsius);
            if (elements.hwGpuLoad) elements.hwGpuLoad.textContent = formatPercent(hw.gpuUtilizationPercent);
            if (elements.hwGpuTemp) elements.hwGpuTemp.textContent = formatTemp(hw.gpuTemperatureCelsius);
            if (elements.hwRamUsage) elements.hwRamUsage.textContent = formatRam(hw.ramUsedBytes, hw.ramTotalBytes);
            if (elements.hwVramUsage) elements.hwVramUsage.textContent = formatRam(hw.vramUsedBytes, hw.vramTotalBytes);
        } else {
            resetHardwareMetrics();
        }
    }

    function resetStaleTimer() {
        if (telemetryStaleTimeout) clearTimeout(telemetryStaleTimeout);
        telemetryStaleTimeout = setTimeout(function () {
            resetTelemetryPresentation();
        }, 3500);
    }
