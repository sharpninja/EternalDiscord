using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EternalDiscord.Authentication;

/// <summary>
/// Authenticates requests from the EternalSocial gateway. The gateway performs the
/// Google OIDC sign-in for every Eternal site and forwards the resulting identity on
/// each proxied request as X-Auth-* headers, proven by the shared X-Gateway-Key.
/// Active only when the GATEWAY_KEY configuration value is present; headers are never
/// trusted without the matching key.
/// </summary>
public sealed class GatewayAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Gateway";

    private readonly IConfiguration configuration;

    public GatewayAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        this.configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var key = configuration["GATEWAY_KEY"];
        if (string.IsNullOrEmpty(key) || Request.Headers["X-Gateway-Key"] != key)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var userId = Request.Headers["X-Auth-UserId"].ToString();
        if (string.IsNullOrEmpty(userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId)
        };

        var name = Request.Headers["X-Auth-Name"].ToString();
        if (name.Length > 0)
        {
            claims.Add(new Claim(ClaimTypes.Name, name));
        }

        var email = Request.Headers["X-Auth-Email"].ToString();
        if (email.Length > 0)
        {
            claims.Add(new Claim(ClaimTypes.Email, email));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
