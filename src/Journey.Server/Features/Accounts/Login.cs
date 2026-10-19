using Journey.Domain;

namespace Journey.Server.Features.Accounts;

public record Login(string Email, string Password, string? Name);

