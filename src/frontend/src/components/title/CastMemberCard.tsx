import { Link, useLocation } from "react-router-dom";
import { Star } from "lucide-react";
import type { CreditPerson } from "../../store/titleTypes";
import { profileUrl } from "../../lib/tmdbImages";

/**
 * One person chip — cast or crew. The whole chip (photo + name) opens that person's page: who
 * they are, your rating control, and their filmography (each film itself openable). A rating chip
 * overlays the avatar when you've already rated them.
 */
export default function CastMemberCard({ member }: { member: CreditPerson }) {
  const location = useLocation();
  const rating = member.userRating;

  return (
    <Link
      to={`/person/${member.personId}`}
      state={{ backgroundLocation: location }}
      className="w-20 shrink-0 text-center space-y-1 group focus:outline-none"
      data-testid="person-chip"
    >
      <div className="relative h-20 w-20 rounded-full overflow-hidden bg-fa-ink-3 mx-auto ring-0 group-hover:ring-2 group-hover:ring-fa-frost/50 transition">
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
      </div>
      <p className="fa-caption text-fa-frost group-hover:text-fa-frost-bright truncate" title={member.name}>{member.name}</p>
      {member.character && <p className="fa-caption text-fa-frost-dim/70 truncate">{member.character}</p>}
    </Link>
  );
}
