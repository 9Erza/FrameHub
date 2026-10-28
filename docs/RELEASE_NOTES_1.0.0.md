# FrameHub v1.0.0 — Release notes / Informacje o wydaniu

**Windows 10/11 · x64 · .NET 10 · MIT · 28 October / 28 października 2026**

[English](#english) · [Polski](#polski) · [Complete changelog (EN)](../CHANGELOG.md) · [Lista zmian (PL)](../CHANGELOG.pl.md)

> The official installer will appear as **`FrameHub-Setup-1.0.0.exe`** under **Assets** on [GitHub Releases](https://github.com/9Erza/FrameHub/releases) after publication. GitHub-generated source ZIP/TAR files are not installers. This document is the bilingual source for the GitHub Release description.

---

## English

### FrameHub 1.0.0: functional improvements, reliability and a redesigned workspace

FrameHub is an open-source Windows utility for deliberate, reversible control of games, CPU scheduling, background processes and performance measurement. **Version 1.0.0 is not merely a visual update.** The October work delivers new diagnostics and export options, substantially improves process/profile and settings workflows, and hardens the lifecycle of sessions, background services and benchmarking. The redesigned Desktop and Companion interfaces make those capabilities easier to use.

The sections below distinguish **changes made for 1.0.0** from **features already present in earlier releases**, so existing functionality is not misrepresented as new.

### What's improved or added in 1.0.0

**1. Settings and network diagnostics**

- New **read-only DNS diagnostics** in **Settings → Companion & Network**: inspect network adapters and their current DNS servers (automatic/DHCP or manually configured).
- Compare resolver latency for providers including Cloudflare, Google Public DNS, Quad9 and OpenDNS; the diagnostics cover IPv4, IPv6 and DoH where supported. View a command preview without executing it.
- **No automatic DNS switching** or modification of Windows network settings is implemented. A DNS-configuration/rollback feature remains future work.
- Settings are organized into six categories, with clearer companion/network controls, benchmark options and advanced diagnostics.

**2. Benchmark comparison and export**

- **Export two-session, same-game benchmark comparisons to CSV or Markdown**, including summary metrics, session context and advisory environment differences. Exports use stable, locale-independent values and UTC timestamps.
- Save dialogs are owned by the application window and report write failures instead of silently failing.
- The capture workflow explains why a benchmark cannot start (for example: missing target game, PresentMon service or permissions) rather than displaying a generic broken-engine state.
- Comparison, history and results received fixes for asynchronous refresh/race conditions. Capture and measurement math remain based on the established PresentMon-backed engine.

**3. Processes, profiles and CPU control**

- Reworked **Processes & CPU** browsing with search, sorting, status/rule information and detailed controls for CPU Sets, Processor Affinity and priority. The table stays virtualized for larger process lists.
- **Profiles & Rules** has a clearer create/validate/apply workflow, search and enable/disable controls, conflict warnings and explicit confirmation for deletion; profile toggle persistence was fixed.
- Process changes revalidate process identity, and protected Riot-related or benchmark-active operations are skipped where safety rules require it.
- Background refresh and comparison operations are serialized to reduce stale-state errors, cancellation races and disposed-object failures.

**4. Session lifecycle, shutdown and restart**

- A clearer manual Session Optimization flow shows configuration, preview, start and restore, with the Restore action kept accessible.
- Stability work improves UI-thread event dispatch, asynchronous cancellation, safe process resume, state recovery and shutdown with an explicit time budget rather than an unbounded hang.
- **Cooperative application restart** starts a verified successor process with approved startup arguments and reports success, timeout or refusal. Restart as administrator has corresponding feedback.
- Logging remains available across a normal restart handover.

**5. Hardware monitoring, logs and transparency**

- Hardware Monitor has explicit **OFF / ON / UNAVAILABLE** states and optional storage sensors via `EnableStorageSensors`; monitoring remains opt-in/on-demand.
- A **read-only Windows timer-resolution card** shows diagnostics. It does not silently tune the timer.
- The activity **Logs** view adds severity filters, search, a visible bounded result count and confirmation before clearing entries.
- Better status, permission, empty and error feedback makes it easier to see when an operation is unavailable or not applied.

**6. Library and Companion usability**

- Library scans are more discoverable, with launcher/source selection, search and filters, a ready-item count and informative states for missing executables.
- Companion now has a wide-screen web layout as well as a mobile app shell. Pairing, permission-aware actions, telemetry states, language changes and navigation received accessibility and reliability fixes.
- The desktop interface has grouped navigation, a more useful Dashboard, clearer module workspaces and consistent Polish/English localization.

**7. Identity, packaging and regression coverage**

- The new Frame Core graphite/amber identity is consistent across the Windows app, taskbar/tray, installer and Companion.
- `version.txt` is the single version source for the Windows app, Companion/API, benchmark metadata and installer.
- Extended unit/regression coverage tests the above behavior, including restart, DNS diagnostics, exports, session/process races, localization and the installer upgrade identity.

### Core functionality carried forward from earlier releases

These are important parts of FrameHub 1.0.0, **not all newly introduced in this release**:

- Steam/Epic/custom game discovery, manual EXE entries, per-game profiles and Quick Start/Gaming Mode.
- Automatic/manual Session Optimization with temporary suspension and recovery of *selected* background processes.
- Saved process rules and CPU Sets / Affinity / priority adjustments, including active-game CPU assignment.
- Local frame-time benchmarking using Intel PresentMon Shared Service/API 2.5.1: FPS, 1%/0.1% Low, percentiles, graphs, history, same-game comparison and optional global capture hotkey.
- Secure LAN Companion pairing with supported remote library, telemetry, benchmark and optimization controls.
- Conservative Counter-Strike 2 configuration management with account-specific backups; restricted Riot Games discovery/launch without altering protected Riot/Vanguard processes.

### Installation and upgrading

1. Open [GitHub Releases](https://github.com/9Erza/FrameHub/releases). Once v1.0.0 has been published, choose its **Assets** and download **`FrameHub-Setup-1.0.0.exe`**, not the source-code ZIP.
2. Close FrameHub, run Setup on **Windows 10/11 x64**, and approve the administrator prompt. The single installer includes the pinned **Intel PresentMon v2.5.1** prerequisite and checks whether it can reuse an existing installation.
3. For a previous **Inno Setup** installation, run the new Setup **over the old version without uninstalling first**. The historical installer `AppId` remains unchanged (used in v0.4.0 and recovered from v0.3.1). The previous application directory is reused.
4. Backing up `%APPDATA%\FrameHub` before a major upgrade is recommended. Benchmark history is stored at `%LOCALAPPDATA%\FrameHub\Benchmarks`.

### Important limitations

- **No guaranteed FPS boost**: effects of scheduling or background-process changes depend on the game and system. FrameHub exposes the operations rather than promising universal performance gains.
- DNS diagnostic commands are **preview-only**; Windows DNS configuration is **not changed** by this feature. The timer-resolution card is also **read-only**.
- FrameHub does **not** inject DLLs into games, read/modify game memory, install its own kernel driver or bypass anti-cheat. Game-specific compatibility is not guaranteed.
- No in-app automatic installer download/execution, cloud account or automatic benchmark-data upload. Updates point to the official GitHub Release.
- PresentMon is a separate shared prerequisite and may remain installed after removing FrameHub. See [third-party notices](THIRD-PARTY-NOTICES.md).
- Report bugs via [GitHub Issues](https://github.com/9Erza/FrameHub/issues); security-sensitive reports follow [SECURITY.md](../SECURITY.md).

---

## Polski

### FrameHub 1.0.0: nowe funkcje, stabilność i przebudowany interfejs

FrameHub to otwartoźródłowa aplikacja Windows pozwalająca świadomie kontrolować gry, przydział CPU, procesy w tle i pomiary wydajności. **Wersja 1.0.0 nie jest tylko zmianą wyglądu.** Październikowe prace objęły nowe narzędzia diagnostyczne i eksport danych, ulepszone zarządzanie procesami i ustawieniami oraz poprawki niezawodności sesji, usług i benchmarków. Nowy interfejs Desktop i Companion pomaga wygodniej korzystać z tych funkcji.

Poniżej oddzielam **zmiany wprowadzone przy 1.0.0** od **możliwości, które FrameHub posiadał już wcześniej**.

### Nowości i usprawnienia w 1.0.0

**1. Ustawienia i diagnostyka sieci**

- Nowy panel **diagnostyki DNS tylko do odczytu** w **Ustawienia → Companion i sieć**: lista kart sieciowych oraz aktualnie używanych serwerów DNS (automatycznych/DHCP lub ręcznych).
- Porównanie opóźnień resolverów, m.in. Cloudflare, Google Public DNS, Quad9 i OpenDNS, z uwzględnieniem IPv4, IPv6 i DoH tam, gdzie jest obsługiwany. Dostępny jest podgląd poleceń bez ich wykonywania.
- **Nie ma automatycznej zmiany DNS** ani modyfikowania ustawień sieci Windows — funkcja zmiany i przywracania konfiguracji pozostaje planem.
- Ustawienia podzielono na sześć kategorii, w tym Companion/sieć, benchmarki i zaawansowaną diagnostykę.

**2. Benchmarki i eksport wyników**

- **Eksport porównania dwóch benchmarków tej samej gry do CSV lub Markdown**, z metrykami, informacjami o sesjach i różnicami w środowisku testowym. Format liczb jest niezależny od języka interfejsu, a daty używają UTC.
- Zapis pliku odbywa się przez okno powiązane z aplikacją, a nieudany eksport pokazuje błąd.
- Widok pomiarów informuje o konkretnej przyczynie niemożności startu (np. brak uruchomionej gry, PresentMon lub odpowiednich uprawnień), zamiast ogólnego komunikatu.
- Naprawiono problemy związane z odświeżaniem historii, porównywaniem i równoległymi operacjami. Sam silnik pomiarowy PresentMon i sposób obliczania wyników nie zostały zastąpione.

**3. Procesy, profile i kontrola CPU**

- Uporządkowany moduł **Procesy i CPU**: wyszukiwanie, sortowanie, statusy i powiązane reguły oraz szczegółowa kontrola CPU Sets, Processor Affinity i priorytetów. Zachowano wirtualizację listy.
- **Profile i reguły**: czytelniejsze tworzenie i sprawdzanie profili, wyszukiwanie, włączanie/wyłączanie, ostrzeżenia o konfliktach i potwierdzenie usunięcia. Poprawiono trwałość zapisu przełączonego profilu.
- Przed zmianą ustawień aplikacja ponownie weryfikuje tożsamość procesu i uwzględnia blokady dla chronionych procesów Riot oraz operacji kolidujących z aktywnym benchmarkiem.
- Poprawiono synchronizację odświeżania, anulowania operacji i porównań, aby ograniczyć błędy związane z równoległym wykonywaniem zadań.

**4. Optymalizacja sesji, zamykanie i restart**

- Czytelniejsza ręczna **Optymalizacja sesji**: konfiguracja → podgląd → start → przywracanie, ze stale dostępną akcją przywrócenia.
- Ulepszono obsługę zdarzeń między wątkami, anulowanie operacji, bezpieczne wznawianie procesów, odzyskiwanie stanu i zamykanie z określonym limitem czasu zamiast niekontrolowanego zawieszania.
- **Bezpieczny restart aplikacji** uruchamia zweryfikowany nowy proces z dozwolonymi parametrami i pokazuje wynik, przekroczenie czasu lub odmowę. Dotyczy to również informacji zwrotnej przy restarcie jako administrator.
- Poprawiono ciągłość zapisu logów podczas restartu.

**5. Monitor sprzętu i Dziennik**

- Monitor sprzętu pokazuje stany **WYŁ. / WŁ. / NIEDOSTĘPNY** i oferuje opcjonalne czujniki dysków przez `EnableStorageSensors`. Monitoring nadal działa na żądanie.
- Nowa karta pokazująca **rozdzielczość timera Windows** jest wyłącznie diagnostyczna — niczego automatycznie nie przestawia.
- **Dziennik zdarzeń** zyskał filtrowanie po poziomie ważności, wyszukiwanie, limit wyświetlanych wpisów i potwierdzenie czyszczenia.
- Bardziej czytelne komunikaty pokazują, dlaczego funkcja jest niedostępna, wymaga uprawnień albo nie została zastosowana.

**6. Biblioteka gier i Companion**

- Łatwiejsze skanowanie źródeł gier, filtrowanie i wyszukiwanie, licznik gotowych pozycji i informacje o brakujących plikach EXE.
- Companion oferuje układ na duże ekrany i mobilną powłokę aplikacji. Poprawiono parowanie, stany telemetrii, obsługę uprawnień, zmianę języka, nawigację i dostępność.
- Desktop otrzymał pogrupowaną nawigację, praktyczniejszy Panel główny, czytelniejsze widoki modułów i spójną lokalizację PL/EN.

**7. Wygląd, wydanie i testy**

- Spójny styl Frame Core w kolorach grafitowo-bursztynowych w aplikacji, zasobniku, instalatorze i Companion.
- Jedno źródło wersji `version.txt` dla aplikacji Windows, Companion/API, metadanych benchmarków i instalatora.
- Rozszerzono testy regresyjne obejmujące restart, DNS, eksporty, współbieżność operacji, lokalizację i zgodność identyfikatora instalacji.

### Funkcje dostępne już we wcześniejszych wersjach

Te możliwości **są częścią FrameHub 1.0.0, ale nie wszystkie zostały dodane dopiero teraz**:

- Wykrywanie gier Steam/Epic i własnych folderów, dodawanie EXE, profile dla gier i Szybki start.
- Automatyczna/ręczna Optymalizacja sesji z tymczasowym wstrzymywaniem *wybranych* aplikacji w tle i ich przywracaniem.
- Zapisane reguły procesów oraz CPU Sets, Affinity i priorytety, w tym przydział CPU aktywnej grze.
- Lokalne benchmarki Intel PresentMon Shared Service/API 2.5.1: FPS, 1%/0,1% Low, percentyle, wykresy, historia, porównywanie i opcjonalny globalny skrót start/stop.
- Bezpieczne parowanie Companion w sieci LAN i obsługiwane zdalne funkcje biblioteki, telemetrii, benchmarków i optymalizacji.
- Ostrożna edycja konfiguracji CS2 z kopiami zapasowymi dla kont Steam oraz ograniczona obsługa Riot Games bez ingerencji w chronione procesy Riot/Vanguard.

### Instalacja i aktualizacja

1. Wejdź na [GitHub Releases](https://github.com/9Erza/FrameHub/releases). Gdy v1.0.0 zostanie opublikowane, pobierz w sekcji **Assets** plik **`FrameHub-Setup-1.0.0.exe`** — nie automatyczne archiwum ZIP z kodem.
2. Zamknij FrameHub, uruchom instalator w **Windows 10/11 x64** i zaakceptuj uprawnienia administratora. Jeden instalator zawiera **Intel PresentMon v2.5.1** i sprawdza możliwość wykorzystania istniejącej instalacji tej zależności.
3. Starszą instalację wykonaną przez **Inno Setup** można zaktualizować **bez wcześniejszego odinstalowania**. Zachowano historyczny identyfikator instalatora `AppId` (używany w 0.4.0 i odzyskany wcześniej z 0.3.1) i poprzednią lokalizację aplikacji.
4. Przed większą aktualizacją warto skopiować `%APPDATA%\FrameHub`. Historia benchmarków znajduje się w `%LOCALAPPDATA%\FrameHub\Benchmarks`.

### Ważne ograniczenia

- **Brak obietnic uniwersalnego wzrostu FPS** — wpływ zarządzania CPU i procesami zależy od gry i komputera.
- Diagnostyka DNS i podgląd poleceń są **tylko do odczytu**; aplikacja **nie zmienia DNS**. Karta timera Windows także nie zmienia ustawień.
- FrameHub **nie wstrzykuje DLL do gier, nie odczytuje ani nie modyfikuje ich pamięci, nie instaluje własnego sterownika kernel ani nie obchodzi anti-cheat**. Nie gwarantujemy zgodności z każdą grą.
- Brak automatycznego pobierania/uruchamiania instalatorów i automatycznego wysyłania benchmarków do chmury. Sprawdzanie aktualizacji kieruje do GitHub Release.
- PresentMon jest osobnym współdzielonym składnikiem i może pozostać w systemie po usunięciu FrameHub. Licencje: [third-party notices](THIRD-PARTY-NOTICES.md).
- Błędy zgłaszaj przez [GitHub Issues](https://github.com/9Erza/FrameHub/issues), a kwestie bezpieczeństwa zgodnie z [SECURITY.md](../SECURITY.md).
