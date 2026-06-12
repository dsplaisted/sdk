// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Tools.Bootstrapper.Commands.Init.Form;

namespace Microsoft.DotNet.Tools.Dotnetup.Tests;

public class InitFormModelTests
{
    // Field order in the sample form.
    private const int ChannelField = 0;
    private const int ProfileField = 1;
    private const int PathField = 2;
    private const int MigrateField = 3;

    // Profile choice order: Yes (recommended), No.
    private const int ProfileYes = 0;
    private const int ProfileNo = 1;

    // Path choice order: No (recommended), Yes.
    private const int PathNo = 0;
    private const int PathYes = 1;

    private const int MigrateYes = 0;
    private const int MigrateNo = 1;

    [Fact]
    public void ProfileChoices_AreYesNo_WithYesDefault()
    {
        var model = InitFormModel.CreateSample();
        var profile = model.Fields[ProfileField];

        profile.Label.Should().Be("Modify shell profile");
        profile.Choices[ProfileYes].Title.Should().Be("Yes");
        profile.Choices[ProfileNo].Title.Should().Be("No");
        profile.DefaultIndex.Should().Be(ProfileYes);
    }

    [Fact]
    public void PathChoices_AreNoYes_WithNoDefault()
    {
        var model = InitFormModel.CreateSample();
        var path = model.Fields[PathField];

        path.Label.Should().Be("Replace system PATH");
        path.Choices[PathNo].Title.Should().Be("No");
        path.Choices[PathYes].Title.Should().Be("Yes");
        path.DefaultIndex.Should().Be(PathNo);
    }

    [Fact]
    public void PathField_IsVisibleOnlyWhenProfileIsYes()
    {
        var model = InitFormModel.CreateSample();
        var profile = model.Fields[ProfileField];
        var path = model.Fields[PathField];

        profile.SelectChoice(ProfileYes);
        path.IsVisible.Should().BeTrue();

        profile.SelectChoice(ProfileNo);
        path.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void Detail_ProfileYes_ShowsInstallAndProfilePaths()
    {
        var model = InitFormModel.CreateSample();

        FieldDetail detail = model.BuildDetail(model.Fields[ProfileField], ProfileYes);

        detail.Lines.Should().Contain(l => l.Label == "Installs to:" && l.Value == model.InstallPath);
        detail.Lines.Should().Contain(l => l.Label == "Edits profile:" && l.Value == model.ProfilePath);
    }

    [Fact]
    public void Detail_ProfileNo_ShowsInstallPathButNoProfile()
    {
        var model = InitFormModel.CreateSample();

        FieldDetail detail = model.BuildDetail(model.Fields[ProfileField], ProfileNo);

        detail.Lines.Should().Contain(l => l.Label == "Installs to:");
        detail.Lines.Should().NotContain(l => l.Label == "Edits profile:");
    }

    [Fact]
    public void Detail_Path_HasHelpTextAndNoDerivedLines()
    {
        var model = InitFormModel.CreateSample();

        FieldDetail yes = model.BuildDetail(model.Fields[PathField], PathYes);
        FieldDetail no = model.BuildDetail(model.Fields[PathField], PathNo);

        yes.HelperText.Should().NotBeNullOrWhiteSpace();
        yes.Lines.Should().BeEmpty();
        no.Lines.Should().BeEmpty();
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
