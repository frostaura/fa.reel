# Reel

**Link your Trakt profile; get live, explainable movie & TV recommendations you haven't seen yet — with where to watch.**

Reel ingests everything you've ever watched and rated on [Trakt](https://trakt.tv), extracts rich taste features (LLM-derived tone/pacing/theme attributes, plot embeddings, cast/crew affinity, taste drift), trains a per-user model that predicts *your* rating for titles you haven't seen, and serves an explainable feed: every pick carries a predicted rating ("8.4 for you") and a why-this sentence backed by the model's actual feature contributions.

A [FrostAura Labs](https://github.com/frostaura) program.

## How it works

1. **Sign in with Trakt** — your Trakt account *is* your Reel account; signup and profile-linking are one gesture.
2. **Live build-up** — Reel ingests your full history while you watch your taste DNA assemble on screen.
3. **Tonight's picks** — a hero of 3–5 confident picks plus rows (Continue Watching, Because You Loved X), all restricted to titles you haven't finished.
4. **Explainable by contract** — the predicted rating is a falsifiable promise; the model is evaluated per user on a leakage-clean time split against a popularity baseline, in-app.

## Stack

- **Backend:** .NET 10, ASP.NET Core Minimal APIs + SSE, EF Core 10, PostgreSQL 16 + pgvector. Hexagonal (Domain / Application / Infrastructure / Api).
- **ML:** ML.NET FastTree regression per user, in-process; ONNX MiniLM embeddings; LLM attribute extraction via OpenRouter.
- **Frontend:** React 19 + TypeScript, Redux Toolkit (RTK Query), Tailwind CSS, shadcn/ui, Vite, PWA.
- **Deploy:** Docker Compose; GitHub Actions → multi-arch Docker Hub → Portainer → Cloudflared.

## Local development

```bash
cp .env.example .env       # fill in Trakt / TMDB / OpenRouter keys
docker compose up -d       # postgres + backend + frontend + gateway
# or run hot:
dotnet run --project src/backend/FrostAura.Reel.Api
cd src/frontend && npm install && npm run dev   # http://localhost:5173
```

Create a Trakt API app at <https://trakt.tv/oauth/applications> with redirect URI `http://localhost:5173/auth/trakt/callback` for dev.

## Data sources & attribution

- **Trakt** — watch history, ratings, in-progress shows, write-back (ratings, watchlist, the managed "Reel — Up Next" list).
- **TMDB** — catalog metadata, search, images, trailers, watch providers. *This product uses the TMDB API but is not endorsed or certified by TMDB.* Watch-provider data on TMDB is powered by JustWatch.

## License

[MIT](LICENSE) — © FrostAura. Open by default, per FrostAura engineering doctrine.
