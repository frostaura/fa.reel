/**
 * Trakt OAuth client-side glue. The server builds the authorize URL and a signed `state`
 * (POST /api/auth/trakt/start); we keep that state in sessionStorage across the redirect
 * round-trip and require an exact match on return before exchanging the code. The server
 * independently verifies its own HMAC + expiry — this check just rejects mismatched tabs early.
 */
const STATE_KEY = "reel.trakt.oauthState";

export function rememberOauthState(state: string): void {
  sessionStorage.setItem(STATE_KEY, state);
}

export function consumeOauthState(): string | null {
  const state = sessionStorage.getItem(STATE_KEY);
  sessionStorage.removeItem(STATE_KEY);
  return state;
}

export interface CallbackParams {
  code: string | null;
  state: string | null;
  error: string | null;
}

export function parseCallbackParams(search: string): CallbackParams {
  const params = new URLSearchParams(search);
  return {
    code: params.get("code"),
    state: params.get("state"),
    error: params.get("error"),
  };
}
