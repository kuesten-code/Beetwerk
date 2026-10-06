using System.Security.Claims;

namespace Kuestencode.Beetwerk.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    public static int UserId(this ClaimsPrincipal principal) =>
        int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? throw new InvalidOperationException("Kein angemeldeter Nutzer."));
}
