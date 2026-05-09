using System.Text.Json;
using Ks0223.Web.Backend.Models;
using Xunit;

namespace Ks0223.Web.Backend.Tests;

/// <summary>
/// Round-trip the wire DTOs through System.Text.Json with the same
/// camelCase serializer the ASP.NET Core minimal-API uses by default.
/// These tests pin the JSON shape that the frontend (`api.ts`) and the
/// Python `SimClient` rely on — accidentally renaming a record field
/// would break both clients silently.
/// </summary>
public sealed class ContractsSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CommandRequest_RoundTrips_With_CamelCase_Field_Names()
    {
        var src = new CommandRequest("op", "unity-sim", "DirForward", "ego");
        var json = JsonSerializer.Serialize(src, JsonOptions);

        Assert.Contains("\"clientId\":\"op\"", json);
        Assert.Contains("\"runtimeMode\":\"unity-sim\"", json);
        Assert.Contains("\"command\":\"DirForward\"", json);
        Assert.Contains("\"agentId\":\"ego\"", json);

        var parsed = JsonSerializer.Deserialize<CommandRequest>(json, JsonOptions);
        Assert.Equal(src, parsed);
    }

    [Fact]
    public void CommandResponse_Serializes_Sent_And_Optional_Error()
    {
        var ok = new CommandResponse(Sent: true);
        var fail = new CommandResponse(Sent: false, Error: "tcp closed");

        var okJson = JsonSerializer.Serialize(ok, JsonOptions);
        var failJson = JsonSerializer.Serialize(fail, JsonOptions);

        Assert.Contains("\"sent\":true", okJson);
        Assert.Contains("\"sent\":false", failJson);
        Assert.Contains("\"error\":\"tcp closed\"", failJson);
    }

    [Fact]
    public void ConnectRequest_Tolerates_Null_Host_And_Port()
    {
        var src = new ConnectRequest("op", "real-robot", Host: null, Port: null);
        var json = JsonSerializer.Serialize(src, JsonOptions);
        var parsed = JsonSerializer.Deserialize<ConnectRequest>(json, JsonOptions);
        Assert.NotNull(parsed);
        Assert.Equal("op", parsed!.ClientId);
        Assert.Equal("real-robot", parsed.RuntimeMode);
        Assert.Null(parsed.Host);
        Assert.Null(parsed.Port);
    }

    [Fact]
    public void ConnectionTargetDto_Defaults_Runtime_Mode_To_RealRobot()
    {
        var src = new ConnectionTargetDto("192.168.1.121", 5051);
        Assert.Equal("real-robot", src.RuntimeMode);

        var json = JsonSerializer.Serialize(src, JsonOptions);
        Assert.Contains("\"host\":\"192.168.1.121\"", json);
        Assert.Contains("\"port\":5051", json);
        Assert.Contains("\"runtimeMode\":\"real-robot\"", json);
    }

    [Fact]
    public void UnityRuntimeCatalogDto_Round_Trips_With_Empty_Lists()
    {
        var src = new UnityRuntimeCatalogDto(
            SelectedTrackId: "track.basic_arena.v1",
            SelectedVehicleId: "vehicle.ks0223.v1",
            SelectedCameraMode: "spectator",
            SelectedControlAgentId: "ego",
            SelectedCameraAgentId: "ego",
            Tracks: new List<UnityRuntimeOptionDto>(),
            Vehicles: new List<UnityRuntimeOptionDto>(),
            Agents: new List<UnityRuntimeAgentDto>());

        var json = JsonSerializer.Serialize(src, JsonOptions);
        var parsed = JsonSerializer.Deserialize<UnityRuntimeCatalogDto>(json, JsonOptions);

        Assert.NotNull(parsed);
        Assert.Equal("track.basic_arena.v1", parsed!.SelectedTrackId);
        Assert.Equal("vehicle.ks0223.v1", parsed.SelectedVehicleId);
        Assert.Empty(parsed.Tracks);
        Assert.Empty(parsed.Agents);
    }

    [Fact]
    public void UnityRuntimeAgentSelectionRequest_Defaults_IsPrimary_To_False()
    {
        var src = new UnityRuntimeAgentSelectionRequest(AgentId: "ego", VehicleId: "v1");
        Assert.False(src.IsPrimary);

        var json = JsonSerializer.Serialize(src, JsonOptions);
        var parsed = JsonSerializer.Deserialize<UnityRuntimeAgentSelectionRequest>(json, JsonOptions);
        Assert.NotNull(parsed);
        Assert.False(parsed!.IsPrimary);
    }
}
