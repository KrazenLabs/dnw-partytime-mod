using System;
using System.Collections.Generic;
using System.Globalization;
using DnWModLoader;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PartyTime
{
    public sealed class PartyTimeMod : Mod
    {
        private const float PartyFadeInSeconds = 1.5f;
        private const float PartyFadeOutSeconds = 1f;
        private const float PartyLingerSeconds = 5f;
        private const float ButtonsDelaySeconds = 1f;

        internal static PartyTimeMod Instance { get; private set; }

        internal PartyTimeSettings Settings { get; private set; }

        internal JukeboxPlayer Player { get; private set; }

        private readonly HashSet<string> _reportedErrors = new HashSet<string>();
        private readonly MusicAnalyzer _analyzer = new MusicAnalyzer();
        private PartyShow _show;
        private JukeboxButtons _buttons;
        private float _buttonsDueAt = float.PositiveInfinity;
        private bool _partyOn;
        private float _partyAmount;
        private float _partyIdleSeconds;
        private AudioResource _loggedSong;
        private bool _loggedTempoLock;
        private ResourceFolder _songs;

        public override void OnInitialize()
        {
            Instance = this;
            Settings = new PartyTimeSettings(Config);
            _songs = GetResourceFolder("Songs");
            if (_songs == null) Logger.Warning("The Songs folder is not accessible.");
            var library = new MusicLibrary(_songs);
            library.MoveLooseSongs(Directory, Logger);
            Player = new JukeboxPlayer(Settings, library, Logger);
            Logger.Info("Initialized.");
        }

        public override void OnResourcesChanged(ResourceFolder folder, ResourceChanges changes)
        {
            if (folder == _songs) Safely(() => Player.SongsChanged(changes), "Song list");
        }

        public override void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            StopShow();
            _partyAmount = 0f;
            _buttons?.Destroy();
            _buttons = null;
            _buttonsDueAt = Time.unscaledTime + ButtonsDelaySeconds;
            Player.ForgetScene();
        }

        public override void OnUpdate()
        {
            try
            {
                Player.Update();
            }
            catch (Exception e)
            {
                Report(e, "Jukebox");
            }
            if (Time.unscaledTime < _buttonsDueAt) return;
            _buttonsDueAt = float.PositiveInfinity;
            try
            {
                CreateButtons();
            }
            catch (Exception e)
            {
                Report(e, "Jukebox buttons");
            }
        }

        public override void OnLateUpdate()
        {
            float deltaTime = Time.unscaledDeltaTime;
            try
            {
                UpdateParty(deltaTime);
            }
            catch (Exception e)
            {
                Report(e, "Party");
            }
            try
            {
                _buttons?.Update(deltaTime, _partyOn, Player.JukeboxSongPlaying, _show != null ? _analyzer.Pulse : 0f);
            }
            catch (Exception e)
            {
                Report(e, "Jukebox buttons");
            }
        }

        internal void Report(Exception e, string what)
        {
            if (_reportedErrors.Add(what + ": " + e.GetType().FullName)) Logger.Exception(e, what + " failed");
        }

        private void CreateButtons()
        {
            var jukebox = Object.FindFirstObjectByType<InteractableJukebox>();
            if (jukebox == null) return;
            _buttons = JukeboxButtons.Create(jukebox, () => Safely(TogglePartyMode, "Party button"), () => Safely(Player.SkipSong, "Skip button"));
            if (_buttons == null) Logger.Warning("Unable to place party buttons.");
        }

        private void TogglePartyMode()
        {
            _partyOn = !_partyOn;
            Logger.Info(_partyOn ? "Party mode on." : "Party mode off.");
            if (_partyOn) Player.StartJukeboxMusic();
        }

        private void Safely(Action action, string what)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Report(e, what);
            }
        }

        private void UpdateParty(float deltaTime)
        {
            var source = Player.Source;
            float target = _partyOn && Player.JukeboxSongPlaying ? Mathf.Clamp01(Player.GameVolume / JukeboxPlayer.GameMusicVolume) : 0f;
            _partyAmount = Mathf.MoveTowards(_partyAmount, target, deltaTime / (target > _partyAmount ? PartyFadeInSeconds : PartyFadeOutSeconds));

            if (_partyAmount <= 0f && target <= 0f)
            {
                _partyIdleSeconds += deltaTime;
                if (_partyIdleSeconds >= PartyLingerSeconds) StopShow();
                if (_show == null) return;
            }
            else
            {
                _partyIdleSeconds = 0f;
            }

            _analyzer.Update(source, deltaTime);
            LogTempo(source);
            if (_show == null)
            {
                _show = new PartyShow(PartyStage.Find(source));
                Logger.Debug("Party started.");
            }
            _show.Update(deltaTime, _analyzer, _partyAmount);
        }

        private void StopShow()
        {
            if (_show == null) return;
            _show.Destroy();
            _show = null;
            _analyzer.Reset();
            _loggedTempoLock = false;
            Logger.Debug("Party stopped.");
        }

        private void LogTempo(AudioSource source)
        {
            if (source == null || !source.isPlaying) return;
            var song = source.resource;
            if (song != _loggedSong)
            {
                _loggedSong = song;
                _loggedTempoLock = false;
            }
            if (_analyzer.TempoLocked == _loggedTempoLock) return;
            _loggedTempoLock = _analyzer.TempoLocked;
            string name = song != null ? song.name : "the song";
            if (_loggedTempoLock) Logger.Debug(string.Format(CultureInfo.InvariantCulture, "Music beat {0}: {1:0.#} BPM (confidence {2:0.00}).", name, _analyzer.Bpm, _analyzer.TempoConfidence));
            else Logger.Debug("Party lost the beat of " + name + ", falling back to accent flashes.");
        }
    }
}
