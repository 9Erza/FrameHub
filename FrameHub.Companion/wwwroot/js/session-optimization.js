'use strict';

    let isOptFetching = false;
    let cachedOptState = null;
    let optPollIntervalId = null;

    async function fetchOptimizationState() {
        if (isOptFetching) return;
        isOptFetching = true;

        try {
            const resp = await fetch('/api/v1/session-optimization', { headers: getAuthHeaders() });
            if (resp.status === 401) {
                clearStoredCredential();
                updateAuthUi(false, 'Pairing Required');
                return;
            }
            if (resp.status === 403) {
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

    function showCpuFeedback(msg, isSuccess) {
        const fb = elements.optCpuFeedback || elements.optFeedback;
        if (!fb) return;
        if (!msg) {
            fb.classList.add('hidden');
            fb.textContent = '';
            return;
        }
        fb.textContent = msg;
        fb.className = 'optimization-feedback ' + (isSuccess ? 'success' : 'error');
        fb.classList.remove('hidden');
    }

    // ------------------------------------------------------------------
    // Game CPU Control (temporary active-game override)
    // ------------------------------------------------------------------

    let cpuState = null;
    let cpuPermissionDenied = false;
    let cpuEditorOpen = false;
    let cpuEditMode = 'cpu-sets';
    let selectedCpuIndices = new Set();
    let cpuEditorSessionToken = null;

    function cpuT(key, fallback) {
        const i18n = window.FrameHubI18n;
        return i18n ? i18n.t(key, fallback) : (fallback || key);
    }

    async function fetchSessionCpuState() {
        try {
            const resp = await fetch('/api/v1/session-optimization/cpu', { headers: getAuthHeaders() });
            if (resp.status === 401) {
                clearStoredCredential();
                updateAuthUi(false, 'Pairing Required');
                return;
            }
            if (resp.status === 403) {
                // Still paired, but this device lacks the Game CPU Control read scope.
                cpuPermissionDenied = true;
                renderCpuState(cpuState);
                return;
            }
            if (!resp.ok) return;

            cpuPermissionDenied = false;
            cpuState = await resp.json();
            renderCpuState(cpuState);
        } catch (_) {
        }
    }

    function cpuSourceText(state) {
        if (!state) return '--';
        if (state.temporaryOverrideActive) return cpuT('optimization.cpu.sourceOverride', 'Temporary override');
        if (state.source === 'profile') {
            return state.profileName
                ? cpuT('optimization.cpu.sourceProfile', 'Game profile') + ' · ' + state.profileName
                : cpuT('optimization.cpu.sourceProfile', 'Game profile');
        }
        return cpuT('optimization.cpu.sourceSystem', 'System');
    }

    function cpuSelectionSummary(selection, totalCount) {
        if (!selection || !selection.indices || selection.indices.length === 0) {
            return cpuT('optimization.cpu.none', 'None');
        }
        if (selection.indices.length <= 8) return selection.indices.join(', ');
        return selection.indices.length + ' / ' + (totalCount || '?');
    }

    function getPhysicalProcessorIndices(processors) {
        if (!processors || !processors.length) return [];
        const nonThreads = processors.filter(function (p) { return !p.isThread; });
        if (nonThreads.length > 0) {
            return nonThreads.map(function (p) { return p.index; });
        }
        // Fallback: one logical processor per unique coreIndex
        const seen = new Set();
        const result = [];
        processors.forEach(function (p) {
            if (!seen.has(p.coreIndex)) {
                seen.add(p.coreIndex);
                result.push(p.index);
            }
        });
        return result;
    }

    function renderCpuState(state) {
        if (!elements.optCpuSource) return;

        const unavailableNote = elements.optCpuUnavailable;
        let noteText = '';
        let available = false;

        if (cpuPermissionDenied) {
            noteText = cpuT('optimization.cpu.permission', 'This device does not have permission for Game CPU Control.');
        } else if (!state || !state.available) {
            const reason = state ? state.unavailableReason : '';
            if (reason === 'no_game') noteText = cpuT('optimization.cpu.noGame', 'No active game. Launch a game from the library or manually to view and temporarily change its CPU settings.');
            else if (reason === 'protected_game') noteText = cpuT('optimization.cpu.protected', 'CPU control is unavailable for this game due to security policies.');
            else noteText = cpuT('optimization.cpu.unavailable', 'CPU control unavailable.');
        } else {
            available = true;
        }

        if (unavailableNote) {
            unavailableNote.textContent = noteText;
            unavailableNote.classList.toggle('hidden', !noteText);
        }

        if (elements.optCpuGameName) {
            elements.optCpuGameName.textContent = state && state.gameDisplayName ? state.gameDisplayName : '--';
        }

        if (elements.optCpuSource) {
            elements.optCpuSource.textContent = state ? cpuSourceText(state) : '--';
        }

        const selection = state && state.currentSelection;
        const total = state && state.topology && state.topology.processors ? state.topology.processors.length : 0;
        if (elements.optCpuModeLabel) {
            elements.optCpuModeLabel.textContent = selection && selection.mode === 'cpu-sets'
                ? cpuT('optimization.cpu.cpuSets', 'CPU Sets')
                : cpuT('optimization.cpu.affinity', 'Affinity');
        }
        if (elements.optCpuSummary) {
            elements.optCpuSummary.textContent = selection ? cpuSelectionSummary(selection, total) : '--';
        }

        if (elements.optCpuOverrideBadge) {
            elements.optCpuOverrideBadge.classList.toggle('hidden', !(state && state.temporaryOverrideActive));
        }

        if (elements.btnEditCpu) {
            elements.btnEditCpu.classList.toggle('hidden', !available);
        }

        // Close a stale editor when the session changed or control became unavailable.
        if (cpuEditorOpen && (!available || !state || state.sessionToken !== cpuEditorSessionToken)) {
            setCpuEditorOpen(false);
        }

        if (elements.btnRestoreCpu) {
            const meaningful = cpuEditorOpen && state && state.temporaryOverrideActive;
            elements.btnRestoreCpu.classList.toggle('hidden', !meaningful);
            if (state && state.profileName) {
                elements.btnRestoreCpu.textContent = cpuT('optimization.cpu.restore', 'Restore profile settings');
            } else {
                elements.btnRestoreCpu.textContent = cpuT('optimization.cpu.restoreOriginal', 'Restore initial settings');
            }
        }
    }

    function setCpuEditorOpen(open) {
        cpuEditorOpen = open;
        if (elements.optCpuEditor) elements.optCpuEditor.classList.toggle('hidden', !open);
        if (open && cpuState) {
            cpuEditorSessionToken = cpuState.sessionToken;
            // Default edit mode is CPU Sets (recommended)
            setCpuEditMode('cpu-sets');
            const selection = cpuState.currentSelection;
            const initialIndices = selection && selection.indices && selection.indices.length > 0
                ? selection.indices
                : (cpuState.topology && cpuState.topology.processors ? cpuState.topology.processors.map(function (p) { return p.index; }) : []);
            populateCpuChips(initialIndices);
        } else {
            cpuEditorSessionToken = null;
        }
        if (elements.btnRestoreCpu) {
            const meaningful = open && cpuState && cpuState.temporaryOverrideActive;
            elements.btnRestoreCpu.classList.toggle('hidden', !meaningful);
        }
    }

    function setCpuEditMode(mode) {
        cpuEditMode = mode;
        if (elements.btnCpuModeAffinity) elements.btnCpuModeAffinity.classList.toggle('active', mode === 'affinity');
        if (elements.btnCpuModeCpusets) elements.btnCpuModeCpusets.classList.toggle('active', mode === 'cpu-sets');
    }

    function populateCpuChips(selectedIndices) {
        if (!elements.optCpuChips || !cpuState || !cpuState.topology || !cpuState.topology.processors) return;
        selectedCpuIndices = new Set(selectedIndices);
        const chips = elements.optCpuChips;
        while (chips.firstChild) chips.removeChild(chips.firstChild);

        cpuState.topology.processors.forEach(function (processor) {
            const chip = document.createElement('button');
            chip.type = 'button';
            chip.className = 'cpu-chip' + (processor.isECore ? ' cpu-chip-ecore' : processor.isThread ? ' cpu-chip-thread' : '');
            chip.textContent = String(processor.index) + (processor.isECore ? 'E' : processor.isThread ? 'T' : '');
            chip.title = 'CPU ' + processor.index + (processor.isECore ? ' (E-core)' : processor.isThread ? ' (thread)' : '');
            if (selectedCpuIndices.has(processor.index)) {
                chip.classList.add('active');
            }
            chip.addEventListener('click', function () {
                if (selectedCpuIndices.has(processor.index)) {
                    selectedCpuIndices.delete(processor.index);
                    chip.classList.remove('active');
                } else {
                    selectedCpuIndices.add(processor.index);
                    chip.classList.add('active');
                }
                updateApplyButtonState();
            });
            chips.appendChild(chip);
        });

        updateApplyButtonState();
    }

    function updateApplyButtonState() {
        if (elements.btnApplyCpu) {
            elements.btnApplyCpu.disabled = selectedCpuIndices.size === 0;
        }
    }

    function handlePresetAll() {
        if (!cpuState || !cpuState.topology || !cpuState.topology.processors) return;
        const allIndices = cpuState.topology.processors.map(function (p) { return p.index; });
        populateCpuChips(allIndices);
    }

    function handlePresetPhysical() {
        if (!cpuState || !cpuState.topology || !cpuState.topology.processors) return;
        const physicalIndices = getPhysicalProcessorIndices(cpuState.topology.processors);
        populateCpuChips(physicalIndices);
    }

    function handlePresetClear() {
        populateCpuChips([]);
    }

    function handleEditCpu() {
        if (!cpuState || !cpuState.available) return;
        setCpuEditorOpen(true);
    }

    async function handleApplyCpuOverride() {
        if (!cpuState || !cpuState.sessionToken || !elements.btnApplyCpu) return;

        if (selectedCpuIndices.size === 0) {
            showCpuFeedback(cpuT('optimization.cpu.invalid', 'Select at least one processor.'), false);
            return;
        }

        elements.btnApplyCpu.disabled = true;
        showCpuFeedback('', false);
        try {
            const resp = await fetch('/api/v1/session-optimization/cpu', {
                method: 'POST',
                headers: getAuthHeaders(),
                body: JSON.stringify({
                    sessionToken: cpuState.sessionToken,
                    mode: cpuEditMode,
                    indices: Array.from(selectedCpuIndices)
                })
            });

            if (resp.status === 401) {
                clearStoredCredential();
                updateAuthUi(false, 'Pairing Required');
                return;
            }
            if (resp.status === 403) {
                showCpuFeedback(cpuT('optimization.cpu.permission', 'This device does not have permission for Game CPU Control.'), false);
                return;
            }

            let data = null;
            try { data = await resp.json(); } catch (_) { }

            const isSuccess = !!(data && data.success);
            const errorCode = data && data.errorCode ? data.errorCode : (!resp.ok ? 'apply_failed' : 'applied');
            if (errorCode === 'stale_session' || errorCode === 'target_lost') {
                showCpuFeedback(cpuT('optimization.cpu.stale', 'Session changed; refresh and try again.'), false);
            } else if (isSuccess) {
                showCpuFeedback(cpuT('optimization.cpu.applied', 'Temporary CPU configuration applied.'), true);
            } else {
                showCpuFeedback(cpuT('optimization.cpu.' + errorCode, data && data.message ? data.message : 'Failed to apply CPU configuration.'), false);
            }

            if (isSuccess) setCpuEditorOpen(false);
            setTimeout(fetchSessionCpuState, 500);
        } catch (_) {
            showCpuFeedback(cpuT('optimization.cpu.applyFailed', 'Failed to apply CPU configuration.'), false);
        } finally {
            elements.btnApplyCpu.disabled = selectedCpuIndices.size === 0;
        }
    }

    async function handleResetCpuOverride() {
        if (!cpuState || !cpuState.sessionToken || !elements.btnRestoreCpu) return;
        elements.btnRestoreCpu.disabled = true;
        showCpuFeedback('', false);
        try {
            const resp = await fetch('/api/v1/session-optimization/cpu/reset', {
                method: 'POST',
                headers: getAuthHeaders(),
                body: JSON.stringify({ sessionToken: cpuState.sessionToken })
            });

            if (resp.status === 401) {
                clearStoredCredential();
                updateAuthUi(false, 'Pairing Required');
                return;
            }
            if (resp.status === 403) {
                showCpuFeedback(cpuT('optimization.cpu.permission', 'This device does not have permission for Game CPU Control.'), false);
                return;
            }

            let data = null;
            try { data = await resp.json(); } catch (_) { }

            const isSuccess = !!(data && data.success);
            const errorCode = data && data.errorCode ? data.errorCode : (!resp.ok ? 'apply_failed' : 'restored');
            if (errorCode === 'stale_session' || errorCode === 'target_lost') {
                showCpuFeedback(cpuT('optimization.cpu.stale', 'Session changed; refresh and try again.'), false);
            } else if (isSuccess) {
                showCpuFeedback(cpuT('optimization.cpu.restored', 'CPU configuration restored.'), true);
            } else {
                showCpuFeedback(cpuT('optimization.cpu.' + errorCode, data && data.message ? data.message : 'Failed to restore CPU configuration.'), false);
            }

            setTimeout(fetchSessionCpuState, 500);
        } catch (_) {
            showCpuFeedback(cpuT('optimization.cpu.restoreFailed', 'Failed to restore CPU configuration.'), false);
        } finally {
            elements.btnRestoreCpu.disabled = false;
        }
    }

    // Initialization
