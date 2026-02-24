namespace NuGetProxy.Services;

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NuGetProxy.Configuration;

public class GitLabPackageIndex : BackgroundService
{
    private volatile ConcurrentDictionary<string, HashSet<int>> _packages = new(StringComparer.OrdinalIgnoreCase);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ProxyOptions _options;
    private readonly ILogger<GitLabPackageIndex> _logger;

    public GitLabPackageIndex(
        IHttpClientFactory httpClientFactory,
        IOptions<ProxyOptions> options,
        ILogger<GitLabPackageIndex> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public bool TryGetGroup(string packageId, out int groupId)
    {
        if (_packages.TryGetValue(packageId, out var groups) && groups.Count > 0)
        {
            groupId = groups.First();
            return true;
        }
        groupId = 0;
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RefreshAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Initial GitLab package index refresh failed");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.GitLab.RefreshIntervalSeconds), stoppingToken);
                await RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh GitLab package index, keeping stale data");
            }
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.GitLab.ServiceToken))
        {
            _logger.LogInformation("GitLab service token not configured, skipping package index refresh");
            return;
        }

        var client = _httpClientFactory.CreateClient("gitlab-discovery");
        var gitlabBase = _options.GitLab.Url.TrimEnd('/');

        // If no group IDs configured, auto-discover all groups
        var groupIds = _options.GitLab.GroupIds;
        if (groupIds.Length == 0)
        {
            groupIds = await DiscoverGroupsAsync(client, gitlabBase, cancellationToken);
            _logger.LogInformation("Auto-discovered {Count} GitLab groups", groupIds.Length);
        }

        var newPackages = new ConcurrentDictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var groupId in groupIds)
        {
            try
            {
                var page = 1;
                while (true)
                {
                    var url = $"{gitlabBase}/api/v4/groups/{groupId}/packages?package_type=nuget&per_page=100&page={page}";
                    var response = await client.GetAsync(url, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    var packages = JsonSerializer.Deserialize<JsonElement>(json);

                    if (packages.ValueKind != JsonValueKind.Array || packages.GetArrayLength() == 0)
                        break;

                    foreach (var pkg in packages.EnumerateArray())
                    {
                        var name = pkg.GetProperty("name").GetString();
                        if (name != null)
                        {
                            newPackages.AddOrUpdate(
                                name,
                                _ => new HashSet<int> { groupId },
                                (_, existing) => { existing.Add(groupId); return existing; });
                        }
                    }

                    // Check for next page
                    if (response.Headers.TryGetValues("x-next-page", out var nextPageValues))
                    {
                        var nextPage = nextPageValues.FirstOrDefault();
                        if (string.IsNullOrEmpty(nextPage))
                            break;
                        page = int.Parse(nextPage);
                    }
                    else
                    {
                        if (packages.GetArrayLength() < 100)
                            break;
                        page++;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to fetch packages for GitLab group {GroupId}", groupId);
            }
        }

        _packages = newPackages;
        _logger.LogInformation("GitLab package index refreshed: {Count} packages across {Groups} groups",
            newPackages.Count, groupIds.Length);
    }

    private async Task<int[]> DiscoverGroupsAsync(HttpClient client, string gitlabBase, CancellationToken cancellationToken)
    {
        var groupIds = new List<int>();
        var page = 1;

        while (true)
        {
            var url = $"{gitlabBase}/api/v4/groups?per_page=100&page={page}";
            var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var groups = JsonSerializer.Deserialize<JsonElement>(json);

            if (groups.ValueKind != JsonValueKind.Array || groups.GetArrayLength() == 0)
                break;

            foreach (var group in groups.EnumerateArray())
            {
                if (group.TryGetProperty("id", out var idProp))
                    groupIds.Add(idProp.GetInt32());
            }

            if (groups.GetArrayLength() < 100)
                break;
            page++;
        }

        return groupIds.ToArray();
    }
}
