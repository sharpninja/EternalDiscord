using LiteDB;

namespace EternalDiscord.Domain;

public sealed class PostDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Title { get; set; }
    public string? Topic { get; set; }
    public string Content { get; set; } = string.Empty;
    public AuthorSnapshot Author { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAutoReplyAt { get; set; }
    public List<ReplyDocument> Replies { get; set; } = [];
    public int Upvotes { get; set; }
    public int Downvotes { get; set; }
}

public sealed class ReplyDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ParentReplyId { get; set; }
    public string Content { get; set; } = string.Empty;
    public AuthorSnapshot Author { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int Upvotes { get; set; }
    public int Downvotes { get; set; }
}

public sealed class AuthorSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public bool IsAi { get; set; }
    public string? Persona { get; set; }
}

public sealed class UserDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public bool IsBanned { get; set; }
    public string? BanReason { get; set; }
    public DateTime? BannedAt { get; set; }
}

public sealed class VoteDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public int Value { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ModerationLogDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SubjectType { get; set; } = string.Empty;
    public Guid? SubjectId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class RateLimitDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;
    public long LastPostUnixTimeMilliseconds { get; set; }
}

public sealed class IpBanDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed record ModerationResult(bool Allowed, bool ShouldBan, string Outcome, string Reason)
{
    public static ModerationResult Allow() => new(true, false, "allowed", "Content passed moderation.");
}
