/**
 * Model lab — lands with M2: training runs, precision@10 vs the popularity baseline, the ≥20%
 * relative-lift gate badge, and the 3-iteration kill-criterion ledger.
 */
export default function Lab() {
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-light text-fa-frost-bright">Model lab</h1>
      <div className="fa-card p-6">
        <p className="fa-body text-fa-frost-dim">
          The evaluation harness arrives with milestone M2 — leakage-clean time-split runs,
          precision@10 against the popularity baseline, and the edge-proof gate.
        </p>
      </div>
    </div>
  );
}
