using BenchmarkDotNet.Attributes;
using Pob.Core;

namespace Pob.Benchmarks;

/// <summary>
/// Baselines for the <see cref="LuaCompat"/> numeric layer (migration tickets 02 and 08).
/// </summary>
/// <remarks>
/// <para>
/// Ticket 08 asks for baselines on <c>Sum</c>, <c>More</c>, <c>EvalMod</c> and
/// <c>calcDamage</c>. None of those exist in C# yet -- they arrive with issues 11, 12 and
/// 22-27 -- and a benchmark over a placeholder measures the placeholder. What does exist is
/// <see cref="LuaCompat"/>, and it is the right first baseline anyway: every one of those four
/// is built out of these calls. <c>ModDB:More</c> rounds to two decimals once per mod name
/// (<c>src/Classes/ModDB.lua:197</c>), a Multiplier tag floors with an epsilon on every
/// evaluation (<c>src/Classes/ModStore.lua:403</c>), and <c>ScaleAddMod</c> does a
/// round-then-modf per scaled mod (<c>src/Classes/ModStore.lua:82</c>). If these primitives are
/// slow, everything above them is slow, and the cause will be invisible by then.
/// </para>
/// <para>
/// Each Lua-compatible helper is paired with the raw <see cref="Math"/> call it replaces, so
/// the report answers a specific question: what does bit-for-bit Lua compatibility cost over
/// the naive .NET call? That number is what justifies -- or eventually challenges -- the
/// banned-API rule in <c>dotnet/BannedSymbols.txt</c>.
/// </para>
/// <para>
/// Every benchmark returns an accumulated value. Returning a value is what stops the JIT
/// deleting the loop, which is the classic way a microbenchmark comes back at zero nanoseconds
/// and is believed.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class LuaCompatBenchmarks
{
    private double[] _values = [];
    private double[] _divisors = [];

    /// <summary>Load the shared input set once per process.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _values = BenchmarkInputs.Values;
        _divisors = BenchmarkInputs.Divisors;
    }

    /// <summary>The .NET call <see cref="LuaCompat.Round(double)"/> replaces.</summary>
    /// <returns>An accumulator, to defeat dead-code elimination.</returns>
    [Benchmark(Baseline = true, Description = "Math.Round (banker's, wrong for the engine)")]
    public double MathRound()
    {
        double total = 0.0;
        foreach (double value in _values)
        {
            total += Math.Round(value);
        }

        return total;
    }

    /// <summary>Lua's <c>round</c>: <c>floor(v + 0.5)</c>, half-up toward positive infinity.</summary>
    /// <returns>An accumulator.</returns>
    [Benchmark(Description = "LuaCompat.Round")]
    public double Round()
    {
        double total = 0.0;
        foreach (double value in _values)
        {
            total += LuaCompat.Round(value);
        }

        return total;
    }

    /// <summary>Lua's <c>round(v, 2)</c>, the form <c>ModDB:More</c> uses on every mod name.</summary>
    /// <returns>An accumulator.</returns>
    [Benchmark(Description = "LuaCompat.Round(v, 2)")]
    public double RoundToTwoPlaces()
    {
        double total = 0.0;
        foreach (double value in _values)
        {
            total += LuaCompat.Round(value, 2);
        }

        return total;
    }

    /// <summary><c>math.floor</c>.</summary>
    /// <returns>An accumulator.</returns>
    [Benchmark(Description = "LuaCompat.MathFloor")]
    public double MathFloor()
    {
        double total = 0.0;
        foreach (double value in _values)
        {
            total += LuaCompat.MathFloor(value);
        }

        return total;
    }

    /// <summary>Common.lua's global <c>floor</c>, which carries a +0.0001 epsilon.</summary>
    /// <returns>An accumulator.</returns>
    [Benchmark(Description = "LuaCompat.Floor(v, 2)")]
    public double FloorToTwoPlaces()
    {
        double total = 0.0;
        foreach (double value in _values)
        {
            total += LuaCompat.Floor(value, 2);
        }

        return total;
    }

    /// <summary><c>roundSymmetric</c>: half away from zero.</summary>
    /// <returns>An accumulator.</returns>
    [Benchmark(Description = "LuaCompat.RoundSymmetric")]
    public double RoundSymmetric()
    {
        double total = 0.0;
        foreach (double value in _values)
        {
            total += LuaCompat.RoundSymmetric(value);
        }

        return total;
    }

    /// <summary><c>math.modf</c>, including its sign-of-zero rules.</summary>
    /// <returns>An accumulator.</returns>
    [Benchmark(Description = "LuaCompat.Modf")]
    public double Modf()
    {
        double total = 0.0;
        foreach (double value in _values)
        {
            (double integral, double fractional) = LuaCompat.Modf(value);
            total += integral + fractional;
        }

        return total;
    }

    /// <summary>
    /// The <c>More</c> call site: <c>round(modResult, 2)</c> per mod name
    /// (<c>src/Classes/ModDB.lua:197</c>).
    /// </summary>
    /// <returns>An accumulator.</returns>
    [Benchmark(Description = "site: ModDB:More scale")]
    public double MoreScale()
    {
        double total = 0.0;
        foreach (double value in _values)
        {
            total += LuaCompat.MoreScale(value);
        }

        return total;
    }

    /// <summary>
    /// The Multiplier tag call site: <c>floor(base/div + 0.0001)</c>
    /// (<c>src/Classes/ModStore.lua:403</c>). Evaluated once per tagged mod per query, which
    /// makes it the hottest single line in the whole mod engine.
    /// </summary>
    /// <returns>An accumulator.</returns>
    [Benchmark(Description = "site: Multiplier tag floor")]
    public double MultiplierFloor()
    {
        double total = 0.0;
        for (int i = 0; i < _values.Length; i++)
        {
            total += LuaCompat.MultiplierFloor(_values[i], _divisors[i]);
        }

        return total;
    }

    /// <summary>
    /// The <c>ScaleAddMod</c> call site: <c>modf(round(v * scale, 2))</c>
    /// (<c>src/Classes/ModStore.lua:82</c>).
    /// </summary>
    /// <returns>An accumulator.</returns>
    [Benchmark(Description = "site: ScaleAddMod value scale")]
    public double ScaleModValue()
    {
        double total = 0.0;
        for (int i = 0; i < _values.Length; i++)
        {
            total += LuaCompat.ScaleModValue(_values[i], _divisors[i]);
        }

        return total;
    }

    /// <summary>
    /// The <c>data.highPrecisionMods</c> path, which floors with no epsilon at all
    /// (<c>src/Classes/ModDB.lua:194-195</c>).
    /// </summary>
    /// <returns>An accumulator.</returns>
    [Benchmark(Description = "site: high-precision More")]
    public double HighPrecisionMore()
    {
        double total = 0.0;
        for (int i = 0; i < _values.Length; i++)
        {
            total += LuaCompat.HighPrecisionMore(_values[i], _divisors[i], 3);
        }

        return total;
    }
}
