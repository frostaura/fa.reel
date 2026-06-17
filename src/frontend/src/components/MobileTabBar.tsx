import { NavLink } from "react-router-dom";
import { Fingerprint, Home, Search, Bookmark } from "lucide-react";

/**
 * Mobile primary navigation. The media feed wants full-width rows, so on small screens the
 * inline header nav collapses into this fixed bottom bar (the plan's "top header + mobile
 * bottom tabs"). Search is a button rather than a route — it toggles the header search panel
 * so typeahead + Ask Reel are reachable on a phone, which the desktop-only search box hid.
 */
export default function MobileTabBar({
  searchOpen,
  onToggleSearch,
}: {
  searchOpen: boolean;
  onToggleSearch: () => void;
}) {
  const tabClass = ({ isActive }: { isActive: boolean }) =>
    `flex flex-1 flex-col items-center justify-center gap-0.5 py-2 transition ${
      isActive ? "text-fa-frost-bright" : "text-fa-frost-dim hover:text-fa-frost"
    }`;

  return (
    <nav
      className="md:hidden fixed bottom-0 inset-x-0 z-40 border-t border-fa-edge/60 bg-fa-ink/90 backdrop-blur-md pb-[env(safe-area-inset-bottom)]"
      data-testid="mobile-tabbar"
    >
      <div className="flex items-stretch">
        <NavLink to="/home" className={tabClass}>
          <Home className="h-5 w-5" />
          <span className="fa-overline">Home</span>
        </NavLink>
        <button
          type="button"
          onClick={onToggleSearch}
          aria-pressed={searchOpen}
          className={`flex flex-1 flex-col items-center justify-center gap-0.5 py-2 transition ${
            searchOpen ? "text-fa-frost-bright" : "text-fa-frost-dim hover:text-fa-frost"
          }`}
          data-testid="mobile-search-tab"
        >
          <Search className="h-5 w-5" />
          <span className="fa-overline">Search</span>
        </button>
        <NavLink to="/saved" className={tabClass}>
          <Bookmark className="h-5 w-5" />
          <span className="fa-overline">Saved</span>
        </NavLink>
        <NavLink to="/dna" className={tabClass}>
          <Fingerprint className="h-5 w-5" />
          <span className="fa-overline">DNA</span>
        </NavLink>
      </div>
    </nav>
  );
}
