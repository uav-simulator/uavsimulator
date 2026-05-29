using System.Net;
using System.Text;
using Ks0223.Web.Backend.Hubs;
using Ks0223.Web.Backend.Options;
using Ks0223.Web.Backend.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ks0223.Web.Backend.Tests;

public sealed class UnityRuntimeModelFrameTests
{
    [Fact]
    public async Task Model_frame_cache_is_separate_from_operator_camera_frame()
    {
        var operatorFrameBytes = new byte[] { 1, 2, 3, 4 };
        var modelFrameBytes = new byte[] { 9, 8, 7, 6 };
        using var handler = new ResetFrameHandler($$"""
        {
          "state": { "telemetry": [] },
          "frame": { "dataBase64": "{{Convert.ToBase64String(operatorFrameBytes)}}" },
          "modelFrame": { "dataBase64": "{{Convert.ToBase64String(modelFrameBytes)}}" }
        }
        """);
        var provider = CreateProvider(handler);

        using var document = await provider.ResetSimulationWithPayloadAsync(new { }, "ego", CancellationToken.None);

        Assert.True(provider.TryGetLatestFrame("ego", out var operatorFrame, out _, out _, out _));
        Assert.Equal(operatorFrameBytes, operatorFrame);

        Assert.True(provider.TryGetLatestModelFrame("ego", out var modelFrame, out _, out _, out _));
        Assert.Equal(modelFrameBytes, modelFrame);
    }

    private static UnityKs0223RuntimeProvider CreateProvider(HttpMessageHandler handler)
    {
        var logger = new SessionLogger(
            Microsoft.Extensions.Options.Options.Create(new LoggingOptions
            {
                Directory = Path.Combine("logs", Guid.NewGuid().ToString("N")),
            }),
            new TestEnvironment());

        return new UnityKs0223RuntimeProvider(
            logger,
            new StubHttpClientFactory(handler),
            new NoopHubContext(),
            NullLogger<UnityKs0223RuntimeProvider>.Instance);
    }

    private sealed class ResetFrameHandler(string resetJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var statusCode = path == "/reset" ? HttpStatusCode.OK : HttpStatusCode.NotFound;
            var json = path == "/reset" ? resetJson : """{ "error": "unexpected test path" }""";

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class NoopHubContext : IHubContext<TelemetryHub>
    {
        public IHubClients Clients { get; } = new NoopHubClients();
        public IGroupManager Groups { get; } = new NoopGroupManager();
    }

    private sealed class NoopHubClients : IHubClients
    {
        private static readonly IClientProxy Proxy = new NoopClientProxy();

        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }

    private sealed class NoopClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Tests";
        public string ApplicationName { get; set; } = "Ks0223.Web.Backend.Tests";
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
