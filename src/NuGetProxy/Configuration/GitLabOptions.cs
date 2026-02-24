namespace NuGetProxy.Configuration;

public class GitLabOptions
{
    public string Url { get; set; } = string.Empty;
    public int[] GroupIds { get; set; } = [];
    public string ServiceToken { get; set; } = string.Empty;
    public int RefreshIntervalSeconds { get; set; } = 300;
}
