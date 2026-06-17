import type { AvailabilityFilter, BrowseFilter, MediaFilter } from "../../lib/browseFilter";

/** Segmented browse-filter controls: media type + release availability. */
export default function BrowseFilters({
  value,
  onChange,
  showAvailability = true,
}: {
  value: BrowseFilter;
  onChange: (next: BrowseFilter) => void;
  showAvailability?: boolean;
}) {
  return (
    <div className="flex flex-wrap items-center gap-2" data-testid="browse-filters">
      <Segment<MediaFilter>
        options={[
          ["all", "All"],
          ["movie", "Movies"],
          ["show", "TV"],
        ]}
        value={value.media}
        onSelect={(media) => onChange({ ...value, media })}
      />
      {showAvailability && (
        <Segment<AvailabilityFilter>
          options={[
            ["all", "All"],
            ["available", "Available"],
            ["coming", "Coming soon"],
          ]}
          value={value.availability}
          onSelect={(availability) => onChange({ ...value, availability })}
        />
      )}
    </div>
  );
}

function Segment<T extends string>({
  options,
  value,
  onSelect,
}: {
  options: [T, string][];
  value: T;
  onSelect: (v: T) => void;
}) {
  return (
    <div className="inline-flex rounded-lg border border-fa-edge bg-fa-glass p-0.5">
      {options.map(([key, label]) => (
        <button
          key={key}
          onClick={() => onSelect(key)}
          aria-pressed={value === key}
          className={`px-3 py-1 rounded-md fa-caption transition ${
            value === key ? "bg-fa-frost/20 text-fa-frost-bright" : "text-fa-frost-dim hover:text-fa-frost"
          }`}
          data-testid={`browse-${key}`}
        >
          {label}
        </button>
      ))}
    </div>
  );
}
