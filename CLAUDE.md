# fa.reel — Reel

## Research question
Can a per-user, content-based classic-ML recommender trained on a user's full Trakt history (watched + ratings) beat a popularity baseline by ≥20% relative precision@10 on a time-split held-out slice of that user's own ratings — and can that engine carry a commercial multi-tenant SaaS?

## What this program is
A multi-tenant movies/TV recommender SaaS. Each user links a Trakt profile; Reel ingests everything ever watched plus ratings and in-progress shows, extracts rich taste features (LLM-derived tone/pacing/theme attributes, plot embeddings, cast/crew affinity, taste drift), and serves live, explainable recommendations restricted to titles the user has **not** finished (unwatched + currently-in-progress are eligible; fully watched is excluded). Live search and where-to-watch are first-class. Interesting now because LLM feature extraction makes content-based recommenders viable for a single user from day 1 — no cross-user data needed at launch.

## Stack & repo
Mandated stack: .NET 10 + EF Core + PostgreSQL (pgvector for embeddings); React 19 + Tailwind + shadcn/ui. ML: ML.NET FastTree in-process (pure managed — arm64-safe); ONNX MiniLM embeddings (local default; OpenRouter-compatible adapter behind `IEmbeddingProvider`); all LLM calls via OpenRouter (fa.startup config conventions + deterministic stub mode) — **extraction model `openai/gpt-5.4-mini` (founder-locked 2026-06-12)**. Data: Trakt API (history, ratings, watching), TMDB (catalog, search, metadata, watch providers). Standard FrostAura deploy pattern (GH Actions → Docker Hub multi-arch → Portainer → Cloudflared; `reel.frostaura.net`). **Repo: `github.com/frostaura/fa.reel`** (public, MIT) — solution `fa.reel.slnx` at root, hexagonal backend under `src/backend`, frontend under `src/frontend`. **Quality bar: Silver** (lint + build + tests pass, deployed) — CI enforces format, coverage ratchets, security audits, pgvector integration tests, Playwright smoke.

## Owner & key people
Dean Martin (founder). Dev/test Trakt profile: `deanmar09` (public).

## Current phase
`experiments running` (created 2026-06-12; M1 verified + **M2 edge proof PASSED 2026-06-12**: model precision@10 70% vs popularity baseline 50% = **40% relative lift** on the leakage-clean time split, iteration 1 of 3, reproducible, founder account, before embeddings/LLM attributes. The research question has its first affirmative answer; M3 MVP surface build underway. Full plan at `~/.claude/plans/plan-the-full-implementation-wiggly-koala.md`.)

## Milestones
1. **App skeleton** ✅ (2026-06-12) — mandated-stack repo: Trakt OAuth wireup (per-user config, `/auth/trakt/callback`), background sync into Postgres, tenant model. Founder ingest verified: 3,484 ratings exact, 3,091 watched titles, 113 in-progress.
2. **In-app ML pipeline + edge proof** ✅ (2026-06-12) — feature extraction → training → time-split evaluation as app jobs; v1 model beat the popularity baseline by **40% relative precision@10** (70% vs 50%, ≥20% required), leakage-clean, reproducible, surfaced on the in-app `/lab` metrics view. Iteration 1 of 3; cast/crew affinity features carried the edge.
3. **MVP surface** ✅ (2026-06-12) — per `docs/product-spec.md`: tonight's-picks hero + rows ✓, card anatomy + expanded modal ✓, reactions + Trakt write-back + managed "Reel — Up Next" list (created live, id 35111432) ✓, both search modes (typeahead live; semantic key-gated and graceful) ✓, where-to-watch ladder (direct/TMDB kinds + attribution) ✓, taste DNA dashboard ✓, live build-up onboarding with frost-melt reveal ✓. Exit checks verified live: filter airtightness (horror excluded → 0/35 in rebuilt feed), entitlement strip (Free = 3-pick shortlist server-side), providers resolve. 48 backend + 16 frontend unit + 6 e2e tests green.
4. **Multi-tenant** — code-side done (row-level isolation audited on real PG, entitlements enforced server-side, shared rate budget, Degraded surfaced); ≥5 external users pending prod deploy (founder items: DOCKERHUB_TOKEN, Portainer stack, Cloudflared hostname).
5. **Commercial gate** — Trakt commercial-API terms agreed; JustWatch deep-link decision made.

## Kill criteria
- Model cannot beat the popularity baseline by a meaningful margin after 3 modelling iterations.
- Trakt refuses commercial use or prices it beyond unit economics.
- Fewer than 5 external users willing to link a profile within 60 days of MVP.

## Graduation pathway
Technologies/ once recurring-revenue path is proven (mirror fa.foresight's Labs → Technologies pattern). Ventures/ only on standalone-brand traction. Coordinate with `fa.whatsnext` — both build recommendation cores; share capability, do not merge scope.

## Working notes
- **No manual pipelines (founder-locked 2026-06-12):** OAuth wireup, ingestion, feature extraction, training, and evaluation all run inside the app, driven by per-user config. No ad-hoc scripts/notebooks as the path of record.
- **Product spec:** all v1 UX decisions locked in `docs/product-spec.md` (2026-06-12 refinement session).
- Eligibility rule (founder-locked, tightened 2026-06-12 — "never show things I've already seen"): suggestion surfaces (feed hero/rows, Ask Reel, lexical/semantic search) show ONLY titles with zero interaction footprint — no watch history even partial, no rating (rated implies seen, incl. titles rated without a logged play), not NotInterested. In-progress titles appear exclusively on the continue-watching surface (next episode/season). Enforced in `EligibilityQueryBuilder.EligibleTitles`; typeahead intentionally still finds watched titles (lookup, not suggestion) badged via `ContentFilteredTitles`.
- Where-to-watch: TMDB watch-provider data is legal but deep links are licence-restricted (JustWatch). v1 ships provider badges + provider-side search links; true deep links require a JustWatch partnership. No scraping.
- Collaborative filtering is deferred to ~200+ active tenants; launching cross-tenant CF with one tenant is dead on arrival.
- **Identity = Sign in with Trakt (locked 2026-06-12):** the OAuth callback IS account creation; app session = reel_at/reel_rt HttpOnly cookies. Row-level tenancy via `IAccountScoped` + EF global query filters — a model test fails the build if a scoped entity ships without isolation.
- **Engineering footguns found live (don't relearn):** Trakt/TMDB sit behind Cloudflare, which 403s UA-less requests — the explicit User-Agent on the typed clients is load-bearing. All `DateTime`s normalize through a model-wide UTC converter (Trakt date-only fields arrive `Kind=Unspecified`; Npgsql refuses them). Local compose postgres publishes on **5433** (5432 is taken on the dev Mac).
- **Dev QA helper:** `POST /api/auth/dev/link` (Development + `QA_HELPERS_ENABLED=true` only) links `TRAKT_DEV_PROFILE` using `TRAKT_ACCESS_TOKEN/REFRESH_TOKEN` from `.env` — full pipeline + session without the browser consent hop. Production path of record stays the OAuth callback.
- **Pending founder inputs:** TMDB v4 Read Access Token (hydration is parked Failed until set); `DOCKERHUB_TOKEN` repo secret; new Portainer stack id + Cloudflared hostname for `reel.frostaura.net`.
