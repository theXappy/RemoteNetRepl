// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Threading.Tasks;
using CSharpRepl.PrettyPromptConfig;
using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Scripting;
using PrettyPrompt;
using PrettyPrompt.Consoles;
using PrettyPrompt.Highlighting;

namespace CSharpRepl;

/// <summary>
/// The core REPL; prints the welcome message, collects input with the <see cref="PrettyPrompt"/> library and
/// processes that input with <see cref="RoslynServices" />.
/// </summary>
internal sealed class ReadEvalPrintLoop
{
    private readonly RoslynServices roslyn;
    private readonly IPrompt prompt;
    private readonly IConsole console;

    public ReadEvalPrintLoop(RoslynServices roslyn, IPrompt prompt, IConsole console)
    {
        this.roslyn = roslyn;
        this.prompt = prompt;
        this.console = console;
    }

    public async Task RunAsync(Configuration config)
    {
        console.WriteLine("Welcome to the C# REPL (Read Eval Print Loop)!");
        console.WriteLine("Type C# expressions and statements at the prompt and press Enter to evaluate them.");
        console.WriteLine($"Type {Help} to learn more, and type {Exit} to quit.");
        console.WriteLine($"Type {HelpRemote} to learn more about RemoteNET API.");
        console.WriteLine(string.Empty);

        await Preload(roslyn, console, config).ConfigureAwait(false);

        string[] remoteNetUsings = new string[2]
        {
            "using RemoteNET;",
            "using Process = System.Diagnostics.Process;",
        };
        Console.WriteLine("Applying predifined `using` statements...");
        foreach (string usingStatement in remoteNetUsings)
        {
            Console.WriteLine(usingStatement);
            roslyn.EvaluateAsync(usingStatement).Wait();
        }
        console.WriteLine(string.Empty);

        while (true)
        {
            var response = await prompt.ReadLineAsync().ConfigureAwait(false);

            if (response is ExitApplicationKeyPress)
            {
                break;
            }

            if (response.IsSuccess)
            {
                var commandText = response.Text.Trim().ToLowerInvariant();

                // evaluate built in commands
                if (commandText == "exit") { break; }
                if (commandText == "clear") { console.Clear(); continue; }
                if (commandText == "help-remote") 
                {
                    PrintRemoteNETHelp();
                    continue;
                }
                if (new[] { "help", "#help", "?" }.Contains(commandText))
                {
                    PrintHelp();
                    continue;
                }

                // evaluate results returned by special keybindings (configured in the PromptConfiguration.cs)
                if (response is KeyPressCallbackResult callbackOutput)
                {
                    console.WriteLine(Environment.NewLine + callbackOutput.Output);
                    continue;
                }

                response.CancellationToken.Register(() => Environment.Exit(1));

                // evaluate C# code and directives
                var result = await roslyn
                    .EvaluateAsync(response.Text, config.LoadScriptArgs, response.CancellationToken)
                    .ConfigureAwait(false);

                var displayDetails = config.SubmitPromptDetailedKeys.Matches(response.SubmitKeyInfo);
                await PrintAsync(roslyn, console, result, displayDetails);
            }
        }
    }

    private static async Task Preload(RoslynServices roslyn, IConsole console, Configuration config)
    {
        bool hasReferences = config.References.Count > 0;
        bool hasLoadScript = config.LoadScript is not null;
        if (!hasReferences && !hasLoadScript)
        {
            _ = roslyn.WarmUpAsync(config.LoadScriptArgs); // don't await; we don't want to block the console while warmup happens.
            return;
        }

        if (hasReferences)
        {
            console.WriteLine("Adding supplied references...");
            var loadReferenceScript = string.Join("\r\n", config.References.Select(reference => $@"#r ""{reference}"""));
            var loadReferenceScriptResult = await roslyn.EvaluateAsync(loadReferenceScript).ConfigureAwait(false);
            await PrintAsync(roslyn, console, loadReferenceScriptResult, displayDetails: false).ConfigureAwait(false);
        }

        if (hasLoadScript)
        {
            console.WriteLine("Running supplied CSX file...");
            var loadScriptResult = await roslyn.EvaluateAsync(config.LoadScript!, config.LoadScriptArgs).ConfigureAwait(false);
            await PrintAsync(roslyn, console, loadScriptResult, displayDetails: false).ConfigureAwait(false);
        }
    }

    private static async Task PrintAsync(RoslynServices roslyn, IConsole console, EvaluationResult result, bool displayDetails)
    {
        switch (result)
        {
            case EvaluationResult.Success ok:
                var formatted = await roslyn.PrettyPrintAsync(ok?.ReturnValue, displayDetails);
                console.WriteLine(formatted);
                break;
            case EvaluationResult.Error err:
                var formattedError = await roslyn.PrettyPrintAsync(err.Exception, displayDetails);
                console.WriteErrorLine(AnsiColor.Red.GetEscapeSequence() + formattedError + AnsiEscapeCodes.Reset);
                break;
            case EvaluationResult.Cancelled:
                console.WriteErrorLine(
                    AnsiColor.Yellow.GetEscapeSequence() + "Operation cancelled." + AnsiEscapeCodes.Reset
                );
                break;
        }
    }

    private void PrintRemoteNETHelp()
    {
        console.WriteLine(
$@"
Complete RemoteNET API is available here:
{Link("https://github.com/theXappy/RemoteNET")}

Connecting to a Remote App
==========================
To start investigating a process call the static `Connect` func of RemoteApp with its name:
{(Code(
@"```
    var app = RemoteApp.Connect(""target_process_name"");
```"
))}

Finding an Object
=================
Use RemoteApp.QueryInstances to search the remote heap of a specific object 
type.
You should get back a list of candidates for the requested object.
A candidate should be given to the RemoteApp.GetRemoteObject function to 
get ahold of a remote object.
{(Code(
@"```
    IEnumerable<CandidateObject> candidates = remoteApp.QueryInstances(""System.IO.FileSystemWatcher"");
    RemoteObject remoteDocumentManagerEx = remoteApp.GetRemoteObject(candidates.Single());
```"
))}


Working with a Remote Object
============================
First, you'll probably want to gain a dynamic proxy of the RemoteObject you got:
{(Code(
@"```
    dynamic dynRemoteObj = myRemoteObject.Dynamify();
```"
))}

We are doing that because the dynamic object has a nicer API. The 'raw' 
RemoteObject can do everything too but it's required to study it's specific 
methods instead of writing ""trivial C# statements"" like in the dynamic 
API below.

Once you have a dynamic object you can ignore the fact that it's a remote 
one and start using it's members as if it was a local object:
{(Code(
@"```
    // Reading Fields/Properties
    Console.WriteLine(dynRemoteObject.Length);

    // Invoking functions
    string myString = dynRemoteObject.ToString();
```"
))}

There are more to interacting with dynamic objects (like using them as 
parameters to other functions or registering to events) and you are encourage to
read more at the GitHub repo specified in the begining of this help message.
"
        );
    }

    private void PrintHelp()
    {
        console.WriteLine(
$@"
More details and screenshots are available at
https://github.com/waf/CSharpRepl/blob/main/README.md

Evaluating Code
===============
Type C# at the prompt and press {Underline("Enter")} to run it. The result will be printed.
{Underline("Ctrl+Enter")} will also run the code, but show detailed member info / stack traces.
{Underline("Shift+Enter")} will insert a newline, to support multiple lines of input.
If the code isn't a complete statement, pressing Enter will insert a newline.

Adding References
=================
Use the {Reference()} command to add assembly or nuget references.
For assembly references, run {Reference("AssemblyName")} or {Reference("path/to/assembly.dll")}
For nuget packages, run {Reference("nuget: PackageName")} or {Reference("nuget: PackageName, version")}
For project references, run {Reference("path/to/my.csproj")} or {Reference("path/to/my.sln")} 

Use {Preprocessor("#load", "path-to-file")} to evaluate C# stored in files (e.g. csx files). This can
be useful, for example, to build a "".profile.csx"" that includes libraries you want
to load.

Exploring Code
==============
{Underline("F1")}: when the caret is in a type or member, open the corresponding MSDN documentation.
{Underline("F9")}: show the IL (intermediate language) for the current statement.
{Underline("F12")}: open the type's source code in the browser, if the assembly supports Source Link.

Configuration Options
=====================
All configuration, including theming, is done at startup via command line flags.
Run --help at the command line to view these options
"
        );
    }

    private string Reference(string? argument = null) =>
        Preprocessor("#r", argument);

    private string Link(string? url = null) =>
        prompt.HasUserOptedOutFromColor
        ? url
        : AnsiEscapeCodes.BrightBlue + url + AnsiEscapeCodes.Reset;

    private string Code(string? code = null) =>
        prompt.HasUserOptedOutFromColor
        ? code
        : AnsiEscapeCodes.Green + code + AnsiEscapeCodes.Reset;

    /// <summary>
    /// Produce syntax-highlighted strings like "#r reference" for the provided <paramref name="argument"/> string.
    /// </summary>
    private string Preprocessor(string keyword, string? argument = null)
    {
        var highlightedKeyword = Color("preprocessor keyword") + keyword + AnsiEscapeCodes.Reset;
        var highlightedArgument = argument is null ? "" : Color("string") + @" """ + argument + @"""" + AnsiEscapeCodes.Reset;

        return highlightedKeyword + highlightedArgument;
    }

    private string Color(string reference) =>
        PromptConfiguration.HasUserOptedOutFromColor
        ? string.Empty
        : AnsiEscapeCodes.ToAnsiEscapeSequenceSlow(new ConsoleFormat(roslyn!.ToColor(reference)));

    private static string Underline(string word) =>
        AnsiEscapeCodes.ToAnsiEscapeSequenceSlow(new ConsoleFormat(Underline: true))
        + word + AnsiEscapeCodes.Reset;

    private static string Help =>
        PromptConfiguration.HasUserOptedOutFromColor
        ? @"""help"""
        : AnsiColor.Green.GetEscapeSequence() + "help" + AnsiEscapeCodes.Reset;

    private string HelpRemote =>
        prompt.HasUserOptedOutFromColor
        ? @"""help-remote"""
        : AnsiEscapeCodes.Green + "help-remote" + AnsiEscapeCodes.Reset;

    private string Exit =>
        PromptConfiguration.HasUserOptedOutFromColor
        ? @"""exit"""
        : AnsiColor.BrightRed.GetEscapeSequence() + "exit" + AnsiEscapeCodes.Reset;
}
