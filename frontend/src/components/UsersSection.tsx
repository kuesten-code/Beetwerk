import { useCallback, useEffect, useState, type FormEvent } from "react";
import { api, errorMessage } from "../lib/api";
import type { AppUser } from "../lib/types";
import { Sheet } from "./Sheet";
import { useToast } from "./Toast";

const MIN_PASSWORD_LENGTH = 8;

export function UsersSection() {
  const toast = useToast();
  const [users, setUsers] = useState<AppUser[]>([]);
  const [dialog, setDialog] = useState<{ kind: "create" } | { kind: "password"; user: AppUser } | null>(null);

  const load = useCallback(() => {
    api.users().then(setUsers, (e) => toast.error(errorMessage(e)));
  }, [toast]);

  useEffect(load, [load]);

  async function remove(user: AppUser) {
    if (!window.confirm(`Nutzer „${user.username}“ löschen? Seine Geräte bekommen dann keine Benachrichtigungen mehr.`)) return;
    try {
      await api.deleteUser(user.username);
      toast.show(`„${user.username}“ gelöscht.`);
      load();
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  return (
    <section>
      <div className="section-header">
        <h2>Nutzer</h2>
        <button type="button" className="primary" onClick={() => setDialog({ kind: "create" })}>
          + Nutzer
        </button>
      </div>
      <ul className="user-list">
        {users.map((user) => (
          <li key={user.username}>
            <span className="user-name">
              👤 <strong>{user.username}</strong>
              {user.isCurrent && <small className="muted"> (du)</small>}
            </span>
            <span className="row">
              <button type="button" onClick={() => setDialog({ kind: "password", user })}>
                Passwort
              </button>
              {!user.isCurrent && (
                <button type="button" className="danger" onClick={() => remove(user)} aria-label={`${user.username} löschen`}>
                  Löschen
                </button>
              )}
            </span>
          </li>
        ))}
      </ul>
      <p className="hint">Alle Nutzer sehen denselben Garten und dürfen weitere Nutzer anlegen.</p>

      {dialog && (
        <Sheet
          title={dialog.kind === "create" ? "Neuer Nutzer" : `Passwort für ${dialog.user.username}`}
          onClose={() => setDialog(null)}
        >
          <UserForm
            username={dialog.kind === "password" ? dialog.user.username : undefined}
            onDone={(message) => {
              setDialog(null);
              toast.show(message);
              load();
            }}
          />
        </Sheet>
      )}
    </section>
  );
}

function UserForm({ username, onDone }: { username?: string; onDone: (message: string) => void }) {
  const toast = useToast();
  const [name, setName] = useState("");
  const [password, setPassword] = useState("");
  const [repeat, setRepeat] = useState("");
  const [saving, setSaving] = useState(false);
  const mismatch = repeat.length > 0 && password !== repeat;

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (password !== repeat) return;
    setSaving(true);
    try {
      if (username) {
        await api.setPassword(username, password);
        onDone("Passwort geändert. Andere Sitzungen dieses Nutzers sind abgemeldet.");
      } else {
        await api.createUser(name, password);
        onDone(`„${name.trim()}“ angelegt.`);
      }
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      setSaving(false);
    }
  }

  return (
    <form className="form" onSubmit={submit}>
      {!username && (
        <label className="field">
          <span>Benutzername</span>
          <input value={name} onChange={(e) => setName(e.target.value)} autoCapitalize="none" autoComplete="off" required autoFocus />
        </label>
      )}
      <label className="field">
        <span>Passwort (mind. {MIN_PASSWORD_LENGTH} Zeichen)</span>
        <input
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          minLength={MIN_PASSWORD_LENGTH}
          autoComplete="new-password"
          required
          autoFocus={Boolean(username)}
        />
      </label>
      <label className="field">
        <span>Passwort wiederholen</span>
        <input type="password" value={repeat} onChange={(e) => setRepeat(e.target.value)} autoComplete="new-password" required />
      </label>
      {mismatch && <p className="hint error-text">Die Passwörter stimmen nicht überein.</p>}
      <div className="actions">
        <button type="submit" className="primary" disabled={saving || mismatch || password.length < MIN_PASSWORD_LENGTH}>
          Speichern
        </button>
      </div>
    </form>
  );
}
