// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;

namespace Microsoft.DotNet.Tools.Bootstrapper.Commands.Init.Form;

/// <summary>
/// Prototype command that renders the interactive init "form" UI. UI-only: it shows a mock form and
/// prints the chosen settings; it is not wired to real install/config logic.
/// </summary>
internal sealed class InitFormCommand : CommandBase
{
    public InitFormCommand(ParseResult parseResult)
        : base(parseResult)
    {
    }

    protected override string GetCommandName() => "initform";

    protected override void ExecuteCore()
    {
        InitFormModel model = InitFormModel.CreateSample();
        InteractiveFormSelector.Show(model);
    }
}
