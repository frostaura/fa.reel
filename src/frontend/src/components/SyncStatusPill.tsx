import { useSelector } from "react-redux";
import type { RootState } from "../store";

/** Header pill that surfaces background sync activity without stealing attention. */
export default function SyncStatusPill() {
  const sync = useSelector((s: RootState) => s.syncUi);
  if (!sync.active) return null;

  return (
    <span className="inline-flex items-center gap-2 rounded-full border border-fa-edge bg-fa-glass px-3 py-1 fa-caption text-fa-frost-dim">
      <span className="fa-status-orb fa-status-running" />
      {sync.label ?? "Syncing"}
      {sync.pct != null && <span className="tabular-nums">{Math.round(sync.pct)}%</span>}
    </span>
  );
}
