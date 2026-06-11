// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.Tools.Bootstrapper.Commands.Init.Form;

/// <summary>
/// A single field in the init form: a label, the values it can take, the default value, and the
/// currently selected value. UI-only prototype data — the choices and help text are mock content.
/// </summary>
internal sealed class FormField
{
    public FormField(string label, IReadOnlyList<FieldChoice> choices, int defaultIndex)
    {
        if (choices.Count == 0)
        {
            throw new ArgumentException("A field needs at least one choice.", nameof(choices));
        }

        if (defaultIndex < 0 || defaultIndex >= choices.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultIndex));
        }

        Label = label;
        Choices = choices;
        DefaultIndex = defaultIndex;
        SelectedIndex = defaultIndex;
    }

    /// <summary>The field label shown to the left of the value (e.g. "SDK Channel").</summary>
    public string Label { get; }

    /// <summary>The values this field can take.</summary>
    public IReadOnlyList<FieldChoice> Choices { get; }

    /// <summary>The index of the recommended default value.</summary>
    public int DefaultIndex { get; }

    /// <summary>The index of the currently selected value. Updated when the user picks a value.</summary>
    public int SelectedIndex { get; set; }

    /// <summary>The currently selected value.</summary>
    public FieldChoice Selected => Choices[SelectedIndex];

    /// <summary>True when the selected value differs from the recommended default (drives coloring).</summary>
    public bool IsChangedFromDefault => SelectedIndex != DefaultIndex;
}
