# Instrukcja użytkownika FrameHub

[English](USER_GUIDE.md)

Po pierwszym uruchomieniu przeskanuj **Gry i optymalizacja** (Bibliotekę gier). Dodaj folder lub plik EXE ręcznie, jeśli launcher nie wykrył gry. Zapis profilu utrwala regułę; **Optymalizuj/Zastosuj** próbuje zmienić pasujące, uruchomione procesy. Możesz także użyć funkcji **Szybki start** na Panelu głównym, aby uruchomić grę i zastosować Optymalizację sesji jednym kliknięciem.

W **Optymalizacji sesji** wybierasz procesy w tle do wstrzymania, sprawdzasz podgląd i uruchamiasz sesję ręcznie albo automatycznie. FrameHub zapisuje wstrzymane procesy i wznawia je bezpiecznie przy zakończeniu lub odzyskiwaniu — nie kończy ich działania.

Moduł **Procesy i CPU** służy do bezpośredniego sterowania działającymi procesami (CPU Sets, Processor Affinity, priorytet). **Profile i reguły** zarządzają trwałymi ustawieniami stosowanymi automatycznie przez monitor profili.

CS2 należy zamknąć przed edycją konfiguracji; tworzone są kopie zapasowe. Monitor sprzętu jest opcjonalny i działa na żądanie (lease-controlled). Zakładka **Ustawienia** obejmuje zasobnik systemowy, język, logi, autostart, parowanie z serwerem Companion i opcjonalne uprawnienia administratora. Dane i logi: `%APPDATA%\FrameHub`.

Gdy istnieje jeden prawidłowy folder Steam userdata CS2, FrameHub wybiera go automatycznie. Przy kilku folderach wybierz numeryczne ID userdata w szczegółach CS2. Do tego czasu FrameHub blokuje odczyt edytowalnej konfiguracji i wszystkie zapisy CS2. Nowe kopie zapasowe są rozdzielane według wybranej ścieżki userdata.

---

## Przeprojektowany interfejs (październik 2026, Ember Signal)

Powłoka desktopowa zachowuje wszystkie moduły, ale je przebudowuje: pasek tytułu 42 px, szyna
nawigacji 224 px z grupami, dwupigułkowy pasek stanu Monitor/Sesja oraz stopka ze stanem sesji,
monitora i PresentMon plus `MIT · wersja`. Stopka szyny linkuje autora (`9Erza`), `DobryPC.pl`,
wsparcie projektu i O programie/licencje.

- **Panel główny** (EN: Dashboard): hero z główną akcją, 4 klikalne karty metryk (Monitor,
  Sesja, Benchmarki z uczciwym stanem PresentMon, Biblioteka), podgląd procesów Top-CPU
  i 3 ostatnie wpisy dziennika. Akcje kart nawigują; Szybki start jest niedostępny, gdy
  benchmark jest aktywny lub zarezerwowany.
- **Biblioteka**: jeden główny przycisk skanowania z podziałem (wszystkie/źródła launcherów),
  segment źródeł, pasek gotowości X/Y, wyszukiwarka/filtr i wirtualizowana lista; pusty stan
  oferuje Skanuj / Dodaj folder / Dodaj exe.
- **Sesja**: 4-krokowy kreator (konfiguracja → podgląd → start → przywróć) z przyklejonym
  przyciskiem Przywróć.
- **Benchmarki**: maszyna stanów gra → przyczyna silnika (mówi *która* przyczyna blokuje pomiar:
  brak gry, brak serwisu PresentMon, brak uprawnień, nieustawiony skrót) → konfiguracja →
  start; Historia/Porównanie z eksportem CSV/Markdown przez okno zapisu zgłaszające błędy zapisu.
- **Procesy**: 7 kolumn plus kolumna reguły (`brak` / nazwa profilu), sortowanie/filtr, detale
  wiersza z CPU Sets/Affinity/priorytetem i presetami topologii; wirtualizacja zachowana;
  wiersze Riot i aktywnego benchmarku są pomijane z podanym powodem. Mutacje najpierw
  ponownie potwierdzają tożsamość procesu.
- **Profile**: tworzenie jako pierwsze, 0–50 wierszy z wyszukiwaniem, przełącznik wł./wył.
  na wiersz, ostrzeżenia o konfliktach, walidacja i jawna zgoda na Auto Apply.
- **Sprzęt**: stany WYŁ. / WŁ. (lease) / NIEDOSTĘPNE, opcjonalne czujniki magazynu
  (`EnableStorageSensors`) i karta timer resolution tylko do odczytu.
- **Dziennik**: filtr severity + wyszukiwarka, widoczny limit 200 z N oraz akcja Wyczyść
  wymagająca potwierdzenia.
- **Ustawienia**: 6 kategorii (Ogólne, Język, Start i okno, Companion i sieć, Benchmarki,
  Zaawansowane i diagnostyka). **Companion i sieć** zawiera panel diagnostyki sieci tylko
  do odczytu: karty, bieżące resolvery (automatyczne/DHCP lub statyczne), popularni
  dostawcy (Cloudflare, Google Public DNS, Quad9, OpenDNS) z porównaniem opóźnień
  IPv4/IPv6/DoH. Panel wprost mówi, że niczego nie stosuje — kodu mutacji nie ma z projektu.
  Restart oferuje zwykły restart kooperacyjny (najpierw następca, ustawienia zachowane)
  i restart jako administrator, oba z informacją o wyniku/odmowie.

Język: **Ustawienia > Język** (lub przełącznik w zasobniku/Ustawieniach) zapisuje
`Language: pl|en` w `settings.json` (domyślnie `en`). Pełny słownik desktopowy to 1005 kluczy
na język bez luk; Companion ma własny, testowany parytetowo słownik. Pliki eksportu zawsze
używają formatu invariant i znaczników czasu UTC niezależnie od języka UI.

---

## Serwer LAN Companion i Przydział CPU dla gry

1. W aplikacji desktopowej FrameHub przejdź do **Ustawienia > Companion i sieć** i upewnij się, że serwer jest włączony.
2. Otwórz okno parowania, aby wyświetlić adres w sieci lokalnej i kod QR.
3. Na smartfonie (połączonym z tą samą siecią Wi-Fi/LAN) zeskanuj kod QR lub wpisz adres URL w przeglądarce.
4. Przeprowadź jednorazowe parowanie, aby utworzyć bezpieczny token sesji.
5. W interfejsie mobilnym Companion:
   - **Telemetria**: Przeglądaj telemetrię CPU, GPU, RAM i czasu klatek strumieniowaną w czasie rzeczywistym przez WebSocket.
   - **Biblioteka i Szybki start**: Przeglądaj wykryte gry i uruchamiaj je zdalnie.
   - **Przydział CPU dla gry**: Przy aktywnej grze tymczasowo dostosuj przydział rdzeni procesora przy użyciu presetów topologii (Wszystkie rdzenie, Tylko fizyczne, Wyczyść) lub CPU Sets.
   - **Optymalizacja sesji**: Monitoruj i kontroluj stan wstrzymania aplikacji w tle.
   - **Benchmarki**: Śledź postęp testów lub uruchamiaj pomiary zdalnie.
6. Companion działa jako mobilna powłoka aplikacji: dolna nawigacja pozostaje stała podczas przewijania treści, działa w mobilnym Safari/Chrome, a stronę można dodać do ekranu głównego z ikoną FrameHub.

---

## Sprawdzanie aktualizacji

Po włączeniu **Ustawienia > Sprawdzaj aktualizacje przy starcie** FrameHub sprawdza dostępność nowej wersji raz na uruchomienie aplikacji — ale dopiero wtedy, gdy okno główne zostanie faktycznie pokazane. Nie przerywa cichego startu do zasobnika/zminimalizowanego, milczy, gdy wersja jest aktualna lub brakuje internetu, a przy nowej wersji wyświetla okno w stylu FrameHub z bezpośrednim odnośnikiem do strony wydania. Przycisk **Ustawienia > Sprawdź teraz** wykonuje ręczne sprawdzenie w dowolnym momencie i używa tego samego okna; instalatory nie są pobierane ani uruchamiane automatycznie.

---

## Benchmarki

1. Dodaj lub przeskanuj grę w **Bibliotece gier**.
2. Uruchom grę.
3. Otwórz **Benchmarki** albo wybierz grę i użyj przycisku **Benchmark** w jej szczegółach.
4. Wybierz 30, 60, 120 sekund lub własny czas 10–600 sekund oraz opcjonalne odliczanie.
5. Rozpocznij test, wróć do gry i odtwórz zamierzoną scenę lub obciążenie.
6. Sprawdź średnie FPS, 1% Low, 0,1% Low, P95/P99 czasu klatki, metadane środowiska (system, CPU, GPU, RAM, ekran), ostrzeżenia jakości i wykres czasu klatek.
7. Powtórz pomiar przy tych samych ustawieniach gry, scenie i warunkach systemowych.
8. W zakładce **Porównanie** wybierz dwie ukończone sesje tej samej gry.

Globalny skrót benchmarku jest domyślnie nieprzypisany i wyłączony. Skonfiguruj go w sekcji **Ustawienia > Skrót benchmarku**: wybierz **Zmień / nagraj skrót**, a następnie naciśnij obsługiwaną kombinację z modyfikatorem (albo F8–F12). Ten sam skrót rozpoczyna i zatrzymuje pomiar, gdy FrameHub ma fokus, jest zminimalizowany, działa w zasobniku albo gdy fokus ma gra. Start skrótem jest natychmiastowy; zwykły przycisk nadal używa ustawionego odliczania, którego skrót nie zmienia. FrameHub korzysta z interfejsu Windows `RegisterHotKey` i nie instaluje haka klawiatury.

Skrót używa wybranej, uruchomionej gry z biblioteki. Jeśli żadnej nie wybrano, FrameHub rozpocznie pomiar tylko wtedy, gdy działa dokładnie jedna gra dostępna do benchmarku. Przy kilku uruchomionych grach program zapisuje zdarzenie i prosi o wybranie celu w FrameHub zamiast zgadywać.

Średnie FPS opisuje przepustowość, natomiast 1%/0,1% Low i wysokie percentyle czasu klatki pomagają zauważyć okresowe przycięcia i problemy z płynnością. Same nie wskazują ich przyczyny. Pomiar zapisuje aktywny profil CPU, stan Optymalizacji sesji oraz metadane środowiska, ale nie zmienia procesów gry. **Historia** rozpoznaje lokalne sesje schema-v1 utworzone przez aplikację i narzędzie deweloperskie, pozwala otworzyć folder oraz trwale usunąć jedną zweryfikowaną sesję.

Dane pozostają w `%LOCALAPPDATA%\FrameHub\Benchmarks`; ustawienia i logi w `%APPDATA%\FrameHub`. Wyniki nie są wysyłane do chmury. Gdy silnik benchmarków jest niedostępny, widok Benchmarki nazywa konkretną przyczynę (nie wybrano/uruchomiono gry, brak serwisu PresentMon, brak uprawnień albo nieustawiony skrót) zamiast ogólnej podpowiedzi o reinstalacji. Jeden instalator Setup zawiera wymagany PresentMon — naprawiaj lub instaluj ponownie tylko wtedy, gdy panel silnika zgłasza brak samego serwisu. Sam FrameHub nie wstrzykuje kodu do gier ani nie odczytuje pamięci gry; zgodność z konkretną grą lub anti-cheat może się różnić.
