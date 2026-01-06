using UavSimulator.Contracts;

namespace UavSimulator.Vehicles
{
    public interface IStateSensor
    {
        VehicleState ReadState();
    }
}

