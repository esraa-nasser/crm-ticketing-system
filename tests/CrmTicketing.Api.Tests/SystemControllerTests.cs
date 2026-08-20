using CrmTicketing.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;

namespace CrmTicketing.Api.Tests;

public sealed class SystemControllerTests
{
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "CrmTicketing.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void GetInfo_ReturnsEnvironmentAndClockFromDependencies()
    {
        var now = new DateTimeOffset(2026, 8, 20, 9, 30, 0, TimeSpan.Zero);
        var controller = new SystemController(
            new FakeHostEnvironment { EnvironmentName = "Staging" },
            new FixedTimeProvider(now));

        var result = Assert.IsType<OkObjectResult>(controller.GetInfo().Result);
        var info = Assert.IsType<Shared.Contracts.ApiInfoResponse>(result.Value);

        Assert.Equal("CrmTicketing.Api", info.Name);
        Assert.Equal("Staging", info.Environment);
        Assert.Equal(now, info.ServerTimeUtc);
        Assert.False(string.IsNullOrWhiteSpace(info.Version));
    }
}
