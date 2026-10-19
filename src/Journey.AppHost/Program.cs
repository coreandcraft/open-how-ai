using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres", port: builder.Configuration.GetValue("Ports:Postgres", 5433))
    .WithImageTag("17")
    .WithDataVolume()
    .WithPgAdmin(admin => admin.WithHostPort(builder.Configuration.GetValue("Ports:PgAdmin", 5050)));
var database = postgres.AddDatabase("Journey", "journey");

var server = builder.AddProject<Projects.Journey_Server>("server", launchProfileName: null)
    .WithHttpEndpoint(name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("Database__Provider", "PostgreSQL")
    .WithReference(database)
    .WaitFor(database)
    .WithHttpHealthCheck("/api/health");

foreach (var setting in new[] { "Enabled", "AuthenticationMode", "App:ClientId", "App:Slug" })
    if (builder.Configuration["GitHub:" + setting] is { } value)
        server.WithEnvironment("GitHub__" + setting.Replace(":", "__"), value);
// Keep the PEM in server user-secrets or Key Vault, outside the Aspire dashboard.

var client = builder.AddViteApp("client", "../Journey.Client")
    .WithEndpoint("http", endpoint => endpoint.Port = builder.Configuration.GetValue("Ports:Client", 5173))
    .WithEnvironment("JOURNEY_API_URL", server.GetEndpoint("http"))
    .WaitFor(server);

server.WithEnvironment("Site__PublicUrl", client.GetEndpoint("http"));

builder.Build().Run();
