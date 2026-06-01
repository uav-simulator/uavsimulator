using Ks0223.Web.Backend.Services;
using Xunit;

namespace Ks0223.Web.Backend.Tests;

public sealed class AutopilotRepeatedCommandTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(5, false)]
    [InlineData(6, true)]
    public void Stop_Recovery_Probe_AutoStops_After_Half_Turn(int probeCount, bool expected)
    {
        Assert.Equal(expected, AutopilotService.ShouldAutoStopForStopRecoveryProbe(probeCount));
    }

    [Theory]
    [InlineData("DirForward")]
    [InlineData("DirStop")]
    public void Repeated_Forward_And_Stop_Do_Not_Trigger_Stuck_Autostop(string command)
    {
        Assert.False(AutopilotService.ShouldAutoStopForRepeatedCommand(command, repeatedCount: 30));
        Assert.False(AutopilotService.ShouldAutoStopForRepeatedCommand(command, repeatedCount: 300));
    }

    [Theory]
    [InlineData("DirLeft")]
    [InlineData("DirRight")]
    [InlineData("DirBack")]
    public void Repeated_Turn_And_Back_Commands_Still_Trigger_Stuck_Autostop(string command)
    {
        Assert.False(AutopilotService.ShouldAutoStopForRepeatedCommand(command, repeatedCount: 29));
        Assert.True(AutopilotService.ShouldAutoStopForRepeatedCommand(command, repeatedCount: 30));
    }
}
