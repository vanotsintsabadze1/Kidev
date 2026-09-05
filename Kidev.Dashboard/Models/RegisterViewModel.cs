using System.ComponentModel.DataAnnotations;

namespace Kidev.Dashboard.Models;

/// <summary>Contains administrator-controlled local account creation input.</summary>
public sealed record RegisterViewModel
{
    /// <summary>Gets the display name.</summary>
    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets the account email.</summary>
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = string.Empty;
    /// <summary>Gets the password.</summary>
    [Required, StringLength(1024, MinimumLength = 12), DataType(DataType.Password)]
    public string Password { get; init; } = string.Empty;
    /// <summary>Gets the password confirmation.</summary>
    [Required, Compare(nameof(Password)), DataType(DataType.Password), StringLength(1024)]
    public string ConfirmPassword { get; init; } = string.Empty;
}
