import { useEffect, useState } from "react";
import { useAppData } from "../AppData";
import { api, errorMessage } from "../lib/api";
import { ACTIVITY, COMMANDS, DURATIONS } from "../lib/devices";
import type { DeviceCommand, DeviceInfo, DeviceLink, DeviceProviderInfo, GardenObject } from "../lib/types";
import { useToast } from "./Toast";

/** Verbindung eines Kartenobjekts mit einem echten Gerät (Mähroboter) samt Status und Steuerung. */
export function DevicePanel({ object }: { object: GardenObject }) {
  const { devices } = useAppData();
  const [providers, setProviders] = useState<DeviceProviderInfo[] | null>(null);
  const link = devices.get(object.id);

  useEffect(() => {
    api.deviceProviders().then(setProviders, () => setProviders([]));
  }, []);

  if (!providers) return <p className="muted">Lädt …</p>;
  const provider = link ? providers.find((p) => p.key === link.provider) : undefined;
  return link ? <DeviceControl object={object} link={link} provider={provider} /> : <ConnectForm object={object} providers={providers} />;
}

function ConnectForm({ object, providers }: { object: GardenObject; providers: DeviceProviderInfo[] }) {
  const { reloadDevices } = useAppData();
  const toast = useToast();
  const configured = providers.filter((p) => p.configured);
  const [providerKey, setProviderKey] = useState(configured[0]?.key ?? "");
  const [found, setFound] = useState<DeviceInfo[] | null>(null);
  const [externalId, setExternalId] = useState("");
  const [settings, setSettings] = useState<Record<string, string>>({});
  const [createTasks, setCreateTasks] = useState(true);
  const [busy, setBusy] = useState(false);

  if (configured.length === 0)
    return (
      <p className="hint">
        Noch keine Geräteanbindung eingerichtet. Für Husqvarna <code>HUSQVARNA_CLIENT_ID</code> und <code>HUSQVARNA_CLIENT_SECRET</code>, für Home Assistant{" "}
        <code>HOMEASSISTANT_URL</code> und <code>HOMEASSISTANT_TOKEN</code> in der <code>.env</code> setzen und Beetwerk neu starten.
      </p>
    );

  async function search() {
    setBusy(true);
    try {
      const list = await api.providerDevices(providerKey);
      setFound(list);
      setExternalId(list[0]?.externalId ?? "");
      if (list.length === 0) toast.error("Beim Anbieter wurde kein Mähroboter gefunden.");
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  async function connect() {
    setBusy(true);
    try {
      const link = await api.linkDevice(object.id, providerKey, externalId, settings, createTasks);
      await reloadDevices();
      toast.show(link.error ? `Verbunden, aber: ${link.error}` : "Gerät verbunden.");
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="form">
      <label className="field">
        <span>Anbieter</span>
        <select
          value={providerKey}
          onChange={(e) => {
            setProviderKey(e.target.value);
            setFound(null);
          }}
        >
          {configured.map((p) => (
            <option key={p.key} value={p.key}>
              {p.name}
            </option>
          ))}
        </select>
      </label>
      {found === null ? (
        <button type="button" onClick={search} disabled={busy}>
          {busy ? "Suche …" : "Geräte suchen"}
        </button>
      ) : (
        <>
          <label className="field">
            <span>Gerät</span>
            <select value={externalId} onChange={(e) => setExternalId(e.target.value)}>
              {found.map((d) => (
                <option key={d.externalId} value={d.externalId}>
                  {d.name}
                  {d.model ? ` (${d.model})` : ""}
                </option>
              ))}
            </select>
          </label>
          {providerKey === "homeassistant" && (
            <details>
              <summary>Zusätzliche Entities (optional)</summary>
              <p className="hint">Leer lassen, wenn die Husqvarna-Integration von Home Assistant genutzt wird – die Namen werden dann abgeleitet.</p>
              {[
                ["batteryEntity", "Akku", "sensor.robi_battery"],
                ["cuttingHeightEntity", "Schnitthöhe", "number.robi_cutting_height"],
                ["errorEntity", "Fehler", "sensor.robi_error"],
              ].map(([key, label, example]) => (
                <label className="field" key={key}>
                  <span>{label}</span>
                  <input value={settings[key] ?? ""} placeholder={example} onChange={(e) => setSettings({ ...settings, [key]: e.target.value })} />
                </label>
              ))}
            </details>
          )}
          <label className="check">
            <input type="checkbox" checked={createTasks} onChange={(e) => setCreateTasks(e.target.checked)} />
            Bei Fehlern automatisch eine Aufgabe anlegen
          </label>
          <div className="actions">
            <button type="button" className="primary" onClick={connect} disabled={busy || !externalId}>
              Verbinden
            </button>
          </div>
        </>
      )}
    </div>
  );
}

function DeviceControl({ object, link, provider }: { object: GardenObject; link: DeviceLink; provider?: DeviceProviderInfo }) {
  const { reloadDevices, reloadTasks } = useAppData();
  const toast = useToast();
  const status = link.status;
  const [duration, setDuration] = useState(180);
  const [height, setHeight] = useState<number | null>(status?.cuttingHeight ?? null);
  const [busy, setBusy] = useState(false);

  useEffect(() => setHeight(status?.cuttingHeight ?? null), [status?.cuttingHeight]);

  // Befehle wirken beim Gerät mit Verzögerung; danach einmal gezielt nachfragen.
  function refreshSoon() {
    window.setTimeout(async () => {
      await api.refreshDevice(object.id).catch(() => undefined);
      await Promise.all([reloadDevices(), reloadTasks()]);
    }, 25_000);
  }

  async function run(command: DeviceCommand) {
    const info = COMMANDS[command];
    if (info.confirm && !window.confirm(info.confirm)) return;
    setBusy(true);
    try {
      await api.deviceCommand(object.id, command, command === "Start" && provider?.startNeedsDuration ? duration : undefined);
      toast.show("Befehl gesendet. Der Status aktualisiert sich in Kürze.");
      refreshSoon();
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  async function applyHeight() {
    if (height === null) return;
    setBusy(true);
    try {
      await api.setCuttingHeight(object.id, height);
      toast.show(`Schnitthöhe ${height} gesendet.`);
      refreshSoon();
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  async function refresh() {
    setBusy(true);
    try {
      const updated = await api.refreshDevice(object.id);
      await reloadDevices();
      if (updated.error) toast.error(updated.error);
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  async function toggleTasks(enabled: boolean) {
    try {
      await api.linkDevice(object.id, link.provider, link.externalId, link.settings, enabled);
      await reloadDevices();
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  async function unlink() {
    if (!window.confirm("Verbindung zum Gerät lösen? Das Objekt auf der Karte bleibt erhalten.")) return;
    try {
      await api.unlinkDevice(object.id);
      await reloadDevices();
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  const activity = status ? ACTIVITY[status.activity] : null;

  return (
    <div className="device">
      {status ? (
        <div className={`device-status ${status.activity.toLowerCase()}`}>
          <span className="device-icon" aria-hidden>
            {activity!.icon}
          </span>
          <div>
            <strong>
              {status.name}: {activity!.label}
            </strong>
            <small className="muted">
              {status.batteryPercent !== null && `Akku ${status.batteryPercent} %`}
              {status.nextStart && ` · nächster Start ${new Date(status.nextStart).toLocaleString("de-DE", { weekday: "short", hour: "2-digit", minute: "2-digit" })}`}
            </small>
          </div>
        </div>
      ) : (
        <p className="muted">Noch kein Status.</p>
      )}

      {status?.errorText && (
        <div className="warning" role="alert">
          <strong>⚠ {status.errorText}</strong>
        </div>
      )}
      {link.error && <p className="hint error-text">{link.error}</p>}

      {provider && (
        <>
          {provider.commands.includes("Start") && provider.startNeedsDuration && (
            <label className="field">
              <span>Mähdauer beim Starten</span>
              <select value={duration} onChange={(e) => setDuration(Number(e.target.value))}>
                {DURATIONS.map((d) => (
                  <option key={d.minutes} value={d.minutes}>
                    {d.label}
                  </option>
                ))}
              </select>
            </label>
          )}
          <div className="actions wrap">
            {provider.commands.map((command) => (
              <button type="button" key={command} className={command === "Start" ? "primary" : ""} onClick={() => run(command)} disabled={busy}>
                {COMMANDS[command].label}
              </button>
            ))}
          </div>

          {status && (
            <div className="field">
              <span>
                Schnitthöhe {height ?? "–"} <small className="muted">(Stufe {status.cuttingHeightMin}–{status.cuttingHeightMax})</small>
              </span>
              <div className="opacity-row">
                <input
                  type="range"
                  min={status.cuttingHeightMin}
                  max={status.cuttingHeightMax}
                  step={1}
                  value={height ?? status.cuttingHeightMin}
                  onChange={(e) => setHeight(Number(e.target.value))}
                  aria-label="Schnitthöhe"
                />
                <button type="button" onClick={applyHeight} disabled={busy || height === null || height === status.cuttingHeight}>
                  Übernehmen
                </button>
              </div>
            </div>
          )}
        </>
      )}

      <div className="row wrap-row">
        <button type="button" onClick={refresh} disabled={busy}>
          ↻ Aktualisieren
        </button>
        {link.fetchedAt && <small className="muted">Stand {new Date(link.fetchedAt).toLocaleTimeString("de-DE", { hour: "2-digit", minute: "2-digit" })}</small>}
      </div>

      <details>
        <summary>Verbindung</summary>
        <p className="hint">
          {provider?.name ?? link.provider} · {link.externalId}
        </p>
        <label className="check">
          <input type="checkbox" checked={link.createTasksOnError} onChange={(e) => toggleTasks(e.target.checked)} />
          Bei Fehlern automatisch eine Aufgabe anlegen
        </label>
        <button type="button" className="danger" onClick={unlink}>
          Verbindung lösen
        </button>
      </details>
    </div>
  );
}
