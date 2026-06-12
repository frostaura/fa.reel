import { useNavigate, useParams } from "react-router-dom";

/** Stub detail surface — the route contract for cards; the full modal family replaces this. */
export default function TitleDetail() {
  const { mediaType, tmdbId } = useParams();
  const navigate = useNavigate();

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-fa-ink/70 backdrop-blur-sm" onClick={() => navigate(-1)}>
      <div className="fa-card max-w-lg w-full mx-4 p-6" onClick={(e) => e.stopPropagation()}>
        <p className="fa-section-title">Title detail</p>
        <p className="fa-body text-fa-frost-dim mt-2">
          {mediaType} / {tmdbId} — the expanded card (trailer, cast, prediction breakdown, reactions) lands next.
        </p>
        <button onClick={() => navigate(-1)} className="fa-button mt-4">
          Close
        </button>
      </div>
    </div>
  );
}
