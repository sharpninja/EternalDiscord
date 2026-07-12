using EternalDiscord.Domain;
using LiteDB;

namespace EternalDiscord.Persistence;

public sealed class EternalStore : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly object _gate = new();

    public EternalStore(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configuredPath = configuration["Storage:Path"] ?? "data/eternaldiscord.db";
        var databasePath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);

        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _database = new LiteDatabase($"Filename={databasePath};Connection=shared;UtcDate=true");

        _database.GetCollection<PostDocument>("posts").EnsureIndex(x => x.CreatedAt);
        _database.GetCollection<UserDocument>("users").EnsureIndex(x => x.Id, unique: true);
        _database.GetCollection<VoteDocument>("votes").EnsureIndex(x => x.Id, unique: true);
        _database.GetCollection<RateLimitDocument>("rate_limits").EnsureIndex(x => x.Id, unique: true);
        _database.GetCollection<IpBanDocument>("ip_bans").EnsureIndex(x => x.Id, unique: true);
        _database.GetCollection<ModerationLogDocument>("moderation_logs").EnsureIndex(x => x.CreatedAt);
    }

    public IReadOnlyList<PostDocument> GetRecentPosts(int limit = 50)
    {
        lock (_gate)
        {
            return _database.GetCollection<PostDocument>("posts")
                .Query()
                .OrderByDescending(x => x.CreatedAt)
                .Limit(limit)
                .ToList();
        }
    }

    public IReadOnlyList<PostDocument> GetActivePosts(DateTime activeSince, int limit = 10)
    {
        lock (_gate)
        {
            return _database.GetCollection<PostDocument>("posts")
                .Query()
                .Where(x => x.LastActivityAt >= activeSince)
                .OrderByDescending(x => x.LastActivityAt)
                .Limit(limit)
                .ToList();
        }
    }

    public PostDocument? GetPost(Guid id)
    {
        lock (_gate)
        {
            return _database.GetCollection<PostDocument>("posts").FindById(id);
        }
    }

    public void InsertPost(PostDocument post)
    {
        lock (_gate)
        {
            _database.GetCollection<PostDocument>("posts").Insert(post);
        }
    }

    public bool UpdatePost(PostDocument post)
    {
        lock (_gate)
        {
            return _database.GetCollection<PostDocument>("posts").Update(post);
        }
    }

    public UserDocument UpsertUser(string id, string displayName)
    {
        lock (_gate)
        {
            var users = _database.GetCollection<UserDocument>("users");
            var user = users.FindById(id) ?? new UserDocument { Id = id };
            user.DisplayName = displayName;
            user.LastSeenAt = DateTime.UtcNow;
            users.Upsert(user);
            return user;
        }
    }

    public UserDocument? GetUser(string id)
    {
        lock (_gate)
        {
            return _database.GetCollection<UserDocument>("users").FindById(id);
        }
    }

    public bool IsIpBanned(string ipAddress)
    {
        lock (_gate)
        {
            return _database.GetCollection<IpBanDocument>("ip_bans").Exists(x => x.Id == ipAddress);
        }
    }

    public void Ban(string userId, string ipAddress, string reason)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(userId))
            {
                var users = _database.GetCollection<UserDocument>("users");
                var user = users.FindById(userId) ?? new UserDocument
                {
                    Id = userId,
                    DisplayName = userId
                };
                user.IsBanned = true;
                user.BanReason = reason;
                user.BannedAt = DateTime.UtcNow;
                users.Upsert(user);
            }

            if (!string.IsNullOrWhiteSpace(ipAddress))
            {
                _database.GetCollection<IpBanDocument>("ip_bans").Upsert(new IpBanDocument
                {
                    Id = ipAddress,
                    Reason = reason,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }
    }

    public bool TryConsumePostQuota(string ipAddress, DateTime now, out TimeSpan retryAfter)
    {
        lock (_gate)
        {
            var limits = _database.GetCollection<RateLimitDocument>("rate_limits");
            var counter = limits.FindAll().FirstOrDefault(item => item.Id == ipAddress);
            if (counter is not null)
            {
                var nowMilliseconds = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc))
                    .ToUnixTimeMilliseconds();
                var elapsed = TimeSpan.FromMilliseconds(
                    Math.Max(0, nowMilliseconds - counter.LastPostUnixTimeMilliseconds));
                if (elapsed < TimeSpan.FromMinutes(1))
                {
                    retryAfter = TimeSpan.FromMinutes(1) - elapsed;
                    return false;
                }
            }

            if (counter is null)
            {
                limits.Insert(new RateLimitDocument { Id = ipAddress, LastPostUnixTimeMilliseconds = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)).ToUnixTimeMilliseconds() });
            }
            else
            {
                counter.LastPostUnixTimeMilliseconds = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
                limits.Update(counter);
            }
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    public void LogModeration(ModerationLogDocument entry)
    {
        lock (_gate)
        {
            _database.GetCollection<ModerationLogDocument>("moderation_logs").Insert(entry);
        }
    }

    public IReadOnlyDictionary<string, int> GetVotes(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new Dictionary<string, int>();
        }

        lock (_gate)
        {
            return _database.GetCollection<VoteDocument>("votes")
                .Find(x => x.UserId == userId)
                .ToDictionary(x => $"{x.TargetType}:{x.TargetId:N}", x => x.Value);
        }
    }

    public bool ApplyVote(string userId, string targetType, Guid targetId, int value)
    {
        targetType = targetType.Trim().ToLowerInvariant();
        if (targetType is not ("post" or "reply") || value is < -1 or > 1)
        {
            return false;
        }

        lock (_gate)
        {
            var posts = _database.GetCollection<PostDocument>("posts");
            PostDocument? post;
            ReplyDocument? reply = null;

            if (targetType == "post")
            {
                post = posts.FindById(targetId);
            }
            else
            {
                post = posts.FindAll().FirstOrDefault(x => x.Replies.Any(replyItem => replyItem.Id == targetId));
                reply = post?.Replies.FirstOrDefault(x => x.Id == targetId);
            }

            if (post is null || (targetType == "reply" && reply is null))
            {
                return false;
            }

            var votes = _database.GetCollection<VoteDocument>("votes");
            var key = $"{userId}:{targetType}:{targetId:N}";
            var existing = votes.FindById(key);
            var previous = existing?.Value ?? 0;

            if (previous == value)
            {
                return true;
            }

            if (targetType == "post")
            {
                AdjustCounts(post, previous, value);
            }
            else
            {
                AdjustCounts(reply!, previous, value);
            }

            if (value == 0)
            {
                votes.Delete(key);
            }
            else
            {
                votes.Upsert(new VoteDocument
                {
                    Id = key,
                    UserId = userId,
                    TargetType = targetType,
                    TargetId = targetId,
                    Value = value,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            post.LastActivityAt = DateTime.UtcNow;
            posts.Update(post);
            return true;
        }
    }

    private static void AdjustCounts(PostDocument post, int previous, int current)
    {
        if (previous == 1) post.Upvotes--;
        if (previous == -1) post.Downvotes--;
        if (current == 1) post.Upvotes++;
        if (current == -1) post.Downvotes++;
    }

    private static void AdjustCounts(ReplyDocument reply, int previous, int current)
    {
        if (previous == 1) reply.Upvotes--;
        if (previous == -1) reply.Downvotes--;
        if (current == 1) reply.Upvotes++;
        if (current == -1) reply.Downvotes++;
    }

    public void Dispose() => _database.Dispose();
}
