# FrameHub NOC — zestawienie końcowe (2026-10-24, po polsku)

Jedyny dokument podsumowujący nocną operację. Szczegóły w raportach źródłowych
(odnośniki w §8). Liczby i wyniki pochodzą z istniejących dowodów (raporty QA /
Researchera / Baseline); w ramach tej karty niczego nie uruchamiano ponownie.
Roadmapa to plan — nie wdrożenie; historyczne pomiary to historia — nie nowe wyniki.

## 1. Werdykt i gałąź

- **QA: PASS WITH WARNINGS** (niezależne review na istniejących commitach; akceptacja
  administracyjna, nie natywny PASS workera Kanban — review worker uległ awarii).
- **Otwarte: 1× P2** — test `LateCapturingSnapshot_…` może przejść fałszywie (vacuous PASS)
  zależnie od kolejności testów; wymaga jednej wąskiej rundy (kontrola pozytywna injection).
  Do tego 4× P3 higieniczne. **Przed mergem: wzmocnić test (P2).**
- **Zakazane operacje wykonane: zero** — brak push / merge / PR / deploy / publikacji / aktualizacji instalacji.
- Worktree: `C:\1_eryk_prywatne\Projekty\FrameHub-noc-t_2e7bd27e`
- Branch: `noc/t_2e7bd27e-0c6efc2` (bez upstream, tylko lokalnie)
- Baza: `0c6efc24ba556161c26385c6607609c307e4767e` (`main`, czysty)
- HEAD: `2bd22cb5d5994605594d8b3f719ab888bad44013`
- Commity (3, zachować wszystkie): `e70e967`, `d9bfc39`, `2bd22cb`
- Główne repo: nietknięte (`main` @ `0c6efc24`, status czysty).

## 2. Zmienione pliki (diff BASE..HEAD: 5 plików, +151 / −4)

| Plik | Zmiana |
|---|---|
| `FrameHub.App/ViewModels/BenchmarkViewModel.cs` (+17) | Bramki odrzucające spóźnione snapshoty koordynatora (`:275-276`) |
| `FrameHub.App/ViewModels/DashboardViewModel.cs` (+22/−4) | Nazwane handlery + idempotentny `IDisposable.Dispose` odpinający 4 zdarzenia |
| `FrameHub.App/ViewModels/ShellViewModel.cs` (+1) | `Dispose()` dashboardu jako pierwszy |
| `FrameHub.Tests/BenchmarkUiCoreTests.cs` (+69) | Deterministyczny test `LateCapturingSnapshot_…` + 25× tripwire |
| `FrameHub.Tests/GamingQuickActionServiceTests.cs` (+42) | 2 testy subskrypcji/unsubskrypcji dashboardu |
| Dokumentacja (ta karta, pliki poniżej + 4 minimalne edycje kanoniczne) | `docs/audits/2026-10-24-noc-*.md`, banner GameData, tytuł BENCHMARKING, odnośnik RUNTIME→BACKGROUND-WORK, adnotacje ROADMAP |

## 3. Realne poprawki (co udowodniono, czym)

1. **Wyścig benchmark UI (flake `Failed`→`Capturing`) — NAPRAWIONY i zweryfikowany.**
   Przyczyna: asynchroniczny drain powiadomień `BenchmarkCaptureCoordinator` (`:515-534, :558-590`)
   + bezwarunkowa gałąź `Capturing` nadpisywała lokalnie ustawiony stan terminalny.
   Dowód negatywny QA: deterministyczny test pada na kodzie BASE 3/3 i na `e70e967`
   z sygnaturą bazowego flake'a (`Oczekiwana:<Failed>. Rzeczywista:<Capturing>`), na HEAD zielony.
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
| `FrameHub.Tests` (Desktop) | **PASS** | **655/655** ×3 (w tym 1× pod obciążeniem z suitą Companion); BASE miał 651 |
| `FrameHub.Companion.Tests` | **PASS** | **140/140** ×3 |
| `dotnet test FrameHub.slnx` (parzystość z CI) | **PASS** | 655 + 140, 0 niepowodzeń |
| `node --check` (7 plików `wwwroot/js`) | **PASS** | 7/7 |
| `git diff --check` | **PASS** | clean |
| Nowy test `LateCapturingSnapshot_…` (izolacja) | **PASS** | 10/10; pod obciążeniem 6× procesów ×3 rundy: 18/18 |
| Kontrole negatywne na BASE / `e70e967` | **FAIL zgodnie z oczekiwaniami** (dowód, że test wykrywa defekt) | exit 1, sygnatura bazowego flake'a |
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
Logi: `fh-baseline/*.log` + `SHA256SUMS.txt` (załączniki karty `t_602bd1b8).

## 5. Decyzje: wykonane i odłożone (7 obszarów specyfikacji)

- ✅ Stabilność/P0/P1, benchmark UI, CPU/RAM/lifecycle (dispose dashboardu) — wykonane powyżej.
- ❌ PresentMon v2.6.0 — **odłożony**: brak serwisu/DLL na maszynie; pin bez walidacji ABI to spekulacja.
- ❌ GameData — **odłożony z flagą P3**: dormant potwierdzony, zero konsumentów, nic nie kasowane.
- ❌ „Retired backends" — **rozstrzygnięty negatywnie**: strażnik CLI, nic do usuwania.
- ❌ Funkcje / UI / Web / nowe funkcje — **bez zmian** (JS bez harnessu testowego nie tykany;
  Riot/sesja/auth bez zastrzeżeń QA nie tykane).

## 6. Znane problemy i ryzyka

- **P2 (otwarty, przed mergem):** vacuous PASS testu `LateCapturingSnapshot_…` (§4 macierz; poprawka testowa w 1 wąskiej rundzie).
- P3: tripwire 25× nierozstrzygający; brak testu widoczności countdownu; brak `Dispose` w teście konstruktora;
  flagi bez `volatile` (bezpieczne w produkcji); ten sam wzorzec wycieku lambdą w `HardwareViewModel`,
  `ProfilesViewModel`, `ShellViewModel`, `MainWindow` (sprzed diffa — osobna karta).
- F-2: brak izolacji katalogu danych (`AppPaths.cs:10-13`) zanieczyszcza logi i pomiary.
- F-3: 3003 LOC JS bez testów logiki.
- PresentMon nieobecny na maszynie deweloperskiej → ścieżka „engine unavailable" to wspierana
  degradacja, nie awaria (opisana w BENCHMARKING / znanych ograniczeniach).

## 7. Dalsze decyzje (proponowana kolejność, z kryteriami)

1. **Wzmocnić test P2** (kontrola pozytywna injection) → 10 kolejnych pełnych przebiegów suity zielonych,
   w tym pierwszy po buildzie → merge brancha nocnego.
2. **Pilot PresentMon v2.6.0** tylko po bramce ABI: `PresentMonIntrospectionTests` +
   `BenchmarkHarness --presentmon-api-dll <2.6.0>` → pin (`FrameHub.iss:8`, ps1) + SHA-256 → regresja QA.
3. **GameData**: osobna recenzowana decyzja adopt-vs-remove (P3).
4. **Izolacja katalogu danych** (F-2) i **harness testów JS** (F-3) — osobne karty, po pomiarze/uzasadnieniu.
5. **Weryfikacja podpisu aktualizacji** (P2, threat-model) i pozostałe wycieki lambd — osobne karty.
6. Wariant C (własny backend ETW) **odrzucony na stałe w tej formie**; D tylko jako daleki research.

## 8. Odnośniki do raportów szczegółowych

- Niezależne QA review (ten NOC): `C:\Users\erykz\AI\FrameHub_QA_Recovery_20261024-074034\QA_REVIEW.md`
- Baseline QA + logi + sumy: profil QA `cache/scratch/fh-baseline/` (załączniki karty `t_602bd1b8`)
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
