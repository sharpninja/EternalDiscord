using System.ComponentModel.DataAnnotations;

namespace EternalDiscord.Contracts;

public sealed record AuthorDto(
    string Id,
    string DisplayName,
    string Initials,
    bool IsAi = false,
    string? Persona = null);

public sealed record ReplyDto(
    Guid Id,
    Guid? ParentReplyId,
    string Content,
    AuthorDto Author,
    DateTimeOffset CreatedAt,
    int Upvotes,
    int Downvotes,
    int UserVote);

public sealed record PostDto(
    Guid Id,
    string? Title,
    string? Topic,
    string Content,
    AuthorDto Author,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ReplyDto> Replies,
    int Upvotes,
    int Downvotes,
    int UserVote);

public sealed record FeedDto(IReadOnlyList<PostDto> Posts, DateTimeOffset RefreshedAt);

public sealed class CreatePostRequest
{
    [StringLength(120)]
    public string? Title { get; set; }

    [StringLength(60)]
    public string? Topic { get; set; }

    [Required]
    [StringLength(4000, MinimumLength = 1)]
    public string Content { get; set; } = string.Empty;
}

public sealed record VoteRequest(string TargetType, Guid TargetId, int Value);

public sealed record CurrentUserDto(
    bool IsAuthenticated,
    string? Id,
    string DisplayName,
    bool IsBanned,
    string? BanReason);

public sealed record AuthProviderDto(string Id, string DisplayName, bool IsConfigured);

public sealed record AiProviderStatusDto(
    string Id,
    string DisplayName,
    bool IsConfigured,
    bool IsActive,
    string Mode);

public sealed record AiStatusDto(
    string DefaultProvider,
    int AutoReplyIntervalSeconds,
    IReadOnlyList<AiProviderStatusDto> Providers);

public sealed record ApiError(string Code, string Message);
