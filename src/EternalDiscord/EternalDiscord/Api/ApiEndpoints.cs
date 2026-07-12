using System.Security.Claims;
using EternalDiscord.Authentication;
using EternalDiscord.Contracts;
using EternalDiscord.Domain;
using EternalDiscord.Persistence;
using EternalDiscord.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace EternalDiscord.Api;

public static class ApiEndpoints
{
    public static WebApplication MapEternalDiscordEndpoints(this WebApplication app)
    {
        MapAuthentication(app);
        MapCommunityApi(app);
        return app;
    }

    private static void MapAuthentication(WebApplication app)
    {
        app.MapGet("/api/auth/providers", (
            IConfiguration configuration,
            IWebHostEnvironment environment) =>
            Results.Ok(AuthenticationExtensions.GetAuthProviders(
                configuration,
                environment.IsDevelopment())));

        app.MapGet("/auth/login/{provider}", async (
            string provider,
            string? returnUrl,
            HttpContext context,
            IConfiguration configuration,
            IWebHostEnvironment environment) =>
        {
            var destination = GetSafeReturnUrl(returnUrl);
            if (provider.Equals("dev", StringComparison.OrdinalIgnoreCase))
            {
                if (!environment.IsDevelopment())
                {
                    return Results.NotFound();
                }

                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "dev:developer"),
                    new Claim(ClaimTypes.Name, "Local Developer")
                };
                var identity = new ClaimsIdentity(
                    claims,
                    CookieAuthenticationDefaults.AuthenticationScheme);
                await context.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity));
                return Results.Redirect(destination);
            }

            var configured = AuthenticationExtensions.GetAuthProviders(configuration, false)
                .Any(item =>
                    item.IsConfigured &&
                    item.Id.Equals(provider, StringComparison.OrdinalIgnoreCase));
            if (!configured)
            {
                return Results.NotFound();
            }

            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = destination },
                [provider.ToLowerInvariant()]);
        });

        app.MapPost("/auth/logout", async (HttpContext context) =>
        {
            if (!HasMutationHeader(context))
            {
                return Results.BadRequest(new ApiError(
                    "invalid_request",
                    "The required same-origin request header is missing."));
            }

            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
    }

    private static void MapCommunityApi(WebApplication app)
    {
        app.MapGet("/api/feed", (HttpContext context, EternalStore store) =>
        {
            var userId = GetUserId(context.User);
            var votes = store.GetVotes(userId);
            var posts = store.GetRecentPosts()
                .Select(post => ToDto(post, votes))
                .ToList();
            return Results.Ok(new FeedDto(posts, DateTimeOffset.UtcNow));
        });

        app.MapGet("/api/me", (HttpContext context, EternalStore store) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                return Results.Ok(new CurrentUserDto(
                    false,
                    null,
                    "Guest",
                    store.IsIpBanned(GetIpAddress(context)),
                    store.IsIpBanned(GetIpAddress(context))
                        ? "This network address is banned."
                        : null));
            }

            var userId = GetUserId(context.User);
            var displayName = context.User.Identity.Name ?? "Member";
            var user = store.UpsertUser(userId, displayName);
            var ipBanned = store.IsIpBanned(GetIpAddress(context));

            return Results.Ok(new CurrentUserDto(
                true,
                user.Id,
                user.DisplayName,
                user.IsBanned || ipBanned,
                user.BanReason ?? (ipBanned ? "This network address is banned." : null)));
        });

        app.MapGet("/api/ai/status", (AiReplyService ai) => Results.Ok(ai.GetStatus()));

        app.MapPost("/api/posts", async (
            CreatePostRequest request,
            HttpContext context,
            EternalStore store,
            ModerationService moderation,
            ThreadService threads,
            CancellationToken cancellationToken) =>
        {
            var guard = ValidateAuthenticatedMutation(context, store);
            if (guard is not null)
            {
                return guard;
            }

            var content = request.Content?.Trim() ?? string.Empty;
            if (content.Length is < 1 or > 4000 ||
                request.Title?.Length > 120 ||
                request.Topic?.Length > 60)
            {
                return Results.BadRequest(new ApiError(
                    "validation_failed",
                    "Content is required and must be 4,000 characters or fewer."));
            }

            var userId = GetUserId(context.User);
            var ipAddress = GetIpAddress(context);
            var moderationText = string.Join(' ', new[] { request.Title, request.Topic, content }.Where(value => !string.IsNullOrWhiteSpace(value)));
            var decision = moderation.EvaluateAndRecord(
                moderationText,
                "post",
                null,
                userId,
                ipAddress);

            if (!decision.Allowed)
            {
                return Results.Json(
                    new ApiError(decision.Outcome, decision.Reason),
                    statusCode: decision.ShouldBan
                        ? StatusCodes.Status403Forbidden
                        : StatusCodes.Status422UnprocessableEntity);
            }

            if (!store.TryConsumePostQuota(ipAddress, DateTime.UtcNow, out var retryAfter))
            {
                context.Response.Headers.RetryAfter =
                    Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Results.Json(
                    new ApiError("rate_limited", "One post per minute is allowed."),
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            var displayName = context.User.Identity?.Name ?? "Member";
            var post = new PostDocument
            {
                Id = Guid.NewGuid(),
                Title = NullIfWhiteSpace(request.Title),
                Topic = NullIfWhiteSpace(request.Topic),
                Content = content,
                Author = new AuthorSnapshot
                {
                    Id = userId,
                    DisplayName = displayName,
                    Initials = GetInitials(displayName)
                },
                CreatedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow
            };

            store.UpsertUser(userId, displayName);
            store.InsertPost(post);
            await threads.GenerateInitialThreadAsync(post, cancellationToken);

            return Results.Created(
                $"/api/posts/{post.Id}",
                ToDto(post, store.GetVotes(userId)));
        }).RequireAuthorization();

        app.MapPost("/api/votes", (
            VoteRequest request,
            HttpContext context,
            EternalStore store) =>
        {
            var guard = ValidateAuthenticatedMutation(context, store);
            if (guard is not null)
            {
                return guard;
            }

            if (request.Value is < -1 or > 1)
            {
                return Results.BadRequest(new ApiError(
                    "invalid_vote",
                    "Vote value must be -1, 0, or 1."));
            }

            var applied = store.ApplyVote(
                GetUserId(context.User),
                request.TargetType,
                request.TargetId,
                request.Value);
            return applied
                ? Results.NoContent()
                : Results.NotFound(new ApiError("target_not_found", "The vote target was not found."));
        }).RequireAuthorization();
    }

    private static IResult? ValidateAuthenticatedMutation(HttpContext context, EternalStore store)
    {
        if (!HasMutationHeader(context))
        {
            return Results.BadRequest(new ApiError(
                "invalid_request",
                "The required same-origin request header is missing."));
        }

        var userId = GetUserId(context.User);
        var user = store.GetUser(userId);
        if (user?.IsBanned == true || store.IsIpBanned(GetIpAddress(context)))
        {
            return Results.Json(
                new ApiError("banned", user?.BanReason ?? "This network address is banned."),
                statusCode: StatusCodes.Status403Forbidden);
        }

        return null;
    }

    private static bool HasMutationHeader(HttpContext context)
    {
        return context.Request.Headers.TryGetValue("X-EternalDiscord-Request", out var value) &&
               value.Count == 1 &&
               value[0] == "1";
    }

    private static string GetUserId(ClaimsPrincipal principal)
    {
        return principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    }

    private static string GetIpAddress(HttpContext context)
    {
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static string GetSafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) ||
            !returnUrl.StartsWith("/", StringComparison.Ordinal) ||
            returnUrl.StartsWith("//", StringComparison.Ordinal))
        {
            return "/";
        }

        return returnUrl;
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string GetInitials(string displayName)
    {
        var initials = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(value => char.ToUpperInvariant(value[0]));
        var result = string.Concat(initials);
        return string.IsNullOrWhiteSpace(result) ? "M" : result;
    }

    private static PostDto ToDto(
        PostDocument post,
        IReadOnlyDictionary<string, int> votes)
    {
        return new PostDto(
            post.Id,
            post.Title,
            post.Topic,
            post.Content,
            ToDto(post.Author),
            AsUtcOffset(post.CreatedAt),
            post.Replies
                .OrderBy(reply => reply.CreatedAt)
                .Select(reply => new ReplyDto(
                    reply.Id,
                    reply.ParentReplyId,
                    reply.Content,
                    ToDto(reply.Author),
                    AsUtcOffset(reply.CreatedAt),
                    reply.Upvotes,
                    reply.Downvotes,
                    GetVote(votes, "reply", reply.Id)))
                .ToList(),
            post.Upvotes,
            post.Downvotes,
            GetVote(votes, "post", post.Id));
    }

    private static AuthorDto ToDto(AuthorSnapshot author)
    {
        return new AuthorDto(
            author.Id,
            author.DisplayName,
            author.Initials,
            author.IsAi,
            author.Persona);
    }

    private static int GetVote(
        IReadOnlyDictionary<string, int> votes,
        string targetType,
        Guid targetId)
    {
        return votes.GetValueOrDefault($"{targetType}:{targetId:N}");
    }

    private static DateTimeOffset AsUtcOffset(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
