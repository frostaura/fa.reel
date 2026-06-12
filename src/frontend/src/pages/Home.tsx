import { useGetSessionQuery } from "../store/api";

/**
 * M1 placeholder. The real surface (Tonight's Picks hero, Continue Watching, Because You
 * Loved X rows) ships in M3, after the M2 edge-proof gate passes — by design, not omission.
 */
export default function Home() {
  const { data: session } = useGetSessionQuery();

  const ready = session?.pipelineStage === "FeedReady";

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-light text-fa-frost-bright">Tonight&apos;s picks</h1>
        <p className="fa-caption text-fa-frost-dim mt-1">
          {ready
            ? "Your model is trained — the full feed lands with the MVP surface."
            : `Your library is ${session?.pipelineStage === "Ingesting" ? "still syncing" : "being processed"} (${session?.pipelineStage ?? "…"}).`}
        </p>
      </div>
      <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-5 gap-4">
        {Array.from({ length: 5 }).map((_, i) => (
          <div key={i} className="reel-poster reel-shimmer" />
        ))}
      </div>
    </div>
  );
}
