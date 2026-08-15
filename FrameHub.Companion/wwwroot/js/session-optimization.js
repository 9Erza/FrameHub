'use strict';

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
