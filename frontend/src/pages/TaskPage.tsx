import { useCallback, useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useAppData } from "../AppData";
import { TaskDetail } from "../components/TaskDetail";
import { TaskForm } from "../components/TaskForm";
import { useToast } from "../components/Toast";
import { api, errorMessage } from "../lib/api";
import type { GardenTask } from "../lib/types";

export function TaskPage() {
  const id = Number(useParams().id);
  const navigate = useNavigate();
  const toast = useToast();
  const { reloadTasks } = useAppData();
  const [task, setTask] = useState<GardenTask | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [editing, setEditing] = useState(false);

  const load = useCallback(() => {
    api.task(id).then(setTask, () => setNotFound(true));
  }, [id]);

  useEffect(load, [load]);

  async function remove() {
    if (!task || !window.confirm(`Aufgabe „${task.title}“ löschen?`)) return;
    try {
      await api.deleteTask(task.id);
      await reloadTasks();
      navigate("/aufgaben", { replace: true });
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  if (notFound)
    return (
      <div className="page">
        <p className="empty">Diese Aufgabe gibt es nicht mehr.</p>
        <Link to="/aufgaben">Zur Aufgabenliste</Link>
      </div>
    );
  if (!task) return <div className="page muted">Lädt …</div>;

  return (
    <div className="page">
      <header className="page-header">
        <Link to="/aufgaben" className="back" aria-label="Zurück">
          ←
        </Link>
        <h1>{task.title}</h1>
      </header>
      {editing ? (
        <TaskForm
          initial={task}
          onSaved={(saved) => {
            setTask(saved);
            setEditing(false);
          }}
          onCancel={() => setEditing(false)}
        />
      ) : (
        <TaskDetail
          task={task}
          onChanged={load}
          onEdit={() => setEditing(true)}
          onDelete={remove}
          onShowOnMap={() => navigate(`/?aufgabe=${task.id}`)}
        />
      )}
    </div>
  );
}
