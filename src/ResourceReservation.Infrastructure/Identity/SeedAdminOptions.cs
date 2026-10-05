namespace ResourceReservation.Infrastructure.Identity;
public sealed class SeedAdminOptions
{
    public const string SectionName = "SeedAdmin";

    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = "System Admin";
}
