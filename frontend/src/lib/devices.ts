import type { DeviceActivity, DeviceCommand } from "./types";

export const ACTIVITY: Record<DeviceActivity, { label: string; icon: string }> = {
  Unknown: { label: "Unbekannt", icon: "?" },
  Mowing: { label: "Mäht", icon: "▶" },
  GoingHome: { label: "Fährt zur Station", icon: "⌂" },
  Charging: { label: "Lädt", icon: "⚡" },
  Leaving: { label: "Verlässt die Station", icon: "↗" },
  Parked: { label: "Geparkt", icon: "P" },
  Paused: { label: "Pausiert", icon: "⏸" },
  Stopped: { label: "Gestoppt", icon: "■" },
  Error: { label: "Fehler", icon: "!" },
  Offline: { label: "Nicht verbunden", icon: "✕" },
};

export const COMMANDS: Record<DeviceCommand, { label: string; confirm?: string }> = {
  Start: { label: "▶ Mähen starten", confirm: "Mäher jetzt starten? Er fährt sofort los." },
  Pause: { label: "⏸ Pausieren" },
  ParkUntilFurtherNotice: { label: "⌂ Parken bis auf Weiteres" },
  ParkUntilNextSchedule: { label: "⌂ Parken bis zum nächsten Termin" },
  ResumeSchedule: { label: "↻ Zeitplan fortsetzen", confirm: "Zeitplan fortsetzen? Der Mäher startet ggf. sofort." },
};

export const DURATIONS = [
  { minutes: 30, label: "30 Minuten" },
  { minutes: 60, label: "1 Stunde" },
  { minutes: 120, label: "2 Stunden" },
  { minutes: 180, label: "3 Stunden" },
  { minutes: 360, label: "6 Stunden" },
];
