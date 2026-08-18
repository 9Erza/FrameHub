# Instrukcja użytkownika FrameHub

[English](USER_GUIDE.md)

Po pierwszym uruchomieniu przeskanuj **Gry i optymalizacja** (Bibliotekę gier). Dodaj folder lub plik EXE ręcznie, jeśli launcher nie wykrył gry. Zapis profilu utrwala regułę; **Optymalizuj/Zastosuj** próbuje zmienić pasujące, uruchomione procesy. Możesz także użyć funkcji **Szybki start** na Pulpicie, aby uruchomić grę i zastosować Optymalizację sesji jednym kliknięciem.

W **Optymalizacji sesji** wybierasz procesy w tle do wstrzymania, sprawdzasz podgląd i uruchamiasz sesję ręcznie albo automatycznie. FrameHub zapisuje wstrzymane procesy i wznawia je bezpiecznie przy zakończeniu lub odzyskiwaniu — nie kończy ich działania.

Moduł **Procesy i CPU** służy do bezpośredniego sterowania działającymi procesami (CPU Sets, Processor Affinity, priorytet). **Profile i reguły** zarządzają trwałymi ustawieniami stosowanymi automatycznie przez monitor profili.

CS2 należy zamknąć przed edycją konfiguracji; tworzone są kopie zapasowe. Monitor sprzętu jest opcjonalny i działa na żądanie (lease-controlled). Zakładka **Ustawienia** obejmuje zasobnik systemowy, język, logi, autostart, parowanie z serwerem Companion i opcjonalne uprawnienia administratora. Dane i logi: `%APPDATA%\FrameHub`.

Gdy istnieje jeden prawidłowy folder Steam userdata CS2, FrameHub wybiera go automatycznie. Przy kilku folderach wybierz numeryczne ID userdata w szczegółach CS2. Do tego czasu FrameHub blokuje odczyt edytowalnej konfiguracji i wszystkie zapisy CS2. Nowe kopie zapasowe są rozdzielane według wybranej ścieżki userdata.

---

## Serwer LAN Companion i Przydział CPU dla gry

1. W aplikacji desktopowej FrameHub przejdź do **Ustawienia > Companion** i upewnij się, że serwer jest włączony.
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

Dane pozostają w `%LOCALAPPDATA%\FrameHub\Benchmarks`; ustawienia i logi w `%APPDATA%\FrameHub`. Wyniki nie są wysyłane do chmury. Gdy silnik benchmarków jest niedostępny, napraw lub zainstaluj ponownie FrameHub—jeden instalator Setup zawiera wymagany PresentMon. Sam FrameHub nie wstrzykuje kodu do gier ani nie odczytuje pamięci gry; zgodność z konkretną grą lub anti-cheat może się różnić.
