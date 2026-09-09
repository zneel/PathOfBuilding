namespace Pob.Benchmarks;

/// <summary>
/// The fixed input set every numeric benchmark runs over.
/// </summary>
/// <remarks>
/// <para>
/// Generated from a Lehmer LCG rather than <see cref="Random"/>, for the same reason
/// <c>dotnet/tools/fuzz/rng.lua</c> and <c>dotnet/tools/luacompat-oracle/dump.lua</c> use one:
/// the numbers have to be identical on every machine and every runtime version, or a benchmark
/// comparison across two commits is comparing two different workloads. <see cref="Random"/>
/// with a fixed seed is not a stable contract -- .NET has changed its algorithm.
/// </para>
/// <para>
/// The mix matters as much as the reproducibility. Rounding helpers branch on sign and on
/// whether the value sits exactly on a half, so an input set of well-behaved positive numbers
/// would measure only the fast path. This mixes magnitudes across six orders, both signs, and
/// deliberately includes exact halves.
/// </para>
/// </remarks>
public static class BenchmarkInputs
{
    private const int Modulus = 2147483647;   // 2^31 - 1
    private const int Multiplier = 16807;

    /// <summary>How many values each benchmark iteration walks.</summary>
    public const int Count = 4096;

    /// <summary>The input values.</summary>
    public static double[] Values { get; } = Build();

    /// <summary>Second operands, for the two-argument helpers.</summary>
    public static double[] Divisors { get; } = BuildDivisors();

    private static double[] Build()
    {
        double[] values = new double[Count];
        long state = 20260909;

        for (int i = 0; i < Count; i++)
        {
            state = state * Multiplier % Modulus;
            double unit = (double)(state - 1) / (Modulus - 1);

            values[i] = (i % 8) switch
            {
                0 => (unit - 0.5) * 2.0,              // around zero, both signs
                1 => (unit - 0.5) * 2_000.0,          // typical stat magnitudes
                2 => (unit - 0.5) * 2_000_000.0,      // DPS magnitudes
                3 => unit * 1e-3,                     // small positives
                4 => -unit * 1e-3,                    // small negatives
                5 => Math.Floor(unit * 200.0) + 0.5,  // exact halves: the rounding fork
                6 => -(Math.Floor(unit * 200.0) + 0.5),
                _ => unit * 1e6,
            };
        }

        return values;
    }

    private static double[] BuildDivisors()
    {
        double[] divisors = new double[Count];
        long state = 424242;

        for (int i = 0; i < Count; i++)
        {
            state = state * Multiplier % Modulus;
            // Never zero: these stand in for a Multiplier tag's divisor
            // (src/Classes/ModStore.lua:403), which the engine never calls with zero.
            divisors[i] = 1.0 + ((double)(state - 1) / (Modulus - 1) * 99.0);
        }

        return divisors;
    }
}
