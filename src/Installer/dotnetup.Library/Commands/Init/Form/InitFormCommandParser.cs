// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;

namespace Microsoft.DotNet.Tools.Bootstrapper.Commands.Init.Form;

internal static class InitFormCommandParser
{
    private static readonly Command s_command = ConstructCommand();

    public static Command GetCommand() => s_command;

    private static Command ConstructCommand()
    {
        Command command = new("initform", "Prototype of the interactive init form UI (UI-only demo).");

        command.SetAction(parseResult => new InitFormCommand(parseResult).Execute());

        return command;
    }
}
