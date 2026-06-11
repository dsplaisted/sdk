// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Tools.Bootstrapper.Commands.Init.Form;

namespace Microsoft.DotNet.Tools.Dotnetup.Tests;

public class FormSelectorStateTests
{
    private static List<FormField> SampleFields() =>
    [
        new FormField("Channel", [new FieldChoice("a", ""), new FieldChoice("b", ""), new FieldChoice("c", "")], defaultIndex: 0),
        new FormField("Mode", [new FieldChoice("x", ""), new FieldChoice("y", "")], defaultIndex: 0),
    ];

    [Fact]
    public void InitialFocus_IsAcceptRow()
    {
        var state = new FormSelectorState(SampleFields());

        state.Mode.Should().Be(FormMode.Form);
        state.FocusedRow.Should().Be(state.AcceptRow);
        state.IsAcceptFocused.Should().BeTrue();
    }

    [Fact]
    public void Enter_OnAccept_CompletesAsDone()
    {
        var state = new FormSelectorState(SampleFields());

        state.Enter();

        state.IsDone.Should().BeTrue();
    }

    [Fact]
    public void MoveUp_FromAccept_FocusesLastField_AndClampsAtTop()
    {
        var fields = SampleFields();
        var state = new FormSelectorState(fields);

        state.MoveUp();
        state.FocusedRow.Should().Be(1);
        state.FocusedField.Should().BeSameAs(fields[1]);

        state.MoveUp();
        state.FocusedRow.Should().Be(0);

        // Clamps at the top.
        state.MoveUp();
        state.FocusedRow.Should().Be(0);
    }

    [Fact]
    public void MoveDown_ClampsAtAcceptRow()
    {
        var state = new FormSelectorState(SampleFields());
        state.MoveUp(); // to last field
        state.MoveUp(); // to first field

        state.MoveDown();
        state.MoveDown();
        state.FocusedRow.Should().Be(state.AcceptRow);

        // Clamps at the bottom.
        state.MoveDown();
        state.FocusedRow.Should().Be(state.AcceptRow);
    }

    [Fact]
    public void Enter_OnField_OpensEditorAtCurrentSelection()
    {
        var fields = SampleFields();
        var state = new FormSelectorState(fields);
        state.MoveUp();
        state.MoveUp(); // focus first field

        state.Enter();

        state.Mode.Should().Be(FormMode.EditingField);
        state.EditChoiceIndex.Should().Be(fields[0].SelectedIndex);
    }

    [Fact]
    public void EditThenEnter_CommitsSelection_AndReturnsToForm()
    {
        var fields = SampleFields();
        var state = new FormSelectorState(fields);
        state.MoveUp();
        state.MoveUp(); // first field
        state.Enter();  // open editor (at index 0)

        state.MoveDown(); // highlight choice 1
        state.MoveDown(); // highlight choice 2
        state.Enter();    // commit

        state.Mode.Should().Be(FormMode.Form);
        fields[0].SelectedIndex.Should().Be(2);
        fields[0].IsChangedFromDefault.Should().BeTrue();
        state.EditChoiceIndex.Should().Be(-1);
    }

    [Fact]
    public void EditChoiceNavigation_ClampsWithinChoices()
    {
        var fields = SampleFields();
        var state = new FormSelectorState(fields);
        state.MoveUp();
        state.MoveUp();
        state.Enter(); // editing first field (3 choices), at index 0

        state.MoveUp(); // clamp at 0
        state.EditChoiceIndex.Should().Be(0);

        state.MoveDown();
        state.MoveDown();
        state.MoveDown(); // clamp at last (2)
        state.EditChoiceIndex.Should().Be(2);
    }

    [Fact]
    public void Cancel_WhileEditing_DiscardsAndKeepsValue()
    {
        var fields = SampleFields();
        var state = new FormSelectorState(fields);
        state.MoveUp();
        state.MoveUp();
        state.Enter();    // edit first field
        state.MoveDown(); // highlight a different choice

        state.Cancel();

        state.Mode.Should().Be(FormMode.Form);
        fields[0].SelectedIndex.Should().Be(0); // unchanged
        state.FocusedRow.Should().Be(0);        // focus stays on the field
    }

    [Fact]
    public void Cancel_InFormMode_IsNoOp()
    {
        var state = new FormSelectorState(SampleFields());

        state.Cancel();

        state.Mode.Should().Be(FormMode.Form);
        state.IsDone.Should().BeFalse();
    }
}
