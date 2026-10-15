using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.AzureIllusion.Api;
using Jellyfin.Plugin.AzureIllusion.Matching;
using Jellyfin.Plugin.AzureIllusion.ScheduledTasks;
using Jellyfin.Plugin.AzureIllusion.State;
using Jellyfin.Plugin.AzureIllusion.Subtitles;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AzureIllusion.Tests;

public sealed class MaintenanceRegressionTests
{
    [Fact]
    public async Task ApiTimeout_IsAnErrorRatherThanUserCancellation()
    {
        var attempts = 0;
        var client = TestClient(new StubHandler(_ => { attempts++; throw new TaskCanceledException("timeout"); }));
        var error = await Assert.ThrowsAsync<AzureIllusionApiException>(() => client.TestConnectionAsync(CancellationToken.None));
        Assert.Equal("API_TIMEOUT", error.Code);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task TitleLookup_ChecksAllPagesBeforeClaimingUniqueness()
    {
        var calls = 0;
        var candidate = new AnimeItem("one", "one", "111321", new AnimeTitles("Example Season 2", null, null), "TV", 2022, 12, null);
        var client = TestClient(new StubHandler(request => {
            calls++;
            var second = request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal);
            var item = second ? candidate with { Id = "two", AniListId = "999" } : candidate;
            return Task.FromResult(JsonResponse(new { ok = true, data = new { items = new[] { item }, page = second ? 2 : 1, total = 2, totalPages = 2, limit = 100 } }));
        }));
        var items = await client.SearchAnimeAsync("Example Season 2", null, CancellationToken.None);
        Assert.Equal(2, calls);
        Assert.Null(AniListResolver.SelectExactTitleCandidate(items, "Example Season 2", null, true));
    }

    [Fact]
    public async Task IdentifierLookup_RejectsServersThatIgnoreIdentifierFilter()
    {
        var candidate = new AnimeItem("one", "one", "111321", new AnimeTitles("Example", null, null), "TV", 2022, 12, null) { MalId = 40356 };
        var client = TestClient(new StubHandler(_ => Task.FromResult(JsonResponse(new { ok = true, data = new { items = new[] { candidate }, page = 1, total = 200, totalPages = 2, limit = 100 } }))));
        Assert.Null(await client.ResolveIdentifierAsync("malId", "40356", CancellationToken.None));
    }

    private static HttpResponseMessage JsonResponse(object value)
        => new(System.Net.HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))) };

    private static AzureIllusionApiClient TestClient(HttpMessageHandler handler)
        => new(new HttpClient(handler), new ApiRequestGate(TimeProvider.System, TimeSpan.Zero), null!,
            NullLogger<AzureIllusionApiClient>.Instance, new Configuration.PluginConfiguration { ApiKey = "azai_test_fixture_only" });

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
    [Theory]
    [InlineData(401, "{\"ok\":false,\"error\":{\"code\":\"UNAUTHORIZED\",\"message\":\"invalid key\"}}")]
    [InlineData(403, "<html>forbidden</html>")]
    [InlineData(404, "<html>not found</html>")]
    [InlineData(429, "{\"ok\":false,\"error\":{\"code\":\"RATE_LIMIT\",\"message\":\"wait\"}}")]
    [InlineData(502, "gateway failure")]
    public async Task HttpErrors_PreserveStatusForJsonAndProxyResponses(int status, string content)
    {
        using var response = new HttpResponseMessage((System.Net.HttpStatusCode)status) { Content = new StringContent(content) };
        var error = await Assert.ThrowsAsync<AzureIllusionApiException>(() => AzureIllusionApiClient.ThrowApiErrorAsync(response, CancellationToken.None));
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(status == 404, error.IsEndpointUnavailable);
    }
    [Fact]
    public void Updates_ProcessEveryRecordAcrossMultipleBoundedBatches()
    {
        var records = Enumerable.Range(0, 701).Select(index => new ManagedSubtitleDownload($"media-{index:D4}", $"release-{index % 270}", "checksum", DateTimeOffset.UtcNow));
        var batches = UpdateDownloadedSubtitlesTask.BuildUpdateBatches(records, 250).ToArray();
        Assert.Equal([250, 250, 201], batches.Select(batch => batch.Length));
        Assert.Equal(701, batches.SelectMany(batch => batch).Select(record => record.MediaKey).Distinct().Count());
        Assert.All(batches, batch => Assert.InRange(batch.Select(record => record.ReleaseId).Distinct().Count(), 1, 250));
    }

    [Fact]
    public void IncompleteRevisionResponse_IsNotInterpretedAsRemovedSubtitles()
    {
        var available = new SubtitleRevisionItem("a", "available", "checksum", 12, DateTimeOffset.UtcNow, "v1");
        Assert.Throws<InvalidDataException>(() => UpdateDownloadedSubtitlesTask.ValidateRevisionBatch(["a", "b"], [available]));
        Assert.Throws<InvalidDataException>(() => UpdateDownloadedSubtitlesTask.ValidateRevisionBatch(["a"], [available with { Status = "unknown" }]));
        Assert.Single(UpdateDownloadedSubtitlesTask.ValidateRevisionBatch(["a", "a"], [available]));
    }
    [Fact]
    public async Task DisabledDownloads_DoNotBlockUpdates()
    {
        var updates = 0;
        await DownloadMissingSubtitlesTask.RunMaintenanceStagesAsync(false, true,
            () => throw new Exception("must not download"), () => { updates++; return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal(1, updates);
    }

    [Fact]
    public async Task FailedDownloads_StillRunUpdatesAndReportFailure()
    {
        var updates = 0;
        var error = await Assert.ThrowsAsync<AggregateException>(() => DownloadMissingSubtitlesTask.RunMaintenanceStagesAsync(true, true,
            () => throw new IOException("network failure"), () => { updates++; return Task.CompletedTask; }, CancellationToken.None));
        Assert.IsType<IOException>(Assert.Single(error.InnerExceptions));
        Assert.Equal(1, updates);
    }

    [Fact]
    public async Task Cancellation_PreventsNextStage()
    {
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DownloadMissingSubtitlesTask.RunMaintenanceStagesAsync(true, true,
            () => { cancellation.Cancel(); return Task.CompletedTask; }, () => throw new Exception("must not update"), cancellation.Token));
    }

    [Theory]
    [InlineData(404, true)]
    [InlineData(405, true)]
    [InlineData(501, true)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(429, false)]
    [InlineData(500, false)]
    public void OnlyUnsupportedEndpointsPermitFallback(int status, bool expected)
        => Assert.Equal(expected, new AzureIllusionApiException("failure", statusCode: status).IsEndpointUnavailable);

    [Fact]
    public void ExplicitEpisodeRanges_ApplyOffsetAndRejectOverlap()
    {
        using var mapping = JsonDocument.Parse("""
            [{"anilistId":"111","episodeStart":1,"episodeEnd":12,"episodeOffset":0},
             {"anilistId":"222","episodeStart":13,"episodeEnd":24,"episodeOffset":-12}]
            """);
        var match = AniListResolver.ParseMappingValue(mapping.RootElement, 14);
        Assert.Equal("222", match!.AniListId);
        Assert.Equal(2, match.Episode);
        Assert.Null(AniListResolver.ParseMappingValue(mapping.RootElement, 25));
        Assert.Null(AniListResolver.ParseMappingValue(mapping.RootElement, null));
        using var overlap = JsonDocument.Parse("""
            [{"anilistId":"111","episodeStart":1,"episodeEnd":20,"episodeOffset":0},
             {"anilistId":"222","episodeStart":13,"episodeEnd":24,"episodeOffset":-12}]
            """);
        Assert.Null(AniListResolver.ParseMappingValue(overlap.RootElement, 14));
    }

    [Fact]
    public void LaterSeasons_DoNotInheritUnqualifiedParentMapping()
    {
        var request = new SubtitleSearchRequest { ContentType = VideoContentType.Episode, ParentIndexNumber = 2, IndexNumber = 1, MediaPath = "/anime/show/season2/one.mkv" };
        Assert.Null(AniListResolver.ResolveLocalMapping(request, """{"path:/anime/show":"123"}"""));
        Assert.Equal("456", AniListResolver.ResolveLocalMapping(request, """{"path:/anime/show#season:2":"456"}"""));
        Assert.Empty(AniListResolver.BuildTitleFallbacks("Main series", "Season 0", 0));
    }

    [Fact]
    public void ActualSeasonIdentifier_WinsOverInheritedRequestAndNamedSeasonsDoNotInheritRoot()
    {
        var root = new Dictionary<string, string> { ["AniList"] = "35790" };
        var season = new Dictionary<string, string> { ["AniList"] = "111321" };
        Assert.Equal("111321", AniListResolver.SelectMetadataIdentifier(root, null, season, root, 2)!.AniListId);
        Assert.Null(AniListResolver.SelectMetadataIdentifier(root, null, null, root, 2));
        Assert.Null(AniListResolver.SelectMetadataIdentifier(root, null, null, root, 1, "Named separate part"));
        Assert.Equal("35790", AniListResolver.SelectMetadataIdentifier(root, null, null, root, 1, "Season 1")!.AniListId);
        Assert.False(AniListResolver.SelectMetadataIdentifier(season, root, null, null, 2)!.IsConfident);
    }

    [Fact]
    public async Task PendingDownloads_AreNotDeduplicatedUntilLocalFileIsConfirmed()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pna-pending", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new DownloadStateStore(NullLogger<DownloadStateStore>.Instance, Path.Combine(directory, "state.json"));
            using var source = new MemoryStream(Encoding.UTF8.GetBytes("verified subtitle"));
            var (stream, checksum) = await AzureIllusionSubtitleProvider.BufferVerifiedSubtitleAsync(source, null, CancellationToken.None);
            await using var verified = stream;
            var pending = new ManagedSubtitleDownload("media", "release", checksum, DateTimeOffset.UtcNow, Pending: true);
            await store.MarkDownloadedAsync(pending, CancellationToken.None);
            Assert.False(await store.ContainsAsync("media", "release", checksum, CancellationToken.None));
            var local = Path.Combine(directory, "subtitle.ass");
            Assert.False(await UpdateDownloadedSubtitlesTask.CanConfirmPendingAsync(pending, local, CancellationToken.None));
            await File.WriteAllTextAsync(local, "partial");
            Assert.False(await UpdateDownloadedSubtitlesTask.CanConfirmPendingAsync(pending, local, CancellationToken.None));
            await File.WriteAllBytesAsync(local, stream.ToArray());
            Assert.True(await UpdateDownloadedSubtitlesTask.CanConfirmPendingAsync(pending, local, CancellationToken.None));
            await store.MarkDownloadedAsync(pending with { Pending = false, LocalPath = local, Checksum = null }, CancellationToken.None);
            await store.MarkDownloadedAsync(pending, CancellationToken.None);
            Assert.True(await store.ContainsAsync("media", "release", checksum, CancellationToken.None));
            Assert.False(Assert.Single(await store.GetAllAsync(CancellationToken.None)).Pending);
            Assert.Equal(checksum, Assert.Single(await store.GetAllAsync(CancellationToken.None)).Checksum);
            await store.RemovePendingAsync("media", "release", CancellationToken.None);
            Assert.Single(await store.GetAllAsync(CancellationToken.None));
            File.Delete(local);
            Assert.False(await store.ContainsAsync("media", "release", checksum, CancellationToken.None));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CorruptOrEmptyDownload_IsNeverConfirmed()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("wrong content"));
        await Assert.ThrowsAsync<InvalidDataException>(() => AzureIllusionSubtitleProvider.BufferVerifiedSubtitleAsync(source, "incorrect", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => AzureIllusionSubtitleProvider.BufferVerifiedSubtitleAsync(new MemoryStream(), null, CancellationToken.None));
    }

    [Fact]
    public void HistoricalDiagnostics_GroupEpisodesButKeepVersionsSeparate()
    {
        var now = DateTimeOffset.UtcNow;
        var events = new[] {
            new PluginDiagnosticEvent(now, "warning", "ANILIST_SEASON_UNRESOLVED", "Yuru Camp, season 2", "ep1.mkv"),
            new PluginDiagnosticEvent(now.AddMinutes(1), "warning", "ANILIST_SEASON_UNRESOLVED", "Yuru Camp, season 2", "ep2.mkv"),
            new PluginDiagnosticEvent(now.AddHours(1), "warning", "ANILIST_SEASON_UNRESOLVED", "Yuru Camp, season 2", "ep3.mkv", "0.7.1.0"),
        };
        var grouped = DiagnosticEventStore.GroupEvents(events);
        Assert.Equal(2, grouped.Count);
        Assert.Equal(2, grouped.Single(item => item.PluginVersion is null).Occurrences);
    }
}
