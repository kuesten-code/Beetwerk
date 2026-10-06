using Kuestencode.Beetwerk.Domain.Enums;

namespace Kuestencode.Beetwerk.Domain.Entities;

/// <summary>
/// Symmetrische Beziehung zwischen zwei Pflanzenarten. Gespeichert wird immer mit
/// <see cref="SpeciesAId"/> &lt; <see cref="SpeciesBId"/>, damit pro Artenpaar nur ein Eintrag existieren kann.
/// </summary>
public class NeighborRelation
{
    public int Id { get; set; }
    public int SpeciesAId { get; private set; }
    public PlantSpecies? SpeciesA { get; set; }
    public int SpeciesBId { get; private set; }
    public PlantSpecies? SpeciesB { get; set; }
    public NeighborRating Rating { get; set; }
    public string? Note { get; set; }
    public string? Source { get; set; }

    public void SetPair(int speciesId, int otherSpeciesId)
    {
        if (speciesId == otherSpeciesId)
            throw new ArgumentException("Eine Art kann keine Beziehung zu sich selbst haben.");

        (SpeciesAId, SpeciesBId) = Normalize(speciesId, otherSpeciesId);
    }

    public int OtherThan(int speciesId) => speciesId == SpeciesAId ? SpeciesBId : SpeciesAId;

    public static (int A, int B) Normalize(int first, int second) =>
        first < second ? (first, second) : (second, first);
}
