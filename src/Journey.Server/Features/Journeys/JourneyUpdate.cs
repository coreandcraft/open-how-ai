using Journey.Domain;

namespace Journey.Server.Features.Journeys;

public record JourneyUpdate(
    string Title,
    string Description,
    string Visibility,
    string Graph,
    int Version
);

