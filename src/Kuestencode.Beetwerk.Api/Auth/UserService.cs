using System.Text.RegularExpressions;
using Kuestencode.Beetwerk.Data;
using Kuestencode.Beetwerk.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Beetwerk.Api.Auth;

public partial class UserService(BeetwerkDbContext db)
{
    public const string ImplicitUsername = "garten";
    public const int MinPasswordLength = 8;

    private static readonly PasswordHasher<User> Hasher = new();

    public async Task<User?> VerifyAsync(string username, string password, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (user?.PasswordHash is null)
        {
            // Gleicher Aufwand wie bei einem echten Nutzer, damit die Antwortzeit nichts über existierende Namen verrät.
            Hasher.VerifyHashedPassword(new User { Username = username }, DummyHash, password);
            return null;
        }

        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
            return null;

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, password);
            await db.SaveChangesAsync(ct);
        }
        return user;
    }

    /// <summary>Legt einen neuen Nutzer an; wirft <see cref="InvalidOperationException"/>, wenn es ihn schon gibt.</summary>
    public async Task<User> CreateAsync(string username, string password, CancellationToken ct = default)
    {
        username = ValidateUsername(username);
        ValidatePassword(password);
        if (await db.Users.AnyAsync(u => u.Username == username, ct))
            throw new InvalidOperationException($"Den Nutzer '{username}' gibt es bereits.");

        var user = db.Users.Add(new User { Username = username }).Entity;
        ApplyPassword(user, password);
        await db.SaveChangesAsync(ct);
        return user;
    }

    /// <summary>Legt den Nutzer an oder setzt sein Passwort neu. Bestehende Sitzungen des Nutzers werden dabei ungültig.</summary>
    public async Task<bool> SetPasswordAsync(string username, string password, CancellationToken ct = default)
    {
        ValidatePassword(password);
        var user = await FindAsync(username, ct);
        if (user is null)
        {
            await CreateAsync(username, password, ct);
            return true;
        }

        ApplyPassword(user, password);
        await db.SaveChangesAsync(ct);
        return false;
    }

    public async Task<bool> EnsureUserAsync(string username, string password, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(u => u.Username == username, ct))
            return false;
        await CreateAsync(username, password, ct);
        return true;
    }

    public Task<User?> FindAsync(string username, CancellationToken ct = default) =>
        db.Users.FirstOrDefaultAsync(u => u.Username == username && u.PasswordHash != null, ct);

    /// <summary>Löscht einen Nutzer; der letzte Nutzer mit Passwort bleibt erhalten, damit sich noch jemand anmelden kann.</summary>
    public async Task<bool> DeleteAsync(string username, CancellationToken ct = default)
    {
        var user = await FindAsync(username, ct);
        if (user is null)
            return false;
        if (await db.Users.CountAsync(u => u.PasswordHash != null, ct) <= 1)
            throw new InvalidOperationException("Der letzte Nutzer kann nicht gelöscht werden.");

        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<List<string>> ListAsync(CancellationToken ct = default) =>
        db.Users.Where(u => u.PasswordHash != null).OrderBy(u => u.Username).Select(u => u.Username).ToListAsync(ct);

    public async Task<User> EnsureImplicitUserAsync(CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == ImplicitUsername, ct);
        if (user is not null)
            return user;

        user = db.Users.Add(new User { Username = ImplicitUsername }).Entity;
        await db.SaveChangesAsync(ct);
        return user;
    }

    public static string ValidateUsername(string? username)
    {
        var trimmed = username?.Trim() ?? "";
        if (!UsernamePattern().IsMatch(trimmed))
            throw new ArgumentException("Der Benutzername darf 1–100 Zeichen lang sein und nur Buchstaben, Ziffern, Punkt, Binde- und Unterstrich enthalten.");
        if (trimmed.Equals(ImplicitUsername, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Der Name '{ImplicitUsername}' ist für den Modus ohne Anmeldung reserviert.");
        return trimmed;
    }

    private static void ValidatePassword(string? password)
    {
        if (password is null || password.Length < MinPasswordLength)
            throw new ArgumentException($"Das Passwort muss mindestens {MinPasswordLength} Zeichen lang sein.");
    }

    private static void ApplyPassword(User user, string password)
    {
        user.PasswordHash = Hasher.HashPassword(user, password);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
    }

    [GeneratedRegex(@"^[\p{L}\p{N}._-]{1,100}$")]
    private static partial Regex UsernamePattern();

    private static readonly string DummyHash = Hasher.HashPassword(new User { Username = "-" }, Guid.NewGuid().ToString());
}
