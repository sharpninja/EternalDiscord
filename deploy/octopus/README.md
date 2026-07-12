# Octopus Deploy

Eternal Discord is deployed as the image produced by the root `Dockerfile`. The container listens on port `8080`, runs as the .NET image's non-root `app` user, and persists LiteDB at `/app/data/eternaldiscord.db`.

## Recommended process

1. Build and publish the image to the target registry using the Git commit SHA as the immutable tag.
2. Create an Octopus project with a Docker container deployment step.
3. Mount a persistent volume at `/app/data`.
4. Map the desired host port to container port `8080`.
5. configure the health check as `GET /health`.
6. Run ngrok as a second container or lifecycle step on the same Docker network with `http app:8080`.

## Variables

Mark secrets as sensitive Octopus variables.

| Variable | Required | Purpose |
| --- | --- | --- |
| `NGROK_AUTHTOKEN` | For ngrok | ngrok sidecar authentication |
| `Ai__DefaultProvider` | No | Defaults to `claude` |
| `Ai__Providers__claude__ApiKey` | No | Anthropic access |
| `Ai__Providers__openai__ApiKey` | No | OpenAI access |
| `Ai__Providers__xai__ApiKey` | No | xAI access |
| `Ai__Providers__huggingface__ApiKey` | No | Hugging Face access |
| `Authentication__Providers__google__ClientId` | No | Google OAuth |
| `Authentication__Providers__google__ClientSecret` | No | Google OAuth secret |
| `Authentication__Providers__microsoft__ClientId` | No | Microsoft OAuth |
| `Authentication__Providers__microsoft__ClientSecret` | No | Microsoft OAuth secret |
| `Authentication__Providers__github__ClientId` | No | GitHub OAuth |
| `Authentication__Providers__github__ClientSecret` | No | GitHub OAuth secret |
| `Proxy__TrustForwardedHeaders` | Yes behind ngrok | Set to `true` so IP throttling sees the forwarded client address |

Register each configured OAuth callback as `https://<public-host>/auth/callback/<provider>`.

## Smoke checks

After deployment, Octopus should fail the release if either check fails:

```powershell
Invoke-WebRequest "${PublicBaseUrl}/health" -UseBasicParsing
Invoke-RestMethod "${PublicBaseUrl}/api/feed"
```
