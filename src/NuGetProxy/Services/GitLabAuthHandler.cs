namespace NuGetProxy.Services;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NuGetProxy.Configuration;

/// <summary>
/// DelegatingHandler that adds Basic auth to GitLab NuGet registry requests.
/// Auto-detects the GitLab username from the PAT on first use.
/// </summary>
public class GitLabAuthHandler : DelegatingHandler
{
    private readonly ProxyOptions _options;
    private readonly ILogger<GitLabAuthHandler> _logger;
    private AuthenticationHeaderValue? _authHeader;
    private bool _resolved;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public GitLabAuthHandler(IOptions<ProxyOptions> options, ILogger<GitLabAuthHandler> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!_resolved)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                if (!_resolved)
                    await ResolveUsernameAsync(cancellationToken);
            }
            finally
            {
                _lock.Release();
            }
        }

        if (_authHeader != null)
            request.Headers.Authorization = _authHeader;

        return await base.SendAsync(request, cancellationToken);
    }

    private async Task ResolveUsernameAsync(CancellationToken cancellationToken)
    {
        var token = _options.GitLab.ServiceToken;
        if (string.IsNullOrEmpty(token))
        {
            _resolved = true;
            return;
        }

        var gitlabUrl = _options.GitLab.Url.TrimEnd('/');
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{gitlabUrl}/api/v4/user");
            request.Headers.Add("PRIVATE-TOKEN", token);

            var response = await base.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var doc = JsonDocument.Parse(json);
                var username = doc.RootElement.GetProperty("username").GetString()!;

                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{token}"));
                _authHeader = new AuthenticationHeaderValue("Basic", credentials);
                _logger.LogInformation("GitLab auth configured for user '{Username}'", username);
            }
            else
            {
                _logger.LogWarning("Failed to resolve GitLab username (HTTP {Status}), falling back to 'root'",
                    (int)response.StatusCode);
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"root:{token}"));
                _authHeader = new AuthenticationHeaderValue("Basic", credentials);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve GitLab username, falling back to 'root'");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"root:{token}"));
            _authHeader = new AuthenticationHeaderValue("Basic", credentials);
        }

        _resolved = true;
    }
}
