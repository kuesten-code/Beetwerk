import type { ReactNode } from "react";

interface SheetProps {
  title: ReactNode;
  onClose?: () => void;
  children: ReactNode;
}

/** Unten einfahrendes Panel über der Karte (am Desktop seitlich), damit die Karte sichtbar bleibt. */
export function Sheet({ title, onClose, children }: SheetProps) {
  return (
    <section className="sheet" aria-label={typeof title === "string" ? title : undefined}>
      <header className="sheet-header">
        <h2>{title}</h2>
        {onClose && (
          <button type="button" className="icon-button" onClick={onClose} aria-label="Schließen">
            ✕
          </button>
        )}
      </header>
      <div className="sheet-body">{children}</div>
    </section>
  );
}
