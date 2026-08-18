    'use strict';
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
        hwCpuElevationHint: document.getElementById('hw-cpu-elevation-hint'),
        hwGpuLoad: document.getElementById('hw-gpu-load'),
        hwGpuTemp: document.getElementById('hw-gpu-temp'),
        hwRamUsage: document.getElementById('hw-ram-usage'),
        hwVramUsage: document.getElementById('hw-vram-usage'),
        hwMonitorToggleBtn: document.getElementById('hw-monitor-toggle-btn'),
        hwToggleDot: document.getElementById('hw-toggle-dot'),
        hwToggleText: document.getElementById('hw-toggle-text'),
        hwDisabledNotice: document.getElementById('hw-disabled-notice'),
        hwEnableBtn: document.getElementById('hw-enable-btn'),
        hwPermissionNotice: document.getElementById('hw-permission-notice'),
        hwGridContainer: document.getElementById('hw-grid-container'),

        optSection: document.getElementById('session-optimization-section'),
        optStateBadge: document.getElementById('opt-state-badge'),
        optGameName: document.getElementById('opt-game-name'),
        optSuspendedCount: document.getElementById('opt-suspended-count'),
        optTaskbarBadge: document.getElementById('opt-taskbar-badge'),
        optRecoveryBadge: document.getElementById('opt-recovery-badge'),
        btnApplyOpt: document.getElementById('btn-apply-optimization'),
        btnRestoreOpt: document.getElementById('btn-restore-optimization'),
        btnRefreshOpt: document.getElementById('btn-refresh-optimization'),
        optFeedback: document.getElementById('opt-feedback'),

        gameCpuSection: document.getElementById('game-cpu-section'),
        optCpuGameName: document.getElementById('opt-cpu-game-name'),
        optCpuSource: document.getElementById('opt-cpu-source'),
        optCpuModeLabel: document.getElementById('opt-cpu-mode-label'),
        optCpuSummary: document.getElementById('opt-cpu-summary'),
        optCpuOverrideBadge: document.getElementById('opt-cpu-override-badge'),
        optCpuUnavailable: document.getElementById('opt-cpu-unavailable'),
        btnEditCpu: document.getElementById('btn-edit-cpu'),
        optCpuEditor: document.getElementById('opt-cpu-editor'),
        optCpuChips: document.getElementById('opt-cpu-chips'),
        btnCpuModeAffinity: document.getElementById('btn-cpu-mode-affinity'),
        btnCpuModeCpusets: document.getElementById('btn-cpu-mode-cpusets'),
        btnCpuPresetAll: document.getElementById('btn-cpu-preset-all'),
        btnCpuPresetPhysical: document.getElementById('btn-cpu-preset-physical'),
        btnCpuPresetClear: document.getElementById('btn-cpu-preset-clear'),
        btnApplyCpu: document.getElementById('btn-apply-cpu'),
        btnCancelCpu: document.getElementById('btn-cancel-cpu'),
        btnRestoreCpu: document.getElementById('btn-restore-cpu'),
        optCpuFeedback: document.getElementById('opt-cpu-feedback')
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
        } else if (activeTab === 'benchmarks') {
            // One-shot per tab activation: picks up scopes granted in Desktop Settings
            // without re-pairing. No polling; repeated only on each new activation.
            loadTargets();
        } else if (activeTab === 'home') {
            fetchOptimizationState();
        }
    }

    // Helper: Auth Header

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
        if (elements.btnRefreshOpt) elements.btnRefreshOpt.addEventListener('click', function () {
            fetchOptimizationState();
            fetchSessionCpuState();
        });
        if (elements.btnEditCpu) elements.btnEditCpu.addEventListener('click', handleEditCpu);
        if (elements.btnApplyCpu) elements.btnApplyCpu.addEventListener('click', handleApplyCpuOverride);
        if (elements.btnCancelCpu) elements.btnCancelCpu.addEventListener('click', function () { setCpuEditorOpen(false); });
        if (elements.btnRestoreCpu) elements.btnRestoreCpu.addEventListener('click', handleResetCpuOverride);
        if (elements.btnCpuModeAffinity) elements.btnCpuModeAffinity.addEventListener('click', function () { setCpuEditMode('affinity'); });
        if (elements.btnCpuModeCpusets) elements.btnCpuModeCpusets.addEventListener('click', function () { setCpuEditMode('cpu-sets'); });
        if (elements.btnCpuPresetAll) elements.btnCpuPresetAll.addEventListener('click', handlePresetAll);
        if (elements.btnCpuPresetPhysical) elements.btnCpuPresetPhysical.addEventListener('click', handlePresetPhysical);
        if (elements.btnCpuPresetClear) elements.btnCpuPresetClear.addEventListener('click', handlePresetClear);
        elements.btnRefreshTargets.addEventListener('click', loadTargets);
        elements.btnStart.addEventListener('click', handleStart);
        elements.btnStop.addEventListener('click', handleStop);
        if (elements.hwMonitorToggleBtn) {
            elements.hwMonitorToggleBtn.addEventListener('click', function () {
                const target = !currentHardwareMonitorState || !currentHardwareMonitorState.enabled;
                handleToggleHardwareMonitor(target);
            });
        }
        if (elements.hwEnableBtn) {
            elements.hwEnableBtn.addEventListener('click', function () {
                handleToggleHardwareMonitor(true);
            });
        }
        elements.btnRefreshHistory.addEventListener('click', fetchHistory);
        elements.btnCompareSessions.addEventListener('click', handleCompareClick);
        elements.btnCloseComparison.addEventListener('click', handleCloseComparison);

        loadTargets();
        fetchHistory();
        fetchStatus();
        fetchOptimizationState();
        fetchSessionCpuState();
        initTelemetryConnection();

        // Polling interval (1000ms for status, 4000ms for optimization + session CPU)
        pollIntervalId = setInterval(fetchStatus, 1000);
        optPollIntervalId = setInterval(function () {
            fetchOptimizationState();
            fetchSessionCpuState();
        }, 4000);
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
