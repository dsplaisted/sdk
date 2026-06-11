// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Microsoft.DotNet.Tools.Bootstrapper.Commands.Init.Form;

/// <summary>
/// Renders the init form prototype and runs its input loop using Spectre.Console's
/// <see cref="LiveDisplay"/> for flicker-free updates. Mirrors the approach of
/// <see cref="InteractiveOptionSelector"/> but drives a <see cref="FormSelectorState"/>:
///
/// <list type="bullet">
/// <item><b>Collapsed form view</b> — banner, subtitle/question, one row per field
/// (<c>Label: value</c>, value colored accent when default / warning when changed), an Accept row
/// (the default focus, with the flashing arrow), and a navigation legend.</item>
/// <item><b>Expanded edit view</b> — the focused field's choices, the selected one marked, with a
/// detailed helper-text panel for it; the other fields collapse to dimmed one-liners to make room.</item>
/// </list>
///
/// UI-only prototype: it mutates the model's field selections in place and reports whether the user
/// accepted. It is not wired to any install/config logic.
/// </summary>
internal static class InteractiveFormSelector
{
    // Arrow flash interval in milliseconds (matches InteractiveOptionSelector).
    private const int FlashIntervalMs = 600;

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
                        KeyResult result = ApplyKey(state, Console.ReadKey(intercept: true).Key);
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

    private static KeyResult ApplyKey(FormSelectorState state, ConsoleKey key)
    {
        switch (key)
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
                return KeyResult.Ignore;
        }
    }

    private static Rows BuildRenderable(InitFormModel model, FormSelectorState state, bool showArrow)
    {
        return state.Mode == FormMode.EditingField
            ? BuildEditView(model, state, showArrow)
            : BuildFormView(model, state, showArrow);
    }

    private static Rows BuildFormView(InitFormModel model, FormSelectorState state, bool showArrow)
    {
        ThemeColors theme = DotnetupTheme.Current;
        int labelWidth = MaxLabelWidth(model);

        var rows = new List<IRenderable>
        {
            DotnetBotBanner.BuildPanel(),
            Text.Empty,
            new Markup($"[bold {theme.Brand}]{model.Subtitle.EscapeMarkup()}[/]"),
            new Markup(model.Question.EscapeMarkup()),
            Text.Empty,
        };

        for (int i = 0; i < model.Fields.Count; i++)
        {
            rows.Add(BuildFieldRow(model.Fields[i], labelWidth, state.FocusedRow == i, showArrow, theme));
        }

        rows.Add(Text.Empty);
        rows.Add(BuildAcceptRow(state.IsAcceptFocused, showArrow, theme));
        rows.Add(Text.Empty);
        rows.Add(BuildLegend("↑/↓ move · Enter edit/accept · Esc quit", theme));

        return new Rows(rows);
    }

    private static Markup BuildFieldRow(FormField field, int labelWidth, bool focused, bool showArrow, ThemeColors theme)
    {
        string arrow = focused && showArrow ? "> " : "  ";
        string label = field.Label.PadRight(labelWidth);
        string valueColor = field.IsChangedFromDefault ? theme.Warning : theme.Accent;
        string labelColor = focused ? theme.Brand : theme.Dim;

        return new Markup(string.Format(
            CultureInfo.InvariantCulture,
            "[{0}]{1}{2}[/]  [{3}]{4}[/]",
            labelColor,
            arrow.EscapeMarkup(),
            label.EscapeMarkup(),
            valueColor,
            field.Selected.Title.EscapeMarkup()));
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

    private static Rows BuildEditView(InitFormModel model, FormSelectorState state, bool showArrow)
    {
        ThemeColors theme = DotnetupTheme.Current;
        FormField editing = state.FocusedField!;

        var rows = new List<IRenderable>
        {
            DotnetBotBanner.BuildPanel(),
            Text.Empty,
        };

        rows.AddRange(BuildEditContextRows(model, editing, theme));
        rows.Add(new Markup($"[bold {theme.Brand}]{("Select " + editing.Label).EscapeMarkup()}[/]"));
        rows.Add(Text.Empty);

        for (int i = 0; i < editing.Choices.Count; i++)
        {
            rows.Add(BuildChoiceRow(editing, i, state.EditChoiceIndex == i, showArrow, theme));
        }

        rows.Add(Text.Empty);
        rows.Add(BuildHelperPanel(editing.Choices[state.EditChoiceIndex], theme));
        rows.Add(Text.Empty);
        rows.Add(BuildLegend("↑/↓ choose · Enter select · Esc back", theme));

        return new Rows(rows);
    }

    // Collapsed, dimmed one-liners for the fields not being edited, so the user keeps their place.
    private static List<IRenderable> BuildEditContextRows(InitFormModel model, FormField editing, ThemeColors theme)
    {
        int labelWidth = MaxLabelWidth(model);
        var rows = new List<IRenderable>();

        foreach (FormField field in model.Fields)
        {
            if (!ReferenceEquals(field, editing))
            {
                rows.Add(new Markup(string.Format(
                    CultureInfo.InvariantCulture,
                    "[{0}]  {1}  {2}[/]",
                    theme.Dim,
                    field.Label.PadRight(labelWidth).EscapeMarkup(),
                    field.Selected.Title.EscapeMarkup())));
            }
        }

        rows.Add(Text.Empty);
        return rows;
    }

    private static Markup BuildChoiceRow(FormField editing, int index, bool selected, bool showArrow, ThemeColors theme)
    {
        FieldChoice choice = editing.Choices[index];
        string suffix = index == editing.DefaultIndex ? "  (default)" : string.Empty;

        if (selected)
        {
            string arrow = showArrow ? "> " : "  ";
            return new Markup(string.Format(
                CultureInfo.InvariantCulture,
                "[{0} bold]{1}{2}[/][{3}]{4}[/]",
                theme.Accent,
                arrow.EscapeMarkup(),
                choice.Title.EscapeMarkup(),
                theme.Dim,
                suffix.EscapeMarkup()));
        }

        return new Markup(string.Format(
            CultureInfo.InvariantCulture,
            "[{0}]  {1}{2}[/]",
            theme.Dim,
            choice.Title.EscapeMarkup(),
            suffix.EscapeMarkup()));
    }

    private static Panel BuildHelperPanel(FieldChoice choice, ThemeColors theme)
    {
        return new Panel(new Markup(choice.HelperText.EscapeMarkup()))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(theme.Dim),
            Header = new PanelHeader($" {choice.Title.EscapeMarkup()} "),
            Padding = new Padding(1, 0),
        };
    }

    private static Markup BuildLegend(string text, ThemeColors theme) =>
        new($"[{theme.Dim}]{text.EscapeMarkup()}[/]");

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
                "  [{0}]{1}[/]  [{2}]{3}[/]",
                theme.Dim,
                field.Label.PadRight(labelWidth).EscapeMarkup(),
                valueColor,
                field.Selected.Title.EscapeMarkup()));
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
