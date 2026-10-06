namespace Kuestencode.Beetwerk.Api.Auth;

/// <summary>Standardnutzer im Modus AUTH_MODE=none; die Id wird beim Start aus der Datenbank gesetzt.</summary>
public sealed class ImplicitUser
{
    public int Id { get; set; }
}
