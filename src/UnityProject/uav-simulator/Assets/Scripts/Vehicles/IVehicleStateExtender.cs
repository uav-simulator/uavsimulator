using System.Collections.Generic;
using UavSimulator.Contracts;

namespace UavSimulator.Vehicles
{
    /// <summary>
    /// Components on a vehicle GameObject that contribute key-value entries
    /// to <see cref="VehicleState.telemetry"/> on each telemetry tick.
    /// Aggregated by <see cref="VehicleBase.MergeTelemetry"/>.
    /// </summary>
    public interface IVehicleStateExtender
    {
        IEnumerable<ConfigKeyValue> BuildExtensions();
    }
}
