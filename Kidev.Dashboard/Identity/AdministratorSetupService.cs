using System.ComponentModel.DataAnnotations;
using Kidev.Dashboard.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kidev.Dashboard.Identity;

/// <summary>Creates administrator accounts transactionally and supports explicit first-admin setup.</summary>
/// <param name="context">The isolated Identity context.</param>
/// <param name="users">The local account manager.</param>
/// <param name="roles">The local role manager.</param>
public sealed class AdministratorSetupService(
    DashboardIdentityContext context,
    UserManager<DashboardUser> users,
    RoleManager<IdentityRole> roles)
{
    /// <summary>Creates an Administrator, optionally requiring the account store to be empty.</summary>
    public async Task<IdentityResult> CreateAsync(RegisterViewModel model, bool firstAdministratorOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!Validator.TryValidateObject(model, new ValidationContext(model), [], validateAllProperties: true))
        {
            return IdentityResult.Failed(new IdentityError { Description = "Provide a name, valid email, and matching passwords of 12 to 1024 characters." });
        }

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        // Serialize bootstrap and account creation so concurrent setup cannot create multiple first administrators.
        await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(724193650021)", cancellationToken);
        if (firstAdministratorOnly && await context.Users.AnyAsync(cancellationToken))
        {
            return IdentityResult.Failed(new IdentityError { Description = "Setup is already complete. Sign in as an administrator to create accounts." });
        }

        if (!await roles.RoleExistsAsync(DashboardSecurity.AdministratorRole))
        {
            IdentityResult roleResult = await roles.CreateAsync(new IdentityRole(DashboardSecurity.AdministratorRole));
            if (!roleResult.Succeeded)
            {
                return roleResult;
            }
        }

        var user = new DashboardUser { UserName = model.Email, Email = model.Email, Name = model.Name };
        IdentityResult result = await users.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            return result;
        }

        result = await users.AddToRoleAsync(user, DashboardSecurity.AdministratorRole);
        if (result.Succeeded)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return result;
    }

    /// <summary>Runs explicit interactive or environment-driven bootstrap, optionally migrating Identity only.</summary>
    public async Task<int> RunAsync(bool migrateIdentity, CancellationToken cancellationToken)
    {
        if (migrateIdentity)
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        if (await context.Users.AnyAsync(cancellationToken))
        {
            await Console.Error.WriteLineAsync("Setup is already complete. Use administrator-controlled registration.");
            return 1;
        }

        string email = ReadCredential("KIDEV_DASHBOARD_ADMIN_EMAIL", "Admin email: ", secret: false);
        string name = ReadCredential("KIDEV_DASHBOARD_ADMIN_NAME", "Admin name: ", secret: false);
        string password = ReadCredential("KIDEV_DASHBOARD_ADMIN_PASSWORD", "Admin password: ", secret: true);
        string confirmation = Environment.GetEnvironmentVariable("KIDEV_DASHBOARD_ADMIN_PASSWORD") is not null
            ? password : ReadCredential("KIDEV_DASHBOARD_ADMIN_PASSWORD_CONFIRMATION", "Confirm password: ", secret: true);
        IdentityResult result = await CreateAsync(new RegisterViewModel
        {
            Email = email,
            Name = name,
            Password = password,
            ConfirmPassword = confirmation,
        }, firstAdministratorOnly: true, cancellationToken);
        await Console.Out.WriteLineAsync(result.Succeeded
            ? "First Administrator created. Start the dashboard normally to sign in."
            : "Administrator creation failed. Check email, name, and matching passwords (12+ characters, upper/lowercase, digit and symbol). Setup may already be complete.");
        return result.Succeeded ? 0 : 1;
    }

    private static string ReadCredential(string environmentVariable, string prompt, bool secret)
    {
        string? value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException("Setup requires credential environment variables or an interactive terminal.");
        }

        Console.Write(prompt);
        if (!secret)
        {
            return Console.ReadLine() ?? string.Empty;
        }

        var input = new System.Text.StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return input.ToString();
            }

            if (key.Key == ConsoleKey.Backspace && input.Length > 0)
            {
                input.Length--;
            }
            else if (!char.IsControl(key.KeyChar) && input.Length < 1024)
            {
                input.Append(key.KeyChar);
            }
        }
    }
}
