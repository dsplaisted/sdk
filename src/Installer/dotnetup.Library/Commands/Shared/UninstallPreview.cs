// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using Microsoft.DotNet.Tools.Bootstrapper.Commands.List;
using Spectre.Console;

namespace Microsoft.DotNet.Tools.Bootstrapper.Commands.Shared;

/// <summary>Renders affected requirements and the cancel-by-default physical removal choice.</summary>
internal static class UninstallPreview
{
    internal static void DisplayRequiredBy(DotnetRootEntry root, Installation target, List<InstallSpec> specs,
        IReadOnlyDictionary<InstallSpec, InstallSpecResolution>? requirements = null)
    {
        specs = specs.DistinctBy(s => (s.Component, s.VersionOrChannel, s.InstallSource, s.GlobalJsonPath)).ToList();
        AnsiConsole.WriteLine(string.Format(CultureInfo.InvariantCulture,
            specs.Count == 1 ? Strings.UninstallRequiredByOne : Strings.UninstallRequiredByMany,
            InstallationListRenderer.ComponentName(target.Component), target.Version));
        DisplaySpecs(root, specs, requirements);
        AnsiConsole.WriteLine(target.Component == InstallComponent.SDK
            ? specs.Count == 1 ? Strings.UninstallReinstallWarningOne : Strings.UninstallReinstallWarningMany
            : specs.Count == 1 ? Strings.UninstallRuntimeReinstallWarningOne : Strings.UninstallRuntimeReinstallWarningMany);
        var repositories = specs.Count(s => s.InstallSource == InstallSource.GlobalJson);
        if (repositories > 0)
        {
            AnsiConsole.WriteLine(specs.Count == 1 ? Strings.UninstallSingleRepositorySpecWarning
                : repositories == 1 ? Strings.UninstallRepositoryWarningOne : Strings.UninstallRepositoryWarningMany);
        }
    }

    internal static void DisplayRetained(DotnetRootEntry root, Installation target, List<InstallSpec> specs,
        IReadOnlyDictionary<InstallSpec, InstallSpecResolution>? requirements = null)
    {
        specs = specs.DistinctBy(s => (s.Component, s.VersionOrChannel, s.InstallSource, s.GlobalJsonPath)).ToList();
        AnsiConsole.WriteLine(string.Format(CultureInfo.InvariantCulture,
            specs.Count == 1 ? Strings.UninstallRetainedByOne : Strings.UninstallRetainedByMany,
            InstallationListRenderer.ComponentName(target.Component), target.Version));
        DisplaySpecs(root, specs, requirements);
    }

    private static void DisplaySpecs(DotnetRootEntry root, List<InstallSpec> specs,
        IReadOnlyDictionary<InstallSpec, InstallSpecResolution>? requirements)
    {
        var pairs = specs.Select(s => (Spec: s, Info: new InstallSpecInfo
        {
            Component = s.Component, VersionOrChannel = s.VersionOrChannel, Source = s.InstallSource,
            GlobalJsonPath = s.GlobalJsonPath, MinimumVersion = s.MinimumVersion,
            AllowPrerelease = s.AllowPrerelease, InstallRoot = root.Path, Architecture = root.Architecture
        })).ToList();
        var evaluations = requirements is null ? null : pairs.ToDictionary(pair => pair.Info, pair => requirements[pair.Spec]);
        var installations = root.Installations.Where(i => i.RemovalPending ||
            Directory.Exists(DotnetupSharedManifest.GetComponentDirectory(root.Path, i)))
            .Select(i => new InstallationInfo
            {
                Component = i.Component, Version = i.Version, InstallRoot = root.Path, Architecture = root.Architecture,
                IsValid = i.RemovalPending ? false : null
            }).ToList();
        var rows = InstallationListRenderer.CreateRowsForRoot(pairs.Select(pair => pair.Info), installations, evaluations)
            .Where(r => r.Spec is not null)
            .DistinctBy(r => (r.Component, r.SpecDisplay, r.SourceDisplay)).ToList();
        AnsiConsole.Write(InstallationListRenderer.CreateTable(rows));
    }

    internal static UninstallAction ChooseAction(Installation target, int specCount)
    {
        var choices = new[]
        {
            string.Format(CultureInfo.InvariantCulture,
                specCount == 1 ? Strings.UninstallRemoveChoiceOne : Strings.UninstallRemoveChoiceMany,
                InstallationListRenderer.ComponentName(target.Component), target.Version),
            string.Format(CultureInfo.InvariantCulture, Strings.UninstallKeepChoice,
                InstallationListRenderer.ComponentName(target.Component), target.Version),
            Strings.UninstallCancelChoice
        };
        AnsiConsole.WriteLine(Strings.UninstallChooseAction);
        var selected = 2;
        if (Console.IsInputRedirected)
        {
            for (var index = 0; index < choices.Length; index++)
            {
                AnsiConsole.WriteLine($"{(index == selected ? ">" : " ")} {index + 1}. {choices[index]}");
            }
            while (true)
            {
                var input = Console.ReadLine()?.Trim();
                if (input is null or "" or "3" or "\u001b")
                {
                    break;
                }
                if (input is "1" or "2")
                {
                    selected = input == "1" ? 0 : 1;
                    break;
                }
                AnsiConsole.WriteLine(Strings.UninstallChooseNumber);
            }
        }
        else
        {
            Rows Render() => new(choices.Select((choice, index) =>
                new Text($"{(index == selected ? ">" : " ")} {choice}")));
            AnsiConsole.Live(Render()).AutoClear(true).Start(context =>
            {
                while (true)
                {
                    var key = Console.ReadKey(intercept: true).Key;
                    if (key == ConsoleKey.Escape)
                    {
                        selected = 2;
                        break;
                    }
                    if (key == ConsoleKey.Enter)
                    {
                        break;
                    }
                    if (key == ConsoleKey.UpArrow)
                    {
                        selected = (selected + 2) % 3;
                    }
                    else if (key == ConsoleKey.DownArrow)
                    {
                        selected = (selected + 1) % 3;
                    }
                    context.UpdateTarget(Render());
                }
            });
        }
        AnsiConsole.WriteLine(choices[selected]);
        return selected switch { 0 => UninstallAction.RemoveSpecs, 1 => UninstallAction.KeepSpecs, _ => UninstallAction.Cancel };
    }
}
