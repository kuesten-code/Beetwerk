import { Link } from "react-router-dom";
import type { Neighbor } from "../lib/types";

interface NeighborListProps {
  neighbors: Neighbor[];
  onEdit?: (neighbor: Neighbor) => void;
}

export function NeighborList({ neighbors, onEdit }: NeighborListProps) {
  const groups = [
    { rating: "Good", title: "👍 Gute Nachbarn" },
    { rating: "Bad", title: "👎 Schlechte Nachbarn" },
  ] as const;

  if (neighbors.length === 0) return <p className="muted">Noch keine Beziehungen eingetragen.</p>;

  return (
    <div className="neighbors">
      {groups.map(({ rating, title }) => {
        const entries = neighbors.filter((n) => n.rating === rating);
        if (entries.length === 0) return null;
        return (
          <div key={rating} className={`neighbor-group ${rating.toLowerCase()}`}>
            <h4>{title}</h4>
            <ul>
              {entries.map((n) => (
                <li key={n.relationId}>
                  <Link to={`/arten/${n.speciesId}`}>{n.speciesName}</Link>
                  {n.note && <span className="muted"> – {n.note}</span>}
                  {n.source && <SourceText source={n.source} />}
                  {onEdit && (
                    <button type="button" className="link small" onClick={() => onEdit(n)}>
                      bearbeiten
                    </button>
                  )}
                </li>
              ))}
            </ul>
          </div>
        );
      })}
    </div>
  );
}

function SourceText({ source }: { source: string }) {
  const isUrl = /^https?:\/\//i.test(source);
  return (
    <small className="source">
      Quelle:{" "}
      {isUrl ? (
        <a href={source} target="_blank" rel="noopener noreferrer">
          {source}
        </a>
      ) : (
        source
      )}
    </small>
  );
}
