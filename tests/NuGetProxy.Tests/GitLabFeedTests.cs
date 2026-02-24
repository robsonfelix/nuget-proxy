using System.Text.Json;
using Xunit;

namespace NuGetProxy.Tests;

/// <summary>
/// Tests that verify GitLab package routing works correctly.
/// These tests require GITLAB_URL and GITLAB_SERVICE_TOKEN environment variables.
/// They are skipped when GitLab is not configured.
/// </summary>
public class GitLabFeedTests : IClassFixture<ProxyFactory>
{
    private readonly HttpClient _client;
    private readonly ProxyFactory _factory;

    public GitLabFeedTests(ProxyFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private void SkipIfGitLabNotConfigured()
    {
        if (!_factory.GitLabConfigured)
            Assert.Skip("GitLab not configured (set GITLAB_URL and GITLAB_SERVICE_TOKEN)");
    }

    [Fact]
    public async Task GitLab_VersionList_ReturnsVersions()
    {
        SkipIfGitLabNotConfigured();

        // Wait for the index to populate (background service)
        await Task.Delay(5000);

        var response = await _client.GetAsync("/v3/package/DevExpress.AIIntegration/index.json");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var versions = doc.RootElement.GetProperty("versions").EnumerateArray().ToList();

        Assert.NotEmpty(versions);
    }

    [Fact]
    public async Task GitLab_Registration_ReturnsMetadataWithRewrittenUrls()
    {
        SkipIfGitLabNotConfigured();

        await Task.Delay(5000);

        var response = await _client.GetAsync("/v3/registration/DevExpress.AIIntegration/index.json");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();

        // URLs should be rewritten to point through the proxy
        Assert.Contains("http://localhost/v3/registration/", json);
        Assert.Contains("http://localhost/v3/package/", json);
        // Should NOT contain raw GitLab URLs
        Assert.DoesNotContain("srv-git01.fortress.internal", json);
    }

    [Fact]
    public async Task GitLab_PackageRoutedToGitLab_NotUpstream()
    {
        SkipIfGitLabNotConfigured();

        await Task.Delay(5000);

        // DevExpress.AIIntegration exists on GitLab, not nuget.org
        // If routing works, we get metadata; if it falls through to nuget.org, we get 404
        var response = await _client.GetAsync("/v3/registration/DevExpress.AIIntegration/index.json");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("DevExpress.AIIntegration", json);
    }

    [Fact]
    public async Task PublicPackage_NotRoutedToGitLab()
    {
        // Serilog should come from nuget.org, not GitLab
        var response = await _client.GetAsync("/v3/package/serilog/index.json");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var versions = doc.RootElement.GetProperty("versions").EnumerateArray().ToList();

        // Serilog has many versions on nuget.org
        Assert.True(versions.Count > 50);
    }
}
