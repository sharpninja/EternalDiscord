# Eternal Discord

Eternal Discord is a clean-room Blazor WebAssembly application based on the published [EternalX requirements](https://github.com/sharpninja/EternalX.Blazor/blob/main/REQUIREMENTS.md). It presents public discussions in a Discord-style workspace and builds moderated AI reply chains from rotating historical perspectives.

## What is implemented

- Anonymous chronological feed with authenticated posting
- Discord-style responsive layout with server, channel, feed, composer, and member panels
- Configurable Google, Microsoft, and GitHub OAuth login
- Development-only local login when `ASPNETCORE_ENVIRONMENT=Development`
- LiteDB persistence for posts, embedded replies, users, votes, moderation decisions, IP bans, and rate-limit counters
- One post per minute per client IP
- Prompt-injection blocking with automatic user/IP ban
- Adult, hateful, and violent content blocking without automatic ban
- Five to seven nested AI replies for each new post
- Ten-second background reply scan for active threads
- Anthropic, OpenAI-compatible, xAI, and Hugging Face provider adapters
- Deterministic local AI fallback when no provider keys are configured
- Post/reply voting, search, share-link copying, provider status, and profile/ban state
- Structured JSON logging and `/health`
- Non-root Docker image and ngrok Compose sidecar

## Run locally

Prerequisites: .NET SDK 10.0.301 or newer.

```powershell
dotnet restore EternalDiscord.slnx
dotnet run --project src/EternalDiscord/EternalDiscord/EternalDiscord.csproj --urls http://localhost:5080
```

Open `http://localhost:5080`. In Development, use the Local development login entry.

## Configuration

ASP.NET Core environment variables use double underscores. For example:

```powershell
$env:Ai__Providers__claude__ApiKey = "<key>"
$env:Authentication__Providers__github__ClientId = "<client-id>"
$env:Authentication__Providers__github__ClientSecret = "<client-secret>"
```

OAuth callback paths are:

- `/auth/callback/google`
- `/auth/callback/microsoft`
- `/auth/callback/github`

Provider endpoints and models are configurable under `Ai:Providers:{provider}`. Keys are read only by the server and are never sent to the WebAssembly client.

## Docker and ngrok

Create a local `.env` with `NGROK_AUTHTOKEN` and any optional provider secrets, then run:

```powershell
docker compose up --build
```

The app is available at `http://localhost:8080`; the ngrok inspector is at `http://localhost:4040`. LiteDB data is stored in the `eternaldiscord-data` volume.

## Validate

```powershell
dotnet build EternalDiscord.slnx --no-restore
dotnet test EternalDiscord.slnx --no-restore
```

See [deploy/octopus/README.md](deploy/octopus/README.md) for the Octopus container deployment contract.
