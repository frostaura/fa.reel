import { describe, expect, it } from "vitest";
import { jobKindToSseKind, syncExplainer, isBackgroundKind } from "./syncCopy";

describe("syncExplainer", () => {
  it("maps known job kinds to their explainers", () => {
    expect(syncExplainer("hydrate").title).toBe("Enriching your library");
    expect(syncExplainer("delta").title).toBe("Syncing with Trakt");
    expect(syncExplainer("reconcile").title).toBe("Nightly reconcile");
    expect(syncExplainer("train").title).toBe("Training your model");
    expect(syncExplainer("feed").title).toBe("Building tonight's picks");
  });

  it("falls back generically for unknown or missing kinds (backend-first jobs are safe)", () => {
    expect(syncExplainer("embeddings-v2").title).toBe("Background sync");
    expect(syncExplainer(null).title).toBe("Background sync");
    expect(syncExplainer(undefined).title).toBe("Background sync");
  });

  it("every explainer has at least one line", () => {
    for (const kind of ["ingest", "hydrate", "delta", "reconcile", "train", "feed", "unknown"]) {
      expect(syncExplainer(kind).lines.length).toBeGreaterThan(0);
    }
  });

  it("maps backend JobKinds to the SSE kind vocabulary (reload-seed path)", () => {
    expect(jobKindToSseKind("HydrateCatalog")).toBe("hydrate");
    expect(jobKindToSseKind("FullIngest")).toBe("ingest");
    expect(jobKindToSseKind("DeltaSync")).toBe("delta");
    expect(jobKindToSseKind("EnrichCatalog")).toBe("enrich");
    expect(jobKindToSseKind("Train")).toBe("train");
    expect(jobKindToSseKind("Evaluate")).toBe("evaluate");
    expect(jobKindToSseKind("SomethingNew")).toBeNull();
    expect(jobKindToSseKind(null)).toBeNull();
  });

  it("seeded kinds round-trip into real explainers", () => {
    expect(syncExplainer(jobKindToSseKind("HydrateCatalog")).title).toBe("Enriching your library");
    expect(syncExplainer(jobKindToSseKind("FullIngest")).title).toBe("First sync with Trakt");
    expect(syncExplainer(jobKindToSseKind("EnrichCatalog")).title).toBe("Reading every title");
  });

  it("classifies background polls so they cannot hijack the pill from a primary job", () => {
    expect(isBackgroundKind("delta")).toBe(true);
    expect(isBackgroundKind("reconcile")).toBe(true);
    expect(isBackgroundKind("enrich")).toBe(false);
    expect(isBackgroundKind("ingest")).toBe(false);
    expect(isBackgroundKind("train")).toBe(false);
    expect(isBackgroundKind(null)).toBe(false);
  });
});
