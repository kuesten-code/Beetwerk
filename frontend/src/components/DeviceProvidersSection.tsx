import { useEffect, useState } from "react";
import { api } from "../lib/api";
import type { DeviceProviderInfo } from "../lib/types";

const SETUP: Record<string, string> = {
  husqvarna: "HUSQVARNA_CLIENT_ID und HUSQVARNA_CLIENT_SECRET (Application Key/Secret aus developer.husqvarnagroup.cloud)",
  homeassistant: "HOMEASSISTANT_URL und HOMEASSISTANT_TOKEN (Langlebiges Zugangstoken aus dem HA-Profil)",
};

export function DeviceProvidersSection() {
  const [providers, setProviders] = useState<DeviceProviderInfo[]>([]);

  useEffect(() => {
    api.deviceProviders().then(setProviders, () => setProviders([]));
  }, []);

  return (
    <section>
      <h2>Geräteanbindung</h2>
      <ul className="link-list">
        {providers.map((p) => (
          <li key={p.key} className="provider-row">
            <span>{p.configured ? "✅" : "⚪"}</span>
            <span>
              <strong>{p.name}</strong>
              <small className="muted"> {p.configured ? "eingerichtet" : `nicht eingerichtet – ${SETUP[p.key] ?? "Zugangsdaten fehlen"} in der .env`}</small>
            </span>
          </li>
        ))}
      </ul>
      <p className="hint">Ein Gerät verbindest du auf der Karte: Objekt vom Typ „Gerät“ antippen → Abschnitt „Gerät“.</p>
    </section>
  );
}
