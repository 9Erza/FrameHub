# FrameHub v1.0.1 — Release notes / Informacje o wydaniu

**Windows 10/11 · x64 · .NET 10 · MIT · 2 November / 2 listopada 2026**

[English](#english) · [Polski](#polski) · [Complete changelog (EN)](../CHANGELOG.md) · [Lista zmian (PL)](../CHANGELOG.pl.md) · [Previous release notes v1.0.0](RELEASE_NOTES_1.0.0.md)

> The official installer is **`FrameHub-Setup-1.0.1.exe`** under **Assets** on [GitHub Releases](https://github.com/9Erza/FrameHub/releases). GitHub-generated source ZIP/TAR files are not installers. This document is the bilingual source for the GitHub Release description.

---

## English

### FrameHub 1.0.1: guided first run, explicit optional modules and honest updates

1.0.1 collects everything added after the published 1.0.0 installer was prepared: a guided six-step first-run setup, an explicit Live FPS opt-in on top of Companion, an Application data & reset workspace, an optional PresentMon install step, plus window-management, stability, startup and memory-hardening fixes. There is no interface redesign and no new background work: the existing graphite/brass workspace, the safety boundaries (process identity revalidation, Riot protection, benchmark arbitration, Companion scopes) and the benchmark engine are unchanged.

### What's new in 1.0.1

**1. First-run setup wizard**

- A six-step wizard (welcome/language, Hardware monitoring, Companion, Live FPS, Start with Windows, summary) appears on fresh installs and after an interrupted first run. It works on a draft that is persisted only on Finish/Skip, so cancelling can never corrupt settings.
- Optional modules default OFF for a light footprint; core process/CPU control and the profile watcher stay always on and are not toggleable here.
- Existing installations (upgrades) never see the wizard: a pre-existing settings file is stamped completed silently with every setting preserved. The wizard can be reopened any time from **Settings → Application data & reset**.

**2. Live FPS monitoring as an explicit opt-in**

- **Settings → Companion & Network → Live FPS monitoring** (and the matching wizard step) controls the continuous PresentMon frame-rate/frametime preview. It defaults OFF.
- The native PresentMon loop runs only while Companion is enabled **and** Live FPS is enabled; one-demand benchmark capture stays available regardless of this switch. Disabling Companion while Live FPS is on coerces Live FPS off so the configuration can never contradict itself.

**3. Application data & reset**

- **Settings → Application data & reset** (new, seventh settings category) restores default settings without touching the library, profiles, benchmark history or paired devices.
- Full erase types the `RESET` confirmation, then deletes only FrameHub-owned data (settings, profiles, library, session state, benchmark history, logs, CS2 config backups, pairings), refuses to run during benchmark/session/recovery activity, and restarts into the first-run wizard. A leftover Windows startup entry warns but never fails the wipe and never blocks the restart.

**4. Optional PresentMon install step**

- The installer offers **Intel PresentMon 2.5.1** as a checked-by-default optional component. Deselecting it installs nothing, repairs nothing and never starts `msiexec` for PresentMon; an already installed PresentMon is left untouched and may still be used.
- A verified v2.5.1 shared service/API is reused; otherwise the embedded MSI installs silently. A newer or unverified shared installation is never downgraded automatically, and PresentMon is never removed with FrameHub.

**5. Honest update notifications**

- The desktop update check now notifies only when the matching GitHub Release tag (for example `v1.0.1`) is actually published — non-draft, non-prerelease — **and** carries the expected `FrameHub-Setup-X.Y.Z.exe` installer asset. A newer `version.txt` without a ready release never presents a dialog.
- If verification itself fails (network error, timeout, API error), the automatic check stays silent and manual **Check now** reports the failure honestly. No installers are ever downloaded or executed automatically.

### Fixed in 1.0.1

- First-run wizard keyboard flow: initial focus lands on the visible primary action (Next, then Finish), Enter activates Finish on the summary, the summary always reflects earlier choices, and Esc/Skip applies the light configuration.
- Window management (W1): maximized windows are clamped to the monitor work area via `WM_GETMINMAXINFO`; game lists are bounded and virtualized; the Library scan menu, inspector, session workflow guide and filter preservation were reworked.
- Desktop usability and localization refinements (W2), including Companion & Network wording and Benchmark hotkey recording feedback.
- Full-wipe status split: a leftover startup entry warns without failing data deletion and without blocking the restart into the wizard.
- View-model retention and unbounded caches: Library/Session/Benchmark icon decoding now shares one bounded (96-entry LRU) cache, Companion icon blob-URLs are capped at 200 with revocation, and page view-models detach event handlers on disposal.
- Startup registration reconciliation: Registry vs elevated-scheduled-task vs conflict/broken states are evaluated against the real system state, with explicit UAC-cancellation feedback.

### Installation and upgrading (1.0.0 → 1.0.1)

1. Open [GitHub Releases](https://github.com/9Erza/FrameHub/releases). Choose the **v1.0.1 Assets** and download **`FrameHub-Setup-1.0.1.exe`**, not a source-code ZIP.
2. Close FrameHub and run Setup on **Windows 10/11 x64**, approving the administrator prompt. Requirements are unchanged from 1.0.0.
3. For an existing 1.0.0 Inno Setup installation, run the new Setup **over the old version without uninstalling first**. The historical installer `AppId` (`A073F5F3-E7D0-45C7-A233-8A98B033385B`) and the previous directory are reused.
4. Settings, profiles, library, benchmark history and paired devices are preserved; the first-run wizard does not reappear. The PresentMon step defaults to selected on upgrade (1.0.0 had no such choice, so there is nothing to remember from it); the choice is pre-selected from the previous install on later upgrades.
5. FrameHub itself installs no Windows service or driver: the only service involved is the shared Intel `PresentMonSharedService` when the optional component is used. Without PresentMon the application runs normally, while FPS preview and benchmark capture report the missing dependency with its cause. PresentMon remains installed if FrameHub is uninstalled.

### Known limitations

- Effects of scheduling or background-process changes depend on the game and system; FrameHub exposes the operations rather than promising universal FPS gains.
- DNS diagnostics and the Windows timer-resolution card are read-only; Windows network settings are never changed.
- Benchmark analysis v1 does not segment scenes, warm-up, pauses, loading screens or frame types.
- The in-app update check opens the official release page; it never downloads or runs installers.
- FrameHub does not inject DLLs into games, read/modify game memory, install its own kernel driver or bypass anti-cheat; per-game compatibility is not guaranteed.
- PresentMon is a shared prerequisite and may remain installed after FrameHub is removed. See [third-party notices](THIRD-PARTY-NOTICES.md).

---

## Polski

### FrameHub 1.0.1: kreator startowy, jawne moduły opcjonalne i uczciwe aktualizacje

1.0.1 zbiera wszystko, co dodano po przygotowaniu opublikowanego instalatora 1.0.0: sześciokrokowy kreator pierwszego uruchomienia, jawny przełącznik Live FPS na bazie Companion, sekcję danych aplikacji i resetu, opcjonalny krok instalacji PresentMon oraz poprawki okien, stabilności, autostartu i pamięci. Brak redesignu interfejsu i nowych zadań w tle: dotychczasowy obszar roboczy, granice bezpieczeństwa (ponowna weryfikacja tożsamości procesów, ochrona Riot, arbitraż benchmarków, zakresy Companion) oraz silnik benchmarków są bez zmian.

### Co nowego w 1.0.1

**1. Kreator pierwszego uruchomienia**

- Sześć kroków (powitanie/język, monitor sprzętu, Companion, Live FPS, start z Windows, podsumowanie) pojawia się przy świeżej instalacji i po przerwanym pierwszym uruchomieniu. Kreator pracuje na szkicu zapisywanym dopiero przy Zakończ/Pomiń, więc anulowanie nie uszkodzi ustawień.
- Moduły opcjonalne są domyślnie WYŁĄCZONE (lekki ślad); bazowe sterowanie procesami/CPU i monitor profili są zawsze włączone i nie ma ich tu do przełączenia.
- Istniejące instalacje (aktualizacje) nigdy nie widzą kreatora: wcześniejszy plik ustawień jest po cichu oznaczany jako ukończony z zachowaniem wszystkich ustawień. Kreator można otworzyć w każdej chwili z **Ustawienia → Dane aplikacji i reset**.

**2. Live FPS jako jawna zgoda**

- **Ustawienia → Companion i sieć → monitorowanie Live FPS** (i odpowiedni krok kreatora) steruje ciągłym podglądem FPS czasu klatek z PresentMon. Domyślnie WYŁĄCZONE.
- Natywna pętla PresentMon działa tylko, gdy Companion jest włączony **i** Live FPS jest włączony; jednorazowe benchmarki działają niezależnie. Wyłączenie Companion przy włączonym Live FPS wyłącza też Live FPS, więc konfiguracja nie bywa sprzeczna.

**3. Dane aplikacji i reset**

- **Ustawienia → Dane aplikacji i reset** (nowa, siódma kategoria ustawień) przywraca domyślne ustawienia bez dotykania biblioteki, profili, historii benchmarków i sparowanych urządzeń.
- Pełne czyszczenie wymaga wpisania `RESET`, usuwa wyłącznie dane FrameHub (ustawienia, profile, biblioteka, stan sesji, historia benchmarków, logi, kopie CS2, parowania), odmawia działania podczas benchmarku/sesji/odzyskiwania i restartuje do kreatora. Pozostały wpis autostartu Windows ostrzega, ale nie psuje czyszczenia i nie blokuje restartu.

**4. Opcjonalna instalacja PresentMon**

- Instalator oferuje **Intel PresentMon 2.5.1** jako domyślnie zaznaczony komponent opcjonalny. Odznaczenie nic nie instaluje, niczego nie naprawia i nie uruchamia `msiexec` dla PresentMon; istniejący PresentMon zostaje nietknięty i nadal może być używany.
- Zweryfikowana współdzielona usługa/API v2.5.1 jest wykorzystywana ponownie; w przeciwnym razie osadzony MSI instaluje się po cichu. Nowsza lub niezweryfikowana współdzielona instalacja nigdy nie jest automatycznie obniżana, a PresentMon nie jest usuwany razem z FrameHub.

**5. Uczciwe powiadomienia o aktualizacji**

- Sprawdzanie aktualizacji powiadamia tylko, gdy pasujący tag wydania GitHub (np. `v1.0.1`) jest rzeczywiście opublikowany — nie jest szkicem ani wersją przedpremierową — **i** zawiera oczekiwany asset `FrameHub-Setup-X.Y.Z.exe`. Nowszy `version.txt` bez gotowego wydania nigdy nie pokazuje dialogu.
- Gdy sama weryfikacja się nie powiedzie (błąd sieci, timeout, błąd API), automatyczne sprawdzenie milczy, a ręczne **Sprawdź teraz** zgłasza błąd uczciwie. Instalatory nigdy nie są pobierane ani uruchamiane automatycznie.

### Poprawki w 1.0.1

- Obsługa klawiatury w kreatorze: początkowy fokus na widocznej akcji (Dalej, potem Zakończ), Enter aktywuje Zakończ na podsumowaniu, podsumowanie zawsze odzwierciedla wcześniejsze wybory, a Esc/Pomiń stosuje lekką konfigurację.
- Zarządzanie oknami (W1): zmaksymalizowane okna ograniczone do obszaru roboczego monitora przez `WM_GETMINMAXINFO`; listy gier ograniczone i wirtualizowane; przebudowano menu skanowania biblioteki, inspektora, przewodnik sesji i zachowanie filtrów.
- Dopracowanie interfejsu i lokalizacji (W2), w tym nazewnictwo Companion i sieci oraz nagrywanie skrótu benchmarku.
- Podział statusu pełnego czyszczenia: pozostały wpis autostartu ostrzega, ale nie psuje usuwania danych i nie blokuje restartu do kreatora.
- Przecieki view-modeli i nieograniczone cache: dekodowanie ikon Biblioteki/Sesji/Benchmarków korzysta ze współdzielonego ograniczonego cache (96 wpisów LRU), blob-URL ikon Companion ograniczone do 200 ze zwalnianiem, a view-modele stron odpinają zdarzenia przy usuwaniu.
- Uzgadnianie rejestracji autostartu: stany Rejestr / zadanie z podwyższeniem / konflikt / uszkodzony są oceniane względem rzeczywistego stanu systemu, z jawną informacją o anulowaniu UAC.

### Instalacja i aktualizacja (1.0.0 → 1.0.1)

1. Wejdź na [GitHub Releases](https://github.com/9Erza/FrameHub/releases). W **Assets** wydania **v1.0.1** pobierz **`FrameHub-Setup-1.0.1.exe`** — nie archiwum ZIP z kodem.
2. Zamknij FrameHub i uruchom instalator w **Windows 10/11 x64**, akceptując uprawnienia administratora. Wymagania są bez zmian względem 1.0.0.
3. Istniejącą instalację 1.0.0 wykonaną przez Inno Setup zaktualizuj **bez wcześniejszego odinstalowania**. Historyczny `AppId` instalatora (`A073F5F3-E7D0-45C7-A233-8A98B033385B`) i poprzedni katalog są wykorzystywane ponownie.
4. Ustawienia, profile, biblioteka, historia benchmarków i sparowane urządzenia są zachowane; kreator pierwszego uruchomienia nie pojawia się ponownie. Krok PresentMon jest domyślnie zaznaczony przy aktualizacji (1.0.0 nie miało takiego wyboru, więc nie ma czego pamiętać); przy kolejnych aktualizacjach wybór jest wstępnie zaznaczany z poprzedniej instalacji.
5. Sam FrameHub nie instaluje usługi ani sterownika Windows: jedyną usługą jest współdzielona `PresentMonSharedService` firmy Intel, gdy używany jest komponent opcjonalny. Bez PresentMon aplikacja działa normalnie, a podgląd FPS i benchmarki zgłaszają brak zależności z podaniem przyczyny. PresentMon pozostaje w systemie po odinstalowaniu FrameHub.

### Znane ograniczenia

- Wpływ zarządzania CPU i procesami zależy od gry i komputera; FrameHub udostępnia operacje, a nie obiecuje uniwersalnego wzrostu FPS.
- Diagnostyka DNS i karta rozdzielczości timera Windows są tylko do odczytu; ustawienia sieci Windows nie są zmieniane.
- Analiza benchmarków v1 nie segmentuje scen, rozgrzewki, pauz, ekranów ładowania ani typów klatek.
- Sprawdzanie aktualizacji otwiera oficjalną stronę wydania; instalatory nie są pobierane ani uruchamiane.
- FrameHub nie wstrzykuje DLL do gier, nie odczytuje ani nie modyfikuje ich pamięci, nie instaluje własnego sterownika kernel ani nie obchodzi anti-cheat; zgodność z konkretną grą nie jest gwarantowana.
- PresentMon jest współdzielonym składnikiem i może pozostać w systemie po usunięciu FrameHub. Licencje: [third-party notices](THIRD-PARTY-NOTICES.md).
