using Ks0223.Web.Backend.Endpoints;
using Ks0223.Web.Backend.Models;
using Xunit;

namespace Ks0223.Web.Backend.Tests;

public sealed class ScenarioRuntimeHostHeaderTests
{
    [Fact]
    public void Runtime_host_header_override_is_required_for_docker_host_internal()
    {
        var uri = new Uri("http://host.docker.internal:8000/reset");

        var hostHeader = ScenarioEndpoints.GetRuntimeHostHeaderOverride(uri, runningInContainer: true);

        Assert.Equal("127.0.0.1:8000", hostHeader);
    }

    [Fact]
    public void Runtime_host_header_override_is_not_used_outside_container()
    {
        var uri = new Uri("http://host.docker.internal:8000/reset");

        var hostHeader = ScenarioEndpoints.GetRuntimeHostHeaderOverride(uri, runningInContainer: false);

        Assert.Null(hostHeader);
    }

    [Fact]
    public void Runtime_reset_uri_appends_reset_path()
    {
        var uri = ScenarioEndpoints.BuildRuntimeResetUri("http://host.docker.internal:8000");

        Assert.Equal("http://host.docker.internal:8000/reset", uri.ToString());
    }

    [Fact]
    public void Scenario_load_prefers_connected_unity_session_when_client_is_provided()
    {
        var request = new LoadScenarioRequest(
            "/app/configs/scenarios/showcase-city.yaml",
            ClientId: "city-smoke",
            RuntimeMode: "unity-sim",
            AgentId: "ego");

        var shouldUseSession = ScenarioEndpoints.TryGetConnectedUnitySessionRequest(
            request,
            out var clientId,
            out var runtimeMode,
            out var agentId);

        Assert.True(shouldUseSession);
        Assert.Equal("city-smoke", clientId);
        Assert.Equal("unity-sim", runtimeMode);
        Assert.Equal("ego", agentId);
    }

    [Fact]
    public void Scenario_load_falls_back_to_base_url_without_unity_client_context()
    {
        var request = new LoadScenarioRequest("/app/configs/scenarios/showcase-city.yaml");

        var shouldUseSession = ScenarioEndpoints.TryGetConnectedUnitySessionRequest(
            request,
            out _,
            out _,
            out _);

        Assert.False(shouldUseSession);
    }
}
