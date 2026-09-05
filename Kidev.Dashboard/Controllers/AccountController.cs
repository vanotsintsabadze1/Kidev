using Kidev.Dashboard.Identity;
using Kidev.Dashboard.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Kidev.Dashboard.Controllers;

/// <summary>Provides local sign-in and administrator-controlled account creation.</summary>
[Authorize(Policy = DashboardSecurity.AccessPolicy)]
[Area("Kidev")]
[AutoValidateAntiforgeryToken]
[ServiceFilter(typeof(DatabaseUnavailableFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountController : Controller
{
    private readonly SignInManager<DashboardUser> _signIn;
    private readonly AdministratorSetupService _setup;

    /// <summary>Creates the account controller.</summary>
    public AccountController(SignInManager<DashboardUser> signIn, AdministratorSetupService setup)
    {
        _signIn = signIn;
        _setup = setup;
    }

    /// <summary>Shows the sign-in form, accepting local return destinations only.</summary>
    [AllowAnonymous, HttpGet]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054", Justification = "MVC accepts untrusted return URLs as strings and validates them with IsLocalUrl.")]
    public IActionResult Login(string? returnUrl)
    {
        // Tag helpers prefer ModelState's attempted value over the sanitized model.
        ModelState.Remove(nameof(returnUrl));
        return View(new SignInViewModel { ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });
    }

    /// <summary>Signs in a local account with generic failures and lockout enabled.</summary>
    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> LoginAsync(SignInViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (ModelState.IsValid)
        {
            Microsoft.AspNetCore.Identity.SignInResult result = await _signIn.PasswordSignInAsync(
                model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                return Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl) : RedirectToAction("Index", "Dashboard");
            }
        }

        ModelState.Clear();
        ModelState.AddModelError(string.Empty, "Unable to sign in. Check your credentials or try again later.");
        return View("Login", model with { Password = string.Empty, ReturnUrl = Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl : null });
    }

    /// <summary>Shows account creation for an authenticated administrator.</summary>
    [HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    /// <summary>Creates another Administrator without replacing the current sign-in.</summary>
    [HttpPost]
    public async Task<IActionResult> RegisterAsync(RegisterViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (ModelState.IsValid)
        {
            IdentityResult result = await _setup.CreateAsync(model, firstAdministratorOnly: false, cancellationToken);
            if (result.Succeeded)
            {
                return RedirectToAction("Settings", "Dashboard");
            }

            ModelState.AddModelError(string.Empty, "Unable to create account. Check the email and password requirements; the account may already exist.");
        }

        ModelState.Remove(nameof(model.Password));
        ModelState.Remove(nameof(model.ConfirmPassword));
        return View("Register", model with { Password = string.Empty, ConfirmPassword = string.Empty });
    }

    /// <summary>Signs out the current local account.</summary>
    [Authorize, HttpPost]
    public async Task<IActionResult> LogoutAsync()
    {
        await _signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    /// <summary>Shows a safe access-denied page.</summary>
    [AllowAnonymous, HttpGet]
    public IActionResult Denied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    /// <summary>Shows an error page without exception or configuration details.</summary>
    [AllowAnonymous, IgnoreAntiforgeryToken]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S4502", Justification = "Read-only exception-handler destination must render even when the failed request was a POST; it performs no mutations.")]
    public IActionResult Error() => View();
}
