import { useEffect, useState, type FormEvent } from "react";
import { useAppData } from "../AppData";
import { Sheet } from "../components/Sheet";
import { useToast } from "../components/Toast";
import { OverlaysSection } from "../components/OverlaysSection";
import { UsersSection } from "../components/UsersSection";
import { api, errorMessage } from "../lib/api";
import { GEOMETRY_LABELS } from "../lib/geo";
import { currentSubscription, pushSupport, subscribe, unsubscribe, type PushSupport } from "../lib/push";
import type { FieldType, GeometryKind, ObjectType, ObjectTypeInput } from "../lib/types";

export function SettingsPage() {
  const { config, garden, setGarden, objectTypes } = useAppData();
  const toast = useToast();
  const [name, setName] = useState(garden.name);
  const [distance, setDistance] = useState(String(garden.neighborWarningDistance));
  const [editingType, setEditingType] = useState<ObjectType | "new" | null>(null);

  async function saveName(event: FormEvent) {
    event.preventDefault();
    try {
      setGarden(await api.saveGarden({ ...garden, name }));
      toast.show("Gespeichert.");
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  async function saveDistance(event: FormEvent) {
    event.preventDefault();
    try {
      setGarden(await api.saveGarden({ ...garden, neighborWarningDistance: Number(distance) }));
      toast.show("Warnabstand gespeichert.");
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  async function logout() {
    await api.logout();
    window.location.href = "/login";
  }

  return (
    <div className="page">
      <header className="page-header">
        <h1>Einstellungen</h1>
      </header>

      <section>
        <h2>Garten</h2>
        <form className="row" onSubmit={saveName}>
          <input value={name} onChange={(e) => setName(e.target.value)} aria-label="Name des Gartens" required />
          <button type="submit" className="primary" disabled={name === garden.name}>
            Speichern
          </button>
        </form>
        <p className="hint">Startansicht und Gartengrenze stellst du auf der Karte über ☰ ein.</p>
        <form className="row" onSubmit={saveDistance}>
          <label className="field">
            <span>Warnabstand für schlechte Nachbarn (m)</span>
            <input
              type="number"
              min={0.1}
              max={50}
              step={0.1}
              value={distance}
              onChange={(e) => setDistance(e.target.value)}
              required
            />
          </label>
          <button type="submit" className="primary align-end" disabled={Number(distance) === garden.neighborWarningDistance}>
            Speichern
          </button>
        </form>
      </section>

      <OverlaysSection />

      <PushSection />

      <section>
        <div className="section-header">
          <h2>Objekttypen</h2>
          <button type="button" className="primary" onClick={() => setEditingType("new")}>
            + Typ
          </button>
        </div>
        <ul className="link-list">
          {objectTypes.map((t) => (
            <li key={t.id}>
              <button type="button" className="list-button" onClick={() => setEditingType(t)}>
                <span className="choice-icon" style={{ background: t.color }}>
                  {t.icon}
                </span>
                <span>
                  <strong>{t.name}</strong>
                  <small className="muted"> {t.allowedGeometries.map((g) => GEOMETRY_LABELS[g]).join(", ")}</small>
                </span>
              </button>
            </li>
          ))}
        </ul>
      </section>

      <section>
        <h2>Konto</h2>
        {config.authMode === "login" ? (
          <div className="row">
            <span>
              Angemeldet als <strong>{config.username}</strong>
            </span>
            <button type="button" onClick={logout}>
              Abmelden
            </button>
          </div>
        ) : (
          <p className="hint">Anmeldung ist deaktiviert (AUTH_MODE=none).</p>
        )}
      </section>

      {config.authMode === "login" && <UsersSection />}

      <p className="hint">Beetwerk {config.version}</p>

      {editingType && (
        <Sheet title={editingType === "new" ? "Neuer Objekttyp" : editingType.name} onClose={() => setEditingType(null)}>
          <ObjectTypeForm initial={editingType === "new" ? undefined : editingType} onDone={() => setEditingType(null)} />
        </Sheet>
      )}
    </div>
  );
}

const SUPPORT_TEXT: Record<Exclude<PushSupport, "ok">, string> = {
  "server-disabled": "Push ist auf dem Server abgeschaltet (PUSH_ENABLED oder fehlendes HTTPS). Fällige Aufgaben siehst du in der Aufgabenliste.",
  insecure: "Push braucht HTTPS. Über eine unverschlüsselte Adresse ist es nicht verfügbar.",
  unsupported: "Dieser Browser unterstützt keine Push-Benachrichtigungen.",
  "ios-needs-homescreen": "Auf iPhone/iPad gibt es Push nur, wenn Beetwerk zum Home-Bildschirm hinzugefügt wurde: Teilen → „Zum Home-Bildschirm“.",
};

function PushSection() {
  const { config } = useAppData();
  const toast = useToast();
  const support = pushSupport(config.pushEnabled);
  const [subscribed, setSubscribed] = useState<boolean | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (support === "ok") currentSubscription().then((s) => setSubscribed(s !== null), () => setSubscribed(false));
  }, [support]);

  async function run(action: () => Promise<void>, success: string) {
    setBusy(true);
    try {
      await action();
      setSubscribed((await currentSubscription()) !== null);
      toast.show(success);
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section>
      <h2>Benachrichtigungen</h2>
      {support !== "ok" ? (
        <p className="hint">{SUPPORT_TEXT[support]}</p>
      ) : (
        <>
          <p className="hint">Push gilt für dieses Gerät. Erinnerungen kommen ab der eingestellten Uhrzeit des Servers, einmal pro fälliger Aufgabe.</p>
          <div className="actions wrap">
            {subscribed ? (
              <>
                <button type="button" disabled={busy} onClick={() => run(async () => void (await api.testPush()), "Testnachricht gesendet.")}>
                  Test senden
                </button>
                <button type="button" disabled={busy} onClick={() => run(unsubscribe, "Benachrichtigungen ausgeschaltet.")}>
                  Ausschalten
                </button>
              </>
            ) : (
              <button
                type="button"
                className="primary"
                disabled={busy || subscribed === null}
                onClick={() => run(() => subscribe(config.vapidPublicKey!), "Benachrichtigungen eingeschaltet.")}
              >
                Auf diesem Gerät einschalten
              </button>
            )}
          </div>
        </>
      )}
    </section>
  );
}

const GEOMETRIES: GeometryKind[] = ["Point", "LineString", "Polygon"];
const FIELD_TYPES: { value: FieldType; label: string }[] = [
  { value: "Text", label: "Text" },
  { value: "Date", label: "Datum" },
  { value: "Species", label: "Pflanzenart" },
];

function ObjectTypeForm({ initial, onDone }: { initial?: ObjectType; onDone: () => void }) {
  const { reloadTypes } = useAppData();
  const toast = useToast();
  const [form, setForm] = useState<ObjectTypeInput>(
    initial ?? { name: "", icon: "📍", color: "#4caf50", allowedGeometries: ["Point", "Polygon"], fields: [] },
  );

  function toggleGeometry(kind: GeometryKind) {
    const has = form.allowedGeometries.includes(kind);
    setForm({ ...form, allowedGeometries: has ? form.allowedGeometries.filter((g) => g !== kind) : [...form.allowedGeometries, kind] });
  }

  function updateField(index: number, patch: Partial<ObjectTypeInput["fields"][number]>) {
    setForm({ ...form, fields: form.fields.map((f, i) => (i === index ? { ...f, ...patch } : f)) });
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    try {
      if (initial) await api.updateObjectType(initial.id, form);
      else await api.createObjectType(form);
      await reloadTypes();
      onDone();
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  async function remove() {
    if (!initial || !window.confirm(`Typ „${initial.name}“ löschen?`)) return;
    try {
      await api.deleteObjectType(initial.id);
      await reloadTypes();
      onDone();
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  return (
    <form className="form" onSubmit={submit}>
      <label className="field">
        <span>Name</span>
        <input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} required />
      </label>
      <div className="row">
        <label className="field">
          <span>Icon (Emoji)</span>
          <input value={form.icon} onChange={(e) => setForm({ ...form, icon: e.target.value })} maxLength={8} className="icon-input" />
        </label>
        <label className="field">
          <span>Farbe</span>
          <input type="color" value={form.color} onChange={(e) => setForm({ ...form, color: e.target.value })} />
        </label>
      </div>
      <fieldset className="field">
        <legend>Erlaubte Geometrien</legend>
        <div className="chips">
          {GEOMETRIES.map((kind) => (
            <button type="button" key={kind} className={`chip${form.allowedGeometries.includes(kind) ? " on" : ""}`} onClick={() => toggleGeometry(kind)}>
              {GEOMETRY_LABELS[kind]}
            </button>
          ))}
        </div>
      </fieldset>
      <fieldset className="field">
        <legend>Zusatzfelder</legend>
        {form.fields.map((field, index) => (
          <div className="row field-row" key={index}>
            <input value={field.label} onChange={(e) => updateField(index, { label: e.target.value })} placeholder="Bezeichnung" aria-label="Bezeichnung" required />
            <input
              value={field.key}
              onChange={(e) => updateField(index, { key: e.target.value.replace(/[^A-Za-z0-9_]/g, "") })}
              placeholder="schluessel"
              aria-label="Schlüssel"
              required
            />
            <select value={field.type} onChange={(e) => updateField(index, { type: e.target.value as FieldType })} aria-label="Feldtyp">
              {FIELD_TYPES.map((t) => (
                <option key={t.value} value={t.value}>
                  {t.label}
                </option>
              ))}
            </select>
            <button
              type="button"
              className="icon-button"
              aria-label="Feld entfernen"
              onClick={() => setForm({ ...form, fields: form.fields.filter((_, i) => i !== index) })}
            >
              ✕
            </button>
          </div>
        ))}
        <button type="button" onClick={() => setForm({ ...form, fields: [...form.fields, { key: "", label: "", type: "Text" }] })}>
          + Feld
        </button>
      </fieldset>
      <div className="actions">
        {initial && (
          <button type="button" className="danger" onClick={remove}>
            Löschen
          </button>
        )}
        <button type="submit" className="primary">
          Speichern
        </button>
      </div>
    </form>
  );
}
