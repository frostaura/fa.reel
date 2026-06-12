/**
 * Brand + attribution footer, present on every page. The TMDB line is a hard licence
 * requirement — do not remove or reword it.
 */
export default function Footer() {
  return (
    <footer className="mt-auto border-t border-fa-edge/50 py-6 px-6 text-center space-y-1">
      <p className="fa-caption text-fa-frost-dim">
        Reel by <span className="text-fa-frost">FrostAura</span>
      </p>
      <p className="fa-caption text-fa-frost-dim/70">
        This product uses the TMDB API but is not endorsed or certified by TMDB. Watch-provider
        data powered by JustWatch via TMDB.
      </p>
    </footer>
  );
}
