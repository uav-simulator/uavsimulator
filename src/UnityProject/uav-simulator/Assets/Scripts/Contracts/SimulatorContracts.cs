using System;

namespace UavSimulator.Contracts
{
    [Serializable]
    public sealed class Vector3f
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    public sealed class Quaternionf
    {
        public float x;
        public float y;
        public float z;
        public float w;
    }

    [Serializable]
    public sealed class Posef
    {
        public Vector3f position;
        public Quaternionf rotation;
    }

    [Serializable]
    public sealed class CameraFrame
    {
        public string frameId;

        public int width;
        public int height;

        public string format;
        public string encoding;

        public long timestamp;
        public string timeBase;

        public int bytesLength;

        public string dataRef;
        public string dataBase64;
    }

    [Serializable]
    public sealed class VehicleState
    {
        public Posef pose;

        public Vector3f linearVelocity;
        public Vector3f angularVelocity;

        public float speed;

        public long timestamp;
        public string timeBase;
    }

    [Serializable]
    public sealed class ControlCommand
    {
        public float throttle;
        public float steer;
        public float brake;

        public long timestamp;
        public string timeBase;
    }

    [Serializable]
    public sealed class ConfigKeyValue
    {
        public string key;
        public string value;
    }

    [Serializable]
    public sealed class SimulationConfig
    {
        public int seed;
        public float timeScale;

        public string selectedTrackId;
        public string selectedVehicleId;

        public ConfigKeyValue[] trackParams;
        public ConfigKeyValue[] vehicleParams;
        public ConfigKeyValue[] flags;
    }

    [Serializable]
    public sealed class StepResult
    {
        public VehicleState state;
        public float reward;
        public bool done;

        public ConfigKeyValue[] info;
        public CameraFrame frame;
    }

    [Serializable]
    public sealed class SensorDescriptor
    {
        public string id;
        public string sensorType;
        public string format;

        public int[] shape;
        public float rateHz;
    }

    [Serializable]
    public sealed class ActuatorDescriptor
    {
        public string id;
        public string actuatorType;

        public float min;
        public float max;
    }

    [Serializable]
    public sealed class DeviceContractDescriptor
    {
        public string deviceId;
        public string deviceType;

        public SensorDescriptor[] sensors;
        public ActuatorDescriptor[] actuators;

        public string observationSchemaJson;
        public string actionSchemaJson;
    }

    [Serializable]
    public sealed class TrackContractDescriptor
    {
        public string trackId;
        public string displayName;

        public string parametersSchemaJson;
    }

    [Serializable]
    public sealed class SimulatorContractDescriptor
    {
        public string simulatorId;
        public string simulatorName;

        public string contractVersion;
        public string transportInfo;

        public DeviceContractDescriptor[] availableVehicles;
        public TrackContractDescriptor[] availableTracks;
    }
}
