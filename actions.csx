#r "nuget: System.CommandLine, 2.0.0-beta1.20371.2"
#r "nuget: SimpleExec, 6.2.0"
#r "nuget: AngleSharp, 0.14.0"
#r "nuget: AngleSharp.Js, 0.14.0"

#nullable enable

using System.CommandLine;
using System.CommandLine.Invocation;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Js;

using static SimpleExec.Command;

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

            await GitConfigUserAsync(workingDirectory, "GitHub Actions", "actions@users.noreply.github.com");

            if (!await GitCommitAsync(workingDirectory, "update {files}", Path.GetFileName(LocationsFilePath)))
                return;

            await GitPushAsync(workingDirectory);
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

private async Task GitConfigUserAsync(string workingDirectory, string name, string email)
{
    await RunAsync("git", $"config user.name \"{name}\"", workingDirectory: workingDirectory);
    await RunAsync("git", $"config user.email \"{email}\"", workingDirectory: workingDirectory);
}

private async Task<bool> GitCommitAsync(string workingDirectory, string message, params string[] files)
{
    var gitStatus = await ReadAsync("git", $"status --short --untracked-files", workingDirectory: workingDirectory);

    var changedFiles = files.Where(f => gitStatus.Contains(f)).ToArray();

    if (changedFiles.Length <= 0)
        return false;

    var changedFilesJoin = $"\"{string.Join("\" \"", changedFiles)}\"";

    await RunAsync("git", $"add {changedFilesJoin}", workingDirectory: workingDirectory);

    var gitCommitMessage = message.Replace($"{{{nameof(files)}}}", changedFilesJoin.Replace("\"", "'"));
    await RunAsync("git", $"commit -m \"{gitCommitMessage}\"", workingDirectory: workingDirectory);

    return true;
}

private async Task GitPushAsync(string workingDirectory)
{
    await RunAsync("git", "push --quiet --progress", workingDirectory: workingDirectory);
}