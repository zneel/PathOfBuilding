using System.Globalization;
using System.Text.Json;

namespace Pob.Tests.Infrastructure;

/// <summary>
/// Reading conventions shared by every Lua-produced corpus in this repository.
/// </summary>
/// <remarks>
/// The dump scripts all write the same shape: a top-level object carrying a
/// <c>"format"</c> integer, and scalars spelled as JSON strings per <see cref="LuaNumber"/>.
/// Centralising the reading means a format bump is caught in one place, with a message that
/// names the script to re-run, rather than surfacing as a null-reference deep inside a suite.
/// </remarks>
public static class CorpusReader
{
    /// <summary>
    /// Open a corpus file and check its format version.
    /// </summary>
    /// <param name="path">Absolute path to the corpus.</param>
    /// <param name="expectedFormat">The <c>format</c> value this reader understands.</param>
    /// <param name="regenerateWith">Command that regenerates the file, for the error message.</param>
    /// <returns>The parsed document. The caller owns it and must dispose it.</returns>
    public static JsonDocument Open(string path, int expectedFormat, string regenerateWith)
    {
        RepoPaths.RequireFile(path, regenerateWith);

        JsonDocument document;
        using (FileStream stream = File.OpenRead(path))
        {
            document = JsonDocument.Parse(stream);
        }

        try
        {
            // `format` is a JSON number in some corpora and a JSON string in others: the fuzz
            // writer spells every number as a string on purpose (see LuaNumber), and the
            // version marker is not worth carving an exception out of that rule for.
            if (!document.RootElement.TryGetProperty("format", out JsonElement format))
            {
                throw new InvalidDataException(
                    $"{path} has no 'format' property. Regenerate it with: {regenerateWith}");
            }

            int actualFormat = ReadInt32(format);
            if (actualFormat != expectedFormat)
            {
                throw new InvalidDataException(
                    $"{path} is format {actualFormat}, this reader understands {expectedFormat}. "
                    + $"Regenerate it with: {regenerateWith}");
            }
        }
        catch
        {
            document.Dispose();
            throw;
        }

        return document;
    }

    /// <summary>
    /// Read a scalar written by a Lua dump script.
    /// </summary>
    /// <remarks>
    /// JSON strings are ambiguous by design here: the scripts spell numbers as strings so no
    /// reader re-rounds them. Which means the corpus has to say, out of band, which keys are
    /// numbers. The fuzz corpus does that by tagging booleans as JSON booleans and everything
    /// else as a string; a string that parses as a Lua number spelling IS a number, and one
    /// that does not is Lua text. Engine output keys never hold a string that looks like a
    /// number, so the ambiguity is theoretical -- but the rule is written down here rather
    /// than assumed at each call site.
    /// </remarks>
    /// <param name="element">The JSON value.</param>
    /// <returns>The scalar it denotes.</returns>
    public static ScalarValue ReadScalar(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                return ScalarValue.OfBoolean(true);

            case JsonValueKind.False:
                return ScalarValue.OfBoolean(false);

            case JsonValueKind.String:
                string text = element.GetString()!;
                return LooksNumeric(text)
                    ? ScalarValue.OfNumber(LuaNumber.Parse(text))
                    : ScalarValue.OfText(text);

            case JsonValueKind.Number:
                return ScalarValue.OfNumber(element.GetDouble());

            default:
                throw new InvalidDataException($"corpus scalar has unsupported JSON kind {element.ValueKind}");
        }
    }

    /// <summary>
    /// Read a number written by a Lua dump script, rejecting anything that is not one.
    /// </summary>
    /// <param name="element">The JSON value.</param>
    /// <returns>The value.</returns>
    public static double ReadNumber(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => LuaNumber.Parse(element.GetString()!),
        JsonValueKind.Number => element.GetDouble(),
        _ => throw new InvalidDataException($"expected a corpus number, found JSON {element.ValueKind}"),
    };

    /// <summary>
    /// Read a number written by a Lua dump script that is known to be a count.
    /// </summary>
    /// <param name="element">The JSON value.</param>
    /// <returns>The value as an int.</returns>
    public static int ReadInt32(JsonElement element)
    {
        double value = ReadNumber(element);
        if (!LuaNumber.IsIntegral(value) || value is < int.MinValue or > int.MaxValue)
        {
            throw new InvalidDataException(
                string.Create(CultureInfo.InvariantCulture, $"expected an integer, found {LuaNumber.Format(value)}"));
        }

        return (int)value;
    }

    private static bool LooksNumeric(string text) =>
        text is "inf" or "-inf" or "nan"
        || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
}
