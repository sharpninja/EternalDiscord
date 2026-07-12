using EternalDiscord.Api;
using EternalDiscord.Authentication;
using EternalDiscord.Components;
using EternalDiscord.Persistence;
using EternalDiscord.Services;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

builder.Services
    .AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddEternalAuthentication(builder.Configuration);
builder.Services.AddHealthChecks();
builder.Services.AddHttpClient("ai", client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
});

builder.Services.AddSingleton<EternalStore>();
builder.Services.AddSingleton<ModerationService>();
builder.Services.AddSingleton<AiReplyService>();
builder.Services.AddSingleton<ThreadService>();
builder.Services.AddHostedService<AutoReplyBackgroundService>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;

    if (string.Equals(
        builder.Configuration["Proxy:TrustForwardedHeaders"],
        "true",
        StringComparison.OrdinalIgnoreCase))
    {
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    }
});

var app = builder.Build();

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapHealthChecks("/health");
app.MapEternalDiscordEndpoints();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(EternalDiscord.Client._Imports).Assembly);

app.Run();

public partial class Program;
