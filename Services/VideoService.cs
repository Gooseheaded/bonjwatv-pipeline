using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using bwkt_webapp.Models;
using Microsoft.AspNetCore.Hosting;

namespace bwkt_webapp.Services
{
    public class VideoService : IVideoService, IDisposable
    {
        private List<VideoInfo> _videos = new List<VideoInfo>();
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly FileSystemWatcher? _watcher;

        public VideoService(IWebHostEnvironment env)
        {
            var dataPath = Path.Combine(env.ContentRootPath, "data", "videos.json");
            _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            LoadData(dataPath);

            var dir = Path.GetDirectoryName(dataPath)!;
            if (Directory.Exists(dir))
            {
                _watcher = new FileSystemWatcher(dir, Path.GetFileName(dataPath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
                };
                _watcher.Changed += (_, __) => LoadData(dataPath);
                _watcher.Created += (_, __) => LoadData(dataPath);
                _watcher.Renamed += (_, __) => LoadData(dataPath);
                _watcher.EnableRaisingEvents = true;
            }
            else
            {
                _watcher = null;
            }
        }

        private void LoadData(string dataPath)
        {
            try
            {
                if (File.Exists(dataPath))
                {
                    var json = File.ReadAllText(dataPath);
                    var list = JsonSerializer.Deserialize<List<VideoInfo>>(json, _jsonOptions)
                               ?? new List<VideoInfo>();
                    _videos = list;
                }
                else
                {
                    _videos = new List<VideoInfo>();
                }
            }
            catch
            {
                // ignore malformed JSON or file access errors
                _videos = _videos ?? new List<VideoInfo>();
            }
        }

        public IEnumerable<VideoInfo> GetAll() => _videos;

        public VideoInfo? GetById(string videoId) =>
            _videos.FirstOrDefault(v => string.Equals(v.VideoId, videoId, StringComparison.OrdinalIgnoreCase));

        public IEnumerable<VideoInfo> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return _videos;

            return _videos.Where(v =>
                v.Title.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        public void Dispose()
        {
            _watcher?.Dispose();
        }
    }
}