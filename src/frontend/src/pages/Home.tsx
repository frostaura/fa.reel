import { useState } from "react";
import { Lock } from "lucide-react";
import { useGetContinueWatchingQuery, useGetFeedQuery, useGetSessionQuery } from "../store/api";
import RecCardHero from "../components/rec/RecCardHero";
import RecRow from "../components/rows/RecRow";
import ContinueWatchingRow from "../components/rows/ContinueWatchingRow";
import BrowseFilters from "../components/rec/BrowseFilters";
import { DEFAULT_BROWSE_FILTER, matchesBrowseFilter, matchesMedia } from "../lib/browseFilter";

/** The hybrid home: tonight's-picks hero answering one question, rows below for grazing. */
export default function Home() {
  const { data: session } = useGetSessionQuery();
  const { data: feed, isLoading } = useGetFeedQuery();
  const { data: continueWatching } = useGetContinueWatchingQuery();
  const [filter, setFilter] = useState(DEFAULT_BROWSE_FILTER);

  const building = session != null && session.pipelineStage !== "FeedReady" && (feed?.hero.length ?? 0) === 0;

  const hero = (feed?.hero ?? []).filter((c) => matchesBrowseFilter(c, filter));
  const rows = (feed?.rows ?? [])
    .map((row) => ({ ...row, items: row.items.filter((c) => matchesBrowseFilter(c, filter)) }))
    .filter((row) => row.items.length > 0);
  const continueFiltered = (continueWatching ?? []).filter((e) => matchesMedia(e.mediaType, filter));

  return (
    <div className="space-y-8">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-light text-fa-frost-bright">Tonight&apos;s picks</h1>
          {feed?.generatedAt && (
            <p className="fa-caption text-fa-frost-dim mt-1">
              ranked for you · {new Date(feed.generatedAt).toLocaleString()}
            </p>
          )}
        </div>
        {!building && (feed?.hero.length ?? 0) > 0 && <BrowseFilters value={filter} onChange={setFilter} />}
      </div>

      {isLoading || building ? (
        <div className="space-y-8">
          <div className="aspect-[16/8.5] rounded-2xl reel-shimmer" />
          <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-5 gap-4">
            {Array.from({ length: 5 }).map((_, i) => (
              <div key={i} className="reel-poster reel-shimmer" />
            ))}
          </div>
          {building && (
            <p className="fa-body text-fa-frost-dim text-center">
              Your picks are being assembled ({session?.pipelineStage}) — they&apos;ll appear here the moment they&apos;re ready.
            </p>
          )}
        </div>
      ) : (
        <>
          {hero.length === 0 ? (
            <p className="fa-body text-fa-frost-dim text-center py-8">Nothing matches this filter — try widening it.</p>
          ) : (
            <RecCardHero cards={hero} />
          )}

          <ContinueWatchingRow entries={continueFiltered} />

          {rows.map((row) => (
            <RecRow
              key={row.anchorTitleId}
              title={
                <>
                  Because you loved <span className="text-fa-frost-bright">{row.anchorName}</span>
                </>
              }
              items={row.items}
            />
          ))}

          {(feed?.lockedRowCount ?? 0) > 0 && (
            <section className="fa-card p-5 flex items-center gap-4">
              <Lock className="h-5 w-5 text-fa-frost-dim shrink-0" />
              <div>
                <p className="fa-body text-fa-frost-bright">
                  {feed!.lockedRowCount} more personalised row{feed!.lockedRowCount === 1 ? "" : "s"} on the full feed
                </p>
                <p className="fa-caption text-fa-frost-dim">
                  The free shortlist gives you three picks a day — the paid feed unlocks every row, natural-language
                  search and the managed Trakt queue.
                </p>
              </div>
            </section>
          )}
        </>
      )}
    </div>
  );
}
