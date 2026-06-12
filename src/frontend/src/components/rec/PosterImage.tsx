import { useState } from "react";
import { posterUrl, posterSrcSet, type PosterSize } from "../../lib/tmdbImages";

interface Props {
  path: string | null;
  alt: string;
  size?: PosterSize;
  eager?: boolean;
  className?: string;
}

/** 2:3 poster with shimmer skeleton, fade-in, and a gradient-initials fallback. */
export default function PosterImage({ path, alt, size = "w342", eager = false, className = "" }: Props) {
  const [loaded, setLoaded] = useState(false);
  const [failed, setFailed] = useState(false);
  const url = posterUrl(path, size);

  if (!url || failed) {
    return (
      <div className={`reel-poster flex items-center justify-center bg-gradient-to-br from-fa-ink-2 to-fa-ink-3 ${className}`}>
        <span className="text-2xl font-light text-fa-frost-dim select-none">
          {alt.split(" ").slice(0, 2).map((w) => w[0]).join("")}
        </span>
      </div>
    );
  }

  return (
    <div className={`reel-poster relative ${className}`}>
      {!loaded && <div className="absolute inset-0 reel-shimmer" />}
      <img
        src={url}
        srcSet={posterSrcSet(path)}
        sizes="(max-width: 640px) 45vw, 220px"
        alt={alt}
        loading={eager ? "eager" : "lazy"}
        decoding="async"
        onLoad={() => setLoaded(true)}
        onError={() => setFailed(true)}
        className={`h-full w-full object-cover transition-opacity duration-200 ${loaded ? "opacity-100" : "opacity-0"}`}
      />
    </div>
  );
}
