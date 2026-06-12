# fa.reel — Reel

## Research question
Can a per-user, content-based classic-ML recommender trained on a user's full Trakt history (watched + ratings) beat a popularity baseline by ≥20% relative precision@10 on a time-split held-out slice of that user's own ratings — and can that engine carry a commercial multi-tenant SaaS?

## What this program is
A multi-tenant movies/TV recommender SaaS. Each user links a Trakt profile; Reel ingests everything ever watched plus ratings and in-progress shows, extracts rich taste features (LLM-derived tone/pacing/theme attributes, plot embeddings, cast/crew affinity, taste drift), and serves live, explainable recommendations restricted to titles the user has **not** finished (unwatched + currently-in-progress are eligible; fully watched is excluded). Live search and where-to-watch are first-class. Interesting now because LLM feature extraction makes content-based recommenders viable for a single user from day 1 — no cross-user data needed at launch.

## Stack & repo
Mandated stack: .NET 10 + EF Core + PostgreSQL (pgvector for embeddings); React 19 + Tailwind + shadcn/ui. ML: ML.NET FastTree in-process (pure managed — arm64-safe); ONNX MiniLM embeddings (local default; OpenRouter-compatible adapter behind `IEmbeddingProvider`); all LLM calls via OpenRouter (fa.startup config conventions + deterministic stub mode). Data: Trakt API (history, ratings, watching), TMDB (catalog, search, metadata, watch providers). Standard FrostAura deploy pattern (GH Actions → Docker Hub multi-arch → Portainer → Cloudflared; `reel.frostaura.net`). **Repo: `github.com/frostaura/fa.reel`** (public, MIT) — solution `fa.reel.slnx` at root, hexagonal backend under `src/backend`, frontend under `src/frontend`. **Quality bar: Silver** (lint + build + tests pass, deployed) — CI enforces format, coverage ratchets, security audits, pgvector integration tests, Playwright smoke.

## Owner & key people
Dean Martin (founder). Dev/test Trakt profile: `deanmar09` (public).

## Current phase
`prototyping` (created 2026-06-12; M1 skeleton built + founder ingest verified 2026-06-12 — full plan at `~/.claude/plans/plan-the-full-implementation-wiggly-koala.md`, architecture decisions in the implementation commits).

## Milestones
1. **App skeleton** — mandated-stack repo: Trakt OAuth wireup (per-user config, `/auth/trakt/callback`), background sync into Postgres, tenant model.
2. **In-app ML pipeline + edge proof** — feature extraction → training → time-split evaluation as app jobs; v1 model beats popularity baseline by ≥20% relative precision@10 on held-out ratings (leakage-clean), surfaced on an in-app metrics view.
3. **MVP surface** — per `docs/product-spec.md`: tonight's-picks hero + rows, card anatomy, reactions + Trakt write-back + managed list, both search modes, where-to-watch ladder, taste DNA, live build-up onboarding.
4. **Multi-tenant** — onboarding flow live; ≥5 external users linked profiles.
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
- Eligibility rule (founder-locked): recommend only unwatched + in-progress titles; in-progress powers a continue-watching surface (next episode/season).
- Where-to-watch: TMDB watch-provider data is legal but deep links are licence-restricted (JustWatch). v1 ships provider badges + provider-side search links; true deep links require a JustWatch partnership. No scraping.
- Collaborative filtering is deferred to ~200+ active tenants; launching cross-tenant CF with one tenant is dead on arrival.
- **Identity = Sign in with Trakt (locked 2026-06-12):** the OAuth callback IS account creation; app session = reel_at/reel_rt HttpOnly cookies. Row-level tenancy via `IAccountScoped` + EF global query filters — a model test fails the build if a scoped entity ships without isolation.
- **Engineering footguns found live (don't relearn):** Trakt/TMDB sit behind Cloudflare, which 403s UA-less requests — the explicit User-Agent on the typed clients is load-bearing. All `DateTime`s normalize through a model-wide UTC converter (Trakt date-only fields arrive `Kind=Unspecified`; Npgsql refuses them). Local compose postgres publishes on **5433** (5432 is taken on the dev Mac).
- **Dev QA helper:** `POST /api/auth/dev/link` (Development + `QA_HELPERS_ENABLED=true` only) links `TRAKT_DEV_PROFILE` using `TRAKT_ACCESS_TOKEN/REFRESH_TOKEN` from `.env` — full pipeline + session without the browser consent hop. Production path of record stays the OAuth callback.
- **Pending founder inputs:** TMDB v4 Read Access Token (hydration is parked Failed until set); `DOCKERHUB_TOKEN` repo secret; new Portainer stack id + Cloudflared hostname for `reel.frostaura.net`.
