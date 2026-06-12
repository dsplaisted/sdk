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

    private static List<FormField> FieldsWithCustom() =>
    [
        new FormField(
            "Channel",
            [new FieldChoice("a", ""), new FieldChoice("custom", "", IsCustomInput: true)],
            defaultIndex: 0),
    ];

    [Fact]
    public void HighlightingCustomChoice_MakesItLiveForTyping()
    {
        var state = new FormSelectorState(FieldsWithCustom());
        state.MoveUp();   // focus the field
        state.Enter();    // edit (highlight starts at choice 0)
        state.MoveDown(); // highlight the custom choice

        state.Mode.Should().Be(FormMode.EditingField);
        state.IsCustomChoiceHighlighted.Should().BeTrue();
        state.CustomTextBuffer.Should().BeEmpty();
    }

    [Fact]
    public void TypingThenEnter_CommitsCustomValue()
    {
        var fields = FieldsWithCustom();
        var state = new FormSelectorState(fields);
        state.MoveUp();
        state.Enter();
        state.MoveDown(); // highlight custom (now live)

        state.AppendChar('8');
        state.AppendChar('.');
        state.AppendChar('0');
        state.Backspace();
        state.AppendChar('1');
        state.Enter(); // commit "8.1"

        state.Mode.Should().Be(FormMode.Form);
        fields[0].CustomValue.Should().Be("8.1");
        fields[0].DisplayValue.Should().Be("8.1");
        fields[0].IsChangedFromDefault.Should().BeTrue();
    }

    [Fact]
    public void EnterWithEmptyCustomText_KeepsFieldOpen()
    {
        var fields = FieldsWithCustom();
        var state = new FormSelectorState(fields);
        state.MoveUp();
        state.Enter();
        state.MoveDown(); // highlight custom, nothing typed

        state.Enter();

        state.Mode.Should().Be(FormMode.EditingField);
        fields[0].CustomValue.Should().BeNull();
    }

    [Fact]
    public void Escape_RevertsCustomTextToValueWhenFieldWasOpened()
    {
        var fields = FieldsWithCustom();
        fields[0].SetCustomValue(1, "8.0"); // committed; LastCustomText = "8.0"
        var state = new FormSelectorState(fields);
        state.MoveUp();
        state.Enter();        // open field (snapshot "8.0"); custom highlighted, buffer "8.0"
        state.AppendChar('x'); // edit to "8.0x"

        state.Cancel();       // Esc

        state.Mode.Should().Be(FormMode.Form);
        fields[0].LastCustomText.Should().Be("8.0"); // reverted, not "8.0x"
    }

    [Fact]
    public void MovingOffCustom_SavesTypedText_WithoutEnter()
    {
        var fields = FieldsWithCustom();
        var state = new FormSelectorState(fields);
        state.MoveUp();
        state.Enter();
        state.MoveDown(); // highlight custom
        state.AppendChar('9');
        state.AppendChar('.');
        state.AppendChar('0');

        state.MoveUp();   // move off without Enter

        fields[0].LastCustomText.Should().Be("9.0"); // saved
        state.MoveDown(); // back to custom
        state.CustomTextBuffer.Should().Be("9.0");   // restored
    }

    [Fact]
    public void DeletingCustomText_ThenMoveOff_ReturnsToInitialState()
    {
        var fields = FieldsWithCustom();
        fields[0].SetCustomValue(1, "8.0"); // had a value; LastCustomText "8.0"
        var state = new FormSelectorState(fields);
        state.MoveUp();
        state.Enter();    // custom highlighted, buffer "8.0"
        state.Backspace();
        state.Backspace();
        state.Backspace(); // buffer now empty

        state.MoveUp();    // move off

        fields[0].LastCustomText.Should().BeEmpty(); // cleared -> initial placeholder state
    }

    [Fact]
    public void SelectingFixedChoice_ClearsPreviousCustomValue()
    {
        var fields = FieldsWithCustom();
        fields[0].SetCustomValue(1, "9.9");
        var state = new FormSelectorState(fields);
        state.MoveUp();
        state.Enter();   // edit (highlight starts at current selection = custom index 1)
        state.MoveUp();  // highlight choice 0 (fixed)
        state.Enter();   // commit fixed

        fields[0].SelectedIndex.Should().Be(0);
        fields[0].CustomValue.Should().BeNull();
        fields[0].DisplayValue.Should().Be("a");
    }

    [Fact]
    public void ReEditingCommittedCustom_SeedsBufferWithThatValue()
    {
        var fields = FieldsWithCustom();
        fields[0].SetCustomValue(1, "8.1");
        var state = new FormSelectorState(fields);
        state.MoveUp();  // focus field
        state.Enter();   // edit; highlight = custom (current selection), buffer seeded

        state.IsCustomChoiceHighlighted.Should().BeTrue();
        state.CustomTextBuffer.Should().Be("8.1");
    }

    [Fact]
    public void ArrowWhileTyping_RemembersText_AndNavigates()
    {
        var fields = FieldsWithCustom();
        var state = new FormSelectorState(fields);
        state.MoveUp();
        state.Enter();
        state.MoveDown(); // highlight custom (live)
        state.AppendChar('8');

        state.MoveUp();   // arrow away while typing

        state.EditChoiceIndex.Should().Be(0);
        state.CustomTextBuffer.Should().BeEmpty(); // buffer cleared off the custom choice
        fields[0].LastCustomText.Should().Be("8"); // but remembered
    }

    [Fact]
    public void ReturningToCustomAfterTyping_RestoresRememberedText()
    {
        var fields = FieldsWithCustom();
        var state = new FormSelectorState(fields);
        state.MoveUp();
        state.Enter();
        state.MoveDown(); // custom
        state.AppendChar('8');
        state.AppendChar('.');
        state.MoveUp();   // arrow away (remembers "8.")
        state.MoveDown(); // back to custom

        state.CustomTextBuffer.Should().Be("8.");
    }
}
