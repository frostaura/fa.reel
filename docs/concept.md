# fa.reel — Concept Brief

**One-liner:** Link your Trakt profile; get live, explainable movie & TV recommendations you haven't seen yet — with where to watch.

**Locked decisions (2026-06-12):** classic-ML engine · commercial multi-tenant SaaS from the start · web app surface · home in Labs with Technologies graduation path · dev/test profile `deanmar09` · **everything in-app** (OAuth, ingestion, ML pipeline, evaluation — no manual pipelines). Full v1 UX layer: `product-spec.md`.

## Core loop
1. User links Trakt profile (public: client-ID only; private: OAuth device flow).
2. Full ingest: everything ever watched, all ratings, currently-watching. Incremental polling sync thereafter (Trakt has no webhooks).
3. Feature extraction per title + per user (the "magical features" layer).
4. Candidate generation from the TMDB catalog, filtered by the eligibility rule.
5. Rank → live feed with "because you loved X" explanations, live search, where-to-watch.

**Eligibility rule:** fully-watched titles are excluded. Eligible = never watched ∪ in progress. In-progress titles power a distinct continue-watching surface (next episode / next season) rather than mixing into discovery.

## Engine — phased, with one correction
You picked classic ML + commercial-from-day-1. Those conflict at launch: collaborative filtering needs a user base, and day 1 there is one tenant. The fix, not a compromise:

- **Phase A (launch): content-based classic ML, per user.** Features: TMDB/Trakt metadata; LLM-derived attributes per title (tone, pacing, themes, darkness, complexity, era, ensemble-vs-solo); plot-synopsis embeddings in pgvector; cast/crew affinity scores; temporal taste drift; contrarian score (user rating vs global). Model: gradient-boosted trees / regression predicting the user's rating; rank by predicted rating × freshness × diversity penalty. Fully per-tenant — works with one user.
- **Phase B (~200+ active tenants): blend in collaborative filtering** (matrix factorization / implicit ALS) as cross-tenant signal becomes real. The Phase A feature store is exactly what the hybrid needs, so nothing is thrown away.

**Evaluation (leakage-clean):** time-split on the user's own history — train on past, score the most-recent 20% of ratings. Gate: ≥20% relative precision@10 over a popularity baseline. No edge → kill criterion trips.

## Data plane
- **Trakt:** `/users/{slug}/watched`, `/ratings`, `/history`, `/watching`. Free tier with tightening 2026 limits + Fair Use Policy; **commercial use requires engaging Trakt — this is a launch gate, budget for it.** Cache aggressively; nightly full reconciliation, frequent delta polls.
- **TMDB:** catalog, search (typeahead), metadata, images, new releases, watch providers. Free for reasonable use; attribution required.

## Where to watch — the honest constraint
TMDB's watch-provider data (JustWatch-powered) legally gives *which* services carry a title per region — **not deep links**. v1: provider badge + provider-side search URL (working, just not one-click-to-player). v2 (commercial): JustWatch partnership for true deep links. Scraping JustWatch is off the table — brittle and ToS-hostile.

## Multitenancy & stack
Tenant-per-account, row-level isolation in PostgreSQL from day 1. Mandated stack (.NET 10 + EF Core + Postgres/pgvector; React 19 + Tailwind + shadcn/ui), standard deploy pattern. ML in-process (ML.NET/ONNX) first; Python training sidecar if warranted.

## Top risks
1. Trakt commercial terms / rate limits (existential for SaaS framing) — engage early, before public launch.
2. Deep links gated behind JustWatch (UX ceiling until partnered).
3. Recommender fails to beat popularity baseline (kill criterion).
4. Scope overlap with `fa.whatsnext` — share the recommendation-core capability, keep products separate.

## Milestones
M1 app skeleton (OAuth + sync into Postgres) → M2 in-app ML pipeline + edge proof vs baseline → M3 MVP surface per `product-spec.md` → M4 multi-tenant + 5 external users → M5 commercial gate (Trakt terms, JustWatch decision).
