import { useState } from "react";
import { Sparkles } from "lucide-react";
import { useCreateCheckoutMutation, useGetBillingStatusQuery } from "../../store/api";

/**
 * Plan + upgrade. On a paid/founder tier it just confirms the plan. On Free it offers Checkout —
 * which redirects to Stripe when billing is configured, or surfaces "coming soon" until the keys land.
 */
export default function BillingCard() {
  const { data } = useGetBillingStatusQuery();
  const [checkout, { isLoading }] = useCreateCheckoutMutation();
  const [note, setNote] = useState<string | null>(null);

  if (!data) return null;
  const isPaid = data.tier === "Paid" || data.tier === "Founder";

  const upgrade = async () => {
    setNote(null);
    const res = await checkout();
    if ("data" in res && res.data?.url) {
      window.location.href = res.data.url;
    } else {
      setNote("Paid plans are coming soon — we’ll email you first.");
    }
  };

  return (
    <section className="fa-card p-5 space-y-3" data-testid="billing-card">
      <h2 className="fa-section-title">Plan</h2>
      {isPaid ? (
        <p className="fa-body text-fa-frost-bright">
          {data.tier === "Founder" ? "Founder access — everything unlocked." : "Paid — the full feed, search and queue are yours."}
        </p>
      ) : (
        <>
          <p className="fa-caption text-fa-frost-dim">
            Free gives you taste DNA + a daily shortlist + reactions. Paid unlocks the full feed, every
            row, natural-language Ask Reel and the managed Trakt queue.
          </p>
          <button onClick={upgrade} disabled={isLoading} className="fa-button-primary inline-flex items-center gap-2 disabled:opacity-50">
            <Sparkles className="h-4 w-4" /> {isLoading ? "Opening…" : "Upgrade"}
          </button>
          {note && <p className="fa-caption text-fa-frost-dim">{note}</p>}
        </>
      )}
    </section>
  );
}
