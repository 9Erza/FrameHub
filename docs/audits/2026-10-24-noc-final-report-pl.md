# FrameHub NOC — zestawienie końcowe (2026-10-24, po polsku)

Jedyny dokument podsumowujący nocną operację. Szczegóły w raportach źródłowych
(odnośniki w §8). Liczby i wyniki pochodzą z istniejących dowodów (raporty QA /
Researchera / Baseline); w ramach tej karty niczego nie uruchamiano ponownie.
Roadmapa to plan — nie wdrożenie; historyczne pomiary to historia — nie nowe wyniki.

## 1. Werdykt i gałąź

- **QA: PASS WITH WARNINGS** (niezależne review na istniejących commitach NOC; akceptacja
  administracyjna, nie natywny PASS workera Kanban — review worker uległ awarii).
  Etap-0 (ta karta `t_eb7d4587`): P2 i F-DOC-1…8 naprawione (poniżej + raport etap-0);
  niezależne same-card QA tej karty w toku.
- **P2 (był otwarty, naprawiony w etapie-0):** test `LateCapturingSnapshot_…` mógł przejść
  fałszywie (vacuous PASS) zależnie od kolejności testów. Fix `f53e91e`: kontrola
  pozytywna injection + fallback `action()` w `DispatchIfRequired` (odporność na martwy
  dispatcher). Kontrole negatywne na `e70e967` FAIL zgodnie z oczekiwaniami.
- **F-DOC-1…8 (naprawione w etapie-0):** 13 scopes (nie 16), 17 wierszy BACKGROUND-WORK
  (nie 18), przeformułowane zdanie o `textContent`, HEAD gałęzi vs HEAD kodu,
  prawdziwe załączniki na karcie `t_eb7d4587`, `BENCHMARKING.md:95`, `~1800` linii,
  `:257-258`. Szczegóły w raporcie etap-0.
- **Zakazane operacje wykonane: zero** — brak push / merge / PR / deploy / publikacji / aktualizacji instalacji.
- Worktree: `C:\1_eryk_prywatne\Projekty\FrameHub-noc-t_2e7bd27e`
- Branch: `noc/t_2e7bd27e-0c6efc2` (bez upstream, tylko lokalnie)
- Baza: `0c6efc24ba556161c26385c6607609c307e4767e` (`main`, czysty)
- HEAD kodu: `f53e91e` (5 commitów kodu, zachować wszystkie): `e70e967`, `d9bfc39`, `2bd22cb`, `0c6f9d3`, `f53e91e`
- HEAD gałęzi: niniejszy commit dokumentacyjny (tip; treść kodu = `f53e91e`)
- Główne repo: nietknięte (`main` @ `0c6efc24`, status czysty).

## 2. Zmienione pliki (diff BASE..HEAD kodu `0c6efc2..f53e91e`: 5 plików kodu, +233 / −4;
  pełny diff BASE..tip gałęzi: 13 plików + docs tej karty — liczby w raporcie etap-0)

| Plik | Zmiana |
|---|---|
| `FrameHub.App/ViewModels/BenchmarkViewModel.cs` (+28) | Bramki odrzucające spóźnione snapshoty koordynatora (`:275-276`) + fallback `action()` w `DispatchIfRequired` przy martwym dispatcherze |
| `FrameHub.App/ViewModels/DashboardViewModel.cs` (+22/−4) | Nazwane handlery + idempotentny `IDisposable.Dispose` odpinający 4 zdarzenia |
| `FrameHub.App/ViewModels/ShellViewModel.cs` (+1) | `Dispose()` dashboardu jako pierwszy |
| `FrameHub.Tests/BenchmarkUiCoreTests.cs` (+140) | Deterministyczny test `LateCapturingSnapshot_…` + kontrola pozytywna + test widoczności countdownu `InOrderWaitingSnapshot_…` + 25× tripwire |
| `FrameHub.Tests/GamingQuickActionServiceTests.cs` (+42) | 2 testy subskrypcji/unsubskrypcji dashboardu |
| Dokumentacja (ta karta, pliki poniżej + 4 minimalne edycje kanoniczne) | `docs/audits/2026-10-24-noc-*.md`, banner GameData, tytuł BENCHMARKING, odnośnik RUNTIME→BACKGROUND-WORK, adnotacje ROADMAP |

## 3. Realne poprawki (co udowodniono, czym)

1. **Wyścig benchmark UI (flake `Failed`→`Capturing`) — NAPRAWIONY i zweryfikowany.**
   Przyczyna: asynchroniczny drain powiadomień `BenchmarkCaptureCoordinator` (`:515-534, :558-590`)
   + bezwarunkowa gałąź `Capturing` nadpisywała lokalnie ustawiony stan terminalny.
   Dowód negatywny QA: deterministyczny test pada na kodzie BASE 3/3 i na `e70e967`
   z sygnaturą bazowego flake'a (`Oczekiwana:<Failed>. Rzeczywista:<Capturing>`), na HEAD zielony.
   Etap-0 (`t_eb7d4587`): kontrola pozytywna + test countdown FAIL na `e70e967`
   (`Expected <Waiting>, Actual <Idle>`), PASS na HEAD; fallback w `DispatchIfRequired`
   eliminuje zależność od kolejności testów.
   Dodatkowo sonda QA potwierdziła, że `e70e967` gasił podgląd countdownu, a `2bd22cb` go przywrócił.
2. **Wyciek subskrypcji `DashboardViewModel` (P2 Researchera) — NAPRAWIONY.**
   4 zdarzenia runtime/koordynatora, lambdy nie do odpięcia → nazwane handlery + `Dispose`,
   wołany z `ShellViewModel.Dispose` przed `_runtime.Dispose`; ścieżka produkcyjna
   `MainWindow.OnClosed → DataContext as IDisposable` potwierdzona w kodzie.
3. **Zero nowych właścicieli/timerów/pętli** — REUSE-FIRST czysty (`BenchmarkCaptureCoordinator`
   jedynym właścicielem lifecycle, `ProcessService` jedyną mechaniką natywną).

## 4. Mierzalne wyniki — macierz PASS / FAIL / NOT RUN

| Kontrola | Wynik | Exit / liczba |
|---|---|---|
| `dotnet build --configuration Release` | **PASS** | 0 ostrzeżeń, 0 błędów |
| `FrameHub.Tests` (Desktop) | **PASS** | **656/656** ×10 z rzędu (RUN1 bezpośrednio po buildzie; NOC miał 655/655 ×3, BASE 651; +1 test etap-0) |
| `FrameHub.Companion.Tests` | **PASS** | **140/140** |
| `dotnet test FrameHub.slnx` (parzystość z CI) | **PASS** | 656 + 140, 0 niepowodzeń |
| `node --check` (7 plików `wwwroot/js`) | **PASS** | 7/7 |
| `git diff --check` | **PASS** | clean |
| Nowy test `LateCapturingSnapshot_…` (+ kontrola pozytywna) | **PASS** | zielony w pełnej suicie; kontrola pozytywna `State == Capturing` na świeżym VM |
| Nowy test `InOrderWaitingSnapshot_…` (countdown) | **PASS** | zielony; `Waiting` + `RemainingCountdown > 0` w trakcie lokalnego await |
| Kontrole negatywne na `e70e967` | **FAIL zgodnie z oczekiwaniami** (dowód, że testy wykrywają defekt) | LateCapturing: `Oczekiwana:<Failed>. Rzeczywista:<Capturing>`; countdown: `Oczekiwana:<Waiting>. Rzeczywista:<Idle>` |
| Baseline (BASE, przed zmianami): build / Desktop run1 (flake) / Desktop ×3 / Companion / JS | **PASS z 1 FAIL** | run1: 650/651 (`BenchmarkUiCoreTests.cs:326`), potem 651/651 ×3; Companion 140/140 |
| Capture e2e PresentMon, live telemetry, preempcja | **NOT RUN** | usługa niezainstalowana na maszynie, brak gry |
| Mutacje live / ochrona Riot live | **NOT RUN** | zabronione (cudze procesy) |
| Companion HTTP/WS runtime | **NOT RUN** | `CompanionEnabled=false`, brak izolacji katalogu danych |
| Sensory sprzętowe | **NOT RUN** | `HardwareMonitorEnabled=false` |
| Cykle UI / DPI / motywy / shutdown przez tray-Exit | **NOT RUN** | brak automatyzacji UIA |
| Soak > 240 s, soak interaktywny, smoke WPF, instalator, CI na GitHubie | **NOT RUN** | ograniczenia środowiska; zero pusha |
| Pomiary CPU/RAM po zmianie (after) | **NOT RUN** | zmiana nie dotyczy hot path + ryzyko kontaminacji F-2 |

**Before (BASE, zmierzone przez baseline QA — nie historyczne, nie moje):**
start→okno 2812 ms i 1550 ms; idle WS 172,8 / 168,9 MB, Private 80,7 / 78,8 MB;
soak 240 s płaski (WS 169,6→171,2 MB, Private 82,4→79,9 MB, uchwyty 697→706, wątki 26→14);
CPU idle 0,020–0,029 %. Formuła obowiązująca: **„brak dowodów wycieku w 240 s;
horyzont za krótki, by wykluczyć wyciek"** — nie dokumentować jako „brak wycieku".
Warunki: Release z `bin`, bez instalacji, per-PID, `CloseToTray=true` (ukrycie do tray
to projekt, nie błąd), `CompanionEnabled=false`, `HardwareMonitorEnabled=false`, sesja console/ID 1.
Logi etap-0: katalog `C:/Users/erykz/AI/FrameHub_Etap0_20261024/logs/` (build + 10× Desktop
RUN1–RUN10, Companion, JS) + SHA-256; dołączone jako załączniki karty `t_eb7d4587`.
Logi baseline NOC: profil QA `cache/scratch/fh-baseline/` (załączniki nie były dołączone
do kart NOC — F-DOC-5; kopie dowodowe w pakiecie etap-0).

## 5. Decyzje: wykonane i odłożone (7 obszarów specyfikacji)

- ✅ Stabilność/P0/P1, benchmark UI, CPU/RAM/lifecycle (dispose dashboardu) — wykonane powyżej.
- ❌ PresentMon v2.6.0 — **odłożony**: brak serwisu/DLL na maszynie; pin bez walidacji ABI to spekulacja.
- ❌ GameData — **odłożony z flagą P3**: dormant potwierdzony, zero konsumentów, nic nie kasowane.
- ❌ „Retired backends" — **rozstrzygnięty negatywnie**: strażnik CLI, nic do usuwania.
- ❌ Funkcje / UI / Web / nowe funkcje — **bez zmian** (JS bez harnessu testowego nie tykany;
  Riot/sesja/auth bez zastrzeżeń QA nie tykane).

## 6. Znane problemy i ryzyka

- **P2 (naprawiony w etapie-0, `f53e91e`):** vacuous PASS wyeliminowany dwustronnie
  (kontrola pozytywna + fallback w `DispatchIfRequired`); bramka merge spełniona po QA tej karty.
- P3 pozostałe: tripwire 25× nierozstrzygający (dokumentacyjny, nie strażnik); brak `Dispose`
  w teście konstruktora; flagi bez `volatile` (bezpieczne w produkcji); ten sam wzorzec
  wycieku lambdą w `HardwareViewModel`, `ProfilesViewModel`, `ShellViewModel`,
  `MainWindow` (sprzed diffa — osobna karta).
- F-2: brak izolacji katalogu danych (`AppPaths.cs:10-13`) zanieczyszcza logi i pomiary.
- F-3: 3003 LOC JS bez testów logiki.
- PresentMon nieobecny na maszynie deweloperskiej → ścieżka „engine unavailable" to wspierana
  degradacja, nie awaria (opisana w BENCHMARKING / znanych ograniczeniach).

## 7. Dalsze decyzje (proponowana kolejność, z kryteriami)

1. **P2 i F-DOC-1…8 naprawione w etapie-0** (`f53e91e` + niniejszy commit docs) → po
  niezależnym QA tej karty (`t_eb7d4587`, reviewer `qa`) merge brancha nocnego.
2. **Pilot PresentMon v2.6.0** tylko po bramce ABI: `PresentMonIntrospectionTests` +
   `BenchmarkHarness --presentmon-api-dll <2.6.0>` → pin (`FrameHub.iss:8`, ps1) + SHA-256 → regresja QA.
3. **GameData**: osobna recenzowana decyzja adopt-vs-remove (P3).
4. **Izolacja katalogu danych** (F-2) i **harness testów JS** (F-3) — osobne karty, po pomiarze/uzasadnieniu.
5. **Weryfikacja podpisu aktualizacji** (P2, threat-model) i pozostałe wycieki lambd — osobne karty.
6. Wariant C (własny backend ETW) **odrzucony na stałe w tej formie**; D tylko jako daleki research.

## 8. Odnośniki do raportów szczegółowych

- Niezależne QA review (ten NOC): `C:\Users\erykz\AI\FrameHub_QA_Recovery_20261024-074034\QA_REVIEW.md`
- Baseline QA + logi + sumy: profil QA `cache/scratch/fh-baseline/` (materiał źródłowy;
  F-DOC-5: zdanie o „załącznikach karty `t_602bd1b8`" było nieprawdziwe — prawdziwe
  załączniki etap-0 są na karcie `t_eb7d4587`)
- Audyt Researchera + PresentMon A/B/C/D: profil Researchera `cache/scratch/FRAMEHUB-NOC-AUDIT-RESEARCHER.md`,
  `FRAMEHUB-PRESENTMON-ABCD.md` (karta `t_ddacbbba`; plik ABCD ucięty w zapisie — C5/C6 tylko z komentarzy karty)
- Audyt dokumentacji + szkielet inwentaryzacji: komentarz karty `t_c3f4f259`
- Nowe pliki tej karty (worktree, `docs/audits/`): inwentaryzacja funkcji, audyt techniczny,
  raport wydajności, niniejsze zestawienie
- Kanoniczne docs (aktualne po tej karcie): `README.md`, `docs/BENCHMARKING.md`, `docs/RUNTIME-OVERHEAD.md`,
  `docs/ROADMAP.md`, `docs/architecture/*` (zwłaszcza `BACKGROUND-WORK.md`, `FEATURE-MAP.md`)

## 9. Uwagi formalne

- Dokumentacja w tej karcie nie zmienia konfiguracji AI/Hermes ani niepowiązanych projektów.
- O REA wyłącznie: osobna prywatna karta `t_7cb9e99f`, nie zależność; żadnych prywatnych danych w repo.
- Wszystkie ustalenia sklasyfikowane: fakty zweryfikowane w kodzie (VERIFIED) vs dane przejęte
  z raportów (REPORTED) vs propozycje (PROPOSED) — szczegóły w plikach `docs/audits/`.
