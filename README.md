# RemoteNET REPL

A command line C# <a href="https://en.wikipedia.org/wiki/Read%E2%80%93eval%E2%80%93print_loop" target="_blank"><abbr title="Read Eval Print Loop">REPL</abbr></a> for the rapid experimentation and exploration for the <a href="https://github.com/theXappy/RemoteNET">RemoteNET library</a>.  
It's based on <a href="https://github.com/waf/CSharpRepl">waf/CSharpRepl</a> which gives it many great features like:  
Support for intellisense, installing NuGet packages, referencing local .NET projects and assemblies and more.

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
