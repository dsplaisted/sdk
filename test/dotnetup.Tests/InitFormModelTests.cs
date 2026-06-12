// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Tools.Bootstrapper.Commands.Init.Form;

namespace Microsoft.DotNet.Tools.Dotnetup.Tests;

public class InitFormModelTests
{
    // Field order in the sample form.
    private const int ChannelField = 0;
    private const int ModeField = 1;
    private const int MigrateField = 2;

    // Mode choice order: Isolation, Terminal Profile, Replacement.
    private const int ModeIsolation = 0;
    private const int ModeTerminalProfile = 1;
    private const int ModeReplacement = 2;

    private const int MigrateYes = 0;
    private const int MigrateNo = 1;

    [Fact]
    public void ModeChoices_AreOrderedIsolationProfileReplacement_WithProfileDefault()
    {
        var model = InitFormModel.CreateSample();
        var mode = model.Fields[ModeField];

        mode.Choices[ModeIsolation].Title.Should().StartWith("Isolation");
        mode.Choices[ModeTerminalProfile].Title.Should().StartWith("Terminal Profile");
        mode.Choices[ModeReplacement].Title.Should().StartWith("Replacement");
        mode.DefaultIndex.Should().Be(ModeTerminalProfile);
    }

    [Fact]
    public void Detail_TerminalProfileMode_ShowsInstallAndProfilePaths()
    {
        var model = InitFormModel.CreateSample();

        FieldDetail detail = model.BuildDetail(model.Fields[ModeField], ModeTerminalProfile);

        detail.Lines.Should().Contain(l => l.Label == "Installs to:" && l.Value == model.InstallPath);
        detail.Lines.Should().Contain(l => l.Label == "Edits profile:" && l.Value == model.ProfilePath);
    }

    [Fact]
    public void Detail_IsolationMode_ShowsInstallPathButNoProfile()
    {
        var model = InitFormModel.CreateSample();

        FieldDetail detail = model.BuildDetail(model.Fields[ModeField], ModeIsolation);

        detail.Lines.Should().Contain(l => l.Label == "Installs to:");
        detail.Lines.Should().NotContain(l => l.Label == "Edits profile:");
    }

    [Fact]
    public void Detail_ReplacementMode_NotesPathReplacement()
    {
        var model = InitFormModel.CreateSample();

        FieldDetail detail = model.BuildDetail(model.Fields[ModeField], ModeReplacement);

        detail.Lines.Should().Contain(l => l.Label.Contains("Replaces dotnet"));
    }

    [Fact]
    public void DefaultChannel_IsLatest_WithNoGlobalJsonOption_AndInlineHelp()
    {
        var model = InitFormModel.CreateSample();
        var channel = model.Fields[ChannelField];

        channel.DefaultIndex.Should().Be(0);
        channel.Choices[channel.DefaultIndex].Title.Should().Be("latest");
        channel.Choices.Should().NotContain(c => c.Title.Contains("global.json"));
        channel.InlineHelp.Should().BeTrue();
    }

    [Fact]
    public void Detail_MigrateYes_ShowsSdkAndRuntimeSummaryLines()
    {
        var model = InitFormModel.CreateSample();

        FieldDetail detail = model.BuildDetail(model.Fields[MigrateField], MigrateYes);

        detail.Lines.Should().HaveCount(2);
        detail.Lines[0].Label.Should().Be("SDKs:");
        detail.Lines[0].Value.Should().Contain("and 4 more"); // 7 SDKs, 3 shown
        detail.Lines[1].Label.Should().Be("Runtimes:");
        detail.Lines[1].Value.Should().Contain("and 3 more"); // 6 runtimes, 3 shown
    }

    [Fact]
    public void Detail_MigrateNo_ShowsNoInstallList()
    {
        var model = InitFormModel.CreateSample();

        FieldDetail detail = model.BuildDetail(model.Fields[MigrateField], MigrateNo);

        detail.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Detail_Channel_HasHelpTextAndNoDerivedLines()
    {
        var model = InitFormModel.CreateSample();

        FieldDetail detail = model.BuildDetail(model.Fields[ChannelField], 0);

        detail.HelperText.Should().NotBeNullOrWhiteSpace();
        detail.Lines.Should().BeEmpty();
    }
}
