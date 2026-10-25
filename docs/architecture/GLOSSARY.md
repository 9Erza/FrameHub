# Glossary — canonical EN/PL UI terms

Status: **canonical**. The Desktop dictionary is `FrameHub.App/Services/LocalizationService.cs`
(single file, `English` + `Polish` maps); the Companion dictionary is
`FrameHub.Companion/wwwroot/js/i18n.js`. Both sides share the term choices below;
wording may differ only where grammar requires it.

Decided terms (do not re-translate ad hoc):

| EN (key prefix) | PL | Note / decision |
|---|---|---|
| Dashboard (`Nav.Dashboard`, `Dashboard.Title`) | Panel główny | Decided 2026-10-25 (UI overhaul C). `Pulpit` rejected: collision with the Windows desktop. |
| Games & Optimization | Gry i optymalizacja | |
| Session Optimization | Optymalizacja sesji | |
| Benchmarks | Benchmarki | |
| Processes & CPU | Procesy i CPU | |
| Profiles & Rules | Profile i reguły | |
| Hardware Monitor | Monitor sprzętu | |
| Logs (`Logs.Title`) | Dziennik | Decided: `Dziennik`, not `Logi`. Runtime activity producers use `Aktywność`. |
| Settings | Ustawienia | Six categories: General / Language / Startup & Window / Companion & Network / Benchmarks / Advanced & Diagnostics (PL: Ogólne / Język / Start i okno / Companion i sieć / Benchmarki / Zaawansowane i diagnostyka). |
| Quick Start | Szybki start | Dashboard hero action; unavailable while a benchmark is active/reserved. |
| Scan / Refresh / Apply now / Save profile | Skanuj / Odśwież / Zastosuj / Zapisz profil | |
| Restore | Przywróć | Session restore is always reachable (sticky in the Session flow). |
| No active session | Brak aktywnej sesji | |
| Profile monitor active | Monitor profili aktywny | |
| Sensors closed | Czujniki wyłączone | Hardware Monitor zero-consumer state. |
| Support the project | Wesprzyj projekt | Footer/About entry point. `Supporter` is a proper name and stays untranslated. |
| Network diagnostics (read-only) | Diagnostyka sieci (tylko odczyt) | Read-only panel: preview and comparison only, no DHCP/adapter/registry changes. |
| Restart FrameHub / Restart as administrator | Uruchom ponownie FrameHub / Uruchom ponownie jako administrator | Cooperative successor-first restart; admin variant reports success/timeout/UAC refusal. |

Format contracts (do not localize away):

- UI numbers, percents and times follow the active culture (e.g. PL `1,5%`, system clock format).
- Benchmark CSV/Markdown export is always `InvariantCulture` + UTC `yyyy-MM-ddTHH:mm:ssZ`
  (`BenchmarkComparisonExportService`; deterministic, byte-identical for identical input).
- OS-provided tokens (e.g. adapter states such as `Down`, `NotPresent`) are shown verbatim,
  even in the PL UI; the static/dynamic source label next to them is localized.

Related: [Feature map](FEATURE-MAP.md), [Service catalog](SERVICE-CATALOG.md),
[Edition integration](../../supporter/EDITION-INTEGRATION.md) (Supporter PoC rules).
