# FrameHub v1.0.0 — Release notes / Informacje o wydaniu

**Windows 10/11 · x64 · .NET 10 · MIT · 28 October / 28 października 2026**

[English](#english) · [Polski](#polski) · [Full changelog (EN)](../CHANGELOG.md) · [Changelog (PL)](../CHANGELOG.pl.md)

> The official Windows asset is **`FrameHub-Setup-1.0.0.exe`** on the [GitHub Releases page](https://github.com/9Erza/FrameHub/releases) once attached. GitHub-generated "Source code" ZIP/TAR archives are **not** the installer. This document is also the bilingual text to use for the v1.0.0 GitHub Release.

---

## English

### FrameHub 1.0.0 is ready

FrameHub is a free, open-source Windows gaming and performance utility built around user-controlled, reversible changes. Version 1.0.0 introduces a cohesive desktop and Companion visual identity and a substantially refined experience, while retaining the existing optimization and benchmarking tools.

### Highlights

- **Overhauled Windows Desktop UI** — grouped navigation, new actionable Dashboard, improved Game Library, guided Session Optimization and Benchmarks, clearer Processes/Profiles/Hardware/Settings surfaces, and improved English/Polish localization.
- **New Frame Core branding** — a consistent graphite-and-amber identity across the EXE, app window, taskbar, tray, installer, shortcuts and Companion web UI.
- **Companion on mobile and desktop** — wide-screen navigation and multi-column views, plus a mobile app shell with fixed navigation, safe-area handling, secure LAN pairing and accessible controls.
- **Single version across the product** — Windows app, Companion, About pages, API status, benchmark metadata and installer all report v1.0.0 from the same version source.
- **More resilient day-to-day UX** — improved empty/error states, pairing messages, dynamic language changes, focus/keyboard support, reduced-motion behavior and responsive spacing.

### What you can do

- Discover Steam/Epic games, add your own EXE files and configure per-game CPU profiles.
- Use the profile watcher and CPU Sets / Processor Affinity / priority controls for processes.
- Temporarily suspend *selected* background apps during gaming, then restore them after a session.
- Collect local per-frame benchmarks via Intel PresentMon, review FPS / 1% Low / 0.1% Low / frame times, compare sessions and view environment metadata.
- Monitor hardware when enabled and use the LAN Companion UI for pairing, telemetry and supported remote controls.
- Work with Counter-Strike 2 settings using safety checks and backups.

### Install or upgrade

1. Open [GitHub Releases](https://github.com/9Erza/FrameHub/releases), choose **v1.0.0**, expand **Assets**, and download **`FrameHub-Setup-1.0.0.exe`** once it is available.
2. Close FrameHub, run Setup and approve the administrator prompt. The installer includes the pinned **Intel PresentMon 2.5.1** prerequisite for benchmarking.
3. To upgrade an existing Inno Setup installation, **install over the previous version; do not uninstall first**. The long-standing FrameHub installer `AppId` is unchanged (used since v0.4.0, originally recovered from v0.3.1). Inno Setup reuses the existing install directory; user settings and benchmark captures live in separate per-user directories.
4. For extra safety, back up `%APPDATA%\FrameHub` before a major upgrade. Benchmark history lives at `%LOCALAPPDATA%\FrameHub\Benchmarks`.

### Important notes

- This is a **Windows x64 desktop application**, not a cloud service. No account or automatic upload of benchmark data is required.
- The application **does not automatically download or run** update installers. It only points to the official GitHub release.
- FrameHub does **not** inject into games, read or modify game memory, install its own kernel driver or bypass anti-cheat. Compatibility with every title or anti-cheat is not guaranteed.
- Intel PresentMon is a shared prerequisite and may remain installed after FrameHub is uninstalled. It is distributed under its own license; see the [third-party notices](THIRD-PARTY-NOTICES.md).
- Report problems through [GitHub Issues](https://github.com/9Erza/FrameHub/issues). Security-sensitive reports should follow [SECURITY.md](../SECURITY.md).

---

## Polski

### FrameHub 1.0.0 — gotowy do wydania

FrameHub to darmowe, otwartoźródłowe narzędzie do kontroli wydajności i optymalizacji gier w Windows. Zmiany są świadome, kontrolowane przez użytkownika i w miarę możliwości odwracalne. Wersja 1.0.0 wprowadza spójny wygląd aplikacji i Companiona oraz znacząco dopracowany interfejs, zachowując dostępne wcześniej moduły optymalizacji i benchmarków.

### Najważniejsze nowości

- **Przebudowany interfejs Desktop** — pogrupowana nawigacja, nowy Panel główny z przydatnymi akcjami, poprawiona biblioteka gier, czytelniejsza Optymalizacja sesji i Benchmarki, dopracowane Procesy/Profile/Monitor/Ustawienia oraz lokalizacja PL/EN.
- **Nowa identyfikacja Frame Core** — spójny motyw grafitowo-bursztynowy w pliku EXE, oknie programu, pasku zadań, zasobniku, instalatorze, skrótach i Companion.
- **Companion na telefonie i komputerze** — boczna nawigacja i układ wielokolumnowy na dużych ekranach, a na telefonach stała nawigacja, obsługa safe-area, bezpieczne parowanie w LAN i wygodne sterowanie.
- **Jedna wersja produktu** — program Windows, Companion, informacje o aplikacji, API, metadane benchmarków i instalator korzystają ze wspólnej wersji v1.0.0.
- **Poprawione działanie na co dzień** — czytelniejsze komunikaty, błędy i stany puste, dopracowane parowanie, zmiana języka, dostępność klawiaturą i układ na małych ekranach.

### Co potrafi aplikacja

- Wyszukuje gry Steam/Epic, pozwala dodać własne pliki EXE i przypisać im profile CPU.
- Obsługuje monitor profili, CPU Sets, Processor Affinity i priorytety procesów.
- Tymczasowo wstrzymuje *wybrane* aplikacje w tle podczas grania i przywraca je po sesji.
- Wykonuje lokalne benchmarki z Intel PresentMon, pokazuje FPS, 1% Low, 0.1% Low, czasy klatek i porównania wraz z informacjami o środowisku.
- Udostępnia opcjonalny monitoring sprzętu i Companion w sieci lokalnej.
- Umożliwia bezpieczną pracę z konfiguracją Counter-Strike 2 i kopiami zapasowymi.

### Instalacja i aktualizacja

1. Wejdź w [GitHub Releases](https://github.com/9Erza/FrameHub/releases), otwórz **v1.0.0**, rozwiń **Assets** i pobierz **`FrameHub-Setup-1.0.0.exe`**, gdy plik zostanie opublikowany.
2. Zamknij FrameHub, uruchom instalator i zaakceptuj uprawnienia administratora. Instalator zawiera wymagany do benchmarków **Intel PresentMon 2.5.1**.
3. Aby zaktualizować starszą instalację Inno Setup, **uruchom nowy instalator na starej wersji — nie odinstalowuj jej wcześniej**. Identyfikator instalatora `AppId` nie zmienił się od v0.4.0 (pierwotnie odzyskany z v0.3.1). Poprzedni folder instalacji zostaje wykorzystany ponownie; ustawienia i benchmarki znajdują się w osobnych katalogach użytkownika.
4. Dla bezpieczeństwa warto skopiować `%APPDATA%\FrameHub` przed większą aktualizacją. Historia benchmarków znajduje się w `%LOCALAPPDATA%\FrameHub\Benchmarks`.

### Ważne informacje

- Jest to **aplikacja desktopowa Windows x64**, a nie usługa chmurowa. Nie wymaga konta ani automatycznego wysyłania wyników benchmarków.
- Program **nie pobiera i nie uruchamia samodzielnie instalatorów**; jedynie wskazuje oficjalne wydanie na GitHubie.
- FrameHub **nie ingeruje w pamięć gier, nie wstrzykuje DLL, nie instaluje własnego sterownika kernel ani nie obchodzi anti-cheat**. Nie można zagwarantować zgodności z każdą grą.
- Intel PresentMon jest współdzieloną zależnością i może pozostać w systemie po odinstalowaniu FrameHub. Informacje licencyjne: [Third-party notices](THIRD-PARTY-NOTICES.md).
- Błędy zgłaszaj przez [GitHub Issues](https://github.com/9Erza/FrameHub/issues), a kwestie bezpieczeństwa zgodnie z [SECURITY.md](../SECURITY.md).
