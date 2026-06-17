import { useCallback, useRef, useState } from "react";

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Thin wrapper over the Web Speech API for voice search. Gracefully degrades: `supported` is
 * false where the browser has no SpeechRecognition, so callers can hide the mic entirely.
 */
export function useSpeech(onResult: (text: string) => void) {
  const supported =
    typeof window !== "undefined" && ("SpeechRecognition" in window || "webkitSpeechRecognition" in window);
  const [listening, setListening] = useState(false);
  const recRef = useRef<any>(null);

  const start = useCallback(() => {
    if (!supported) return;
    const Ctor = (window as any).SpeechRecognition || (window as any).webkitSpeechRecognition;
    const rec = new Ctor();
    rec.lang = "en-US";
    rec.interimResults = false;
    rec.maxAlternatives = 1;
    rec.onresult = (e: any) => {
      const text = e.results?.[0]?.[0]?.transcript;
      if (text) onResult(text);
    };
    rec.onend = () => setListening(false);
    rec.onerror = () => setListening(false);
    recRef.current = rec;
    setListening(true);
    rec.start();
  }, [supported, onResult]);

  const stop = useCallback(() => {
    recRef.current?.stop();
    setListening(false);
  }, []);

  return { supported, listening, start, stop };
}
