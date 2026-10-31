using System;
using System.Collections.Generic;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging.Abstractions;

var config = new Configuration { AppName = "TekitouKeyborad" };
await FoundryLocalManager.CreateAsync(config, NullLogger.Instance);

var catalog = await FoundryLocalManager.Instance.GetCatalogAsync();
var model = await catalog.GetModelAsync("phi-3.5-mini");
Console.WriteLine("Loading model...");
await model.LoadAsync();
Console.WriteLine("Model loaded. Testing ChatSession...");

try
{
    using var session = new ChatSession(model);
    var reqOpt = new RequestOptions
    {
        Search = new SearchOptions
        {
            Temperature = 0.3f,
            MaxOutputTokens = 128
        },
        AdditionalOptions = new Dictionary<string, string>
        {
            { "max_length", "2048" }
        }
    };
    session.SetOptions(reqOpt);

    using var request = new Request()
        .AddItem(MessageItem.System("You are a helpful assistant."))
        .AddItem(MessageItem.User("Hello!"));

    Console.WriteLine("Processing request with AdditionalOptions max_length=2048...");
    using var response = await session.ProcessRequestAsync(request);
    Console.WriteLine("Success! Response:");
    foreach (var item in response)
    {
        if (item is MessageItem mi) Console.WriteLine(mi.GetSimpleText());
    }
}
catch (Exception ex)
{
    Console.WriteLine("ChatSession Exception: " + ex);
}
