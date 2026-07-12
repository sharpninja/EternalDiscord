using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using EternalDiscord.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;

namespace EternalDiscord.Authentication;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddEternalAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var authentication = services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "eternaldiscord.auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromDays(14);
                options.SlidingExpiration = true;
            });

        AddProviderIfConfigured(
            authentication,
            configuration,
            "google",
            "Google",
            "https://accounts.google.com/o/oauth2/v2/auth",
            "https://oauth2.googleapis.com/token",
            "https://openidconnect.googleapis.com/v1/userinfo",
            ["openid", "profile", "email"],
            "sub",
            "name");

        AddProviderIfConfigured(
            authentication,
            configuration,
            "microsoft",
            "Microsoft",
            "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
            "https://login.microsoftonline.com/common/oauth2/v2.0/token",
            "https://graph.microsoft.com/oidc/userinfo",
            ["openid", "profile", "email"],
            "sub",
            "name");

        AddProviderIfConfigured(
            authentication,
            configuration,
            "github",
            "GitHub",
            "https://github.com/login/oauth/authorize",
            "https://github.com/login/oauth/access_token",
            "https://api.github.com/user",
            ["read:user", "user:email"],
            "id",
            "name",
            "login");

        services.AddAuthorization();
        return services;
    }

    public static IReadOnlyList<AuthProviderDto> GetAuthProviders(
        IConfiguration configuration,
        bool includeDevelopment)
    {
        var providers = new List<AuthProviderDto>
        {
            CreateStatus(configuration, "google", "Google"),
            CreateStatus(configuration, "microsoft", "Microsoft"),
            CreateStatus(configuration, "github", "GitHub")
        };

        if (includeDevelopment)
        {
            providers.Add(new AuthProviderDto("dev", "Local development", true));
        }

        return providers;
    }

    private static AuthProviderDto CreateStatus(
        IConfiguration configuration,
        string id,
        string displayName)
    {
        var section = $"Authentication:Providers:{id}";
        var configured =
            !string.IsNullOrWhiteSpace(configuration[$"{section}:ClientId"]) &&
            !string.IsNullOrWhiteSpace(configuration[$"{section}:ClientSecret"]);
        return new AuthProviderDto(id, displayName, configured);
    }

    private static void AddProviderIfConfigured(
        AuthenticationBuilder authentication,
        IConfiguration configuration,
        string id,
        string displayName,
        string authorizationEndpoint,
        string tokenEndpoint,
        string userInformationEndpoint,
        string[] scopes,
        string idClaim,
        string nameClaim,
        string? fallbackNameClaim = null)
    {
        var section = $"Authentication:Providers:{id}";
        var clientId = configuration[$"{section}:ClientId"];
        var clientSecret = configuration[$"{section}:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return;
        }

        authentication.AddOAuth(id, options =>
        {
            options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.ClientId = clientId;
            options.ClientSecret = clientSecret;
            options.CallbackPath = $"/auth/callback/{id}";
            options.AuthorizationEndpoint = authorizationEndpoint;
            options.TokenEndpoint = tokenEndpoint;
            options.UserInformationEndpoint = userInformationEndpoint;
            options.SaveTokens = true;

            foreach (var scope in scopes)
            {
                options.Scope.Add(scope);
            }

            options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, idClaim);
            options.ClaimActions.MapJsonKey(ClaimTypes.Name, nameClaim);
            options.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");

            options.Events = new OAuthEvents
            {
                OnCreatingTicket = async context =>
                {
                    using var request = new HttpRequestMessage(
                        HttpMethod.Get,
                        context.Options.UserInformationEndpoint);
                    request.Headers.Authorization = new AuthenticationHeaderValue(
                        "Bearer",
                        context.AccessToken);
                    request.Headers.UserAgent.ParseAdd("EternalDiscord/1.0");

                    using var response = await context.Backchannel.SendAsync(
                        request,
                        context.HttpContext.RequestAborted);
                    response.EnsureSuccessStatusCode();

                    using var document = JsonDocument.Parse(
                        await response.Content.ReadAsStreamAsync(context.HttpContext.RequestAborted));
                    context.RunClaimActions(document.RootElement);

                    if (!string.IsNullOrWhiteSpace(fallbackNameClaim) &&
                        context.Identity is not null &&
                        !context.Identity.HasClaim(claim => claim.Type == ClaimTypes.Name) &&
                        document.RootElement.TryGetProperty(fallbackNameClaim, out var fallbackName))
                    {
                        context.Identity.AddClaim(new Claim(
                            ClaimTypes.Name,
                            fallbackName.GetString() ?? displayName));
                    }
                }
            };
        });
    }
}
