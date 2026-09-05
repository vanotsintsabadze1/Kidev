using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Kidev.Core;
using Kidev.Dashboard.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kidev.Dashboard.Tests;

/// <summary>Verifies security and storage-only startup without an available database.</summary>
public sealed class DashboardHostTests
{
    /// <summary>Verifies registration alone does not choose storage, Identity, or background workers.</summary>
    [Fact]
    public void PluginRegistrationLeavesInfrastructureToHost()
    {
        var services = new ServiceCollection();
        services.AddKidevDashboard();
        services.Should().NotContain(service => service.ServiceType == typeof(IDashboardQuery) ||
            service.ServiceType == typeof(IJobDefinitionStore) || service.ServiceType == typeof(DashboardIdentityContext) ||
            service.ImplementationType != null && service.ImplementationType.Name == "KidevRunner");
    }

    /// <summary>Verifies dashboard routes do not capture host routes or escape their area.</summary>
    [Theory]
    [InlineData("/", HttpStatusCode.OK)]
    [InlineData("/Home/Index", HttpStatusCode.OK)]
    [InlineData("/Dashboard/Jobs", HttpStatusCode.NotFound)]
    [InlineData("/Account/Login", HttpStatusCode.NotFound)]
    [InlineData("/kidev/Home/Index", HttpStatusCode.NotFound)]
    public async Task HostRoutesRemainIsolatedAsync(string path, HttpStatusCode expected)
    {
        await using var factory = new DashboardFactory();
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.Should().Be(expected);
        if (expected == HttpStatusCode.OK)
        {
            (await response.Content.ReadAsStringAsync()).Should().Be("Host home");
            response.Headers.CacheControl.Should().BeNull();
        }
    }

    /// <summary>Verifies nested prefixes and trailing slashes produce area-aware forms and packaged asset URLs.</summary>
    [Theory]
    [InlineData("/operations/jobs")]
    [InlineData("/operations/jobs/")]
    public async Task CustomPrefixRendersCompiledLoginAsync(string prefix)
    {
        await using var factory = new DashboardFactory();
        await using WebApplicationFactory<DashboardTestHost> configured = factory.WithWebHostBuilder(builder => builder.UseSetting("DashboardPrefix", prefix));
        using HttpClient client = configured.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri("/operations/jobs/Account/Login", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("action=\"/operations/jobs/Account/Login\"")
            .And.Contain("/_content/Kidev.Dashboard/css/dashboard.css");
    }

    /// <summary>Rejects route templates and unsafe prefix syntax rather than broadening route matching.</summary>
    [Theory]
    [InlineData("relative")]
    [InlineData("//host")]
    [InlineData("/../jobs")]
    [InlineData("/{controller}")]
    [InlineData("/jobs?x=1")]
    public async Task InvalidPrefixesAreRejectedAsync(string prefix)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddKidevDashboard();
        await using WebApplication app = builder.Build();
        Action map = () => app.MapKidevDashboard(prefix);
        map.Should().Throw<ArgumentException>();
    }

    /// <summary>Verifies every job page and account creation require authentication.</summary>
    [Theory]
    [InlineData("/kidev")]
    [InlineData("/kidev/Dashboard/Jobs")]
    [InlineData("/kidev/Dashboard/Details/1")]
    [InlineData("/kidev/Dashboard/Recurring")]
    [InlineData("/kidev/Dashboard/Scheduled")]
    [InlineData("/kidev/Dashboard/Workers")]
    [InlineData("/kidev/Dashboard/Failed")]
    [InlineData("/kidev/Dashboard/Metrics")]
    [InlineData("/kidev/Dashboard/Settings")]
    [InlineData("/kidev/Account/Register")]
    public async Task AnonymousRequestsRedirectToLoginAsync(string path)
    {
        await using var factory = new DashboardFactory();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.AbsolutePath.Should().Be("/kidev/Account/Login");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    /// <summary>Verifies anonymous account creation is denied even on POST.</summary>
    [Fact]
    public async Task AnonymousRegistrationPostIsDeniedAsync()
    {
        await using var factory = new DashboardFactory();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var content = new FormUrlEncodedContent([]);
        using HttpResponseMessage response = await client.PostAsync(new Uri("/kidev/Account/Register", UriKind.Relative), content);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    /// <summary>Verifies the public login POST requires an antiforgery token before touching Identity.</summary>
    [Fact]
    public async Task LoginPostRequiresAntiforgeryAsync()
    {
        await using var factory = new DashboardFactory();
        using HttpClient client = factory.CreateClient();
        using var content = new FormUrlEncodedContent([]);
        using HttpResponseMessage response = await client.PostAsync(new Uri("/kidev/Account/Login", UriKind.Relative), content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Verifies no worker or write store is registered and startup never opens PostgreSQL.</summary>
    [Fact]
    public async Task StartupIsStorageOnlyAsync()
    {
        await using var factory = new DashboardFactory();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IDashboardQuery>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IJobDefinitionStore>().Should().BeNull();
        factory.Services.GetServices<IHostedService>().Should().NotContain(service => service.GetType().Name == "KidevRunner");
        IdentityOptions options = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        options.Password.RequiredLength.Should().Be(12);
        options.Lockout.MaxFailedAccessAttempts.Should().Be(5);
        DashboardIdentityContext identity = scope.ServiceProvider.GetRequiredService<DashboardIdentityContext>();
        identity.Model.GetDefaultSchema().Should().Be("kidev_dashboard");
        identity.Model.GetEntityTypes().Should().NotContain(entity => entity.ClrType.Namespace == "Kidev.Core.Data");
    }

    /// <summary>Verifies authenticated nonadministrators still cannot access dashboard data.</summary>
    [Fact]
    public async Task PolicyRequiresAdministratorRoleAsync()
    {
        await using var factory = new DashboardFactory();
        IAuthorizationService authorization = factory.Services.GetRequiredService<IAuthorizationService>();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "test")], "test");
        AuthorizationResult denied = await authorization.AuthorizeAsync(new ClaimsPrincipal(identity), resource: null, "dashboard-access");
        denied.Succeeded.Should().BeFalse();
        identity.AddClaim(new Claim(ClaimTypes.Role, "Administrator"));
        AuthorizationResult allowed = await authorization.AuthorizeAsync(new ClaimsPrincipal(identity), resource: null, "dashboard-access");
        allowed.Succeeded.Should().BeTrue();
    }

    /// <summary>Verifies production cookies cannot be sent over HTTP.</summary>
    [Fact]
    public async Task ProductionCookiesAreSecureAsync()
    {
        await using var factory = new DashboardFactory("Production");
        CookieAuthenticationOptions options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        options.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
        options.Cookie.HttpOnly.Should().BeTrue();
    }

    private sealed class DashboardFactory(string environment = "Development") : DashboardTestHost
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("KIDEV_POSTGRES_CONNECTION_STRING", "Host=127.0.0.1;Port=1;Database=unavailable;Timeout=1");
        }
    }
}
