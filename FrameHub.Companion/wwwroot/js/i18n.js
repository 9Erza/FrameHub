(function () {
    'use strict';

    const STORAGE_KEY = 'companion_language';

    const translations = {
        en: {
            'brand.title': 'FrameHub Companion',
            'brand.subtitle': 'Game performance & optimization',
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
            'pairing.instructions': 'Enter the pairing token shown on your desktop to pair this device.',
            'pairing.tokenLabel': 'Pairing Token',
            'pairing.tokenPlaceholder': 'Enter pairing token...',
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
            'home.hwDisabledTitle': 'Hardware telemetry is disabled.',
            'home.hwDisabledDesc': 'Enable monitoring to display CPU, GPU, RAM and VRAM.',
            'home.hwEnableBtn': 'Enable monitoring',
            'home.hwDisableBtn': 'Disable monitoring',
            'home.hwStatusOn': 'Enabled',
            'home.hwStatusOff': 'Disabled',
            'home.hwPermissionRequired': 'write:telemetry permission required to change hardware monitor state',
            'home.cpuElevationHint': 'CPU temperature may require running FrameHub as administrator.',
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
            'benchmark.targetsPermissionRequired': 'Benchmark target permission required',
            'benchmark.permissionRequired': 'Permission required',
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
            'settings.langLabel': 'Language',
            'settings.langDesc': 'Select the interface language for FrameHub Companion.',
            'settings.langEn': 'English',
            'settings.langPl': 'Polski',
            'settings.connectionTitle': 'Connection & Device',
            'settings.unpairBtn': 'Unpair Device',
            'settings.unpairDesc': 'Removes the saved pairing credential from this browser.',
            'settings.aboutTitle': 'About',
            'settings.aboutDesc': 'FrameHub Companion connects locally to FrameHub running on your Windows PC.',

            'library.title': 'Game Library',
            'library.refresh': 'Refresh',
            'library.loading': 'Loading games...',
            'library.empty': 'No games or applications in library.',
            'library.launch': 'Launch',
            'library.launching': 'Launching...',
            'library.running': 'Running',
            'library.missingBadge': 'Missing',
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

            'backgroundApps.title': 'Background Apps',
            'backgroundApps.description': 'Trusted apps explicitly enabled in the desktop Library.',
            'backgroundApps.refresh': 'Refresh',
            'backgroundApps.loading': 'Loading background apps...',
            'backgroundApps.empty': 'No background apps are enabled for remote control.',
            'backgroundApps.running': 'Running',
            'backgroundApps.stopped': 'Stopped',
            'backgroundApps.start': 'Start',
            'backgroundApps.stop': 'Stop',
            'backgroundApps.busy': 'Busy...',
            'backgroundApps.permissionUnavailable': 'This device does not have background app permission.',
            'backgroundApps.unauthorized': 'Authentication is required for background app control.',
            'backgroundApps.loadFailed': 'Failed to load background apps.',
            'backgroundApps.started': 'Background app started.',
            'backgroundApps.stop_succeeded': 'Background app stopped.',
            'backgroundApps.not_found': 'Background app was not found.',
            'backgroundApps.not_eligible': 'This app is not eligible for remote control.',
            'backgroundApps.already_running': 'Background app is already running.',
            'backgroundApps.not_running': 'Background app is not running.',
            'backgroundApps.operation_busy': 'Another background app operation is in progress.',
            'backgroundApps.benchmark_active': 'Background apps cannot be changed during benchmark capture.',
            'backgroundApps.executable_missing': 'The trusted executable was not found.',
            'backgroundApps.launch_failed': 'Failed to start the background app.',
            'backgroundApps.stop_failed': 'The app could not be stopped safely.',
            'backgroundApps.invalid_id': 'Invalid background app identifier.',
            'backgroundApps.operationFailed': 'Background app operation failed.',

            'optimization.title': 'Session Optimization',
            'optimization.active': 'Active',
            'optimization.inactive': 'Inactive',
            'optimization.game': 'Active Game:',
            'optimization.suspended': 'Suspended Apps:',
            'optimization.taskbarHidden': 'Taskbar Hidden',
            'optimization.recoveryPending': 'Recovery Pending',
            'optimization.description': 'Suspends selected background processes during gameplay to reduce their impact on performance.',
            'optimization.apply': 'Start session optimization',
            'optimization.applying': 'Starting...',
            'optimization.restore': 'Restore session',
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
            'optimization.restore_partial': 'Restore is incomplete. Recovery remains pending.',
            'optimization.restore_manual_required': 'Automatic recovery stopped because prior OS-change ownership is ambiguous. End listed processes or restore taskbar state manually, then retry.',
            'optimization.state_persist_failed': 'Recovery state could not be saved. Recovery may remain pending.',
            'optimization.state_clear_failed': 'System state was restored, but recovery metadata could not be cleared.',
            'optimization.unauthorized': 'Authentication required for optimization.',
            'optimization.forbidden': 'Device does not have session optimization permission.',

            'optimization.cpu.title': 'Game CPU Assignment',
            'optimization.cpu.description': 'Temporarily change how the currently running game uses the CPU.',
            'optimization.cpu.sourceLabel': 'Configuration source',
            'optimization.cpu.sourceSystem': 'System',
            'optimization.cpu.sourceProfile': 'Game profile',
            'optimization.cpu.sourceOverride': 'Temporary override',
            'optimization.cpu.modeLabel': 'CPU mode',
            'optimization.cpu.defaultSetting': 'Default system setting',
            'optimization.cpu.systemSetting': 'System setting',
            'optimization.cpu.availableProcessors': 'Available processors',
            'optimization.cpu.affinity': 'Affinity',
            'optimization.cpu.cpuSets': 'CPU Sets',
            'optimization.cpu.recommended': 'Recommended',
            'optimization.cpu.cpuSetsHelper': 'CPU Sets are recommended because they let the Windows scheduler manage the game more flexibly across the selected processors.',
            'optimization.cpu.none': 'None',
            'optimization.cpu.presetAll': 'All',
            'optimization.cpu.presetPhysical': 'Physical only',
            'optimization.cpu.presetClear': 'Clear',
            'optimization.cpu.edit': 'Edit CPU settings',
            'optimization.cpu.apply': 'Apply for this session',
            'optimization.cpu.cancel': 'Cancel',
            'optimization.cpu.restore': 'Restore profile settings',
            'optimization.cpu.restoreOriginal': 'Restore initial settings',
            'optimization.cpu.sessionOnlyNotice': 'Changes apply only to the current game session and are not saved to the profile.',
            'optimization.cpu.noGame': 'No active game. Launch a game from the library or manually to view and temporarily change its CPU settings.',
            'optimization.cpu.unavailable': 'CPU control unavailable.',
            'optimization.cpu.protected': 'CPU control is unavailable for this game due to security policies.',
            'optimization.cpu.permission': 'This device does not have permission for Game CPU Assignment.',
            'optimization.cpu.stale': 'Session changed; refresh and try again.',
            'optimization.cpu.invalid': 'Select at least one processor.',
            'optimization.cpu.applied': 'Temporary CPU configuration applied.',
            'optimization.cpu.restored': 'CPU configuration restored.',
            'optimization.cpu.applyFailed': 'Failed to apply CPU configuration.',
            'optimization.cpu.restoreFailed': 'Failed to restore CPU configuration.',
            'optimization.cpu.invalid_selection': 'The selected CPU configuration is not valid for this system.',
            'optimization.cpu.benchmark_active': 'CPU configuration cannot change while a benchmark is active.',

            'footer.text': 'FrameHub Companion • Single Source of Truth: BenchmarkCaptureCoordinator',
            'errors.serviceUnavailable': 'Benchmark provider service is unavailable.',
            'errors.captureFailed': 'Benchmark capture failed: '
        },
        pl: {
            'brand.title': 'FrameHub Companion',
            'brand.subtitle': 'Wydajność i optymalizacja gier',
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
            'pairing.instructions': 'Wprowadź kod parowania widoczny na komputerze stacjonarnym, aby sparować to urządzenie.',
            'pairing.tokenLabel': 'Kod Parowania',
            'pairing.tokenPlaceholder': 'Wprowadź kod parowania...',
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
            'home.hwSubtitle': 'Telemetria sprzętowa',
            'home.hwDisabledTitle': 'Telemetria sprzętowa jest wyłączona.',
            'home.hwDisabledDesc': 'Włącz monitoring, aby wyświetlać CPU, GPU, RAM i VRAM.',
            'home.hwEnableBtn': 'Włącz monitoring',
            'home.hwDisableBtn': 'Wyłącz monitoring',
            'home.hwStatusOn': 'Włączona',
            'home.hwStatusOff': 'Wyłączona',
            'home.hwPermissionRequired': 'Wymagane uprawnienie write:telemetry do zmiany stanu monitora sprzętowego',
            'home.cpuElevationHint': 'Temperatura CPU może wymagać uruchomienia FrameHub jako administrator.',
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
            'benchmark.targetsPermissionRequired': 'Wymagane uprawnienie do celów benchmarku',
            'benchmark.permissionRequired': 'Brak uprawnień',
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
            'settings.langLabel': 'Język',
            'settings.langDesc': 'Wybierz język interfejsu aplikacji FrameHub Companion.',
            'settings.langEn': 'English',
            'settings.langPl': 'Polski',
            'settings.connectionTitle': 'Połączenie i urządzenie',
            'settings.unpairBtn': 'Rozparuj urządzenie',
            'settings.unpairDesc': 'Usuwa zapisane dane parowania z tej przeglądarki.',
            'settings.aboutTitle': 'Informacje',
            'settings.aboutDesc': 'FrameHub Companion łączy się lokalnie z aplikacją FrameHub na komputerze Windows.',

            'library.title': 'Biblioteka Gier',
            'library.refresh': 'Odśwież',
            'library.loading': 'Wczytywanie gier...',
            'library.empty': 'Brak gier lub aplikacji w bibliotece.',
            'library.launch': 'Uruchom',
            'library.launching': 'Uruchamianie...',
            'library.running': 'Działa',
            'library.missingBadge': 'Brak pliku',
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

            'backgroundApps.title': 'Aplikacje w tle',
            'backgroundApps.description': 'Zaufane aplikacje jawnie w\u0142\u0105czone w Bibliotece na komputerze.',
            'backgroundApps.refresh': 'Od\u015bwie\u017c',
            'backgroundApps.loading': 'Wczytywanie aplikacji w tle...',
            'backgroundApps.empty': 'Brak aplikacji w tle dopuszczonych do zdalnego sterowania.',
            'backgroundApps.running': 'Uruchomiona',
            'backgroundApps.stopped': 'Zatrzymana',
            'backgroundApps.start': 'Uruchom',
            'backgroundApps.stop': 'Zatrzymaj',
            'backgroundApps.busy': 'Trwa operacja...',
            'backgroundApps.permissionUnavailable': 'To urz\u0105dzenie nie ma uprawnienia do aplikacji w tle.',
            'backgroundApps.unauthorized': 'Sterowanie aplikacjami w tle wymaga autoryzacji.',
            'backgroundApps.loadFailed': 'Nie uda\u0142o si\u0119 wczyta\u0107 aplikacji w tle.',
            'backgroundApps.started': 'Aplikacja w tle zosta\u0142a uruchomiona.',
            'backgroundApps.stop_succeeded': 'Aplikacja w tle zosta\u0142a zatrzymana.',
            'backgroundApps.not_found': 'Nie znaleziono aplikacji w tle.',
            'backgroundApps.not_eligible': 'Ta aplikacja nie jest dopuszczona do zdalnego sterowania.',
            'backgroundApps.already_running': 'Aplikacja w tle jest ju\u017c uruchomiona.',
            'backgroundApps.not_running': 'Aplikacja w tle nie jest uruchomiona.',
            'backgroundApps.operation_busy': 'Trwa inna operacja na aplikacji w tle.',
            'backgroundApps.benchmark_active': 'Nie mo\u017cna zmienia\u0107 aplikacji w tle podczas benchmarku.',
            'backgroundApps.executable_missing': 'Nie znaleziono zaufanego pliku wykonywalnego.',
            'backgroundApps.launch_failed': 'Nie uda\u0142o si\u0119 uruchomi\u0107 aplikacji w tle.',
            'backgroundApps.stop_failed': 'Nie uda\u0142o si\u0119 bezpiecznie zatrzyma\u0107 aplikacji.',
            'backgroundApps.invalid_id': 'Nieprawid\u0142owy identyfikator aplikacji w tle.',
            'backgroundApps.operationFailed': 'Operacja na aplikacji w tle nie powiod\u0142a si\u0119.',

            'optimization.title': 'Optymalizacja Sesji',
            'optimization.active': 'Aktywna',
            'optimization.inactive': 'Nieaktywna',
            'optimization.game': 'Aktywna gra:',
            'optimization.suspended': 'Wstrzymane aplikacje:',
            'optimization.taskbarHidden': 'Pasek zadań ukryty',
            'optimization.recoveryPending': 'Oczekiwanie na przywrócenie',
            'optimization.description': 'Wstrzymuje wybrane procesy działające w tle podczas gry, aby ograniczyć ich wpływ na wydajność.',
            'optimization.apply': 'Uruchom optymalizację sesji',
            'optimization.applying': 'Uruchamianie...',
            'optimization.restore': 'Przywróć sesję',
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
            'optimization.restore_partial': 'Przywracanie jest niepełne. Odzyskiwanie pozostaje aktywne.',
            'optimization.restore_manual_required': 'Automatyczne odzyskiwanie zatrzymano, ponieważ własność wcześniejszej zmiany systemowej jest niejednoznaczna. Zakończ wskazane procesy lub ręcznie przywróć stan paska zadań, a następnie spróbuj ponownie.',
            'optimization.state_persist_failed': 'Nie udało się zapisać stanu odzyskiwania. Odzyskiwanie może nadal być wymagane.',
            'optimization.state_clear_failed': 'Stan systemu przywrócono, ale nie udało się usunąć metadanych odzyskiwania.',
            'optimization.unauthorized': 'Wymagana autoryzacja do sterowania optymalizacją.',
            'optimization.forbidden': 'Urządzenie nie posiada uprawnienia do optymalizacji sesji.',

            'optimization.cpu.title': 'Przydział CPU dla gry',
            'optimization.cpu.description': 'Tymczasowo zmień sposób wykorzystania procesora przez aktualnie uruchomioną grę.',
            'optimization.cpu.sourceLabel': 'Źródło konfiguracji',
            'optimization.cpu.sourceSystem': 'System',
            'optimization.cpu.sourceProfile': 'Profil gry',
            'optimization.cpu.sourceOverride': 'Tymczasowe ustawienia',
            'optimization.cpu.modeLabel': 'Tryb CPU',
            'optimization.cpu.defaultSetting': 'Standardowe ustawienie',
            'optimization.cpu.systemSetting': 'Ustawienie systemowe',
            'optimization.cpu.availableProcessors': 'Dostępne procesory',
            'optimization.cpu.affinity': 'Koligacja',
            'optimization.cpu.cpuSets': 'Zestawy CPU',
            'optimization.cpu.recommended': 'Zalecane',
            'optimization.cpu.cpuSetsHelper': 'Zestawy CPU są zalecane, ponieważ pozwalają harmonogramowi Windows elastyczniej zarządzać grą na wybranych procesorach.',
            'optimization.cpu.none': 'Brak',
            'optimization.cpu.presetAll': 'Wszystkie',
            'optimization.cpu.presetPhysical': 'Tylko fizyczne',
            'optimization.cpu.presetClear': 'Wyczyść',
            'optimization.cpu.edit': 'Edytuj ustawienia CPU',
            'optimization.cpu.apply': 'Zastosuj dla tej sesji',
            'optimization.cpu.cancel': 'Anuluj',
            'optimization.cpu.restore': 'Przywróć ustawienia profilu',
            'optimization.cpu.restoreOriginal': 'Przywróć ustawienia początkowe',
            'optimization.cpu.sessionOnlyNotice': 'Zmiany dotyczą tylko bieżącej sesji gry i nie są zapisywane w profilu.',
            'optimization.cpu.noGame': 'Brak aktywnej gry. Uruchom grę z biblioteki lub ręcznie, aby wyświetlić i tymczasowo zmienić jej ustawienia CPU.',
            'optimization.cpu.unavailable': 'Przydział CPU niedostępny.',
            'optimization.cpu.protected': 'Przydział CPU jest niedostępny dla tej gry ze względu na zasady bezpieczeństwa.',
            'optimization.cpu.permission': 'To urządzenie nie ma uprawnienia do przydziału CPU dla gry.',
            'optimization.cpu.stale': 'Sesja uległa zmianie; odśwież i spróbuj ponownie.',
            'optimization.cpu.invalid': 'Wybierz co najmniej jeden procesor.',
            'optimization.cpu.applied': 'Zastosowano tymczasową konfigurację CPU.',
            'optimization.cpu.restored': 'Przywrócono konfigurację CPU.',
            'optimization.cpu.applyFailed': 'Nie udało się zastosować konfiguracji CPU.',
            'optimization.cpu.restoreFailed': 'Nie udało się przywrócić konfiguracji CPU.',
            'optimization.cpu.invalid_selection': 'Wybrana konfiguracja CPU jest nieprawidłowa dla tego systemu.',
            'optimization.cpu.benchmark_active': 'Nie można zmieniać konfiguracji CPU podczas benchmarku.',

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
