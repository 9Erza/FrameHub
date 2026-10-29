# W2 — audyt lokalizacji PL/EN

Zakres przeglądu kodu: widoki XAML, ViewModels i zasoby Dashboardu, Biblioteki, Sesji, Procesów i CPU, Sprzętu, Benchmarków, Profili, Dziennika, Ustawień oraz dialogów. Sprawdzono powiązania etykiet, menu, tooltipów, list i komunikatów, użycia `ToString()` oraz odświeżanie przez `ShellViewModel.RefreshTexts`. Przeszukanie literałów kluczy `T(...)` nie wykazało brakujących kluczy; słowniki PL i EN mają taki sam zestaw kluczy. Istniejące testy sprawdzają parzystość, wartości i formatowanie.

Potwierdzone poprawki W2: typy pozycji Biblioteki są mapowane na teksty PL/EN bez zmiany enumów; opcje skoku CS2 i wartości ustawień odświeżają się po zmianie języka; poziomy ryzyka CS2 i poziomy Dziennika są lokalizowane bez zmiany identyfikatorów filtrowania; nazwy stanu gry, timera i DNS są czytelniejsze. Nazwy technologii, identyfikatory, ścieżki, diagnostyczne wyjątki i formaty trwałych danych pozostają techniczne.

Wyniki analizy kodu nie są certyfikatem każdego dynamicznego komunikatu: nie uruchomiono wszystkich ścieżek błędów, dialogów ani wszystkich wartości danych. Przełączenie PL/EN w działającym GUI, screenreader i dopasowanie tekstów przy różnych DPI pozostają do ręcznego odbioru.
