using UnityEngine;

namespace PartyTime
{
    internal sealed class PartyShow
    {
        internal static readonly Color[] Palette =
        {
            new Color(1f, 0.05f, 0.6f),
            new Color(0.1f, 0.85f, 1f),
            new Color(1f, 0.8f, 0.05f),
            new Color(0.55f, 0.1f, 1f),
            new Color(0.2f, 1f, 0.3f),
            new Color(1f, 0.35f, 0.05f),
            new Color(0.1f, 0.3f, 1f),
            new Color(1f, 0.1f, 0.15f),
        };

        private const float Darkness = 0.6f;
        private const float DarkeningStops = 1.6f;
        private const float BeatFlash = 0.6f;
        private const float FlashStops = 0.35f;
        private const float DimmedLightShare = 0.15f;
        private const float WashStrength = 0.225f;
        private const float WashChangeSeconds = 0.25f;
        private const int BeatsPerWashColor = 4;
        private const int PartyLightCount = 4;
        private const float PartyLightsBelowBall = 0.5f;

        private readonly GameObject _root;
        private readonly PartyGrading _grading;
        private readonly LevelLights _levelLights;
        private readonly DiscoBall _mirrorBall;
        private readonly PartyLights _partyLights;
        private readonly JukeboxNeon _neon;
        private Color _wash;
        private Color _washTarget;
        private int _washIndex;

        public PartyShow(PartyStage stage)
        {
            _root = new GameObject("PartyTime Party");
            _grading = new PartyGrading(_root.transform);
            _levelLights = new LevelLights(_root.transform);
            _mirrorBall = new DiscoBall(_root.transform, stage);
            _partyLights = new PartyLights(_root.transform, stage, PartyLightCount, stage.RigHeight - PartyLightsBelowBall);
            _neon = JukeboxNeon.Find();

            _washIndex = Random.Range(0, Palette.Length);
            _washTarget = Palette[_washIndex];
            _wash = Color.white;
        }

        public void Update(float deltaTime, MusicAnalyzer music, float amount)
        {
            float flash = music.Pulse * BeatFlash;
            if (music.Beat && music.BeatCount % BeatsPerWashColor == 0)
            {
                _washIndex = (_washIndex + 1) % Palette.Length;
                _washTarget = Palette[_washIndex];
            }
            _wash = Color.Lerp(_wash, _washTarget, 1f - Mathf.Exp(-deltaTime / WashChangeSeconds));

            float darkness = Darkness * amount;
            _grading.Apply(amount, -darkness * DarkeningStops + flash * FlashStops * amount, _wash, WashStrength * amount);
            _levelLights.Dim(Mathf.Lerp(1f, DimmedLightShare, darkness), deltaTime);
            _mirrorBall.Update(deltaTime, music, amount, flash, _wash);
            _partyLights.Update(deltaTime, music, amount, flash);
            _neon?.Apply(amount * (0.3f + 2f * flash + 0.5f * music.Bass));
        }

        public void Destroy()
        {
            _levelLights.Restore();
            _neon?.Restore();
            _mirrorBall.Destroy();
            _grading.Destroy();
            if (_root != null) Object.Destroy(_root);
        }
    }
}
