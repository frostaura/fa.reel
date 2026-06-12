# fa.reel — Product Spec (v1)

All decisions below founder-locked 2026-06-12 in a structured refinement session. This is the UX/product layer on top of `concept.md` (engine, data plane, risks) — read both before building.

## 0. Operating rule
**Everything in-app.** OAuth wireup (per-user config), ingestion, feature extraction, training, and evaluation run inside the application from day 1. No manual/ad-hoc pipelines as the path of record. EDA and edge metrics are app outputs.

## 1. Core model — home screen
**Hybrid.** "Tonight's picks" hero: 3–5 big, confident picks answering *what should I watch tonight*. Curated rows below for grazing.

## 2. Recommendation card
- **Collapsed:** poster · title/year/runtime · why-this sentence ("Slow-burn thriller like Sicario, and you rate Villeneuve 9+") · predicted rating ("8.4 for you") · provider badges (user's region).
- **Expanded (tap):** trailer (TMDB videos/YouTube) · synopsis · cast · feature breakdown behind the prediction.
- Predicted rating is non-negotiable: the falsifiable promise that disciplines the model.

## 3. Rows (v1)
- **Continue watching** — in-progress shows, next episode/season, sorted by resume likelihood.
- **Because you loved X** — anchor rows seeded by the user's highest-rated titles.
- v1.1: Deep cuts (high predicted rating × low popularity), New & for you.

## 4. Content preferences (filters)
Per-account include/exclude filters: genres, themes, keywords, maturity ratings. **None active by default**, opt-in, persisted, managed in settings. Apply across *every* surface (hero, rows, search) — an exclusion is airtight. Optional **settings PIN** locks the preferences section (account creator keeps control, e.g. kids' accounts).

## 5. Accounts
**One user = one account.** Own Trakt link, own filters, own region. No household/profile layer in v1.

## 6. Reactions & Trakt write-back
- **Seen it + rate** (one gesture; feeds the model).
- **Not interested** (+ optional reason — genre / seen-enough / tone).
- **Save for later** (in-app watchlist).
- **Write back to Trakt:** ratings, watched status, watchlist sync to the user's profile.
- **Managed Trakt list — "Reel — Up Next":** Reel owns one list per linked profile. Save a rec → added. Title detected watched on sync → auto-removed. Stays a clean live queue of unwatched picks, visible in every Trakt-connected app (Plex, Kodi, Infuse) — free distribution into the living room.

## 7. Search
One box, two modes, both v1:
- **Typeahead with personal lens:** instant title/person results, each showing predicted rating, watched-status, provider badges; respects active filters.
- **Natural-language:** "slow-burn sci-fi like Arrival but lighter" — semantic search over the same pgvector embeddings the recommender uses.

## 8. Where to watch
- **Link ladder:** per-provider deep-link resolver (maintained URL-pattern registry) → fallback to the title's TMDB watch page. Visible indicator of link type ("▶ direct" vs "↗ via TMDB"). Automated health checks on the pattern registry.
- **Region:** geo-IP default at signup; user-editable in settings; stored per account.
- True one-click deep links everywhere = JustWatch partnership (commercial gate, M5).

## 9. Taste DNA page (v1)
Full dashboard: top genres/eras/themes by ratings · taste drift over time · contrarian score · creator affinities · stats (hours watched, completion rate). Shareable card later — this page is the viral/marketing loop.

## 10. Onboarding
- **Trakt-required v1.** Cold-start path (in-app rating picker) only after edge proof.
- **First minute = live build-up show:** ingest streams visibly ("Found 2,842 movies… you're a 2010s sci-fi person… strong Villeneuve signal…"), taste DNA assembles on screen, then the feed reveals. The wait *is* the wow.

## 11. Brand & pricing
- **FrostAura family look:** frosty/glass, light-blue-on-dark-blue, clean and elegant (dark UI suits evening movie-picking). "Reel by FrostAura" footer. Canonical assets: `/docs/brand/assets/` (blue variant default).
- **Free taste, paid engine:** Free = link Trakt, taste DNA page, daily shortlist. Paid (~$3–5/mo) = full feed + rows, natural-language search, managed Trakt list, filters. Taste DNA stays free as the hook.

## OAuth & config (per the operating rule)
Authorization-code flow against per-environment config; redirect URIs registered: `https://reel.frostaura.net/auth/trakt/callback` (prod), `http://localhost:5173/auth/trakt/callback` (dev). Tokens stored per user, refresh handled by the app. Scopes: public + write (list management, ratings/watched write-back). Rotate the client secret before public launch.
