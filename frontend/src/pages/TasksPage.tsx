import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAppData } from "../AppData";
import { Sheet } from "../components/Sheet";
import { TaskForm } from "../components/TaskForm";
import { TaskRow, useTaskActions } from "../components/TaskList";
import { api } from "../lib/api";
import { groupByDue, toIsoDate, type DueBucket } from "../lib/dates";
import type { GardenTask } from "../lib/types";

const SECTIONS: { bucket: DueBucket; title: string }[] = [
  { bucket: "overdue", title: "Überfällig" },
  { bucket: "today", title: "Heute" },
  { bucket: "week", title: "Diese Woche" },
  { bucket: "later", title: "Später" },
];

export function TasksPage() {
  const { openTasks } = useAppData();
  const [showDone, setShowDone] = useState(false);
  const [doneTasks, setDoneTasks] = useState<GardenTask[]>([]);
  const [creating, setCreating] = useState(false);
  const [exporting, setExporting] = useState(false);
  const loadDone = () => api.tasks("done").then((tasks) => setDoneTasks(tasks.reverse()));
  const { complete, reopen } = useTaskActions(() => showDone && loadDone());
  const groups = groupByDue(openTasks, toIsoDate(new Date()));

  useEffect(() => {
    if (showDone) loadDone();
  }, [showDone]);

  return (
    <div className="page">
      <header className="page-header">
        <h1>Aufgaben</h1>
        <button type="button" className="icon-button" onClick={() => setExporting(true)} aria-label="Kalender exportieren" title="Kalender exportieren">
          📅
        </button>
        <button type="button" className="primary" onClick={() => setCreating(true)}>
          + Aufgabe
        </button>
      </header>

      {openTasks.length === 0 && <p className="empty">Keine offenen Aufgaben. 🌿</p>}

      {SECTIONS.map(({ bucket, title }) =>
        groups[bucket].length === 0 ? null : (
          <section key={bucket} className={`task-section ${bucket}`}>
            <h2>
              {title} <span className="count">{groups[bucket].length}</span>
            </h2>
            <ul className="task-list">
              {groups[bucket].map((task) => (
                <TaskRow key={task.id} task={task} onComplete={complete} />
              ))}
            </ul>
          </section>
        ),
      )}

      <label className="check">
        <input type="checkbox" checked={showDone} onChange={(e) => setShowDone(e.target.checked)} />
        Erledigte anzeigen
      </label>
      {showDone && (
        <ul className="task-list">
          {doneTasks.map((task) => (
            <TaskRow key={task.id} task={task} onComplete={complete} onReopen={reopen} />
          ))}
        </ul>
      )}

      {exporting && (
        <Sheet title="Kalender exportieren" onClose={() => setExporting(false)}>
          <CalendarExport onDone={() => setExporting(false)} />
        </Sheet>
      )}

      {creating && (
        <Sheet title="Neue Aufgabe" onClose={() => setCreating(false)}>
          <p className="hint">
            Aufgabe für einen freien Ort? <Link to="/?aktion=aufgabe">Auf der Karte einzeichnen</Link>
          </p>
          <TaskForm onSaved={() => setCreating(false)} onCancel={() => setCreating(false)} />
        </Sheet>
      )}
    </div>
  );
}

/** Download als .ics-Datei: läuft über die normale Anmeldung, es gibt bewusst keinen öffentlichen Kalender-Feed. */
function CalendarExport({ onDone }: { onDone: () => void }) {
  const currentYear = new Date().getFullYear();
  const [year, setYear] = useState(currentYear);
  const [includeDone, setIncludeDone] = useState(false);

  return (
    <div className="form">
      <p className="hint">
        Alle Aufgaben eines Jahres als ganztägige Termine. Wiederkehrende Aufgaben werden als Serie exportiert, Erinnerungen als Alarm. Die Datei lässt sich in
        Google Kalender, Outlook oder Apple Kalender importieren.
      </p>
      <label className="field">
        <span>Jahr</span>
        <select value={year} onChange={(e) => setYear(Number(e.target.value))}>
          {[currentYear - 1, currentYear, currentYear + 1].map((y) => (
            <option key={y} value={y}>
              {y}
            </option>
          ))}
        </select>
      </label>
      <label className="check">
        <input type="checkbox" checked={includeDone} onChange={(e) => setIncludeDone(e.target.checked)} />
        Erledigte Aufgaben mit exportieren
      </label>
      <div className="actions">
        <a className="button primary" href={api.calendarUrl(year, includeDone)} download onClick={onDone}>
          .ics herunterladen
        </a>
      </div>
    </div>
  );
}
