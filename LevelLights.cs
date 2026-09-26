using System.Collections.Generic;
using UnityEngine;

namespace PartyTime
{
    internal sealed class LevelLights
    {
        private const float RefreshSeconds = 1f;

        private readonly Transform _partyRoot;
        private readonly Dictionary<Light, float> _intensities = new Dictionary<Light, float>();
        private readonly List<Light> _lights = new List<Light>();
        private float _refreshTimer;

        public LevelLights(Transform partyRoot)
        {
            _partyRoot = partyRoot;
        }

        public void Dim(float share, float deltaTime)
        {
            _refreshTimer -= deltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = RefreshSeconds;
                Refresh();
            }
            foreach (var light in _lights)
                if (light != null) light.intensity = _intensities[light] * share;
        }

        public void Restore()
        {
            foreach (var pair in _intensities)
                if (pair.Key != null) pair.Key.intensity = pair.Value;
            _intensities.Clear();
            _lights.Clear();
        }

        private void Refresh()
        {
            _lights.Clear();
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.bakingOutput.lightmapBakeType == LightmapBakeType.Baked || light.transform.IsChildOf(_partyRoot)) continue;
                if (!_intensities.ContainsKey(light)) _intensities[light] = light.intensity;
                _lights.Add(light);
            }
        }
    }
}
