namespace Ks0223.Web.Backend.Options;

public sealed class RealRobotCommandOptions
{
    public bool Enabled { get; set; } = true;
    public int ForwardPulseMs { get; set; } = 40;
    public int TurnPulseMs { get; set; } = 240;
    public int ForwardTrimLeft { get; set; } = 64;
    public int ForwardTrimRight { get; set; } = 80;
    public int NeutralTrimLeft { get; set; } = 80;
    public int NeutralTrimRight { get; set; } = 80;
    public double MinForwardClearanceM { get; set; } = 0.03;
    public int AutopilotTelemetryStaleMs { get; set; } = 1500;
    public bool RequireFreshTelemetryForAutopilotForward { get; set; } = true;
    public bool DisableBackCommand { get; set; } = true;
    public bool RecoverAutopilotStopWithRightProbe { get; set; } = false;
    public int StopRecoveryTurnPulseMs { get; set; } = 80;
    public double StopRecoveryMinFrontClearanceM { get; set; } = 0.0;
}
