import { Navigate } from "react-router-dom";
import { Clapperboard } from "lucide-react";
import { useGetSessionQuery, useStartTraktAuthMutation } from "../store/api";
import { rememberOauthState } from "../lib/traktOauth";
import Footer from "../components/Footer";

export default function Landing() {
  const { data: session, isLoading } = useGetSessionQuery();
  const [startTraktAuth, { isLoading: isStarting, isError }] = useStartTraktAuthMutation();

  if (session) {
    return <Navigate to={session.onboarded ? "/home" : "/onboarding"} replace />;
  }

  const handleSignIn = async () => {
    const { authorizeUrl, state } = await startTraktAuth().unwrap();
    rememberOauthState(state);
    window.location.assign(authorizeUrl);
  };

  return (
    <div className="min-h-screen flex flex-col">
      <main className="flex-1 flex items-center justify-center px-6">
        <div className="max-w-xl text-center space-y-8 reel-rise">
          <div className="space-y-4">
            <h1 className="text-5xl font-light tracking-wide text-fa-frost-bright">Reel</h1>
            <p className="text-lg text-fa-frost/90 leading-relaxed">
              Link your Trakt profile; get live, explainable picks you haven&apos;t seen yet —
              with a predicted rating for <em>you</em>, and where to watch.
            </p>
          </div>
          <div className="space-y-3">
            <button
              onClick={handleSignIn}
              disabled={isLoading || isStarting}
              className="fa-button-primary text-base px-6 py-3"
            >
              <Clapperboard className="h-5 w-5" />
              {isStarting ? "Opening Trakt…" : "Sign in with Trakt"}
            </button>
            {isError && (
              <p className="fa-caption text-fa-danger">
                Couldn&apos;t reach the sign-in service. Try again in a moment.
              </p>
            )}
            <p className="fa-caption text-fa-frost-dim">
              Your Trakt account is your Reel account — nothing else to create.
            </p>
          </div>
        </div>
      </main>
      <Footer />
    </div>
  );
}
