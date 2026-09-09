using System.Globalization;
using System.Text.Json;
using Pob.Core;
using Xunit;

namespace Pob.Tests;

/// <summary>
/// Replays <c>oracles/luacompat.json</c> against <see cref="LuaCompat"/>.
/// </summary>
/// <remarks>
/// <para>
/// The corpus is produced by <c>dotnet/tools/luacompat-oracle/dump.lua</c>, which sources the
/// helper bodies verbatim out of <c>src/Modules/Common.lua</c> and evaluates them in a real
/// Lua interpreter. Regenerate it with
/// <c>luajit dotnet/tools/luacompat-oracle/dump.lua</c> after touching those helpers.
/// </para>
/// <para>
/// Comparison is on <see cref="BitConverter.DoubleToInt64Bits(double)"/>, never <c>==</c>:
/// <c>-0.0 == 0.0</c> is true and the whole point of this suite is that the sign of zero and
/// the last ulp survive the port.
/// </para>
/// </remarks>
public sealed class LuaCompatOracleTests
{
    private static readonly Lazy<Corpus> LoadedCorpus = new(Load, isThreadSafe: true);

    public static TheoryData<string> FunctionNames
    {
        get
        {
            TheoryData<string> data = [];
            foreach (string name in LoadedCorpus.Value.FunctionNames)
            {
                data.Add(name);
            }

            return data;
        }
    }

    /// <summary>
    /// Every case for one Lua function must reproduce bit for bit. Sharded per function so a
    /// regression names the primitive that broke instead of one opaque failure.
    /// </summary>
    [Theory]
    [MemberData(nameof(FunctionNames))]
    public void Function_MatchesLuaBitForBit(string function)
    {
        Corpus corpus = LoadedCorpus.Value;
        List<Case> cases = corpus.ByFunction[function];

        Assert.NotEmpty(cases);

        List<string> failures = [];

        foreach (Case c in cases)
        {
            double[] actual;

            try
            {
                actual = Invoke(c);
            }
            catch (Exception ex)
            {
                failures.Add($"{Describe(c)} threw {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            if (actual.Length != c.Results.Length)
            {
                failures.Add($"{Describe(c)} returned {actual.Length} values, Lua returned {c.Results.Length}");
                continue;
            }

            for (int i = 0; i < actual.Length; i++)
            {
                if (BitConverter.DoubleToInt64Bits(actual[i]) != BitConverter.DoubleToInt64Bits(c.Results[i]))
                {
                    failures.Add(
                        $"{Describe(c)} -> [{i}] {Show(actual[i])}, Lua says {Show(c.Results[i])}");
                }
            }

            if (failures.Count >= 25)
            {
                failures.Add("... (further failures suppressed)");
                break;
            }
        }

        Assert.True(
            failures.Count == 0,
            $"{function}: {failures.Count} of {cases.Count} corpus cases disagree with Lua.{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Guards the corpus itself. A dump script that silently emitted nothing, or a corpus that
    /// lost the shapes the ticket calls for, would make every test above vacuous.
    /// </summary>
    [Fact]
    public void Corpus_CoversTheShapesThatMatter()
    {
        Corpus corpus = LoadedCorpus.Value;

        Assert.True(corpus.Cases.Count > 20_000, $"corpus has only {corpus.Cases.Count} cases");

        // Every function LuaCompat claims to mirror is actually exercised.
        string[] required =
        [
            "round", "floor", "roundSymmetric", "alwaysPositiveRound", "floorSymmetric", "ceilSymmetric",
            "ceil_b", "floor_b", "math.floor", "math.ceil", "math.modf", "pow10",
            "floorSymmetric.multi", "alwaysPositiveRound.multi",
            "site.moreScale", "site.highPrecisionMore", "site.multiplierFloor",
            "site.scaleModValue", "site.scaleModValueHighPrecision",
        ];
        Assert.Equal(required.Order(StringComparer.Ordinal), corpus.FunctionNames.Order(StringComparer.Ordinal));

        // Distinct() on double would fold -0.0 into 0.0 and every NaN into one; dedupe on the
        // bit pattern instead, for the same reason the comparisons below use it.
        double[] inputs = corpus.Cases
            .Where(static c => c.Arguments.Length > 0 && c.Arguments[0] is not null)
            .Select(static c => c.Arguments[0]!.Value)
            .DistinctBy(BitConverter.DoubleToInt64Bits)
            .ToArray();

        Assert.Contains(inputs, static v => BitConverter.DoubleToInt64Bits(v) == BitConverter.DoubleToInt64Bits(-0.0));
        Assert.Contains(inputs, static v => v == 2.5);
        Assert.Contains(inputs, static v => v == -2.5);
        Assert.Contains(inputs, double.IsNaN);
        Assert.Contains(inputs, double.IsPositiveInfinity);
        Assert.Contains(inputs, double.IsNegativeInfinity);
        Assert.Contains(inputs, static v => v == double.Epsilon);                 // 5e-324, the smallest subnormal
        Assert.Contains(inputs, static v => v == double.MaxValue);
        Assert.Contains(inputs, static v => v == -double.MaxValue);
        Assert.Contains(inputs, static v => v == 9007199254740992.0);             // 2^53
        Assert.Contains(inputs, static v => v == 0.1 + 0.2);                      // not 0.3

        // Values a hair either side of an exact half, which is where half-up and
        // half-to-even part company.
        Assert.Contains(inputs, static v => v is > 2.5 and < 2.5000000000001);
        Assert.Contains(inputs, static v => v is < 2.5 and > 2.4999999999999);

        // Values whose product with 10^dec is not exactly representable: (k + 0.5) / 100
        // times 100 lands just under or just over the true half.
        Assert.Contains(inputs, static v => v == 2.5 / 100.0);
        Assert.Contains(inputs, static v => v == 1.0 / 3.0);

        // Non-vacuity of the corpus for the ticket's four call sites.
        Assert.True(corpus.ByFunction["site.moreScale"].Count > 1000);
        Assert.True(corpus.ByFunction["site.multiplierFloor"].Count > 1000);
        Assert.True(corpus.ByFunction["site.scaleModValue"].Count > 1000);
        Assert.True(corpus.ByFunction["site.highPrecisionMore"].Count > 500);
        Assert.True(corpus.ByFunction["site.scaleModValueHighPrecision"].Count > 500);
    }

    /// <summary>
    /// The half-rounding rules, spelled out independently of the corpus. If the dump script and
    /// LuaCompat ever drifted together, these would still hold Lua's documented behaviour.
    /// </summary>
    [Fact]
    public void Round_IsHalfUpTowardPositiveInfinity_NotBankers()
    {
        Assert.Equal(3.0, LuaCompat.Round(2.5));
        Assert.Equal(2.0, LuaCompat.Round(1.5));
        Assert.Equal(1.0, LuaCompat.Round(0.5));

        // Banker's rounding would give -2 for -1.5 and 2 for 2.5 by a different route; the
        // asymmetry below is the tell. Lua's round() is floor(val + 0.5), full stop.
        Assert.Equal(-2.0, LuaCompat.Round(-2.5));
        Assert.Equal(-1.0, LuaCompat.Round(-1.5));
        Assert.Equal(0.0, LuaCompat.Round(-0.5));

        // roundSymmetric is the one that goes away from zero.
        Assert.Equal(-3.0, LuaCompat.RoundSymmetric(-2.5));
        Assert.Equal(3.0, LuaCompat.RoundSymmetric(2.5));
    }

    /// <summary>
    /// The sign of zero, which <c>==</c> cannot see and which the engine carries into products.
    /// </summary>
    [Fact]
    public void SignedZero_IsPreserved()
    {
        AssertBits(-0.0, LuaCompat.MathFloor(-0.0));
        AssertBits(-0.0, LuaCompat.MathCeil(-0.5));
        AssertBits(-0.0, LuaCompat.Truncate(-0.5));
        AssertBits(-0.0, LuaCompat.Modf(-0.0).Integral);
        AssertBits(-0.0, LuaCompat.Modf(-0.0).Fractional);
        AssertBits(-0.0, LuaCompat.Modf(-3.0).Fractional);
        AssertBits(0.0, LuaCompat.Modf(3.0).Fractional);

        // floor(-0.0) keeps the sign; round(-0.0) does not, because it adds 0.5 first.
        AssertBits(-0.0, LuaCompat.Floor(-0.0));
        AssertBits(0.0, LuaCompat.Round(-0.0));
    }

    /// <summary>
    /// The <c>0.0001</c> epsilon, which is the difference between a Multiplier tag reading 3 and
    /// reading 2 when the division lands one ulp short.
    /// </summary>
    [Fact]
    public void MultiplierFloor_AbsorbsTheUlpShortfall()
    {
        const double JustUnderThree = 2.9999999999999996;   // the double immediately below 3

        Assert.Equal(2.0, LuaCompat.MathFloor(JustUnderThree));
        Assert.Equal(3.0, LuaCompat.MultiplierFloor(JustUnderThree, 1.0));
        Assert.Equal(3.0, LuaCompat.Floor(JustUnderThree, 0));

        // Same story on the negative side, where "one ulp short" means one ulp below -3.
        Assert.Equal(-4.0, LuaCompat.MathFloor(-3.0000000000000004));
        Assert.Equal(-3.0, LuaCompat.MultiplierFloor(-3.0000000000000004, 1.0));

        // ...but it is an epsilon, not a rounding mode: 2.9 still floors to 2.
        Assert.Equal(2.0, LuaCompat.MultiplierFloor(2.9, 1.0));
        Assert.Equal(2.0, LuaCompat.MultiplierFloor(2.999, 1.0));   // the epsilon is 1e-4, so 2.9999 would flip
    }

    /// <summary>
    /// The <c>select(1, math.modf(val))</c> quirk in <c>floorSymmetric</c>: it returns two
    /// values, and only the first survives an assignment.
    /// </summary>
    [Fact]
    public void FloorSymmetric_IsTheIntegralPartOfModf()
    {
        foreach (double v in new[] { 3.7, -3.7, 0.0, -0.0, -0.5, 1e300, double.NaN })
        {
            AssertBits(LuaCompat.Modf(v).Integral, LuaCompat.FloorSymmetric(v));
        }
    }

    /// <summary>
    /// <c>dec = 0</c> is not the same call as no <c>dec</c> at all: Lua treats 0 as truthy, so
    /// the epsilon branch is taken.
    /// </summary>
    [Fact]
    public void Floor_WithDecZero_IsNotTheSameAsFloorWithoutDec()
    {
        Assert.Equal(-1.0, LuaCompat.Floor(-0.00005));
        Assert.Equal(0.0, LuaCompat.Floor(-0.00005, 0));
    }

    /// <summary>
    /// ScaleAddMod keeps only the first return of <c>math.modf</c>, so the rounded product is
    /// truncated toward zero rather than rounded.
    /// </summary>
    [Fact]
    public void ScaleModValue_TruncatesAfterRounding()
    {
        Assert.Equal(2.0, LuaCompat.ScaleModValue(2.994, 1.0));
        Assert.Equal(-2.0, LuaCompat.ScaleModValue(-2.994, 1.0));
        Assert.Equal(1.0, LuaCompat.ScaleModValue(3.0, 1.0 / 3.0));
    }

    private static void AssertBits(double expected, double actual) =>
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));

    private static double[] Invoke(Case c)
    {
        double?[] a = c.Arguments;

        return c.Function switch
        {
            "round" => [Unary(a, LuaCompat.Round, LuaCompat.Round)],
            "floor" => [Unary(a, LuaCompat.Floor, LuaCompat.Floor)],
            "roundSymmetric" => [Unary(a, LuaCompat.RoundSymmetric, LuaCompat.RoundSymmetric)],
            "alwaysPositiveRound" => [Unary(a, LuaCompat.AlwaysPositiveRound, LuaCompat.AlwaysPositiveRound)],
            "floorSymmetric" => [Unary(a, LuaCompat.FloorSymmetric, LuaCompat.FloorSymmetric)],
            "ceilSymmetric" => [Unary(a, LuaCompat.CeilSymmetric, LuaCompat.CeilSymmetric)],
            "math.floor" => [LuaCompat.MathFloor(Arg(a, 0))],
            "math.ceil" => [LuaCompat.MathCeil(Arg(a, 0))],
            "math.modf" => Pair(LuaCompat.Modf(Arg(a, 0))),

            // Common.lua's no-dec `floorSymmetric` is `return select(1, math.modf(val))`, and
            // select(1, ...) yields every value from index 1 on, so the branch returns BOTH of
            // modf's results. alwaysPositiveRound tail-calls it and inherits that. Call sites
            // assign to a single variable and only ever see the first, which is what
            // LuaCompat.FloorSymmetric returns; these two families pin the raw form.
            "floorSymmetric.multi" => Pair(LuaCompat.Modf(Arg(a, 0))),
            "alwaysPositiveRound.multi" => Pair(LuaCompat.Modf(Arg(a, 0) + 0.5)),
            "pow10" => [LuaCompat.Pow10(Dec(a, 0))],
            "ceil_b" => [LuaCompat.CeilB(Arg(a, 0), Arg(a, 1))],
            "floor_b" => [LuaCompat.FloorB(Arg(a, 0), Arg(a, 1))],
            "site.moreScale" => [LuaCompat.MoreScale(Arg(a, 0))],
            "site.highPrecisionMore" => [LuaCompat.HighPrecisionMore(Arg(a, 0), Arg(a, 1), Dec(a, 2))],
            "site.multiplierFloor" => [LuaCompat.MultiplierFloor(Arg(a, 0), Arg(a, 1))],
            "site.scaleModValue" => [LuaCompat.ScaleModValue(Arg(a, 0), Arg(a, 1))],
            "site.scaleModValueHighPrecision" =>
                [LuaCompat.ScaleModValueHighPrecision(Arg(a, 0), Arg(a, 1), Dec(a, 2))],
            _ => throw new InvalidOperationException($"corpus names a function this test cannot dispatch: {c.Function}"),
        };
    }

    /// <summary>Dispatches to the no-dec or the dec overload, mirroring Lua's <c>if dec then</c>.</summary>
    private static double Unary(double?[] a, Func<double, double> withoutDec, Func<double, int, double> withDec) =>
        a.Length < 2 || a[1] is null ? withoutDec(Arg(a, 0)) : withDec(Arg(a, 0), Dec(a, 1));

    private static double[] Pair((double Integral, double Fractional) v) => [v.Integral, v.Fractional];

    private static double Arg(double?[] a, int index) =>
        a[index] ?? throw new InvalidOperationException($"corpus argument {index} is null where a number was expected");

    private static int Dec(double?[] a, int index)
    {
        double value = Arg(a, index);
        int dec = (int)value;
        return dec == value ? dec : throw new InvalidOperationException($"corpus dec {value} is not an integer");
    }

    private static string Show(double value) =>
        $"{value.ToString("R", CultureInfo.InvariantCulture)} (0x{BitConverter.DoubleToInt64Bits(value):X16})";

    private static string Describe(Case c) =>
        $"{c.Function}({string.Join(", ", c.Arguments.Select(static v => v is null ? "nil" : Show(v.Value)))})";

    private static Corpus Load()
    {
        string path = Path.Combine(SolutionDirectory(), "Pob.Tests", "oracles", "luacompat.json");
        Assert.True(File.Exists(path), $"oracle corpus missing at {path}; run dotnet/tools/luacompat-oracle/dump.lua");

        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;

        Assert.Equal(1, root.GetProperty("format").GetInt32());

        List<Case> cases = [];
        foreach (JsonElement element in root.GetProperty("cases").EnumerateArray())
        {
            string function = element[0].GetString()!;
            double?[] arguments = element[1].EnumerateArray().Select(ParseNullable).ToArray();
            double[] results = element[2].EnumerateArray().Select(static e => ParseNumber(e.GetString()!)).ToArray();
            cases.Add(new Case(function, arguments, results));
        }

        Assert.Equal(root.GetProperty("caseCount").GetInt32(), cases.Count);

        Dictionary<string, List<Case>> byFunction = new(StringComparer.Ordinal);
        foreach (Case c in cases)
        {
            if (!byFunction.TryGetValue(c.Function, out List<Case>? bucket))
            {
                bucket = [];
                byFunction[c.Function] = bucket;
            }

            bucket.Add(c);
        }

        return new Corpus(cases, byFunction);
    }

    private static double? ParseNullable(JsonElement element) =>
        element.ValueKind == JsonValueKind.Null ? null : ParseNumber(element.GetString()!);

    /// <summary>
    /// Corpus numbers are strings, in the shortest <c>%g</c> spelling that round-trips through
    /// the C library, plus explicit spellings for the values <c>%g</c> cannot express portably.
    /// .NET's parser is correctly rounded, so the string maps back to the identical double.
    /// </summary>
    private static double ParseNumber(string text) => text switch
    {
        "nan" => double.NaN,
        "inf" => double.PositiveInfinity,
        "-inf" => double.NegativeInfinity,
        "-0" => -0.0,
        _ => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
    };

    private static string SolutionDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PathOfBuilding.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    private sealed record Case(string Function, double?[] Arguments, double[] Results);

    private sealed record Corpus(IReadOnlyList<Case> Cases, Dictionary<string, List<Case>> ByFunction)
    {
        public IEnumerable<string> FunctionNames => ByFunction.Keys;
    }
}
