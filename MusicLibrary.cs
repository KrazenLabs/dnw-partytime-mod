using System;
using System.Collections.Generic;
using System.IO;
using DnWModLoader;
using DnWModLoader.Logging;
using UnityEngine;
using UnityEngine.Networking;

namespace PartyTime
{
    internal sealed class MusicLibrary
    {
        private static readonly HashSet<string> OtherAudioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".wma", ".opus" };

        private readonly ResourceFolder _songs;
        private readonly HashSet<string> _broken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private UnityWebRequest _request;
        private string _loadingPath;

        public MusicLibrary(ResourceFolder songs)
        {
            _songs = songs;
        }

        public string Folder
        {
            get { return _songs != null ? _songs.Path : "the Songs folder"; }
        }

        public bool IsLoading
        {
            get { return _request != null; }
        }

        public List<string> FindSongs()
        {
            var songs = new List<string>();
            if (_songs == null) return songs;
            foreach (string file in _songs.Files)
                if (!_broken.Contains(BrokenKey(file))) songs.Add(file);
            return songs;
        }

        public int CountOtherAudioFiles()
        {
            if (_songs == null || !Directory.Exists(_songs.Path)) return 0;
            int count = 0;
            foreach (string file in Directory.GetFiles(_songs.Path, "*.*", SearchOption.AllDirectories))
                if (OtherAudioExtensions.Contains(Path.GetExtension(file))) count++;
            return count;
        }

        public int MoveLooseSongs(string modDirectory, ModLogger logger)
        {
            if (_songs == null || string.IsNullOrEmpty(modDirectory) || !Directory.Exists(modDirectory)) return 0;
            string root = modDirectory.TrimEnd('\\', '/');
            string songsPath = _songs.Path.TrimEnd('\\', '/');
            int moved = 0;
            var emptied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in LooseMusicFiles(root, songsPath))
            {
                string relative = file.Substring(root.Length + 1);
                string target = Path.Combine(songsPath, relative);
                if (File.Exists(target) || Directory.Exists(target))
                {
                    logger.Warning(relative + " already exists.");
                    continue;
                }
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Move(file, target);
                    moved++;
                    emptied.Add(Path.GetDirectoryName(file));
                }
                catch (Exception e)
                {
                    logger.Warning("Could not move " + relative + " into Songs folder: " + e.Message);
                }
            }
            foreach (string folder in emptied) RemoveEmptyFolders(folder, root);
            if (moved > 0) logger.Info("Moved " + moved + (moved == 1 ? " music file" : " music files") + " into " + songsPath + ".");
            return moved;
        }

        private static List<string> LooseMusicFiles(string root, string songsPath)
        {
            var files = new List<string>();
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                if (string.Equals(directory, songsPath, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var info = new DirectoryInfo(directory);
                    foreach (var file in info.GetFiles())
                    {
                        string extension = file.Extension;
                        bool music = string.Equals(extension, ".ogg", StringComparison.OrdinalIgnoreCase) || OtherAudioExtensions.Contains(extension);
                        if (music && IsVisible(file.Attributes)) files.Add(file.FullName);
                    }
                    foreach (var subdirectory in info.GetDirectories())
                        if (IsVisible(subdirectory.Attributes) && (subdirectory.Attributes & FileAttributes.ReparsePoint) == 0) pending.Push(subdirectory.FullName);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return files;
        }

        private static bool IsVisible(FileAttributes attributes)
        {
            return (attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;
        }

        private static void RemoveEmptyFolders(string folder, string root)
        {
            while (!string.IsNullOrEmpty(folder) && folder.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (Directory.GetFileSystemEntries(folder).Length > 0) return;
                    Directory.Delete(folder, false);
                }
                catch (Exception)
                {
                    return;
                }
                folder = Path.GetDirectoryName(folder);
            }
        }

        public void BeginLoad(string path)
        {
            Cancel();
            _request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.OGGVORBIS);
            ((DownloadHandlerAudioClip)_request.downloadHandler).streamAudio = true;
            _request.SendWebRequest();
            _loadingPath = path;
        }

        public bool TryFinishLoad(out string path, out AudioClip clip, out string error)
        {
            path = _loadingPath;
            clip = null;
            error = null;
            if (_request == null || !_request.isDone) return false;
            try
            {
                if (_request.result == UnityWebRequest.Result.Success) clip = DownloadHandlerAudioClip.GetContent(_request);
                if (clip == null) error = string.IsNullOrEmpty(_request.error) ? _request.result.ToString() : _request.error;
            }
            catch (Exception e)
            {
                error = e.Message;
            }
            if (clip != null) clip.name = Path.GetFileNameWithoutExtension(path);
            else _broken.Add(BrokenKey(path));
            Cancel();
            return true;
        }

        public void CancelLoad(string path)
        {
            if (_request != null && string.Equals(_loadingPath, path, StringComparison.OrdinalIgnoreCase)) Cancel();
        }

        public void Cancel()
        {
            _request?.Dispose();
            _request = null;
            _loadingPath = null;
        }

        private static string BrokenKey(string path)
        {
            try
            {
                return path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
            }
            catch (Exception)
            {
                return path;
            }
        }
    }
}
