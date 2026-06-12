import { useEffect, useState } from "react";
import { Lock, ShieldCheck, X } from "lucide-react";
import {
  useGetFiltersQuery,
  useGetSessionQuery,
  useRemovePinMutation,
  useSetPinMutation,
  useUpdateFiltersMutation,
  useVerifyPinMutation,
  type FiltersPayload,
} from "../../store/api";

const GENRES = [
  "action", "adventure", "animation", "anime", "comedy", "crime", "documentary", "drama",
  "family", "fantasy", "history", "holiday", "horror", "music", "musical", "mystery",
  "romance", "science-fiction", "sports", "superhero", "suspense", "thriller", "war", "western",
];

const MATURITY = ["", "G", "PG", "PG-13", "R"];

const UNLOCK_KEY = "reel.pinUnlock";
const UNLOCK_TTL_MS = 5 * 60 * 1000;

function readUnlock(): string | null {
  try {
    const raw = sessionStorage.getItem(UNLOCK_KEY);
    if (!raw) return null;
    const { pin, until } = JSON.parse(raw);
    return Date.now() < until ? pin : null;
  } catch {
    return null;
  }
}

/**
 * Content preferences (airtight across every surface) behind the optional settings PIN —
 * the account creator keeps control on shared devices. Unlock lives in sessionStorage for
 * five minutes and dies with the tab.
 */
export default function PreferencesSection() {
  const { data: session } = useGetSessionQuery();
  const { data: saved } = useGetFiltersQuery();
  const [updateFilters, { isLoading: saving }] = useUpdateFiltersMutation();
  const [setPinMutation] = useSetPinMutation();
  const [verifyPin] = useVerifyPinMutation();
  const [removePin] = useRemovePinMutation();

  const [unlockedPin, setUnlockedPin] = useState<string | null>(readUnlock);
  const [pinEntry, setPinEntry] = useState("");
  const [pinError, setPinError] = useState(false);
  const [draft, setDraft] = useState<FiltersPayload | null>(null);
  const [flash, setFlash] = useState<string | null>(null);
  const [newPin, setNewPin] = useState("");

  useEffect(() => {
    if (saved && draft === null) {
      setDraft(saved);
    }
  }, [saved, draft]);

  const pinConfigured = session?.pinConfigured ?? false;
  const locked = pinConfigured && unlockedPin === null;

  const unlock = async () => {
    const result = await verifyPin({ pin: pinEntry }).unwrap().catch(() => null);
    if (result?.valid) {
      sessionStorage.setItem(UNLOCK_KEY, JSON.stringify({ pin: pinEntry, until: Date.now() + UNLOCK_TTL_MS }));
      setUnlockedPin(pinEntry);
      setPinEntry("");
      setPinError(false);
    } else {
      setPinError(true);
    }
  };

  const save = async () => {
    if (!draft) return;
    await updateFilters({ filters: draft, pin: unlockedPin ?? undefined }).unwrap()
      .then(() => setFlash("Preferences saved — applied to every surface."))
      .catch(() => setFlash("Locked — verify the settings PIN first."));
    window.setTimeout(() => setFlash(null), 2500);
  };

  const toggleGenre = (genre: string) => {
    if (!draft) return;
    const excluded = draft.excludeGenres.includes(genre);
    setDraft({
      ...draft,
      excludeGenres: excluded ? draft.excludeGenres.filter((g) => g !== genre) : [...draft.excludeGenres, genre],
    });
  };

  if (locked) {
    return (
      <section className="fa-card p-5 space-y-3" data-testid="prefs-locked">
        <h2 className="fa-section-title flex items-center gap-2">
          <Lock className="h-4 w-4" /> Content preferences
        </h2>
        <p className="fa-caption text-fa-frost-dim">Locked with your settings PIN.</p>
        <div className="flex items-center gap-2">
          <input
            type="password"
            inputMode="numeric"
            maxLength={8}
            value={pinEntry}
            onChange={(e) => setPinEntry(e.target.value.replace(/\D/g, ""))}
            onKeyDown={(e) => e.key === "Enter" && unlock()}
            placeholder="PIN"
            className="fa-input w-28 tabular-nums"
            data-testid="pin-input"
          />
          <button onClick={unlock} className="fa-button">
            Unlock
          </button>
          {pinError && <span className="fa-caption text-fa-danger">Wrong PIN</span>}
        </div>
      </section>
    );
  }

  return (
    <section className="fa-card p-5 space-y-5" data-testid="prefs-section">
      <div>
        <h2 className="fa-section-title">Content preferences</h2>
        <p className="fa-caption text-fa-frost-dim mt-1">
          None active by default. An exclusion is airtight — it applies to the hero, every row and all search.
        </p>
      </div>

      {/* Genre exclusions */}
      <div className="space-y-2">
        <p className="fa-overline text-fa-frost-dim">excluded genres</p>
        <div className="flex flex-wrap gap-1.5">
          {GENRES.map((genre) => {
            const excluded = draft?.excludeGenres.includes(genre) ?? false;
            return (
              <button
                key={genre}
                onClick={() => toggleGenre(genre)}
                className={`rounded-full border px-2.5 py-1 fa-caption capitalize transition ${
                  excluded
                    ? "border-fa-danger/50 bg-fa-danger/15 text-fa-danger"
                    : "border-fa-edge bg-fa-glass text-fa-frost hover:border-fa-frost/40"
                }`}
                data-testid={`genre-${genre}`}
              >
                {genre.replace("-", " ")}
                {excluded && <X className="inline h-3 w-3 ml-1 -mt-0.5" />}
              </button>
            );
          })}
        </div>
      </div>

      {/* Keywords */}
      <div className="space-y-2">
        <p className="fa-overline text-fa-frost-dim">excluded keywords</p>
        <KeywordEditor
          keywords={draft?.excludeKeywords ?? []}
          onChange={(excludeKeywords) => draft && setDraft({ ...draft, excludeKeywords })}
        />
      </div>

      {/* Minimum predicted rating */}
      <div className="space-y-2">
        <p className="fa-overline text-fa-frost-dim">minimum predicted rating</p>
        <p className="fa-caption text-fa-frost-dim">
          Hide recommendations the model scores below this for you. 0 = show everything.
        </p>
        <div className="flex items-center gap-4 max-w-md">
          <input
            type="range"
            min={0}
            max={9}
            step={0.5}
            value={draft?.minPredictedRating ?? 0}
            onChange={(e) => draft && setDraft({ ...draft, minPredictedRating: Number(e.target.value) || null })}
            className="fa-range flex-1"
            style={{ "--fa-range-fill": `${(((draft?.minPredictedRating ?? 0) / 9) * 100).toFixed(0)}%` } as React.CSSProperties}
            data-testid="min-rating-slider"
          />
          <span className="fa-metric-sm text-fa-frost-bright w-14 text-right tabular-nums">
            {(draft?.minPredictedRating ?? 0) > 0 ? (draft!.minPredictedRating!).toFixed(1) : "off"}
          </span>
        </div>
      </div>

      {/* Maturity */}
      <div className="space-y-2">
        <p className="fa-overline text-fa-frost-dim">maturity ceiling</p>
        <select
          className="fa-input fa-select w-40"
          value={draft?.maturityCeiling ?? ""}
          onChange={(e) => draft && setDraft({ ...draft, maturityCeiling: e.target.value || null })}
        >
          {MATURITY.map((level) => (
            <option key={level} value={level} className="bg-fa-ink-2">
              {level === "" ? "none" : level === "R" ? "R / TV-MA" : level}
            </option>
          ))}
        </select>
      </div>

      <div className="flex items-center gap-3 border-t border-fa-edge/50 pt-4">
        <button onClick={save} disabled={saving || !draft} className="fa-button-primary" data-testid="save-prefs">
          Save preferences
        </button>
        {flash && <span className="fa-caption text-fa-success">{flash}</span>}
      </div>

      {/* PIN management */}
      <div className="border-t border-fa-edge/50 pt-4 space-y-2">
        <p className="fa-overline text-fa-frost-dim flex items-center gap-1.5">
          <ShieldCheck className="h-3.5 w-3.5" /> settings pin
        </p>
        <p className="fa-caption text-fa-frost-dim">
          {pinConfigured
            ? "A PIN locks this section — the account creator keeps control on shared screens."
            : "Optionally lock this section with a 4–8 digit PIN (e.g. for kids' profiles)."}
        </p>
        <div className="flex items-center gap-2">
          <input
            type="password"
            inputMode="numeric"
            maxLength={8}
            value={newPin}
            onChange={(e) => setNewPin(e.target.value.replace(/\D/g, ""))}
            placeholder={pinConfigured ? "new PIN" : "set PIN"}
            className="fa-input w-28 tabular-nums"
          />
          <button
            onClick={async () => {
              if (newPin.length < 4) return;
              await setPinMutation({ pin: newPin, currentPin: unlockedPin ?? undefined }).unwrap().catch(() => undefined);
              sessionStorage.setItem(UNLOCK_KEY, JSON.stringify({ pin: newPin, until: Date.now() + UNLOCK_TTL_MS }));
              setUnlockedPin(newPin);
              setNewPin("");
              setFlash("PIN updated.");
              window.setTimeout(() => setFlash(null), 2000);
            }}
            className="fa-button"
          >
            {pinConfigured ? "Change PIN" : "Set PIN"}
          </button>
          {pinConfigured && (
            <button
              onClick={async () => {
                await removePin({ pin: unlockedPin ?? "" }).unwrap().catch(() => undefined);
                sessionStorage.removeItem(UNLOCK_KEY);
                setUnlockedPin(null);
              }}
              className="fa-button-ghost text-fa-danger"
            >
              Remove
            </button>
          )}
        </div>
      </div>
    </section>
  );
}

function KeywordEditor({ keywords, onChange }: { keywords: string[]; onChange: (next: string[]) => void }) {
  const [input, setInput] = useState("");

  const add = () => {
    const value = input.trim().toLowerCase();
    if (value && !keywords.includes(value)) {
      onChange([...keywords, value]);
    }
    setInput("");
  };

  return (
    <div className="flex flex-wrap items-center gap-1.5">
      {keywords.map((keyword) => (
        <span key={keyword} className="inline-flex items-center gap-1 rounded-full border border-fa-edge bg-fa-glass px-2.5 py-1 fa-caption text-fa-frost">
          {keyword}
          <button onClick={() => onChange(keywords.filter((k) => k !== keyword))} aria-label={`remove ${keyword}`}>
            <X className="h-3 w-3 text-fa-frost-dim hover:text-fa-danger" />
          </button>
        </span>
      ))}
      <input
        value={input}
        onChange={(e) => setInput(e.target.value)}
        onKeyDown={(e) => e.key === "Enter" && add()}
        onBlur={add}
        placeholder="add keyword…"
        className="fa-input h-8 w-36 text-sm"
      />
    </div>
  );
}
