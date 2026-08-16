'use strict';

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

    async function loadTargets() {
        try {
            const response = await fetch('/api/v1/benchmarks/targets', {
                headers: getAuthHeaders()
            });

            if (response.status === 401) {
                clearStoredCredential();
                updateAuthUi(false, 'Pairing Required');
                return;
            }
            if (response.status === 403) {
                // Authenticated but missing read:benchmarks. Never claim a server error or
                // "no games": clear stale options and present an explicit permission state.
                const i18n403 = window.FrameHubI18n;
                elements.targetSelect.innerHTML = '';
                const opt403 = document.createElement('option');
                opt403.value = '';
                opt403.textContent = i18n403 ? i18n403.t('benchmark.targetsPermissionRequired') : 'Benchmark target permission required';
                elements.targetSelect.appendChild(opt403);
                elements.targetSelect.disabled = true;
                elements.targetCountBadge.textContent = i18n403 ? i18n403.t('benchmark.permissionRequired') : 'Permission required';
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
                clearStoredCredential();
                updateAuthUi(false, 'Pairing Required');
                return;
            } else if (response.status === 403) {
                if (getStoredCredential()) updateAuthUi(true, 'Paired Device');
                return;
            } else if (response.ok) {
                const credential = getStoredCredential();
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
