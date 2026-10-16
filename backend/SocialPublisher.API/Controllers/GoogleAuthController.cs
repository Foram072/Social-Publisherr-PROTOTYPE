using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace SocialPublisher.API.Controllers;

[ApiController]
[Route("api/google-auth")]
public class GoogleAuthController : ControllerBase
{
    // STEP 1: open this URL in the browser to START the login.
    // e.g. https://hrzv4tg1-7264.inc1.devtunnels.ms/api/google-auth/login
    [HttpGet("login")]
    public IActionResult Login()
    {
        var props = new AuthenticationProperties
        {
            // Where the app continues AFTER the middleware has handled /signin-google
            RedirectUri = Url.Action(nameof(Callback))
        };
        return Challenge(props, "Google");
    }

    // STEP 2: Google -> /signin-google (handled automatically by the middleware) -> this action.
    [HttpGet("callback")]
    public async Task<IActionResult> Callback()
    {
        var result = await HttpContext.AuthenticateAsync("External");
        if (!result.Succeeded || result.Principal == null)
            return Unauthorized(new { message = "Google login failed or the login cookie expired. Start again from /api/google-auth/login" });

        var p = result.Principal;
        var googleId = p.FindFirstValue(ClaimTypes.NameIdentifier);
        var email    = p.FindFirstValue(ClaimTypes.Email);
        var name     = p.FindFirstValue(ClaimTypes.Name);

        // The temporary cookie is no longer needed
        await HttpContext.SignOutAsync("External");

        // TODO (next step): find or create the user in the database, then call TokenService
        // to issue your JWT and redirect to the frontend with it.
        return Ok(new { googleId, email, name });
    }
}
