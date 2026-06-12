import { useSelector } from "react-redux";
import type { RootState } from "../store";
import { syncExplainer } from "../lib/syncCopy";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "./ui/tooltip";

/**
 * Header pill surfacing background sync activity without stealing attention. Hover/focus
 * reveals a contextual explainer of what the running job is actually doing for the user.
 */
export default function SyncStatusPill() {
  const sync = useSelector((s: RootState) => s.syncUi);
  if (!sync.active) return null;

  const explainer = syncExplainer(sync.kind);

  return (
    <TooltipProvider delayDuration={150}>
      <Tooltip>
        <TooltipTrigger asChild>
          <span
            tabIndex={0}
            className="inline-flex items-center gap-2 rounded-full border border-fa-edge bg-fa-glass px-3 py-1 fa-caption text-fa-frost-dim cursor-default focus:outline-none focus:border-fa-frost/40"
          >
            <span className="fa-status-orb fa-status-running" />
            {sync.label ?? "Syncing"}
            {sync.pct != null && <span className="tabular-nums">{Math.round(sync.pct)}%</span>}
          </span>
        </TooltipTrigger>
        <TooltipContent side="bottom" align="end" className="max-w-64 px-3 py-2.5">
          <p className="fa-section-title mb-1.5">{explainer.title}</p>
          <ul className="space-y-1">
            {explainer.lines.map((line) => (
              <li key={line} className="fa-caption text-fa-frost-dim flex gap-1.5">
                <span aria-hidden className="text-fa-frost/50">·</span>
                {line}
              </li>
            ))}
          </ul>
        </TooltipContent>
      </Tooltip>
    </TooltipProvider>
  );
}
