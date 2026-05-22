namespace UavSimulator.CityDemo
{
    /// <summary>
    /// Components that decide whether a vehicle should brake based on
    /// environment perception. Two implementations live in the runtime:
    ///   - <see cref="UavSimulator.Vehicles.TrafficLightAwareController"/> —
    ///     ground-truth raycast path used by tests and the offline label pipeline.
    ///   - <see cref="OnnxTrafficLightAwareController"/> —
    ///     ONNX-inference path that consumes a camera frame.
    ///
    /// Both report identical semantics:
    ///   Red    → ShouldBrake=true,  intensity 1.0
    ///   Yellow → ShouldBrake=true,  intensity 0.5
    ///   Green  → ShouldBrake=false, intensity 0.0
    /// </summary>
    public interface IMovementGate
    {
        bool ShouldBrake(out float brakeIntensity);
    }
}
