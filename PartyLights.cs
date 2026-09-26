using UnityEngine;

namespace PartyTime
{
    internal sealed class PartyLights
    {
        private const float RingRadius = 7f;
        private const float WallGap = 0.5f;
        private const float SpotAngle = 32f;
        private const float InnerSpotAngle = 18f;
        private const float Strength = 30f;
        private const float ColorChangeSeconds = 0.08f;
        private const float SweepCenter = 3f;
        private const float SweepSwing = 2f;

        private readonly Light[] _lights;
        private readonly Color[] _colors;
        private readonly Vector3 _floor;
        private float _sweep;

        public PartyLights(Transform parent, PartyStage stage, int count, float height)
        {
            _floor = stage.Floor;
            _lights = new Light[count];
            _colors = new Color[count];
            var hub = new Vector3(_floor.x, height, _floor.z);
            for (int i = 0; i < count; i++)
            {
                var direction = Quaternion.Euler(0f, 45f + i * 360f / count, 0f) * Vector3.forward;
                float reach = RingRadius;
                if (Physics.Raycast(hub, direction, out var hit, RingRadius + WallGap, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    reach = Mathf.Max(1f, hit.distance - WallGap);

                var go = new GameObject("Party Light " + (i + 1));
                go.transform.SetParent(parent, false);
                go.transform.position = hub + direction * reach;
                var light = go.AddComponent<Light>();
                light.type = LightType.Spot;
                light.spotAngle = SpotAngle;
                light.innerSpotAngle = InnerSpotAngle;
                light.shadows = LightShadows.None;
                light.intensity = 0f;
                _colors[i] = PartyShow.Palette[i * 3 % PartyShow.Palette.Length];
                light.color = _colors[i];
                _lights[i] = light;
            }
        }

        public void Update(float deltaTime, MusicAnalyzer music, float amount, float flash)
        {
            _sweep += deltaTime * (0.35f + 0.65f * music.Loudness);
            float colorFollow = 1f - Mathf.Exp(-deltaTime / ColorChangeSeconds);
            for (int i = 0; i < _lights.Length; i++)
            {
                var light = _lights[i];
                float side = i % 2 == 0 ? 1f : -1f;
                float angle = i * Mathf.PI * 2f / _lights.Length + side * _sweep * 0.9f;
                float radius = SweepCenter + SweepSwing * Mathf.Sin(_sweep * 1.3f + i * 1.7f);
                var aim = _floor + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                var throwVector = aim - light.transform.position;
                light.transform.rotation = Quaternion.LookRotation(throwVector);

                if (music.Beat) _colors[i] = PartyShow.Palette[(music.BeatCount + i * 3) % PartyShow.Palette.Length];
                light.color = Color.Lerp(light.color, _colors[i], colorFollow);
                float distance = throwVector.magnitude;
                light.range = distance * 2f + 2f;
                light.intensity = Strength * distance * distance * amount * (0.5f + 0.5f * music.Loudness + 1.5f * flash);
            }
        }
    }
}
