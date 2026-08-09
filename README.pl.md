# FrameHub

[English](README.md) | [Polski](README.pl.md)

FrameHub to otwartoźródłowe narzędzie dla Windows do świadomej konfiguracji profili CPU dla gier, procesów w tle i wybranych ustawień gier. Wersja v0.5 pozostaje w przygotowaniu.

## Moduły

| Moduł | Zastosowanie |
| --- | --- |
| Biblioteka gier | Skanowanie Steam/Epic/folderów i konfiguracja konkretnej gry. |
| Optymalizacja sesji | Tymczasowe wstrzymanie wybranych aplikacji w tle podczas gry. |
| Procesy i CPU | Ręczna kontrola aktualnie uruchomionego procesu. |
| Profile i reguły | Zapisane ustawienia stosowane później przez monitor profili. |
| Monitor sprzętu | Opcjonalna telemetria lokalna, wyłączona po każdym uruchomieniu. |

Obsługiwane są profile CPU, CPU Sets/Affinity, priorytety, monitor profili, bezpieczne wstrzymanie i przywracanie sesji, konfiguracja CS2 z kopiami zapasowymi, logi, tray, autostart oraz interfejs PL/EN.

Jeden prawidłowy profil Steam userdata dla CS2 jest wybierany automatycznie; przy wielu profilach trzeba wskazać numeryczne ID userdata przed zapisem.

FrameHub nie stosuje DLL injection, modyfikacji pamięci gry, obejść anti-cheat ani sterownika jądra. Dane aplikacji znajdują się w `%APPDATA%\FrameHub`; uprawnienia administratora są potrzebne tylko dla części operacji systemowych.

## Start

Na Windows z SDK .NET 10 uruchom `dotnet restore .\FrameHub.slnx`, `dotnet build .\FrameHub.slnx`, `dotnet test .\FrameHub.slnx`, a następnie `dotnet run --project .\FrameHub.App\FrameHub.App.csproj`.

Szczegóły: [instrukcja użytkownika](docs/USER_GUIDE.pl.md), [architektura](docs/ARCHITECTURE.md), [roadmapa](docs/ROADMAP.md), [wkład](CONTRIBUTING.md). Autor: [9Erza](https://github.com/9Erza), [DobryPC.pl](https://dobrypc.pl), [Buy Me a Coffee](https://buymeacoffee.com/9erza). Licencja: [MIT](LICENSE).
