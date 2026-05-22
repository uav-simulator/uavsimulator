using System.Collections.Generic;
using UnityEngine;

namespace UavSimulator.CityDemo
{
    /// <summary>
    /// ScriptableObject holding an ordered list of camera "angles" for the
    /// cinematic showcase. Each angle pins the camera transform, FOV and how
    /// long it should be held before cutting to the next entry.
    ///
    /// Authoring lives entirely as data so the showcase camera rig can be
    /// retuned without rebuilding the Unity project.
    /// </summary>
    [CreateAssetMenu(menuName = "UavSimulator/Cinematic Angles")]
    public sealed class CinematicAngles : ScriptableObject
    {
        [System.Serializable]
        public struct Angle
        {
            public string name;
            public Vector3 position;
            public Vector3 eulerAngles;
            public float fov;
            public float durationSec;
        }

        public List<Angle> angles = new();
    }
}
