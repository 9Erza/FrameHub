# Instrukcja użytkownika FrameHub

[English](USER_GUIDE.md)

Po pierwszym uruchomieniu przeskanuj **Gry i optymalizacja** (Bibliotekę gier). Dodaj folder lub plik EXE ręcznie, jeśli launcher nie wykrył gry. Zapis profilu utrwala regułę; **Optymalizuj/Zastosuj** próbuje zmienić pasujące, uruchomione procesy.

W **Optymalizacji sesji** wybierasz procesy do wstrzymania, sprawdzasz podgląd i uruchamiasz sesję ręcznie albo automatycznie. FrameHub zapisuje wstrzymane procesy i wznawia je przy przywracaniu — nie kończy ich.

**Procesy i CPU** służą do sterowania teraz, a **Profile i reguły** do zapisanych ustawień i monitora profili. CS2 należy zamknąć przed edycją konfiguracji; tworzone są kopie zapasowe. Monitor sprzętu jest opcjonalny i wyłączony po starcie. Dane i logi: `%APPDATA%\FrameHub`.

Gdy istnieje jeden prawidłowy folder Steam userdata CS2, FrameHub wybiera go automatycznie. Przy kilku folderach wybierz numeryczne ID userdata w szczegółach CS2. Do tego czasu FrameHub blokuje odczyt edytowalnej konfiguracji i wszystkie zapisy CS2. Nowe kopie zapasowe są rozdzielane według wybranej ścieżki userdata.
## Benchmarki (v0.6.0)

1. Dodaj lub przeskanuj grę w **Bibliotece gier**.
2. Uruchom grę.
3. Otwórz **Benchmarki** albo wybierz grę i użyj przycisku **Benchmark** w jej szczegółach.
4. Wybierz 30, 60, 120 sekund lub własny czas 10–600 sekund oraz opcjonalne odliczanie.
5. Rozpocznij test, wróć do gry i odtwórz zamierzoną scenę lub obciążenie.
6. Sprawdź średnie FPS, 1% Low, 0,1% Low, P95/P99 czasu klatki, ostrzeżenia jakości i wykres czasu klatek.
7. Powtórz pomiar przy tych samych ustawieniach gry, scenie i warunkach systemowych.
8. W zakładce **Porównanie** wybierz dwie ukończone sesje tej samej gry.

Globalny skrót benchmarku jest domyślnie nieprzypisany i wyłączony. Skonfiguruj go w sekcji **Ustawienia > Skrót benchmarku**: wybierz **Zmień / nagraj skrót**, a następnie naciśnij obsługiwaną kombinację z modyfikatorem (albo F8–F12). Ten sam skrót rozpoczyna i zatrzymuje pomiar, gdy FrameHub ma fokus, jest zminimalizowany, działa w zasobniku albo gdy fokus ma gra. Start skrótem jest natychmiastowy; zwykły przycisk nadal używa ustawionego odliczania, którego skrót nie zmienia. FrameHub korzysta z interfejsu Windows `RegisterHotKey` i nie instaluje haka klawiatury.

Skrót używa wybranej, uruchomionej gry z biblioteki. Jeśli żadnej nie wybrano, FrameHub rozpocznie pomiar tylko wtedy, gdy działa dokładnie jedna gra dostępna do benchmarku. Przy kilku uruchomionych grach program zapisuje zdarzenie i prosi o wybranie celu w FrameHub zamiast zgadywać.

Średnie FPS opisuje przepustowość, natomiast 1%/0,1% Low i wysokie percentyle czasu klatki pomagają zauważyć okresowe przycięcia i problemy z płynnością. Same nie wskazują ich przyczyny. Pomiar zapisuje aktywny profil CPU i stan Optymalizacji sesji, ale ich nie zmienia. **Historia** rozpoznaje lokalne sesje schema-v1 utworzone przez aplikację i narzędzie deweloperskie, pozwala otworzyć folder oraz trwale usunąć jedną zweryfikowaną sesję.

Dane pozostają w `%LOCALAPPDATA%\FrameHub\Benchmarks`; ustawienia i logi w `%APPDATA%\FrameHub`. Wyniki nie są wysyłane. Gdy silnik benchmarków jest niedostępny, napraw lub zainstaluj ponownie FrameHub—jeden instalator Setup zawiera wymagany PresentMon. Sam FrameHub nie wstrzykuje kodu do gier ani nie odczytuje pamięci gry; zgodność z konkretną grą lub anti-cheat może się różnić.
