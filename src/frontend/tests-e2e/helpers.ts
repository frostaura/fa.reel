import type { Page } from "@playwright/test";

export const SESSION_FOUNDER = {
  accountId: "00000000-0000-0000-0000-000000000001",
  displayName: "Test Founder",
  traktSlug: "test-founder",
  avatarUrl: null,
  region: "US",
  tier: "Founder",
  pipelineStage: "FeedReady",
  onboarded: true,
  pinConfigured: false,
};

export const EMPTY_SYNC = {
  pipelineStage: "Ingesting",
  connectionStatus: "Active",
  lastDeltaSyncAt: null,
  lastFullReconcileAt: null,
  activeJob: null,
  recentJobs: [],
  outboxPending: 0,
  outboxDeadLetters: 0,
};

export async function mockJson(page: Page, url: string, body: unknown, status = 200): Promise<void> {
  await page.route(url, (route) =>
    route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) })
  );
}

/**
 * Replaces EventSource before any app code runs. Tests drive named events through
 * window.__sse.emit(type, data); instances auto-register on a global list.
 */
export async function stubEventSource(page: Page): Promise<void> {
  await page.addInitScript(() => {
    type Listener = (ev: MessageEvent) => void;
    const sources: { listeners: Map<string, Listener[]> }[] = [];

    class FakeEventSource {
      listeners = new Map<string, Listener[]>();
      onerror: ((ev: Event) => void) | null = null;
      constructor(_url: string) {
        sources.push(this);
      }
      addEventListener(type: string, listener: Listener) {
        const list = this.listeners.get(type) ?? [];
        list.push(listener);
        this.listeners.set(type, list);
      }
      close() {
        const index = sources.indexOf(this);
        if (index >= 0) sources.splice(index, 1);
      }
    }

    let seq = 0;
    (window as unknown as Record<string, unknown>).__sse = {
      emit(type: string, data: Record<string, unknown>) {
        seq += 1;
        const payload = JSON.stringify({ seq, ...data });
        for (const source of sources) {
          for (const listener of source.listeners.get(type) ?? []) {
            listener(new MessageEvent(type, { data: payload }));
          }
        }
      },
      count() {
        return sources.length;
      },
    };
    (window as unknown as Record<string, unknown>).EventSource = FakeEventSource;
  });
}

export async function emit(page: Page, type: string, data: Record<string, unknown>): Promise<void> {
  await page.evaluate(
    ([t, d]) => (window as unknown as { __sse: { emit: (a: string, b: unknown) => void } }).__sse.emit(t as string, d),
    [type, data] as const
  );
}

export async function waitForStream(page: Page): Promise<void> {
  await page.waitForFunction(
    () => (window as unknown as { __sse?: { count: () => number } }).__sse?.count() ?? 0 > 0
  );
}
