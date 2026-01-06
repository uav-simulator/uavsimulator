using UavSimulator.Contracts;

namespace UavSimulator.Vehicles
{
    public interface IDriveActuator
    {
        void Apply(ControlCommand command);
    }
}

