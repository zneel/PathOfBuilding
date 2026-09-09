namespace Pob.Core;

/// <summary>
/// Bit-exact ports of the arithmetic primitives the Lua engine rounds with.
/// </summary>
/// <remarks>
/// <para>
/// Every method here mirrors a specific line of the Lua tree, named in its own doc comment,
/// and is verified against a corpus dumped from the real interpreter
/// (<c>dotnet/tools/luacompat-oracle/dump.lua</c> → <c>Pob.Tests/oracles/luacompat.json</c>,
/// replayed by <c>LuaCompatOracleTests</c>). The corpus compares
/// <see cref="BitConverter.DoubleToInt64Bits(double)"/>, so negative zero and the last ulp
/// are part of the contract.
/// </para>
/// <para>
/// Two things about the originals are load-bearing and easy to get wrong:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <c>round</c> is <b>half-up toward positive infinity</b> — <c>floor(val + 0.5)</c>. It is not
/// symmetric (<c>round(-2.5)</c> is <c>-2</c>, not <c>-3</c>) and it is emphatically not the
/// banker's rounding that <c>Math.Round</c> does by default. Getting this wrong
/// drifts every DPS number in the fourth decimal.
/// </description>
/// </item>
/// <item>
/// <description>
/// Everything is <see cref="double"/>. Lua 5.1 has exactly one number type, so
/// <see cref="decimal"/> anywhere in the engine is a bug, not a precision improvement.
/// </description>
/// </item>
/// </list>
/// <para>
/// A note on <c>dec</c>: Lua treats <c>0</c> as truthy, so <c>floor(v, 0)</c> takes the
/// <c>if dec then</c> branch and still adds the epsilon, while <c>floor(v)</c> does not. The
/// no-argument and the <c>dec</c> overloads here are therefore genuinely different functions
/// and both are exercised by the corpus.
/// </para>
/// </remarks>
public static class LuaCompat
{
    // ---------------------------------------------------------------------------------
    // Raw math.* primitives.
    //
    // This is the only place in Pob.Core/Pob.Calc/Pob.Parsing allowed to call
    // System.Math's rounding functions; BannedSymbols.txt forbids them everywhere else so
    // that no call site can quietly acquire banker's rounding. The suppression is scoped to
    // these four wrappers, which do nothing but forward.
    // ---------------------------------------------------------------------------------
#pragma warning disable RS0030 // Do not use banned APIs — LuaCompat is the sanctioned wrapper.

    /// <summary>
    /// Lua's <c>math.floor</c> (<c>lmathlib.c</c>: C <c>floor()</c> on a double).
    /// Identical to <c>Math.Floor</c> including NaN, the infinities and
    /// <c>floor(-0.0) == -0.0</c>.
    /// </summary>
    public static double MathFloor(double val) => Math.Floor(val);

    /// <summary>
    /// Lua's <c>math.ceil</c> (<c>lmathlib.c</c>: C <c>ceil()</c> on a double).
    /// Note <c>ceil(-0.5) == -0.0</c>, which <c>Math.Ceiling</c> also produces.
    /// </summary>
    public static double MathCeil(double val) => Math.Ceiling(val);

    /// <summary>
    /// Lua's <c>math.modf</c> (<c>lmathlib.c</c>: C <c>modf()</c>), returning the integral part
    /// truncated toward zero and the fractional remainder, both carrying the sign of the input.
    /// </summary>
    /// <remarks>
    /// The sign rules are the reason this is not simply <c>(Math.Truncate(v), v - Math.Truncate(v))</c>:
    /// C's <c>modf</c> gives <c>-0.0</c> for the fraction of any negative whole number and for
    /// <c>-0.0</c> itself, and gives a <em>zero</em> fraction (not NaN) for the infinities.
    /// Used by <c>floorSymmetric</c> (<c>src/Modules/Common.lua:766</c>) and by
    /// <c>ScaleAddMod</c> (<c>src/Classes/ModStore.lua:82</c>).
    /// </remarks>
    public static (double Integral, double Fractional) Modf(double val)
    {
        if (double.IsNaN(val))
        {
            return (val, val);
        }

        if (double.IsInfinity(val))
        {
            return (val, Math.CopySign(0.0, val));
        }

        double integral = Math.Truncate(val);
        double fractional = val - integral;

        // val - integral loses the sign of a zero fraction; C modf keeps it.
        return (integral, fractional == 0.0 ? Math.CopySign(0.0, val) : fractional);
    }

    /// <summary>
    /// Lua's <c>10 ^ dec</c>. Lua's <c>^</c> operator is C <c>pow()</c> on doubles, which is what
    /// <c>Math.Pow</c> forwards to.
    /// </summary>
    public static double Pow10(int dec) => Math.Pow(10.0, dec);

#pragma warning restore RS0030

    /// <summary>
    /// The integral part of <c>math.modf</c>, i.e. Lua's <c>select(1, math.modf(val))</c>.
    /// Truncation toward zero; <c>Truncate(-0.0)</c> is <c>-0.0</c>.
    /// </summary>
    public static double Truncate(double val) => Modf(val).Integral;

    // ---------------------------------------------------------------------------------
    // src/Modules/Common.lua globals.
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>round(val)</c> — <c>src/Modules/Common.lua:709</c>, <c>m_floor(val + 0.5)</c>.
    /// Half-up <b>toward positive infinity</b>: <c>Round(2.5) == 3</c> but <c>Round(-2.5) == -2</c>.
    /// </summary>
    public static double Round(double val) => MathFloor(val + 0.5);

    /// <summary>
    /// <c>round(val, dec)</c> — <c>src/Modules/Common.lua:709</c>,
    /// <c>m_floor(val * 10 ^ dec + 0.5) / 10 ^ dec</c>.
    /// </summary>
    /// <remarks>
    /// This is the <c>round(modResult, 2)</c> applied once per mod name inside <c>More</c>
    /// (<c>src/Classes/ModDB.lua:197</c>, <c>src/Classes/ModList.lua:147</c>). It is not
    /// cosmetic — it changes results, and it is applied per name rather than per query.
    /// </remarks>
    public static double Round(double val, int dec)
    {
        double power = Pow10(dec);
        return MathFloor((val * power) + 0.5) / power;
    }

    /// <summary>
    /// <c>floor(val)</c> — <c>src/Modules/Common.lua:721</c>, plain <c>m_floor(val)</c>.
    /// </summary>
    /// <remarks>
    /// Common.lua defines <c>floor</c> as a <em>global</em>. That shadows nothing:
    /// <c>math.floor</c> is a table field and stays untouched, so a Lua call site written
    /// <c>math.floor(x)</c> or <c>m_floor(x)</c> means <see cref="MathFloor(double)"/>, and only a
    /// bare <c>floor(x)</c> means this. The distinction matters because this overload's sibling
    /// carries an epsilon and <see cref="MathFloor(double)"/> does not.
    /// </remarks>
    public static double Floor(double val) => MathFloor(val);

    /// <summary>
    /// <c>floor(val, dec)</c> — <c>src/Modules/Common.lua:721</c>,
    /// <c>local mult = 10 ^ dec; m_floor(val * mult + 0.0001) / mult</c>.
    /// </summary>
    /// <remarks>
    /// The <c>+ 0.0001</c> absorbs a quotient that lands an ulp short of the integer it should
    /// have been. The same epsilon is hand-rolled inline at <c>src/Classes/ModStore.lua:403</c>
    /// — see <see cref="MultiplierFloor(double, double)"/>, which is <em>not</em> a call to this
    /// helper and must not be routed through it.
    /// </remarks>
    public static double Floor(double val, int dec)
    {
        double mult = Pow10(dec);
        return MathFloor((val * mult) + 0.0001) / mult;
    }

    /// <summary>
    /// <c>roundSymmetric(val)</c> — <c>src/Modules/Common.lua:733</c>. Half <b>away from zero</b>:
    /// <c>m_floor(val + 0.5)</c> when <c>val &gt;= 0</c>, <c>m_ceil(val - 0.5)</c> otherwise.
    /// </summary>
    public static double RoundSymmetric(double val) =>
        val >= 0 ? MathFloor(val + 0.5) : MathCeil(val - 0.5);

    /// <summary>
    /// <c>roundSymmetric(val, dec)</c> — <c>src/Modules/Common.lua:733</c>.
    /// </summary>
    public static double RoundSymmetric(double val, int dec)
    {
        double factor = Pow10(dec);
        return val >= 0
            ? MathFloor((val * factor) + 0.5) / factor
            : MathCeil((val * factor) - 0.5) / factor;
    }

    /// <summary>
    /// <c>floorSymmetric(val)</c> — <c>src/Modules/Common.lua:766</c>,
    /// <c>select(1, math.modf(val))</c>. Rounds toward zero.
    /// </summary>
    /// <remarks>
    /// Careful with the original: Lua's <c>select(1, ...)</c> yields <em>every</em> value from
    /// index 1 onward, so this branch returns both of <c>math.modf</c>'s results, not one. The
    /// <c>dec</c> overload divides by the factor, which adjusts the call back to a single value,
    /// and every call site assigns the result to one variable (e.g.
    /// <c>src/Modules/ItemTools.lua:64</c>), which does the same. The port therefore returns the
    /// integral part only; use <see cref="Modf(double)"/> if a future call site ever needs the
    /// fraction Lua is leaking here.
    /// </remarks>
    public static double FloorSymmetric(double val) => Truncate(val);

    /// <summary>
    /// <c>floorSymmetric(val, dec)</c> — <c>src/Modules/Common.lua:766</c>,
    /// <c>select(1, math.modf(val * factor)) / factor</c>.
    /// </summary>
    public static double FloorSymmetric(double val, int dec)
    {
        double factor = Pow10(dec);
        return Truncate(val * factor) / factor;
    }

    /// <summary>
    /// <c>ceilSymmetric(val)</c> — <c>src/Modules/Common.lua:778</c>. Rounds away from zero.
    /// </summary>
    public static double CeilSymmetric(double val) => val >= 0 ? MathCeil(val) : MathFloor(val);

    /// <summary>
    /// <c>ceilSymmetric(val, dec)</c> — <c>src/Modules/Common.lua:778</c>.
    /// </summary>
    public static double CeilSymmetric(double val, int dec)
    {
        double factor = Pow10(dec);
        return val >= 0 ? MathCeil(val * factor) / factor : MathFloor(val * factor) / factor;
    }

    /// <summary>
    /// <c>alwaysPositiveRound(val)</c> — <c>src/Modules/Common.lua:753</c>,
    /// <c>floorSymmetric(val + 0.5)</c>.
    /// </summary>
    /// <remarks>
    /// Upstream's own comment calls this "an incorrect way to round numbers"; it exists because
    /// corrupted unique roll ranges are computed that way in game and the port has to reproduce
    /// the mistake, not fix it. Being a tail call to <see cref="FloorSymmetric(double)"/>, this
    /// overload inherits its two-value return in Lua; see that method's remarks.
    /// </remarks>
    public static double AlwaysPositiveRound(double val) => FloorSymmetric(val + 0.5);

    /// <summary>
    /// <c>alwaysPositiveRound(val, dec)</c> — <c>src/Modules/Common.lua:753</c>,
    /// <c>floorSymmetric(val * factor + 0.5) / factor</c>.
    /// </summary>
    public static double AlwaysPositiveRound(double val, int dec)
    {
        double factor = Pow10(dec);
        return FloorSymmetric((val * factor) + 0.5) / factor;
    }

    /// <summary>
    /// <c>ceil_b(x, base)</c> — <c>src/Modules/Common.lua:1013</c>, <c>base * m_ceil(x / base)</c>.
    /// </summary>
    public static double CeilB(double x, double baseValue) => baseValue * MathCeil(x / baseValue);

    /// <summary>
    /// <c>floor_b(x, base)</c> — <c>src/Modules/Common.lua:1019</c>, <c>base * m_floor(x / base)</c>.
    /// </summary>
    public static double FloorB(double x, double baseValue) => baseValue * MathFloor(x / baseValue);

    // ---------------------------------------------------------------------------------
    // Call-site shapes. These are separate members rather than open-coded arithmetic at the
    // eventual call sites so that the oracle can pin the whole expression, operand order
    // included, and not just the primitive at the bottom of it.
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The per-mod-name factor in <c>More</c> — <c>src/Classes/ModDB.lua:197</c> and
    /// <c>src/Classes/ModList.lua:147</c>: <c>result = result * round(modResult, 2)</c>.
    /// Returns the <c>round(modResult, 2)</c> factor.
    /// </summary>
    public static double MoreScale(double modResult) => Round(modResult, 2);

    /// <summary>
    /// The <c>data.highPrecisionMods</c> override in <c>More</c> —
    /// <c>src/Classes/ModDB.lua:193-195</c>, <c>src/Classes/ModList.lua:143-145</c>:
    /// <c>local power = 10 ^ precision; math.floor(result * modResult * power) / power</c>.
    /// </summary>
    /// <remarks>
    /// This branch calls raw <c>math.floor</c>, <b>not</b> Common.lua's global <c>floor</c>:
    /// there is no <c>0.0001</c> here. The precision comes from
    /// <c>data.highPrecisionMods[name][type]</c> (<c>src/Modules/Data.lua:436</c>).
    /// </remarks>
    public static double HighPrecisionMore(double result, double modResult, int precision)
    {
        double power = Pow10(precision);
        return MathFloor(result * modResult * power) / power;
    }

    /// <summary>
    /// The <c>Multiplier</c> tag divisor — <c>src/Classes/ModStore.lua:403</c>:
    /// <c>local mult = m_floor(base / (tag.div or 1) + 0.0001)</c>.
    /// </summary>
    /// <remarks>
    /// Hand-rolled at the call site with the same epsilon as Common.lua's global <c>floor</c>,
    /// but written against <c>m_floor</c> (<c>math.floor</c>), so it is deliberately its own
    /// member here. Pass <c>1.0</c> for <c>div</c> when the tag has none — that is what
    /// <c>tag.div or 1</c> does. The <c>tag.noFloor</c> branch on the following line bypasses
    /// the floor entirely and is a plain division, so it needs nothing from this class.
    /// </remarks>
    public static double MultiplierFloor(double baseValue, double div) =>
        MathFloor((baseValue / div) + 0.0001);

    /// <summary>
    /// <c>ScaleAddMod</c>'s default value scaling — <c>src/Classes/ModStore.lua:82</c>:
    /// <c>subMod.value = m_modf(round(subMod.value * scale, 2))</c>.
    /// </summary>
    /// <remarks>
    /// <c>m_modf</c> returns two values but the assignment keeps only the first, so the rounded
    /// product is then truncated toward zero — the fractional part is discarded, and a value
    /// that rounded to <c>2.99</c> becomes <c>2</c>.
    /// </remarks>
    public static double ScaleModValue(double value, double scale) => Truncate(Round(value * scale, 2));

    /// <summary>
    /// <c>ScaleAddMod</c>'s high-precision branch — <c>src/Classes/ModStore.lua:79-80</c>:
    /// <c>local power = 10 ^ precision; subMod.value = m_floor(subMod.value * scale * power) / power</c>.
    /// </summary>
    /// <remarks>
    /// Taken when <c>data.highPrecisionMods[name][type]</c> exists, or when the value is not a
    /// whole number and <c>data.defaultHighPrecision</c> (1, <c>src/Modules/Data.lua:433</c>)
    /// applies. Raw <c>math.floor</c>, no epsilon.
    /// </remarks>
    public static double ScaleModValueHighPrecision(double value, double scale, int precision)
    {
        double power = Pow10(precision);
        return MathFloor(value * scale * power) / power;
    }
}
