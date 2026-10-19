using System.Text.Json;

namespace Journey.Domain;

public class User : Microsoft.AspNetCore.Identity.IdentityUser
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Name { get; set; } = "";
    public string SanitizationRules { get; set; } = "[]";
}

