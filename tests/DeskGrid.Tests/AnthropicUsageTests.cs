using System.IO;
using System.Net;
using System.Net.Http;
using DeskGrid.Island;

namespace DeskGrid.Tests;

public sealed class AnthropicUsageTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);

    private const string Answer =
        "{\"five_hour\":{\"utilization\":37.5,\"resets_at\":\"2026-11-12T11:00:00.000000+00:00\"},"
        + "\"seven_day\":{\"utilization\":12,\"resets_at\":\"2026-11-15T09:00:00+00:00\"},\"seven_day_opus\":null}";

    private sealed class Stub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public System.Net.Http.Headers.AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Calls++;
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public void TheAnswer_GivesBothWindows()
    {
        var plan = AnthropicUsage.Parse(Answer, Now)!;

        Assert.Equal(37.5, plan.FivePercent);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 11, 0, 0, TimeSpan.Zero), plan.FiveResets);
        Assert.Equal(12, plan.WeekPercent);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"five_hour\":null}")]
    [InlineData("{\"five_hour\":{\"utilization\":\"high\",\"resets_at\":\"2026-11-12T11:00:00+00:00\"}}")]
    [InlineData("{\"five_hour\":{\"utilization\":5,\"resets_at\":\"2026-11-12T07:00:00+00:00\"}}")] // The window has ended.
    public void NoRunningWindow_OrBrokenAnswer_GivesNothing(string json) => Assert.Null(AnthropicUsage.Parse(json, Now));

    [Fact]
    public void ThePercentage_IsKeptWithin0And100() =>
        Assert.Equal(100, AnthropicUsage.Parse("{\"five_hour\":{\"utilization\":140,\"resets_at\":\"2026-11-12T11:00:00+00:00\"}}", Now)!.FivePercent);

    [Fact]
    public void TheToken_IsReadFromTheCredentials_UnlessItIsEmptyOrExpired()
    {
        var future = Now.AddHours(2).ToUnixTimeMilliseconds();
        var past = Now.AddHours(-2).ToUnixTimeMilliseconds();

        Assert.Equal("t0ken", AnthropicUsage.TokenOf("{\"claudeAiOauth\":{\"accessToken\":\"t0ken\",\"expiresAt\":" + future + "}}", Now));
        Assert.Equal("t0ken", AnthropicUsage.TokenOf("{\"claudeAiOauth\":{\"accessToken\":\"t0ken\",\"expiresAt\":0}}", Now)); // Unknown: tried.
        Assert.Null(AnthropicUsage.TokenOf("{\"claudeAiOauth\":{\"accessToken\":\"t0ken\",\"expiresAt\":" + past + "}}", Now));
        Assert.Null(AnthropicUsage.TokenOf("{\"claudeAiOauth\":{\"accessToken\":\"\",\"refreshToken\":\"\"}}", Now));
        Assert.Null(AnthropicUsage.TokenOf("{\"mcpOAuth\":{}}", Now));
        Assert.Null(AnthropicUsage.TokenOf("garbage", Now));
    }

    [Fact]
    public async Task Fetching_SendsTheTokenAsABearer_AndReadsTheAnswer()
    {
        var stub = new Stub(HttpStatusCode.OK, Answer);
        using var client = new HttpClient(stub);

        var plan = await AnthropicUsage.FetchAsync(client, "t0ken", Now, TestContext.Current.CancellationToken);

        Assert.Equal(37.5, plan!.FivePercent);
        Assert.Equal("Bearer", stub.Authorization!.Scheme);
        Assert.Equal("t0ken", stub.Authorization.Parameter);
    }

    [Fact]
    public async Task AnErrorAnswer_GivesNothing()
    {
        using var client = new HttpClient(new Stub(HttpStatusCode.Unauthorized, "{}"));

        Assert.Null(await AnthropicUsage.FetchAsync(client, "t0ken", Now, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OnlineUsage_AsksOnlyEveryFewMinutes_AndAfterAFailureLessOften()
    {
        var file = Path.Combine(Path.GetTempPath(), "deskgrid-cred-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(file, "{\"claudeAiOauth\":{\"accessToken\":\"t0ken\",\"expiresAt\":0}}", TestContext.Current.CancellationToken);
            var stub = new Stub(HttpStatusCode.OK, Answer);
            using var client = new HttpClient(stub);
            var online = new OnlineUsage(file, () => client);

            Assert.NotNull(await online.ReadAsync(Now));
            Assert.NotNull(await online.ReadAsync(Now.AddMinutes(1))); // The last answer stands.
            Assert.Equal(1, stub.Calls);
            Assert.NotNull(await online.ReadAsync(Now.AddMinutes(4)));
            Assert.Equal(2, stub.Calls);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task WithoutASignIn_NothingIsAsked()
    {
        var file = Path.Combine(Path.GetTempPath(), "deskgrid-cred-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(file, "{\"claudeAiOauth\":{\"accessToken\":\"\",\"refreshToken\":\"\"}}", TestContext.Current.CancellationToken);
            var stub = new Stub(HttpStatusCode.OK, Answer);
            using var client = new HttpClient(stub);
            var online = new OnlineUsage(file, () => client);

            Assert.Null(await online.ReadAsync(Now));
            Assert.Equal(0, stub.Calls);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
