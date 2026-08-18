'use strict';

    let isLibraryFetching = false;
    let cachedLibraryItems = null;
    const iconUrlCache = new Map();

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
                clearStoredCredential();
                updateAuthUi(false, i18n ? i18n.t('auth.required') : 'Pairing Required');
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

            const cardHeader = document.createElement('div');
            cardHeader.className = 'library-card-header';

            const iconWrap = document.createElement('div');
            iconWrap.className = 'library-card-icon-wrap';

            if (item.hasIcon) {
                loadCardIcon(item.id, iconWrap);
            } else {
                renderIconPlaceholder(iconWrap);
            }
            cardHeader.appendChild(iconWrap);

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
            } else if (item.isExecutableMissing) {
                const badgeMissing = document.createElement('span');
                badgeMissing.className = 'badge badge-warning';
                badgeMissing.textContent = i18n ? i18n.t('library.missingBadge') : 'Missing';
                badges.appendChild(badgeMissing);
            }

            info.appendChild(badges);
            cardHeader.appendChild(info);
            card.appendChild(cardHeader);

            const actions = document.createElement('div');
            actions.className = 'library-card-actions';

            const feedback = document.createElement('div');
            feedback.className = 'launch-feedback hidden';

            const btnLaunch = document.createElement('button');
            btnLaunch.type = 'button';
            btnLaunch.className = 'btn btn-primary btn-launch';

            if (item.isRunning) {
                btnLaunch.textContent = i18n ? i18n.t('library.running') : 'Running';
                btnLaunch.disabled = true;
            } else if (item.isExecutableMissing) {
                btnLaunch.textContent = i18n ? i18n.t('library.launch') : 'Launch';
                btnLaunch.disabled = true;
                btnLaunch.title = i18n ? i18n.t('launch.executable_missing') : 'Game executable was not found on desktop.';
            } else {
                btnLaunch.textContent = i18n ? i18n.t('library.launch') : 'Launch';
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

    async function loadCardIcon(itemId, wrapEl) {
        if (iconUrlCache.has(itemId)) {
            const cached = iconUrlCache.get(itemId);
            if (cached) {
                const img = document.createElement('img');
                img.className = 'library-card-icon';
                img.alt = '';
                img.src = cached;
                wrapEl.appendChild(img);
            } else {
                renderIconPlaceholder(wrapEl);
            }
            return;
        }

        try {
            const resp = await fetch('/api/v1/library/' + encodeURIComponent(itemId) + '/icon', {
                headers: getAuthHeaders()
            });
            if (resp.ok) {
                const blob = await resp.blob();
                const objectUrl = URL.createObjectURL(blob);
                iconUrlCache.set(itemId, objectUrl);
                const img = document.createElement('img');
                img.className = 'library-card-icon';
                img.alt = '';
                img.src = objectUrl;
                wrapEl.appendChild(img);
            } else {
                iconUrlCache.set(itemId, null);
                renderIconPlaceholder(wrapEl);
            }
        } catch (_) {
            iconUrlCache.set(itemId, null);
            renderIconPlaceholder(wrapEl);
        }
    }

    function renderIconPlaceholder(wrapEl) {
        const placeholder = document.createElement('div');
        placeholder.className = 'library-card-icon-placeholder';
        placeholder.textContent = '🎮';
        wrapEl.appendChild(placeholder);
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
                clearStoredCredential();
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
                clearStoredCredential();
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

    // Session Optimization Logic
