# Beetwerk

Beetwerk – selbst gehosteter Gartenplaner mit Satellitenkarte. Beete, Hecken, Gewächshaus und Pflanzen als Punkt, Linie oder Fläche einzeichnen, Aufgaben mit Wiederholung und Push verwalten. ASP.NET Core, React, MapLibre, SQLite, Docker. Eine Installation pro Garten.

## Funktionen

- **Luftbildkarte**: standardmäßig Orthofotos (DOP20) des Landes Schleswig-Holstein, darunter Esri World Imagery als Fallback. Beide Quellen sind konfigurierbar.
- **Objekte** als Punkt, Linie oder Fläche: Pflanze, Beet, Hecke, Gewächshaus, Stall, Gerät und Sonstiges. Die Typen sind erweiterbar und haben eigene Zusatzfelder. Objekte lassen sich verschieben, umformen und ineinander verschachteln (z. B. Pflanze im Beet).
- **Pflanzenarten** mit externem Link (z. B. NaturaDB, nur als Verweis) und selbst gepflegten **guten/schlechten Nachbarn**. Es werden bewusst keine Mischkultur-Daten mitgeliefert.
- **Aufgaben** an Objekten oder an einem freien Ort auf der Karte: einmalig oder wiederkehrend (täglich, wöchentlich, monatlich, jährlich, mit Intervall und optionalem Saisonfenster). Die Regeln lassen sich als iCalendar-RRULE abbilden.
- **Eigene Luftbilder und Pläne** (z. B. Drohnenaufnahme oder gezeichneter Gartenplan): auf der Karte über ☰ → „Bild hinzufügen …“ einfügen und über vier Ecken auf passende Punkte ziehen. Verwaltung (Deckkraft, Anzeigen, Löschen) unter **Mehr**.
- **Warnung bei schlechten Nachbarn**: Stehen Pflanzen, deren Arten als schlechte Nachbarn eingetragen sind, näher als der Warnabstand (Standard 1 m, einstellbar unter **Mehr**), werden sie auf der Karte rot markiert und im Objekt aufgeführt.
- **Pflanzenvorlagen**: typische Aufgaben je Art (z. B. „Ausgeizen, wöchentlich Juni bis August“). Sie werden beim Anlegen einer Pflanze dieser Art zur Übernahme angeboten.
- **Verlauf und Fotos** je Objekt: Notizen, Fotos (direkt mit der Handykamera), angelegt und erledigte Aufgaben werden automatisch festgehalten.
- **Mähroboter-Anbindung** (Husqvarna Automower Connect oder Home Assistant): Status, Akku und nächster Start am Geräte-Pin und im Objekt. Starten, Pausieren und Parken sowie die Schnitthöhe direkt aus der App. Meldet der Mäher einen Fehler, entsteht automatisch eine Aufgabe mit Push.
- **Kalender-Export**: Aufgaben eines Jahres als `.ics` (📅 in der Aufgabenliste). Wiederholungen als Serie, Erinnerungen als Alarm, hinter der normalen Anmeldung, ohne öffentlichen Feed.
- **Push-Benachrichtigungen** (Web Push/VAPID). Der Scheduler prüft stündlich und meldet jede fällige Aufgabe einmal. Ein Klick öffnet die Aufgabe.
- **PWA**, mobile first: Bedienung mit dem Finger, zum Home-Bildschirm hinzufügbar.
- **Anmeldung** zentral für alles erzwungen: Oberfläche, API, Kartenkacheln und Push-Endpunkte. Ausgenommen sind nur `/health` und `/login`.

## Schnellstart (Docker Compose)

```bash
git clone https://github.com/kuesten-code/Beetwerk.git && cd Beetwerk
cp .env.example .env          # ADMIN_PASSWORD setzen, ggf. PUBLIC_URL, Kartenquelle
mkdir -p data                 # vor dem ersten Start anlegen, damit es dir gehört
docker compose up -d --build
```

`--build` baut das Image lokal aus dem Quellcode. Ohne lokalen Build geht es mit den fertigen Images (amd64 und arm64, also auch auf dem Raspberry Pi):

```bash
docker compose pull && docker compose up -d     # kuestencode/beetwerk:latest von Docker Hub
```

Eine feste Version wählst du mit `BEETWERK_VERSION=0.2.0` in der `.env`. Die Images liegen auch unter `ghcr.io/kuesten-code/beetwerk`.

Danach läuft Beetwerk unter `http://127.0.0.1:8080`. Ein Login ist mit `ADMIN_USERNAME`/`ADMIN_PASSWORD` möglich.

Der Container läuft mit der UID/GID aus `BEETWERK_UID`/`BEETWERK_GID` (Standard 1000:1000). Diese muss `./data` beschreiben dürfen. Falls Docker `data` als root angelegt hat, hilft `sudo chown -R 1000:1000 data`.


## Konfiguration

Alle Einstellungen stehen kommentiert in [.env.example](.env.example). Die wichtigsten:

| Variable | Standard | Bedeutung |
|---|---|---|
| `PORT`, `BIND_ADDRESS` | `8080`, `127.0.0.1` | Adresse auf dem Host |
| `PUBLIC_URL` | – | Öffentliche Adresse, z. B. `https://garten.example.de` |
| `AUTH_MODE` | `login` | `login` oder `none` |
| `ADMIN_USERNAME`, `ADMIN_PASSWORD` | – | Wird beim Start angelegt, falls noch nicht vorhanden |
| `AUTH_ALLOW_NONE_PUBLIC` | `false` | Erlaubt `AUTH_MODE=none` trotz gesetzter `PUBLIC_URL` |
| `PUSH_ENABLED` | `true` | Push global an/aus |
| `VAPID_PUBLIC_KEY`, `VAPID_PRIVATE_KEY`, `VAPID_SUBJECT` | automatisch | Leer = werden erzeugt und in `data/keys/vapid.json` gespeichert |
| `NOTIFY_HOUR` | `8` | Ab dieser Stunde (Ortszeit, `TZ`) werden Erinnerungen verschickt |
| `MAP_PRIMARY_URL`, `MAP_PRIMARY_ATTRIBUTION` | DOP20 SH | Kachel-URL mit `{z}/{x}/{y}` oder WMS mit `{bbox-epsg-3857}` |
| `MAP_FALLBACK_URL` | Esri World Imagery | `none` schaltet den Fallback ab |

### Anmeldung

- **`AUTH_MODE=login`** (Standard): Die Anmeldung läuft über ein Cookie, gültig 30 Tage. Passwörter werden mit PBKDF2 gehasht. Es gibt keine Selbstregistrierung. Gegen Brute Force greifen zwei Sperren: maximal 10 Login-Versuche pro 5 Minuten und IP, und nach 5 Fehlversuchen wird der Benutzername für 15 Minuten gesperrt.
- **`AUTH_MODE=none`**: keine Anmeldung, beim Start wird eine deutliche Warnung geloggt. Nur für Heimnetz/VPN gedacht oder wenn der Reverse Proxy absichert. Ist `PUBLIC_URL` gesetzt, startet die App in diesem Modus nicht, außer mit `AUTH_ALLOW_NONE_PUBLIC=true`.

Nutzer verwalten: in der App unter **Mehr → Nutzer** (anlegen, Passwort setzen, löschen). Alle Nutzer sind gleichberechtigt. Man kann sich nicht selbst löschen, und der letzte Nutzer bleibt immer erhalten. Löschen oder ein neues Passwort beendet die bestehenden Sitzungen dieses Nutzers sofort.

Alternativ über die Kommandozeile im laufenden Container, etwa wenn niemand mehr sein Passwort weiß:

```bash
docker compose exec beetwerk dotnet Kuestencode.Beetwerk.Api.dll user list
docker compose exec -it beetwerk dotnet Kuestencode.Beetwerk.Api.dll user set anna      # fragt das Passwort ab
docker compose exec beetwerk dotnet Kuestencode.Beetwerk.Api.dll user delete anna
```

`ADMIN_PASSWORD` wirkt nur beim ersten Anlegen. Ein späteres Passwort ändert man mit `user set`.

### Kartenquelle

Kacheln werden über den Server geladen (`/tiles/...`). Viele Geodienste, darunter der WMS für Schleswig-Holstein, erlauben keinen direkten Abruf aus dem Browser (CORS). Außerdem bleiben die Kacheln so hinter der Anmeldung, und die IP-Adressen der Nutzer gehen nicht an Dritte.

- **DOP20 Schleswig-Holstein** (Standard): `© GeoBasis-DE/LVermGeo SH/CC BY 4.0`. Die Nutzung ist frei bei Namensnennung, die Attribution wird in der Karte angezeigt.
- **Esri World Imagery** (Fallback): Es gelten die [Esri-Nutzungsbedingungen](https://www.esri.com/en-us/legal/terms/full-master-agreement). Vor dem Einsatz prüfen, ob die private Nutzung abgedeckt ist, sonst mit `MAP_FALLBACK_URL=none` abschalten.
- Andere Bundesländer: Die meisten Vermessungsverwaltungen bieten DOP als WMS an. Die URL dann mit `CRS=EPSG:3857&BBOX={bbox-epsg-3857}&WIDTH=256&HEIGHT=256` angeben.


## Mähroboter anbinden

### Husqvarna Automower Connect

1. Auf [developer.husqvarnagroup.cloud](https://developer.husqvarnagroup.cloud) mit dem Husqvarna-Konto anmelden, mit dem auch die Automower-App läuft.
2. **Create application**: Name beliebig, Redirect-URL z. B. `http://localhost`.
3. Der Anwendung die APIs **Authentication API** und **Automower Connect API** hinzufügen (*Connect new API*).
4. **Application Key** und **Application Secret** in die `.env` eintragen:
   ```
   HUSQVARNA_CLIENT_ID=<Application Key>
   HUSQVARNA_CLIENT_SECRET=<Application Secret>
   ```
5. `docker compose up -d`, dann in der App ein Objekt vom Typ **Gerät** antippen → **Gerät** → *Geräte suchen* → *Verbinden*.

Die Automower-API erlaubt 10 000 Anfragen pro Monat. Beetwerk fragt deshalb standardmäßig alle 10 Minuten ab (`HUSQVARNA_POLL_SECONDS`) und nach Befehlen einmal gezielt nach. Die Schnitthöhe ist eine Stufe von 1 bis 9 wie in der Automower-App.

### Home Assistant

`HOMEASSISTANT_URL` (z. B. `http://homeassistant.local:8123`) und ein langlebiges Zugangstoken (HA → Profil → Sicherheit) in die `.env` eintragen. Verbunden wird eine `lawn_mower.*`-Entity. Akku, Schnitthöhe und Fehler werden aus den Entities der Husqvarna-Integration abgeleitet (`sensor.<name>_battery`, `number.<name>_cutting_height`, `sensor.<name>_error`) oder beim Verbinden einzeln angegeben. Über Home Assistant gibt es Starten, Pausieren und Parken; ein Start ist dort nur innerhalb des Zeitplans aus der Automower-App möglich.

## Push-Benachrichtigungen

- Push braucht **HTTPS**. Ist `PUBLIC_URL` eine `http://`-Adresse (außer `localhost`), schaltet Beetwerk Push automatisch ab. Mit `PUSH_ENABLED=false` lässt es sich auch ganz abschalten.
- Einschalten pro Gerät unter **Mehr → Benachrichtigungen**. Dort gibt es auch eine Testnachricht.
- **iPhone/iPad**: Push gibt es nur, wenn Beetwerk über *Teilen → Zum Home-Bildschirm* installiert wurde und von dort geöffnet wird (ab iOS 16.4).
- Aufgaben gehören keinem einzelnen Nutzer. Erinnerungen gehen daher an alle registrierten Geräte.
- Die VAPID-Schlüssel gehören zum Backup. Neue Schlüssel machen bestehende Abos ungültig; die Geräte melden sich beim nächsten Öffnen der App automatisch neu an.

## Backup

Alle Daten liegen in `./data`:

| Pfad | Inhalt |
|---|---|
| `data/beetwerk.db` | SQLite-Datenbank (Garten, Objekte, Arten, Aufgaben, Nutzer, Push-Abos) |
| `data/uploads/` | Fotos der Objekte und eigene Luftbilder/Pläne |
| `data/keys/` | Schlüssel für Login-Cookies und VAPID |

Ein konsistentes Backup im laufenden Betrieb:

```bash
sqlite3 data/beetwerk.db ".backup 'backup/beetwerk-$(date +%F).db'"
tar czf backup/beetwerk-files-$(date +%F).tgz data/uploads data/keys
```

Alternativ `docker compose stop`, den Ordner `data` kopieren und wieder starten. Zum Wiederherstellen die Dateien zurück nach `data` legen. Datenbankmigrationen laufen beim Start automatisch.

## Entwicklung

Voraussetzungen: .NET SDK 10, Node.js 24.

```bash
# Backend (http://localhost:5080, legt Nutzer admin / admin-dev-passwort an)
dotnet run --project src/Kuestencode.Beetwerk.Api

# Frontend mit Hot Reload (http://localhost:5173, leitet /api, /login, /tiles ans Backend weiter)
cd frontend && npm install && npm run dev
```

Über `localhost` funktionieren Service Worker und Web Push auch ohne Zertifikat.

```bash
dotnet test                       # Backend-Tests (Unit + Integration mit WebApplicationFactory)
cd frontend && npm test           # Frontend-Tests (Vitest)
cd frontend && npm run build      # baut nach src/Kuestencode.Beetwerk.Api/wwwroot
```

### Aufbau

```
src/
  Kuestencode.Beetwerk.Domain/   Entities, Wiederholungsregeln (RRULE-kompatibel), GeoJSON-Prüfung, IDeviceProvider
  Kuestencode.Beetwerk.Data/     EF Core DbContext (SQLite), Migrationen, Seed der Standardtypen
  Kuestencode.Beetwerk.Api/      Minimal APIs, Auth, Kachel-Proxy, Web Push, Scheduler, CLI
tests/Kuestencode.Beetwerk.Tests/
frontend/                        React + TypeScript + Vite, MapLibre GL JS, Terra Draw
```

Geometrien werden als GeoJSON-Text (WGS84) gespeichert. Ein Wechsel auf PostgreSQL/PostGIS bleibt über EF Core möglich, falls später räumliche Abfragen nötig werden.

## Ausblick

- **Später**: GeoTIFF direkt aus WebODM übernehmen (Position automatisch aus der Datei).
- **Später**: Gerätestatus live per WebSocket statt Abfrage, Arbeitsbereiche (Work Areas) des Automowers, weitere Geräte über Home Assistant (Bewässerung, Sensoren).
