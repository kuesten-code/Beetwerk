using System.Security.Claims;
using Kuestencode.Beetwerk.Api.Auth;
using Kuestencode.Beetwerk.Api.Configuration;
using Kuestencode.Beetwerk.Api.Contracts;

namespace Kuestencode.Beetwerk.Api.Endpoints;

/// <summary>
/// Nutzerverwaltung für angemeldete Nutzer. Es gibt bewusst keine Rollen: Beetwerk ist für eine Familie gedacht,
/// in der jeder weitere Mitglieder anlegen darf. Selbstregistrierung gibt es weiterhin nicht.
/// </summary>
public static class UserEndpoints
{
    public static void MapUserEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/users");

        group.MapGet("/", async (UserService users, ClaimsPrincipal current, CancellationToken ct) =>
            (await users.ListAsync(ct)).Select(name => new UserDto(name, IsCurrent(current, name))));

        group.MapPost("/", async (CreateUserInput input, UserService users, CancellationToken ct) =>
        {
            try
            {
                var user = await users.CreateAsync(input.Username, input.Password, ct);
                return Results.Created($"/api/users/{Uri.EscapeDataString(user.Username)}", new UserDto(user.Username, false));
            }
            catch (ArgumentException ex)
            {
                return ApiResults.Error(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return ApiResults.Conflict(ex.Message);
            }
        });

        group.MapPut("/{username}/password", async (string username, PasswordInput input, UserService users,
            ClaimsPrincipal current, HttpContext context, BeetwerkOptions options, CancellationToken ct) =>
        {
            var user = await users.FindAsync(username, ct);
            if (user is null)
                return Results.NotFound();
            try
            {
                await users.SetPasswordAsync(user.Username, input.Password, ct);
            }
            catch (ArgumentException ex)
            {
                return ApiResults.Error(ex.Message);
            }

            // Der neue Stempel beendet alle Sitzungen des Nutzers – die eigene wird gleich mit dem neuen erneuert.
            if (options.AuthMode == AuthMode.Login && IsCurrent(current, user.Username))
                await UserPrincipal.SignInAsync(context, user);
            return Results.NoContent();
        });

        group.MapDelete("/{username}", async (string username, UserService users, ClaimsPrincipal current, CancellationToken ct) =>
        {
            if (IsCurrent(current, username))
                return ApiResults.Error("Du kannst dich nicht selbst löschen.");
            try
            {
                return await users.DeleteAsync(username, ct) ? Results.NoContent() : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return ApiResults.Conflict(ex.Message);
            }
        });
    }

    private static bool IsCurrent(ClaimsPrincipal current, string username) =>
        string.Equals(current.Identity?.Name, username, StringComparison.OrdinalIgnoreCase);
}
