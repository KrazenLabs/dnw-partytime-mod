using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PartyTime
{
    internal sealed class PartyGrading
    {
        private const float ClearWeatherPriority = 999f;
        private const float PartyPriority = 1000f;
        private const float MinFilterStrength = 0.001f;

        private readonly VolumeProfile _clearWeatherProfile;
        private readonly VolumeProfile _partyProfile;
        private readonly Volume _clearWeather;
        private readonly ColorAdjustments _party;
        private readonly ColorAdjustments _gameBrightness;

        public PartyGrading(Transform parent)
        {
            _clearWeatherProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            var neutral = _clearWeatherProfile.Add<ColorAdjustments>();
            neutral.saturation.Override(0f);
            neutral.contrast.Override(0f);
            neutral.colorFilter.Override(Color.white);
            _clearWeatherProfile.Add<WhiteBalance>(true);
            _clearWeatherProfile.Add<ShadowsMidtonesHighlights>(true);
            _clearWeather = CreateVolume(parent, "PartyTime Clear Weather", ClearWeatherPriority, _clearWeatherProfile);
            _clearWeather.weight = 0f;

            _partyProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _party = _partyProfile.Add<ColorAdjustments>();
            _party.postExposure.overrideState = true;
            CreateVolume(parent, "PartyTime Lights", PartyPriority, _partyProfile);
            _gameBrightness = FindGameBrightness();
        }

        public void Apply(float amount, float exposureOffset, Color filter, float filterStrength)
        {
            _clearWeather.weight = amount;
            float gameExposure = _gameBrightness != null && _gameBrightness.postExposure.overrideState ? _gameBrightness.postExposure.value : 0f;
            _party.postExposure.value = gameExposure + exposureOffset;
            _party.colorFilter.overrideState = filterStrength > MinFilterStrength;
            _party.colorFilter.value = Color.Lerp(Color.white, filter, filterStrength);
        }

        public void Destroy()
        {
            Object.Destroy(_clearWeatherProfile);
            Object.Destroy(_partyProfile);
        }

        private static Volume CreateVolume(Transform parent, string name, float priority, VolumeProfile profile)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = priority;
            volume.sharedProfile = profile;
            return volume;
        }

        private static ColorAdjustments FindGameBrightness()
        {
            foreach (var listener in Object.FindObjectsByType<VolumeSettingListener>(FindObjectsSortMode.None))
            {
                var volume = listener.GetComponent<Volume>();
                if (volume == null) continue;
                var profile = volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile;
                if (profile != null && profile.TryGet<ColorAdjustments>(out var grading)) return grading;
            }
            return null;
        }
    }
}
