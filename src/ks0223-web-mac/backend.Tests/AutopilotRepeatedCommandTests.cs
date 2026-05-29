using Ks0223.Web.Backend.Services;
using Xunit;

namespace Ks0223.Web.Backend.Tests;

public sealed class AutopilotRepeatedCommandTests
{
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
