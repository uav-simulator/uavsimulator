using Ks0223.Web.Backend.Services;
using Xunit;

namespace Ks0223.Web.Backend.Tests;

public sealed class AutopilotCommandResolutionTests
{
    [Theory]
    [InlineData(-1.0f, "DirRight")]
    [InlineData(1.0f, "DirLeft")]
    public void SafetyDecision_Allows_InPlace_Turn_When_EStop_Blocks_Forward(float steer, string expectedCommand)
    {
        var decision = new SafetyDecision(Throttle: 0f, Steer: steer, EStopActive: true);

        var command = AutopilotService.ResolveCommandForSafetyDecision(decision);

        Assert.Equal(expectedCommand, command);
    }

    [Fact]
    public void SafetyDecision_Forces_Stop_If_EStop_Decision_Still_Has_Forward_Throttle()
    {
        var decision = new SafetyDecision(Throttle: 0.25f, Steer: 0f, EStopActive: true);

        var command = AutopilotService.ResolveCommandForSafetyDecision(decision);

        Assert.Equal("DirStop", command);
    }

    [Fact]
    public void DiscretePolicy_Does_Not_Use_DirectDrive_Because_CommandPath_Matches_Keyboard()
    {
        var decision = new SafetyDecision(Throttle: 0.18f, Steer: 0f, EStopActive: false);

        var directDrive = AutopilotService.ResolveDirectDriveForPolicy(decision, isDiscreteAction: true);

        Assert.False(directDrive.UseDirectDrive);
        Assert.True(directDrive.ClearDirectDrive);
    }

    [Fact]
    public void ContinuousPolicy_Uses_DirectDrive_Output()
    {
        var decision = new SafetyDecision(Throttle: 0.18f, Steer: -0.25f, EStopActive: false);

        var directDrive = AutopilotService.ResolveDirectDriveForPolicy(decision, isDiscreteAction: false);

        Assert.True(directDrive.UseDirectDrive);
        Assert.False(directDrive.ClearDirectDrive);
        Assert.Equal(0.18f, directDrive.Throttle, 3);
        Assert.Equal(-0.25f, directDrive.Steer, 3);
    }
}
