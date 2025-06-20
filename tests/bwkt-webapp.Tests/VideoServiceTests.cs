using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using bwkt_webapp.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace bwkt_webapp.Tests
{
    public class VideoServiceTests
    {
        private const string JsonContent =
            "[{\"v\":\"id1\",\"title\":\"Title1\",\"description\":\"Desc1\",\"subtitleUrl\":\"url1\"}," +
            "{\"v\":\"id2\",\"title\":\"Title2\",\"description\":null,\"subtitleUrl\":\"url2\"}]";

        private IWebHostEnvironment CreateEnvironmentWithJson(string json)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            var dataDir = Path.Combine(tempDir, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "videos.json"), json);

            return new TestEnv { ContentRootPath = tempDir };
        }

        private class TestEnv : IWebHostEnvironment
        {
            public string EnvironmentName { get; set; }
            public string ApplicationName { get; set; }
            public string ContentRootPath { get; set; }
            public IFileProvider ContentRootFileProvider { get; set; }
            public string WebRootPath { get; set; }
            public IFileProvider WebRootFileProvider { get; set; }
        }

        [Fact]
        public void GetAll_ReturnsAllEntries()
        {
            var env = CreateEnvironmentWithJson(JsonContent);
            var svc = new VideoService(env);
            var all = svc.GetAll().ToList();
            Assert.Equal(2, all.Count);
            Assert.Contains(all, v => v.VideoId == "id1");
            Assert.Contains(all, v => v.VideoId == "id2");
        }

        [Fact]
        public void GetById_FindsCorrectEntry()
        {
            var env = CreateEnvironmentWithJson(JsonContent);
            var svc = new VideoService(env);
            var v = svc.GetById("id2");
            Assert.NotNull(v);
            Assert.Equal("Title2", v.Title);
        }

        [Fact]
        public void GetById_ReturnsNullForMissing()
        {
            var env = CreateEnvironmentWithJson(JsonContent);
            var svc = new VideoService(env);
            Assert.Null(svc.GetById("missing"));
        }

        [Fact]
        public void Search_EmptyQuery_ReturnsAll()
        {
            var env = CreateEnvironmentWithJson(JsonContent);
            var svc = new VideoService(env);
            var all = svc.Search(string.Empty);
            Assert.Equal(2, all.Count());
        }

    [Fact]
    public void Search_MatchesTitle_CaseInsensitive()
    {
        var env = CreateEnvironmentWithJson(JsonContent);
        var svc = new VideoService(env);
        var result = svc.Search("title1");
        Assert.Single(result);
        Assert.Equal("id1", result.First().VideoId);
    }
    [Fact]
    public void Search_MatchesTagCode()
    {
        var tagJson =
            "[{\"v\":\"id1\",\"title\":\"Foo\",\"description\":null,\"subtitleUrl\":\"u\",\"tags\":[\"z\"]}," +
            "{\"v\":\"id2\",\"title\":\"Bar\",\"description\":null,\"subtitleUrl\":\"u\",\"tags\":[\"p\"]}]";
        var env = CreateEnvironmentWithJson(tagJson);
        var svc = new VideoService(env);
        var result = svc.Search("z");
        Assert.Single(result);
        Assert.Equal("id1", result.First().VideoId);
    }

    [Fact]
    public void Search_MatchesTagLabel()
    {
        var tagJson =
            "[{\"v\":\"id1\",\"title\":\"Foo\",\"description\":null,\"subtitleUrl\":\"u\",\"tags\":[\"t\"]}," +
            "{\"v\":\"id2\",\"title\":\"Bar\",\"description\":null,\"subtitleUrl\":\"u\",\"tags\":[\"p\"]}]";
        var env = CreateEnvironmentWithJson(tagJson);
        var svc = new VideoService(env);
        // 'Terran' is label for code 't'
        var result = svc.Search("Terran");
        Assert.Single(result);
        Assert.Equal("id1", result.First().VideoId);
    }

    [Fact]
    public void Search_MultiToken_ANDSemantics()
    {
        var tagJson =
            "[{\"v\":\"id1\",\"title\":\"Foo\",\"description\":null,\"subtitleUrl\":\"u\",\"tags\":[\"z\",\"p\"]}," +
            "{\"v\":\"id2\",\"title\":\"Foo\",\"description\":null,\"subtitleUrl\":\"u\",\"tags\":[\"z\"]}]";
        var env = CreateEnvironmentWithJson(tagJson);
        var svc = new VideoService(env);
        var result = svc.Search("z p");
        Assert.Single(result);
        Assert.Equal("id1", result.First().VideoId);
    }

        [Fact]
        public void Constructor_NoFile_FileCreatesEmptyList()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            var env = new TestEnv { ContentRootPath = tempDir };
            var svc = new VideoService(env);
            Assert.Empty(svc.GetAll());
        }

        [Fact]
        public void FileWatcher_ReloadsOnChange()
        {
            var env = CreateEnvironmentWithJson(JsonContent);
            var svc = new VideoService(env);
            var dataFile = Path.Combine(env.ContentRootPath, "data", "videos.json");
            var updatedJson = "[{\"v\":\"new\",\"title\":\"New\",\"description\":null,\"subtitleUrl\":\"url\"}]";
            File.WriteAllText(dataFile, updatedJson);
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(1) && svc.GetAll().First().VideoId != "new")
            {
                Thread.Sleep(50);
            }
            var all = svc.GetAll().ToList();
            Assert.Single(all);
            Assert.Equal("new", all.First().VideoId);
        }
    }

    [Fact]
    public void GetAll_JsonIncludesCreator()
    {
        const string json =
            "[{\"videoId\":\"id1\",\"title\":\"T\",\"description\":null,\"subtitleUrl\":\"u\",\"creator\":\"Alice\"}]";
        var env = CreateEnvironmentWithJson(json);
        var svc = new VideoService(env);
        var v = svc.GetAll().FirstOrDefault();
        Assert.NotNull(v);
        Assert.Equal("Alice", v.Creator);
    }
}