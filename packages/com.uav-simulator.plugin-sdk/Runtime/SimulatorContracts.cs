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

        public ConfigKeyValue[] telemetry;
    }

    [Serializable]
    public sealed class ControlCommand
    {
        public float throttle;
        public float steer;
        public float brake;

        public string targetAgentId;
        public string targetVehicleId;

        public long timestamp;
        public string timeBase;

        public ConfigKeyValue[] extensions;
    }

    [Serializable]
    public sealed class ConfigKeyValue
    {
        public string key;
        public string value;
    }

    [Serializable]
    public sealed class SensorDescriptor
    {
        public string id;
        public string sensorType;
        public string format;
        public string unit;

        public int[] shape;
        public float rateHz;
    }

    [Serializable]
    public sealed class ActuatorDescriptor
    {
        public string id;
        public string actuatorType;
        public string unit;

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
}
