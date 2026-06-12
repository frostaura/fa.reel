import type { ReactNode } from "react";
import { Navigate } from "react-router-dom";
import { useGetSessionQuery } from "../store/api";

/** Frost splash while the session resolves; bounce to the landing page when signed out. */
export default function RequireAuth({ children }: { children: ReactNode }) {
  const { isLoading, isError } = useGetSessionQuery();

  if (isLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center">
        <div className="text-2xl font-light tracking-wide text-fa-frost animate-pulse">Reel</div>
      </div>
    );
  }
  if (isError) return <Navigate to="/" replace />;
  return <>{children}</>;
}
