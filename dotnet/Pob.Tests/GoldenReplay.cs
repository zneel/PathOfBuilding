using System.Collections.Immutable;

namespace Pob.Tests;

/// <summary>
/// The seam between the golden corpus and whatever calculates a build (migration ticket 07).
/// </summary>
/// <remarks>
/// <para>
/// The corpus is finished; the engine that will be measured against it is not. Tickets 21-27
/// build <c>Pob.Calc</c>, and <c>Pob.Calc</c> today contains an assembly marker and nothing
/// else. Rather than leave the replay side unwritten until then, everything except the engine
/// is here and tested: loading, section matching, the tolerance ladder and the
/// magnitude-sorted report all run today against
/// <see cref="GoldenReplay.PlaybackEngine"/> - a stand-in that reads the corpus back - so the
/// harness itself is exercised rather than merely declared.
/// </para>
/// <para>
/// Wiring the real engine in is one class: implement <see cref="IGoldenEngine"/> over
/// <c>Pob.Calc</c>'s entry point, return sections keyed <c>"MAIN/player"</c>,
/// <c>"CALCS/enemy"</c> and so on with the same flattened key names the Lua dumper produces
/// (<c>MainHand/CritChance</c>, <c>SkillDPS/1/dps</c>), and hand it to
/// <see cref="Replay"/>. Nothing else changes.
/// </para>
/// </remarks>
public static class GoldenReplay
{
    /// <summary>Runs one build through an engine and reports how it differs from the golden values.</summary>
    public static ImmutableArray<GoldenDiff> Replay(GoldenBuild build, IGoldenEngine engine, GoldenTolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(tolerance);

        IReadOnlyDictionary<string, IReadOnlyDictionary<string, GoldenValue>> actual = engine.Calculate(build.InputXml);
        return GoldenDiffReporter.Compare(build, actual, tolerance);
    }

    /// <summary>
    /// Reads the golden values straight back out of the build. Used to exercise the replay
    /// path end to end while the real engine does not exist, and - once it does - to prove a
    /// failing comparison is the engine's doing rather than the harness's.
    /// </summary>
    public sealed class PlaybackEngine : IGoldenEngine
    {
        private readonly GoldenBuild _build;
        private readonly Func<string, string, GoldenValue, GoldenValue>? _perturb;

        /// <param name="build">The build to play back.</param>
        /// <param name="perturb">
        /// Optional per-key transform, so a test can inject a known deviation and check that
        /// the reporter finds and ranks it.
        /// </param>
        public PlaybackEngine(GoldenBuild build, Func<string, string, GoldenValue, GoldenValue>? perturb = null)
        {
            ArgumentNullException.ThrowIfNull(build);
            _build = build;
            _perturb = perturb;
        }

        public string Description => _perturb is null
            ? "corpus playback (identity)"
            : "corpus playback (perturbed)";

        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, GoldenValue>> Calculate(string inputXml)
        {
            Dictionary<string, IReadOnlyDictionary<string, GoldenValue>> sections = new(StringComparer.Ordinal);
            foreach ((string name, GoldenSection section) in _build.Sections)
            {
                Dictionary<string, GoldenValue> values = new(StringComparer.Ordinal);
                foreach ((string key, GoldenValue value) in section.Values)
                {
                    values[key] = _perturb is null ? value : _perturb(name, key, value);
                }

                sections[name] = values;
            }

            return sections;
        }
    }
}

/// <summary>Anything that can turn a build XML into actor output sections.</summary>
public interface IGoldenEngine
{
    /// <summary>Human-readable name, printed in failure reports.</summary>
    string Description { get; }

    /// <summary>
    /// Calculates a build, returning one flattened output map per <c>MODE/actor</c> section.
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, GoldenValue>> Calculate(string inputXml);
}
