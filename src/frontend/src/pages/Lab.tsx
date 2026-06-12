import { FlaskConical, Play } from "lucide-react";
import { useGetModelMetricsQuery, useTrainModelMutation, useGetSyncStatusQuery } from "../store/api";

function pct(value: number | null | undefined, digits = 0): string {
  return value == null ? "—" : `${(value * 100).toFixed(digits)}%`;
}

/**
 * The model lab — the M2 instrument panel. The gate badge answers the program's one
 * falsifiable question; the ledger tracks the 3-iteration kill criterion run by run.
 */
export default function Lab() {
  const { data } = useGetModelMetricsQuery(undefined, { pollingInterval: 10_000 });
  const { data: sync } = useGetSyncStatusQuery();
  const [train, { isLoading: starting }] = useTrainModelMutation();

  const gate = data?.gate;
  const latest = data?.runs.find((r) => r.eval != null)?.eval ?? null;
  const trainActive = sync?.activeJob != null && ["Train", "Evaluate"].includes(sync.activeJob.kind);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-2xl font-light text-fa-frost-bright flex items-center gap-2">
            <FlaskConical className="h-6 w-6 text-fa-frost" /> Model lab
          </h1>
          <p className="fa-caption text-fa-frost-dim mt-1">
            Leakage-clean time-split evaluation against the popularity baseline — the edge-proof gate.
          </p>
        </div>
        <button onClick={() => train()} disabled={starting || trainActive} className="fa-button-primary">
          <Play className="h-4 w-4" />
          {trainActive ? "Training…" : "Train + evaluate"}
        </button>
      </div>

      {/* Gate badge */}
      <section
        className={`fa-card p-5 border ${
          gate?.passed ? "border-fa-success/50" : "border-fa-edge"
        }`}
        data-testid="gate-badge"
      >
        <div className="flex items-center justify-between flex-wrap gap-4">
          <div>
            <p className="fa-overline text-fa-frost-dim">M2 edge-proof gate</p>
            <p className={`text-3xl font-light mt-1 ${gate?.passed ? "text-fa-success" : "text-fa-frost-bright"}`}>
              {gate?.passed ? "PASSED" : "NOT MET"}
            </p>
            <p className="fa-caption text-fa-frost-dim mt-1">
              needs ≥ {pct(gate?.threshold)} relative lift over popularity
            </p>
          </div>
          <div className="text-right">
            <p className="fa-stat-label">latest lift</p>
            <p className={`fa-metric ${gate?.passed ? "text-fa-success" : "text-fa-frost-bright"}`}>
              {latest ? pct(latest.relativeImprovement) : "—"}
            </p>
            <p className="fa-caption text-fa-frost-dim mt-1">
              iteration {gate?.iterationsUsed ?? 0} of {gate?.killCriterionAt ?? 3} (kill criterion)
            </p>
          </div>
        </div>
      </section>

      {/* Metric tiles */}
      {latest && (
        <section className="grid grid-cols-2 sm:grid-cols-4 gap-3">
          <div className="fa-card px-4 py-3">
            <p className="fa-stat-label">model p@10</p>
            <p className="fa-stat-value">{pct(latest.modelPrecisionAt10)}</p>
          </div>
          <div className="fa-card px-4 py-3">
            <p className="fa-stat-label">popularity p@10</p>
            <p className="fa-stat-value">{pct(latest.baselinePrecisionAt10)}</p>
          </div>
          <div className="fa-card px-4 py-3">
            <p className="fa-stat-label">rmse</p>
            <p className="fa-stat-value">{latest.rmse.toFixed(2)}</p>
          </div>
          <div className="fa-card px-4 py-3">
            <p className="fa-stat-label">spearman ρ</p>
            <p className="fa-stat-value">{latest.spearmanRho.toFixed(2)}</p>
          </div>
        </section>
      )}

      {/* Feature importance */}
      {latest?.detail.featureImportance && Object.keys(latest.detail.featureImportance).length > 0 && (
        <section className="fa-card p-5">
          <h2 className="fa-section-title mb-3">What the model leans on</h2>
          <div className="space-y-2">
            {Object.entries(latest.detail.featureImportance)
              .sort(([, a], [, b]) => Math.abs(b) - Math.abs(a))
              .slice(0, 10)
              .map(([name, value], _, all) => {
                const max = Math.abs(all[0][1]) || 1;
                return (
                  <div key={name} className="flex items-center gap-3">
                    <span className="fa-caption text-fa-frost w-44 truncate" title={name}>
                      {name}
                    </span>
                    <div className="flex-1 h-2 rounded-full bg-fa-glass overflow-hidden">
                      <div
                        className="h-full rounded-full bg-fa-frost/70"
                        style={{ width: `${Math.max(4, (Math.abs(value) / max) * 100)}%` }}
                      />
                    </div>
                    <span className="fa-caption text-fa-frost-dim tabular-nums w-14 text-right">
                      {value.toFixed(1)}
                    </span>
                  </div>
                );
              })}
          </div>
        </section>
      )}

      {/* Runs ledger */}
      <section className="fa-card p-5">
        <h2 className="fa-section-title mb-3">Iteration ledger</h2>
        {!data || data.runs.length === 0 ? (
          <p className="fa-body text-fa-frost-dim">
            No runs yet — hit “Train + evaluate” to fit the first model on your ratings.
          </p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left">
              <thead>
                <tr className="fa-overline text-fa-frost-dim border-b border-fa-edge/50">
                  <th className="py-2 pr-4">iter</th>
                  <th className="py-2 pr-4">when</th>
                  <th className="py-2 pr-4">train / holdout</th>
                  <th className="py-2 pr-4">threshold</th>
                  <th className="py-2 pr-4">model p@10</th>
                  <th className="py-2 pr-4">baseline</th>
                  <th className="py-2 pr-4">lift</th>
                  <th className="py-2">gate</th>
                </tr>
              </thead>
              <tbody>
                {data.runs.map((run) => (
                  <tr key={run.id} className="border-b border-fa-edge/30 fa-body">
                    <td className="py-2 pr-4 tabular-nums">{run.iteration}</td>
                    <td className="py-2 pr-4 fa-caption text-fa-frost-dim">
                      {new Date(run.startedAt).toLocaleString()}
                    </td>
                    <td className="py-2 pr-4 tabular-nums">
                      {run.trainRowCount} / {run.holdoutRowCount}
                    </td>
                    <td className="py-2 pr-4 tabular-nums">
                      ≥{run.positiveThreshold}
                      {run.eval?.lowSample ? " (low sample)" : ""}
                    </td>
                    <td className="py-2 pr-4 tabular-nums">{pct(run.eval?.modelPrecisionAt10)}</td>
                    <td className="py-2 pr-4 tabular-nums">{pct(run.eval?.baselinePrecisionAt10)}</td>
                    <td className="py-2 pr-4 tabular-nums">{pct(run.eval?.relativeImprovement)}</td>
                    <td className="py-2">
                      {run.eval == null ? (
                        <span className="fa-caption text-fa-frost-dim">—</span>
                      ) : run.eval.passedGate ? (
                        <span className="fa-caption text-fa-success">passed</span>
                      ) : (
                        <span className="fa-caption text-fa-danger">not met</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {latest?.detail.caveats && (
        <p className="fa-caption text-fa-frost-dim/70">{latest.detail.caveats.join(" · ")}</p>
      )}
    </div>
  );
}
