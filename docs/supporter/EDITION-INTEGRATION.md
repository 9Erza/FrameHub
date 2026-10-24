# FrameHub Supporter — Edition Integration (PoC)

Status: **proof of concept, local only**. No private repository exists, no
accounts were created, no payment integration was built. This document plus
`supporter/edition.manifest.json`, the `SupporterEditionTests` suite and the
`supporter-compat` CI job are the complete PoC the directive asks for.

## 1. Rules (binding)

1. **Public `9Erza/FrameHub` (MIT) is the single source of truth** for all
   shared code: Core, App shell, every existing feature (D1–D13, C1–C8, N1–N3),
   CSV/Markdown export, tests, localization and this doc.
2. **FrameHub is and stays complete and free.** FrameHub Supporter = FrameHub
   + NEW quality-of-life additions only (see §3). No paywall, no DRM, no user
   accounts, no license server, no updater changes, no feature gating.
3. **Extension-only separation.** Anything Supporter-specific lives OUTSIDE the
   public repo: a future private `FrameHub-Supporter` repo holds ONLY the
   extension modules, the build/integration glue and credentials (none exist).
   It never clones the public code; it references it by **pinned public SHA**
   (see `supporter/edition.manifest.json`).
4. **Auto-adoption with tests.** When the public repo advances, the Supporter
   build adopts the new SHA only after its compatibility suite passes against
   that exact SHA. Two copies of shared code must never exist.
5. **Private-only code never ships in public artifacts.** Enforced by test
   (`SupporterEditionTests.PublicArtifact_ExcludesPrivateOnlyCode`).
6. **Licenses.** Base edition stays MIT with `LICENSE` + `docs/THIRD-PARTY-NOTICES.md`
   intact. Future Supporter extension modules may carry their own license only
   after a rights/dependency audit — not started, no code written.

## 2. Reproducible mechanism (PoC)

```
public 9Erza/FrameHub @ <pinned-sha>   (MIT, auditable, tagged release)
        │ pinned SHA in supporter/edition.manifest.json
        ▼
private FrameHub-Supporter/            (does NOT exist yet — see §5)
  ├─ extensions/        NEW modules only (Benchmark Lab, …)
  ├─ integration/       build glue: checkout public SHA, build, run compat suite
  └─ releases/          Supporter binaries, built FROM the pinned public SHA
```

Adoption flow: public fix lands → manifest proposes new SHA → compat suite
(manifest schema + public tests + extension tests) runs against it → green
adopts, red blocks. No manual copy-paste of shared code, ever.

## 3. Feature ownership matrix (from Research §10)

PUBLIC (MIT, this repo) — everything that exists today:

- All Desktop flows D1–D13 (dashboard, library/scan, session + Restore,
  benchmarks capture/history/compare, processes/CPU, profiles, hardware,
  logs, settings, tray, restart, updates).
- All Companion flows C1–C8 (home, telemetry WS, benchmarks, library,
  background apps, session CPU, pairing/scopes, mobile shell).
- Network N1–N3 (cooperative restart, read-only DNS diagnostics,
  CSV/Markdown export with InvariantCulture/UTC contract).
- Localization, accessibility, installer, docs.

PRIVATE extension candidates (future, NOT implemented, NOT promised in UI):

- Benchmark Lab (trends, multi-session compare, deviation detection).
- Historical telemetry dashboards, profile versioning, per-game automations,
  layout personalization, extra Companion views.

The About dialog states only that Supporter editions fund development; it
lists no future features as available.

## 4. Support platforms (research snapshot, no accounts)

- **Buy Me a Coffee** (first candidate): ~5% platform fee + Stripe processing,
  weekly/monthly payouts. Already linked as the Support URL (pre-existing).
- **Patreon**: new creators after 2026-08-04 ≈ 10% platform fee (+ payment
  processing/payout fees in PLN).
- **GitHub Sponsors**: retrieval BLOCKED (docs 404 + extractor failure) —
  no terms quoted from memory; operator-side verification required.
- Formalities (business/tax on payouts): needs an accountant, not decided.

Zero integrations were created in this round.

## 5. Private repo status: BLOCKER (explicit, no guessing)

The private `FrameHub-Supporter` repo was NOT created: no explicit GitHub
permission was verified on this installation, and the directive forbids
guessing, overwriting an existing private repo, or cloning the app into it.

To unblock, the operator provides ONE of:

1. explicit permission + proof that the GitHub identity may create repos, then
   the skeleton is created EMPTY and read back as `private=true` via the API
   BEFORE any push; or
2. confirmation the repo already exists and may be used (visibility readback
   first, never overwrite).

Until then, this PoC (manifest + tests + CI) is the complete deliverable.
