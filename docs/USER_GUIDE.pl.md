# Instrukcja użytkownika FrameHub

[English](USER_GUIDE.md)

Po pierwszym uruchomieniu przeskanuj **Bibliotekę gier**. Dodaj folder lub plik EXE ręcznie, jeśli launcher nie wykrył gry. Zapis profilu utrwala regułę; **Optymalizuj/Zastosuj** próbuje zmienić pasujące, uruchomione procesy.

W **Optymalizacji sesji** wybierasz procesy do wstrzymania, sprawdzasz podgląd i uruchamiasz sesję ręcznie albo automatycznie. FrameHub zapisuje wstrzymane procesy i wznawia je przy przywracaniu — nie kończy ich.

**Procesy i CPU** służą do sterowania teraz, a **Profile i reguły** do zapisanych ustawień i monitora profili. CS2 należy zamknąć przed edycją konfiguracji; tworzone są kopie zapasowe. Monitor sprzętu jest opcjonalny i wyłączony po starcie. Dane i logi: `%APPDATA%\FrameHub`.

Gdy istnieje jeden prawidłowy folder Steam userdata CS2, FrameHub wybiera go automatycznie. Przy kilku folderach wybierz numeryczne ID userdata w szczegółach CS2. Do tego czasu FrameHub blokuje odczyt edytowalnej konfiguracji i wszystkie zapisy CS2. Nowe kopie zapasowe są rozdzielane według wybranej ścieżki userdata.
