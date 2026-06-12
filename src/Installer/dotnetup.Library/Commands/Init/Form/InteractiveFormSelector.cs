// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Microsoft.DotNet.Tools.Bootstrapper.Commands.Init.Form;

/// <summary>
/// Renders the init form prototype and runs its input loop using Spectre.Console's
/// <see cref="LiveDisplay"/> for flicker-free updates. The whole form stays visible at all times,
/// and every field always shows its current value's help text and derived info (install location,
/// profile file, the system installs that would migrate). The focused field expands <i>inline</i>
/// to show all of its choices (each with help text); the highlighted choice's derived info appears
/// directly beneath it. A custom-input choice opens an inline text box.
///
/// Nested content is indented with <see cref="Padder"/> so wrapped help text keeps a consistent
/// hanging indent.
///
/// UI-only prototype: it mutates the model's field selections in place and reports whether the user
/// accepted. It is not wired to any install/config logic.
/// </summary>
internal static class InteractiveFormSelector
{
    // Arrow/cursor flash interval in milliseconds (matches InteractiveOptionSelector).
    private const int FlashIntervalMs = 600;

    // Left-indent (columns) for content nested under a field row and under a choice row.
    private const int FieldIndent = 4;
    private const int ChoiceIndent = 2;
    private const int ChoiceDetailIndent = 6;

    private enum KeyResult
    {
        Ignore,
        Redraw,
        Quit,
        Accept,
    }

    /// <summary>
    /// Displays the form. Returns <c>true</c> if the user accepted (the model's field selections
    /// hold the chosen values), or <c>false</c> if they quit without accepting.
    /// </summary>
    public static bool Show(InitFormModel model)
    {
        var state = new FormSelectorState(model.Fields);

        if (Console.IsInputRedirected)
        {
            // Non-interactive/redirected: render the form once and accept the defaults.
            AnsiConsole.Write(BuildRenderable(model, state, showArrow: true));
            return true;
        }

        return RunInteractive(model, state);
    }

    private static bool RunInteractive(InitFormModel model, FormSelectorState state)
    {
        bool showArrow = true;
        bool accepted = false;
        long lastToggle = Environment.TickCount64;
        bool done = false;

        AnsiConsole.Live(BuildRenderable(model, state, showArrow))
            .AutoClear(true)
            .Start(ctx =>
            {
                while (!done)
                {
                    if (Console.KeyAvailable)
                    {
                        KeyResult result = ApplyKey(state, Console.ReadKey(intercept: true));
                        if (result == KeyResult.Accept)
                        {
                            accepted = true;
                            done = true;
                        }
                        else if (result == KeyResult.Quit)
                        {
                            done = true;
                        }
                        else if (result == KeyResult.Redraw)
                        {
                            showArrow = true;
                            lastToggle = Environment.TickCount64;
                            ctx.UpdateTarget(BuildRenderable(model, state, showArrow));
                        }

                        continue;
                    }

                    long now = Environment.TickCount64;
                    if (now - lastToggle >= FlashIntervalMs)
                    {
                        lastToggle = now;
                        showArrow = !showArrow;
                        ctx.UpdateTarget(BuildRenderable(model, state, showArrow));
                    }

                    Thread.Sleep(50);
                }
            });

        RenderFinal(model, accepted);
        return accepted;
    }

    private static KeyResult ApplyKey(FormSelectorState state, ConsoleKeyInfo keyInfo)
    {
        return state.Mode == FormMode.EditingCustomText
            ? ApplyTextKey(state, keyInfo)
            : ApplyNavKey(state, keyInfo);
    }

    private static KeyResult ApplyNavKey(FormSelectorState state, ConsoleKeyInfo keyInfo)
    {
        switch (keyInfo.Key)
        {
            case ConsoleKey.UpArrow:
                state.MoveUp();
                return KeyResult.Redraw;

            case ConsoleKey.DownArrow:
                state.MoveDown();
                return KeyResult.Redraw;

            case ConsoleKey.Enter:
                state.Enter();
                return state.IsDone ? KeyResult.Accept : KeyResult.Redraw;

            case ConsoleKey.Escape:
                // Esc backs out of an open field, or quits the form without accepting.
                if (state.Mode == FormMode.EditingField)
                {
                    state.Cancel();
                    return KeyResult.Redraw;
                }

                return KeyResult.Quit;

            default:
                // Typing while a custom-input choice is highlighted starts text entry immediately.
                if (state.IsCustomChoiceHighlighted && !char.IsControl(keyInfo.KeyChar) && keyInfo.KeyChar != '\0')
                {
                    state.BeginCustomText();
                    state.AppendChar(keyInfo.KeyChar);
                    return KeyResult.Redraw;
                }

                return KeyResult.Ignore;
        }
    }

    private static KeyResult ApplyTextKey(FormSelectorState state, ConsoleKeyInfo keyInfo)
    {
        switch (keyInfo.Key)
        {
            case ConsoleKey.Enter:
                state.Enter();
                return KeyResult.Redraw;

            case ConsoleKey.Escape:
                state.Cancel();
                return KeyResult.Redraw;

            case ConsoleKey.Backspace:
                state.Backspace();
                return KeyResult.Redraw;

            default:
                if (!char.IsControl(keyInfo.KeyChar) && keyInfo.KeyChar != '\0')
                {
                    state.AppendChar(keyInfo.KeyChar);
                    return KeyResult.Redraw;
                }

                return KeyResult.Ignore;
        }
    }

    private static Rows BuildRenderable(InitFormModel model, FormSelectorState state, bool showArrow)
    {
        ThemeColors theme = DotnetupTheme.Current;
        int labelWidth = MaxLabelWidth(model);

        var rows = new List<IRenderable>
        {
            DotnetBotBanner.BuildPanel(),
            Text.Empty,
            new Markup($"[bold {theme.Brand}]{model.Subtitle.EscapeMarkup()}[/]"),
            new Markup($"[white]{model.Question.EscapeMarkup()}[/]"),
            Text.Empty,
        };

        for (int i = 0; i < model.Fields.Count; i++)
        {
            AppendField(rows, model, state, i, labelWidth, showArrow, theme);
        }

        rows.Add(BuildAcceptRow(state.IsAcceptFocused, showArrow, theme));
        rows.Add(Text.Empty);
        rows.Add(BuildLegend(state.Mode, theme));

        return new Rows(rows);
    }

    // Appends a field's row plus its always-visible detail. The field being edited expands to show
    // all of its choices instead of just the selected value's detail.
    private static void AppendField(
        List<IRenderable> rows,
        InitFormModel model,
        FormSelectorState state,
        int index,
        int labelWidth,
        bool showArrow,
        ThemeColors theme)
    {
        FormField field = model.Fields[index];
        bool focused = state.FocusedRow == index;
        bool editing = focused && state.Mode != FormMode.Form;

        rows.Add(BuildFieldRow(field, labelWidth, focused, showArrow, theme));

        if (editing)
        {
            for (int c = 0; c < field.Choices.Count; c++)
            {
                AppendChoice(rows, model, field, state, c, showArrow, theme);
            }
        }
        else
        {
            // Always show the selected value's help and derived info, even when not focused.
            FieldDetail detail = model.BuildDetail(field, field.SelectedIndex);
            rows.Add(Indent(HelpMarkup(detail.HelperText, theme), FieldIndent));
            AppendDerived(rows, detail.Lines, FieldIndent, theme);
        }

        rows.Add(Text.Empty);
    }

    private static Markup BuildFieldRow(FormField field, int labelWidth, bool focused, bool showArrow, ThemeColors theme)
    {
        string arrow = focused && showArrow ? "> " : "  ";
        string label = field.Label.PadRight(labelWidth);
        string labelStyle = focused ? "white bold" : "white";
        string valueColor = field.IsChangedFromDefault ? theme.Warning : theme.Accent;

        return new Markup(string.Format(
            CultureInfo.InvariantCulture,
            "[{0}]{1}{2}[/]  [{3}]{4}[/]",
            labelStyle,
            arrow.EscapeMarkup(),
            label.EscapeMarkup(),
            valueColor,
            field.DisplayValue.EscapeMarkup()));
    }

    // A choice row, its help text, and (when highlighted) its derived info inline beneath it.
    private static void AppendChoice(
        List<IRenderable> rows,
        InitFormModel model,
        FormField field,
        FormSelectorState state,
        int index,
        bool showArrow,
        ThemeColors theme)
    {
        bool selected = state.EditChoiceIndex == index;
        FieldChoice choice = field.Choices[index];
        string? inlineHelp = field.InlineHelp ? choice.HelperText : null;

        rows.Add(Indent(BuildChoiceMarkup(field, index, selected, showArrow, inlineHelp, theme), ChoiceIndent));

        if (!field.InlineHelp)
        {
            rows.Add(Indent(HelpMarkup(choice.HelperText, theme), ChoiceDetailIndent));
        }

        if (!selected)
        {
            return;
        }

        AppendDerived(rows, model.BuildDetail(field, index).Lines, ChoiceDetailIndent, theme);

        // The custom-input choice shows an inline text box as soon as it's highlighted, so the user
        // can start typing directly (the buffer is empty until they do).
        if (choice.IsCustomInput)
        {
            rows.Add(Indent(BuildInputMarkup(state.CustomTextBuffer, showArrow, theme), ChoiceDetailIndent));
        }
    }

    private static Markup BuildChoiceMarkup(FormField field, int index, bool selected, bool showArrow, string? inlineHelp, ThemeColors theme)
    {
        FieldChoice choice = field.Choices[index];
        string suffix = index == field.DefaultIndex ? "  (default)" : string.Empty;
        string trailing = inlineHelp is null
            ? string.Empty
            : string.Format(CultureInfo.InvariantCulture, "  [{0}]{1}[/]", theme.Dim, inlineHelp.EscapeMarkup());

        if (selected)
        {
            string arrow = showArrow ? "> " : "  ";
            return new Markup(string.Format(
                CultureInfo.InvariantCulture,
                "[{0} bold]{1}{2}[/][{3}]{4}[/]{5}",
                theme.Accent,
                arrow.EscapeMarkup(),
                choice.Title.EscapeMarkup(),
                theme.Dim,
                suffix.EscapeMarkup(),
                trailing));
        }

        return new Markup(string.Format(
            CultureInfo.InvariantCulture,
            "[white]  {0}[/][{1}]{2}[/]{3}",
            choice.Title.EscapeMarkup(),
            theme.Dim,
            suffix.EscapeMarkup(),
            trailing));
    }

    private static Markup BuildInputMarkup(string buffer, bool showArrow, ThemeColors theme)
    {
        string cursor = showArrow ? "▏" : " ";
        return new Markup(string.Format(
            CultureInfo.InvariantCulture,
            "[{0}]> [/][{1}]{2}[/][{0}]{3}[/]",
            theme.Dim,
            theme.Accent,
            buffer.EscapeMarkup(),
            cursor));
    }

    private static void AppendDerived(List<IRenderable> rows, IReadOnlyList<DetailLine> lines, int indent, ThemeColors theme)
    {
        foreach (DetailLine line in lines)
        {
            rows.Add(Indent(BuildDetailLine(line, theme), indent));
        }
    }

    private static Markup HelpMarkup(string text, ThemeColors theme) =>
        new($"[{theme.Dim} italic]{text.EscapeMarkup()}[/]");

    private static Markup BuildDetailLine(DetailLine line, ThemeColors theme)
    {
        if (line.Value is null)
        {
            return new Markup($"[{theme.Dim}]{line.Label.EscapeMarkup()}[/]");
        }

        return new Markup(string.Format(
            CultureInfo.InvariantCulture,
            "[{0}]{1}[/] [{2}]{3}[/]",
            theme.Dim,
            line.Label.EscapeMarkup(),
            theme.Accent,
            line.Value.EscapeMarkup()));
    }

    private static Markup BuildAcceptRow(bool focused, bool showArrow, ThemeColors theme)
    {
        const string accept = "Accept and install";
        if (focused)
        {
            string arrow = showArrow ? "> " : "  ";
            return new Markup($"[{theme.Success} bold]{arrow.EscapeMarkup()}{accept.EscapeMarkup()}[/]");
        }

        return new Markup($"[{theme.Dim}]  {accept.EscapeMarkup()}[/]");
    }

    private static Markup BuildLegend(FormMode mode, ThemeColors theme)
    {
        string text = mode switch
        {
            FormMode.EditingCustomText => "type a channel · Enter confirm · Esc back",
            FormMode.EditingField => "↑/↓ choose · Enter select · Esc back",
            _ => "↑/↓ move · Enter edit/accept · Esc quit",
        };

        return new Markup($"[{theme.Dim}]{text.EscapeMarkup()}[/]");
    }

    private static Padder Indent(IRenderable content, int left) =>
        new(content, new Padding(left, 0, 0, 0));

    private static void RenderFinal(InitFormModel model, bool accepted)
    {
        ThemeColors theme = DotnetupTheme.Current;
        if (!accepted)
        {
            AnsiConsole.MarkupLine($"[{theme.Dim}]{"Exited without changes.".EscapeMarkup()}[/]");
            return;
        }

        int labelWidth = MaxLabelWidth(model);
        AnsiConsole.MarkupLine($"[{theme.Success} bold]{"Selected settings:".EscapeMarkup()}[/]");
        foreach (FormField field in model.Fields)
        {
            string valueColor = field.IsChangedFromDefault ? theme.Warning : theme.Accent;
            AnsiConsole.MarkupLine(string.Format(
                CultureInfo.InvariantCulture,
                "  [white]{0}[/]  [{1}]{2}[/]",
                field.Label.PadRight(labelWidth).EscapeMarkup(),
                valueColor,
                field.DisplayValue.EscapeMarkup()));
        }
    }

    private static int MaxLabelWidth(InitFormModel model)
    {
        int width = 0;
        foreach (FormField field in model.Fields)
        {
            width = Math.Max(width, field.Label.Length);
        }

        return width;
    }
}
