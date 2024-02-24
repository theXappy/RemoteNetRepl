# RemoteNET REPL

A command line C# <a href="https://en.wikipedia.org/wiki/Read%E2%80%93eval%E2%80%93print_loop" target="_blank"><abbr title="Read Eval Print Loop">REPL</abbr></a> for the rapid experimentation and exploration for the <a href="https://github.com/theXappy/RemoteNET">RemoteNET library</a>.  
It's based on <a href="https://github.com/waf/CSharpRepl">waf/CSharpRepl</a> which gives it many great features like:  
Support for intellisense, installing NuGet packages, referencing local .NET projects and assemblies and more.

<div align="center">
  <a href="https://raw.githubusercontent.com/waf/CSharpRepl/main/.github/readme_assets/csharprepl.mp4">
    <img src="https://raw.githubusercontent.com/waf/CSharpRepl/main/.github/readme_assets/csharprepl.png" alt="C# REPL screenshot" style="max-width:80%;">
  </a>
  <p align="center"><i>(click to view animation)</i></p>
</div>

C# REPL provides the following features:

- Syntax highlighting via ANSI escape sequences
- Intellisense with documentation and overload navigation
- Automatic formatting of typed input
- Nuget package installation
- Reference local assemblies, solutions, and projects
- Dump and explore objects with syntax highlighting and rich Spectre.Console formatting
- OpenAI integration (bring your own API key)
- Navigate to source via Source Link
- IL disassembly (both Debug and Release mode)
- Fast and flicker-free rendering. A "diff" algorithm is used to only render what's changed.

## Installation

C# REPL is a .NET 8 global tool, and runs on Windows, Mac OS, and Linux. It can be installed [from NuGet](https://www.nuget.org/packages/CSharpRepl) via:

```console
dotnet tool install -g csharprepl
```

If you're running on Mac OS Catalina (10.15) or later, make sure you follow any additional directions printed to the screen. You may need to update your PATH variable in order to use .NET global tools.

After installation is complete, run `csharprepl` to begin. C# REPL can be updated via `dotnet tool update -g csharprepl`.

## Themes and Colors

The default theme uses the same colors as Visual Studio dark mode, and custom themes can be created using a [`theme.json`](https://github.com/waf/CSharpRepl/blob/main/CSharpRepl/themes/dracula.json) file. Additionally, your terminal's colors can be used by supplying the `--useTerminalPaletteTheme` command line option. To completely disable colors, set the NO_COLOR environment variable.

## Usage

1. Clone this repo
2. Open `RemoteNetRepl.sln` in Visual Studio 2022
3. Compile & Run

### Evaluating Code
If you aren't familiar with REPLs, please read <a href="https://github.com/waf/CSharpRepl#evaluating-code">CSharpRepl's "Evaluating Code" section</a>.  

When launching this project you get a C# REPL with the RemoteNET library already referenced.    
Here's an example of how to use **RemoteNetRepl** to:  
1. Connect to a .NET app
2. Get some of its objects
3. Read some of their properties (`ConnetionString`s of every `SqlConnection` object):

```csharp
> var app = RemoteNET.RemoteApp.Connect(Process.GetProcessesByName("Target_App_Name").Single());

> var sqlConCandidates = app.QueryInstances("*SqlConnection");

> foreach (CandidateObject candidate in sqlConCandidates)
  {
    RemoteObject remoteSqlConnection = remoteApp.GetRemoteObject(candidate);
    dynamic dynamicSqlConnection = remoteSqlConnection.Dynamify();
    Console.WriteLine("ConnectionString: " + dynamicSqlConnection.ConnectionString);
  }

ConnectionString: SERVER=localhost;DATABASE=tree;UID=root;PASSWORD=you_found_me;Min Pool Size = 0;Max Pool Size=200
```

You can also read their private fields, invoke their functions and subscribe to their events.  
Details on how to do those are available in the RemoteNET's README.
