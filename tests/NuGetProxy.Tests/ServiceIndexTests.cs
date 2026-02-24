using System.Text.Json;
using Xunit;

namespace NuGetProxy.Tests;

public class ServiceIndexTests : IClassFixture<ProxyFactory>
{
    private readonly HttpClient _client;

    public ServiceIndexTests(ProxyFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ServiceIndex_ReturnsValidNuGetV3Index()
    {
        var response = await _client.GetAsync("/v3/index.json");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        Assert.Equal("3.0.0", doc.RootElement.GetProperty("version").GetString());

        var resources = doc.RootElement.GetProperty("resources").EnumerateArray().ToList();
        Assert.True(resources.Count >= 4);

        var types = resources.Select(r => r.GetProperty("@type").GetString()).ToList();
        Assert.Contains("PackageBaseAddress/3.0.0", types);
        Assert.Contains("RegistrationsBaseUrl/3.6.0", types);
        Assert.Contains("SearchQueryService/3.5.0", types);
        Assert.Contains("SearchAutocompleteService/3.5.0", types);
    }

    [Fact]
    public async Task ServiceIndex_ContainsCorrectBaseUrls()
    {
        var response = await _client.GetAsync("/v3/index.json");
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var resources = doc.RootElement.GetProperty("resources").EnumerateArray().ToList();
        foreach (var resource in resources)
        {
            var id = resource.GetProperty("@id").GetString()!;
            Assert.StartsWith("http://localhost", id);
        }
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", json);
    }
}
