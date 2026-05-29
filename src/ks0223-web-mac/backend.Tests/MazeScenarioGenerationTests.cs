using Ks0223.Web.Backend.Endpoints;
using Ks0223.Web.Backend.Models;
using Xunit;

namespace Ks0223.Web.Backend.Tests;

public sealed class MazeScenarioGenerationTests
{
    [Fact]
    public void Maze_reset_payload_clears_previous_curated_path()
    {
        var payload = ScenarioEndpoints.BuildMazeResetPayload(new GenerateMazeScenarioRequest(Seed: 73));

        Assert.Equal(73, payload.seed);
        Assert.Equal("track.cardboard_maze.v1", payload.selectedTrackId);
        Assert.Contains(payload.trackParams, item => item.key == "maze.seed" && item.value == "73");
        Assert.Contains(payload.trackParams, item => item.key == "maze.path_encoded" && item.value == string.Empty);
    }

    [Fact]
    public void Maze_reset_payload_carries_research_knobs()
    {
        var payload = ScenarioEndpoints.BuildMazeResetPayload(new GenerateMazeScenarioRequest(
            Seed: 120,
            LengthCells: 30,
            CorridorWidthM: 0.45f,
            LeftTurns: 6,
            RightTurns: 6,
            WallHeightM: 0.25f,
            VehicleId: "vehicle.ks0223.arcade.blue.v1",
            AgentId: "ego"));

        Assert.Equal("vehicle.ks0223.arcade.blue.v1", payload.selectedVehicleId);
        Assert.Contains(payload.agents, agent => agent.agentId == "ego" && agent.vehicleId == "vehicle.ks0223.arcade.blue.v1");
        Assert.Contains(payload.trackParams, item => item.key == "maze.length_cells" && item.value == "30");
        Assert.Contains(payload.trackParams, item => item.key == "maze.left_turns" && item.value == "6");
        Assert.Contains(payload.trackParams, item => item.key == "maze.right_turns" && item.value == "6");
        Assert.Contains(payload.trackParams, item => item.key == "maze.wall_height_m" && item.value == "0.25");
    }
}
