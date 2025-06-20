using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace bwkt_webapp.Tests
{
    /// <summary>
    /// Custom WebApplicationFactory that uses a test-specific content root (TestData folder).
    /// </summary>
    public class TestWebAppFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Point the content root to the TestData folder copied to the test output
            var testDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");
            builder.UseContentRoot(testDataPath);
        }
    }
}