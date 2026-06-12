import type { ReactNode } from "react";
import type { FeedCard } from "../../store/feedTypes";
import RecCard from "../rec/RecCard";

interface Props {
  title: ReactNode;
  items: FeedCard[];
}

/** Generic horizontal scroll-snap row of collapsed cards. */
export default function RecRow({ title, items }: Props) {
  if (items.length === 0) {
    return null;
  }

  return (
    <section className="space-y-3">
      <h2 className="fa-section-title text-base">{title}</h2>
      <div className="flex gap-4 overflow-x-auto snap-x pb-2 -mx-1 px-1 [scrollbar-width:thin]">
        {items.map((card) => (
          <div key={card.titleId} className="snap-start">
            <RecCard card={card} />
          </div>
        ))}
      </div>
    </section>
  );
}
