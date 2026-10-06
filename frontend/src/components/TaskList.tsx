import { useCallback } from "react";
import { Link } from "react-router-dom";
import { useAppData } from "../AppData";
import { api, errorMessage } from "../lib/api";
import { describeRecurrence, dueBucket, formatDate, relativeDue, toIsoDate } from "../lib/dates";
import type { GardenTask } from "../lib/types";
import { useToast } from "./Toast";

/** Erledigen mit Rückgängig-Möglichkeit; bei Wiederholung wird der nächste Termin angezeigt. */
export function useTaskActions(onChanged?: () => void) {
  const { reloadTasks } = useAppData();
  const toast = useToast();

  const complete = useCallback(
    async (task: GardenTask) => {
      try {
        const { next } = await api.completeTask(task.id);
        await reloadTasks();
        onChanged?.();
        toast.show(next ? `Erledigt. Nächster Termin: ${formatDate(next.dueDate)}` : "Erledigt.", {
          label: "Rückgängig",
          run: async () => {
            await api.reopenTask(task.id).catch((e) => toast.error(errorMessage(e)));
            await reloadTasks();
            onChanged?.();
          },
        });
      } catch (e) {
        toast.error(errorMessage(e));
      }
    },
    [reloadTasks, toast, onChanged],
  );

  const reopen = useCallback(
    async (task: GardenTask) => {
      try {
        await api.reopenTask(task.id);
        await reloadTasks();
        onChanged?.();
      } catch (e) {
        toast.error(errorMessage(e));
      }
    },
    [reloadTasks, toast, onChanged],
  );

  return { complete, reopen };
}

interface TaskRowProps {
  task: GardenTask;
  onComplete: (task: GardenTask) => void;
  onReopen?: (task: GardenTask) => void;
  showObject?: boolean;
}

export function TaskRow({ task, onComplete, onReopen, showObject = true }: TaskRowProps) {
  const today = toIsoDate(new Date());
  const done = task.status === "Done";
  const recurrence = describeRecurrence(task);
  return (
    <li className={`task-row ${done ? "done" : dueBucket(task.dueDate, today)}`}>
      <button
        type="button"
        className="check-button"
        aria-label={done ? `${task.title} wieder öffnen` : `${task.title} erledigen`}
        onClick={() => (done ? onReopen?.(task) : onComplete(task))}
      >
        {done ? "✓" : ""}
      </button>
      <Link to={`/aufgaben/${task.id}`} className="task-text">
        <strong>{task.title}</strong>
        <small>
          {done ? `erledigt` : relativeDue(task.dueDate, today)}
          {recurrence && ` · 🔁 ${recurrence}`}
          {showObject && task.objectName && ` · ${task.objectName}`}
          {task.notify && !done && " · 🔔"}
        </small>
      </Link>
    </li>
  );
}
