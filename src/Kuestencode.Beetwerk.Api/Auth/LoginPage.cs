using System.Net;

namespace Kuestencode.Beetwerk.Api.Auth;

/// <summary>
/// Serverseitig gerenderte Login-Seite. Sie ist neben dem Health-Check die einzige anonym erreichbare Ressource;
/// die eigentliche App (inkl. JS/CSS) liegt bereits hinter der Anmeldung und kann das Formular deshalb nicht liefern.
/// </summary>
public static class LoginPage
{
    public static string Render(string? returnUrl, string? error)
    {
        var message = error switch
        {
            "invalid" => "Benutzername oder Passwort ist falsch.",
            "locked" => "Zu viele Fehlversuche. Bitte später erneut versuchen.",
            null or "" => null,
            _ => "Anmeldung fehlgeschlagen."
        };
        var errorHtml = message is null ? "" : $"<p class=\"error\" role=\"alert\">{WebUtility.HtmlEncode(message)}</p>";
        var returnUrlHtml = WebUtility.HtmlEncode(returnUrl ?? "/");

        return $$"""
            <!doctype html>
            <html lang="de">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="theme-color" content="#2e7d32">
            <title>Beetwerk – Anmelden</title>
            <style>
              :root { color-scheme: light dark; --bg:#f4f7f2; --card:#fff; --fg:#1f2a1d; --muted:#5f6b5c; --accent:#2e7d32; --err:#c62828; --border:#d5dccf; }
              @media (prefers-color-scheme: dark) { :root { --bg:#121712; --card:#1c241b; --fg:#e7eee4; --muted:#9fae9a; --accent:#43a047; --err:#ef9a9a; --border:#33402f; } }
              * { box-sizing: border-box; }
              body { margin:0; min-height:100dvh; display:grid; place-items:center; background:var(--bg); color:var(--fg); font:16px/1.5 system-ui, sans-serif; padding:16px; }
              form { width:100%; max-width:360px; background:var(--card); border:1px solid var(--border); border-radius:16px; padding:24px; display:grid; gap:14px; }
              h1 { margin:0; font-size:1.5rem; } p { margin:0; color:var(--muted); }
              label { display:grid; gap:6px; font-weight:600; }
              input { font:inherit; padding:12px; border-radius:10px; border:1px solid var(--border); background:transparent; color:inherit; min-height:48px; }
              button { font:inherit; font-weight:600; min-height:48px; border:0; border-radius:10px; background:var(--accent); color:#fff; cursor:pointer; }
              .error { color:var(--err); font-weight:600; }
            </style>
            </head>
            <body>
            <form method="post" action="/login">
              <h1>🌱 Beetwerk</h1>
              <p>Bitte melde dich an.</p>
              {{errorHtml}}
              <label>Benutzername <input name="username" autocomplete="username" autocapitalize="none" required autofocus></label>
              <label>Passwort <input name="password" type="password" autocomplete="current-password" required></label>
              <input type="hidden" name="returnUrl" value="{{returnUrlHtml}}">
              <button type="submit">Anmelden</button>
            </form>
            </body>
            </html>
            """;
    }
}
