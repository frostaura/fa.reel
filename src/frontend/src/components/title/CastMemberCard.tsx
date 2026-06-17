import { useState } from "react";
import { Link } from "react-router-dom";
import { Star } from "lucide-react";
import type { CastMember } from "../../store/titleTypes";
import { useRatePersonMutation, useUnratePersonMutation } from "../../store/api";
import { profileUrl } from "../../lib/tmdbImages";
import { Popover, PopoverContent, PopoverTrigger } from "../ui/popover";

/**
 * One cast member: avatar (tap → 1–10 quick-rate popover, same control as rating a title),
 * a rating chip when rated, and a name that links to the actor page. Explicit person ratings
 * feed the recommender via the person-affinity signal.
 */
export default function CastMemberCard({ member }: { member: CastMember }) {
  const [ratePerson] = useRatePersonMutation();
  const [unratePerson] = useUnratePersonMutation();
  const [open, setOpen] = useState(false);
  const rating = member.userRating;

  return (
    <div className="w-20 shrink-0 text-center space-y-1">
      <Popover open={open} onOpenChange={setOpen}>
        <PopoverTrigger asChild>
          <button className="relative block h-20 w-20 rounded-full overflow-hidden bg-fa-ink-3 mx-auto focus:outline-none focus:ring-2 focus:ring-fa-frost/50" data-testid="cast-rate-trigger" aria-label={`Rate ${member.name}`}>
            {profileUrl(member.profilePath) ? (
              <img src={profileUrl(member.profilePath)!} alt={member.name} loading="lazy" className="h-full w-full object-cover" />
            ) : (
              <div className="h-full w-full flex items-center justify-center fa-caption text-fa-frost-dim">
                {member.name.split(" ").map((w) => w[0]).slice(0, 2).join("")}
              </div>
            )}
            {rating != null && (
              <span className="absolute bottom-0 inset-x-0 bg-fa-ink/80 text-fa-frost-bright fa-caption py-0.5 flex items-center justify-center gap-0.5">
                <Star className="h-2.5 w-2.5 fill-current" /> {rating}
              </span>
            )}
          </button>
        </PopoverTrigger>
        <PopoverContent align="center" className="w-auto">
          <p className="fa-overline text-fa-frost-dim mb-2">rate {member.name}</p>
          <div className="flex gap-1">
            {Array.from({ length: 10 }, (_, i) => i + 1).map((value) => (
              <button
                key={value}
                onClick={() => {
                  ratePerson({ personId: member.personId, rating: value });
                  setOpen(false);
                }}
                className={`h-9 w-7 rounded-md border text-sm tabular-nums transition ${
                  rating === value
                    ? "border-fa-frost bg-fa-frost/20 text-fa-frost-bright"
                    : "border-fa-edge bg-fa-glass text-fa-frost hover:border-fa-frost/40"
                }`}
                data-testid="cast-rate-value"
              >
                {value}
              </button>
            ))}
          </div>
          {rating != null && (
            <button
              onClick={() => {
                unratePerson({ personId: member.personId });
                setOpen(false);
              }}
              className="fa-button-ghost mt-2 w-full justify-center text-fa-frost-dim"
            >
              Clear rating
            </button>
          )}
        </PopoverContent>
      </Popover>
      <Link to={`/person/${member.personId}`} className="block fa-caption text-fa-frost hover:text-fa-frost-bright truncate" title={member.name}>
        {member.name}
      </Link>
      {member.character && <p className="fa-caption text-fa-frost-dim/70 truncate">{member.character}</p>}
    </div>
  );
}
