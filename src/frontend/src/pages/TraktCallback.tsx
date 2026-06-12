import { useEffect, useRef, useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { useExchangeTraktCodeMutation } from "../store/api";
import { consumeOauthState, parseCallbackParams } from "../lib/traktOauth";

type Status = "exchanging" | "error";

/**
 * The registered Trakt redirect URI (dev: http://localhost:5173/auth/trakt/callback).
 * Validates the state nonce against the one stored before redirect, forwards the code to the
 * API (which verifies its own HMAC-signed state independently), then enters the app.
 */
export default function TraktCallback() {
  const location = useLocation();
  const navigate = useNavigate();
  const [exchange] = useExchangeTraktCodeMutation();
  const [status, setStatus] = useState<Status>("exchanging");
  const [message, setMessage] = useState<string | null>(null);
  const started = useRef(false);

  useEffect(() => {
    if (started.current) return; // StrictMode double-invoke guard — the code is single-use
    started.current = true;

    const { code, state, error } = parseCallbackParams(location.search);
    const expectedState = consumeOauthState();

    if (error) {
      setStatus("error");
      setMessage(error === "access_denied" ? "Trakt access was declined." : `Trakt returned: ${error}`);
      return;
    }
    if (!code || !state) {
      setStatus("error");
      setMessage("Missing authorization code — start again from the sign-in page.");
      return;
    }
    if (expectedState && state !== expectedState) {
      setStatus("error");
      setMessage("Sign-in state mismatch. Start again from the sign-in page.");
      return;
    }

    exchange({ code, state })
      .unwrap()
      .then((session) => {
        navigate(session.onboarded ? "/home" : "/onboarding", { replace: true });
      })
      .catch(() => {
        setStatus("error");
        setMessage("Could not complete sign-in with Trakt. Please try again.");
      });
  }, [location.search, exchange, navigate]);

  return (
    <div className="min-h-screen flex items-center justify-center px-6">
      <div className="text-center space-y-4">
        {status === "exchanging" ? (
          <>
            <div className="text-2xl font-light text-fa-frost animate-pulse">Reel</div>
            <p className="fa-body text-fa-frost-dim">Linking your Trakt profile…</p>
          </>
        ) : (
          <div className="fa-card px-8 py-6 space-y-3 max-w-md">
            <p className="fa-section-title text-fa-danger">Sign-in didn&apos;t complete</p>
            <p className="fa-body text-fa-frost/90">{message}</p>
            <Link to="/" className="fa-button inline-flex">
              Back to sign-in
            </Link>
          </div>
        )}
      </div>
    </div>
  );
}
