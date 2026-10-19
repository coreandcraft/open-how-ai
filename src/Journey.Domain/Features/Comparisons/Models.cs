using System.Text.Json;

namespace Journey.Domain;

public class ComparisonSession : Record
{
    public string WorkspaceId { get; set; } = "";
    public string? JourneyId { get; set; }
    public string Question { get; set; } = "";
    public string Outcome { get; set; } = "Undecided";
    public string Rationale { get; set; } = "";
    public string Ranges { get; set; } = "[[1,999],[1,999]]";
    public string Title { get; set; } = "";
    public string LeftId { get; set; } = "";
    public string RightId { get; set; } = "";
    public string Links { get; set; } = "[]";
}

