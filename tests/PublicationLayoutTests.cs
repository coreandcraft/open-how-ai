using System.Text.Json;
using Journey.Domain;
using Journey.Data;


namespace Journey.Tests;

public class PublicationLayoutTests
{
    [Fact]
    public void ParallelBranchesKeepPositionsAndReconvergingEdges()
    {
        var journey = new Journey.Domain.Journey
        {
            Graph = """
                {"nodes":[
                  {"id":"start","position":{"x":0,"y":100},"data":{"title":"Problem"}},
                  {"id":"a","position":{"x":300,"y":0,"secret":"PRIVATE"},"data":{"title":"Chat A"}},
                  {"id":"b","position":{"x":300,"y":200},"data":{"title":"Chat B"}},
                  {"id":"end","position":{"x":600,"y":100},"data":{"title":"Result"}}
                ],"steps":["start","a","b","end"],"edges":[
                  {"source":"start","target":"a"},{"source":"start","target":"b"},
                  {"source":"a","target":"end"},{"source":"b","target":"end"}]}
                """,
        };
        var json = Json.Write(Publishing.Snapshot(journey, [], new([], [], "Unlisted", true)));
        var snapshot = JsonDocument.Parse(json).RootElement;
        var steps = snapshot.GetProperty("steps");
        Assert.Equal(300, steps[1].GetProperty("position").GetProperty("x").GetInt32());
        Assert.Equal(300, steps[2].GetProperty("position").GetProperty("x").GetInt32());
        Assert.Equal(0, steps[1].GetProperty("position").GetProperty("y").GetInt32());
        Assert.Equal(200, steps[2].GetProperty("position").GetProperty("y").GetInt32());
        Assert.Equal(4, snapshot.GetProperty("connections").GetArrayLength());
        Assert.DoesNotContain("PRIVATE", json);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"x\":\"private path\",\"y\":0}")]
    [InlineData("{\"x\":1e999,\"y\":0}")]
    public void InvalidPositionsFallBackWithoutLeakingMetadata(string position)
    {
        var journey = new Journey.Domain.Journey
        {
            Graph =
                "{\"nodes\":[{\"id\":\"a\",\"data\":{\"title\":\"A\"},\"position\":"
                + position
                + "}]}",
        };
        var json = Json.Write(Publishing.Snapshot(journey, [], new([], [], "Unlisted", true)));
        var snapshot = JsonDocument.Parse(json).RootElement;
        Assert.Equal(
            JsonValueKind.Null,
            snapshot.GetProperty("steps")[0].GetProperty("position").ValueKind
        );
        Assert.DoesNotContain("private path", json);
    }

    [Fact]
    public void SharedConversationIsStoredOnceAndBlankLinksStayOnTheCanvas()
    {
        var journey = new Journey.Domain.Journey
        {
            Graph = """
                {"nodes":[
                  {"id":"a","position":{"x":0,"y":0},"data":{"kind":"conversation","title":"A","conversationId":"shared"}},
                  {"id":"b","position":{"x":80,"y":0},"data":{"kind":"decision","title":"B","conversationId":""}},
                  {"id":"c","position":{"x":160,"y":0},"data":{"kind":"conversation","title":"C","conversationId":"shared"}}
                ],"steps":["a","b","c"],"edges":[
                  {"source":"a","target":"b"},{"source":"b","target":"c"}]}
                """,
        };
        var conversation = new Conversation
        {
            Id = "shared",
            Provider = "synthetic",
            Messages =
            [
                new ConversationMessage
                {
                    Id = "m1",
                    Role = "user",
                    Order = 0,
                    Blocks = [new MessageContentBlock { Kind = "text", Text = "unique-evidence-token", Order = 0 }],
                },
            ],
        };
        var json = Json.Write(
            Publishing.Snapshot(journey, [conversation], new(["m1"], [], "Unlisted", true))
        );
        var snapshot = JsonDocument.Parse(json).RootElement;
        Assert.Equal(3, snapshot.GetProperty("steps").GetArrayLength());
        Assert.Equal(2, snapshot.GetProperty("connections").GetArrayLength());
        Assert.Equal(1, snapshot.GetProperty("sources").GetArrayLength());
        Assert.Equal(1, json.Split("unique-evidence-token").Length - 1);
    }
}
