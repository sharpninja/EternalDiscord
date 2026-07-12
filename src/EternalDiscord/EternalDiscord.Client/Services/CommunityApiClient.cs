using System.Net.Http.Json;
using EternalDiscord.Contracts;

namespace EternalDiscord.Client.Services;

public sealed class CommunityApiClient(HttpClient httpClient)
{
    public async Task<FeedDto> GetFeedAsync(CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<FeedDto>("api/feed", cancellationToken)
            ?? new FeedDto([], DateTimeOffset.UtcNow);
    }

    public async Task<CurrentUserDto> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<CurrentUserDto>("api/me", cancellationToken)
            ?? new CurrentUserDto(false, null, "Guest", false, null);
    }

    public async Task<AiStatusDto?> GetAiStatusAsync(CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<AiStatusDto>("api/ai/status", cancellationToken);
    }

    public async Task<IReadOnlyList<AuthProviderDto>> GetAuthProvidersAsync(
        CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<List<AuthProviderDto>>(
            "api/auth/providers",
            cancellationToken) ?? [];
    }

    public async Task<ApiCallResult<PostDto>> CreatePostAsync(
        CreatePostRequest request,
        CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/posts")
        {
            Content = JsonContent.Create(request)
        };
        AddMutationHeader(message);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        return await ReadResultAsync<PostDto>(response, cancellationToken);
    }

    public async Task<ApiCallResult<bool>> VoteAsync(
        VoteRequest request,
        CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/votes")
        {
            Content = JsonContent.Create(request)
        };
        AddMutationHeader(message);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return ApiCallResult<bool>.Success(true);
        }

        return ApiCallResult<bool>.Failure(await ReadErrorAsync(response, cancellationToken));
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "auth/logout");
        AddMutationHeader(message);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static void AddMutationHeader(HttpRequestMessage message)
    {
        message.Headers.Add("X-EternalDiscord-Request", "1");
    }

    private static async Task<ApiCallResult<T>> ReadResultAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            return value is null
                ? ApiCallResult<T>.Failure(new ApiError("empty_response", "The server returned an empty response."))
                : ApiCallResult<T>.Success(value);
        }

        return ApiCallResult<T>.Failure(await ReadErrorAsync(response, cancellationToken));
    }

    private static async Task<ApiError> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ApiError>(cancellationToken)
                ?? new ApiError("request_failed", $"Request failed with status {(int)response.StatusCode}.");
        }
        catch
        {
            return new ApiError("request_failed", $"Request failed with status {(int)response.StatusCode}.");
        }
    }
}

public sealed record ApiCallResult<T>(bool IsSuccess, T? Value, ApiError? Error)
{
    public static ApiCallResult<T> Success(T value) => new(true, value, null);
    public static ApiCallResult<T> Failure(ApiError error) => new(false, default, error);
}
