using System.Net;
using MicLock.Core.Sys;
using Xunit;

namespace MicLock.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.1.0", "1.0.0", 1)]
    [InlineData("1.0.0", "1.1.0", -1)]
    [InlineData("2.0.0", "1.9.9", 1)]
    [InlineData("1.0.1", "1.0.0", 1)]
    public void CompareVersions_CalculatesCorrectPrecedence(string v1, string v2, int expectedSign)
    {
        int result = GitHubUpdateChecker.CompareVersions(v1, v2);
        if (expectedSign == 0) Assert.Equal(0, result);
        else if (expectedSign > 0) Assert.True(result > 0);
        else Assert.True(result < 0);
    }

    [Fact]
    public async Task CheckForUpdates_HandlesMissingOrInvalidEndpointGracefully()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, "{}");
        var client = new HttpClient(handler);
        var checker = new GitHubUpdateChecker(client);

        var result = await checker.CheckForUpdatesAsync("1.0.0");
        Assert.False(result.Success);
        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdates_ParsesNewerReleaseCorrectly()
    {
        string json = """
        {
            "tag_name": "v1.1.0",
            "name": "MicAnchor v1.1.0 Release",
            "body": "Added keyboard remap and persistent mute badge",
            "html_url": "https://github.com/ricardowong/mic-lock/releases/tag/v1.1.0",
            "assets": [
                {
                    "name": "MicLock-Setup-v1.1.0.exe",
                    "browser_download_url": "https://github.com/ricardowong/mic-lock/releases/download/v1.1.0/MicLock-Setup-v1.1.0.exe"
                }
            ]
        }
        """;

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, json);
        var client = new HttpClient(handler);
        var checker = new GitHubUpdateChecker(client);

        var result = await checker.CheckForUpdatesAsync("1.0.0");
        Assert.True(result.Success);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.1.0", result.LatestVersion);
        Assert.Equal("MicAnchor v1.1.0 Release", result.ReleaseTitle);
        Assert.Equal("https://github.com/ricardowong/mic-lock/releases/download/v1.1.0/MicLock-Setup-v1.1.0.exe", result.InstallerDownloadUrl);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;

        public MockHttpMessageHandler(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = _statusCode,
                Content = new StringContent(_content)
            });
        }
    }
}
