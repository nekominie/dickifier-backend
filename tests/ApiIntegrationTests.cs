using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using backend_dickifier.DTOs;
using Xunit;

namespace backend_dickifier.Tests;

public class ApiIntegrationTests(DickifierApiFactory factory) : IClassFixture<DickifierApiFactory>
{
    private const string StrongPass = "TestPassword_2026_Secure!";

    private static string UniqueUser() => $"usr_{Guid.NewGuid():N}"[..16];

    private static JsonElement ValidAnatomy() =>
        JsonDocument.Parse("""{"glans":{"coronaFlare":0.8}}""").RootElement;

    private async Task<AuthResponse> RegisterAsync(HttpClient client, string? username = null, string? email = null)
    {
        var name = username ?? UniqueUser();
        var res = await client.PostAsJsonAsync("/api/dickifier/auth/register", new RegisterRequest(name, StrongPass, email));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        return auth!;
    }

    [Fact]
    public async Task HealthAndCapabilities_ReturnOk()
    {
        using var client = factory.CreateBrowserClient();

        var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        var caps = await client.GetAsync("/api/dickifier/capabilities");
        Assert.Equal(HttpStatusCode.OK, caps.StatusCode);
        var json = await caps.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("anonymousPublishingEnabled", out _));
    }

    [Fact]
    public async Task Auth_RegisterLoginMeLogout_AndWeakPasswordRejected()
    {
        using var client = factory.CreateBrowserClient();

        var weakRes = await client.PostAsJsonAsync("/api/dickifier/auth/register", new RegisterRequest(UniqueUser(), "short"));
        Assert.Equal(HttpStatusCode.BadRequest, weakRes.StatusCode);

        var username = UniqueUser();
        var reg = await RegisterAsync(client, username);
        Assert.NotEmpty(reg.Token);
        Assert.Equal(username, reg.User.Username);
        Assert.Null(reg.User.Email);

        var dupRes = await client.PostAsJsonAsync("/api/dickifier/auth/register", new RegisterRequest(username, StrongPass));
        Assert.Equal(HttpStatusCode.Conflict, dupRes.StatusCode);

        var loginRes = await client.PostAsJsonAsync("/api/dickifier/auth/login", new LoginRequest(username, StrongPass));
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
        var login = await loginRes.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(login);

        DickifierApiFactory.SetBearer(client, login!.Token);
        var meRes = await client.GetAsync("/api/dickifier/auth/me");
        Assert.Equal(HttpStatusCode.OK, meRes.StatusCode);
        var me = await meRes.Content.ReadFromJsonAsync<UserProfileResponse>();
        Assert.Equal(username, me!.User.Username);

        var logoutRes = await client.PostAsync("/api/dickifier/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logoutRes.StatusCode);

        var meAfterLogout = await client.GetAsync("/api/dickifier/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAfterLogout.StatusCode);

        var refreshAfterLogout = await client.PostAsync("/api/dickifier/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);
    }

    [Fact]
    public async Task Auth_RefreshRotation_AndReuseDetectionRevokesFamily()
    {
        using var client = factory.CreateBrowserClient(handleCookies: false);
        var username = UniqueUser();

        var regRes = await client.PostAsJsonAsync("/api/dickifier/auth/register", new RegisterRequest(username, StrongPass));
        Assert.Equal(HttpStatusCode.Created, regRes.StatusCode);
        var cookie1 = ExtractRefreshCookie(regRes);
        Assert.NotNull(cookie1);

        // First rotation -> succeeds and issues cookie2
        var refresh1Req = new HttpRequestMessage(HttpMethod.Post, "/api/dickifier/auth/refresh");
        refresh1Req.Headers.Add("Cookie", cookie1);
        var refresh1Res = await client.SendAsync(refresh1Req);
        Assert.Equal(HttpStatusCode.OK, refresh1Res.StatusCode);
        var cookie2 = ExtractRefreshCookie(refresh1Res);
        Assert.NotNull(cookie2);
        Assert.NotEqual(cookie1, cookie2);
        var auth2 = await refresh1Res.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth2);

        // Replay old cookie1 -> detected as reuse -> 401 and revokes entire session family
        var replayReq = new HttpRequestMessage(HttpMethod.Post, "/api/dickifier/auth/refresh");
        replayReq.Headers.Add("Cookie", cookie1);
        var replayRes = await client.SendAsync(replayReq);
        Assert.Equal(HttpStatusCode.Unauthorized, replayRes.StatusCode);

        // Even cookie2 and auth2.Token are now revoked
        var refresh2Req = new HttpRequestMessage(HttpMethod.Post, "/api/dickifier/auth/refresh");
        refresh2Req.Headers.Add("Cookie", cookie2);
        var refresh2Res = await client.SendAsync(refresh2Req);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh2Res.StatusCode);

        var meReq = new HttpRequestMessage(HttpMethod.Get, "/api/dickifier/auth/me");
        meReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth2!.Token);
        var meRes = await client.SendAsync(meReq);
        Assert.Equal(HttpStatusCode.Unauthorized, meRes.StatusCode);
    }

    [Fact]
    public async Task Authorization_RegisteredModelOwnership_AndPrivateWorkspaceIsolation()
    {
        using var clientA = factory.CreateBrowserClient();
        using var clientB = factory.CreateBrowserClient();

        var authA = await RegisterAsync(clientA);
        var authB = await RegisterAsync(clientB);
        DickifierApiFactory.SetBearer(clientA, authA.Token);
        DickifierApiFactory.SetBearer(clientB, authB.Token);

        // User A creates a model
        var createRes = await clientA.PostAsJsonAsync("/api/dickifier/models", new CreateModelRequest(
            "UserAModel", 15.5m, 12.2m, "up", 10, "MX", 175m, 70m, ValidAnatomy()));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<CreateModelResponse>();
        Assert.NotNull(created);
        Assert.Equal("registered", created!.Status);
        Assert.Null(created.EditToken);

        // User B cannot update or delete User A's model -> 403
        var updateByB = await clientB.PutAsJsonAsync($"/api/dickifier/models/{created.Id}",
            new UpdateModelRequest("Hacked", null, null, null, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, updateByB.StatusCode);

        var deleteByB = await clientB.DeleteAsync($"/api/dickifier/models/{created.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteByB.StatusCode);

        // User A saves private workspace draft; User B does not see it
        var draftId = Guid.NewGuid();
        var draftPayload = JsonDocument.Parse($$"""
            {
              "id": "{{draftId}}",
              "name": "PrivateDraftA",
              "dimensions": { "length": 16.2, "girth": 12.5 },
              "curvature": { "direction": "up", "degrees": 10 },
              "glans": { "coronaFlare": 0.8 },
              "foreskin": { "isCircumcised": false, "coverage": 0.5 },
              "skin": { "toneHex": "#e0ac69", "roughness": 0.4 }
            }
            """).RootElement;
        var putDraft = await clientA.PutAsJsonAsync("/api/dickifier/auth/workspace", new SaveWorkspaceDraftRequest(draftPayload));
        Assert.Equal(HttpStatusCode.OK, putDraft.StatusCode);

        var meA = await (await clientA.GetAsync("/api/dickifier/auth/me")).Content.ReadFromJsonAsync<UserProfileResponse>();
        var meB = await (await clientB.GetAsync("/api/dickifier/auth/me")).Content.ReadFromJsonAsync<UserProfileResponse>();
        Assert.NotNull(meA!.WorkspaceDraft);
        Assert.Equal("PrivateDraftA", meA.WorkspaceDraft!.Value.GetProperty("name").GetString());
        Assert.Null(meB!.WorkspaceDraft);
    }

    [Fact]
    public async Task AnonymousModel_EditToken_Views_Votes_ShortCode_AndConcurrency()
    {
        using var client = factory.CreateBrowserClient();
        var modelName = $"Anon_{Guid.NewGuid():N}"[..14];

        var createRes = await client.PostAsJsonAsync("/api/dickifier/models", new CreateModelRequest(
            modelName, 15.0m, 12.0m, "up", 12, "MX", 176m, 72m, ValidAnatomy()));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<CreateModelResponse>();
        Assert.NotNull(created);
        Assert.Equal("anonymous", created!.Status);
        Assert.Equal(32, created.EditToken!.Length);
        Assert.StartsWith("DK-", created.ShortCode);

        // Query-string editToken rejected
        var queryTokenRes = await client.PutAsJsonAsync($"/api/dickifier/models/{created.Id}?editToken={created.EditToken}",
            new UpdateModelRequest("Query", null, null, null, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, queryTokenRes.StatusCode);

        // GET by ID or ShortCode does NOT increment ViewsCount
        var detail1 = await (await client.GetAsync($"/api/dickifier/models/{created.ShortCode!.ToLowerInvariant()}")).Content.ReadFromJsonAsync<ModelDetailResponse>();
        Assert.Equal(0, detail1!.ViewsCount);

        // Explicit POST /view increments once per IP
        var view1 = await client.PostAsync($"/api/dickifier/models/{created.Id}/view", null);
        Assert.Equal(HttpStatusCode.NoContent, view1.StatusCode);
        var view2 = await client.PostAsync($"/api/dickifier/models/{created.Id}/view", null);
        Assert.Equal(HttpStatusCode.NoContent, view2.StatusCode);
        var detail2 = await (await client.GetAsync($"/api/dickifier/models/{created.Id}")).Content.ReadFromJsonAsync<ModelDetailResponse>();
        Assert.Equal(1, detail2!.ViewsCount);

        // Concurrent votes from same user/IP + spoofed X-Voter-Fingerprint still count as 1
        var voteTasks = Enumerable.Range(0, 6).Select(i =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/dickifier/models/{created.Id}/vote");
            req.Headers.Add("X-Voter-Fingerprint", $"spoofed-fp-{i}");
            return client.SendAsync(req);
        });
        var voteResponses = await Task.WhenAll(voteTasks);
        Assert.All(voteResponses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var detailAfterVotes = await (await client.GetAsync($"/api/dickifier/models/{created.Id}")).Content.ReadFromJsonAsync<ModelDetailResponse>();
        Assert.Equal(1, detailAfterVotes!.UpvotesCount);

        // Update with X-Edit-Token succeeds; wrong token returns 403
        var badReq = new HttpRequestMessage(HttpMethod.Put, $"/api/dickifier/models/{created.Id}")
        {
            Content = JsonContent.Create(new UpdateModelRequest("Bad", null, null, null, null, null, null, null, null))
        };
        badReq.Headers.Add("X-Edit-Token", "wrong-token");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(badReq)).StatusCode);

        var okReq = new HttpRequestMessage(HttpMethod.Put, $"/api/dickifier/models/{created.Id}")
        {
            Content = JsonContent.Create(new UpdateModelRequest($"{modelName}_Upd", 16.0m, null, null, null, null, null, null, null))
        };
        okReq.Headers.Add("X-Edit-Token", created.EditToken);
        var okRes = await client.SendAsync(okReq);
        Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);
    }

    [Fact]
    public async Task Security_CsrfOrigin_PayloadLimits_SsrfUrlRejection_AndPlausibility()
    {
        using var client = factory.CreateBrowserClient(withOrigin: false);

        // Cross-site Origin rejected -> 403 on auth endpoints
        var evilOriginReq = new HttpRequestMessage(HttpMethod.Post, "/api/dickifier/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("nobody", StrongPass))
        };
        evilOriginReq.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(evilOriginReq)).StatusCode);

        // Sec-Fetch-Site: cross-site rejected -> 403 on auth endpoints
        var crossSiteReq = new HttpRequestMessage(HttpMethod.Post, "/api/dickifier/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("nobody", StrongPass))
        };
        crossSiteReq.Headers.Add("Sec-Fetch-Site", "cross-site");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(crossSiteReq)).StatusCode);

        // Malformed JSON / oversized payload rejected -> 400
        using var validClient = factory.CreateBrowserClient();
        var invalidJson = new StringContent("{not-json", Encoding.UTF8, "application/json");
        var invalidJsonRes = await validClient.PostAsync("/api/dickifier/models", invalidJson);
        Assert.Equal(HttpStatusCode.BadRequest, invalidJsonRes.StatusCode);

        // Auth endpoint > 8 KiB rejected -> 413
        var oversizedLogin = new StringContent(
            $"{{\"username\":\"{new string('a', 9000)}\",\"password\":\"{StrongPass}\"}}",
            Encoding.UTF8,
            "application/json");
        var oversizedRes = await validClient.PostAsync("/api/dickifier/auth/login", oversizedLogin);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversizedRes.StatusCode);

        // SSRF / external URL or SVG in AnatomyPayload rejected -> 400
        var ssrfPayload = JsonDocument.Parse("""{"textureUrl":"http://169.254.169.254/latest/meta-data/"}""").RootElement;
        var ssrfRes = await validClient.PostAsJsonAsync("/api/dickifier/models", new CreateModelRequest(
            "SsrfTest", 15m, 12m, "up", 0, "MX", 175m, 70m, ssrfPayload));
        Assert.Equal(HttpStatusCode.BadRequest, ssrfRes.StatusCode);

        // Plausibility rejected -> 400
        var implausibleRes = await validClient.PostAsJsonAsync("/api/dickifier/models", new CreateModelRequest(
            "Impossible", 50m, 12m));
        Assert.Equal(HttpStatusCode.BadRequest, implausibleRes.StatusCode);
    }

    private static string? ExtractRefreshCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        foreach (var header in cookies)
        {
            var first = header.Split(';', 2)[0].Trim();
            if (first.StartsWith($"{backend_dickifier.Security.SessionService.CookieName}=", StringComparison.Ordinal))
                return first;
        }
        return null;
    }
}
