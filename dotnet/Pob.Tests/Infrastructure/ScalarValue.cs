using System.Globalization;

namespace Pob.Tests.Infrastructure;

/// <summary>What kind of Lua value a <see cref="ScalarValue"/> holds.</summary>
public enum ScalarKind
{
    /// <summary>A Lua number, always a double.</summary>
    Number,

    /// <summary>A Lua boolean.</summary>
    Boolean,

    /// <summary>A Lua string.</summary>
    Text,
}

/// <summary>
/// One scalar read out of a conformance corpus, tagged with the Lua type it had.
/// </summary>
/// <remarks>
/// <para>
/// The tag is what makes the tolerance ladder possible. "Exact for booleans, relative epsilon
/// for numbers" needs to know which it is looking at, and by the time a value has been widened
/// to <see cref="double"/> that information is gone: a corpus <c>true</c> and a corpus
/// <c>1</c> both become 1.0, and an engine that returned the wrong one of the two would pass.
/// </para>
/// <para>
/// Lua's engine outputs really are only these three types plus tables; the dump scripts drop
/// tables and record their key names separately rather than inventing a flattening.
/// </para>
/// </remarks>
public readonly record struct ScalarValue
{
    private ScalarValue(ScalarKind kind, double number, bool boolean, string? text)
    {
        Kind = kind;
        Number = number;
        Boolean = boolean;
        Text = text;
    }

    /// <summary>The Lua type this value had.</summary>
    public ScalarKind Kind { get; }

    /// <summary>The numeric value. Meaningful only when <see cref="Kind"/> is <see cref="ScalarKind.Number"/>.</summary>
    public double Number { get; }

    /// <summary>The boolean value. Meaningful only when <see cref="Kind"/> is <see cref="ScalarKind.Boolean"/>.</summary>
    public bool Boolean { get; }

    /// <summary>The string value. Non-null only when <see cref="Kind"/> is <see cref="ScalarKind.Text"/>.</summary>
    public string? Text { get; }

    /// <summary>Wrap a number.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A number-kinded scalar.</returns>
    public static ScalarValue OfNumber(double value) => new(ScalarKind.Number, value, false, null);

    /// <summary>Wrap a boolean.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A boolean-kinded scalar.</returns>
    public static ScalarValue OfBoolean(bool value) => new(ScalarKind.Boolean, 0.0, value, null);

    /// <summary>Wrap a string.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A text-kinded scalar.</returns>
    public static ScalarValue OfText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ScalarValue(ScalarKind.Text, 0.0, false, value);
    }

    /// <summary>
    /// Is this a whole number? Drives the "exact for integers" rung of the ladder.
    /// </summary>
    public bool IsIntegralNumber => Kind == ScalarKind.Number && LuaNumber.IsIntegral(Number);

    /// <summary>Render for a failure message, including the bit pattern for numbers.</summary>
    /// <returns>A short human-readable description.</returns>
    public override string ToString() => Kind switch
    {
        ScalarKind.Number => LuaNumber.Describe(Number),
        ScalarKind.Boolean => Boolean ? "true" : "false",
        ScalarKind.Text => string.Create(CultureInfo.InvariantCulture, $"\"{Text}\""),
        _ => "<unknown>",
    };
}
