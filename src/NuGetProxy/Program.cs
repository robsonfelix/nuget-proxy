using Microsoft.AspNetCore.HttpOverrides;
using NuGetProxy.Configuration;
using NuGetProxy.Endpoints;
using NuGetProxy.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<ProxyOptions>(builder.Configuration.GetSection(ProxyOptions.SectionName));

// HttpClients
builder.Services.AddHttpClient("gitlab-discovery", (sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ProxyOptions>>().Value;
    if (!string.IsNullOrEmpty(opts.GitLab.ServiceToken))
        client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", opts.GitLab.ServiceToken);
});

builder.Services.AddTransient<GitLabAuthHandler>();
builder.Services.AddHttpClient("gitlab", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
}).AddHttpMessageHandler<GitLabAuthHandler>();

builder.Services.AddHttpClient("upstream", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});

// Services
builder.Services.AddSingleton<GitLabPackageIndex>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GitLabPackageIndex>());
builder.Services.AddSingleton<UpstreamFeedRegistry>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<UpstreamFeedRegistry>());
builder.Services.AddSingleton<UrlRewriter>();

// Forwarded headers (for reverse proxy)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

app.UseForwardedHeaders();

// Map endpoints
ServiceIndexEndpoint.Map(app);
PackageBaseAddressEndpoint.Map(app);
RegistrationEndpoint.Map(app);
SearchEndpoint.Map(app);

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

// Enable WebApplicationFactory<Program> in tests
public partial class Program { }
