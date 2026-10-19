using Microsoft.Extensions.DependencyInjection;

namespace Journey.Service;

public static class DependencyInjection
{
    public static IServiceCollection AddJourneyServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<Access>();
        services.AddSingleton<ImportLimits>();
        services.AddSingleton<LocalImportDiscovery>();
        services.AddSingleton<ImportSourceStore>();
        services.AddSingleton<PrivateFileStore>();
        services.AddScoped<Events>();
        services.AddSingleton<IConversationImporter, PrivateAssetImporter>();
        services.AddSingleton<IConversationImporter, ClaudeProjectImporter>();
        services.AddSingleton<IConversationImporter, ChatGptImporter>();
        services.AddSingleton<IConversationImporter, ClaudeImporter>();
        services.AddSingleton<IConversationImporter, ClaudeCodeImporter>();
        services.AddSingleton<IConversationImporter, CodexImporter>();
        services.AddSingleton<IConversationImporter, GeminiCliImporter>();
        services.AddSingleton<IConversationImporter, ApiMessagesImporter>();
        services.AddSingleton<IConversationImporter, CursorSqliteImporter>();
        services.AddSingleton<IConversationImporter, MappingImporter>();
        services.AddSingleton<IConversationImporter, DeveloperHistoryImporter>();
        services.AddSingleton<IConversationImporter, HtmlTranscriptImporter>();
        services.AddSingleton<IConversationImporter, TraceImporter>();
        services.AddSingleton<IConversationImporter, LangChainImporter>();
        services.AddSingleton<IConversationImporter, CursorAgentTranscriptImporter>();
        services.AddSingleton<IConversationImporter, GenericImporter>();
        services.AddSingleton<ImportRegistry>(sp => new ImportRegistry(sp.GetServices<IConversationImporter>()));
        services.AddSingleton<ImportProgressStore>();
        services.AddHostedService<ImportWorker>();
        services.AddHostedService<ImportRetentionWorker>();
        services.AddSignalR();
        return services;
    }
}
