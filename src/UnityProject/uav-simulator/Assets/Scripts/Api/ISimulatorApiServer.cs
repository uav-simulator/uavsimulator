namespace UavSimulator.Api
{
    public interface ISimulatorApiServer
    {
        bool IsRunning { get; }
        int Port { get; }

        void Start();
        void Stop();
    }
}

