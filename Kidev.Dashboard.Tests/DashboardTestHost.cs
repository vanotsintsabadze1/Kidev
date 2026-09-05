using Kidev.Storage.PostgreSQL;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kidev.Dashboard.Tests;

internal class DashboardTestHost : WebApplicationFactory<DashboardTestHost>
{
    protected override IHostBuilder CreateHostBuilder() => Host.CreateDefaultBuilder()
        .ConfigureWebHost(builder => builder.UseStartup<DashboardTestStartup>());

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseContentRoot(AppContext.BaseDirectory);
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by the test web host.")]
internal sealed class DashboardTestStartup(IConfiguration configuration, IWebHostEnvironment environment)
{
    public void ConfigureServices(IServiceCollection services)
    {
        string connectionString = configuration["KIDEV_POSTGRES_CONNECTION_STRING"] ?? "Host=127.0.0.1;Port=1;Database=unavailable;Timeout=1";
        services.AddKidevDashboardStorage(connectionString);
        services.AddKidevDashboard();
        services.AddControllersWithViews().AddApplicationPart(typeof(HomeController).Assembly);
        services.AddKidevDashboardIdentity(connectionString);
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "Kidev.Dashboard.Tests.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.LoginPath = "/kidev/Account/Login";
            options.AccessDeniedPath = "/kidev/Account/Denied";
        });
    }

    public void Configure(IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/kidev", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers.CacheControl = "no-store";
            }

            await next(context);
        });
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapKidevDashboard(configuration["DashboardPrefix"] ?? "/kidev");
            endpoints.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        });
    }
}

/// <summary>A consuming MVC application's unrelated controller, used to verify area isolation.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515", Justification = "MVC discovers public controllers.")]
public sealed class HomeController : Controller
{
    /// <summary>Returns the host home page.</summary>
    public IActionResult Index() => Content("Host home");
}
