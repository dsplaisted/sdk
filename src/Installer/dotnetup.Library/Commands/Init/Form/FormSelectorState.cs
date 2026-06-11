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
}

/// <summary>
/// Pure, console-free state machine for the init form selector. Owns the focus/edit state and the
/// transitions for arrow navigation, entering/committing/cancelling a field edit, and accepting the
/// form. Kept separate from the Spectre rendering so the behavior is unit-testable.
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

    /// <summary>True once the user accepted the form; the input loop should stop.</summary>
    public bool IsDone { get; private set; }

    /// <summary>True when <see cref="FocusedRow"/> is the Accept row (Form mode only).</summary>
    public bool IsAcceptFocused => Mode == FormMode.Form && FocusedRow == AcceptRow;

    /// <summary>The field currently focused (Form mode) or being edited; null when Accept is focused.</summary>
    public FormField? FocusedField =>
        FocusedRow >= 0 && FocusedRow < _fields.Count ? _fields[FocusedRow] : null;

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
        else if (EditChoiceIndex > 0)
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
        else
        {
            FormField field = _fields[FocusedRow];
            if (EditChoiceIndex < field.Choices.Count - 1)
            {
                EditChoiceIndex++;
            }
        }
    }

    /// <summary>
    /// Enter: in Form mode, opens the focused field for editing, or accepts when Accept is focused.
    /// In Editing mode, commits the highlighted choice and returns to the form.
    /// </summary>
    public void Enter()
    {
        if (Mode == FormMode.Form)
        {
            if (FocusedRow == AcceptRow)
            {
                IsDone = true;
                return;
            }

            EditChoiceIndex = _fields[FocusedRow].SelectedIndex;
            Mode = FormMode.EditingField;
        }
        else
        {
            _fields[FocusedRow].SelectedIndex = EditChoiceIndex;
            CollapseToForm();
        }
    }

    /// <summary>
    /// Escape/cancel: in Editing mode, discards the in-progress choice and returns to the form
    /// without changing the field's value. In Form mode it is a no-op (the caller decides whether
    /// to treat it as quit).
    /// </summary>
    public void Cancel()
    {
        if (Mode == FormMode.EditingField)
        {
            CollapseToForm();
        }
    }

    private void CollapseToForm()
    {
        Mode = FormMode.Form;
        EditChoiceIndex = -1;
    }
}
