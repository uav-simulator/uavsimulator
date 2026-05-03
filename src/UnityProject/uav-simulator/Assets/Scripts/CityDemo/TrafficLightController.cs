using System.Collections;
using UnityEngine;

namespace UavSimulator.CityDemo
{
    /// <summary>
    /// Coordinates a group of <see cref="TrafficLight"/>s at one intersection.
    /// All lights in <c>northSouthLights</c> share one state; same for
    /// <c>eastWestLights</c>. NS and EW are interlocked: when NS is Green/Yellow,
    /// EW is Red, and vice versa.
    ///
    /// Cycle:
    ///   NS Green     (greenSeconds)
    ///   NS Yellow    (yellowSeconds)
    ///   EW Green     (greenSeconds)   [NS Red throughout]
    ///   EW Yellow    (yellowSeconds)  [NS Red throughout]
    ///   repeat
    ///
    /// The controller exposes <see cref="AdvanceTime"/> for unit tests so the
    /// FSM can be exercised without running real coroutines.
    /// </summary>
    public sealed class TrafficLightController : MonoBehaviour
    {
        [SerializeField] private TrafficLight[] northSouthLights;
        [SerializeField] private TrafficLight[] eastWestLights;

        public float redSeconds = 8f;       // Reserved (clearance time); not part of the basic cycle.
        public float greenSeconds = 10f;
        public float yellowSeconds = 2f;

        private enum Phase
        {
            NsGreen,
            NsYellow,
            EwGreen,
            EwYellow,
        }

        private Phase phase = Phase.NsGreen;
        private float phaseElapsed;
        private bool running;
        private Coroutine driver;

        // ── Public API ──

        public void StartCycle()
        {
            if (running) return;
            running = true;
            phase = Phase.NsGreen;
            phaseElapsed = 0f;
            ApplyPhase();
            driver = StartCoroutine(Drive());
        }

        public void StopCycle()
        {
            running = false;
            if (driver != null)
            {
                StopCoroutine(driver);
                driver = null;
            }
        }

        public void ResetCycle(int seed)
        {
            // Use the seed to choose a starting phase deterministically. This way
            // adjacent intersections aren't all in lockstep when reset with the
            // same global seed.
            var rng = new System.Random(seed);
            phase = (Phase)(rng.Next() & 3);
            phaseElapsed = (float)(rng.NextDouble() * PhaseDuration(phase));
            ApplyPhase();
        }

        // ── Test-friendly tick ──

        /// <summary>
        /// Advance the FSM by <paramref name="dt"/> seconds. Public so tests can
        /// drive the controller deterministically without coroutine timing.
        /// </summary>
        public void AdvanceTime(float dt)
        {
            if (dt <= 0f) return;
            phaseElapsed += dt;
            // Multiple transitions in one call are possible if dt > phase duration.
            while (phaseElapsed >= PhaseDuration(phase))
            {
                phaseElapsed -= PhaseDuration(phase);
                phase = NextPhase(phase);
                ApplyPhase();
            }
        }

        // ── Internals ──

        private IEnumerator Drive()
        {
            while (running)
            {
                AdvanceTime(Time.deltaTime);
                yield return null;
            }
        }

        private void ApplyPhase()
        {
            switch (phase)
            {
                case Phase.NsGreen:
                    SetGroup(northSouthLights, TrafficLightState.Green);
                    SetGroup(eastWestLights, TrafficLightState.Red);
                    break;
                case Phase.NsYellow:
                    SetGroup(northSouthLights, TrafficLightState.Yellow);
                    SetGroup(eastWestLights, TrafficLightState.Red);
                    break;
                case Phase.EwGreen:
                    SetGroup(northSouthLights, TrafficLightState.Red);
                    SetGroup(eastWestLights, TrafficLightState.Green);
                    break;
                case Phase.EwYellow:
                    SetGroup(northSouthLights, TrafficLightState.Red);
                    SetGroup(eastWestLights, TrafficLightState.Yellow);
                    break;
            }
        }

        private float PhaseDuration(Phase p)
        {
            switch (p)
            {
                case Phase.NsGreen: return greenSeconds;
                case Phase.NsYellow: return yellowSeconds;
                case Phase.EwGreen: return greenSeconds;
                case Phase.EwYellow: return yellowSeconds;
                default: return greenSeconds;
            }
        }

        private static Phase NextPhase(Phase p)
        {
            switch (p)
            {
                case Phase.NsGreen: return Phase.NsYellow;
                case Phase.NsYellow: return Phase.EwGreen;
                case Phase.EwGreen: return Phase.EwYellow;
                case Phase.EwYellow: return Phase.NsGreen;
                default: return Phase.NsGreen;
            }
        }

        private static void SetGroup(TrafficLight[] group, TrafficLightState state)
        {
            if (group == null) return;
            for (var i = 0; i < group.Length; i++)
            {
                if (group[i] != null) group[i].SetState(state);
            }
        }

        // ── Test injection helpers (no [SerializeField] mutation in inspector) ──

        /// <summary>Replace the NS group. Test-only seam.</summary>
        public void SetNorthSouthLights(TrafficLight[] lights) => northSouthLights = lights;

        /// <summary>Replace the EW group. Test-only seam.</summary>
        public void SetEastWestLights(TrafficLight[] lights) => eastWestLights = lights;
    }
}
