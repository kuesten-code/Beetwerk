import { Link } from "react-router-dom";
import { describeRecurrence, formatDate, relativeDue, toIsoDate } from "../lib/dates";
import type { GardenTask } from "../lib/types";
import { useTaskActions } from "./TaskList";

interface TaskDetailProps {
  task: GardenTask;
  onChanged: () => void;
  onEdit: () => void;
  onDelete: () => void;
  onShowOnMap?: () => void;
  onEditGeometry?: () => void;
}

export function TaskDetail({ task, onChanged, onEdit, onDelete, onShowOnMap, onEditGeometry }: TaskDetailProps) {
  const { complete, reopen } = useTaskActions(onChanged);
  const today = toIsoDate(new Date());
  const done = task.status === "Done";
  const recurrence = describeRecurrence(task);

  return (
    <div className="detail">
      <dl className="facts">
        <div className="fact">
          <dt>Fällig</dt>
          <dd>
            {formatDate(task.dueDate)} {!done && <span className="muted">({relativeDue(task.dueDate, today)})</span>}
          </dd>
        </div>
        <div className="fact">
          <dt>Status</dt>
          <dd>{done ? `erledigt am ${new Date(task.completedAt!).toLocaleDateString("de-DE")}` : "offen"}</dd>
        </div>
        {recurrence && (
          <div className="fact">
            <dt>Wiederholung</dt>
            <dd>🔁 {recurrence}</dd>
          </div>
        )}
        <div className="fact">
          <dt>Ort</dt>
          <dd>{task.objectId ? <Link to={`/?objekt=${task.objectId}`}>{task.objectName}</Link> : "eigener Ort auf der Karte"}</dd>
        </div>
        {task.notify && (
          <div className="fact">
            <dt>Erinnerung</dt>
            <dd>🔔 {task.leadDays === 0 ? "am Fälligkeitstag" : `${task.leadDays} Tag(e) vorher`}</dd>
          </div>
        )}
      </dl>
      {task.description && <p className="notes">{task.description}</p>}

      <div className="actions wrap">
        {done ? (
          <button type="button" onClick={() => reopen(task)}>
            Wieder öffnen
          </button>
        ) : (
          <button type="button" className="primary" onClick={() => complete(task)}>
            ✓ Erledigt
          </button>
        )}
        <button type="button" onClick={onEdit}>
          Bearbeiten
        </button>
        {onEditGeometry && !task.objectId && (
          <button type="button" onClick={onEditGeometry}>
            Ort ändern
          </button>
        )}
        {onShowOnMap && (
          <button type="button" onClick={onShowOnMap}>
            Auf Karte zeigen
          </button>
        )}
        <button type="button" className="danger" onClick={onDelete}>
          Löschen
        </button>
      </div>
    </div>
  );
}
