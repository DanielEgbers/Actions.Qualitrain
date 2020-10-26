#r "nuget: System.CommandLine, 2.0.0-beta1.20371.2"
#r "nuget: AngleSharp, 0.14.0"
#r "nuget: AngleSharp.Js, 0.14.0"

#load "../Actions.Shared/git.csx"

#nullable enable

using System.CommandLine;
using System.CommandLine.Invocation;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Js;

return await InvokeCommandAsync(Args.ToArray());

private async Task<int> InvokeCommandAsync(string[] args)
{
    const string LocationsFilePath = "data/locations.json";

    var scrape = new Command("scrape")
    {
        Handler = CommandHandler.Create(async () =>
        {
            var json = await RequestLocationsJsonAsync();

            if (json == null)
                return;

            File.WriteAllText(LocationsFilePath, json);
        })
    };

    var push = new Command("push")
    {
        Handler = CommandHandler.Create(async () =>
        {
            if (Debugger.IsAttached)
                return;

            var workingDirectory = Path.GetDirectoryName(LocationsFilePath)!;

            await Git.ConfigUserAsync(workingDirectory, "GitHub Actions", "actions@users.noreply.github.com");

            if (!await Git.CommitAsync(workingDirectory, "update {files}", Path.GetFileName(LocationsFilePath)))
                return;

            await Git.PushAsync(workingDirectory);
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
    const string Script = "JSON.stringify(tx_locations_markers.locations, null, 2)";

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