interface Props {
  rating: number;
  size?: "sm" | "lg";
}

/** The falsifiable promise on every card: "8.4 for you". */
export default function PredictedRatingBadge({ rating, size = "sm" }: Props) {
  return (
    <span
      className={`inline-flex items-baseline gap-1 rounded-full border border-fa-frost/40 bg-fa-ink/80 backdrop-blur-sm font-medium text-fa-frost-bright tabular-nums ${
        size === "lg" ? "px-3 py-1 text-base" : "px-2 py-0.5 text-xs"
      }`}
      title="Predicted from your ratings"
    >
      {rating.toFixed(1)}
      <span className="fa-overline text-fa-frost-dim font-normal">for you</span>
    </span>
  );
}
