(function () {
    'use strict';

    const STORAGE_KEY = 'companion_language';

    const translations = {
        en: {
            'brand.title': 'FrameHub Companion',
            'brand.subtitle': 'Benchmark Control',
            'auth.checking': 'Checking Pairing...',
            'auth.connected': 'Paired & Connected',
            'auth.disconnected': 'Unpaired / Local',
            'auth.required': 'Pairing Required',

            'nav.home': 'Home',
            'nav.library': 'Library',
            'nav.benchmarks': 'Benchmarks',
            'nav.settings': 'Settings',

            'pairing.title': 'Device Pairing',
            'pairing.badge': 'Required for LAN control',
            'pairing.instructions': 'Enter the pairing token shown on your desktop or scan the QR code to pair this device.',
            'pairing.tokenLabel': 'Pairing Token',
            'pairing.tokenPlaceholder': 'Enter 6-character pairing token...',
            'pairing.nameLabel': 'Device Name',
            'pairing.namePlaceholder': 'e.g. Mobile Browser',
            'pairing.submitBtn': 'Pair Device',
            'pairing.pendingMsg': 'Pairing request submitted. Please approve on your desktop...',

            'home.noGame': 'No Game Detected',
            'home.gameRunning': 'Running',
            'home.gameNotRunning': 'Not Running',
            'home.noticeBanner': 'Benchmark capture in progress — live monitoring paused',
            'home.liveSubtitle': 'Live Performance (PresentMon)',
            'home.fps': 'Current FPS',
            'home.frametime': 'Frametime',
            'home.oneLow': '1% Low FPS',
            'home.pointOneLow': '0.1% Low FPS',
            'home.hwSubtitle': 'Hardware Telemetry',
            'home.cpuLoad': 'CPU Load',
            'home.cpuTemp': 'CPU Temp',
            'home.gpuLoad': 'GPU Load',
            'home.gpuTemp': 'GPU Temp',
            'home.ramUsage': 'RAM Usage',
            'home.vramUsage': 'VRAM Usage',

            'benchmark.setupTitle': 'Capture Setup',
            'benchmark.targetLabel': 'Benchmark Target',
            'benchmark.loadingTargets': 'Loading targets...',
            'benchmark.noTargets': 'No running games detected',
            'benchmark.targetsAvailable': 'available',
            'benchmark.targetsUnavailable': 'Targets unavailable',
            'benchmark.durationLabel': 'Duration (Seconds)',
            'benchmark.countdownLabel': 'Countdown (Seconds)',
            'benchmark.secondsUnit': 'seconds',
            'benchmark.instantCountdown': 'Instant (0s)',
            'benchmark.startBtn': '▶ Start Benchmark',
            'benchmark.stopBtn': '⏹ Stop Benchmark',

            'benchmark.statusTitle': 'Live Benchmark Status',
            'benchmark.activeTargetLabel': 'Active Game Target',
            'benchmark.noneSelected': 'None selected',
            'benchmark.countdownStatLabel': 'Countdown',
            'benchmark.elapsedStatLabel': 'Elapsed Time',

            'benchmark.state.idle': 'Idle',
            'benchmark.state.waiting': 'Waiting',
            'benchmark.state.capturing': 'Capturing',
            'benchmark.state.completing': 'Completing',
            'benchmark.state.stopping': 'Stopping',
            'benchmark.state.completed': 'Completed',
            'benchmark.state.cancelled': 'Cancelled',
            'benchmark.state.failed': 'Failed',

            'result.title': 'Latest Benchmark Result',
            'result.completedBadge': 'Completed',
            'result.avgFps': 'Average FPS',
            'result.oneLow': '1% Low FPS',
            'result.pointOneLow': '0.1% Low FPS',
            'result.p99Frametime': 'P99 Frame Time',
            'result.duration': 'Duration',
            'result.quality': 'Quality Level',
            'result.chartTitle': 'Frametime Graph (ms)',
            'result.noChartData': 'No frametime data available',
            'result.chartPoints': 'points (Downsampled)',

            'history.title': 'Recent Sessions',
            'history.compareBtn': 'Compare Selected',
            'history.refreshBtn': 'Refresh',
            'history.thSelect': 'Select',
            'history.thGame': 'Game',
            'history.thStatus': 'Status',
            'history.thDuration': 'Duration',
            'history.thAvgFps': 'Avg FPS',
            'history.thCapturedAt': 'Captured At',
            'history.thAction': 'Action',
            'history.noSessions': 'No benchmark sessions recorded yet.',
            'history.loadBtn': 'Load',

            'comparison.title': 'Session Comparison',
            'comparison.closeBtn': 'Close',
            'comparison.sessionA': 'Session A (Baseline)',
            'comparison.sessionB': 'Session B (Comparison)',
            'comparison.loading': 'Loading comparison...',
            'comparison.thMetric': 'Metric',
            'comparison.thSessionA': 'Session A',
            'comparison.thSessionB': 'Session B',
            'comparison.thDelta': 'Delta',
            'comparison.thPctDelta': '% Delta',
            'comparison.thEvaluation': 'Evaluation',
            'comparison.better': '▲ Better',
            'comparison.worse': '▼ Worse',
            'comparison.neutral': 'Neutral',

            'settings.title': 'Companion Settings',
            'settings.langLabel': 'Presentation Language',
            'settings.langDesc': 'Select the interface language for FrameHub Companion.',
            'settings.langEn': 'English',
            'settings.langPl': 'Polski',

            'library.title': 'Game Library',
            'library.refresh': 'Refresh',
            'library.loading': 'Loading games...',
            'library.empty': 'No games or applications in library.',
            'library.launch': 'Launch',
            'library.launching': 'Launching...',
            'library.running': 'Running',
            'library.available': 'Ready',
            'library.loadFailed': 'Failed to load library items.',

            'launch.launched': 'Game launched successfully.',
            'launch.not_found': 'Library item was not found.',
            'launch.already_running': 'Game is already running.',
            'launch.benchmark_active': 'Cannot launch game while benchmark capture is active.',
            'launch.launch_in_progress': 'Another launch is already in progress. Please wait.',
            'launch.not_launchable': 'Item is not launchable.',
            'launch.executable_missing': 'Executable file was not found on desktop.',
            'launch.launch_failed': 'Failed to launch executable.',
            'launch.unauthorized': 'Authentication required to launch games.',
            'launch.forbidden': 'Device does not have remote launch permission.',

            'optimization.title': 'Session Optimization',
            'optimization.active': 'Active',
            'optimization.inactive': 'Inactive',
            'optimization.game': 'Active Game:',
            'optimization.suspended': 'Suspended Apps:',
            'optimization.taskbarHidden': 'Taskbar Hidden',
            'optimization.recoveryPending': 'Recovery Pending',
            'optimization.apply': 'Start Optimization',
            'optimization.applying': 'Starting...',
            'optimization.restore': 'Restore Session',
            'optimization.restoring': 'Restoring...',
            'optimization.refresh': 'Refresh',

            'optimization.applied': 'Session optimization started.',
            'optimization.restored': 'Session restored successfully.',
            'optimization.no_game': 'No running game detected to optimize.',
            'optimization.not_running': 'Game is not running.',
            'optimization.already_active': 'Session optimization is already active.',
            'optimization.not_active': 'No active session to restore.',
            'optimization.benchmark_active': 'Cannot change optimization while benchmark is active.',
            'optimization.operation_in_progress': 'Another optimization operation is in progress.',
            'optimization.apply_failed': 'Failed to start session optimization.',
            'optimization.restore_failed': 'Failed to restore session optimization.',
            'optimization.unauthorized': 'Authentication required for optimization.',
            'optimization.forbidden': 'Device does not have session optimization permission.',

            'footer.text': 'FrameHub Companion • Single Source of Truth: BenchmarkCaptureCoordinator',
            'errors.serviceUnavailable': 'Benchmark provider service is unavailable.',
            'errors.captureFailed': 'Benchmark capture failed: '
        },
        pl: {
            'brand.title': 'FrameHub Companion',
            'brand.subtitle': 'Sterowanie Benchmarkiem',
            'auth.checking': 'Sprawdzanie parowania...',
            'auth.connected': 'Sparowano i połączono',
            'auth.disconnected': 'Niesparowano / Lokalny',
            'auth.required': 'Wymagane parowanie',

            'nav.home': 'Główna',
            'nav.library': 'Biblioteka',
            'nav.benchmarks': 'Benchmarki',
            'nav.settings': 'Ustawienia',

            'pairing.title': 'Parowanie Urządzenia',
            'pairing.badge': 'Wymagane do sterowania przez LAN',
            'pairing.instructions': 'Wprowadź kod parowania widoczny na komputerze stacjonarnym lub zeskanuj kod QR.',
            'pairing.tokenLabel': 'Kod Parowania',
            'pairing.tokenPlaceholder': 'Wprowadź 6-cyfrowy kod parowania...',
            'pairing.nameLabel': 'Nazwa Urządzenia',
            'pairing.namePlaceholder': 'np. Przeglądarka Mobilna',
            'pairing.submitBtn': 'Sparuj Urządzenie',
            'pairing.pendingMsg': 'Wysłano prośbę o parowanie. Zatwierdź ją na komputerze stacjonarnym...',

            'home.noGame': 'Nie wykryto gry',
            'home.gameRunning': 'Uruchomiona',
            'home.gameNotRunning': 'Wstrzymana',
            'home.noticeBanner': 'Przechwytywanie benchmarku w toku — podgląd na żywo wstrzymany',
            'home.liveSubtitle': 'Wydajność na żywo (PresentMon)',
            'home.fps': 'Aktualne FPS',
            'home.frametime': 'Czas klatki',
            'home.oneLow': '1% Low FPS',
            'home.pointOneLow': '0.1% Low FPS',
            'home.hwSubtitle': 'Telemetria Sprzętowa',
            'home.cpuLoad': 'Obciążenie CPU',
            'home.cpuTemp': 'Temp. CPU',
            'home.gpuLoad': 'Obciążenie GPU',
            'home.gpuTemp': 'Temp. GPU',
            'home.ramUsage': 'Użycie RAM',
            'home.vramUsage': 'Użycie VRAM',

            'benchmark.setupTitle': 'Konfiguracja Przechwytywania',
            'benchmark.targetLabel': 'Cel Benchmarku',
            'benchmark.loadingTargets': 'Wczytywanie celów...',
            'benchmark.noTargets': 'Nie wykryto uruchomionych gier',
            'benchmark.targetsAvailable': 'dostępnych',
            'benchmark.targetsUnavailable': 'Cele niedostępne',
            'benchmark.durationLabel': 'Czas trwania (sekundy)',
            'benchmark.countdownLabel': 'Odliczanie (sekundy)',
            'benchmark.secondsUnit': 'sekund',
            'benchmark.instantCountdown': 'Natychmiast (0s)',
            'benchmark.startBtn': '▶ Rozpocznij Benchmark',
            'benchmark.stopBtn': '⏹ Zatrzymaj Benchmark',

            'benchmark.statusTitle': 'Stan Benchmarku na Żywo',
            'benchmark.activeTargetLabel': 'Aktywny Cel Gry',
            'benchmark.noneSelected': 'Brak wybranego',
            'benchmark.countdownStatLabel': 'Odliczanie',
            'benchmark.elapsedStatLabel': 'Czas trwania',

            'benchmark.state.idle': 'Bezczynny',
            'benchmark.state.waiting': 'Oczekiwanie',
            'benchmark.state.capturing': 'Przechwytywanie',
            'benchmark.state.completing': 'Finalizowanie',
            'benchmark.state.stopping': 'Zatrzymywanie',
            'benchmark.state.completed': 'Zakończony',
            'benchmark.state.cancelled': 'Anulowany',
            'benchmark.state.failed': 'Błąd',

            'result.title': 'Ostatni Wynik Benchmarku',
            'result.completedBadge': 'Zakończony',
            'result.avgFps': 'Średnie FPS',
            'result.oneLow': '1% Low FPS',
            'result.pointOneLow': '0.1% Low FPS',
            'result.p99Frametime': 'Czas klatki P99',
            'result.duration': 'Czas trwania',
            'result.quality': 'Jakość danych',
            'result.chartTitle': 'Wykres Czasu Klatki (ms)',
            'result.noChartData': 'Brak danych czasu klatki',
            'result.chartPoints': 'punktów (Próbkowane)',

            'history.title': 'Ostatnie Sesje',
            'history.compareBtn': 'Porównaj Wybrane',
            'history.refreshBtn': 'Odśwież',
            'history.thSelect': 'Wybór',
            'history.thGame': 'Gra',
            'history.thStatus': 'Status',
            'history.thDuration': 'Czas',
            'history.thAvgFps': 'Śr. FPS',
            'history.thCapturedAt': 'Data',
            'history.thAction': 'Akcja',
            'history.noSessions': 'Brak zarejestrowanych sesji benchmarku.',
            'history.loadBtn': 'Pobierz',

            'comparison.title': 'Porównanie Sesji',
            'comparison.closeBtn': 'Zamknij',
            'comparison.sessionA': 'Sesja A (Bazowa)',
            'comparison.sessionB': 'Sesja B (Porównywana)',
            'comparison.loading': 'Wczytywanie porównania...',
            'comparison.thMetric': 'Metryka',
            'comparison.thSessionA': 'Sesja A',
            'comparison.thSessionB': 'Sesja B',
            'comparison.thDelta': 'Różnica',
            'comparison.thPctDelta': '% Różnicy',
            'comparison.thEvaluation': 'Ocena',
            'comparison.better': '▲ Lepszy',
            'comparison.worse': '▼ Gorszy',
            'comparison.neutral': 'Neutralny',

            'settings.title': 'Ustawienia Companion',
            'settings.langLabel': 'Język Prezentacji',
            'settings.langDesc': 'Wybierz język interfejsu aplikacji FrameHub Companion.',
            'settings.langEn': 'English',
            'settings.langPl': 'Polski',

            'library.title': 'Biblioteka Gier',
            'library.refresh': 'Odśwież',
            'library.loading': 'Wczytywanie gier...',
            'library.empty': 'Brak gier lub aplikacji w bibliotece.',
            'library.launch': 'Uruchom',
            'library.launching': 'Uruchamianie...',
            'library.running': 'Działa',
            'library.available': 'Gotowa',
            'library.loadFailed': 'Nie udało się wczytać biblioteki gier.',

            'launch.launched': 'Gra została pomyślnie uruchomiona.',
            'launch.not_found': 'Nie znaleziono wybranego elementu biblioteki.',
            'launch.already_running': 'Gra jest już uruchomiona.',
            'launch.benchmark_active': 'Nie można uruchomić gry w trakcie trwania benchmarku.',
            'launch.launch_in_progress': 'Trwa uruchamianie innej gry. Poczekaj chwilę.',
            'launch.not_launchable': 'Element nie nadaje się do uruchomienia.',
            'launch.executable_missing': 'Nie znaleziono pliku wykonywalnego na komputerze.',
            'launch.launch_failed': 'Uruchomienie pliku wykonywalnego nie powiodło się.',
            'launch.unauthorized': 'Wymagana autoryzacja, aby uruchamiać gry.',
            'launch.forbidden': 'Urządzenie nie posiada uprawnienia do uruchamiania gier.',

            'optimization.title': 'Optymalizacja Sesji',
            'optimization.active': 'Aktywna',
            'optimization.inactive': 'Nieaktywna',
            'optimization.game': 'Aktywna gra:',
            'optimization.suspended': 'Wstrzymane aplikacje:',
            'optimization.taskbarHidden': 'Pasek zadań ukryty',
            'optimization.recoveryPending': 'Oczekiwanie na przywrócenie',
            'optimization.apply': 'Rozpocznij Optymalizację',
            'optimization.applying': 'Uruchamianie...',
            'optimization.restore': 'Przywróć Sesję',
            'optimization.restoring': 'Przywracanie...',
            'optimization.refresh': 'Odśwież',

            'optimization.applied': 'Optymalizacja sesji została uruchomiona.',
            'optimization.restored': 'Sesja została pomyślnie przywrócona.',
            'optimization.no_game': 'Nie wykryto uruchomionej gry do optymalizacji.',
            'optimization.not_running': 'Gra nie jest uruchomiona.',
            'optimization.already_active': 'Optymalizacja sesji jest już aktywna.',
            'optimization.not_active': 'Brak aktywnej sesji do przywrócenia.',
            'optimization.benchmark_active': 'Nie można modyfikować optymalizacji w trakcie benchmarku.',
            'optimization.operation_in_progress': 'Inna operacja optymalizacji jest w toku.',
            'optimization.apply_failed': 'Nie udało się uruchomić optymalizacji sesji.',
            'optimization.restore_failed': 'Nie udało się przywrócić sesji optymalizacji.',
            'optimization.unauthorized': 'Wymagana autoryzacja do sterowania optymalizacją.',
            'optimization.forbidden': 'Urządzenie nie posiada uprawnienia do optymalizacji sesji.',

            'footer.text': 'FrameHub Companion • Jedno Źródło Prawdy: BenchmarkCaptureCoordinator',
            'errors.serviceUnavailable': 'Usługa dostawcy benchmarków jest niedostępna.',
            'errors.captureFailed': 'Przechwytywanie benchmarku nie powiodło się: '
        }
    };

    let currentLanguage = 'en';

    function normalizeLanguage(lang) {
        if (typeof lang === 'string' && lang.toLowerCase().startsWith('pl')) {
            return 'pl';
        }
        return 'en';
    }

    function getInitialLanguage() {
        try {
            const stored = localStorage.getItem(STORAGE_KEY);
            if (stored && (stored === 'en' || stored === 'pl')) {
                return stored;
            }
        } catch (_) { }

        const navLangs = navigator.languages || [navigator.language || 'en'];
        for (let i = 0; i < navLangs.length; i++) {
            if (navLangs[i] && navLangs[i].toLowerCase().startsWith('pl')) {
                return 'pl';
            }
        }
        return 'en';
    }

    function t(key, fallback) {
        if (!key) return fallback || '';
        const dict = translations[currentLanguage] || translations.en;
        if (dict && typeof dict[key] === 'string') {
            return dict[key];
        }
        if (translations.en && typeof translations.en[key] === 'string') {
            return translations.en[key];
        }
        return fallback !== undefined ? fallback : key;
    }

    function applyTranslations(root) {
        const scope = root || document;
        const elements = scope.querySelectorAll('[data-i18n]');
        elements.forEach(function (el) {
            const key = el.getAttribute('data-i18n');
            if (key) {
                el.textContent = t(key, el.textContent);
            }
        });
    }

    function setLanguage(lang, persist) {
        const normalized = normalizeLanguage(lang);
        currentLanguage = normalized;
        if (persist) {
            try {
                localStorage.setItem(STORAGE_KEY, normalized);
            } catch (_) { }
        }
        applyTranslations(document);
        window.dispatchEvent(new CustomEvent('framehub:languagechanged', { detail: { language: normalized } }));
        return normalized;
    }

    function translateState(stateId) {
        if (!stateId) return t('benchmark.state.idle');
        const key = 'benchmark.state.' + String(stateId).toLowerCase();
        return t(key, stateId);
    }

    // Initialize initial language
    currentLanguage = getInitialLanguage();

    window.FrameHubI18n = {
        translations: translations,
        normalizeLanguage: normalizeLanguage,
        getInitialLanguage: getInitialLanguage,
        getCurrentLanguage: function () { return currentLanguage; },
        t: t,
        setLanguage: setLanguage,
        applyTranslations: applyTranslations,
        translateState: translateState
    };
})();
