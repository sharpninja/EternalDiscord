using EternalDiscord.Domain;
using EternalDiscord.Persistence;

namespace EternalDiscord.Services;

public sealed class ThreadService(
    EternalStore store,
    AiReplyService ai,
    ModerationService moderation,
    ILogger<ThreadService> logger)
{
    public async Task GenerateInitialThreadAsync(
        PostDocument post,
        CancellationToken cancellationToken)
    {
        var replyCount = 5 + Math.Abs(post.Id.GetHashCode() % 3);
        Guid? parentReplyId = null;
        var discussion = post.Content;

        for (var index = 0; index < replyCount; index++)
        {
            var persona = SelectPersona(post.Id, index);
            var generated = await ai.GenerateAsync(discussion, persona, cancellationToken);
            var replyId = Guid.NewGuid();
            var decision = moderation.EvaluateAndRecord(
                generated.Content,
                "ai_reply",
                replyId,
                $"ai:{generated.Provider}",
                "server");

            if (!decision.Allowed)
            {
                logger.LogWarning(
                    "AI reply blocked by moderation. Provider={Provider} Outcome={Outcome}",
                    generated.Provider,
                    decision.Outcome);
                continue;
            }

            var reply = new ReplyDocument
            {
                Id = replyId,
                ParentReplyId = parentReplyId,
                Content = generated.Content,
                Author = CreateAiAuthor(persona, generated.Provider),
                CreatedAt = DateTime.UtcNow.AddMilliseconds(index)
            };

            post.Replies.Add(reply);
            parentReplyId = reply.Id;
            discussion = $"Original post: {post.Content}\nLatest reply: {reply.Content}";
        }

        post.LastActivityAt = DateTime.UtcNow;
        store.UpdatePost(post);
    }

    public async Task<bool> AddAutoReplyAsync(Guid postId, CancellationToken cancellationToken)
    {
        var post = store.GetPost(postId);
        if (post is null)
        {
            return false;
        }

        var lastReply = post.Replies.OrderBy(x => x.CreatedAt).LastOrDefault();
        var discussion = lastReply is null
            ? post.Content
            : $"Original post: {post.Content}\nLatest reply: {lastReply.Content}";
        var persona = SelectPersona(post.Id, post.Replies.Count);
        var generated = await ai.GenerateAsync(discussion, persona, cancellationToken);
        var replyId = Guid.NewGuid();
        var decision = moderation.EvaluateAndRecord(
            generated.Content,
            "auto_reply",
            replyId,
            $"ai:{generated.Provider}",
            "server");

        if (!decision.Allowed)
        {
            logger.LogWarning(
                "Background AI reply blocked. PostId={PostId} Outcome={Outcome}",
                postId,
                decision.Outcome);
            return false;
        }

        post.Replies.Add(new ReplyDocument
        {
            Id = replyId,
            ParentReplyId = lastReply?.Id,
            Content = generated.Content,
            Author = CreateAiAuthor(persona, generated.Provider),
            CreatedAt = DateTime.UtcNow
        });
        post.LastAutoReplyAt = DateTime.UtcNow;
        post.LastActivityAt = DateTime.UtcNow;
        return store.UpdatePost(post);
    }

    private string SelectPersona(Guid postId, int offset)
    {
        var seed = Math.Abs(postId.GetHashCode());
        return ai.HistoricalFigures[(seed + offset) % ai.HistoricalFigures.Count];
    }

    private static AuthorSnapshot CreateAiAuthor(string persona, string provider)
    {
        return new AuthorSnapshot
        {
            Id = $"ai:{provider}:{persona.Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant()}",
            DisplayName = persona,
            Initials = string.Concat(persona.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Take(2)
                .Select(part => char.ToUpperInvariant(part[0]))),
            IsAi = true,
            Persona = $"AI simulation via {provider}"
        };
    }
}

public sealed class AutoReplyBackgroundService(
    EternalStore store,
    AiReplyService ai,
    ThreadService threads,
    ILogger<AutoReplyBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(ai.GetAutoReplyIntervalSeconds());
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var eligible = store.GetActivePosts(DateTime.UtcNow.AddHours(-24), 10)
                    .FirstOrDefault(post =>
                        post.LastAutoReplyAt is null ||
                        DateTime.UtcNow - post.LastAutoReplyAt >= interval);

                if (eligible is not null)
                {
                    await threads.AddAutoReplyAsync(eligible.Id, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Background auto-reply scan failed.");
            }
        }
    }
}
