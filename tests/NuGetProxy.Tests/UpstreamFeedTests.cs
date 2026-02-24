using System.Text.Json;
using Xunit;

namespace NuGetProxy.Tests;

/// <summary>
/// Tests that verify upstream feed (nuget.org) proxying works correctly.
/// These tests hit the real nuget.org API.
/// </summary>
public class UpstreamFeedTests : IClassFixture<ProxyFactory>
{
    private readonly HttpClient _client;

    public UpstreamFeedTests(ProxyFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task VersionList_ReturnsVersionsFromNuGetOrg()
    {
        var response = await _client.GetAsync("/v3/package/newtonsoft.json/index.json");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var versions = doc.RootElement.GetProperty("versions").EnumerateArray().ToList();

        Assert.True(versions.Count > 50);
        Assert.Contains(versions, v => v.GetString() == "13.0.3");
    }

    [Fact]
    public async Task Registration_ReturnsMetadataWithRewrittenUrls()
    {
        var response = await _client.GetAsync("/v3/registration/newtonsoft.json/index.json");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();

        // URLs should be rewritten to point through the proxy
        Assert.Contains("http://localhost/v3/registration/", json);
        // Should NOT contain upstream URLs
        Assert.DoesNotContain("api.nuget.org/v3/registration", json);
    }

    [Fact]
    public async Task Search_ReturnsResultsWithRewrittenUrls()
    {
        var response = await _client.GetAsync("/v3/search?q=newtonsoft&take=1");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("totalHits").GetInt32() > 0);
        Assert.Contains("http://localhost/v3/registration/", json);
    }

    [Fact]
    public async Task VersionList_Returns404ForNonexistentPackage()
    {
        var response = await _client.GetAsync("/v3/package/this-package-definitely-does-not-exist-xyz-123/index.json");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Download_StreamsNupkgBinary()
    {
        // Download a small, well-known package
        var response = await _client.GetAsync(
            "/v3/package/newtonsoft.json/13.0.3/newtonsoft.json.13.0.3.nupkg");
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync();
        // .nupkg is a zip file, starts with PK magic bytes
        Assert.True(bytes.Length > 1000);
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
    }
}
