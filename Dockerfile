FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/EternalDiscord/EternalDiscord/EternalDiscord.csproj", "src/EternalDiscord/EternalDiscord/"]
COPY ["src/EternalDiscord/EternalDiscord.Client/EternalDiscord.Client.csproj", "src/EternalDiscord/EternalDiscord.Client/"]
COPY ["src/EternalDiscord.Contracts/EternalDiscord.Contracts.csproj", "src/EternalDiscord.Contracts/"]
RUN dotnet restore "src/EternalDiscord/EternalDiscord/EternalDiscord.csproj"

COPY . .
RUN dotnet publish "src/EternalDiscord/EternalDiscord/EternalDiscord.csproj"     --configuration Release     --output /app/publish     --no-restore     /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update     && apt-get install --yes --no-install-recommends curl     && rm -rf /var/lib/apt/lists/*     && mkdir -p /app/data     && chown -R "$APP_UID:$APP_UID" /app

COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080
ENV Storage__Path=/app/data/eternaldiscord.db
EXPOSE 8080
VOLUME ["/app/data"]

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3     CMD curl --fail --silent http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "EternalDiscord.dll"]
