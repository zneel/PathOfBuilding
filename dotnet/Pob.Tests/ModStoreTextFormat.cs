using System.Text;

namespace Pob.Tests;

/// <summary>
/// The C# side of <c>ModDB:Print()</c>. Renders a modifier exactly as
/// <c>src/Classes/ModDB.lua:336-370</c> does, through the same three helpers
/// (<c>modLib.formatValue</c>, <c>modLib.formatTag</c>, <c>modLib.formatTags</c>,
/// <c>src/Modules/ModTools.lua:149-224</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately a reimplementation rather than an echo of the dump's own
/// <c>print</c> field. Every mod in the corpus is rendered here and compared against what
/// Lua produced, so the comparison actually exercises the parsed <see cref="ModStoreValue"/>
/// tree: a value shape the model dropped, a tag parameter it lost, or a number formatted the
/// .NET way instead of Lua's <c>%.14g</c> all show up as a mismatched line.
/// </para>
/// <para>
/// It takes <see cref="ModStoreDumpMod"/>, not a JSON element, so pointing it at
/// <c>Pob.Core</c>'s real <c>Mod</c> once ticket 09 lands is an adapter, not a rewrite.
/// </para>
/// </remarks>
public static class ModStoreTextFormat
{
    /// <summary>
    /// One mod as <c>ModDB:Print()</c> writes it:
    /// <c>&lt;value&gt; = &lt;type&gt;|&lt;flags&gt;|&lt;keywordFlags&gt;|&lt;tags&gt;|&lt;source&gt;</c>.
    /// </summary>
    public static string Print(ModStoreDumpMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);

        // ModDB.lua:346 writes `mod.source or "?"`, so an unsourced mod is visibly "?" and
        // not an empty column.
        return string.Join(
            '|',
            FormatValue(mod.Value) + " = " + mod.Type,
            mod.FlagNames,
            mod.KeywordFlagNames,
            FormatTags(mod.Tags),
            mod.Source ?? "?");
    }

    /// <summary><c>modLib.formatMod</c> — the form a nested mod takes inside a value.</summary>
    public static string FormatMod(ModStoreDumpMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);

        return FormatValue(mod.Value) + " = " + string.Join(
            '|',
            mod.Name,
            mod.Type,
            mod.FlagNames,
            mod.KeywordFlagNames,
            FormatTags(mod.Tags));
    }

    /// <summary><c>modLib.formatTags</c> — comma-joined tags, or <c>"-"</c> when there are none.</summary>
    public static string FormatTags(IReadOnlyList<ModStoreDumpTag> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        return tags.Count == 0 ? "-" : string.Join(',', tags.Select(FormatTag));
    }

    /// <summary><c>modLib.formatTag</c>.</summary>
    public static string FormatTag(ModStoreDumpTag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        return string.Join('/', TypeFirst(tag.Entries).Select(static e => e.Key + "=" + FormatTagValue(e.Value)));
    }

    /// <summary><c>modLib.formatValue</c>.</summary>
    public static string FormatValue(ModStoreValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        switch (value)
        {
            case ModStoreNumberValue number:
                return ModStoreLua.Number(number.Value);

            case ModStoreStringValue text:
                return text.Value;

            case ModStoreBooleanValue flag:
                return ModStoreLua.Boolean(flag.Value);

            case ModStoreNilValue:
                return "nil";

            // dump.lua swaps tostring for the duration of the render so a closure prints as
            // its definition site instead of an address; this is the same spelling.
            case ModStoreFunctionValue function:
                return "function@" + function.Definition;

            case ModStoreListValue list:
                // formatValue walks the table with pairs(), so an array's keys come out as
                // 1, 2, 3 and it prints "{1=cold/2=spell/3=skill}" — unlike formatTag, which
                // concatenates the same table as "{cold,spell,skill}".
                return "{" + string.Join(
                    '/',
                    list.Items.Select(static (item, index) =>
                        (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + "=" + FormatValue(item))) + "}";

            case ModStoreMapValue map:
                return "{" + string.Join('/', TypeFirst(map.Entries).Select(FormatValueEntry)) + "}";

            case ModStoreModValue:
                // Reachable only if some mod's `value` were itself a bare modifier rather
                // than { mod = ... }. Lua's formatValue would t_sort that table's mixed
                // number and string keys and raise; nothing in the 3.13 corpus does it.
                throw new NotSupportedException(
                    "a mod value is a bare modifier; Lua's formatValue cannot render that shape");

            default:
                throw new NotSupportedException($"unhandled mod value {value.GetType().Name}");
        }
    }

    /// <summary>
    /// <c>modLib.formatValue</c>'s one special case: a parameter literally named <c>mod</c>
    /// is rendered as a whole modifier in brackets rather than as a nested table.
    /// </summary>
    private static string FormatValueEntry(KeyValuePair<string, ModStoreValue> entry)
    {
        if (string.Equals(entry.Key, "mod", StringComparison.Ordinal) && entry.Value is ModStoreModValue nested)
        {
            return "mod=[" + FormatMod(nested.Mod) + "]";
        }

        return entry.Key + "=" + FormatValue(entry.Value);
    }

    /// <summary>
    /// <c>modLib.formatTag</c>'s value rules, which are not <c>formatValue</c>'s: a table
    /// whose first element exists is either a list of tags (rendered as tags) or a list of
    /// scalars (comma-joined), and any other table is a single nested tag. All three get
    /// wrapped in braces.
    /// </summary>
    private static string FormatTagValue(ModStoreValue value) => value switch
    {
        ModStoreListValue { Items.Count: > 0 } list when list.Items[0] is ModStoreMapValue =>
            "{" + string.Join(',', list.Items.Select(static item => FormatTag(AsTag(item)))) + "}",

        ModStoreListValue { Items.Count: > 0 } list =>
            "{" + string.Join(',', list.Items.Select(FormatValue)) + "}",

        // An empty Lua table has no [1], so formatTag takes the "single nested tag" branch
        // and renders it as the empty string inside braces.
        ModStoreListValue => "{}",

        ModStoreMapValue map => "{" + FormatTag(new ModStoreDumpTag(map.Entries)) + "}",

        _ => FormatValue(value),
    };

    private static ModStoreDumpTag AsTag(ModStoreValue value) => value is ModStoreMapValue map
        ? new ModStoreDumpTag(map.Entries)
        : throw new NotSupportedException($"a tag list holds a {value.GetType().Name}");

    /// <summary>
    /// Both formatters hoist <c>type</c> to the front and leave every other key in sorted
    /// order. The dump already emits entries in Lua's ordinal sort order, so this only has to
    /// do the hoist.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, ModStoreValue>> TypeFirst(
        IReadOnlyList<KeyValuePair<string, ModStoreValue>> entries)
    {
        int type = -1;
        for (int i = 0; i < entries.Count; i++)
        {
            if (string.Equals(entries[i].Key, "type", StringComparison.Ordinal))
            {
                type = i;
                break;
            }
        }

        if (type < 0)
        {
            return entries;
        }

        List<KeyValuePair<string, ModStoreValue>> ordered = [entries[type]];
        for (int i = 0; i < entries.Count; i++)
        {
            if (i != type)
            {
                ordered.Add(entries[i]);
            }
        }

        return ordered;
    }

    /// <summary>
    /// A whole store as <c>ModDB:Print()</c> would dump it: the mods, then the conditions,
    /// then the multipliers.
    /// </summary>
    public static string PrintStore(ModStoreDumpStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        StringBuilder text = new();

        text.Append("=== Modifiers ===\n");
        foreach (ModStoreDumpMod mod in store.Mods)
        {
            text.Append('\'').Append(mod.Name).Append("':\n\t").Append(Print(mod)).Append('\n');
        }

        text.Append("=== Conditions ===\n");
        foreach (string condition in store.Conditions)
        {
            text.Append(condition).Append('\n');
        }

        text.Append("=== Multipliers ===\n");
        foreach (ModStoreDumpMultiplier multiplier in store.Multipliers)
        {
            text.Append(multiplier.Name).Append(" = ").Append(ModStoreLua.Number(multiplier.Value)).Append('\n');
        }

        return text.ToString();
    }
}
