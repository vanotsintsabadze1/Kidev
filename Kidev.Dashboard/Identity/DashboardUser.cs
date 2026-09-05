using Microsoft.AspNetCore.Identity;

namespace Kidev.Dashboard.Identity;

/// <summary>A local dashboard account, independent of worker application accounts.</summary>
public sealed class DashboardUser : IdentityUser
{
    /// <summary>Gets or sets the display name.</summary>
    public string Name { get; set; } = string.Empty;
}
