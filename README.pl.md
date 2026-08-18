<div align="center">

<img src="FrameHub.App/Assets/FrameHubLogo.png" alt="Logo FrameHub" width="220" />

# FrameHub

**Kontrola wydajności i optymalizacji gier w Windows — bez ukrytych tweaków i „magicznych” paczek FPS.**

Biblioteka gier, profile CPU, optymalizacja sesji i lokalne benchmarki czasu klatek,
serwer LAN Companion, konfiguracja CS2 oraz monitoring sprzętu w jednej aplikacji desktopowej.

[English](README.md) · [**Polski**](README.pl.md)

![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square)
[![CI](https://github.com/9Erza/FrameHub/actions/workflows/ci.yml/badge.svg)](https://github.com/9Erza/FrameHub/actions/workflows/ci.yml)
[![Licencja](https://img.shields.io/badge/licencja-MIT-2EA44F?style=flat-square)](LICENSE)
![Wydanie](https://img.shields.io/badge/wydanie-v0.7.0-2EA44F?style=flat-square)

</div>

> [!NOTE]
> **Aktualne wydanie: v0.7.0.** Szczegóły wydania znajdziesz w [Changelogu](CHANGELOG.md), a dalsze plany w [Roadmapie](docs/ROADMAP.md).

---

## Czym jest FrameHub?

**FrameHub** to otwartoźródłowe narzędzie dla Windows stworzone z myślą o świadomej, odwracalnej i kontrolowanej przez użytkownika optymalizacji gier.

Zamiast stosować ukryte zmiany systemowe lub gotowe „paczki FPS”, FrameHub daje bezpośrednią kontrolę nad:

- tym, co jest zmieniane,
- momentem zastosowania ustawień,
- procesami objętymi optymalizacją,
- oraz tym, co powinno zostać przywrócone po zakończeniu sesji.

Projekt skupia się obecnie na:

- **profilach CPU i procesów dla konkretnych gier,**
- **tymczasowym ograniczaniu aplikacji działających w tle podczas grania,**
- **przydziale CPU dla aktywnej gry (CPU Sets i Affinity),**
- **serwerze LAN Companion do podglądu i sterowania ze smartfona,**
- **lokalnych benchmarkach pojedynczych klatek, historii i porównywaniu wyników wraz z metadanymi środowiska,**
- **bezpiecznej konfiguracji Counter-Strike 2,**
- **opcjonalnym lokalnym monitoringu sprzętu i diagnostyce.**

---

## Moduły

| Moduł | Zastosowanie |
| --- | --- |
| **Gry i optymalizacja** | Skanowanie Steam, Epic i własnych folderów, ręczne dodawanie plików wykonywalnych, uruchamianie gier oraz konfiguracja ustawień optymalizacji CPU dla konkretnej gry. |
| **Optymalizacja sesji** | Tymczasowe wstrzymywanie wybranych aplikacji działających w tle podczas aktywnej sesji gry i bezpieczne przywracanie ich po zakończeniu. |
| **Przydział CPU dla gry** | Tymczasowe przypisywanie rdzeni CPU dla aktywnej gry (Affinity / CPU Sets) z profilami topologii (Wszystkie, Tylko fizyczne, Wyczyść). |
| **Serwer Companion** | Lokalny serwer WWW w sieci LAN umożliwiający strumieniowanie telemetrii sprzętowej, kontrolę benchmarków i uruchamianie gier ze smartfona. |
| **Benchmarki** | Wykrywanie uruchomionych gier z biblioteki, pomiar dokładnego procesu z metadanymi środowiska, wykres czasu klatek, lokalna historia i porównanie sesji. |
| **Procesy i CPU** | Podgląd uruchomionego procesu i natychmiastowe zastosowanie CPU Sets, Processor Affinity lub priorytetu procesu. |
| **Profile i reguły** | Zapisywanie ustawień procesów i automatyczne stosowanie ich przez monitor profili po uruchomieniu odpowiedniego pliku wykonywalnego. |
| **Monitor sprzętu** | Opcjonalna lokalna telemetria CPU, GPU i RAM z automatycznym cyklem życia czujników na żądanie. |
| **Logi i ustawienia** | Diagnostyka, język, zachowanie w zasobniku systemowym, parowanie urządzeń Companion oraz konfiguracja autostartu Windows. |

---

### Benchmarki w v0.7.0

Uruchom grę z Biblioteki gier, otwórz **Benchmarki** (albo użyj akcji **Benchmark** przy grze), wybierz czas i za każdym razem odtwórz tę samą scenę. FrameHub pokazuje średnie FPS, medianę, 1% Low, 0,1% Low, P95/P99 czasu klatki, metadane środowiska testowego (system, CPU, sterownik GPU, RAM, ekran), diagnostykę jakości, wykres zachowujący skoki, lokalną historię oraz porównania tej samej gry. Surowe klatki i podsumowania pozostają na komputerze w `%LOCALAPPDATA%\FrameHub\Benchmarks`; FrameHub nie wysyła ich do chmury i nie dodaje analityki ani kont.

Pomiar wykorzystuje Intel PresentMon Shared Service/API. Oficjalny instalator PresentMon v2.5.1 MSI jest osadzony w jednym FrameHub Setup, więc użytkownik nie pobiera drugiego instalatora. PresentMon jest współdzielonym składnikiem na licencji MIT i może pozostać po usunięciu FrameHub; zobacz [informacje o składnikach zewnętrznych](docs/THIRD-PARTY-NOTICES.md).

Sam FrameHub nie wstrzykuje bibliotek DLL do gier, nie odczytuje ani nie zmienia pamięci gry, nie instaluje sterownika jądra FrameHub i nie omija anti-cheat. Korzysta z udokumentowanej ścieżki usługi/API/ETW PresentMon. Zgodność zależy od gry i anti-cheat i nie jest gwarantowana dla każdego tytułu.

---

## Funkcje

### Biblioteka gier i Szybki start

- Skanowanie biblioteki Steam.
- Skanowanie Epic Games.
- Obsługa własnych folderów z grami.
- Ręczne dodawanie plików `.exe`.
- Konfiguracja ustawień dla konkretnej gry.
- Wykrywanie, czy gra jest aktualnie uruchomiona.
- Przypisywanie profilu CPU do wybranej gry.
- Szybki start: jednoczesne uruchamianie gry i Optymalizacji sesji jednym kliknięciem.
- Bezpieczna obsługa gier Riot (League of Legends, VALORANT) przez oficjalne skróty.
- Zabezpieczenia przed brakującymi plikami wykonywalnymi.
- Filtrowanie znanych elementów pomocniczych Steam, które nie są grami.

### Optymalizacja sesji i Przydział CPU

- Automatyczne wykrywanie uruchomienia gry.
- Ręczne uruchamianie sesji optymalizacji.
- Tymczasowe wstrzymywanie wybranych aplikacji działających w tle.
- Tymczasowy przydział rdzeni CPU dla aktywnej gry (CPU Sets i Affinity).
- Presety topologii procesora (Wszystkie rdzenie, Tylko fizyczne, Wyczyść).
- Bezpieczne przywracanie procesów po zakończeniu sesji.
- Stan odzyskiwania po nieprawidłowo zakończonej sesji.
- Walidacja procesów ograniczająca ryzyko przywrócenia niewłaściwego procesu.

### Serwer LAN Companion i interfejs mobilny

- Lekki serwer WWW ASP.NET Core Kestrel w sieci lokalnej.
- Bezpieczne parowanie kryptograficzne z kodami QR i tokenami sesyjnymi.
- Szczegółowe uprawnienia odczytu i zapisu (uprawnienia scoped).
- Strumieniowanie telemetrii sprzętowej w czasie rzeczywistym (WebSocket).
- Zdalny podgląd biblioteki i uruchamianie gier.
- Zdalne sterowanie pomiarami i podgląd stanu benchmarków.
- Karty Optymalizacji sesji i Przydziału CPU w telefonie.
- Buforowanie ikon gier po stronie klienta (IndexedDB).

### CPU i procesy

- Obsługa CPU Sets.
- Fallback do klasycznego Processor Affinity.
- Zarządzanie priorytetem procesu.
- Profile CPU dla konkretnych gier.
- Zapisywane profile procesów.
- Monitor profili działający w tle.
- Przypisywanie profili do konkretnej ścieżki pliku wykonywalnego.
- Zabezpieczenie przed przypadkowym dopasowaniem procesów o tej samej nazwie.
- Obsługa tradycyjnych profili opartych o samą nazwę procesu.

### Counter-Strike 2

- Konfiguracja ustawień graficznych CS2.
- Pomocnik edycji pliku `autoexec.cfg`.
- Bezpieczny mechanizm tworzenia kopii zapasowych przed zapisem.
- Unikalne, bezkolizyjne nazwy kopii zapasowych.
- Ostrzeżenia o synchronizacji ze Steam Cloud.
- Sprawdzanie, czy CS2 jest aktualnie uruchomiony.
- Bezpieczna obsługa wielu profili Steam `userdata`.
- Automatyczny wybór jedynego poprawnego profilu userdata.
- Wymóg ręcznego wskazania profilu w przypadku wykrycia wielu kont.

### Monitoring sprzętu

- Lokalna telemetria CPU.
- Lokalna telemetria GPU.
- Monitoring wykorzystania RAM.
- Monitoring jest w pełni opcjonalny.
- Odczyt czujników działa tylko przy aktywnym zapotrzebowaniu (lease-controlled).
- Automatyczne zamykanie czujników po zwolnieniu zasobu.

### Integracja z systemem Windows

- Interfejs w języku polskim i angielskim.
- Logi aplikacji i historia aktywności.
- Obsługa zasobnika systemowego (tray).
- Minimalizowanie i zamykanie do zasobnika.
- Konfiguracja autostartu wraz z systemem Windows.
- Standardowy autostart w kontekście bieżącego użytkownika.
- Opcjonalny autostart z uprawnieniami administratora.
- Uprawnienia administratora nie są wymagane do standardowego działania.

---

## Bezpieczeństwo i przejrzystość

FrameHub podchodzi do optymalizacji w sposób celowo ostrożny i bezpieczny.

FrameHub **nie**:

- wstrzykuje bibliotek DLL do gier,
- modyfikuje pamięci gry,
- instaluje sterowników jądra (kernel driver),
- omija systemów anti-cheat,
- stosuje po cichu nieudokumentowanych zmian w rejestrze,
- korzysta z gotowych „magicznych paczek FPS”,
- modyfikuje ani nie benchmarkuje procesów gier, klienta i Vanguard firmy Riot Games,
- zapisuje danych do niejednoznacznych kont Steam CS2.

Zmiany dotyczące CPU i procesów są jawne i kontrolowane przez użytkownika.

Optymalizacja sesji rejestruje wstrzymane procesy, aby bezpiecznie przywrócić je po zakończeniu grania.

Zmiany w konfiguracji CS2 opierają się na plikach tekstowych i są chronione kopiami zapasowymi oraz walidacją stanu gry.

Czujniki sprzętowe są inicjalizowane dopiero po jawnym włączeniu monitoringu lub podłączeniu klienta.

> [!WARNING]
> Żadne zewnętrzne narzędzie nie może zagwarantować pełnej zgodności z każdą grą, platformą anti-cheat lub konfiguracją sprzętową.
> Zawsze weryfikuj stosowane ustawienia i testuj zmiany na swoim komputerze.

---

## Rozwój, wsparcie i zgodność

### Przejrzystość procesu rozwoju

FrameHub to niezależny projekt hobbystyczny rozwijany i utrzymywany przez jednego autora w czasie wolnym. Ponieważ za projektem nie stoi firma ani dedykowany zespół programistów, dostępny czas na rozwój jest w naturalny sposób ograniczony.

Prace nad projektem opierają się na testach automatycznych, ukierunkowanym przeglądzie kodu i ostrożnych decyzjach technicznych. Nowoczesne narzędzia, w tym asystenci programistyczni AI, są wykorzystywane na każdym etapie prac do wspierania implementacji, testów, ukierunkowanego przeglądu kodu, dokumentacji i analizy technicznej. Kierunek architektoniczny, zakres funkcji, granice bezpieczeństwa i decyzje o wydaniach pozostają w pełni pod kontrolą autora.

### Wsparcie, gwarancja i zgodność z grami

- **Wsparcie i zgłoszenia**: Zgłoszenia błędów oraz uwag dotyczących bezpieczeństwa są mile widziane poprzez GitHub Issues oraz kontakt z autorem (zobacz [Politykę bezpieczeństwa](SECURITY.md)). Autor dokłada starań, aby analizować i rozwiązywać istotne problemy w miarę dostępnego czasu, jednak nie gwarantuje określonego czasu reakcji, terminów napraw (SLA) ani stałego cyklu wydań.
- **Licencja i brak gwarancji**: FrameHub jest udostępniany na warunkach [Licencji MIT](LICENSE) na zasadzie „tak jak jest” (AS IS), bez jakichkolwiek gwarancji. Szczegółowe warunki prawne znajdują się w pliku [LICENSE](LICENSE).
- **Filozofia bezpieczeństwa i nieinwazyjność**: FrameHub został zaprojektowany z myślą o maksymalnym poszanowaniu środowiska gier i platform anti-cheat. Projekt unika stosowania metod inwazyjnych, takich jak wstrzykiwanie bibliotek DLL, modyfikacja pamięci gier, sterowniki trybu jądra (kernel-mode drivers), podpinanie debuggera, omijanie zabezpieczeń czy nieudokumentowane hooki.
- **Research i ocena ryzyka**: Wszelkie funkcjonalności wchodzące w interakcję z grami, procesami systemowymi lub telemetrią są analizowane w oparciu o oficjalną dokumentację, źródła techniczne oraz manualny research, a także dodatkowo weryfikowane przy pomocy niezależnych narzędzi badawczych opartych na AI. W przypadku pojawienia się istotnych wątpliwości, niepotrzebnej inwazyjności lub niejasnego wpływu na systemy zabezpieczeń gier, zasadą projektu jest odrzucenie lub zaniechanie danej funkcji zamiast akceptowania niepotrzebnego ryzyka.
- **Reakcja na nowe ryzyka**: W przypadku pojawienia się wiarygodnych przesłanek, że jakakolwiek funkcja FrameHub może stwarzać nieakceptowalne ryzyko kompatybilności z systemami anti-cheat lub stabilności, priorytetem jest jej natychmiastowe ograniczenie, wyłączenie lub usunięcie do czasu bezpiecznego wyjaśnienia sprawy.
- **Brak formalnych certyfikacji**: FrameHub jest niezależnym projektem hobbystycznym i nie posiada formalnych partnerstw, aprobat ani certyfikatów od producentów gier czy dostawców systemów anti-cheat. Z uwagi na ciągłe zmiany w grach, aktualizacje systemu Windows, sterowników oraz niejawny charakter mechanizmów anti-cheat, żadne narzędzie zewnętrzne nie może zagwarantować 100% zgodności. Zaleca się świadome stosowanie ustawień i weryfikację ich działania na własnym sprzęcie.

---

## Szybki start

1. Otwórz **Gry i optymalizacja** (Bibliotekę gier).
2. Przeskanuj Steam, Epic lub własne foldery, albo dodaj grę ręcznie.
3. Wybierz grę i skonfiguruj profil CPU lub skorzystaj z funkcji **Szybki start** na Pulpicie.
4. Skonfiguruj **Optymalizację sesji**, jeśli chcesz wstrzymywać wybrane aplikacje w tle podczas grania.
5. Użyj modułu **Procesy i CPU**, gdy chcesz zmienić ustawienia aktualnie działającego procesu.
6. Sparuj telefon w zakładce **Ustawienia > Companion**, aby monitorować komputer w sieci LAN.
7. Włącz **Monitor sprzętu** tylko wtedy, gdy potrzebujesz lokalnej telemetrii.

Szczegółowe informacje znajdziesz w [Instrukcji użytkownika](docs/USER_GUIDE.pl.md).

---

## Budowanie ze źródeł

### Wymagania

- Windows 10 lub Windows 11
- .NET 10 SDK
- Git

Sklonuj repozytorium:

```powershell
git clone https://github.com/9Erza/FrameHub.git
cd FrameHub
```

Przywróć zależności:

```powershell
dotnet restore .\FrameHub.slnx
```

Skompiluj projekt:

```powershell
dotnet build .\FrameHub.slnx
```

Uruchom testy:

```powershell
dotnet test .\FrameHub.slnx
```

Uruchom FrameHub:

```powershell
dotnet run --project .\FrameHub.App\FrameHub.App.csproj
```

### Budowanie instalatora

Zainstaluj Inno Setup 6, a następnie uruchom:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1
```

Skrypt automatycznie przygotuje oficjalny instalator Intel PresentMon v2.5.1 MSI, zweryfikuje jego sumę kontrolną SHA-256 i osadzi go w wygenerowanym pakiecie instalacyjnym FrameHub.

---

## Dane aplikacji

FrameHub przechowuje dane w katalogu:

```text
%APPDATA%\FrameHub
```

Obejmuje to:

- ustawienia,
- dane biblioteki gier,
- zapisane profile,
- sparowane urządzenia Companion,
- logi aplikacji,
- stan przywracania Optymalizacji sesji,
- kopie zapasowe konfiguracji.

---

## Dokumentacja

- **[Instrukcja użytkownika](docs/USER_GUIDE.pl.md)**
  Szczegółowy opis obsługi programu.

- **[Instrukcja po angielsku](docs/USER_GUIDE.md)**
  User guide in English.

- **[Architektura](docs/ARCHITECTURE.md)**
  Opis architektury i granic usług.

- **[Roadmapa](docs/ROADMAP.md)**
  Planowane i zrealizowane funkcje.

- **[Changelog](CHANGELOG.md)**
  Historia zmian i wydań.

- **[Współtworzenie](CONTRIBUTING.md)**
  Zasady rozwoju projektu.

- **[Zasady bezpieczeństwa](SECURITY.md)**
  Informacje o zgłaszaniu podatności.

---

## Autor i wsparcie

### Autor

[**9Erza na GitHubie**](https://github.com/9Erza)

### Strona internetowa

[**DobryPC.pl**](https://dobrypc.pl)

### Wsparcie projektu

[**☕ Postaw kawę (Buy Me a Coffee)**](https://buymeacoffee.com/9erza)

### Repozytorium

[**github.com/9Erza/FrameHub**](https://github.com/9Erza/FrameHub)

Jeśli FrameHub jest dla Ciebie przydatny, **gwiazdka na GitHubie** lub wsparcie projektu pomaga w jego dalszym rozwoju.

---

## Licencja

FrameHub jest udostępniany na warunkach [Licencji MIT](LICENSE).
