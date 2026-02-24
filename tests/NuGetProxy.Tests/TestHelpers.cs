using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace NuGetProxy.Tests;

public class ProxyFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Override with env vars (GITLAB_URL, GITLAB_SERVICE_TOKEN are in the environment)
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NuGetProxy:BaseUrl"] = "http://localhost",
                ["NuGetProxy:GitLab:Url"] = Environment.GetEnvironmentVariable("GITLAB_URL") ?? "",
                ["NuGetProxy:GitLab:ServiceToken"] = Environment.GetEnvironmentVariable("GITLAB_SERVICE_TOKEN") ?? "",
                ["NuGetProxy:GitLab:RefreshIntervalSeconds"] = "99999",
                ["NuGetProxy:UpstreamFeeds:0:Name"] = "nuget.org",
                ["NuGetProxy:UpstreamFeeds:0:ServiceIndexUrl"] = "https://api.nuget.org/v3/index.json",
                ["NuGetProxy:UpstreamFeeds:0:ForwardAuth"] = "false",
            });
        });
    }

    public bool GitLabConfigured =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITLAB_SERVICE_TOKEN"));
}
