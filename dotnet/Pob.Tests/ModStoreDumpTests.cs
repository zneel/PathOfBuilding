using Xunit;

namespace Pob.Tests;

/// <summary>
/// Replays the committed mod-store dumps under <c>Pob.Tests/oracles/modstore/</c>
/// (migration ticket 06).
/// </summary>
/// <remarks>
/// <para>
/// The corpus is produced by <c>dotnet/tools/modstore-dump/dump.lua</c>, which loads each
/// build in <c>spec/TestBuilds/3.13/</c> through <c>src/HeadlessWrapper.lua</c> and writes out
/// <c>env.modDB</c>, <c>env.enemyDB</c>, <c>env.itemModDB</c>,
/// <c>env.player.mainSkill.skillModList</c> and — when the main skill summons —
/// <c>env.minion.modDB</c>. Regenerate with
/// <c>cd src &amp;&amp; luajit ../dotnet/tools/modstore-dump/dump.lua</c>.
/// </para>
/// <para>
/// The C# mod stores do not exist yet (tickets 10-12), so what is under test today is the
/// corpus and the model that reads it: the dumps parse, they hold what the ticket says they
/// must hold, and every mod's <c>ModDB:Print()</c> line can be rebuilt from the parsed
/// structure. When <c>Pob.Core.ModDb</c> lands, the change is an adapter that projects a real
/// store into <see cref="ModStoreDumpStore"/> and a second theory that runs the same
/// assertions over it; <see cref="ModStoreTextFormat"/> and <see cref="ModStoreSummary"/> do
/// not move.
/// </para>
/// </remarks>
public sealed class ModStoreDumpTests
{
    /// <summary>The stores every 3.13 build must contribute. <c>minionModDB</c> is not among them; see below.</summary>
    private static readonly string[] RequiredStores = ["modDB", "enemyDB", "itemModDB", "skillModList"];

    private static readonly Lazy<IReadOnlyList<ModStoreDump>> Corpus =
        new(ModStoreDumpReader.LoadAll, isThreadSafe: true);

    public static TheoryData<string> Builds
    {
        get
        {
            TheoryData<string> data = [];
            foreach (string slug in ModStoreDumpReader.BuildSlugs)
            {
                data.Add(slug);
            }

            return data;
        }
    }

    /// <summary>
    /// The load-and-parse guard the ticket asks for, per build so a failure names the build.
    /// </summary>
    [Theory]
    [MemberData(nameof(Builds))]
    public void Dump_LoadsAndHoldsEveryStoreTheTicketNames(string slug)
    {
        ModStoreDump dump = ModStoreDumpReader.Load(slug);

        Assert.NotEmpty(dump.Build);
        Assert.StartsWith("spec/TestBuilds/3.13/", dump.BuildFile, StringComparison.Ordinal);
        Assert.NotEmpty(dump.MainSkill);
        Assert.NotEqual("?", dump.MainSkill);

        string[] names = dump.Stores.Select(static s => s.Name).ToArray();
        foreach (string required in RequiredStores)
        {
            Assert.Contains(required, names, StringComparer.Ordinal);
        }

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());

        foreach (ModStoreDumpStore store in dump.Stores)
        {
            // The header counts are written by the generator from the Lua tables, the arrays
            // by the encoder. Disagreement means the dump was truncated.
            Assert.Equal(store.DeclaredModCount, store.Mods.Count);
            Assert.Equal(store.DeclaredConditionCount, store.Conditions.Count);
            Assert.Equal(store.DeclaredMultiplierCount, store.Multipliers.Count);

            Assert.NotEmpty(store.Mods);
            Assert.Contains(store.Class, new[] { "ModDB", "ModList" }, StringComparer.Ordinal);
            Assert.StartsWith("env.", store.Path, StringComparison.Ordinal);
        }

        // skillModList is the one ModList in the set; the rest are ModDBs. The two classes
        // store their mods differently (a flat array versus per-name buckets), so a store
        // that came out the wrong class means the dump read the wrong field.
        Assert.Equal(
            "ModList",
            dump.Stores.Single(static s => string.Equals(s.Name, "skillModList", StringComparison.Ordinal)).Class);
        Assert.All(
            dump.Stores.Where(static s => !string.Equals(s.Name, "skillModList", StringComparison.Ordinal)),
            static s => Assert.Equal("ModDB", s.Class));
    }

    /// <summary>
    /// Every mod's <c>ModDB:Print()</c> line, rebuilt in C# from the parsed model, matches
    /// the line Lua wrote.
    /// </summary>
    /// <remarks>
    /// This is the real conformance assertion in this file. It is not a tautology: the dump's
    /// <c>print</c> string comes from <c>modLib.formatValue</c> / <c>formatTags</c> running
    /// inside the engine, while the comparison side is rebuilt from
    /// <see cref="ModStoreDumpMod"/> — its value tree, its tags, its flag names, its source.
    /// A value shape the model flattened, a tag parameter it lost, or a double formatted as
    /// .NET's <c>R</c> instead of Lua's <c>%.14g</c> all surface here as a mismatched line.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Builds))]
    public void Print_RoundTripsThroughTheModel(string slug)
    {
        ModStoreDump dump = ModStoreDumpReader.Load(slug);

        List<string> failures = [];
        int compared = 0;

        foreach (ModStoreDumpStore store in dump.Stores)
        {
            foreach (ModStoreDumpMod mod in store.Mods)
            {
                compared++;
                string rebuilt = ModStoreTextFormat.Print(mod);

                if (!string.Equals(rebuilt, mod.Print, StringComparison.Ordinal))
                {
                    if (failures.Count < 25)
                    {
                        failures.Add(
                            $"{store.Name}/{mod.Name}{Environment.NewLine}"
                            + $"    lua: {mod.Print}{Environment.NewLine}"
                            + $"    c# : {rebuilt}");
                    }
                }
            }
        }

        Assert.True(compared > 0, "the dump contained no mods at all");
        Assert.True(
            failures.Count == 0,
            $"{failures.Count} of {compared} mods do not render identically:{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Mods are emitted in a canonical order. The generator sorts on the encoded mod, which
    /// begins with the name, so names come out non-decreasing; anything else means a dump was
    /// hand-edited or generated by an older script, and a golden whose order depends on Lua's
    /// <c>pairs()</c> is noise waiting to happen.
    /// </summary>
    [Theory]
    [MemberData(nameof(Builds))]
    public void Mods_AreInCanonicalOrder(string slug)
    {
        ModStoreDump dump = ModStoreDumpReader.Load(slug);

        foreach (ModStoreDumpStore store in dump.Stores)
        {
            string[] names = store.Mods.Select(static m => m.Name).ToArray();
            Assert.Equal(names.Order(StringComparer.Ordinal), names);

            Assert.Equal(store.Conditions.Order(StringComparer.Ordinal), store.Conditions);
            Assert.Equal(
                store.Multipliers.Select(static m => m.Name).Order(StringComparer.Ordinal),
                store.Multipliers.Select(static m => m.Name));
        }
    }

    /// <summary>
    /// Sources. <c>modLib.createMod</c> assigns <c>source</c> only when its fourth positional
    /// argument is a string and <c>flags</c> only when the fifth is a number
    /// (<c>src/Modules/ModTools.lua:38-49</c>), so a caller that passes them in the wrong
    /// order silently produces a mod sourced to nothing. Every query in <c>ModStore</c>
    /// filters on the source prefix, which makes that a whole-build-wrong bug that no output
    /// number points at. Pinning the distribution here is how the port notices.
    /// </summary>
    [Theory]
    [MemberData(nameof(Builds))]
    public void Sources_AreAttributed(string slug)
    {
        ModStoreDump dump = ModStoreDumpReader.Load(slug);

        foreach (ModStoreDumpStore store in dump.Stores)
        {
            foreach (ModStoreDumpMod mod in store.Mods)
            {
                // The Print() line spells a missing source "?", never an empty column.
                Assert.EndsWith("|" + (mod.Source ?? "?"), mod.Print, StringComparison.Ordinal);
            }

            // A store where nothing at all carries a source would mean createMod's
            // positional sniffing went wrong across the board.
            Assert.Contains(store.Mods, static m => m.Source is not null);
        }

        // The prefixes ModStore's source filtering actually keys on. Every build has tree
        // nodes, items and config, and the engine seeds the character with "Base".
        HashSet<string> prefixes = dump.Stores
            .SelectMany(static s => s.Mods)
            .Select(static m => m.SourcePrefix)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string expected in new[] { "Base", "Tree", "Item", "Config" })
        {
            Assert.Contains(expected, prefixes, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Flag words and their rendered names agree, in both directions. The names come from the
    /// dump (deriving them needs the <c>ModFlag</c> / <c>KeywordFlag</c> tables, which are
    /// ticket 09's) but a zero word and a non-<c>"-"</c> name cannot both be right.
    /// </summary>
    [Theory]
    [MemberData(nameof(Builds))]
    public void FlagWords_AgreeWithFlagNames(string slug)
    {
        ModStoreDump dump = ModStoreDumpReader.Load(slug);

        foreach (ModStoreDumpMod mod in dump.Stores.SelectMany(static s => s.Mods))
        {
            Assert.Equal(mod.Flags == 0, mod.FlagNames == "-");
            Assert.Equal(mod.KeywordFlags == 0, mod.KeywordFlagNames == "-");
            Assert.True(mod.Flags >= 0, $"{mod.Name} has a negative flag word");
            Assert.True(mod.KeywordFlags >= 0, $"{mod.Name} has a negative keyword flag word");
        }
    }

    /// <summary>
    /// Non-vacuity. A dump that lost tags, or flattened every table value to a scalar, would
    /// still satisfy every check above while being useless for the job this harness exists to
    /// do — tags decide whether a mod applies at all (<c>ModStore:EvalMod</c>,
    /// <c>src/Classes/ModStore.lua:363-971</c>), and nested mod values are where source
    /// attribution goes wrong.
    /// </summary>
    [Fact]
    public void Corpus_CoversTheShapesThatMatter()
    {
        IReadOnlyList<ModStoreDump> corpus = Corpus.Value;

        Assert.Equal(5, corpus.Count);

        ModStoreDumpMod[] mods = corpus.SelectMany(static d => d.Stores).SelectMany(static s => s.Mods).ToArray();
        Assert.True(mods.Length > 4_000, $"corpus holds only {mods.Length} mods");

        ModStoreDumpTag[] tags = mods.SelectMany(static m => m.Tags).ToArray();
        Assert.True(tags.Length > 500, $"corpus holds only {tags.Length} tags");

        // The tag types EvalMod branches on, and which a dump without tags could not express.
        HashSet<string> tagTypes = tags.Select(static t => t.Type ?? "(untyped)").ToHashSet(StringComparer.Ordinal);
        foreach (string expected in new[]
        {
            "Condition", "ActorCondition", "Multiplier", "MultiplierThreshold",
            "PerStat", "SkillName", "SkillType", "SocketedIn", "GlobalEffect",
        })
        {
            Assert.Contains(expected, tagTypes, StringComparer.Ordinal);
        }

        // A tag with a varList, which formatTag renders differently from formatValue — the
        // one shape most likely to be lost by a naive reimplementation.
        Assert.Contains(tags, static t => t.Entries.Any(static e => e.Value is ModStoreListValue));

        // Value shapes, over the whole value tree rather than just its root: a radius-jewel
        // closure, for instance, sits one level down under `value.func`.
        ModStoreValue[] values = mods.SelectMany(static m => Flatten(m.Value)).ToArray();
        Assert.Contains(values, static v => v is ModStoreNumberValue);
        Assert.Contains(values, static v => v is ModStoreBooleanValue);
        Assert.Contains(values, static v => v is ModStoreStringValue);
        Assert.Contains(values, static v => v is ModStoreMapValue);
        Assert.Contains(values, static v => v is ModStoreListValue);
        Assert.Contains(values, static v => v is ModStoreFunctionValue);

        // { mod = <inner mod> }: a whole modifier nested inside a value, carrying its own
        // source, flags and tags.
        ModStoreDumpMod[] nested = values
            .OfType<ModStoreModValue>()
            .Select(static v => v.Mod)
            .ToArray();

        Assert.True(nested.Length > 20, $"corpus holds only {nested.Length} nested mods");
        Assert.Contains(nested, static m => m.Tags.Count > 0);

        // Non-integer values, which is where Lua's %.14g and .NET's default formatting part
        // company.
        Assert.Contains(
            mods,
            static m => m.Value is ModStoreNumberValue n && n.Value != Math.Truncate(n.Value));

        // Negative zero. The engine stores it (an inactive Chill contributes
        // `-0 INC ActionSpeed`) and Lua prints it as "-0", so a dump that wrote it as an
        // integer would silently drop the sign — the same trap ticket 02 exists for.
        Assert.Contains(
            mods,
            static m => m.Value is ModStoreNumberValue n && n.Value == 0 && double.IsNegative(n.Value));

        // Mods that flags and keyword flags actually gate.
        Assert.Contains(mods, static m => m.Flags != 0);
        Assert.Contains(mods, static m => m.KeywordFlags != 0);
    }

    /// <summary>
    /// <c>env.minion.modDB</c> is dumped when present, and it is present in no 3.13 test
    /// build: <c>env.minion</c> is <c>env.player.mainSkill.minion</c>
    /// (<c>src/Modules/CalcPerform.lua:1316</c>) and none of the five main skills summons.
    /// This test states that as a known gap rather than leaving the absence to be discovered
    /// later — a build whose main skill is a minion skill would exercise a store the port has
    /// no coverage for at all.
    /// </summary>
    [Fact]
    public void MinionStore_IsAbsentFromEveryCurrentTestBuild()
    {
        string[] withMinion = Corpus.Value
            .Where(static d => d.Stores.Any(static s => string.Equals(s.Name, "minionModDB", StringComparison.Ordinal)))
            .Select(static d => d.Build)
            .ToArray();

        Assert.Empty(withMinion);
    }

    /// <summary>
    /// The snapshot. Pins each build's store shape — counts, source and type distributions,
    /// tag types, plus the main skill's mod list line by line.
    /// </summary>
    [Theory]
    [MemberData(nameof(Builds))]
    public void Summary_MatchesSnapshot(string slug) =>
        ModStoreSnapshot.Verify(ModStoreSummary.SnapshotName(slug), ModStoreSummary.Render(ModStoreDumpReader.Load(slug)));

    /// <summary>A value and everything nested inside it, including nested mods' own values.</summary>
    private static IEnumerable<ModStoreValue> Flatten(ModStoreValue value)
    {
        yield return value;

        switch (value)
        {
            case ModStoreListValue list:
                foreach (ModStoreValue nested in list.Items.SelectMany(Flatten))
                {
                    yield return nested;
                }

                break;

            case ModStoreMapValue map:
                foreach (ModStoreValue nested in map.Entries.SelectMany(static e => Flatten(e.Value)))
                {
                    yield return nested;
                }

                break;

            case ModStoreModValue mod:
                foreach (ModStoreValue nested in Flatten(mod.Mod.Value))
                {
                    yield return nested;
                }

                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Lua's <c>%.14g</c>, stated independently of the corpus. If the corpus and
    /// <see cref="ModStoreLua"/> ever drifted together these would still hold.
    /// </summary>
    [Theory]
    [InlineData(0.0175, "0.0175")]
    [InlineData(18.016666666666666, "18.016666666667")]     // 14 significant digits, not 17
    [InlineData(-34.12384716732543, "-34.123847167325")]
    [InlineData(999999.0, "999999")]
    [InlineData(0.1, "0.1")]
    [InlineData(-0.5, "-0.5")]
    [InlineData(1.0 / 3.0, "0.33333333333333")]
    [InlineData(1e15, "1e+15")]
    [InlineData(1e-5, "1e-05")]
    [InlineData(0.0001, "0.0001")]
    [InlineData(-0.0, "-0")]                                // the sign survives; == 0.0 cannot see it
    [InlineData(0.0, "0")]
    public void LuaNumberFormatting_IsFourteenSignificantDigits(double value, string expected) =>
        Assert.Equal(expected, ModStoreLua.Number(value));
}
