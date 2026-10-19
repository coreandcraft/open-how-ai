using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.DataProtection;
using OpenTelemetry.Trace;

namespace Journey.Server;

public static class AzureHosting
{
    public static void AddAzureHosting(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var credential = new DefaultAzureCredential();
        if (!string.IsNullOrWhiteSpace(config["KeyVault:Uri"]))
            config.AddAzureKeyVault(new Uri(config["KeyVault:Uri"]!), credential);

        if (builder.Environment.IsProduction())
        {
            foreach (var key in new[] { "KeyVault:Uri", "ConnectionStrings:Journey", "Storage:ContainerUri",
                "DataProtection:BlobUri", "DataProtection:KeyIdentifier", "APPLICATIONINSIGHTS_CONNECTION_STRING" })
                if (string.IsNullOrWhiteSpace(config[key]))
                    throw new InvalidOperationException($"Production requires {key}.");
            if (config["Database:Provider"] != "PostgreSQL" || config["Storage:Provider"] != "AzureBlob")
                throw new InvalidOperationException("Production requires PostgreSQL and AzureBlob providers.");
        }

        if (!string.IsNullOrWhiteSpace(config["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            builder.Services.AddOpenTelemetry().UseAzureMonitor();
            builder.Services.ConfigureOpenTelemetryTracerProvider((_, tracing) =>
                tracing.AddProcessor(new RequestTelemetryProcessor()));
        }

        if (!string.IsNullOrWhiteSpace(config["DataProtection:BlobUri"]))
            builder.Services.AddDataProtection()
                .SetApplicationName("JourneyAI")
                .PersistKeysToAzureBlobStorage(new Uri(config["DataProtection:BlobUri"]!), credential)
                .ProtectKeysWithAzureKeyVault(new Uri(config["DataProtection:KeyIdentifier"]
                    ?? throw new InvalidOperationException("DataProtection:KeyIdentifier is required.")), credential);
    }
}
