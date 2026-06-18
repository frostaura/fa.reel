import { useState } from "react";
import { Sparkles } from "lucide-react";
import { useGetRateSuggestionsQuery, useRateTitleMutation } from "../store/api";
import PosterImage from "../components/rec/PosterImage";
import { Popover, PopoverContent, PopoverTrigger } from "../components/ui/popover";

interface Suggestion {
  titleId: string;
  mediaType: "Movie" | "Show";
  tmdbId: number;
  name: string;
  year: number | null;
  posterPath: string | null;
}

/**
 * "Sharpen your taste" — a grid of popular titles you haven't rated. Rate any you've seen from
 * memory; each rating feeds the model. The useful core of cold-start, available to any user.
 */
export default function RatePicker() {
  const { data = [] } = useGetRateSuggestionsQuery();
  const [rateTitle] = useRateTitleMutation();
  const [rated, setRated] = useState<Record<string, number>>({});

  const remaining = data.filter((t) => !(t.titleId in rated));
  const count = Object.keys(rated).length;

  const rate = async (t: Suggestion, value: number) => {
    setRated((r) => ({ ...r, [t.titleId]: value }));
    await rateTitle({ mediaType: t.mediaType, tmdbId: t.tmdbId, rating: value, markWatched: true }).unwrap().catch(() => undefined);
  };

  return (
    <div className="space-y-6 pb-12">
      <div>
        <h1 className="text-2xl font-light text-fa-frost-bright flex items-center gap-2">
          <Sparkles className="h-5 w-5 text-fa-frost" /> Sharpen your taste
        </h1>
        <p className="fa-caption text-fa-frost-dim mt-1">
          Rate any you’ve seen — the more you rate, the sharper your recommendations.
          {count > 0 && <span className="text-fa-frost"> · {count} rated</span>}
        </p>
      </div>

      <div className="grid grid-cols-3 sm:grid-cols-5 md:grid-cols-6 gap-4" data-testid="rate-picker">
        {remaining.map((t) => (
          <Popover key={t.titleId}>
            <PopoverTrigger asChild>
              <button className="group block text-left focus:outline-none reel-rise" data-testid="rate-card">
                <div className="relative overflow-hidden rounded-lg group-hover:ring-2 group-hover:ring-fa-frost/50 transition">
                  <PosterImage path={t.posterPath} alt={t.name} size="w185" />
                </div>
                <p className="mt-1.5 fa-caption text-fa-frost-bright truncate" title={t.name}>{t.name}</p>
              </button>
            </PopoverTrigger>
            <PopoverContent align="center" className="w-auto">
              <p className="fa-overline text-fa-frost-dim mb-2">rate {t.name}</p>
              <div className="flex gap-1">
                {Array.from({ length: 10 }, (_, i) => i + 1).map((v) => (
                  <button
                    key={v}
                    onClick={() => rate(t, v)}
                    className="h-9 w-7 rounded-md border border-fa-edge bg-fa-glass text-sm tabular-nums text-fa-frost hover:border-fa-frost/40 transition"
                    data-testid="rate-value"
                  >
                    {v}
                  </button>
                ))}
              </div>
            </PopoverContent>
          </Popover>
        ))}
      </div>

      {data.length > 0 && remaining.length === 0 && (
        <p className="fa-body text-fa-frost-dim text-center py-8">Nice — that’s the batch. Your model sharpens on the next refresh.</p>
      )}
    </div>
  );
}
