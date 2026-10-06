namespace Kuestencode.Beetwerk.Domain.Devices;

/// <summary>
/// Andockpunkt für die Geräteanbindung in v3 (z. B. Husqvarna Automower Connect, Home Assistant).
/// Bewusst leer: Status lesen und Befehle senden werden erst mit dem ersten echten Provider definiert,
/// damit das Kernmodell (Objekttyp „Gerät“ mit Geometrie) dafür nicht geändert werden muss.
/// </summary>
public interface IDeviceProvider;
