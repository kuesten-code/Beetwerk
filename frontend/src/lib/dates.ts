import type { Frequency, GardenTask } from "./types";

/** Datumswerte werden wie im Backend als lokale Kalendertage im Format JJJJ-MM-TT behandelt. */
export function toIsoDate(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, "0");
  const d = String(date.getDate()).padStart(2, "0");
  return `${y}-${m}-${d}`;
}

export function parseIsoDate(value: string): Date {
  const [y, m, d] = value.split("-").map(Number);
  return new Date(y, m - 1, d);
}

export function addDays(value: string, days: number): string {
  const date = parseIsoDate(value);
  date.setDate(date.getDate() + days);
  return toIsoDate(date);
}

export function daysBetween(from: string, to: string): number {
  return Math.round((parseIsoDate(to).getTime() - parseIsoDate(from).getTime()) / 86_400_000);
}

export type DueBucket = "overdue" | "today" | "week" | "later";

export function dueBucket(dueDate: string, today: string): DueBucket {
  const diff = daysBetween(today, dueDate);
  if (diff < 0) return "overdue";
  if (diff === 0) return "today";
  if (diff <= 7) return "week";
  return "later";
}

export function groupByDue(tasks: GardenTask[], today: string): Record<DueBucket, GardenTask[]> {
  const groups: Record<DueBucket, GardenTask[]> = { overdue: [], today: [], week: [], later: [] };
  for (const task of tasks) groups[dueBucket(task.dueDate, today)].push(task);
  return groups;
}

export function formatDate(value: string): string {
  return parseIsoDate(value).toLocaleDateString("de-DE", { weekday: "short", day: "2-digit", month: "2-digit", year: "numeric" });
}

export function relativeDue(dueDate: string, today: string): string {
  const diff = daysBetween(today, dueDate);
  if (diff < -1) return `seit ${-diff} Tagen überfällig`;
  if (diff === -1) return "seit gestern überfällig";
  if (diff === 0) return "heute";
  if (diff === 1) return "morgen";
  if (diff < 7) return `in ${diff} Tagen`;
  return formatDate(dueDate);
}

export const MONTHS = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"];

const UNITS: Record<Exclude<Frequency, "None">, [string, string]> = {
  Daily: ["Tag", "Tage"],
  Weekly: ["Woche", "Wochen"],
  Monthly: ["Monat", "Monate"],
  Yearly: ["Jahr", "Jahre"],
};

export function describeRecurrence(task: Pick<GardenTask, "frequency" | "interval" | "seasonStartMonth" | "seasonEndMonth">): string | null {
  if (task.frequency === "None") return null;
  const [singular, plural] = UNITS[task.frequency];
  const base = task.interval === 1 ? `jede${task.frequency === "Daily" || task.frequency === "Monthly" ? "n" : task.frequency === "Yearly" ? "s" : ""} ${singular}` : `alle ${task.interval} ${plural}`;
  if (task.seasonStartMonth && task.seasonEndMonth)
    return `${base}, ${MONTHS[task.seasonStartMonth - 1]} bis ${MONTHS[task.seasonEndMonth - 1]}`;
  return base;
}
