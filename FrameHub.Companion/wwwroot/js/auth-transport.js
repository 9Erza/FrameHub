'use strict';

    const STORAGE_KEY = 'companion_credential';

    function getStoredCredential() {
        try {
            const persistent = localStorage.getItem(STORAGE_KEY);
            if (persistent) return persistent;

            // Backward compatibility: one-time in-browser migration from legacy sessionStorage
            const legacy = sessionStorage.getItem(STORAGE_KEY);
            if (legacy) {
                localStorage.setItem(STORAGE_KEY, legacy);
                try { sessionStorage.removeItem(STORAGE_KEY); } catch (_) { }
                return legacy;
            }
        } catch (_) {
            // Strict privacy or sandboxed fallback
            try { return sessionStorage.getItem(STORAGE_KEY); } catch (_) { }
        }
        return null;
    }

    function setStoredCredential(credential) {
        if (!credential) {
            clearStoredCredential();
            return;
        }
        try {
            localStorage.setItem(STORAGE_KEY, credential);
        } catch (_) { }
        try {
            sessionStorage.removeItem(STORAGE_KEY);
        } catch (_) { }
    }

    function clearStoredCredential() {
        try { localStorage.removeItem(STORAGE_KEY); } catch (_) { }
        try { sessionStorage.removeItem(STORAGE_KEY); } catch (_) { }
    }

    function getAuthHeaders() {
        const headers = { 'Content-Type': 'application/json' };
        const credential = getStoredCredential();
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
                    setStoredCredential(data.credential);
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

    let wsInstance = null;
    let telemetryPollInterval = null;
    let telemetryStaleTimeout = null;
    let telemetryReconnectTimeout = null;
    let telemetryConnectingGeneration = null;
    let telemetryConnectionGeneration = 0;
    let telemetryHttpRequestId = 0;
    let isUnloading = false;
    let telemetryTicketRetryAfter = 0;

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
                clearStoredCredential();
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
        const expectedCredential = getStoredCredential();
        const requestId = ++telemetryHttpRequestId;
        if (isUnloading || expectedPairedState === false) return;

        const ownsRequest = function () {
            return requestId === telemetryHttpRequestId
                && generation === telemetryConnectionGeneration
                && !isUnloading
                && lastAuthUiPaired === expectedPairedState
                && lastAuthUiPaired !== false
                && getStoredCredential() === expectedCredential
                && !(wsInstance && wsInstance.readyState === WebSocket.OPEN);
        };

        try {
            const resp = await fetch('/api/v1/telemetry', { headers: getAuthHeaders() });
            if (!ownsRequest()) return;

            if (resp.status === 401) {
                clearStoredCredential();
                updateAuthUi(false, 'Pairing Required');
                return;
            }

            if (resp.ok) {
                const data = await resp.json();
                if (!ownsRequest()) return;
                renderTelemetry(data);
                if (getStoredCredential() && !wsInstance) {
                    initTelemetryConnection();
                }
            }
        } catch (_) { }
    }

    // Start Benchmark
