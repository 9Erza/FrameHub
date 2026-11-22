# FrameHub v1.1.0 — Release notes / Informacje o wydaniu

**Windows 10/11 · x64 · .NET 10 · MIT · 22 November / 22 listopada 2026**

[English](#english) · [Polski](#polski) · [Complete changelog (EN)](../CHANGELOG.md) · [Lista zmian (PL)](../CHANGELOG.pl.md) · [Previous release notes v1.0.1](RELEASE_NOTES_1.0.1.md)

> The official installer is **`FrameHub-Setup-1.1.0.exe`** under **Assets** on [GitHub Releases](https://github.com/9Erza/FrameHub/releases). GitHub-generated source ZIP/TAR files are not installers. This document is the bilingual source for the GitHub Release description.

---

## English

### FrameHub 1.1.0: reliable CPU control, better game detection and a cleaner interface

1.1.0 fixes how FrameHub starts with Windows, how CPU Sets and affinity interact, and how games are found and recognised while running. It also removes repeated and contradictory states from the interface, gives every page one name, draws the window frame natively on Windows 11 and makes page switching much faster.

### Upgrading

Install `FrameHub-Setup-1.1.0.exe` over your existing FrameHub. The installer keeps the same identity (`AppId A073F5F3-E7D0-45C7-A233-8A98B033385B`) and installation folder, so settings, Library, profiles, benchmark history and paired devices are preserved and the first-run wizard does not appear again. After upgrading, rescan the Library once: entries with outdated executables and Unreal Engine games are repaired automatically.

### What's new in 1.1.0

**1. Start with Windows and CPU control**

- The elevated (administrator) startup task used the Task Scheduler default priority, which is *below normal*. FrameHub and every game launched from it inherited that low priority. New tasks use normal priority; an existing task is reported in **Settings → Start and window** as needing attention so one click repairs it, and FrameHub restores its own priority to normal at start.
- Applying **CPU Sets** first clears a restricted affinity mask left by an earlier Affinity setting, so the selected cores are really used.
- Profiles are re-applied when a game resets its own affinity, CPU Sets or priority after starting. Already-applied profiles are checked more cheaply.

**2. Game detection and launching**

- New sources: **GOG** and **Ubisoft Connect** (read-only registry discovery). Ubisoft games start through Ubisoft Connect.
- Unreal Engine games are recognised by their real `-Shipping` process instead of the small launcher file that exits immediately.
- A rescan repairs games that were moved or reinstalled instead of leaving them as "game file not found".
- Steam libraries are no longer scanned twice; games still downloading are not listed. Epic engines, plug-ins, DLC and unfinished installs are skipped, and Epic games start through the Epic Games Launcher.
- Running games are recognised by their executable path even when they run as administrator or protected (Riot processes are never opened this way).
- Scanning runs in the background, so the window no longer freezes.

**3. Interface**

- The sidebar and the page titles use the same names (Library, Session Optimization, Hardware Monitor, Profiles).
- Repeated and contradictory states were removed: the profile monitor shows *idle* when no profile is enabled, the ready count ignores games whose file is missing, and Dashboard, Session, Library, Benchmarks and Hardware no longer repeat the same status two or three times.
- Game CPU settings live in the **Library**; **Processes & CPU** is for one-off changes and **Profiles** lists every saved rule.
- On Windows 11 the window frame is drawn by the system: a crisp 1 px border with rounded corners at any display scale.
- Returning to Library, Session Optimization or Settings takes 7–20 ms instead of 100–130 ms; game icons load in the background.
- A link to **erza.studio** is in the sidebar footer and in **About**.

### Known limitations

- EA app and Battle.net games are not discovered automatically; add them manually or with a custom folder.
- The first opening of Library and Session Optimization still takes about 150 ms.
- On Windows 10 the window keeps a thin border drawn by FrameHub (no rounded corners).
- Session Optimization is still a long page; a deeper redesign of individual screens is planned for a later release.

---

## Polski

### FrameHub 1.1.0: niezawodne sterowanie CPU, lepsze wykrywanie gier i czytelniejszy interfejs

1.1.0 poprawia uruchamianie FrameHub z Windows, współdziałanie CPU Sets z koligacją oraz wyszukiwanie i rozpoznawanie uruchomionych gier. Usuwa też powtarzające się i sprzeczne stany z interfejsu, nadaje każdej stronie jedną nazwę, rysuje ramkę okna natywnie w Windows 11 i wyraźnie przyspiesza przełączanie stron.

### Aktualizacja

Zainstaluj `FrameHub-Setup-1.1.0.exe` na istniejącą instalację. Instalator ma tę samą tożsamość (`AppId A073F5F3-E7D0-45C7-A233-8A98B033385B`) i folder instalacji, więc ustawienia, biblioteka, profile, historia benchmarków i sparowane urządzenia zostają, a kreator pierwszego uruchomienia nie pojawia się ponownie. Po aktualizacji przeskanuj raz Bibliotekę: wpisy z nieaktualnymi plikami i gry Unreal Engine naprawią się automatycznie.

### Co nowego w 1.1.0

**1. Start z Windows i sterowanie CPU**

- Zadanie autostartu z uprawnieniami administratora miało domyślny priorytet Harmonogramu zadań, czyli *poniżej normalnego*. FrameHub i każda gra uruchomiona z niego dziedziczyły ten niski priorytet. Nowe zadania mają priorytet normalny; istniejące zadanie jest oznaczone w **Ustawienia → Start i okno** jako wymagające uwagi i naprawia się jednym kliknięciem, a FrameHub przy starcie przywraca sobie normalny priorytet.
- Ustawienie **CPU Sets** najpierw usuwa zawężoną maskę koligacji pozostawioną przez wcześniejsze ustawienie Affinity, więc wybrane rdzenie są naprawdę używane.
- Profile są nakładane ponownie, gdy gra po starcie sama zresetuje koligację, CPU Sets lub priorytet. Sprawdzanie już nałożonych profili jest tańsze.

**2. Wykrywanie i uruchamianie gier**

- Nowe źródła: **GOG** i **Ubisoft Connect** (tylko odczyt rejestru). Gry Ubisoft uruchamiają się przez Ubisoft Connect.
- Gry Unreal Engine są rozpoznawane po prawdziwym procesie `-Shipping`, a nie po małym pliku startowym, który od razu się zamyka.
- Ponowny skan naprawia przeniesione lub przeinstalowane gry zamiast zostawiać je jako „nie znaleziono pliku gry”.
- Biblioteki Steam nie są już skanowane dwa razy, a gry w trakcie pobierania nie trafiają do listy. Silniki, pluginy, DLC i niedokończone instalacje Epic są pomijane, a gry Epic uruchamiają się przez Epic Games Launcher.
- Uruchomione gry są rozpoznawane po ścieżce pliku także wtedy, gdy działają jako administrator lub są chronione (procesy Riot nigdy nie są tak otwierane).
- Skanowanie działa w tle, więc okno już się nie zawiesza.

**3. Interfejs**

- Pasek boczny i nagłówki stron mają te same nazwy (Biblioteka, Optymalizacja sesji, Monitor sprzętu, Profile).
- Usunięto powtarzające się i sprzeczne stany: monitor profili pokazuje *bezczynny*, gdy żaden profil nie jest włączony, licznik gotowych gier pomija gry bez pliku, a Panel główny, Sesja, Biblioteka, Benchmarki i Monitor sprzętu nie powtarzają już tego samego statusu dwa lub trzy razy.
- Ustawienia CPU gry są w **Bibliotece**; **Procesy i CPU** służą do jednorazowych zmian, a **Profile** to lista wszystkich zapisanych reguł.
- W Windows 11 ramkę okna rysuje system: równa linia 1 px i zaokrąglone rogi przy każdym skalowaniu.
- Powrót do Biblioteki, Optymalizacji sesji lub Ustawień trwa 7–20 ms zamiast 100–130 ms; ikony gier ładują się w tle.
- Link do **erza.studio** jest w stopce paska bocznego i w oknie **O programie**.

### Znane ograniczenia

- Gry z EA app i Battle.net nie są wykrywane automatycznie; dodaj je ręcznie lub przez własny folder.
- Pierwsze otwarcie Biblioteki i Optymalizacji sesji nadal trwa około 150 ms.
- W Windows 10 okno ma cienką ramkę rysowaną przez FrameHub (bez zaokrągleń).
- Optymalizacja sesji to wciąż długa strona; głębsze przeprojektowanie poszczególnych ekranów jest planowane w kolejnym wydaniu.
