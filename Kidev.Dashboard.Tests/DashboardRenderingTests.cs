using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Kidev.Core;
using Kidev.Core.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kidev.Dashboard.Tests;

/// <summary>Verifies rendered dashboard pagination, observed lease status, and definition counts.</summary>
public sealed class DashboardRenderingTests
{
    /// <summary>Verifies decorative icons retain labels and use only packaged, path-base-aware resources.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("/host")]
    public async Task IconsUseLocalSpriteAndAccessibleLabelsAsync(string pathBase)
    {
        var snapshot = new DashboardSnapshot(0, 0, 0, 0, 0, 0, [], [], DateTimeOffset.UtcNow, false, false);
        await using var factory = new RenderingFactory(new StubDashboardQuery(snapshot), pathBase);
        using HttpClient client = factory.CreateClient();
        string html = await client.GetStringAsync(new Uri($"{pathBase}/kidev", UriKind.Relative));
        string spritePath = $"{pathBase}/_content/Kidev.Dashboard/icons/lucide.svg";
        (string Label, string Icon)[] navigation =
        [
            ("Overview", "layout-dashboard"), ("Visualization", "server"), ("Jobs", "list-checks"), ("Recurring", "repeat"),
            ("Scheduled", "calendar-clock"), ("Workers", "server"), ("Failed", "circle-x"),
            ("Metrics", "chart-no-axes-combined"), ("Settings", "settings")
        ];
        MatchCollection links = Regex.Matches(html, "<a[^>]*class=\"nav-link[^\"]*\"[^>]*>.*?</a>", RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        links.Should().HaveCount(navigation.Length);
        for (int index = 0; index < navigation.Length; index++)
        {
            links[index].Value.Should().Contain($"href=\"{spritePath}#{navigation[index].Icon}\"")
                .And.Contain($"<span>{navigation[index].Label}</span>");
        }

        (string Attribute, string Label, string Icon)[] controls =
        [
            ("data-theme-toggle", "Light theme", "sun-moon"), ("data-nav-toggle", "Menu", "menu"),
            ("data-refresh", "Refresh", "refresh-cw"), ("type=\"submit\"", "Sign out", "log-out")
        ];
        foreach ((string attribute, string label, string icon) in controls)
        {
            string button = Regex.Match(html, $"<button[^>]* {attribute}(?:[ >])[^<]*.*?</button>", RegexOptions.Singleline, TimeSpan.FromSeconds(1)).Value;
            button.Should().Contain($"href=\"{spritePath}#{icon}\"").And.Contain($">{label}</span>");
        }

        MatchCollection icons = Regex.Matches(html, "<svg class=\"icon\"[^>]*>", RegexOptions.None, TimeSpan.FromSeconds(1));
        icons.Should().HaveCount(13);
        foreach (Match icon in icons)
        {
            icon.Value.Should().Contain("aria-hidden=\"true\"").And.Contain("focusable=\"false\"")
                .And.Contain("width=\"16\" height=\"16\"").And.Contain("stroke=\"currentColor\" stroke-width=\"2\"");
        }

        foreach (Match resource in Regex.Matches(html, "<(?:script|link|use)\\b[^>]*(?:src|href)=\"(?<url>[^\"]+)\"", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1)))
        {
            resource.Groups["url"].Value.Should().StartWith($"{pathBase}/_content/Kidev.Dashboard/");
        }

        string sprite = await client.GetStringAsync(new Uri(spritePath, UriKind.Relative));
        var document = XDocument.Parse(sprite);
        XNamespace svg = "http://www.w3.org/2000/svg";
        document.Root!.Elements(svg + "symbol").Select(symbol => (string?)symbol.Attribute("id"))
            .Should().BeEquivalentTo(navigation.Select(item => item.Icon).Concat(controls.Select(item => item.Icon)).Distinct(StringComparer.Ordinal));
        document.Descendants().Select(element => element.Name.LocalName).Should().NotIntersectWith(["script", "image", "use", "foreignObject"]);
        string license = await client.GetStringAsync(new Uri($"{pathBase}/_content/Kidev.Dashboard/icons/LICENSE.txt", UriKind.Relative));
        license.Should().Contain("ISC License").And.Contain("The MIT License (MIT)").And.Contain("0.468.0");
        string script = await client.GetStringAsync(new Uri($"{pathBase}/_content/Kidev.Dashboard/js/dashboard.js", UriKind.Relative));
        script.Should().NotContain("https://").And.NotContain("http://");
        foreach (string button in new[] { "button", "navButton", "refreshButton" })
        {
            script.Should().Contain($"{button}.querySelector(\"[data-button-label]\").textContent")
                .And.NotContain($"{button}.textContent =");
        }
    }

    /// <summary>Verifies all theme controls expose a separate label for icon-preserving updates.</summary>
    [Theory]
    [InlineData("/kidev/Account/Login", 1)]
    [InlineData("/kidev/Dashboard/Settings", 2)]
    public async Task ThemeControlsKeepIconSeparateFromLabelAsync(string path, int expectedCount)
    {
        var snapshot = new DashboardSnapshot(0, 0, 0, 0, 0, 0, [], [], DateTimeOffset.UtcNow, false, false);
        await using var factory = new RenderingFactory(new StubDashboardQuery(snapshot));
        using HttpClient client = factory.CreateClient();
        string html = await client.GetStringAsync(new Uri(path, UriKind.Relative));

        MatchCollection buttons = Regex.Matches(html, "<button[^>]*data-theme-toggle[^>]*>.*?</button>", RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        buttons.Should().HaveCount(expectedCount);
        foreach (Match button in buttons)
        {
            button.Value.Should().Contain("lucide.svg#sun-moon").And.Contain("aria-hidden=\"true\"")
                .And.Contain("<span data-button-label>Light theme</span>");
        }
    }

    /// <summary>Verifies each route pages only the result lists it displays.</summary>
    [Theory]
    [InlineData("Recurring", false, true, false)]
    [InlineData("Recurring", true, false, true)]
    [InlineData("Recurring", false, false, false)]
    [InlineData("Recurring", true, true, true)]
    [InlineData("Scheduled", false, true, false)]
    [InlineData("Scheduled", true, false, true)]
    [InlineData("Scheduled", false, false, false)]
    [InlineData("Scheduled", true, true, true)]
    [InlineData("Workers", true, false, false)]
    [InlineData("Workers", false, true, true)]
    [InlineData("Workers", false, false, false)]
    [InlineData("Workers", true, true, true)]
    [InlineData("Failed", true, false, false)]
    [InlineData("Failed", false, true, true)]
    [InlineData("Failed", false, false, false)]
    [InlineData("Failed", true, true, true)]
    [InlineData("Jobs", true, false, true)]
    [InlineData("Jobs", false, true, true)]
    [InlineData("Jobs", false, false, false)]
    [InlineData("Jobs", true, true, true)]
    [InlineData("recurring", false, true, false)]
    [InlineData("scheduled", false, true, false)]
    [InlineData("workers", true, false, false)]
    [InlineData("failed", true, false, false)]
    public async Task PaginationUsesVisibleResultsAsync(string action, bool hasNextJobPage, bool hasNextExecutionPage, bool expectedNext)
    {
        var snapshot = new DashboardSnapshot(0, 0, 0, 0, 0, 0, [], [], DateTimeOffset.UtcNow,
            hasNextJobPage, hasNextExecutionPage);
        await using var factory = new RenderingFactory(new StubDashboardQuery(snapshot));
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri($"/kidev/Dashboard/{action}?page=2", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string html = await response.Content.ReadAsStringAsync();
        html.Contains("rel=\"next\"", StringComparison.Ordinal).Should().Be(expectedNext);
        html.Should().Contain("rel=\"prev\"");
    }

    /// <summary>Verifies lease expiry uses the snapshot time, including equality, without changing recorded outcomes.</summary>
    [Theory]
    [InlineData(JobExecutionStatus.Running, -1, "expired")]
    [InlineData(JobExecutionStatus.Running, 0, "expired")]
    [InlineData(JobExecutionStatus.Running, 1, "running")]
    [InlineData(JobExecutionStatus.Failed, -1, "failed")]
    [InlineData(JobExecutionStatus.Succeeded, -1, "succeeded")]
    [InlineData(JobExecutionStatus.LeaseExpired, 1, "expired")]
    public async Task ExecutionHistoryUsesObservedLeaseStatusAsync(JobExecutionStatus status, int leaseOffsetSeconds, string tone)
    {
        var observedAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var execution = new JobExecution { Status = status, LeaseExpiresAtUtc = observedAt.AddSeconds(leaseOffsetSeconds) };
        var snapshot = new DashboardSnapshot(0, 0, 0, 0, 0, 0, [], [execution], observedAt, false, false);
        await using var factory = new RenderingFactory(new StubDashboardQuery(snapshot));
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/kidev/Dashboard/Jobs?status=Expired", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string html = await response.Content.ReadAsStringAsync();
        html.Should().Contain($"class=\"status status-{tone}\"");
        execution.Status.Should().Be(status);
    }

    /// <summary>Verifies the latest timeline and history agree on current lease status without mutating attempts.</summary>
    [Theory]
    [InlineData(JobExecutionStatus.Running, -1, "expired")]
    [InlineData(JobExecutionStatus.Running, 1, "running")]
    [InlineData(JobExecutionStatus.Failed, -1, "failed")]
    [InlineData(JobExecutionStatus.Succeeded, -1, "succeeded")]
    public async Task DetailsUsesCurrentLeaseStatusAsync(JobExecutionStatus status, int leaseOffsetHours, string tone)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var execution = new JobExecution { Status = status, LeaseExpiresAtUtc = now.AddHours(leaseOffsetHours) };
        var snapshot = new DashboardSnapshot(0, 0, 0, 0, 0, 0, [], [], now, false, false);
        var details = new DashboardJobDetails(new JobDefinition { Id = 1 }, [execution]);
        await using var factory = new RenderingFactory(new StubDashboardQuery(snapshot, details));
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/kidev/Dashboard/Details/1", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string html = await response.Content.ReadAsStringAsync();
        html.Split($"class=\"status status-{tone}\"", StringSplitOptions.None).Should().HaveCount(3);
        execution.Status.Should().Be(status);
    }

    /// <summary>Verifies the upstream count represents all definitions rather than future enabled schedules.</summary>
    [Fact]
    public async Task ProcessingFloorLinksRegisteredDefinitionsAsync()
    {
        var snapshot = new DashboardSnapshot(7, 0, 0, 0, 0, 0, [], [], DateTimeOffset.UtcNow, false, false);
        await using var factory = new RenderingFactory(new StubDashboardQuery(snapshot));
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/kidev", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string html = await response.Content.ReadAsStringAsync();
        int stationStart = html.IndexOf("<a class=\"floor-station", StringComparison.Ordinal);
        stationStart.Should().BeGreaterThanOrEqualTo(0);
        string station = html[stationStart..html.IndexOf("</a>", stationStart, StringComparison.Ordinal)];
        station.Should().Contain("href=\"/kidev/Dashboard/Recurring\"").And.Contain("<h3>Registered</h3>")
            .And.Contain("class=\"station-count\">7</strong>").And.NotContain("<h3>Scheduled</h3>");
    }

    private sealed class RenderingFactory(IDashboardQuery query, string pathBase = "") : DashboardTestHost
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting(WebHostDefaults.StaticWebAssetsKey, Path.Combine(AppContext.BaseDirectory, "Kidev.Dashboard.staticwebassets.runtime.json"));
            builder.UseStaticWebAssets();
            builder.UseSetting("KIDEV_POSTGRES_CONNECTION_STRING", "Host=127.0.0.1;Port=1;Database=unavailable;Timeout=1");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(query);
                services.AddSingleton<IStartupFilter>(new RenderingAssetsFilter(pathBase));
                // Security is covered by host tests; these requests exercise MVC rendering without Identity storage.
                services.AddAuthorizationBuilder().AddPolicy("dashboard-access", policy => policy.RequireAssertion(_ => true));
            });
        }
    }

    private sealed class RenderingAssetsFilter(string pathBase) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            if (pathBase.Length > 0)
            {
                app.UsePathBase(pathBase);
            }

            // The RCL's own build manifest is rooted at wwwroot; emulate a consuming host's asset prefix.
            app.UseStaticFiles(new StaticFileOptions { RequestPath = "/_content/Kidev.Dashboard" });
            next(app);
        };
    }

    private sealed class StubDashboardQuery(DashboardSnapshot snapshot, DashboardJobDetails? details = null) : IDashboardQuery
    {
        public Task<FactorySnapshot> ReadFactoryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new FactorySnapshot(snapshot.ObservedAtUtc, snapshot.ObservedAtUtc.AddMinutes(-2), false, [], [], [], []));

        public Task<DashboardSnapshot> ReadAsync(string? search, string? status, int page, CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);

        public Task<DashboardJobDetails?> FindAsync(int id, CancellationToken cancellationToken) => Task.FromResult(details);
    }
}
