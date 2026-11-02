# FrameHub v1.0.1 — Release notes / Informacje o wydaniu

**Windows 10/11 · x64 · .NET 10 · MIT · 2 November / 2 listopada 2026**

[English](#english) · [Polski](#polski) · [Complete changelog (EN)](../CHANGELOG.md) · [Lista zmian (PL)](../CHANGELOG.pl.md) · [Previous release notes v1.0.0](RELEASE_NOTES_1.0.0.md)

> The official installer will appear as **`FrameHub-Setup-1.0.1.exe`** under **Assets** on [GitHub Releases](https://github.com/9Erza/FrameHub/releases) after publication. GitHub-generated source ZIP/TAR files are not installers. This document is the bilingual source for the GitHub Release description.

---

## English

### FrameHub 1.0.1: first-run polish and honest update notifications

1.0.1 is a small patch on top of 1.0.0. There are no new modules, no new background work and no design-system changes: the existing graphite/brass interface, opt-in hardware/Companion/Live FPS policy and safety boundaries are unchanged. This release fixes confirmed first-run and update-check defects found during the 1.0.1 audit.

### What's fixed or improved in 1.0.1

**1. First-run setup wizard**

- Initial keyboard focus lands on the visible primary action (Next on steps 1–5, Finish on the summary) instead of a hidden button.
- Enter activates Finish on the summary step; Esc still activates Skip.
- The summary always reflects choices made on earlier steps, including after Back/Next navigation and language switches.
- Disabling Companion in the wizard coerces Live FPS off, so the wizard can never persist the contradictory "Live FPS on, Companion off" draft.

**2. Update notifications**

- The desktop update check now verifies that the matching GitHub Release tag (for example `v1.0.1`) is actually published — non-draft, non-prerelease — before notifying.
- A newer `version.txt` without a published release never presents an update dialog or points at a page without an installer.
- If the release verification itself is inconclusive (network failure), the automatic check stays silent and the manual "Check now" reports an honest failure instead of a false update.

**3. Settings and Companion consistency**

- Settings → Application data & reset gives the first-run setup block its own heading instead of repeating the button label (EN/PL).
- Companion web UI static version placeholders and versioned asset cache-busting declare 1.0.1; the running version is still served live from `/api/v1/status`.

### Upgrade notes

- Run `FrameHub-Setup-1.0.1.exe` over the existing installation; do not uninstall first. The installer keeps the historical `AppId` (`A073F5F3-E7D0-45C7-A233-8A98B033385B`) and reuses the previous directory.
- Settings, profiles, library, benchmark history and paired devices are preserved. The first-run wizard does not reappear for existing installations.
- PresentMon handling is unchanged: the checked-by-default optional component is reused when the verified v2.5.1 shared service/API is present and installed silently otherwise; deselecting it installs nothing.
- Close the running FrameHub before upgrading; the installer may ask to close it.

---

## Polski

### FrameHub 1.0.1: dopracowany kreator i uczciwe powiadomienia o aktualizacji

1.0.1 to mała poprawka na bazie 1.0.0. Brak nowych modułów, nowych zadań w tle i zmian design systemu: dotychczasowy interfejs, polityka opt-in sprzętu/Companion/Live FPS oraz granice bezpieczeństwa są bez zmian. Wydanie naprawia potwierdzone usterki kreatora i sprawdzania aktualizacji wykryte w audycie 1.0.1.

### Co poprawiono w 1.0.1

**1. Kreator pierwszego uruchomienia**

- Początkowy fokus klawiatury trafia w widoczną akcję (Dalej na krokach 1–5, Zakończ na podsumowaniu), a nie w ukryty przycisk.
- Enter aktywuje Zakończ na kroku podsumowania; Esc nadal aktywuje Pomiń.
- Podsumowanie zawsze odzwierciedla wybory z wcześniejszych kroków, także po nawigacji Wstecz/Dalej i zmianie języka.
- Wyłączenie Companion w kreatorze wyłącza też Live FPS, więc kreator nie zapisze sprzecznego szkicu „Live FPS włączony, Companion wyłączony”.

**2. Powiadomienia o aktualizacji**

- Sprawdzanie aktualizacji potwierdza, że pasujący tag wydania GitHub (np. `v1.0.1`) jest rzeczywiście opublikowany — nie jest szkicem ani wersją przedpremierową — zanim pokaże powiadomienie.
- Nowszy `version.txt` bez opublikowanego wydania nigdy nie pokazuje dialogu ani nie kieruje do strony bez instalatora.
- Gdy sama weryfikacja wydania jest niejednoznaczna (awaria sieci), automatyczne sprawdzenie milczy, a ręczne „Sprawdź teraz” zgłasza uczciwy błąd zamiast fałszywej aktualizacji.

**3. Spójność Ustawień i Companion**

- Ustawienia → Dane aplikacji i reset mają własny nagłówek bloku kreatora zamiast powtarzania etykiety przycisku (EN/PL).
- Statyczne znaczniki wersji Companion web UI i wersjonowane cache-busting zasobów deklarują 1.0.1; działająca wersja nadal pochodzi na żywo z `/api/v1/status`.

### Uwagi o aktualizacji

- Uruchom `FrameHub-Setup-1.0.1.exe` na istniejącej instalacji; nie odinstalowuj wcześniej. Instalator zachowuje historyczny `AppId` (`A073F5F3-E7D0-45C7-A233-8A98B033385B`) i wykorzystuje poprzedni katalog.
- Ustawienia, profile, biblioteka, historia benchmarków i sparowane urządzenia są zachowane. Kreator pierwszego uruchomienia nie pojawia się ponownie w istniejących instalacjach.
- Obsługa PresentMon bez zmian: domyślnie zaznaczony komponent opcjonalny jest wykorzystywany, gdy zweryfikowana współdzielona usługa/API v2.5.1 istnieje, a w przeciwnym razie instalowany po cichu; odznaczenie go nic nie instaluje.
- Przed aktualizacją zamknij działający FrameHub; instalator może poprosić o jego zamknięcie.
