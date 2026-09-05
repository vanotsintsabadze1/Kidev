using System.ComponentModel.DataAnnotations;

namespace Kidev.Dashboard.Models;

/// <summary>Contains local account sign-in input.</summary>
public sealed record SignInViewModel
{
    /// <summary>Gets the account email.</summary>
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = string.Empty;
    /// <summary>Gets the password.</summary>
    [Required, DataType(DataType.Password), StringLength(1024)]
    public string Password { get; init; } = string.Empty;
    /// <summary>Gets whether a persistent cookie is requested.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S6964", Justification = "Omitting the checkbox intentionally defaults to a nonpersistent cookie.")]
    public bool RememberMe { get; init; }
    /// <summary>Gets the local destination after sign-in.</summary>
    [StringLength(2048)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056", Justification = "MVC local return URLs are validated using IUrlHelper.IsLocalUrl.")]
    public string? ReturnUrl { get; init; }
}
