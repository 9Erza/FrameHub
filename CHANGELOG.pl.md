# Lista zmian / Changelog (PL)

[English changelog — pełna historia wydań](CHANGELOG.md) · [Opis wydania v1.0.1 (PL/EN)](docs/RELEASE_NOTES_1.0.1.md)

## [Niewydane]

## [1.0.1] — 2026-11-02

Poprawkowe wydanie na bazie 1.0.0: `FrameHub-Setup-1.0.1.exe` pojawi się w sekcji **Assets** na [GitHub Releases](https://github.com/9Erza/FrameHub/releases) po opublikowaniu. Aktualizacja zachowuje historyczną tożsamość instalatora (`AppId A073F5F3-E7D0-45C7-A233-8A98B033385B`), wykorzystuje poprzedni katalog instalacji, zachowuje ustawienia/profile/bibliotekę/benchmarki, nie uruchamia ponownie kreatora pierwszego uruchomienia w istniejących instalacjach i nie reinstaluje zbędnie PresentMon.

### Poprawki

- Sprawdzanie aktualizacji potwierdza istnienie opublikowanego (nie-szkicowego, nie-przedpremierowego) wydania GitHub, zanim pokaże powiadomienie. Nowszy `version.txt` bez opublikowanego wydania nie kieruje już do strony bez instalatora; niejednoznaczna odpowiedź sieci zgłasza uczciwy błąd sprawdzenia zamiast fałszywej aktualizacji.
- Kreator pierwszego uruchomienia: początkowy fokus klawiatury trafia w widocznej akcji (Dalej, potem Zakończ na ostatnim kroku), Enter aktywuje Zakończ na podsumowaniu, a podsumowanie zawsze odzwierciedla wcześniejsze wybory. Wyłączenie Companion w kreatorze wyłącza też Live FPS, więc szkic nie zapisze sprzecznej kombinacji.
- Ustawienia > Dane aplikacji i reset: blok kreatora ma własny nagłówek zamiast powtarzania etykiety przycisku (nowy klucz `Settings.DataReset.ReopenWizardTitle` w EN/PL).
- Companion web UI: statyczne znaczniki wersji i wersjonowane cache-busting zasobów deklarują 1.0.1; działająca wersja nadal pochodzi na żywo z `/api/v1/status`.
- Manifest aplikacji Windows deklaruje 1.0.1.0 obok centralnego źródła `version.txt`.

### Zmiany

- `version.txt` ma teraz `1.0.1`; każda widoczna powierzchnia raportuje 1.0.1 z tego jednego źródła (WPF, Companion, nazwa instalatora, About, status API, metadane benchmarków).

Historia wcześniejszych wydań pozostaje poniżej bez zmian.

## [1.0.0] — 2026-10-28

Wydanie dla Windows 10/11 x64: `FrameHub-Setup-1.0.0.exe` będzie dostępne w sekcji **Assets** na [GitHub Releases](https://github.com/9Erza/FrameHub/releases) po jego opublikowaniu.

### Nowe możliwości i poprawki funkcjonalne (październik 2026)

- **Diagnostyka DNS tylko do odczytu:** karty sieciowe, aktualne resolvery DHCP/ręczne, podgląd poleceń oraz porównanie opóźnień IPv4/IPv6/DoH. Bez zmiany DNS w Windows.
- **Eksport porównań benchmarków do CSV i Markdown** z metadanymi środowiska; czytelniejsze komunikaty niedostępności silnika i błędów zapisu.
- **Procesy i CPU:** wyszukiwanie, sortowanie, widoczne reguły oraz ponowna weryfikacja tożsamości przed zastosowaniem zmian. **Profile:** walidacja, ostrzeżenia o konfliktach, włączanie/wyłączanie i poprawiony zapis stanu.
- **Optymalizacja sesji:** bezpieczniejsze wznawianie procesów, synchronizacja zdarzeń i poprawki błędów przy równoległym odświeżaniu oraz anulowaniu operacji.
- **Restart i zamykanie:** uruchomienie nowej instancji w kontrolowany sposób, limity czasu, komunikaty o odmowie/uprawnieniach i ciągłość logowania.
- **Monitor sprzętu:** opcjonalne czujniki dysków, czytelne stany WŁ./WYŁ./NIEDOSTĘPNY i diagnostyczny (niemodyfikujący) podgląd timera Windows.
- **Dziennik:** filtrowanie po ważności, wyszukiwanie, limit widocznych wpisów i potwierdzenie czyszczenia.
- **Biblioteka i Companion:** lepsze informacje o skanowaniu i brakujących plikach, dopracowane parowanie, telemetria i obsługa uprawnień.

### Przebudowany interfejs

- Desktop: odświeżone ekrany Panelu głównego, Gry i optymalizacji, Sesji, Benchmarków, Procesów, Profili, Monitora, Dziennika i Ustawień.
- Companion: wygodniejszy układ desktopowy i mobilny, dostępność klawiaturą oraz spójna lokalizacja PL/EN.
- Nowy styl Frame Core, jednolita wersja 1.0.0 w aplikacji i instalatorze.

### Funkcje zachowane z wcześniejszych wydań

Biblioteka Steam/Epic, Quick Start, profile CPU, CPU Sets/Affinity, automatyczna i ręczna Optymalizacja sesji, lokalne benchmarki Intel PresentMon, historia i porównania, Companion w LAN oraz bezpieczna obsługa konfiguracji CS2. **Nie przedstawiamy tych funkcji jako wszystkich nowo dodanych w 1.0.0.**

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
