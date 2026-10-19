using Journey.Domain;

namespace Journey.Server.Features.Review;

public record AnnotationInput(
    string? ConversationId,
    string? MessageId,
    string? JourneyId,
    string? NodeId,
    string? Kind,
    AnnotationAnchor? Anchor,
    string Body
);

