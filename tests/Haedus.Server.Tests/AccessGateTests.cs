using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Haedus.Server.Tests;

public class AccessGateTests
{
    private static async Task<(int Status, bool Reached, HttpContext Context)> Send(
        string? token, string remote, string path = "/", string? query = null, string? cookie = null,
        string host = "haedus.lan:5185", string method = "GET", string? origin = null, string? fetchSite = null,
        string[]? allowedHosts = null)
    {
        var reached = false;
        var gate = new AccessGate(_ => { reached = true; return Task.CompletedTask; },
            Options.Create(new ServerOptions { AccessToken = token, AllowedHosts = allowedHosts ?? [] }), NullLogger<AccessGate>.Instance);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        context.Request.Path = path;
        context.Request.Method = method;
        context.Request.Host = new HostString(host);
        if (origin is not null)
            context.Request.Headers.Origin = origin;
        if (fetchSite is not null)
            context.Request.Headers["Sec-Fetch-Site"] = fetchSite;
        if (query is not null)
            context.Request.QueryString = new QueryString(query);
        if (cookie is not null)
            context.Request.Headers.Cookie = $"{AccessGate.CookieName}={cookie}";
        context.Response.Body = new MemoryStream();
        await gate.InvokeAsync(context);
        return (context.Response.StatusCode, reached, context);
    }

    [Fact]
    public async Task Without_a_token_only_the_same_pc_gets_in()
    {
        Assert.True((await Send(null, "127.0.0.1", host: "localhost:5185")).Reached);
        Assert.True((await Send(null, "::1", host: "localhost:5185")).Reached);

        var remote = await Send(null, "192.168.1.20", host: "localhost:5185");
        Assert.False(remote.Reached);
        Assert.Equal(StatusCodes.Status403Forbidden, remote.Status);
    }

    [Fact]
    public async Task With_a_token_requests_need_the_cookie()
    {
        Assert.Equal(StatusCodes.Status401Unauthorized, (await Send("segreto", "192.168.1.20")).Status);
        Assert.Equal(StatusCodes.Status401Unauthorized, (await Send("segreto", "127.0.0.1", cookie: "sbagliato")).Status);
        Assert.True((await Send("segreto", "192.168.1.20", cookie: "segreto")).Reached);
    }

    [Fact]
    public async Task Opening_the_link_with_the_token_sets_the_cookie_and_hides_the_token()
    {
        var result = await Send("segreto", "192.168.1.20", query: "?token=segreto");

        Assert.False(result.Reached);
        Assert.Equal(StatusCodes.Status302Found, result.Status);
        Assert.Equal("/", result.Context.Response.Headers.Location.ToString());
        var cookie = result.Context.Response.Headers.SetCookie.ToString();
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StatusCodes.Status401Unauthorized, (await Send("segreto", "192.168.1.20", query: "?token=altro")).Status);
    }

    [Theory]
    [InlineData("localhost:5185")]
    [InlineData("localhost")]
    [InlineData("LOCALHOST:80")]
    [InlineData("127.0.0.1:5185")]
    [InlineData("[::1]:5185")]
    public async Task Without_a_token_loopback_hosts_are_accepted(string host) =>
        Assert.True((await Send(null, "127.0.0.1", host: host)).Reached);

    [Theory]
    [InlineData("evil.example:5185")]
    [InlineData("evil.example")]
    [InlineData("localhost.evil.example")]
    public async Task Without_a_token_a_foreign_host_is_rejected_even_from_loopback_dns_rebinding(string host)
    {
        var result = await Send(null, "127.0.0.1", host: host, origin: "http://" + host);
        Assert.False(result.Reached);
        Assert.Equal(StatusCodes.Status403Forbidden, result.Status);
    }

    [Fact]
    public async Task Without_a_token_configured_hosts_are_accepted_with_any_port()
    {
        Assert.True((await Send(null, "127.0.0.1", host: "haedus.lan:9999", allowedHosts: ["haedus.lan"])).Reached);
        Assert.False((await Send(null, "127.0.0.1", host: "evil.example", allowedHosts: ["haedus.lan"])).Reached);
    }

    [Fact]
    public async Task With_a_token_any_host_is_accepted_unless_allowed_hosts_is_set()
    {
        Assert.True((await Send("segreto", "192.168.1.20", cookie: "segreto", host: "nas.casa:5185")).Reached);
        Assert.False((await Send("segreto", "192.168.1.20", cookie: "segreto", host: "evil.example",
            allowedHosts: ["nas.casa"])).Reached);
        Assert.True((await Send("segreto", "192.168.1.20", cookie: "segreto", host: "nas.casa:5185",
            allowedHosts: ["nas.casa"])).Reached);
    }

    [Fact]
    public async Task Health_check_ignores_host_and_token()
    {
        Assert.True((await Send("segreto", "192.168.1.20", path: "/healthz", host: "qualunque")).Reached);
        Assert.True((await Send(null, "127.0.0.1", path: "/healthz", host: "evil.example")).Reached);
    }

    [Theory]
    [InlineData("POST", "/api/audio", "http://evil.example", null)]
    [InlineData("POST", "/api/audio", null, "cross-site")]
    [InlineData("POST", "/api/audio", null, "same-site")]
    [InlineData("POST", "/api/audio", "null", null)]
    [InlineData("GET", "/api/export", "http://evil.example", null)]
    [InlineData("GET", "/api/export", null, "cross-site")]
    [InlineData("DELETE", "/api/x", null, "cross-site")]
    public async Task Cross_site_unsafe_requests_are_rejected(string method, string path, string? origin, string? fetchSite)
    {
        var result = await Send(null, "127.0.0.1", path: path, host: "localhost:5185", method: method,
            origin: origin, fetchSite: fetchSite);
        Assert.False(result.Reached);
        Assert.Equal(StatusCodes.Status403Forbidden, result.Status);
    }

    [Theory]
    [InlineData("POST", "/api/audio", "http://localhost:5185", "same-origin")]
    [InlineData("POST", "/api/audio", null, "same-origin")]
    [InlineData("GET", "/api/export", null, "none")]
    [InlineData("POST", "/api/audio", null, null)]
    [InlineData("GET", "/api/export", null, null)]
    [InlineData("GET", "/", null, "cross-site")]
    public async Task Same_origin_and_non_browser_requests_are_allowed(string method, string path, string? origin, string? fetchSite)
    {
        var result = await Send(null, "127.0.0.1", path: path, host: "localhost:5185", method: method,
            origin: origin, fetchSite: fetchSite);
        Assert.True(result.Reached);
    }

    [Fact]
    public async Task Non_browser_client_with_a_valid_token_can_post()
    {
        var result = await Send("segreto", "192.168.1.20", path: "/api/audio", method: "POST", cookie: "segreto");
        Assert.True(result.Reached);
    }

    [Theory]
    [InlineData("http://haedus.lan:5185", true)]
    [InlineData("http://evil.example", false)]
    [InlineData("http://haedus.lan:9999", false)]
    [InlineData("", true)]
    public void Websocket_origin_must_match_the_host(string origin, bool expected)
    {
        var request = new DefaultHttpContext().Request;
        request.Host = new HostString("haedus.lan:5185");
        if (origin.Length > 0)
            request.Headers.Origin = origin;

        Assert.Equal(expected, AccessGate.SameOrigin(request));
    }

    [Theory]
    [InlineData("riunione.mp3", "riunione.mp3")]
    [InlineData(@"..\..\Windows\evil.wav", "evil.wav")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("ab*c?.wav", "ab_c_.wav")]
    [InlineData("..", "audio")]
    [InlineData("  ", "audio")]
    public void Uploaded_names_cannot_escape_the_upload_folder(string name, string expected) =>
        Assert.Equal(expected, BridgeEndpoints.SafeFileName(name));
}
