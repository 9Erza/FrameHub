(function () {
    'use strict';

    const STORAGE_KEY = 'companion_credential';
    let pollIntervalId = null;
    let isPollPending = false;
    let currentStatus = null;
    let lastCompletedSessionId = null;
    let hasFetchedResultForCurrentCompletedState = false;
    let isFetchingCompletedResult = false;
    let activeTab = 'home';
    let desktopLanguageSynced = false;
    let lastAuthUiPaired = null;

    // DOM Elements
    const elements = {
        authBadge: document.getElementById('auth-status-badge'),
        authText: document.getElementById('auth-status-text'),
        pairingSection: document.getElementById('pairing-section'),
        pairingForm: document.getElementById('pairing-form'),
        pairingToken: document.getElementById('pairing-token'),
        displayName: document.getElementById('display-name'),
        btnPair: document.getElementById('btn-pair'),
        pairingPending: document.getElementById('pairing-pending'),
        pairingError: document.getElementById('pairing-error'),
        globalError: document.getElementById('global-error'),

        appNav: document.getElementById('app-nav'),
        homeView: document.getElementById('home-view'),
        libraryView: document.getElementById('library-view'),
        benchmarksView: document.getElementById('benchmarks-view'),
        settingsView: document.getElementById('settings-view'),
        navTabHome: document.getElementById('nav-tab-home'),
        navTabLibrary: document.getElementById('nav-tab-library'),
        navTabBenchmarks: document.getElementById('nav-tab-benchmarks'),
        navTabSettings: document.getElementById('nav-tab-settings'),
        languageSelect: document.getElementById('language-select'),

        btnRefreshLibrary: document.getElementById('btn-refresh-library'),
        libraryLoading: document.getElementById('library-loading'),
        libraryEmpty: document.getElementById('library-empty'),
        libraryError: document.getElementById('library-error'),
        libraryList: document.getElementById('library-list'),
        btnRefreshBackgroundApps: document.getElementById('btn-refresh-background-apps'),
        backgroundAppsLoading: document.getElementById('background-apps-loading'),
        backgroundAppsEmpty: document.getElementById('background-apps-empty'),
        backgroundAppsError: document.getElementById('background-apps-error'),
        backgroundAppsList: document.getElementById('background-apps-list'),

        targetSelect: document.getElementById('target-select'),
        targetCountBadge: document.getElementById('target-count-badge'),
        btnRefreshTargets: document.getElementById('btn-refresh-targets'),
        durationSelect: document.getElementById('duration-select'),
        countdownSelect: document.getElementById('countdown-select'),
        btnStart: document.getElementById('btn-start'),
        btnStop: document.getElementById('btn-stop'),
        stateBadge: document.getElementById('state-badge'),
        stateText: document.getElementById('state-text'),
        statusTarget: document.getElementById('status-target'),
        statusCountdown: document.getElementById('status-countdown'),
        statusElapsed: document.getElementById('status-elapsed'),
        progressBar: document.getElementById('status-progress-bar'),
        resultSection: document.getElementById('result-section'),
        resGameName: document.getElementById('result-game-name'),
        resAvgFps: document.getElementById('res-avg-fps'),
        resOneLow: document.getElementById('res-one-low'),
        resPointOneLow: document.getElementById('res-point-one-low'),
        resP99Frametime: document.getElementById('res-p99-frametime'),
        resDuration: document.getElementById('res-duration'),
        resQuality: document.getElementById('res-quality'),
        frametimeCanvas: document.getElementById('frametime-canvas'),
        chartPointCount: document.getElementById('chart-point-count'),
        btnCompareSessions: document.getElementById('btn-compare-sessions'),
        comparisonSection: document.getElementById('comparison-section'),
        btnCloseComparison: document.getElementById('btn-close-comparison'),
        compAName: document.getElementById('comp-a-name'),
        compADate: document.getElementById('comp-a-date'),
        compBName: document.getElementById('comp-b-name'),
        compBDate: document.getElementById('comp-b-date'),
        comparisonTableBody: document.getElementById('comparison-table-body'),
        btnRefreshHistory: document.getElementById('btn-refresh-history'),
        historyTableBody: document.getElementById('history-table-body'),
        liveDashboardSection: document.getElementById('live-dashboard-section'),
        liveGameName: document.getElementById('live-game-name'),
        liveGameBadge: document.getElementById('live-game-badge'),
        liveStatusDot: document.getElementById('live-status-dot'),
        benchmarkActiveNotice: document.getElementById('benchmark-active-notice'),
        liveFps: document.getElementById('live-fps'),
        liveFrametime: document.getElementById('live-frametime'),
        liveOneLow: document.getElementById('live-one-low'),
        livePointOneLow: document.getElementById('live-point-one-low'),
        hwCpuLoad: document.getElementById('hw-cpu-load'),
        hwCpuTemp: document.getElementById('hw-cpu-temp'),
        hwGpuLoad: document.getElementById('hw-gpu-load'),
        hwGpuTemp: document.getElementById('hw-gpu-temp'),
        hwRamUsage: document.getElementById('hw-ram-usage'),
        hwVramUsage: document.getElementById('hw-vram-usage'),

        optSection: document.getElementById('session-optimization-section'),
        optStateBadge: document.getElementById('opt-state-badge'),
        optGameName: document.getElementById('opt-game-name'),
        optSuspendedCount: document.getElementById('opt-suspended-count'),
        optTaskbarBadge: document.getElementById('opt-taskbar-badge'),
        optRecoveryBadge: document.getElementById('opt-recovery-badge'),
        btnApplyOpt: document.getElementById('btn-apply-optimization'),
        btnRestoreOpt: document.getElementById('btn-restore-optimization'),
        btnRefreshOpt: document.getElementById('btn-refresh-optimization'),
        optFeedback: document.getElementById('opt-feedback')
    };

    let selectedSessionIds = new Set();

    // Navigation Tab Switching
    function switchTab(tabName) {
        if (!['home', 'library', 'benchmarks', 'settings'].includes(tabName)) return;
        activeTab = tabName;

        if (elements.homeView) elements.homeView.classList.toggle('hidden', activeTab !== 'home');
        if (elements.libraryView) elements.libraryView.classList.toggle('hidden', activeTab !== 'library');
        if (elements.benchmarksView) elements.benchmarksView.classList.toggle('hidden', activeTab !== 'benchmarks');
        if (elements.settingsView) elements.settingsView.classList.toggle('hidden', activeTab !== 'settings');

        const navItems = [
            { el: elements.navTabHome, tab: 'home' },
            { el: elements.navTabLibrary, tab: 'library' },
            { el: elements.navTabBenchmarks, tab: 'benchmarks' },
            { el: elements.navTabSettings, tab: 'settings' }
        ];

        navItems.forEach(function (item) {
            if (item.el) {
                const isActive = item.tab === activeTab;
                item.el.classList.toggle('active', isActive);
                item.el.setAttribute('aria-selected', isActive ? 'true' : 'false');
            }
        });

        if (activeTab === 'library') {
            fetchLibraryItems();
            fetchBackgroundApps();
        } else if (activeTab === 'home') {
            fetchOptimizationState();
        }
    }

    // Helper: Auth Header
    function getAuthHeaders() {
        const headers = { 'Content-Type': 'application/json' };
        const credential = sessionStorage.getItem(STORAGE_KEY);
        if (credential) {
            headers['Authorization'] = 'Bearer ' + credential;
        }
        return headers;
    }

    // Helper: Safe Error Message
    function getFriendlyErrorMessage(err, defaultMsg) {
        if (err && err.message) {
            return err.message;
        }
        return defaultMsg;
    }

    function showGlobalError(msg) {
        if (!msg) {
            elements.globalError.classList.add('hidden');
            return;
        }
        elements.globalError.textContent = msg;
        elements.globalError.classList.remove('hidden');
    }

    function hideGlobalError() {
        elements.globalError.classList.add('hidden');
    }

    // URL Fragment Parser (#v=1&t=...)
    function checkUrlPairingToken() {
        const hash = window.location.hash;
        if (hash) {
            const match = hash.match(/[#&]t=([^&]+)/);
            if (match && match[1]) {
                elements.pairingToken.value = decodeURIComponent(match[1]);
                if (window.history && typeof window.history.replaceState === 'function') {
                    const cleanUrl = window.location.pathname + window.location.search;
                    window.history.replaceState(null, '', cleanUrl);
                }
            }
        }
    }

    // Auth & Connection UI Update
    function updateAuthUi(isPaired, statusMsg) {
        const dot = elements.authBadge.querySelector('.status-dot');
        dot.className = 'status-dot ' + (isPaired ? 'connected' : 'disconnected');

        const i18n = window.FrameHubI18n;
        let text = statusMsg;
        if (i18n) {
            if (isPaired) text = i18n.t('auth.connected');
            else if (statusMsg === 'Pairing Required') text = i18n.t('auth.required');
            else text = i18n.t('auth.disconnected');
        }
        elements.authText.textContent = text;

        const authStateChanged = lastAuthUiPaired !== isPaired;
        lastAuthUiPaired = isPaired;

        if (authStateChanged && !isPaired) {
            teardownTelemetryConnection(true);
        }

        if (isPaired) {
            elements.pairingSection.classList.add('hidden');
            if (elements.appNav) elements.appNav.classList.remove('hidden');
            if (authStateChanged) switchTab(activeTab);
        } else {
            elements.pairingSection.classList.remove('hidden');
            if (elements.appNav) elements.appNav.classList.add('hidden');
            if (elements.homeView) elements.homeView.classList.add('hidden');
            if (elements.libraryView) elements.libraryView.classList.add('hidden');
            if (elements.benchmarksView) elements.benchmarksView.classList.add('hidden');
            if (elements.settingsView) elements.settingsView.classList.add('hidden');
        }
    }

    // Single post-auth language sync with Desktop
    async function syncDesktopLanguageOnce() {
        if (desktopLanguageSynced) return;
        desktopLanguageSynced = true;

        try {
            const resp = await fetch('/api/v1/status', { headers: getAuthHeaders() });
            if (resp.ok) {
                const statusData = await resp.json();
                if (statusData && statusData.desktopLanguage) {
                    let hasExplicit = false;
                    try {
                        hasExplicit = !!localStorage.getItem('companion_language');
                    } catch (_) { }

                    if (!hasExplicit && window.FrameHubI18n) {
                        window.FrameHubI18n.setLanguage(statusData.desktopLanguage, false);
                        if (elements.languageSelect) {
                            elements.languageSelect.value = window.FrameHubI18n.getCurrentLanguage();
                        }
                    }
                }
            }
        } catch (_) { }
    }

    // Canvas Frametime Chart Drawer
    function drawFrametimeChart(points) {
        const canvas = elements.frametimeCanvas;
        if (!canvas) return;
        const ctx = canvas.getContext('2d');
        const width = canvas.width;
        const height = canvas.height;

        ctx.clearRect(0, 0, width, height);

        const i18n = window.FrameHubI18n;

        if (!points || !Array.isArray(points) || points.length === 0) {
            elements.chartPointCount.textContent = i18n ? i18n.t('result.noChartData') : 'No chart points';
            ctx.fillStyle = '#64748b';
            ctx.font = '14px sans-serif';
            ctx.fillText(i18n ? i18n.t('result.noChartData') : 'No frametime data available', width / 2 - 80, height / 2);
            return;
        }

        const ptsText = i18n ? i18n.t('result.chartPoints') : 'points (Downsampled)';
        elements.chartPointCount.textContent = points.length + ' ' + ptsText;

        const padding = { top: 20, right: 20, bottom: 30, left: 50 };
        const plotW = width - padding.left - padding.right;
        const plotH = height - padding.top - padding.bottom;

        let minX = points[0].elapsedSeconds;
        let maxX = points[points.length - 1].elapsedSeconds;
        if (maxX <= minX) maxX = minX + 1;

        let minY = Infinity;
        let maxY = -Infinity;
        points.forEach(p => {
            if (p.frameTimeMs < minY) minY = p.frameTimeMs;
            if (p.frameTimeMs > maxY) maxY = p.frameTimeMs;
        });
        if (!isFinite(minY)) minY = 0;
        if (!isFinite(maxY) || maxY <= minY) maxY = minY + 10;

        const yMargin = (maxY - minY) * 0.1 || 2;
        minY = Math.max(0, minY - yMargin);
        maxY = maxY + yMargin;

        ctx.strokeStyle = '#334155';
        ctx.lineWidth = 1;
        ctx.fillStyle = '#94a3b8';
        ctx.font = '11px sans-serif';

        const yGridCount = 4;
        for (let i = 0; i <= yGridCount; i++) {
            const val = minY + (maxY - minY) * (i / yGridCount);
            const yPos = padding.top + plotH - (i / yGridCount) * plotH;

            ctx.beginPath();
            ctx.moveTo(padding.left, yPos);
            ctx.lineTo(width - padding.right, yPos);
            ctx.stroke();

            ctx.fillText(val.toFixed(1) + 'ms', 5, yPos + 4);
        }

        const xGridCount = 5;
        for (let i = 0; i <= xGridCount; i++) {
            const val = minX + (maxX - minX) * (i / xGridCount);
            const xPos = padding.left + (i / xGridCount) * plotW;
            ctx.fillText(val.toFixed(1) + 's', xPos - 10, height - 8);
        }

        ctx.beginPath();
        ctx.strokeStyle = '#3b82f6';
        ctx.lineWidth = 2;

        points.forEach((p, idx) => {
            const x = padding.left + ((p.elapsedSeconds - minX) / (maxX - minX)) * plotW;
            const y = padding.top + plotH - ((p.frameTimeMs - minY) / (maxY - minY)) * plotH;
            if (idx === 0) {
                ctx.moveTo(x, y);
            } else {
                ctx.lineTo(x, y);
            }
        });
        ctx.stroke();
    }

    async function fetchSessionChart(sessionId) {
        try {
            const response = await fetch('/api/v1/benchmarks/history/' + sessionId + '/chart?buckets=200', {
                headers: getAuthHeaders()
            });
            if (!response.ok) {
                drawFrametimeChart(null);
                return;
            }
            const chartData = await response.json();
            if (chartData && Array.isArray(chartData.points)) {
                drawFrametimeChart(chartData.points);
            } else {
                drawFrametimeChart(null);
            }
        } catch (_) {
            drawFrametimeChart(null);
        }
    }

    // Pairing Submit
    async function handlePairingSubmit(e) {
        e.preventDefault();
        const token = elements.pairingToken.value.trim();
        const name = elements.displayName.value.trim() || 'Companion Client';

        if (!token) return;

        elements.btnPair.disabled = true;
        elements.pairingPending.classList.remove('hidden');
        elements.pairingError.classList.add('hidden');

        try {
            const response = await fetch('/api/v1/pairing/request', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ pairingToken: token, displayName: name })
            });

            if (response.ok) {
                const data = await response.json();
                if (data.credential) {
                    sessionStorage.setItem(STORAGE_KEY, data.credential);
                    updateAuthUi(true, 'Paired Device');
                    syncDesktopLanguageOnce();
                    elements.pairingPending.classList.add('hidden');
                    loadTargets();
                    fetchHistory();
                    fetchStatus();
                    resetTelemetryTransportForNewCredential();
                    initTelemetryConnection();
                } else {
                    throw new Error('Server response contained no credential.');
                }
            } else {
                let errorData = null;
                try { errorData = await response.json(); } catch (_) { }
                const msg = errorData && errorData.message ? errorData.message : 'Pairing failed (Status: ' + response.status + ')';
                elements.pairingError.textContent = msg;
                elements.pairingError.classList.remove('hidden');
            }
        } catch (err) {
            elements.pairingError.textContent = getFriendlyErrorMessage(err, 'Network error during pairing request.');
            elements.pairingError.classList.remove('hidden');
        } finally {
            elements.btnPair.disabled = false;
            elements.pairingPending.classList.add('hidden');
        }
    }

    // Fetch Eligible Targets
    async function loadTargets() {
        try {
            const response = await fetch('/api/v1/benchmarks/targets', {
                headers: getAuthHeaders()
            });

            if (response.status === 401) {
                updateAuthUi(false, 'Pairing Required');
                return;
            }
            if (response.status === 403) {
                elements.targetCountBadge.textContent = window.FrameHubI18n ? window.FrameHubI18n.t('benchmark.targetsUnavailable') : 'Targets unavailable';
                return;
            }

            if (!response.ok) throw new Error('Failed to load benchmark targets.');

            const targets = await response.json();
            elements.targetSelect.innerHTML = '';

            const i18n = window.FrameHubI18n;
            const availText = i18n ? i18n.t('benchmark.targetsAvailable') : 'available';

            if (Array.isArray(targets) && targets.length > 0) {
                targets.forEach(t => {
                    const opt = document.createElement('option');
                    opt.value = t.targetId;
                    opt.textContent = t.displayName || t.targetId;
                    elements.targetSelect.appendChild(opt);
                });
                elements.targetSelect.disabled = false;
                elements.targetCountBadge.textContent = targets.length + ' ' + availText;
            } else {
                const opt = document.createElement('option');
                opt.value = '';
                opt.textContent = i18n ? i18n.t('benchmark.noTargets') : 'No running games detected';
                elements.targetSelect.appendChild(opt);
                elements.targetSelect.disabled = true;
                elements.targetCountBadge.textContent = '0 ' + availText;
            }
        } catch (err) {
            const unavailText = window.FrameHubI18n ? window.FrameHubI18n.t('benchmark.targetsUnavailable') : 'Targets unavailable';
            elements.targetCountBadge.textContent = unavailText;
        }
    }

    // Poll Status
    async function fetchStatus() {
        if (isPollPending) return;
        isPollPending = true;

        try {
            const response = await fetch('/api/v1/benchmarks/status', {
                headers: getAuthHeaders()
            });

            if (response.status === 401) {
                updateAuthUi(false, 'Pairing Required');
                return;
            } else if (response.status === 403) {
                if (sessionStorage.getItem(STORAGE_KEY)) updateAuthUi(true, 'Paired Device');
                return;
            } else if (response.ok) {
                const credential = sessionStorage.getItem(STORAGE_KEY);
                if (credential) {
                    updateAuthUi(true, 'Paired Device');
                    syncDesktopLanguageOnce();
                } else {
                    updateAuthUi(false, 'Localhost / Unpaired');
                }
            }

            if (response.ok) {
                const status = await response.json();
                currentStatus = status;
                renderStatus(status);
                hideGlobalError();
            } else if (response.status === 503) {
                const i18n = window.FrameHubI18n;
                showGlobalError(i18n ? i18n.t('errors.serviceUnavailable') : 'Benchmark provider service is unavailable.');
            }
        } catch (err) {
            // Silently ignore minor intermittent polling network errors
        } finally {
            isPollPending = false;
        }
    }

    // Render Status
    function renderStatus(status) {
        const state = status.state || 'Idle';
        const isActive = !!status.isActive;

        if (state !== 'Completed') {
            hasFetchedResultForCurrentCompletedState = false;
        }

        const i18n = window.FrameHubI18n;

        // State Badge
        elements.stateText.textContent = i18n ? i18n.translateState(state) : state;
        elements.stateBadge.className = 'state-pill state-' + state.toLowerCase();

        // Target Name
        const noneSelectedText = i18n ? i18n.t('benchmark.noneSelected') : 'None selected';
        elements.statusTarget.textContent = status.targetDisplayName || noneSelectedText;

        // Countdown
        if (state === 'Waiting' && typeof status.remainingCountdownSeconds === 'number') {
            elements.statusCountdown.textContent = status.remainingCountdownSeconds + 's';
        } else {
            elements.statusCountdown.textContent = '--';
        }

        // Elapsed Time
        const elapsed = typeof status.elapsedSeconds === 'number' ? status.elapsedSeconds.toFixed(1) : '0.0';
        elements.statusElapsed.textContent = elapsed + 's';

        // Progress Bar
        const configuredDuration = parseInt(elements.durationSelect.value, 10) || 60;
        let pct = 0;
        if (isActive && typeof status.elapsedSeconds === 'number' && configuredDuration > 0) {
            pct = Math.min(100, Math.max(0, (status.elapsedSeconds / configuredDuration) * 100));
        } else if (state === 'Completed') {
            pct = 100;
        }
        elements.progressBar.style.width = pct + '%';

        // Buttons Enable/Disable logic derived strictly from backend status
        const isTargetSelected = !!elements.targetSelect.value;
        elements.btnStart.disabled = isActive || !isTargetSelected;
        elements.btnStop.disabled = !(state === 'Waiting' || state === 'Capturing');

        // Error message if Failed
        if (state === 'Failed' && status.errorCode) {
            const failPrefix = i18n ? i18n.t('errors.captureFailed') : 'Benchmark capture failed: ';
            showGlobalError(failPrefix + status.errorCode);
        }

        // If newly completed, load detailed result once
        if (state === 'Completed' && !hasFetchedResultForCurrentCompletedState && !isFetchingCompletedResult) {
            fetchLatestResult();
        }

        if (lastTelemetrySnapshot) {
            renderTelemetry(lastTelemetrySnapshot);
        } else {
            const isBenchmarkActive = status && (status.isActive || ['Waiting', 'Capturing', 'Completing', 'Stopping'].includes(status.state));
            if (elements.benchmarkActiveNotice) {
                if (isBenchmarkActive) {
                    elements.benchmarkActiveNotice.classList.remove('hidden');
                    resetLivePerformanceMetrics();
                } else {
                    elements.benchmarkActiveNotice.classList.add('hidden');
                }
            }
        }
    }

    // Telemetry Module (M9.2 Companion Live Dashboard)
    let lastTelemetrySnapshot = null;
    let wsInstance = null;
    let telemetryPollInterval = null;
    let telemetryStaleTimeout = null;
    let telemetryReconnectTimeout = null;
    let telemetryConnectingGeneration = null;
    let telemetryConnectionGeneration = 0;
    let telemetryHttpRequestId = 0;
    let isUnloading = false;
    let telemetryTicketRetryAfter = 0;

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

    async function initTelemetryConnection() {
        const generation = telemetryConnectionGeneration;
        if (isUnloading || lastAuthUiPaired === false || telemetryConnectingGeneration === generation) return;
        if (telemetryReconnectTimeout) return;
        if (Date.now() < telemetryTicketRetryAfter) {
            scheduleTelemetryReconnect(Math.max(1, telemetryTicketRetryAfter - Date.now()), generation);
            return;
        }
        if (wsInstance && (wsInstance.readyState === WebSocket.OPEN || wsInstance.readyState === WebSocket.CONNECTING)) return;
        telemetryConnectingGeneration = generation;
        try {
            const ticketResp = await fetch('/api/v1/telemetry/ws-ticket', {
                method: 'POST',
                headers: getAuthHeaders()
            });

            if (generation !== telemetryConnectionGeneration || isUnloading || lastAuthUiPaired === false) return;

            if (ticketResp.ok) {
                const ticketData = await ticketResp.json();
                if (ticketData && ticketData.ticket) {
                    telemetryTicketRetryAfter = 0;
                    connectWebSocket(ticketData.ticket, generation);
                    return;
                }
            } else if (ticketResp.status === 401) {
                updateAuthUi(false, 'Pairing Required');
                return;
            } else if (ticketResp.status === 403) {
                telemetryTicketRetryAfter = Date.now() + 30000;
                scheduleTelemetryReconnect(30000, generation);
            } else {
                telemetryTicketRetryAfter = Date.now() + 3000;
                scheduleTelemetryReconnect(3000, generation);
            }
        } catch (_) {
            telemetryTicketRetryAfter = Date.now() + 3000;
            scheduleTelemetryReconnect(3000, generation);
        }
        finally {
            if (telemetryConnectingGeneration === generation) telemetryConnectingGeneration = null;
        }

        if (generation === telemetryConnectionGeneration && lastAuthUiPaired !== false) startTelemetryPolling();
    }

    function connectWebSocket(ticket, generation) {
        if (generation !== telemetryConnectionGeneration || isUnloading || lastAuthUiPaired === false) return;
        if (wsInstance) {
            wsInstance.onclose = null;
            try { wsInstance.close(); } catch (_) { }
        }

        const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
        const wsUrl = protocol + '//' + window.location.host + '/api/v1/telemetry/ws';

        try {
            const socket = new WebSocket(wsUrl, ['framehub.v1', 'ticket.' + ticket]);
            wsInstance = socket;

            socket.onopen = function () {
                if (wsInstance !== socket || generation !== telemetryConnectionGeneration || lastAuthUiPaired === false) return;
                telemetryTicketRetryAfter = 0;
                cancelTelemetryReconnect();
                stopTelemetryPolling();
            };

            socket.onmessage = function (evt) {
                if (wsInstance !== socket || generation !== telemetryConnectionGeneration || lastAuthUiPaired === false) return;
                try {
                    const data = JSON.parse(evt.data);
                    renderTelemetry(data);
                } catch (_) { }
            };

            socket.onclose = function () {
                if (wsInstance !== socket || generation !== telemetryConnectionGeneration) return;
                wsInstance = null;
                if (isUnloading || lastAuthUiPaired === false) return;
                startTelemetryPolling();
                scheduleTelemetryReconnect(3000, generation);
            };

            socket.onerror = function () {
                try { socket.close(); } catch (_) { }
            };
        } catch (_) {
            startTelemetryPolling();
            scheduleTelemetryReconnect(3000, generation);
        }
    }

    function scheduleTelemetryReconnect(delayMs, generation) {
        if (isUnloading || lastAuthUiPaired === false || generation !== telemetryConnectionGeneration || telemetryReconnectTimeout) return;
        telemetryReconnectTimeout = setTimeout(function () {
            telemetryReconnectTimeout = null;
            if (generation === telemetryConnectionGeneration && lastAuthUiPaired !== false) {
                initTelemetryConnection();
            }
        }, delayMs);
    }

    function cancelTelemetryReconnect() {
        if (!telemetryReconnectTimeout) return;
        clearTimeout(telemetryReconnectTimeout);
        telemetryReconnectTimeout = null;
    }

    function resetTelemetryTransportForNewCredential() {
        telemetryConnectionGeneration++;
        telemetryConnectingGeneration = null;
        telemetryTicketRetryAfter = 0;
        cancelTelemetryReconnect();
    }

    function teardownTelemetryConnection(resetPresentation) {
        telemetryConnectionGeneration++;
        telemetryConnectingGeneration = null;
        telemetryTicketRetryAfter = 0;
        cancelTelemetryReconnect();

        const socket = wsInstance;
        wsInstance = null;
        if (socket) {
            socket.onopen = null;
            socket.onmessage = null;
            socket.onerror = null;
            socket.onclose = null;
            try { socket.close(); } catch (_) { }
        }

        stopTelemetryPolling();
        if (telemetryStaleTimeout) {
            clearTimeout(telemetryStaleTimeout);
            telemetryStaleTimeout = null;
        }
        if (resetPresentation) resetTelemetryPresentation();
    }

    function startTelemetryPolling() {
        if (telemetryPollInterval || isUnloading || lastAuthUiPaired === false) return;
        fetchTelemetryOnce();
        telemetryPollInterval = setInterval(fetchTelemetryOnce, 1000);
    }

    function stopTelemetryPolling() {
        if (!telemetryPollInterval) return;
        clearInterval(telemetryPollInterval);
        telemetryPollInterval = null;
    }

    async function fetchTelemetryOnce() {
        if (wsInstance && wsInstance.readyState === WebSocket.OPEN) return;
        const generation = telemetryConnectionGeneration;
        const expectedPairedState = lastAuthUiPaired;
        const expectedCredential = sessionStorage.getItem(STORAGE_KEY);
        const requestId = ++telemetryHttpRequestId;
        if (isUnloading || expectedPairedState === false) return;

        const ownsRequest = function () {
            return requestId === telemetryHttpRequestId
                && generation === telemetryConnectionGeneration
                && !isUnloading
                && lastAuthUiPaired === expectedPairedState
                && lastAuthUiPaired !== false
                && sessionStorage.getItem(STORAGE_KEY) === expectedCredential
                && !(wsInstance && wsInstance.readyState === WebSocket.OPEN);
        };

        try {
            const resp = await fetch('/api/v1/telemetry', { headers: getAuthHeaders() });
            if (!ownsRequest()) return;

            if (resp.status === 401) {
                updateAuthUi(false, 'Pairing Required');
                return;
            }

            if (resp.ok) {
                const data = await resp.json();
                if (!ownsRequest()) return;
                renderTelemetry(data);
                if (sessionStorage.getItem(STORAGE_KEY) && !wsInstance) {
                    initTelemetryConnection();
                }
            }
        } catch (_) { }
    }

    // Start Benchmark
    async function handleStart() {
        const targetId = elements.targetSelect.value;
        const duration = parseInt(elements.durationSelect.value, 10) || 60;
        const countdown = parseInt(elements.countdownSelect.value, 10) || 0;

        if (!targetId) {
            showGlobalError('Please select a valid benchmark target.');
            return;
        }

        elements.btnStart.disabled = true;
        hideGlobalError();

        try {
            const response = await fetch('/api/v1/benchmarks/start', {
                method: 'POST',
                headers: getAuthHeaders(),
                body: JSON.stringify({
                    targetId: targetId,
                    durationSeconds: duration,
                    countdownSeconds: countdown
                })
            });

            if (response.status === 409) {
                showGlobalError('A benchmark capture is already in progress.');
            } else if (!response.ok) {
                let errData = null;
                try { errData = await response.json(); } catch (_) { }
                showGlobalError(errData && errData.message ? errData.message : 'Failed to start benchmark.');
            } else {
                fetchStatus();
            }
        } catch (err) {
            showGlobalError(getFriendlyErrorMessage(err, 'Network error starting benchmark.'));
        }
    }

    // Stop Benchmark
    async function handleStop() {
        elements.btnStop.disabled = true;
        hideGlobalError();

        try {
            const response = await fetch('/api/v1/benchmarks/stop', {
                method: 'POST',
                headers: getAuthHeaders()
            });

            if (!response.ok) {
                let errData = null;
                try { errData = await response.json(); } catch (_) { }
                showGlobalError(errData && errData.message ? errData.message : 'Failed to stop benchmark.');
            } else {
                fetchStatus();
            }
        } catch (err) {
            showGlobalError(getFriendlyErrorMessage(err, 'Network error stopping benchmark.'));
        }
    }

    // Fetch Latest Completed Result
    async function fetchLatestResult() {
        if (isFetchingCompletedResult) return;
        isFetchingCompletedResult = true;

        try {
            const response = await fetch('/api/v1/benchmarks/history?limit=1', {
                headers: getAuthHeaders()
            });
            if (!response.ok) return;

            const listData = await response.json();
            if (listData && Array.isArray(listData.sessions) && listData.sessions.length > 0) {
                const latestSummary = listData.sessions[0];
                if (latestSummary.sessionId === lastCompletedSessionId) {
                    hasFetchedResultForCurrentCompletedState = true;
                    return;
                }

                const detailResponse = await fetch('/api/v1/benchmarks/history/' + latestSummary.sessionId, {
                    headers: getAuthHeaders()
                });
                if (!detailResponse.ok) return;

                const detail = await detailResponse.json();
                lastCompletedSessionId = latestSummary.sessionId;
                renderResultDetail(detail);
                fetchHistory();
                hasFetchedResultForCurrentCompletedState = true;
            }
        } catch (_) {
        } finally {
            isFetchingCompletedResult = false;
        }
    }

    function renderResultDetail(detail) {
        elements.resGameName.textContent = detail.gameDisplayName || 'Benchmark Session';
        elements.resAvgFps.textContent = typeof detail.averageFps === 'number' ? detail.averageFps.toFixed(1) : '--';
        elements.resOneLow.textContent = typeof detail.onePercentLowFps === 'number' ? detail.onePercentLowFps.toFixed(1) : '--';
        elements.resPointOneLow.textContent = typeof detail.pointOnePercentLowFps === 'number' ? detail.pointOnePercentLowFps.toFixed(1) : '--';
        elements.resP99Frametime.textContent = typeof detail.p99FrameTimeMs === 'number' ? detail.p99FrameTimeMs.toFixed(1) + ' ms' : '--';
        elements.resDuration.textContent = typeof detail.durationSeconds === 'number' ? detail.durationSeconds.toFixed(1) + 's' : '--';
        elements.resQuality.textContent = detail.qualityLevel || 'Valid';

        elements.resultSection.classList.remove('hidden');

        if (detail.sessionId) {
            fetchSessionChart(detail.sessionId);
        }
    }

    function updateCompareButtonState() {
        const count = selectedSessionIds.size;
        const i18n = window.FrameHubI18n;
        const label = i18n ? i18n.t('history.compareBtn') : 'Compare Selected';
        elements.btnCompareSessions.textContent = label + ' (' + count + '/2)';
        elements.btnCompareSessions.disabled = count !== 2;
    }

    function handleCompareClick() {
        if (selectedSessionIds.size !== 2) return;
        const arr = Array.from(selectedSessionIds);
        fetchComparison(arr[0], arr[1]);
    }

    function handleCloseComparison() {
        elements.comparisonSection.classList.add('hidden');
    }

    async function fetchComparison(sessionAId, sessionBId) {
        const i18n = window.FrameHubI18n;
        elements.comparisonSection.classList.remove('hidden');
        const loadingText = i18n ? i18n.t('comparison.loading') : 'Loading comparison...';
        elements.comparisonTableBody.innerHTML = '<tr><td colspan="6" class="text-center text-muted">' + loadingText + '</td></tr>';

        try {
            const response = await fetch('/api/v1/benchmarks/history/compare?sessionA=' + sessionAId + '&sessionB=' + sessionBId, {
                headers: getAuthHeaders()
            });

            if (!response.ok) {
                let errData = null;
                try { errData = await response.json(); } catch (_) { }
                const msg = errData && errData.message ? errData.message : 'Failed to compare sessions.';
                const tr = document.createElement('tr');
                const td = document.createElement('td');
                td.colSpan = 6;
                td.className = 'text-center text-muted';
                td.style.color = 'var(--danger)';
                td.textContent = msg;
                tr.appendChild(td);
                elements.comparisonTableBody.replaceChildren(tr);
                return;
            }

            const data = await response.json();
            elements.compAName.textContent = data.sessionA ? data.sessionA.gameDisplayName : '--';
            elements.compADate.textContent = data.sessionA ? new Date(data.sessionA.capturedAtUtc).toLocaleString() : '--';
            elements.compBName.textContent = data.sessionB ? data.sessionB.gameDisplayName : '--';
            elements.compBDate.textContent = data.sessionB ? new Date(data.sessionB.capturedAtUtc).toLocaleString() : '--';

            elements.comparisonTableBody.innerHTML = '';
            if (data && Array.isArray(data.metrics)) {
                data.metrics.forEach(m => {
                    const tr = document.createElement('tr');

                    const tdKey = document.createElement('td');
                    tdKey.textContent = formatMetricKey(m.key);
                    tr.appendChild(tdKey);

                    const tdA = document.createElement('td');
                    tdA.textContent = typeof m.sessionA === 'number' ? m.sessionA.toFixed(2) : '--';
                    tr.appendChild(tdA);

                    const tdB = document.createElement('td');
                    tdB.textContent = typeof m.sessionB === 'number' ? m.sessionB.toFixed(2) : '--';
                    tr.appendChild(tdB);

                    const tdDelta = document.createElement('td');
                    tdDelta.textContent = typeof m.delta === 'number' ? (m.delta > 0 ? '+' : '') + m.delta.toFixed(2) : '--';
                    tr.appendChild(tdDelta);

                    const tdPct = document.createElement('td');
                    tdPct.textContent = typeof m.percentageDelta === 'number' ? (m.percentageDelta > 0 ? '+' : '') + m.percentageDelta.toFixed(1) + '%' : '--';
                    tr.appendChild(tdPct);

                    const tdDir = document.createElement('td');
                    let dirClass = 'dir-neutral';
                    let dirLabel = i18n ? i18n.t('comparison.neutral') : 'Neutral';
                    const betterText = i18n ? i18n.t('comparison.better') : '▲ Better';
                    const worseText = i18n ? i18n.t('comparison.worse') : '▼ Worse';

                    if (m.direction === 'HigherIsBetter') {
                        if (m.delta > 0) { dirClass = 'dir-higher'; dirLabel = betterText; }
                        else if (m.delta < 0) { dirClass = 'dir-lower'; dirLabel = worseText; }
                    } else if (m.direction === 'LowerIsBetter') {
                        if (m.delta < 0) { dirClass = 'dir-higher'; dirLabel = betterText; }
                        else if (m.delta > 0) { dirClass = 'dir-lower'; dirLabel = worseText; }
                    }
                    tdDir.className = dirClass;
                    tdDir.textContent = dirLabel;
                    tr.appendChild(tdDir);

                    elements.comparisonTableBody.appendChild(tr);
                });
            }
        } catch (_) {
            elements.comparisonTableBody.innerHTML = '<tr><td colspan="6" class="text-center text-muted" style="color: var(--danger);">Network error comparing sessions.</td></tr>';
        }
    }

    function formatMetricKey(key) {
        if (!key) return '--';
        return key.replace(/_/g, ' ').replace(/\b\w/g, l => l.toUpperCase());
    }

    // Fetch History Table
    async function fetchHistory() {
        try {
            const response = await fetch('/api/v1/benchmarks/history?limit=10', {
                headers: getAuthHeaders()
            });
            if (!response.ok) return;

            const data = await response.json();
            elements.historyTableBody.innerHTML = '';
            const i18n = window.FrameHubI18n;

            if (data && Array.isArray(data.sessions) && data.sessions.length > 0) {
                data.sessions.forEach(s => {
                    const tr = document.createElement('tr');

                    const tdCheck = document.createElement('td');
                    tdCheck.className = 'text-center';
                    const checkbox = document.createElement('input');
                    checkbox.type = 'checkbox';
                    checkbox.checked = selectedSessionIds.has(s.sessionId);
                    checkbox.addEventListener('change', function () {
                        if (checkbox.checked) {
                            if (selectedSessionIds.size >= 2) {
                                checkbox.checked = false;
                                return;
                            }
                            selectedSessionIds.add(s.sessionId);
                        } else {
                            selectedSessionIds.delete(s.sessionId);
                        }
                        updateCompareButtonState();
                    });
                    tdCheck.appendChild(checkbox);
                    tr.appendChild(tdCheck);

                    const tdGame = document.createElement('td');
                    tdGame.textContent = s.gameDisplayName || 'Unknown Game';
                    tr.appendChild(tdGame);

                    const tdStatus = document.createElement('td');
                    const badge = document.createElement('span');
                    badge.className = 'badge ' + (s.status === 'Completed' ? 'badge-success' : 'badge-secondary');
                    badge.textContent = i18n ? i18n.translateState(s.status) : (s.status || 'Done');
                    tdStatus.appendChild(badge);
                    tr.appendChild(tdStatus);

                    const tdDuration = document.createElement('td');
                    tdDuration.textContent = typeof s.durationSeconds === 'number' ? s.durationSeconds.toFixed(1) + 's' : '--';
                    tr.appendChild(tdDuration);

                    const tdFps = document.createElement('td');
                    tdFps.textContent = typeof s.averageFps === 'number' ? s.averageFps.toFixed(1) : '--';
                    tr.appendChild(tdFps);

                    const tdDate = document.createElement('td');
                    try {
                        tdDate.textContent = new Date(s.capturedAtUtc).toLocaleString();
                    } catch (_) {
                        tdDate.textContent = s.capturedAtUtc || '--';
                    }
                    tr.appendChild(tdDate);

                    const tdAction = document.createElement('td');
                    const btnView = document.createElement('button');
                    btnView.type = 'button';
                    btnView.className = 'btn btn-sm btn-secondary';
                    btnView.textContent = i18n ? i18n.t('history.loadBtn') : 'View';
                    btnView.addEventListener('click', async function () {
                        try {
                            const resp = await fetch('/api/v1/benchmarks/history/' + s.sessionId, { headers: getAuthHeaders() });
                            if (resp.ok) {
                                const detail = await resp.json();
                                renderResultDetail(detail);
                            }
                        } catch (_) { }
                    });
                    tdAction.appendChild(btnView);
                    tr.appendChild(tdAction);

                    elements.historyTableBody.appendChild(tr);
                });
            } else {
                const noSessMsg = i18n ? i18n.t('history.noSessions') : 'No benchmark sessions recorded yet.';
                elements.historyTableBody.innerHTML = '<tr><td colspan="7" class="text-center text-muted">' + noSessMsg + '</td></tr>';
            }
        } catch (_) { }
    }

    // Library Logic
    let isLibraryFetching = false;
    let cachedLibraryItems = null;

    async function fetchLibraryItems() {
        if (isLibraryFetching) return;
        isLibraryFetching = true;

        if (elements.libraryLoading) elements.libraryLoading.classList.remove('hidden');
        if (elements.libraryError) elements.libraryError.classList.add('hidden');
        if (elements.libraryEmpty) elements.libraryEmpty.classList.add('hidden');

        const i18n = window.FrameHubI18n;

        try {
            const resp = await fetch('/api/v1/library', { headers: getAuthHeaders() });
            if (resp.status === 401) {
                showLibraryError(i18n ? i18n.t('launch.unauthorized') : 'Authentication required.');
                return;
            }
            if (resp.status === 403) {
                showLibraryError(i18n ? i18n.t('launch.forbidden') : 'Permission required.');
                return;
            }
            if (!resp.ok) {
                showLibraryError(i18n ? i18n.t('library.loadFailed') : 'Failed to load library items.');
                return;
            }

            const items = await resp.json();
            cachedLibraryItems = items;
            renderLibraryItems(items);
        } catch (err) {
            showLibraryError(i18n ? i18n.t('library.loadFailed') : 'Failed to load library items.');
        } finally {
            isLibraryFetching = false;
            if (elements.libraryLoading) elements.libraryLoading.classList.add('hidden');
        }
    }

    function showLibraryError(msg) {
        if (elements.libraryList) elements.libraryList.innerHTML = '';
        if (elements.libraryError) {
            elements.libraryError.textContent = msg;
            elements.libraryError.classList.remove('hidden');
        }
    }

    function renderLibraryItems(items) {
        if (!elements.libraryList) return;
        elements.libraryList.innerHTML = '';
        if (elements.libraryError) elements.libraryError.classList.add('hidden');

        const i18n = window.FrameHubI18n;

        if (!items || !Array.isArray(items) || items.length === 0) {
            if (elements.libraryEmpty) elements.libraryEmpty.classList.remove('hidden');
            return;
        }

        if (elements.libraryEmpty) elements.libraryEmpty.classList.add('hidden');

        items.forEach(function (item) {
            const card = document.createElement('div');
            card.className = 'library-card' + (item.isRunning ? ' is-running' : '');

            const info = document.createElement('div');
            info.className = 'library-card-info';

            const title = document.createElement('h3');
            title.className = 'library-card-title';
            title.textContent = item.displayName || 'Unknown';
            info.appendChild(title);

            const badges = document.createElement('div');
            badges.className = 'library-card-badges';

            if (item.source) {
                const badgeSource = document.createElement('span');
                badgeSource.className = 'badge badge-source';
                badgeSource.textContent = item.source;
                badges.appendChild(badgeSource);
            }

            if (item.type) {
                const badgeType = document.createElement('span');
                badgeType.className = 'badge badge-type';
                badgeType.textContent = item.type;
                badges.appendChild(badgeType);
            }

            if (item.isRunning) {
                const badgeRunning = document.createElement('span');
                badgeRunning.className = 'badge badge-running';
                badgeRunning.textContent = i18n ? i18n.t('library.running') : 'Running';
                badges.appendChild(badgeRunning);
            }

            info.appendChild(badges);
            card.appendChild(info);

            const actions = document.createElement('div');
            actions.className = 'library-card-actions';

            const feedback = document.createElement('div');
            feedback.className = 'launch-feedback hidden';

            const btnLaunch = document.createElement('button');
            btnLaunch.type = 'button';
            btnLaunch.className = 'btn btn-primary btn-launch';
            btnLaunch.textContent = item.isRunning
                ? (i18n ? i18n.t('library.running') : 'Running')
                : (i18n ? i18n.t('library.launch') : 'Launch');

            if (item.isRunning) {
                btnLaunch.disabled = true;
            } else {
                btnLaunch.addEventListener('click', function () {
                    handleLaunchItem(item, card, btnLaunch, feedback);
                });
            }

            actions.appendChild(btnLaunch);
            card.appendChild(actions);
            card.appendChild(feedback);

            elements.libraryList.appendChild(card);
        });
    }

    async function handleLaunchItem(item, cardEl, btnLaunch, feedbackEl) {
        if (!item || !item.id) return;
        btnLaunch.disabled = true;
        const i18n = window.FrameHubI18n;
        const origText = btnLaunch.textContent;
        btnLaunch.textContent = i18n ? i18n.t('library.launching') : 'Launching...';
        feedbackEl.className = 'launch-feedback hidden';
        feedbackEl.textContent = '';

        try {
            const resp = await fetch('/api/v1/library/' + encodeURIComponent(item.id) + '/launch', {
                method: 'POST',
                headers: getAuthHeaders()
            });

            let data = null;
            try {
                data = await resp.json();
            } catch (_) { }

            const errorCode = data && data.errorCode ? data.errorCode : (!resp.ok ? 'launch_failed' : 'launched');
            const isSuccess = data && typeof data.success === 'boolean' ? data.success : resp.ok;

            const i18nKey = 'launch.' + errorCode;
            const msg = i18n ? i18n.t(i18nKey, errorCode) : errorCode;

            feedbackEl.textContent = msg;
            feedbackEl.className = 'launch-feedback ' + (isSuccess ? 'success' : 'error');

            if (isSuccess) {
                // Refresh library list shortly to update running state
                setTimeout(function () {
                    fetchLibraryItems();
                }, 1500);
            }
        } catch (err) {
            feedbackEl.textContent = i18n ? i18n.t('launch.launch_failed') : 'Launch failed.';
            feedbackEl.className = 'launch-feedback error';
        } finally {
            btnLaunch.disabled = false;
            btnLaunch.textContent = origText;
        }
    }

    // Trusted Background App Control (M10.1)
    let backgroundAppsFetchPromise = null;
    const backgroundAppOperations = new Set();

    function fetchBackgroundApps() {
        if (backgroundAppsFetchPromise) return backgroundAppsFetchPromise;
        backgroundAppsFetchPromise = fetchBackgroundAppsCore();
        return backgroundAppsFetchPromise;
    }

    async function fetchBackgroundAppsCore() {
        if (elements.backgroundAppsLoading) elements.backgroundAppsLoading.classList.remove('hidden');
        if (elements.backgroundAppsError) elements.backgroundAppsError.classList.add('hidden');
        if (elements.backgroundAppsEmpty) elements.backgroundAppsEmpty.classList.add('hidden');

        const i18n = window.FrameHubI18n;
        try {
            const resp = await fetch('/api/v1/background-apps', { headers: getAuthHeaders() });
            if (resp.status === 401) {
                updateAuthUi(false, i18n ? i18n.t('auth.required') : 'Pairing Required');
                showBackgroundAppsError(i18n ? i18n.t('backgroundApps.unauthorized') : 'Authentication required.');
                return;
            }
            if (resp.status === 403) {
                showBackgroundAppsError(i18n ? i18n.t('backgroundApps.permissionUnavailable') : 'Background app permission is unavailable.');
                return;
            }
            if (!resp.ok) {
                showBackgroundAppsError(i18n ? i18n.t('backgroundApps.loadFailed') : 'Failed to load background apps.');
                return;
            }
            renderBackgroundApps(await resp.json());
        } catch (_) {
            showBackgroundAppsError(i18n ? i18n.t('backgroundApps.loadFailed') : 'Failed to load background apps.');
        } finally {
            backgroundAppsFetchPromise = null;
            if (elements.backgroundAppsLoading) elements.backgroundAppsLoading.classList.add('hidden');
        }
    }

    function showBackgroundAppsError(message) {
        if (elements.backgroundAppsList) elements.backgroundAppsList.textContent = '';
        if (elements.backgroundAppsError) {
            elements.backgroundAppsError.textContent = message;
            elements.backgroundAppsError.classList.remove('hidden');
        }
    }

    function renderBackgroundApps(items) {
        if (!elements.backgroundAppsList) return;
        elements.backgroundAppsList.textContent = '';
        if (elements.backgroundAppsError) elements.backgroundAppsError.classList.add('hidden');
        if (!Array.isArray(items) || items.length === 0) {
            if (elements.backgroundAppsEmpty) elements.backgroundAppsEmpty.classList.remove('hidden');
            return;
        }
        if (elements.backgroundAppsEmpty) elements.backgroundAppsEmpty.classList.add('hidden');

        const i18n = window.FrameHubI18n;
        items.forEach(function (item) {
            const card = document.createElement('div');
            card.className = 'library-card background-app-card' + (item.isRunning ? ' is-running' : '');

            const info = document.createElement('div');
            info.className = 'library-card-info';
            const title = document.createElement('h3');
            title.className = 'library-card-title';
            title.textContent = item.displayName || '';
            const state = document.createElement('span');
            state.className = 'badge ' + (item.isRunning ? 'badge-running' : 'badge-secondary');
            state.textContent = item.isRunning
                ? (i18n ? i18n.t('backgroundApps.running') : 'Running')
                : (i18n ? i18n.t('backgroundApps.stopped') : 'Stopped');
            info.appendChild(title);
            info.appendChild(state);
            card.appendChild(info);

            const feedback = document.createElement('div');
            feedback.className = 'launch-feedback hidden';
            const actions = document.createElement('div');
            actions.className = 'library-card-actions';
            const button = document.createElement('button');
            button.type = 'button';
            button.className = item.isRunning ? 'btn btn-secondary' : 'btn btn-primary';
            const action = item.isRunning ? 'stop' : 'start';
            button.textContent = i18n ? i18n.t('backgroundApps.' + action) : (item.isRunning ? 'Stop' : 'Start');
            button.disabled = item.isRunning ? !item.canStop : !item.canStart;
            button.addEventListener('click', function () {
                controlBackgroundApp(item, action, button, feedback);
            });
            actions.appendChild(button);
            card.appendChild(actions);
            card.appendChild(feedback);
            elements.backgroundAppsList.appendChild(card);
        });
    }

    async function controlBackgroundApp(item, action, button, feedback) {
        if (!item || !item.id || backgroundAppOperations.has(item.id)) return;
        backgroundAppOperations.add(item.id);
        const i18n = window.FrameHubI18n;
        button.disabled = true;
        const originalText = button.textContent;
        button.textContent = i18n ? i18n.t('backgroundApps.busy') : 'Busy...';
        feedback.textContent = '';
        feedback.className = 'launch-feedback hidden';
        let operationSucceeded = false;

        try {
            const resp = await fetch('/api/v1/background-apps/' + encodeURIComponent(item.id) + '/' + action, {
                method: 'POST',
                headers: getAuthHeaders()
            });
            if (resp.status === 401) {
                updateAuthUi(false, i18n ? i18n.t('auth.required') : 'Pairing Required');
                feedback.textContent = i18n ? i18n.t('backgroundApps.unauthorized') : 'Authentication required.';
                feedback.className = 'launch-feedback error';
                return;
            }
            if (resp.status === 403) {
                feedback.textContent = i18n ? i18n.t('backgroundApps.permissionUnavailable') : 'Background app permission is unavailable.';
                feedback.className = 'launch-feedback error';
                return;
            }
            let data = null;
            try { data = await resp.json(); } catch (_) { }
            const code = data && data.errorCode ? data.errorCode : (resp.ok ? (action === 'start' ? 'started' : 'stop_succeeded') : (action === 'start' ? 'launch_failed' : 'stop_failed'));
            const success = data && typeof data.success === 'boolean' ? data.success : resp.ok;
            feedback.textContent = i18n ? i18n.t('backgroundApps.' + code, code) : code;
            feedback.className = 'launch-feedback ' + (success ? 'success' : 'error');
            if (success) {
                operationSucceeded = true;
                await new Promise(function (resolve) {
                    setTimeout(resolve, action === 'start' ? 1500 : 500);
                });
                await fetchBackgroundApps();
            }
        } catch (_) {
            feedback.textContent = i18n ? i18n.t('backgroundApps.operationFailed') : 'Operation failed.';
            feedback.className = 'launch-feedback error';
        } finally {
            backgroundAppOperations.delete(item.id);
            if (!operationSucceeded) {
                button.disabled = false;
                button.textContent = originalText;
            }
        }
    }

    // Session Optimization Logic (M9.5)
    let isOptFetching = false;
    let cachedOptState = null;
    let optPollIntervalId = null;

    async function fetchOptimizationState() {
        if (isOptFetching) return;
        isOptFetching = true;

        try {
            const resp = await fetch('/api/v1/session-optimization', { headers: getAuthHeaders() });
            if (resp.status === 401 || resp.status === 403) {
                return;
            }
            if (!resp.ok) return;

            const state = await resp.json();
            cachedOptState = state;
            renderOptimizationState(state);
        } catch (_) {
        } finally {
            isOptFetching = false;
        }
    }

    function renderOptimizationState(state) {
        if (!state) return;
        const i18n = window.FrameHubI18n;

        // State badge
        if (elements.optStateBadge) {
            elements.optStateBadge.textContent = state.isSessionActive
                ? (i18n ? i18n.t('optimization.active') : 'Active')
                : (i18n ? i18n.t('optimization.inactive') : 'Inactive');
            elements.optStateBadge.className = 'badge ' + (state.isSessionActive ? 'badge-success' : 'badge-secondary');
        }

        // Game name
        if (elements.optGameName) {
            elements.optGameName.textContent = state.gameDisplayName || '--';
        }

        // Suspended count
        if (elements.optSuspendedCount) {
            elements.optSuspendedCount.textContent = typeof state.suspendedProcessCount === 'number' ? state.suspendedProcessCount : '0';
        }

        // Taskbar & Recovery badges
        if (elements.optTaskbarBadge) {
            elements.optTaskbarBadge.classList.toggle('hidden', !state.taskbarHidden);
        }
        if (elements.optRecoveryBadge) {
            elements.optRecoveryBadge.classList.toggle('hidden', !state.isRecoveryPending);
        }

        // Action buttons
        if (elements.btnApplyOpt && elements.btnRestoreOpt) {
            if (state.isSessionActive) {
                elements.btnApplyOpt.classList.add('hidden');
                elements.btnRestoreOpt.classList.remove('hidden');
            } else {
                elements.btnApplyOpt.classList.remove('hidden');
                elements.btnRestoreOpt.classList.add('hidden');
            }
        }
    }

    async function handleApplyOptimization() {
        if (!elements.btnApplyOpt) return;
        elements.btnApplyOpt.disabled = true;
        const i18n = window.FrameHubI18n;
        const origText = elements.btnApplyOpt.textContent;
        elements.btnApplyOpt.textContent = i18n ? i18n.t('optimization.applying') : 'Starting...';
        showOptFeedback('', false);

        try {
            const resp = await fetch('/api/v1/session-optimization/apply', {
                method: 'POST',
                headers: getAuthHeaders()
            });

            let data = null;
            try { data = await resp.json(); } catch (_) { }

            const errorCode = data && data.errorCode ? data.errorCode : (!resp.ok ? 'apply_failed' : 'applied');
            const isSuccess = data && typeof data.success === 'boolean' ? data.success : resp.ok;

            const i18nKey = 'optimization.' + errorCode;
            const msg = i18n ? i18n.t(i18nKey, errorCode) : errorCode;

            showOptFeedback(msg, isSuccess);

            setTimeout(fetchOptimizationState, 1000);
        } catch (err) {
            showOptFeedback(i18n ? i18n.t('optimization.apply_failed') : 'Failed to start optimization.', false);
        } finally {
            elements.btnApplyOpt.disabled = false;
            elements.btnApplyOpt.textContent = origText;
        }
    }

    async function handleRestoreOptimization() {
        if (!elements.btnRestoreOpt) return;
        elements.btnRestoreOpt.disabled = true;
        const i18n = window.FrameHubI18n;
        const origText = elements.btnRestoreOpt.textContent;
        elements.btnRestoreOpt.textContent = i18n ? i18n.t('optimization.restoring') : 'Restoring...';
        showOptFeedback('', false);

        try {
            const resp = await fetch('/api/v1/session-optimization/restore', {
                method: 'POST',
                headers: getAuthHeaders()
            });

            let data = null;
            try { data = await resp.json(); } catch (_) { }

            const errorCode = data && data.errorCode ? data.errorCode : (!resp.ok ? 'restore_failed' : 'restored');
            const isSuccess = data && typeof data.success === 'boolean' ? data.success : resp.ok;

            const i18nKey = 'optimization.' + errorCode;
            const msg = i18n ? i18n.t(i18nKey, errorCode) : errorCode;

            showOptFeedback(msg, isSuccess);

            setTimeout(fetchOptimizationState, 1000);
        } catch (err) {
            showOptFeedback(i18n ? i18n.t('optimization.restore_failed') : 'Failed to restore session.', false);
        } finally {
            elements.btnRestoreOpt.disabled = false;
            elements.btnRestoreOpt.textContent = origText;
        }
    }

    function showOptFeedback(msg, isSuccess) {
        if (!elements.optFeedback) return;
        if (!msg) {
            elements.optFeedback.classList.add('hidden');
            elements.optFeedback.textContent = '';
            return;
        }
        elements.optFeedback.textContent = msg;
        elements.optFeedback.className = 'optimization-feedback ' + (isSuccess ? 'success' : 'error');
        elements.optFeedback.classList.remove('hidden');
    }

    // Initialization
    function init() {
        checkUrlPairingToken();

        // Setup i18n
        if (window.FrameHubI18n) {
            window.FrameHubI18n.applyTranslations(document);
            if (elements.languageSelect) {
                elements.languageSelect.value = window.FrameHubI18n.getCurrentLanguage();
                elements.languageSelect.addEventListener('change', function () {
                    window.FrameHubI18n.setLanguage(elements.languageSelect.value, true);
                    if (currentStatus) renderStatus(currentStatus);
                    if (lastTelemetrySnapshot) renderTelemetry(lastTelemetrySnapshot);
                    if (cachedLibraryItems) renderLibraryItems(cachedLibraryItems);
                    if (cachedOptState) renderOptimizationState(cachedOptState);
                    fetchHistory();
                });
            }
        }

        // Navigation listeners
        [elements.navTabHome, elements.navTabLibrary, elements.navTabBenchmarks, elements.navTabSettings].forEach(function (btn) {
            if (btn) {
                btn.addEventListener('click', function () {
                    const tab = btn.getAttribute('data-tab');
                    switchTab(tab);
                });
            }
        });

        elements.pairingForm.addEventListener('submit', handlePairingSubmit);
        if (elements.btnRefreshLibrary) elements.btnRefreshLibrary.addEventListener('click', function () {
            fetchLibraryItems();
            fetchBackgroundApps();
        });
        if (elements.btnRefreshBackgroundApps) elements.btnRefreshBackgroundApps.addEventListener('click', fetchBackgroundApps);
        if (elements.btnApplyOpt) elements.btnApplyOpt.addEventListener('click', handleApplyOptimization);
        if (elements.btnRestoreOpt) elements.btnRestoreOpt.addEventListener('click', handleRestoreOptimization);
        if (elements.btnRefreshOpt) elements.btnRefreshOpt.addEventListener('click', fetchOptimizationState);
        elements.btnRefreshTargets.addEventListener('click', loadTargets);
        elements.btnStart.addEventListener('click', handleStart);
        elements.btnStop.addEventListener('click', handleStop);
        elements.btnRefreshHistory.addEventListener('click', fetchHistory);
        elements.btnCompareSessions.addEventListener('click', handleCompareClick);
        elements.btnCloseComparison.addEventListener('click', handleCloseComparison);

        loadTargets();
        fetchHistory();
        fetchStatus();
        fetchOptimizationState();
        initTelemetryConnection();

        // Polling interval (1000ms for status, 4000ms for optimization)
        pollIntervalId = setInterval(fetchStatus, 1000);
        optPollIntervalId = setInterval(fetchOptimizationState, 4000);
    }

    // Clean up on unload
    window.addEventListener('beforeunload', function () {
        isUnloading = true;
        teardownTelemetryConnection(false);
        if (pollIntervalId) {
            clearInterval(pollIntervalId);
            pollIntervalId = null;
        }
        if (optPollIntervalId) {
            clearInterval(optPollIntervalId);
            optPollIntervalId = null;
        }
    });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
