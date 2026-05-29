using Ks0223.Web.Backend.Endpoints;
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
}
