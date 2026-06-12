import { Link, NavLink, Outlet, useNavigate } from "react-router-dom";
import { LogOut, Settings as SettingsIcon, FlaskConical } from "lucide-react";
import { useGetSessionQuery, useLogoutMutation, api } from "../store/api";
import { useDispatch } from "react-redux";
import type { AppDispatch } from "../store";
import SyncStatusPill from "./SyncStatusPill";
import SearchBox from "./search/SearchBox";
import Footer from "./Footer";
import RealtimeSync from "./RealtimeSync";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "./ui/dropdown-menu";

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  `px-3 py-1.5 rounded-md text-sm transition ${
    isActive ? "text-fa-frost-bright bg-fa-glass-strong" : "text-fa-frost/80 hover:text-fa-frost-bright hover:bg-fa-glass"
  }`;

export default function Layout() {
  const { data: session } = useGetSessionQuery();
  const [logout] = useLogoutMutation();
  const navigate = useNavigate();
  const dispatch = useDispatch<AppDispatch>();

  const handleSignOut = async () => {
    await logout().unwrap().catch(() => undefined);
    dispatch(api.util.resetApiState());
    navigate("/");
  };

  return (
    <div className="min-h-screen flex flex-col">
      <RealtimeSync />
      <header className="sticky top-0 z-40 border-b border-fa-edge/50 bg-fa-ink/80 backdrop-blur-md">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 h-14 flex items-center gap-4">
          <Link to="/home" className="text-lg font-light tracking-wide text-fa-frost-bright">
            Reel
          </Link>
          <nav className="flex items-center gap-1">
            <NavLink to="/home" className={navLinkClass}>
              Home
            </NavLink>
            <NavLink to="/saved" className={navLinkClass}>
              Saved
            </NavLink>
            <NavLink to="/dna" className={navLinkClass}>
              Taste DNA
            </NavLink>
          </nav>
          <div className="ml-auto flex items-center gap-3 flex-1 justify-end">
            <div className="hidden md:block flex-1 max-w-md">
              <SearchBox />
            </div>
            <SyncStatusPill />
            <DropdownMenu>
              <DropdownMenuTrigger className="flex items-center gap-2 rounded-full border border-fa-edge bg-fa-glass px-2 py-1 hover:border-fa-frost/40 transition">
                {session?.avatarUrl ? (
                  <img src={session.avatarUrl} alt="" className="h-6 w-6 rounded-full object-cover" />
                ) : (
                  <span className="h-6 w-6 rounded-full bg-fa-ink-3 flex items-center justify-center fa-caption text-fa-frost">
                    {session?.displayName?.[0]?.toUpperCase() ?? "?"}
                  </span>
                )}
                <span className="fa-caption text-fa-frost hidden sm:inline">{session?.displayName}</span>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem onClick={() => navigate("/settings")}>
                  <SettingsIcon className="h-4 w-4 mr-2" /> Settings
                </DropdownMenuItem>
                {session?.tier === "Founder" && (
                  <DropdownMenuItem onClick={() => navigate("/lab")}>
                    <FlaskConical className="h-4 w-4 mr-2" /> Model lab
                  </DropdownMenuItem>
                )}
                <DropdownMenuSeparator />
                <DropdownMenuItem onClick={handleSignOut}>
                  <LogOut className="h-4 w-4 mr-2" /> Sign out
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </div>
      </header>
      <main className="flex-1 mx-auto w-full max-w-7xl px-4 sm:px-6 py-6">
        <Outlet />
      </main>
      <Footer />
    </div>
  );
}
