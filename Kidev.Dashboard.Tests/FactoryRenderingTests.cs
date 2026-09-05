using System.Net;
using System.Text.Json;
using FluentAssertions;
using Kidev.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kidev.Dashboard.Tests;

/// <summary>Verifies the live Visualization shell, packaged assets, route isolation, and protected snapshots.</summary>
public sealed class FactoryRenderingTests
{
    /// <summary>Verifies both URLs and packaged assets retain custom prefixes and host path bases.</summary>
    [Theory]
    [InlineData("", "/kidev")]
    [InlineData("/host", "/operations/jobs")]
    public async Task VisualizationUsesAreaAwareRoutesAndPackagedAssetsAsync(string pathBase, string prefix)
    {
        await using var factory = new FactoryHost(pathBase, prefix, true);
        using HttpClient client = factory.CreateClient();
        string html = await client.GetStringAsync(new Uri($"{pathBase}{prefix}/Visualization", UriKind.Relative));

        html.Should().Contain("<h1>Visualization</h1>")
            .And.Contain("<title>Visualization | Kidev</title>")
            .And.Contain($"class=\"nav-link is-current\" aria-current=\"page\" href=\"{pathBase}{prefix}/Visualization\"")
            .And.Contain($"data-snapshot-url=\"{pathBase}{prefix}/Visualization/Snapshot\"")
            .And.Contain($"data-details-url=\"{pathBase}{prefix}/Dashboard/Details\"")
            .And.Contain("aria-current=\"page\"")
            .And.Contain("<span>Visualization</span>")
            .And.Contain("aria-label=\"Live visualization\"")
            .And.Contain("aria-label=\"Visualization schematic, scroll horizontally to explore\"")
            .And.Contain("aria-label=\"Visualization data table\"")
            .And.Contain("Visualization requires JavaScript")
            .And.Contain("data-factory-pause aria-pressed=\"false\"")
            .And.Contain("aria-live=\"polite\"")
            .And.Contain("<noscript>")
            .And.NotContain("data-auto-refresh");

        foreach (string asset in new[] { "css/factory.css", "js/factory.js" })
        {
            string assetPath = $"{pathBase}/_content/Kidev.Dashboard/{asset}";
            html.Should().Contain(assetPath);
            using HttpResponseMessage response = await client.GetAsync(new Uri(assetPath, UriKind.Relative));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            string content = await response.Content.ReadAsStringAsync();
            content.Should().NotBeNullOrWhiteSpace();
            if (asset.EndsWith(".js", StringComparison.Ordinal))
            {
                content.Should().NotContain("innerHTML").And.NotContain("location.reload")
                    .And.Contain("AbortController").And.Contain("visibilitychange").And.Contain("textContent");
            }
            else
            {
                content.Should().Contain("prefers-reduced-motion: no-preference").And.Contain("var(--surface)");
            }
        }

        string json = await client.GetStringAsync(new Uri($"{pathBase}{prefix}/Visualization/Snapshot", UriKind.Relative));
        using var snapshot = JsonDocument.Parse(json);
        snapshot.RootElement.GetProperty("processes").GetArrayLength().Should().Be(0);
        snapshot.RootElement.GetProperty("events").GetArrayLength().Should().Be(0);
        snapshot.RootElement.GetProperty("observedAtUtc").GetDateTimeOffset().Should().Be(FactoryQuery.ObservedAt);
    }

    /// <summary>Verifies anonymous requests cannot access either the shell or telemetry.</summary>
    [Theory]
    [InlineData("/kidev/Visualization")]
    [InlineData("/kidev/Visualization/Snapshot")]
    public async Task VisualizationRequiresAuthenticationAsync(string path)
    {
        await using var factory = new FactoryHost("", "/kidev", false);
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.AbsolutePath.Should().Be("/kidev/Account/Login");
        string body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("observedAtUtc").And.NotContain("data-factory");
    }

    /// <summary>Verifies the previous page routes are not retained as aliases.</summary>
    [Theory]
    [InlineData("/kidev/Factory")]
    [InlineData("/kidev/Factory/Snapshot")]
    public async Task PreviousFactoryRoutesAreNotMappedAsync(string path)
    {
        await using var factory = new FactoryHost("", "/kidev", true);
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed class FactoryHost(string pathBase, string prefix, bool authorize) : DashboardTestHost
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DashboardPrefix", prefix);
            builder.UseSetting(WebHostDefaults.StaticWebAssetsKey, Path.Combine(AppContext.BaseDirectory, "Kidev.Dashboard.staticwebassets.runtime.json"));
            builder.UseStaticWebAssets();
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDashboardQuery>(new FactoryQuery());
                services.AddSingleton<IStartupFilter>(new FactoryAssets(pathBase));
                if (authorize)
                {
                    services.AddAuthorizationBuilder().AddPolicy("dashboard-access", policy => policy.RequireAssertion(_ => true));
                }
            });
        }
    }

    private sealed class FactoryAssets(string pathBase) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            if (pathBase.Length > 0)
            {
                app.UsePathBase(pathBase);
            }

            app.UseStaticFiles(new StaticFileOptions { RequestPath = "/_content/Kidev.Dashboard" });
            next(app);
        };
    }

    private sealed class FactoryQuery : IDashboardQuery
    {
        internal static readonly DateTimeOffset ObservedAt = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

        public Task<FactorySnapshot> ReadFactoryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new FactorySnapshot(ObservedAt, ObservedAt.AddMinutes(-2), false, [], [], [], []));

        public Task<DashboardSnapshot> ReadAsync(string? search, string? status, int page, CancellationToken cancellationToken) =>
            Task.FromResult(new DashboardSnapshot(0, 0, 0, 0, 0, 0, [], [], ObservedAt, false, false));

        public Task<DashboardJobDetails?> FindAsync(int id, CancellationToken cancellationToken) => Task.FromResult<DashboardJobDetails?>(null);
    }
}
