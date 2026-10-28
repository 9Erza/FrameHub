# Lista zmian / Changelog (PL)

[English changelog — pełna historia wydań](CHANGELOG.md) · [Opis wydania v1.0.0 (PL/EN)](docs/RELEASE_NOTES_1.0.0.md)

## [Niewydane]

## [1.0.0] — 2026-10-28

Wydanie dla Windows 10/11 x64: `FrameHub-Setup-1.0.0.exe` będzie dostępne w sekcji **Assets** na [GitHub Releases](https://github.com/9Erza/FrameHub/releases) po jego opublikowaniu.

### Najważniejsze zmiany

- **Gruntowny redesign aplikacji Desktop (Ember Signal)**: nawigacja modułów, Panel główny, biblioteka gier, czteroetapowa Optymalizacja sesji, benchmarki, procesy, profile, monitoring i ustawienia.
- **Nowy wygląd Frame Core**: grafitowo-bursztynowy styl i spójne logo w aplikacji Windows, instalatorze, skrótach oraz Companion.
- **Przeprojektowany Companion**: układ desktopowy dla szerokich ekranów, dopracowany interfejs mobilny ze stałą nawigacją, obsługa safe-area oraz dostępność klawiaturą.
- **Spójna wersja 1.0.0**: Windows, Companion, API, dane benchmarków i instalator odczytują wersję z jednego źródła.
- Zachowane moduły: wykrywanie gier, profile i reguły CPU, bezpieczne wstrzymywanie procesów w sesji, przydział CPU aktywnej gry, CS2, lokalne benchmarki i opcjonalny monitoring.

### Usprawnienia i poprawki

- Bardziej czytelne komunikaty o błędach, stanie modułów oraz braku wyników.
- Poprawione komunikaty parowania Companion w obu językach, synchronizacja treści po zmianie języka i prezentacja jednostek RAM/VRAM na telefonach.
- Lepsze zachowanie na małych ekranach, obsługa klawiatury i wsparcie reduced-motion.
- Poprawne informacje wersji w pliku EXE i metadanych benchmarków.
- Korekty lokalizacji PL/EN po zmianie języka interfejsu.

### Instalacja i aktualizacja

- Jeden instalator Windows x64 obejmuje wymagany Intel PresentMon Shared Service/API 2.5.1.
- **Identyfikator `AppId` został zachowany**: instalator aktualizuje istniejącą instalację z tej samej rodziny Inno Setup, bez potrzeby ręcznego odinstalowania starej wersji.
- Ustawienia, profile i biblioteka pozostają w `%APPDATA%\FrameHub`, a benchmarki w `%LOCALAPPDATA%\FrameHub\Benchmarks`. Przed większą aktualizacją warto zrobić kopię ustawień.
- Program sprawdza aktualizacje i kieruje na GitHub; nie pobiera ani nie uruchamia instalatora automatycznie.
- Pliki „Source code ZIP/TAR” dodawane automatycznie przez GitHub nie są instalatorem Windows.

### Ograniczenia

- FrameHub nie omija zabezpieczeń anti-cheat i nie gwarantuje zgodności z każdą grą.
- Dane benchmarków są lokalne; program nie wymaga chmury ani konta.
- Intel PresentMon może pozostać zainstalowany jako współdzielona zależność po usunięciu FrameHub.

Starsze wpisy historii (0.7.1, 0.7.0, 0.6.0 i 0.5.0) są zachowane w [oryginalnym angielskim changelogu](CHANGELOG.md).
