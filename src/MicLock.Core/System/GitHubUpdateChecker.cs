using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MicLock.Core.Sys;

public class UpdateCheckResult
{
    public bool Success { get; set; }
    public bool IsUpdateAvailable { get; set; }
    public string CurrentVersion { get; set; } = "1.0.0";
    public string LatestVersion { get; set; } = "1.0.0";
    public string? ReleaseTitle { get; set; }
    public string? ReleaseNotes { get; set; }
    public string? HtmlUrl { get; set; }
    public string? InstallerDownloadUrl { get; set; }
    public string? ErrorMessage { get; set; }
}

public class GitHubUpdateChecker
{
    private const string GitHubApiUrl = "https://api.github.com/repos/ricardowong/mic-lock/releases/latest";
    private readonly HttpClient _httpClient;

    public GitHubUpdateChecker(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MicAnchor-Desktop", "1.0.0"));
        }
        _httpClient.Timeout = TimeSpan.FromSeconds(6);
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersion = "1.0.0")
    {
        var result = new UpdateCheckResult
        {
            CurrentVersion = currentVersion,
            LatestVersion = currentVersion
        };

        try
        {
            using var response = await _httpClient.GetAsync(GitHubApiUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                result.Success = false;
                result.ErrorMessage = $"GitHub API returned status: {response.StatusCode}";
                return result;
            }

            var jsonStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(jsonStream).ConfigureAwait(false);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tagElem) ? tagElem.GetString() ?? "" : "";
            string cleanTag = tagName.TrimStart('v', 'V').Trim();

            result.LatestVersion = string.IsNullOrWhiteSpace(cleanTag) ? currentVersion : cleanTag;
            result.ReleaseTitle = root.TryGetProperty("name", out var nameElem) ? nameElem.GetString() : null;
            result.ReleaseNotes = root.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() : null;
            result.HtmlUrl = root.TryGetProperty("html_url", out var urlElem) ? urlElem.GetString() : null;

            if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsElem.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var assetName) &&
                        assetName.GetString()?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true &&
                        asset.TryGetProperty("browser_download_url", out var dlElem))
                    {
                        result.InstallerDownloadUrl = dlElem.GetString();
                        break;
                    }
                }
            }

            result.IsUpdateAvailable = CompareVersions(result.LatestVersion, currentVersion) > 0;
            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    public static int CompareVersions(string v1, string v2)
    {
        if (Version.TryParse(v1, out var parsed1) && Version.TryParse(v2, out var parsed2))
        {
            return parsed1.CompareTo(parsed2);
        }

        return string.Compare(v1, v2, StringComparison.OrdinalIgnoreCase);
    }

    public static void OpenReleaseUrl(string? url)
    {
        string target = string.IsNullOrWhiteSpace(url) ? "https://github.com/ricardowong/mic-lock/releases" : url;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
