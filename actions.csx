#r "nuget: System.CommandLine, 2.0.0-beta1.20371.2"
#r "nuget: AngleSharp, 0.14.0"
#r "nuget: AngleSharp.Js, 0.14.0"
#r "nuget: Newtonsoft.Json, 12.0.3"

#load "../Actions.Shared/git.csx"

#nullable enable

using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Js;
using Newtonsoft.Json;

return await InvokeCommandAsync(Args.ToArray());

private async Task<int> InvokeCommandAsync(string[] args)
{
    var dataPath = @"data";

    var scrape = new Command("scrape")
    {
        Handler = CommandHandler.Create(async () =>
        {
            var locationsJson = await RequestLocationsJsonAsync();

            if (locationsJson == null)
                return;

            ClearLocationsByCity(dataPath);

            SaveLocationsByCity(dataPath, locationsJson);
        })
    };

    var push = new Command("push")
    {
        Handler = CommandHandler.Create(async () =>
        {
            if (Debugger.IsAttached)
                return;

            if (!(await Git.GetChangesAsync(workingDirectory: dataPath)).Any())
                return;

            await Git.ConfigUserAsync(name: "GitHub Actions", email: "actions@users.noreply.github.com", workingDirectory: dataPath);

            await Git.StageAllAsync(workingDirectory: dataPath);

            await Git.CommitAsync("update", workingDirectory: dataPath);

            await Git.PushAsync(workingDirectory: dataPath);
        })
    };

    var root = new RootCommand()
    {
        scrape,
        push,
    };

    root.Handler = CommandHandler.Create(async () =>
    {
        await scrape.InvokeAsync(string.Empty);
        await push.InvokeAsync(string.Empty);
    });

    return await root.InvokeAsync(args);
}

private async Task<string?> RequestLocationsJsonAsync()
{
    const string Url = @"https://qualitrain.net/locations/";
    const string Script = "JSON.stringify(tx_locations_markers.locations)";

    using
    (
        var context = BrowsingContext.New
        (
            Configuration.Default
                .WithDefaultLoader()
                .WithJs()
        )
    )
    {
        var document = await context
            .OpenAsync(Url)
            .WaitUntilAvailable()
            ;

        var result = document.ExecuteScript(Script);

        if (result is string json)
            return json;
    }

    return null;
}

private void ClearLocationsByCity(string path)
{
    foreach (var jsonFile in  Directory.GetFiles(path, "*.json"))
    {
        File.WriteAllText(jsonFile, "[]");
    }
}

private void SaveLocationsByCity(string path, string locationsJson)
{
    var locationsByCity = JsonConvert.DeserializeObject<dynamic[]>(locationsJson)
        .GroupBy(l => l.cityurlslug)
        .ToDictionary(g => g.Key, g => g.ToList())
        ;

    foreach (var locations in locationsByCity)
    {
        var fileName = $"{locations.Key}.json";
        
        File.WriteAllText(Path.Combine(path, fileName), JsonConvert.SerializeObject(locations.Value, Formatting.Indented));
    }
}