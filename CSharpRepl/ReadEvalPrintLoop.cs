// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CSharpRepl.PrettyPromptConfig;
using CSharpRepl.Services;
using CSharpRepl.Services.Extensions;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Formatting;
using CSharpRepl.Services.Roslyn.Scripting;
using CSharpRepl.Services.Theming;
using PrettyPrompt;
using PrettyPrompt.Consoles;
using PrettyPrompt.Highlighting;
using Spectre.Console;

namespace CSharpRepl;

/// <summary>
/// The core REPL; prints the welcome message, collects input with the <see cref="PrettyPrompt"/> library and
/// processes that input with <see cref="RoslynServices" />.
/// </summary>
internal sealed class ReadEvalPrintLoop
{
    private readonly IConsoleEx console;
    private readonly RoslynServices roslyn;
    private readonly IPrompt prompt;

    public ReadEvalPrintLoop(IConsoleEx console, RoslynServices roslyn, IPrompt prompt)
    {
        this.console = console;
        this.roslyn = roslyn;
        this.prompt = prompt;
    }

    public async Task RunAsync(Configuration config)
    {
        console.WriteLine("Welcome to the C# REPL (Read Eval Print Loop)!");
        console.WriteLine("Type C# expressions and statements at the prompt and press Enter to evaluate them.");
        console.WriteLine($"Type {Help} to learn more, {Exit} to quit, and {Clear} to clear your terminal.");
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

        if (File.Exists(config.CmdLineArgStatementsFile))
        {
            Console.WriteLine("Running statments provided via command line args...");
            using (FileStream fs = File.OpenRead(config.CmdLineArgStatementsFile))
            using (StreamReader sr = new StreamReader(fs))
            {
                while (!sr.EndOfStream)
                {
                    string line = sr.ReadLine();
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    Console.WriteLine(line);
                    roslyn.EvaluateAsync(line).Wait();
                }
            }

            Console.WriteLine();
        }

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
                if (commandText == "help_remote")
                {
                    PrintRemoteNETHelp();
                    continue;
                }
                if (new[] { "help", "#help", "?" }.Contains(commandText))
                {
                    PrintHelp(config.KeyBindings, config.SubmitPromptDetailedKeys);
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
                await PrintAsync(roslyn, console, result, displayDetails ? Level.FirstDetailed : Level.FirstSimple);
            }
        }
    }

    private static async Task Preload(RoslynServices roslyn, IConsoleEx console, Configuration config)
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
            await PrintAsync(roslyn, console, loadReferenceScriptResult, level: Level.FirstSimple).ConfigureAwait(false);
        }

        if (hasLoadScript)
        {
            console.WriteLine("Running supplied CSX file...");
            var loadScriptResult = await roslyn.EvaluateAsync(config.LoadScript!, config.LoadScriptArgs).ConfigureAwait(false);
            await PrintAsync(roslyn, console, loadScriptResult, level: Level.FirstSimple).ConfigureAwait(false);
        }
    }

    private static async Task PrintAsync(RoslynServices roslyn, IConsoleEx console, EvaluationResult result, Level level)
    {
        switch (result)
        {
            case EvaluationResult.Success ok:
                if (ok.ReturnValue.HasValue)
                {
                    var formatted = await roslyn.PrettyPrintAsync(ok.ReturnValue.Value, level);
                    console.Write(formatted);
                }
                console.WriteLine();
                break;
            case EvaluationResult.Error err:
                var formattedError = await roslyn.PrettyPrintAsync(err.Exception, level);

                var panel = new Panel(formattedError.ToParagraph())
                {
                    Header = new PanelHeader(err.Exception.GetType().Name, Justify.Center),
                    BorderStyle = new Style(foreground: Color.Red)
                };
                console.WriteError(panel, formattedError.ToString());
                console.WriteLine();
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
    // For .NET targets
    RemoteApp remoteApp = RemoteAppFactory.Connect(""MyDotNetTarget.exe"", RuntimeType.Managed);
    // For MSVC C++ target
    RemoteApp remoteApp = RemoteAppFactory.Connect(""MyNativeTarget.exe"", RuntimeType.Unmanaged);
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
RemoteObject can do everything too but it's required to learn its specific 
methods instead of writing ""trivial"" C# statements, like in the dynamic 
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
read more at the GitHub repo specified in the beginning of this help message.
"
        );
    }

    private void PrintHelp(KeyBindings keyBindings, KeyPressPatterns submitPromptDetailedKeys)
    {
        var newLineBindingName = KeyPressPatternToString(keyBindings.NewLine.DefinedPatterns ?? []);
        var submitPromptName = KeyPressPatternToString((keyBindings.SubmitPrompt.DefinedPatterns ?? []).Except(submitPromptDetailedKeys.DefinedPatterns ?? []));
        var submitPromptDetailedName = KeyPressPatternToString(submitPromptDetailedKeys.DefinedPatterns ?? []);

        console.WriteLine(FormattedStringParser.Parse($"""
More details and screenshots are available at
[blue]https://github.com/waf/CSharpRepl/blob/main/README.md [/]

[underline]Evaluating Code[/]
Type C# code at the prompt and press:
  - {submitPromptName} to run it and get result printed,
  - {submitPromptDetailedName} to run it and get result printed with more details (member info, stack traces, etc.),
  - {newLineBindingName} to insert a newline (to support multiple lines of input).
If the code isn't a complete statement, pressing [green]Enter[/] will insert a newline.

[underline]Adding References[/]
Use the {Reference()} command to add reference to:
  - assembly ({Reference("AssemblyName")} or {Reference("path/to/assembly.dll")}),
  - NuGet package ({Reference("nuget: PackageName")} or {Reference("nuget: PackageName, version")}),
  - project ({Reference("path/to/my.csproj")} or {Reference("path/to/my.sln")}).

Use {Preprocessor("#load", "path-to-file")} to evaluate C# stored in files (e.g. csx files). This can
be useful, for example, to build a [{ToColor("string")}].profile.csx[/] that includes libraries you want
to load.

[underline]Exploring Code[/]
  - [green]{"F1"}[/]:  when the caret is in a type or member, open the corresponding MSDN documentation.
  - [green]{"F9"}[/]:  show the IL (intermediate language) for the current statement.
  - [green]{"F12"}[/]: open the type's source code in the browser, if the assembly supports Source Link.

[underline]Configuration Options[/]
All configuration, including theming, is done at startup via command line flags.
Run [green]--help[/] at the command line to view these options.
"""
        ));

        string Reference(string? argument = null) => Preprocessor("#r", argument);

        string Link(string url) =>
            PromptConfiguration.HasUserOptedOutFromColor
            ? url
            : AnsiColor.Green.GetEscapeSequence() + url + AnsiEscapeCodes.Reset;

        string Code(string code) =>
            PromptConfiguration.HasUserOptedOutFromColor
            ? code
            : AnsiColor.Green.GetEscapeSequence() + code + AnsiEscapeCodes.Reset;

        string Preprocessor(string keyword, string? argument = null)
        {
            var highlightedKeyword = $"[{ToColor("preprocessor keyword")}]{keyword}[/]";
            var highlightedArgument = argument is null ? "" : $" [{ToColor("string")}]\"{argument}\"[/]";
            return highlightedKeyword + highlightedArgument;
        }

        string ToColor(string classification) => roslyn!.ToColor(classification).ToString();

        static string KeyPressPatternToString(IEnumerable<KeyPressPattern> patterns)
        {
            var values = patterns.ToList();
            return values.Count > 0 ?
                string.Join(" or ", values.Select(pattern => $"[green]{pattern.GetStringValue()}[/]")) :
               "[red]<undefined>[/]";
        }
    }

    private static string Help =>
        PromptConfiguration.HasUserOptedOutFromColor
        ? @"""help"""
        : AnsiColor.Green.GetEscapeSequence() + "help" + AnsiEscapeCodes.Reset;

    private string HelpRemote =>
        PromptConfiguration.HasUserOptedOutFromColor
        ? @"""help_remote"""
        : AnsiColor.Green.GetEscapeSequence() + "help_remote" + AnsiEscapeCodes.Reset;

    private string Exit =>
        PromptConfiguration.HasUserOptedOutFromColor
        ? @"""exit"""
        : AnsiColor.BrightRed.GetEscapeSequence() + "exit" + AnsiEscapeCodes.Reset;

    private static string Clear =>
        PromptConfiguration.HasUserOptedOutFromColor
        ? @"""clear"""
        : AnsiColor.BrightBlue.GetEscapeSequence() + "clear" + AnsiEscapeCodes.Reset;
}
