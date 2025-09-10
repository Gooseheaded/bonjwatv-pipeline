
using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace bwkt_webapp.Tests
{
    /// <summary>
    /// Custom WebApplicationFactory that uses a test-specific content root (TestData folder).
    /// </summary>
    public class TestWebAppFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var solutionDir = FindSolutionDirectory();
            var projectDir = Path.Combine(solutionDir, "webapp");
            builder.UseContentRoot(projectDir);
        }

        private static string FindSolutionDirectory()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !dir.GetFiles("*.sln").Any())
            {
                dir = dir.Parent;
            }
            return dir?.FullName ?? throw new InvalidOperationException("Solution directory not found.");
        }
    }
}

