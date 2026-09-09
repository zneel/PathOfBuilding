using System.Globalization;
using Pob.Tests.Infrastructure;
using Xunit;

namespace Pob.Tests;

/// <summary>
/// Guards the differential-fuzz corpus produced by <c>dotnet/tools/fuzz/generate.lua</c>.
/// </summary>
/// <remarks>
/// <para>
/// The differential half of ticket 08 -- generate a random build, run it through both engines,
/// diff the outputs -- needs a second engine. There is none until issues 22-27 land. What
/// exists now is the generator and the reference engine's answers, and those are worth
/// guarding on their own: the corpus is the input to that future diff, and a corpus that
/// silently went empty, lost its determinism guarantees, or started recording engine failures
/// would take the diff down with it.
/// </para>
/// <para>
/// So these tests assert the properties the future differential test will rely on, and the
/// findings the fuzz run produced. When <c>Pob.Calc</c> exists, the test to add here is: replay
/// <c>build.Recipe</c> through it and run the two output sets through <see cref="CorpusDiff"/>.
/// </para>
/// </remarks>
public sealed class FuzzCorpusTests
{
    private static readonly Lazy<FuzzCorpus> Corpus = new(FuzzCorpus.LoadCorpus, isThreadSafe: true);
    private static readonly Lazy<FuzzCorpus> Plan = new(FuzzCorpus.LoadPlan, isThreadSafe: true);

    /// <summary>
    /// The corpus is big enough and varied enough to be worth diffing against.
    /// </summary>
    [Fact]
    public void Corpus_IsSubstantive()
    {
        FuzzCorpus corpus = Corpus.Value;

        Assert.Equal("corpus", corpus.Kind);
        Assert.Equal(corpus.Count, corpus.Builds.Count);
        Assert.True(corpus.Builds.Count >= 25, $"corpus has only {corpus.Builds.Count} builds");
        Assert.False(string.IsNullOrEmpty(corpus.TreeVersion));

        // Randomness actually varied the inputs rather than producing the same build N times.
        Assert.True(corpus.Builds.Select(static b => b.Recipe.ClassName).Distinct(StringComparer.Ordinal).Count() >= 4);
        Assert.True(corpus.Builds.Sum(static b => b.Recipe.Items.Count) >= 100);
        Assert.True(corpus.Builds.Sum(static b => b.Recipe.NodeIds.Count) >= 100);
        Assert.True(corpus.Builds.Sum(static b => b.Recipe.ConfigVars.Count) >= 100);
        Assert.Contains(corpus.Builds, static b => b.Recipe.CustomMods.Length > 0);

        // Every build that calculated produced a full output set, not a stub.
        foreach (FuzzBuild build in corpus.Builds.Where(static b => b.Outputs.Count > 0))
        {
            Assert.True(
                build.Outputs.Count >= 300,
                string.Create(CultureInfo.InvariantCulture, $"build {build.Recipe.Index} has only {build.Outputs.Count} outputs"));
        }

        Assert.True(
            corpus.Builds.SelectMany(static b => b.Outputs.Keys).Distinct(StringComparer.Ordinal).Count() >= 400,
            "corpus covers fewer than 400 distinct output keys");
    }

    /// <summary>
    /// Every build's status is accounted for, and any engine failure is visible rather than
    /// buried.
    /// </summary>
    /// <remarks>
    /// A failure here is a FINDING about the reference engine, not necessarily a broken test:
    /// the generator produces item and mod combinations no player would build, and the whole
    /// point is to see what the engine does with them. The assertion is that failures are
    /// *recorded*, with outputs withheld -- reading a stale <c>mainOutput</c> after a swallowed
    /// error is the trap <c>tools/fuzz/apply.lua</c> exists to avoid.
    /// </remarks>
    [Fact]
    public void Corpus_RecordsEveryBuildOutcome()
    {
        string[] known = ["ok", "partial", "error"];
        List<string> problems = [];

        foreach (FuzzBuild build in Corpus.Value.Builds)
        {
            if (!known.Contains(build.Status, StringComparer.Ordinal))
            {
                problems.Add($"build {build.Recipe.Index}: unknown status '{build.Status}'");
                continue;
            }

            if (string.Equals(build.Status, "ok", StringComparison.Ordinal))
            {
                if (build.Notes.Count > 0)
                {
                    problems.Add($"build {build.Recipe.Index}: status ok but {build.Notes.Count} notes recorded");
                }

                if (build.Outputs.Count == 0)
                {
                    problems.Add($"build {build.Recipe.Index}: status ok but no outputs");
                }
            }

            if (string.Equals(build.Status, "error", StringComparison.Ordinal) && build.Outputs.Count > 0)
            {
                problems.Add($"build {build.Recipe.Index}: status error but outputs were recorded anyway");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// The plan and the corpus were generated from the same seed, and the plan's first N
    /// recipes are exactly the corpus's N recipes.
    /// </summary>
    /// <remarks>
    /// This is the property that makes the generator's per-build seeding worth having: each
    /// build draws from its own stream, derived from the master, so build 40's recipe does not
    /// move when build 39's branch changes and a longer plan is a strict superset of a shorter
    /// one. If that ever stopped holding, a corpus and a plan generated from the same seed
    /// would describe different builds, and the differential diff would compare the wrong pairs.
    /// </remarks>
    [Fact]
    public void Plan_AndCorpus_AgreeOnTheSameSeed()
    {
        FuzzCorpus plan = Plan.Value;
        FuzzCorpus corpus = Corpus.Value;

        Assert.Equal("plan", plan.Kind);
        Assert.Equal(plan.Seed, corpus.Seed);
        Assert.True(plan.Builds.Count >= corpus.Builds.Count);

        for (int i = 0; i < corpus.Builds.Count; i++)
        {
            FuzzRecipe fromPlan = plan.Builds[i].Recipe;
            FuzzRecipe fromCorpus = corpus.Builds[i].Recipe;

            Assert.True(
                fromPlan.Matches(fromCorpus),
                $"recipe {i + 1} differs between the plan and the corpus generated from the same seed");
        }
    }

    /// <summary>
    /// Recipes are self-consistent: indices in order, seeds distinct, ids in range, and item
    /// text in the shape <c>ItemsTab:CreateDisplayItemFromRaw</c> expects.
    /// </summary>
    [Fact]
    public void Recipes_AreWellFormed()
    {
        List<string> problems = [];
        HashSet<double> seeds = [];

        foreach ((FuzzBuild build, int position) in Corpus.Value.Builds.Select(static (b, i) => (b, i)))
        {
            FuzzRecipe recipe = build.Recipe;
            string where = $"build {recipe.Index}";

            if (recipe.Index != position + 1)
            {
                problems.Add($"{where}: index does not match position {position + 1}");
            }

            if (!seeds.Add(recipe.Seed))
            {
                problems.Add($"{where}: seed {LuaNumber.Format(recipe.Seed)} is reused");
            }

            if (recipe.ClassId is < 0 or > 6)
            {
                problems.Add($"{where}: classId {recipe.ClassId} out of range");
            }

            if (recipe.AscendClassId is < 0 or > 3)
            {
                problems.Add($"{where}: ascendClassId {recipe.AscendClassId} out of range");
            }

            if (recipe.Level is < 1 or > 100)
            {
                problems.Add($"{where}: level {recipe.Level} out of range");
            }

            foreach (string raw in recipe.Items)
            {
                if (!raw.StartsWith("Rarity: ", StringComparison.Ordinal))
                {
                    problems.Add($"{where}: item text does not start with a Rarity line");
                    break;
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// The corpus reader really reads the Lua number spelling, including the values that a
    /// careless reader would flatten.
    /// </summary>
    /// <remarks>
    /// Engine outputs are overwhelmingly finite and mostly whole, so this suite would still
    /// pass with a reader that mishandled non-finite values and signed zero -- right up until
    /// the C# engine produced one and the diff quietly agreed with the wrong thing. Pin the
    /// convention directly instead of hoping the corpus exercises it.
    /// </remarks>
    [Fact]
    public void LuaNumber_RoundTripsTheSpellingsTheCorpusUses()
    {
        Assert.Equal(double.PositiveInfinity, LuaNumber.Parse("inf"));
        Assert.Equal(double.NegativeInfinity, LuaNumber.Parse("-inf"));
        Assert.True(double.IsNaN(LuaNumber.Parse("nan")));

        Assert.True(double.IsNegative(LuaNumber.Parse("-0")));
        Assert.False(double.IsNegative(LuaNumber.Parse("0")));

        Assert.Equal(1e+02, LuaNumber.Parse("1e+02"));
        Assert.Equal(0.1 + 0.2, LuaNumber.Parse("0.30000000000000004"));

        Assert.Equal("inf", LuaNumber.Format(double.PositiveInfinity));
        Assert.Equal("-0", LuaNumber.Format(-0.0));
        Assert.Equal("100", LuaNumber.Format(100.0));
        Assert.Equal("1.5", LuaNumber.Format(1.5));
    }

    /// <summary>
    /// Output values keep their Lua type through the reader: a boolean stays a boolean rather
    /// than collapsing to 1, which is the distinction the tolerance ladder's first rung rests
    /// on.
    /// </summary>
    [Fact]
    public void Outputs_KeepTheirLuaTypes()
    {
        IEnumerable<ScalarValue> values = Corpus.Value.Builds.SelectMany(static b => b.Outputs.Values);

        Assert.Contains(values, static v => v.Kind == ScalarKind.Number);
        Assert.Contains(values, static v => v.Kind == ScalarKind.Boolean);
        Assert.Contains(values, static v => v.IsIntegralNumber);
        Assert.Contains(values, static v => v.Kind == ScalarKind.Number && !v.IsIntegralNumber);
    }
}
