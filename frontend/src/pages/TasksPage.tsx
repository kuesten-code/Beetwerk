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
