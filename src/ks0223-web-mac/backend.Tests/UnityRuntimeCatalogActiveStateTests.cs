using System.Net;
using System.Text;
using Ks0223.Web.Backend.Options;
using Ks0223.Web.Backend.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ks0223.Web.Backend.Tests;

public sealed class UnityRuntimeCatalogActiveStateTests
{
    [Fact]
    public async Task Runtime_catalog_prefers_active_health_track_and_agents()
    {
        using var handler = new ContractAndHealthHandler(
            contractJson: """
            {
              "availableVehicles": [
                { "deviceId": "vehicle.ks0223.v1", "displayName": "KS0223" },
                { "deviceId": "vehicle.arcade.blue.v1", "displayName": "Blue arcade" }
              ],
              "availableTracks": [
                { "trackId": "track.roadsystem_realistic.v2", "displayName": "RoadSystem" },
                { "trackId": "track.cardboard_maze.v1", "displayName": "Cardboard Maze" }
              ]
            }
            """,
            healthJson: """
            {
              "status": "ok",
              "activeTrackId": "track.cardboard_maze.v1",
              "activeVehicleId": "vehicle.ks0223.v1",
              "activeAgentIds": ["agent-1"],
              "activeVehicleIds": ["vehicle.ks0223.v1"]
            }
            """);
        var provider = CreateProvider(handler);

        var catalog = await provider.GetRuntimeCatalogAsync("127.0.0.1", 8000, CancellationToken.None);

        Assert.Equal("track.cardboard_maze.v1", catalog.SelectedTrackId);
        Assert.Equal("vehicle.ks0223.v1", catalog.SelectedVehicleId);
        var agent = Assert.Single(catalog.Agents);
        Assert.Equal("agent-1", agent.AgentId);
        Assert.Equal("vehicle.ks0223.v1", agent.VehicleId);
        Assert.True(agent.IsPrimary);
        Assert.Equal("agent-1", catalog.SelectedControlAgentId);
        Assert.Equal("agent-1", catalog.SelectedCameraAgentId);
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
            hubContext: null!,
            NullLogger<UnityKs0223RuntimeProvider>.Instance);
    }

    private sealed class ContractAndHealthHandler(string contractJson, string healthJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var json = path switch
            {
                "/contract" => contractJson,
                "/health" => healthJson,
                _ => """{ "error": "unexpected test path" }""",
            };
            var statusCode = path is "/contract" or "/health" ? HttpStatusCode.OK : HttpStatusCode.NotFound;

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
