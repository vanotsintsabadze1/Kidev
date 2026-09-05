using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Kidev.Dashboard.Identity;
using Kidev.Dashboard.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kidev.Dashboard.Tests;

/// <summary>Verifies generated Identity migrations and bootstrap against an isolated PostgreSQL database.</summary>
public sealed class IdentitySetupTests
{
    /// <summary>Verifies setup touches only Identity, grants Administrator, and refuses repeated bootstrap.</summary>
    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100", Justification = "Database identifier consists solely of a fixed prefix and a generated hexadecimal GUID.")]
    public async Task SetupCreatesOnlyIdentityAndFirstAdministratorAsync()
    {
        string? connectionString = Environment.GetEnvironmentVariable("KIDEV_DASHBOARD_TEST_CONNECTION_STRING");
        await using PostgreSqlContainer? container = connectionString is null ? new PostgreSqlBuilder("postgres:17-alpine").Build() : null;
        if (container is not null)
        {
            await container.StartAsync();
            connectionString = container.GetConnectionString();
        }

        var connectionOptions = new NpgsqlConnectionStringBuilder(connectionString);
        string databaseName = "dashboard_test_" + Guid.NewGuid().ToString("N");
        await using var adminConnection = new NpgsqlConnection(connectionString);
        await adminConnection.OpenAsync();
        await using (var createDatabase = new NpgsqlCommand($"CREATE DATABASE {databaseName}", adminConnection))
        {
            await createDatabase.ExecuteNonQueryAsync();
        }

        connectionOptions.Database = databaseName;
        connectionString = connectionOptions.ConnectionString;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DashboardIdentityContext>(options => options.UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__IdentityMigrationsHistory", "kidev_dashboard")));
        services.AddIdentityCore<DashboardUser>(options => options.Password.RequiredLength = 12)
            .AddRoles<IdentityRole>().AddEntityFrameworkStores<DashboardIdentityContext>();
        services.AddScoped<AdministratorSetupService>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        DashboardIdentityContext context = scope.ServiceProvider.GetRequiredService<DashboardIdentityContext>();
        await context.Database.MigrateAsync();
        AdministratorSetupService setup = scope.ServiceProvider.GetRequiredService<AdministratorSetupService>();
        string password = $"A!z9{Guid.NewGuid():N}";
        string email = $"{Guid.NewGuid():N}@example.test";
        var input = new RegisterViewModel { Name = "Test administrator", Email = email, Password = password, ConfirmPassword = password };
        IdentityResult created = await setup.CreateAsync(input, firstAdministratorOnly: true, CancellationToken.None);
        created.Succeeded.Should().BeTrue();
        UserManager<DashboardUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DashboardUser>>();
        DashboardUser? user = await users.FindByEmailAsync(email);
        user.Should().NotBeNull();
        (await users.IsInRoleAsync(user, "Administrator")).Should().BeTrue();
        IdentityResult repeated = await setup.CreateAsync(input with { Email = "another@example.test" }, firstAdministratorOnly: true, CancellationToken.None);
        repeated.Succeeded.Should().BeFalse();
        (await context.Users.CountAsync()).Should().Be(1);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN ('job_definitions', 'job_executions', '__EFMigrationsHistory')", connection);
        (await command.ExecuteScalarAsync()).Should().Be(0L);

        await using var factory = new IdentityFactory(connectionString);
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using HttpResponseMessage loginPage = await client.GetAsync(new Uri("/kidev/Account/Login?returnUrl=https://example.test/", UriKind.Relative));
        loginPage.StatusCode.Should().Be(HttpStatusCode.OK);
        string html = await loginPage.Content.ReadAsStringAsync();
        html.Should().NotContain("https://example.test/");
        string token = WebUtility.HtmlDecode(Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1)).Groups["token"].Value);
        token.Should().NotBeEmpty();
        using var login = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Email"] = email,
            ["Password"] = password,
            ["ReturnUrl"] = "https://example.test/",
            ["__RequestVerificationToken"] = token,
        });
        using HttpResponseMessage signedIn = await client.PostAsync(new Uri("/kidev/Account/Login", UriKind.Relative), login);
        signedIn.StatusCode.Should().Be(HttpStatusCode.Redirect);
        signedIn.Headers.Location!.OriginalString.Should().Be("/kidev");
        signedIn.Headers.GetValues("Set-Cookie").Should().Contain(cookie => cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));

        // Identity exists but job tables do not: this must render a safe 503, not migrate or leak provider errors.
        using HttpResponseMessage unavailable = await client.GetAsync(new Uri("/kidev", UriKind.Relative));
        unavailable.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        string errorPage = await unavailable.Content.ReadAsStringAsync();
        errorPage.Should().NotContain(connectionString).And.NotContain("Npgsql").And.NotContain("42P01");
        unavailable.Headers.CacheControl!.NoStore.Should().BeTrue();

        using var missingToken = new FormUrlEncodedContent([]);
        using HttpResponseMessage logout = await client.PostAsync(new Uri("/kidev/Account/Logout", UriKind.Relative), missingToken);
        logout.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using HttpClient anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using HttpResponseMessage anonymousLogin = await anonymous.GetAsync(new Uri("/kidev/Account/Login", UriKind.Relative));
        string anonymousHtml = await anonymousLogin.Content.ReadAsStringAsync();
        string anonymousToken = WebUtility.HtmlDecode(Regex.Match(anonymousHtml,
            "name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1)).Groups["token"].Value);
        for (int attempt = 0; attempt < 6; attempt++)
        {
            using var credentials = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Email"] = email,
                ["Password"] = attempt == 5 ? password : "incorrect",
                ["__RequestVerificationToken"] = anonymousToken,
            });
            using HttpResponseMessage failure = await anonymous.PostAsync(new Uri("/kidev/Account/Login", UriKind.Relative), credentials);
            failure.StatusCode.Should().Be(HttpStatusCode.OK);
            string failureHtml = await failure.Content.ReadAsStringAsync();
            failureHtml.Should().Contain("Unable to sign in. Check your credentials or try again later.");
            failureHtml.Should().NotContain(password);
        }

        await context.Entry(user).ReloadAsync();
        (await users.IsLockedOutAsync(user)).Should().BeTrue();
    }

    private sealed class IdentityFactory(string connectionString) : DashboardTestHost
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("KIDEV_POSTGRES_CONNECTION_STRING", connectionString);
        }
    }
}
