using System;
using System.Collections.Generic;
using DnWModLoader;
using DnWModLoader.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace PartyTime
{
    internal sealed class JukeboxPlayer
    {
        public const float GameMusicVolume = 0.5f;
        public const float GameIdleWaitSeconds = 60f;
        private const float PendingStartSeconds = 5f;
        private const float RetireSeconds = 10f;
        private const float RescanSeconds = 1f;
        private const float SwitchNowSeconds = 0.0001f;
        private const float SongChangeFadeSeconds = 0.5f;

        private static readonly AccessTools.FieldRef<MusicManager, AudioResource[]> ProductiveMusic = AccessTools.FieldRefAccess<MusicManager, AudioResource[]>("productiveMusic");

        private sealed class Song
        {
            public string Path;
            public AudioResource Resource;
        }

        private readonly PartyTimeSettings _settings;
        private readonly MusicLibrary _library;
        private readonly ModLogger _logger;
        private readonly List<Song> _queue = new List<Song>();
        private readonly HashSet<AudioClip> _clips = new HashSet<AudioClip>();
        private readonly List<KeyValuePair<AudioClip, float>> _retired = new List<KeyValuePair<AudioClip, float>>();
        private readonly System.Random _random = new System.Random();

        private MusicManager _manager;
        private InteractableJukebox _jukebox;
        private Song _next;
        private AudioResource _current;
        private string _currentPath;
        private bool _currentHeard;
        private float _pendingSince = -1f;
        private float _lastScan = float.NegativeInfinity;
        private bool _quietForDialogue;

        public JukeboxPlayer(PartyTimeSettings settings, MusicLibrary library, ModLogger logger)
        {
            _settings = settings;
            _library = library;
            _logger = logger;
        }

        public AudioSource Source { get; private set; }

        public float GameVolume { get; private set; }

        public bool JukeboxSongPlaying
        {
            get { return Source != null && Source.isPlaying && IsJukeboxSong(Source.resource); }
        }

        public bool IsJukeboxSong(AudioResource resource)
        {
            if (resource == null) return false;
            if (resource is AudioClip clip && _clips.Contains(clip)) return true;
            return _manager != null && Array.IndexOf(ProductiveMusic(_manager), resource) >= 0;
        }

        public void ForgetScene()
        {
            _manager = null;
            _jukebox = null;
            Source = null;
            GameVolume = 0f;
            _pendingSince = -1f;
            _quietForDialogue = false;
            Retire(_current);
            _current = null;
            _currentPath = null;
            _currentHeard = false;
            _queue.Clear();
            if (_next != null && _next.Path == null) _next = null;
        }

        public void Update()
        {
            if (_library.IsLoading && _library.TryFinishLoad(out string path, out var clip, out string error))
            {
                bool wanted = _next != null && _next.Resource == null && _next.Path == path;
                if (clip == null)
                {
                    _logger.Warning("Couldn't load " + path + " (" + error + "). Only .ogg files are supported.");
                    if (wanted) _next = null;
                }
                else if (wanted)
                {
                    _clips.Add(clip);
                    _next.Resource = clip;
                }
                else
                {
                    Object.Destroy(clip);
                }
            }
            if (_manager == null) return;

            PrepareNext(_pendingSince >= 0f && Time.unscaledTime - _lastScan >= RescanSeconds);
            if (_pendingSince >= 0f)
            {
                if (Time.unscaledTime - _pendingSince > PendingStartSeconds) _pendingSince = -1f;
                else if (_next != null && _next.Resource != null && Source != null && !Source.isPlaying && !AudioPaused && MusicManager.GetMusicEnabled())
                {
                    _pendingSince = -1f;
                    StartNextSong();
                }
            }
            DestroyRetired();
        }

        public bool TryPick(MusicManager manager, out AudioResource song)
        {
            song = null;
            Attach(manager);
            if (_library.FindSongs().Count == 0)
            {
                _queue.Clear();
                return false;
            }
            PrepareNext(true);
            if (_next != null && _next.Resource != null)
            {
                song = _next.Resource;
                Retire(_current);
                _current = song;
                _currentPath = _next.Path;
                _currentHeard = false;
                _pendingSince = -1f;
                _next = null;
                PrepareNext(true);
                return true;
            }
            _pendingSince = Time.unscaledTime;
            return true;
        }

        public void AfterGameUpdate(MusicManager manager, AudioSource source)
        {
            Attach(manager);
            Source = source;
            GameVolume = source.volume;
            if (source.resource is AudioClip clip && _clips.Contains(clip)) source.volume = Mathf.Clamp01(source.volume * _settings.Volume.Value);

            if (_current == null || source.resource != _current) return;
            if (source.isPlaying)
            {
                if (!_currentHeard) _logger.Info("Now playing: " + _current.name);
                _currentHeard = true;
            }
            else if (_currentHeard && !AudioPaused && MusicManager.GetMusicEnabled())
            {
                _currentHeard = false;
                StartNextSong();
            }
        }

        private static bool AudioPaused
        {
            get { return AudioListener.pause || (!Application.runInBackground && !Application.isFocused); }
        }

        public void BeforeJukeboxPress()
        {
            if (Source != null && !Source.isPlaying && _pendingSince < 0f && MusicManager.GetMusicEnabled()) MusicManager.SetMusicEnabled(false);
        }

        public float FadeOutSeconds(AudioResource nextSong, float gameSeconds)
        {
            if (Source == null) return gameSeconds;
            if (!Source.isPlaying) return SwitchNowSeconds;
            if (IsJukeboxSong(nextSong)) return SongChangeFadeSeconds;
            return gameSeconds;
        }

        public void KeepQuietForDialogue()
        {
            _quietForDialogue = true;
        }

        public bool ShouldSkipIdleWait()
        {
            if (_pendingSince >= 0f || AudioPaused) return false;
            if (!_quietForDialogue) return true;
            if (DialogCommands.IsDialogueRunning) return false;
            _quietForDialogue = false;
            return true;
        }

        public void SkipSong()
        {
            if (_manager == null) return;
            if (JukeboxSongPlaying && MusicManager.GetMusicEnabled()) StartNextSong();
            else StartJukeboxMusic();
        }

        public void SongsChanged(ResourceChanges changes)
        {
            foreach (string path in changes.Removed) Drop(path);
            foreach (string path in changes.Changed)
                if (Drop(path)) Insert(path);
            if (_queue.Count == 0 && _next == null) return;
            foreach (string path in changes.Added) Insert(path);
        }

        private bool Drop(string path)
        {
            bool dropped = _queue.RemoveAll(song => SamePath(song.Path, path)) > 0;
            if (_next != null && SamePath(_next.Path, path))
            {
                _library.CancelLoad(path);
                Retire(_next.Resource);
                _next = null;
                dropped = true;
            }
            return dropped;
        }

        private void Insert(string path)
        {
            if (SamePath(_next?.Path, path) || _queue.Exists(song => SamePath(song.Path, path))) return;
            _queue.Insert(_random.Next(_queue.Count + 1), new Song { Path = path });
        }

        private static bool SamePath(string a, string b)
        {
            return a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public void StartJukeboxMusic()
        {
            if (_manager == null || JukeboxSongPlaying) return;
            if (MusicManager.GetMusicEnabled()) MusicManager.SetMusicEnabled(false);
            if (_jukebox == null) _jukebox = Object.FindFirstObjectByType<InteractableJukebox>();
            if (_jukebox != null) _jukebox.Interact(null);
            if (!MusicManager.GetMusicEnabled()) MusicManager.SetMusicEnabled(true);
        }

        private static void StartNextSong()
        {
            MusicManager.SetMusicEnabled(false);
            MusicManager.SetMusicEnabled(true);
        }

        private void Attach(MusicManager manager)
        {
            if (_manager == manager) return;
            _manager = manager;
            int count = _library.FindSongs().Count;
            if (count > 0)
            {
                _logger.Info("Found " + count + (count == 1 ? " song" : " songs") + " in " + _library.Folder + ".");
                PrepareNext(true);
            }
            else
            {
                _logger.Info("No songs in " + _library.Folder + " found.");
            }
            int others = _library.CountOtherAudioFiles();
            if (others > 0) _logger.Warning(others + (others == 1 ? " music file is not" : " music files are not") + " .ogg format.");
        }

        private void PrepareNext(bool mayScan)
        {
            if (_next == null)
            {
                if (_queue.Count == 0 && mayScan) Refill();
                if (_queue.Count == 0) return;
                _next = _queue[0];
                _queue.RemoveAt(0);
            }
            if (_next.Resource == null && !_library.IsLoading) _library.BeginLoad(_next.Path);
        }

        private void Refill()
        {
            _lastScan = Time.unscaledTime;
            var songs = new List<Song>();
            foreach (string path in _library.FindSongs()) songs.Add(new Song { Path = path });
            if (songs.Count == 0) return;
            if (_settings.IncludeGameMusic.Value && _manager != null)
            {
                foreach (var resource in ProductiveMusic(_manager))
                    if (resource != null) songs.Add(new Song { Resource = resource });
            }
            for (int i = songs.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                var swap = songs[i];
                songs[i] = songs[j];
                songs[j] = swap;
            }
            if (songs.Count > 1 && IsCurrent(songs[0]))
            {
                int j = 1 + _random.Next(songs.Count - 1);
                var swap = songs[0];
                songs[0] = songs[j];
                songs[j] = swap;
            }
            _queue.AddRange(songs);
        }

        private bool IsCurrent(Song song)
        {
            if (song.Path != null) return string.Equals(song.Path, _currentPath, StringComparison.OrdinalIgnoreCase);
            return song.Resource == _current;
        }

        private void Retire(AudioResource resource)
        {
            if (resource is AudioClip clip && _clips.Contains(clip)) _retired.Add(new KeyValuePair<AudioClip, float>(clip, Time.unscaledTime));
        }

        private void DestroyRetired()
        {
            for (int i = _retired.Count - 1; i >= 0; i--)
            {
                var clip = _retired[i].Key;
                if (Time.unscaledTime - _retired[i].Value < RetireSeconds) continue;
                if (clip == _current || (Source != null && Source.resource == clip) || (_next != null && _next.Resource == clip)) continue;
                _retired.RemoveAt(i);
                _clips.Remove(clip);
                Object.Destroy(clip);
            }
        }
    }
}
