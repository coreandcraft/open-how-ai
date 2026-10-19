using System.Text;
using Journey.Domain;
using Journey.Service.Features.Imports;
using Microsoft.Extensions.DependencyInjection;

namespace Journey.Tests;

public class StreamingImportTests : IClassFixture<Factory>
{
    readonly Factory factory;

    public StreamingImportTests(Factory factory) => this.factory = factory;

    [Fact]
    public async Task StreamsAccountRecordsWithOriginalHashAndRecordLocation()
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ImportSourceStore>();
        var source = new ImportSource { Name = "large-account.json" };
        using var input = new MemoryStream();
        using (var writer = new StreamWriter(input, Encoding.UTF8, 1024, true))
        {
            // No BOM: provider exports use UTF-8 JSON.
            writer.Write('[');
            for (var i = 0; i < 10000; i++)
            {
                if (i > 0)
                    writer.Write(',');
                writer.Write(
                    Json.Write(
                        new
                        {
                            id = "c-" + i,
                            messages = new[]
                            {
                                new
                                {
                                    id = "m-" + i,
                                    role = "user",
                                    content = "Deterministic synthetic source",
                                },
                            },
                        }
                    )
                );
            }
            writer.Write(']');
        }
        input.Position = 3; // StreamWriter UTF-8 preamble; only the JSON bytes are supplied below.
        await store.Save(source, input, input.Length, input.Length, default);
        try
        {
            int count = 0,
                largest = 0;
            await foreach (var record in StreamingImportInputs.Read(source, store, default))
            {
                Assert.Equal(source.Hash, record.OriginalHash);
                Assert.Equal("$[" + count + "]", record.RecordLocation);
                largest = Math.Max(largest, record.Bytes.Length);
                count++;
            }
            Assert.Equal(10000, count);
            Assert.True(
                largest < 300,
                "Inputs must be emitted per conversation, not as the complete account history."
            );
        }
        finally
        {
            store.Delete(source);
        }
    }

    [Fact]
    public async Task DoesNotSplitMessageArraysIntoInventedConversations()
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ImportSourceStore>();
        var source = new ImportSource
        {
            Name = "messages.json",
            Bytes =
                """[{"role":"user","content":"one"},{"role":"assistant","content":"two"}]"""u8.ToArray(),
        };
        var inputs = new List<SourceInput>();
        await foreach (var input in StreamingImportInputs.Read(source, store, default))
            inputs.Add(input);
        Assert.Single(inputs);
        Assert.Equal(
            2,
            (await new GenericImporter().PreviewAsync(inputs[0], default))
                .Conversations.Single()
                .Messages.Count
        );
    }
}
