<div align="center">

<img src="FrameHub.App/Assets/FrameHubLogo.png" alt="Logo FrameHub" width="220" />

# FrameHub

**Kontrola wydajności i optymalizacji gier w Windows — bez ukrytych tweaków i „magicznych” paczek FPS.**

Biblioteka gier, profile CPU, optymalizacja sesji i lokalne benchmarki czasu klatek,
konfiguracja CS2 oraz monitoring sprzętu w jednej aplikacji desktopowej.

[English](README.md) · [**Polski**](README.pl.md)

![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square)
[![CI](https://github.com/9Erza/FrameHub/actions/workflows/ci.yml/badge.svg)](https://github.com/9Erza/FrameHub/actions/workflows/ci.yml)
[![Licencja](https://img.shields.io/badge/licencja-MIT-2EA44F?style=flat-square)](LICENSE)
![Wydanie](https://img.shields.io/badge/wydanie-v0.6.0-2EA44F?style=flat-square)

</div>

> [!NOTE]
> **Aktualne wydanie: v0.6.0.** Szczegóły wydania znajdziesz w [Changelogu](CHANGELOG.md), a dalsze plany w [Roadmapie](docs/ROADMAP.md).

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
- **lokalnych benchmarkach pojedynczych klatek, historii i porównywaniu wyników tej samej gry,**
- **bezpiecznej konfiguracji Counter-Strike 2,**
- **opcjonalnym lokalnym monitoringu sprzętu i diagnostyce.**

---

## Moduły

| Moduł | Zastosowanie |
| --- | --- |
| **Gry i optymalizacja** | Skanowanie Steam, Epic i własnych folderów, ręczne dodawanie plików wykonywalnych, uruchamianie gier oraz konfiguracja ustawień optymalizacji CPU dla konkretnej gry. |
| **Optymalizacja sesji** | Tymczasowe wstrzymywanie wybranych aplikacji działających w tle podczas aktywnej sesji gry i bezpieczne przywracanie ich po zakończeniu. |
| **Procesy i CPU** | Podgląd uruchomionego procesu i natychmiastowe zastosowanie CPU Sets, Processor Affinity lub priorytetu procesu. |
| **Benchmarki** | Wykrywanie uruchomionych gier z biblioteki, pomiar dokładnego procesu, wykres czasu klatek, lokalna historia i porównanie sesji tej samej gry. |
| **Profile i reguły** | Zapisywanie ustawień procesów i automatyczne stosowanie ich przez monitor profili po uruchomieniu odpowiedniego pliku wykonywalnego. |
| **Monitor sprzętu** | Opcjonalna lokalna telemetria CPU, GPU i RAM. Monitoring jest ponownie wyłączony po każdym nowym uruchomieniu FrameHub. |
| **Logi i ustawienia** | Diagnostyka, język, zachowanie w zasobniku systemowym, logowanie oraz konfiguracja autostartu Windows. |

---

### Benchmarki w v0.6.0

Uruchom grę z Biblioteki gier, otwórz **Benchmarki** (albo użyj akcji **Benchmark** przy grze), wybierz czas i za każdym razem odtwórz tę samą scenę. FrameHub pokazuje średnie FPS, medianę, 1% Low, 0,1% Low, P95/P99 czasu klatki, diagnostykę jakości, wykres zachowujący skoki, lokalną historię oraz porównania tej samej gry. Surowe klatki i podsumowania pozostają na komputerze w `%LOCALAPPDATA%\FrameHub\Benchmarks`; FrameHub nie wysyła ich do chmury i nie dodaje analityki ani kont.

Pomiar wykorzystuje Intel PresentMon Shared Service/API. Oficjalny instalator PresentMon v2.5.1 MSI jest osadzony w jednym FrameHub Setup, więc użytkownik nie pobiera drugiego instalatora. PresentMon jest współdzielonym składnikiem na licencji MIT i może pozostać po usunięciu FrameHub; zobacz [informacje o składnikach zewnętrznych](docs/THIRD-PARTY-NOTICES.md).

Sam FrameHub nie wstrzykuje bibliotek DLL do gier, nie odczytuje ani nie zmienia pamięci gry, nie instaluje sterownika jądra FrameHub i nie omija anti-cheat. Korzysta z udokumentowanej ścieżki usługi/API/ETW PresentMon. Zgodność zależy od gry i anti-cheat i nie jest gwarantowana dla każdego tytułu.

## Funkcje

### Biblioteka gier

- Skanowanie biblioteki Steam.
- Skanowanie Epic Games.
- Obsługa własnych folderów z grami.
- Ręczne dodawanie plików `.exe`.
- Konfiguracja ustawień dla konkretnej gry.
- Wykrywanie, czy gra jest aktualnie uruchomiona.
- Przypisywanie profilu CPU do wybranej gry.
- Filtrowanie znanych elementów pomocniczych Steam, które nie są grami.

### Optymalizacja sesji

- Automatyczne wykrywanie uruchomienia gry.
- Ręczne uruchamianie sesji optymalizacji.
- Tymczasowe wstrzymywanie wybranych aplikacji działających w tle.
- Bezpieczne przywracanie procesów po zakończeniu sesji.
- Stan odzyskiwania po nieprawidłowo zakończonej sesji.
- Walidacja procesów ograniczająca ryzyko przywrócenia niewłaściwego procesu.
- Obsługa zarówno sesji automatycznych, jak i ręcznych.

### CPU i procesy

- Obsługa CPU Sets.
- Fallback do klasycznego Processor Affinity.
- Zarządzanie priorytetem procesu.
- Profile CPU dla konkretnych gier.
- Zapisywane profile procesów.
- Monitor profili działający w tle.
- Powiązanie profilu ze ścieżką pliku wykonywalnego, jeśli jest dostępna.
- Ochrona przed przypadkowym dopasowaniem dwóch różnych programów posiadających tę samą nazwę procesu.
- Zachowanie kompatybilności ze starszymi profilami opartymi wyłącznie na nazwie procesu.

### Counter-Strike 2

- Obsługa wybranych ustawień graficznych i plików konfiguracyjnych CS2.
- Edytor `autoexec.cfg`.
- Automatyczna kopia zapasowa przed zapisem.
- Nazwy backupów odporne na kolizje.
- Ostrzeżenia związane ze Steam Cloud.
- Blokada niebezpiecznych operacji konfiguracyjnych podczas działania CS2.
- Bezpieczna obsługa wielu profili Steam `userdata`.
- Przy jednym prawidłowym profilu userdata konto jest wybierane automatycznie.
- Przy wielu prawidłowych profilach wymagany jest ręczny wybór przed umożliwieniem operacji zapisu.

### Monitoring sprzętu

- Lokalny monitoring CPU.
- Lokalny monitoring GPU.
- Monitoring wykorzystania pamięci RAM.
- Monitoring jest całkowicie opcjonalny.
- Sensory są uruchamiane dopiero po ręcznym włączeniu monitoringu.
- Po każdym nowym uruchomieniu FrameHub monitoring ponownie startuje jako wyłączony.

### Integracja z Windows

- Interfejs w języku polskim i angielskim.
- Logi aplikacji i historia aktywności.
- Obsługa zasobnika systemowego.
- Minimalizacja do zasobnika.
- Zamykanie aplikacji do zasobnika.
- Konfiguracja autostartu Windows.
- Standardowy autostart w kontekście aktualnego użytkownika.
- Opcjonalny autostart z podwyższonymi uprawnieniami.
- Uprawnienia administratora nie są wymagane do normalnego korzystania z aplikacji.

---

## Bezpieczeństwo i przejrzystość

FrameHub został zaprojektowany w konserwatywny sposób i nie stosuje agresywnych metod „optymalizacji”.

FrameHub **nie**:

- wstrzykuje bibliotek DLL do gier,
- modyfikuje pamięci gry,
- instaluje sterownika działającego w jądrze systemu,
- omija zabezpieczeń anti-cheat,
- stosuje ukrytych tweaków Windows,
- korzysta z gotowych „one-click FPS boost” packów,
- zapisuje konfiguracji CS2 do przypadkowo wybranego konta Steam.

Zmiany CPU i procesów są jawne i kontrolowane przez użytkownika.

Optymalizacja sesji zapisuje informacje o procesach wstrzymanych podczas sesji, aby możliwe było ich późniejsze bezpieczne przywrócenie.

Zmiany konfiguracji CS2 dotyczą plików tekstowych i są chronione przez kopie zapasowe oraz kontrole bezpieczeństwa wykonywane przed zapisem.

Sensory sprzętowe są inicjalizowane dopiero po ręcznym włączeniu monitoringu.

> [!WARNING]
> Żadne zewnętrzne narzędzie nie może zagwarantować kompatybilności z każdą grą, systemem anti-cheat ani konfiguracją sprzętową.  
> Sprawdzaj stosowane ustawienia i testuj zmiany na własnym komputerze.

---

## Rozwój, wsparcie i kompatybilność

### Transparentność rozwoju

FrameHub to niezależny projekt hobbystyczny rozwijany i utrzymywany w czasie wolnym przez jednego autora. Ze względu na brak zaplecza w postaci firmy czy dedykowanego zespołu inżynierskiego, czas przeznaczony na rozwój i bieżące wsparcie jest naturalnie ograniczony.

Proces powstawania projektu opiera się na automatycznych testach, ukierunkowanych przeglądach kodu oraz konserwatywnych decyzjach technicznych. W codziennej pracy wykorzystywane są nowoczesne narzędzia wspomagające programowanie i research, w tym narzędzia oparte na AI, które pomagają m.in. przy implementacji, przygotowywaniu testów, weryfikacji kodu, tworzeniu dokumentacji oraz researchu technicznym. Kierunek architektoniczny, zakres funkcji, granice bezpieczeństwa i decyzje o wydaniach pozostają w pełni kierowane przez autora projektu.

### Wsparcie, gwarancje i kompatybilność z systemami anti-cheat

- **Utrzymanie i zgłoszenia**: Zgłoszenia błędów oraz kwestii związanych z bezpieczeństwem są mile widziane poprzez GitHub Issues oraz prywatny kontakt z autorem (zobacz [Security Policy](SECURITY.md)). Autor dąży do analizowania i naprawiania istotnych usterek oraz problemów bezpieczeństwa w miarę dostępnego czasu, jednak nie gwarantuje określonego czasu reakcji (SLA), terminów wydań ani ciągłego harmonogramu aktualizacji.
- **Licencja i gwarancje**: FrameHub jest udostępniany na warunkach [licencji MIT](LICENSE) na zasadzie „AS IS” („tak jak jest”), bez jakichkolwiek gwarancji. Plik [LICENSE](LICENSE) zawiera wiążące postanowienia prawne.
- **Filozofia anti-cheat i brak inwazyjności**: FrameHub jest projektowany z zachowaniem szczególnej ostrożności wobec procesów gier oraz platform anti-cheat. Projekt świadomie unika inwazyjnych technik, takich jak wstrzykiwanie bibliotek DLL, odczyt lub modyfikacja pamięci gry, sterowniki trybu jądra (kernel-mode), podpinanie debuggera, próby obchodzenia zabezpieczeń anti-cheat czy nieudokumentowane hooki w pamięci procesów.
- **Research i ocena ryzyka**: Wszelkie funkcjonalności wchodzące w interakcję z grami, procesami systemowymi lub telemetrią są analizowane w oparciu o oficjalną dokumentację, źródła techniczne oraz manualny research, a także dodatkowo weryfikowane przy pomocy niezależnych narzędzi badawczych opartych na AI. W przypadku pojawienia się istotnych wątpliwości, niepotrzebnej inwazyjności lub niejasnego wpływu na systemy zabezpieczeń gier, zasadą projektu jest odrzucenie lub zaniechanie danej funkcji zamiast akceptowania niepotrzebnego ryzyka.
- **Reakcja na nowe ryzyka**: W przypadku pojawienia się wiarygodnych przesłanek, że jakakolwiek funkcja FrameHub może stwarzać nieakceptowalne ryzyko kompatybilności z systemami anti-cheat lub stabilności, priorytetem jest jej natychmiastowe ograniczenie, wyłączenie lub usunięcie do czasu bezpiecznego wyjaśnienia sprawy.
- **Brak formalnych certyfikacji**: FrameHub jest niezależnym projektem hobbystycznym i nie posiada formalnych partnerstw, aprobat ani certyfikatów od producentów gier czy dostawców systemów anti-cheat. Z uwagi na ciągłe zmiany w grach, aktualizacje systemu Windows, sterowników oraz niejawny charakter mechanizmów anti-cheat, żadne narzędzie zewnętrzne nie może zagwarantować 100% zgodności. Zaleca się świadome stosowanie ustawień i weryfikację ich działania na własnym sprzęcie.

---

## Szybki start

1. Otwórz **Bibliotekę gier**.
2. Przeskanuj Steam, Epic lub własne foldery albo dodaj grę ręcznie.
3. Wybierz grę i skonfiguruj profil CPU, jeśli chcesz z niego korzystać.
4. Skonfiguruj **Optymalizację sesji**, jeśli FrameHub ma tymczasowo wstrzymywać wybrane aplikacje podczas grania.
5. Użyj **Procesów i CPU**, jeśli chcesz bezpośrednio zmienić ustawienia już uruchomionego procesu.
6. Włącz **Monitor sprzętu** tylko wtedy, gdy potrzebujesz lokalnej telemetrii.

Szczegółowe instrukcje znajdziesz w [Instrukcji użytkownika](docs/USER_GUIDE.pl.md).

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

Zbuduj projekt:

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

---

## Dane aplikacji

FrameHub przechowuje swoje dane w:

```text
%APPDATA%\FrameHub
```

Znajdują się tam między innymi:

- ustawienia aplikacji,
- dane biblioteki gier,
- zapisane profile,
- logi,
- dane odzyskiwania Optymalizacji sesji,
- kopie zapasowe zarządzane przez FrameHub.

---

## Dokumentacja

- **[Instrukcja użytkownika](docs/USER_GUIDE.pl.md)**  
  Szczegółowe informacje dotyczące korzystania z FrameHub.

- **[User Guide](docs/USER_GUIDE.md)**  
  Angielska instrukcja użytkownika.

- **[Architektura](docs/ARCHITECTURE.md)**  
  Aktualna architektura aplikacji i główne zależności między usługami.

- **[Roadmapa](docs/ROADMAP.md)**  
  Funkcje zaimplementowane, planowane i eksperymentalne.

- **[Changelog](CHANGELOG.md)**  
  Informacje o aktualnym wydaniu i historia wydań.

- **[Contributing](CONTRIBUTING.md)**  
  Informacje dotyczące rozwoju projektu i zgłaszania zmian.

- **[Security Policy](SECURITY.md)**  
  Informacje dotyczące zgłaszania problemów związanych z bezpieczeństwem.

---

## Projekt i wsparcie

### Autor

[**9Erza na GitHubie**](https://github.com/9Erza)

### Strona

[**DobryPC.pl**](https://dobrypc.pl)

### Wesprzyj rozwój

[**☕ Buy Me a Coffee**](https://buymeacoffee.com/9erza)

### Repozytorium

[**github.com/9Erza/FrameHub**](https://github.com/9Erza/FrameHub)

Jeżeli FrameHub jest dla Ciebie przydatny, możesz pomóc projektowi zostawiając **gwiazdkę na GitHubie** albo wspierając jego dalszy rozwój.

---

## Licencja

FrameHub jest dostępny na licencji [MIT](LICENSE).
