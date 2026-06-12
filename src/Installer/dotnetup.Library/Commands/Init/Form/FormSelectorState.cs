// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.Tools.Bootstrapper.Commands.Init.Form;

/// <summary>
/// The display/interaction mode of the form selector.
/// </summary>
internal enum FormMode
{
    /// <summary>Browsing the collapsed form: navigation moves between fields and the Accept row.</summary>
    Form,

    /// <summary>A field is expanded: navigation moves between that field's choices.</summary>
    EditingField,

    /// <summary>Typing a free-text value for a custom-input choice (e.g. a custom channel).</summary>
    EditingCustomText,
}

/// <summary>
/// Pure, console-free state machine for the init form selector. Owns the focus/edit state and the
/// transitions for arrow navigation, entering/committing/cancelling a field edit, typing a custom
/// value, and accepting the form. Kept separate from the Spectre rendering so the behavior is
/// unit-testable.
///
/// Row layout (Form mode): rows <c>0..Fields.Count-1</c> are the fields; the final row
/// (<c>Fields.Count</c>) is the Accept action, which is the initial focus so a single Enter accepts.
/// </summary>
internal sealed class FormSelectorState
{
    private readonly IReadOnlyList<FormField> _fields;

    public FormSelectorState(IReadOnlyList<FormField> fields)
    {
        if (fields.Count == 0)
        {
            throw new ArgumentException("The form needs at least one field.", nameof(fields));
        }

        _fields = fields;

        // Focus starts on the Accept row so the recommended path is a single Enter.
        FocusedRow = AcceptRow;
    }

    /// <summary>The row index representing the Accept action (immediately after the last field).</summary>
    public int AcceptRow => _fields.Count;

    /// <summary>Current interaction mode.</summary>
    public FormMode Mode { get; private set; } = FormMode.Form;

    /// <summary>
    /// The focused row in Form mode: <c>0..Fields.Count-1</c> for fields, <see cref="AcceptRow"/>
    /// for Accept.
    /// </summary>
    public int FocusedRow { get; private set; }

    /// <summary>The choice index highlighted while editing a field; otherwise -1.</summary>
    public int EditChoiceIndex { get; private set; } = -1;

    /// <summary>The in-progress text typed for a custom-input choice (valid in EditingCustomText).</summary>
    public string CustomTextBuffer { get; private set; } = string.Empty;

    /// <summary>True once the user accepted the form; the input loop should stop.</summary>
    public bool IsDone { get; private set; }

    /// <summary>True when <see cref="FocusedRow"/> is the Accept row (Form mode only).</summary>
    public bool IsAcceptFocused => Mode == FormMode.Form && FocusedRow == AcceptRow;

    /// <summary>The field currently focused (Form mode) or being edited; null when Accept is focused.</summary>
    public FormField? FocusedField =>
        FocusedRow >= 0 && FocusedRow < _fields.Count ? _fields[FocusedRow] : null;

    /// <summary>True when editing a field and the highlighted choice accepts free-text input.</summary>
    public bool IsCustomChoiceHighlighted =>
        Mode == FormMode.EditingField
        && FocusedField is { } focused
        && EditChoiceIndex >= 0
        && EditChoiceIndex < focused.Choices.Count
        && focused.Choices[EditChoiceIndex].IsCustomInput;

    /// <summary>
    /// Switches a highlighted custom-input choice into text-entry mode so the user can start typing
    /// immediately (without first pressing Enter). No-op if the highlighted choice isn't custom.
    /// </summary>
    public void BeginCustomText()
    {
        if (IsCustomChoiceHighlighted)
        {
            CustomTextBuffer = _fields[FocusedRow].CustomValue ?? string.Empty;
            Mode = FormMode.EditingCustomText;
        }
    }

    /// <summary>Moves focus to the previous row (Form) or previous choice (Editing). Clamps at the top.</summary>
    public void MoveUp()
    {
        if (Mode == FormMode.Form)
        {
            if (FocusedRow > 0)
            {
                FocusedRow--;
            }
        }
        else if (Mode == FormMode.EditingField && EditChoiceIndex > 0)
        {
            EditChoiceIndex--;
        }
    }

    /// <summary>Moves focus to the next row (Form) or next choice (Editing). Clamps at the bottom.</summary>
    public void MoveDown()
    {
        if (Mode == FormMode.Form)
        {
            if (FocusedRow < AcceptRow)
            {
                FocusedRow++;
            }
        }
        else if (Mode == FormMode.EditingField && EditChoiceIndex < _fields[FocusedRow].Choices.Count - 1)
        {
            EditChoiceIndex++;
        }
    }

    /// <summary>
    /// Enter: Form mode opens the focused field or accepts when Accept is focused; EditingField
    /// commits the highlighted choice (or opens text entry for a custom-input choice); EditingCustomText
    /// commits the typed value (or returns to the choice list when the text is empty).
    /// </summary>
    public void Enter()
    {
        switch (Mode)
        {
            case FormMode.Form:
                EnterFromForm();
                break;

            case FormMode.EditingField:
                EnterFromEditingField();
                break;

            case FormMode.EditingCustomText:
                CommitCustomText();
                break;
        }
    }

    /// <summary>
    /// Escape/cancel: EditingCustomText returns to the choice list; EditingField returns to the
    /// form; Form mode is a no-op (the caller decides whether to treat it as quit).
    /// </summary>
    public void Cancel()
    {
        if (Mode == FormMode.EditingCustomText)
        {
            CustomTextBuffer = string.Empty;
            Mode = FormMode.EditingField;
        }
        else if (Mode == FormMode.EditingField)
        {
            CollapseToForm();
        }
    }

    /// <summary>Appends a typed character to the custom-text buffer (EditingCustomText only).</summary>
    public void AppendChar(char c)
    {
        if (Mode == FormMode.EditingCustomText)
        {
            CustomTextBuffer += c;
        }
    }

    /// <summary>Removes the last character from the custom-text buffer (EditingCustomText only).</summary>
    public void Backspace()
    {
        if (Mode == FormMode.EditingCustomText && CustomTextBuffer.Length > 0)
        {
            CustomTextBuffer = CustomTextBuffer[..^1];
        }
    }

    private void EnterFromForm()
    {
        if (FocusedRow == AcceptRow)
        {
            IsDone = true;
            return;
        }

        EditChoiceIndex = _fields[FocusedRow].SelectedIndex;
        Mode = FormMode.EditingField;
    }

    private void EnterFromEditingField()
    {
        FormField field = _fields[FocusedRow];
        if (field.Choices[EditChoiceIndex].IsCustomInput)
        {
            CustomTextBuffer = field.CustomValue ?? string.Empty;
            Mode = FormMode.EditingCustomText;
            return;
        }

        field.SelectChoice(EditChoiceIndex);
        CollapseToForm();
    }

    private void CommitCustomText()
    {
        string trimmed = CustomTextBuffer.Trim();
        if (trimmed.Length == 0)
        {
            // Nothing typed: drop back to the choice list rather than committing an empty value.
            Mode = FormMode.EditingField;
            return;
        }

        _fields[FocusedRow].SetCustomValue(EditChoiceIndex, trimmed);
        CollapseToForm();
    }

    private void CollapseToForm()
    {
        Mode = FormMode.Form;
        EditChoiceIndex = -1;
        CustomTextBuffer = string.Empty;
    }
}
