using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EternalDiscord.Domain;
using EternalDiscord.Persistence;

namespace EternalDiscord.Services;

public sealed partial class ModerationService(EternalStore store, ILogger<ModerationService> logger)
{
    public ModerationResult Evaluate(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new ModerationResult(false, false, "empty", "Content cannot be empty.");
        }

        if (PromptInjectionPattern().IsMatch(content))
        {
            return new ModerationResult(
                false,
                true,
                "prompt_injection",
                "Prompt-injection content is not permitted.");
        }

        if (AdultPattern().IsMatch(content))
        {
            return new ModerationResult(
                false,
                false,
                "adult_content",
                "Adult or sexually explicit content is not permitted.");
        }

        if (HateOrViolencePattern().IsMatch(content))
        {
            return new ModerationResult(
                false,
                false,
                "hateful_or_violent",
                "Hateful or violent content is not permitted.");
        }

        return ModerationResult.Allow();
    }

    public ModerationResult EvaluateAndRecord(
        string content,
        string subjectType,
        Guid? subjectId,
        string userId,
        string ipAddress)
    {
        var result = Evaluate(content);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

        store.LogModeration(new ModerationLogDocument
        {
            SubjectType = subjectType,
            SubjectId = subjectId,
            UserId = userId,
            IpAddress = ipAddress,
            Outcome = result.Outcome,
            Reason = result.Reason,
            ContentHash = hash,
            CreatedAt = DateTime.UtcNow
        });

        if (result.ShouldBan)
        {
            store.Ban(userId, ipAddress, result.Reason);
            logger.LogWarning(
                "Moderation auto-ban applied. UserId={UserId} IpAddress={IpAddress} Outcome={Outcome}",
                userId,
                ipAddress,
                result.Outcome);
        }

        return result;
    }

    [GeneratedRegex(
        @"\b(ignore|disregard|forget)\b.{0,40}\b(previous|prior|system|developer)\b.{0,30}\b(instruction|prompt|message)s?\b|\bjailbreak\b|\bact\s+as\s+dan\b|\breveal\b.{0,20}\bsystem\s+prompt\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex PromptInjectionPattern();

    [GeneratedRegex(
        @"\b(nsfw|porn(?:ography|ographic)?|sexually\s+explicit|explicit\s+sexual|adult\s+content)\b",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex AdultPattern();

    [GeneratedRegex(
        @"\b(hate\s+speech|ethnic\s+cleansing|massacre|behead(?:ing)?|graphic\s+violence|murder\s+all)\b",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex HateOrViolencePattern();
}
