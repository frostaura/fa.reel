import { createSlice, type PayloadAction } from "@reduxjs/toolkit";

/** Lightweight live-sync state for the header pill, fed by the steady-state SSE stream. */
export interface SyncUiState {
  active: boolean;
  label: string | null;
  pct: number | null;
  /** Job kind ("hydrate", "delta", …) — drives the pill's contextual tooltip copy. */
  kind: string | null;
}

const initialState: SyncUiState = { active: false, label: null, pct: null, kind: null };

export const syncSlice = createSlice({
  name: "syncUi",
  initialState,
  reducers: {
    syncProgress(state, action: PayloadAction<{ label: string; pct: number | null; kind: string | null }>) {
      state.active = true;
      state.label = action.payload.label;
      state.pct = action.payload.pct;
      state.kind = action.payload.kind;
    },
    syncIdle() {
      return initialState;
    },
  },
});

export const { syncProgress, syncIdle } = syncSlice.actions;
export default syncSlice.reducer;
