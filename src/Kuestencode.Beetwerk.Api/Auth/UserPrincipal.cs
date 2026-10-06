using System.Security.Claims;
using Kuestencode.Beetwerk.Data;
using Kuestencode.Beetwerk.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Beetwerk.Api.Auth;

public static class UserPrincipal
{
    public const string StampClaim = "beetwerk:stamp";

    public static Task SignInAsync(HttpContext context, User user)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(StampClaim, user.SecurityStamp)
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    /// <summary>
    /// Prüft bei jeder Anfrage, ob es den Nutzer noch gibt und sein Passwort seit der Anmeldung unverändert ist.
    /// Sonst würde ein gelöschter Nutzer bis zum Ablauf des Cookies (30 Tage) Zugriff behalten.
    /// </summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        if (principal is null || !int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            context.RejectPrincipal();
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<BeetwerkDbContext>();
        var stamp = await db.Users
            .Where(u => u.Id == userId && u.PasswordHash != null)
            .Select(u => u.SecurityStamp)
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        if (stamp is null || stamp != (principal.FindFirstValue(StampClaim) ?? ""))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
