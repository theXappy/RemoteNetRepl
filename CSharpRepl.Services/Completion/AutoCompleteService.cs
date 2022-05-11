// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CSharpRepl.Services.Extensions;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Scripting;
using CSharpRepl.Services.SyntaxHighlighting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Caching.Memory;
using PrettyPrompt.Highlighting;

using PrettyPromptCompletionItem = PrettyPrompt.Completion.CompletionItem;

namespace CSharpRepl.Services.Completion;

public record CompletionItemWithDescription(CompletionItem Item, FormattedString DisplayText, PrettyPromptCompletionItem.GetExtendedDescriptionHandler GetDescriptionAsync);

internal sealed class AutoCompleteService
{
    private const string CacheKeyPrefix = "AutoCompleteService_";

    private readonly SyntaxHighlighter highlighter;
    private readonly IMemoryCache cache;
    private readonly Configuration configuration;
    private readonly RoslynServices parent;

    public AutoCompleteService(SyntaxHighlighter highlighter, IMemoryCache cache, Configuration configuration, RoslynServices parent)
    {
        this.highlighter = highlighter;
        this.cache = cache;
        this.configuration = configuration;
        this.parent = parent;
    }

    private string? HackyVariableNameParser(string text, int caret)
    {
        if (text.Length == 0 || caret > text.Length)
            return null;

        if (text[caret - 1] != '.')
        {
            // Not an accessor expression
            return null;
        }

        int pos = caret - 2;
        int length = 0;
        while (pos >= 0)
        {
            char curr = text[pos];
            if (char.IsLetter(curr) || curr == '_' || curr == '@')
            {
                pos--;
                length++;
                continue;
            }
            if (char.IsDigit(curr))
            {
                if (pos == 0)
                {
                    // Seems like this variable name start at begining of the line AND witha digit. this is illegal in C#.
                    return null;
                }
                char prevChar = text[pos - 1];
                if (char.IsDigit(prevChar) || char.IsLetter(prevChar) || prevChar == '_' || prevChar == '@')
                {
                    pos--;
                    length++;
                    continue;
                }
                else
                {
                    // Some punctionation/ whitespace/ etc before digit (which start the var name). This is illegal in C#.
                    return null;
                }
            }
            if (curr == '.')
            {
                // Actually what we have in hand is a MEMBER of another var so we can't do anything with it.
                return null;
            }

            // Arrived at a char which can't be in variable name. Assume we are done
            break;
        }

        return text.Substring(pos + 1, length);
    }

    public async Task<CompletionItemWithDescription[]> Complete(Document document, string text, int caret)
    {
        List<CompletionItemWithDescription> dynamicallyAssociatedMembers = new List<CompletionItemWithDescription>();
        string? varName = HackyVariableNameParser(text, caret);
        if (varName != null)
        {
            ScriptRunner? stolenRunner = parent.Steal<ScriptRunner?>("scriptRunner");
            if (stolenRunner != null)
            {
                ScriptState<object>? state = stolenRunner.Steal<ScriptState<object>?>("state");
                if (state != null)
                {
                    ScriptVariable? variable = state.Variables.FirstOrDefault(x => x.Name == varName);
                    Console.WriteLine($"@@@ Complete called for variable `{variable.Type}`");
                    if (variable?.Value is RemoteNET.Internal.DynamicRemoteObject dro)
                    {
                        string? shortTypeName = dro.GetType().FullName;
                        shortTypeName = shortTypeName?.Substring(shortTypeName.LastIndexOf('.') + 1);

                        System.Reflection.MemberInfo[]? members = dro.GetType().GetMembers();
                        List<string> seenMethods = new List<string>();
                        foreach (System.Reflection.MemberInfo? member in members)
                        {
                            string name = member.Name;
                            string desc = "";
                            switch (member)
                            {
                                case FieldInfo fi:
                                    desc = $"{fi.GetType().FullName} {shortTypeName}.{name}";
                                    desc += "\n\n";
                                    desc += $"NOTE: This is a proxy for a remote {fi.FieldType.ToString().ToLower()}.\n" +
                                            "Assume all types will be proxies.\n";
                                    break;
                                case PropertyInfo pi:
                                    desc = $"{pi.GetType().FullName} {shortTypeName}.{name}";
                                    desc += " { ";
                                    desc += (pi.GetMethod != null) ? "get; " : String.Empty;
                                    desc += (pi.SetMethod != null) ? "set; " : String.Empty;
                                    desc += " }";
                                    desc += "\n\n";
                                    desc += $"NOTE: This is a proxy for a remote {pi.PropertyType.ToString().ToLower()}.\n" +
                                            "Assume all types will be proxies.\n";
                                    break; ;
                                case MethodInfo mi:
                                    if (!seenMethods.Contains(mi.Name))
                                    {
                                        var firstOverload = mi;
                                        string parameters = string.Join(", ", firstOverload.GetParameters().Select(pi => $"{pi.ParameterType.FullName} {pi.Name}").ToArray());
                                        string returnType = firstOverload.ReturnType.FullName;
                                        desc = $"{returnType} {name}({parameters})";
                                        int otherOverloadsCount = members.OfType<MemberInfo>().Where(mi2 => mi2.Name == mi.Name).Count() - 1;
                                        if (otherOverloadsCount > 0)
                                        {
                                            desc += $" ( +{otherOverloadsCount} overloads)";
                                        }
                                        desc += "\n\n";
                                        desc += "NOTE: This is a proxy for a remote function.\n" +
                                                "Assume all types will be proxies.\n";
                                    }
                                    break;
                            }


                            System.Collections.Immutable.ImmutableDictionary<string, string> b = System.Collections.Immutable.ImmutableDictionary<string, string>.Empty;
                            b = b.Add("SymbolName", name);
                            b = b.Add("ContextPosition", caret.ToString());
                            b = b.Add("InsertionText", name);
                            b = b.Add("ShouldProvideParenthesisCompletion", "True");
                            b = b.Add("SymbolKind", "9");

                            System.Collections.Immutable.ImmutableArray<string> iaBuilder = System.Collections.Immutable.ImmutableArray<string>.Empty;
                            iaBuilder = iaBuilder.Add("Method");
                            iaBuilder = iaBuilder.Add("Public");

                            TextSpan t = new TextSpan(caret, name.Length);
                            var theCreatFuncTheyTriedToHide = typeof(CompletionItem).GetMethod("Create", (BindingFlags)0xffff, new Type[]
                            {
                                typeof(string) ,
                                typeof(string) ,
                                typeof(string) ,
                                typeof(TextSpan) ,
                                typeof(System.Collections.Immutable.ImmutableDictionary<string, string> ) ,
                                typeof(System.Collections.Immutable.ImmutableArray<string>),
                                typeof(CompletionItemRules)
                            });
                            var compItem = theCreatFuncTheyTriedToHide.Invoke(null, new object[] { name, name, name, t, b, iaBuilder, null });

                            Lazy<Task<string>> lazyTask = new Lazy<Task<string>>(() => Task.FromResult(desc));
                            FormattedString formattedDesc = new FormattedString(desc, ConsoleFormat.None);
                            var compItemWithDesc = new CompletionItemWithDescription(compItem as CompletionItem, name, (cancelToken) => Task.FromResult(formattedDesc));
                            dynamicallyAssociatedMembers.Add(compItemWithDesc);
                        }
                    }
                }
            }
        }

        var cacheKey = CacheKeyPrefix + document.Name + text + caret;
        if (text != string.Empty && cache.Get<CompletionItemWithDescription[]>(cacheKey) is CompletionItemWithDescription[] cached)
            return cached;

        var completionService = CompletionService.GetService(document);
        if (completionService is null) return Array.Empty<CompletionItemWithDescription>();

        var completions = await completionService
            .GetCompletionsAsync(document, caret)
            .ConfigureAwait(false);

        var completionsWithDescriptions = completions?.Items
            .Select(item => new CompletionItemWithDescription(item, GetDisplayText(item), cancellationToken => GetExtendedDescriptionAsync(completionService, document, item, highlighter)))
            .ToArray() ?? Array.Empty<CompletionItemWithDescription>();

        // Add auto complete info for dynamically associated members of DynamicRemoteObject
        completionsWithDescriptions = completionsWithDescriptions.Concat(dynamicallyAssociatedMembers).ToArray();

        cache.Set(cacheKey, completionsWithDescriptions, DateTimeOffset.Now.AddMinutes(1));

        return completionsWithDescriptions;

        FormattedString GetDisplayText(CompletionItem item)
        {
            var text = item.DisplayTextPrefix + item.DisplayText + item.DisplayTextSuffix;
            if (item.Tags.Length > 0)
            {
                var classification = RoslynExtensions.TextTagToClassificationTypeName(item.Tags.First());
                if (classification is not null &&
                    highlighter.TryGetColor(classification, out var color))
                {
                    Span<char> prefix = stackalloc char[3];
                    if (configuration.UseUnicode)
                    {
                        var symbol = classification switch
                        {
                            ClassificationTypeNames.Keyword => "🔑",
                            ClassificationTypeNames.MethodName or ClassificationTypeNames.ExtensionMethodName => "🟣",
                            ClassificationTypeNames.PropertyName => "🟡",
                            ClassificationTypeNames.FieldName or ClassificationTypeNames.ConstantName or ClassificationTypeNames.EnumMemberName => "🔵",
                            ClassificationTypeNames.EventName => "⚡",
                            ClassificationTypeNames.ClassName or ClassificationTypeNames.RecordClassName => "🟨",
                            ClassificationTypeNames.InterfaceName => "🔷",
                            ClassificationTypeNames.StructName or ClassificationTypeNames.RecordStructName => "🟦",
                            ClassificationTypeNames.EnumName => "🟧",
                            ClassificationTypeNames.DelegateName => "💼",
                            ClassificationTypeNames.NamespaceName => "⬜",
                            ClassificationTypeNames.TypeParameterName => "⬛",
                            _ => "⚫",
                        };

                        Debug.Assert(symbol.Length <= prefix.Length);
                        symbol.CopyTo(prefix);
                        prefix[symbol.Length] = ' ';
                        prefix = prefix[..(symbol.Length + 1)];
                    }
                    else
                    {
                        prefix = Span<char>.Empty;
                    }

                    return new FormattedString($"{prefix}{text}", new FormatSpan(prefix.Length, text.Length, new ConsoleFormat(Foreground: color)));
                }
            }
            return text;
        }
    }

    private static async Task<FormattedString> GetExtendedDescriptionAsync(CompletionService completionService, Document document, CompletionItem item, SyntaxHighlighter highlighter)
    {
        var description = await completionService.GetDescriptionAsync(document, item);
        if (description is null) return string.Empty;

        var stringBuilder = new FormattedStringBuilder();
        foreach (var taggedText in description.TaggedParts)
        {
            var classification = RoslynExtensions.TextTagToClassificationTypeName(taggedText.Tag);
            if (classification is not null &&
                highlighter.TryGetColor(classification, out var color))
            {
                stringBuilder.Append(taggedText.Text, new FormatSpan(0, taggedText.Text.Length, new ConsoleFormat(Foreground: color)));
            }
            else
            {
                stringBuilder.Append(taggedText.Text);
            }
        }
        return stringBuilder.ToFormattedString();
    }
}