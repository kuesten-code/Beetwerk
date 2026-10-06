namespace Kuestencode.Beetwerk.Domain.Enums;

public enum ObjectFieldType
{
    Text,
    Date,
    /// <summary>Verweis auf eine Pflanzenart; der Wert liegt in <c>GardenObject.PlantSpeciesId</c>, nicht in den Attributen.</summary>
    Species
}
