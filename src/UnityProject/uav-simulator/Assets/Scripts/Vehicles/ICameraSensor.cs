using UavSimulator.Contracts;

namespace UavSimulator.Vehicles
{
    public interface ICameraSensor
    {
        bool TryReadFrame(out CameraFrame frame);
    }
}

