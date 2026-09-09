using System.Globalization;
using System.Text;
using Xunit;

namespace Pob.Tests;

/// <summary>
/// A minimal <c>.received</c> / <c>.verified</c> snapshot comparer, in the shape ticket 06
/// asks for and the shape <c>spec/GenerateBuilds.lua</c> already trains the habit for: a
/// failing snapshot writes what it got next to what was expected, and regenerating the
/// goldens is one command.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not Verify (VerifyTests).</b> The ticket names Verify.XunitV3, and the
/// package version is already pinned in <c>dotnet/Directory.Packages.props</c> — but using it
/// needs a <c>&lt;PackageReference Include="Verify.XunitV3" /&gt;</c> in
/// <c>Pob.Tests.csproj</c>, and this ticket does not own that file (tickets 05, 07 and 08 are
/// editing the same test project concurrently). The behaviour below is the subset the
/// snapshots here need. Swapping it out is a one-line change per call site:
/// <c>ModStoreSnapshot.Verify(name, text)</c> becomes
/// <c>await Verifier.Verify(text).UseFileName(name)</c>, and the committed
/// <c>*.verified.txt</c> files move to wherever Verify is configured to look.
/// </para>
/// <para>
/// Snapshots live beside the dumps they describe, in
/// <c>Pob.Tests/oracles/modstore/verified/</c>.
/// </para>
/// </remarks>
public static class ModStoreSnapshot
{
    /// <summary>
    /// Set this environment variable to <c>1</c> to rewrite every golden from what the test
    /// produced, the way <c>spec/GenerateBuilds.lua</c> rewrites the build goldens. Review
    /// the resulting diff; do not commit it blind.
    /// </summary>
    public const string AcceptEnvironmentVariable = "POB_MODSTORE_ACCEPT";

    /// <summary>How many differing lines a failure message prints before it gives up.</summary>
    private const int MaxReportedDifferences = 20;

    /// <summary>Directory holding the committed <c>*.verified.txt</c> files.</summary>
    public static string SnapshotDirectory => Path.Combine(ModStoreDumpReader.CorpusDirectory, "verified");

    /// <summary>
    /// Compares <paramref name="content"/> against the committed snapshot for
    /// <paramref name="name"/>, failing the test with a line-level diff when they differ.
    /// </summary>
    public static void Verify(string name, string content)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(content);

        string received = Normalise(content);
        string verifiedPath = Path.Combine(SnapshotDirectory, name + ".verified.txt");
        string receivedPath = Path.Combine(SnapshotDirectory, name + ".received.txt");

        if (Accepting)
        {
            Directory.CreateDirectory(SnapshotDirectory);
            File.WriteAllText(verifiedPath, received);
            Delete(receivedPath);
            return;
        }

        if (!File.Exists(verifiedPath))
        {
            Write(receivedPath, received);
            Assert.Fail(
                $"No snapshot at {verifiedPath}. The produced text is in {receivedPath}; "
                + $"review it and accept with {AcceptEnvironmentVariable}=1 dotnet test.");
        }

        string verified = Normalise(File.ReadAllText(verifiedPath));

        if (string.Equals(verified, received, StringComparison.Ordinal))
        {
            // Leaving a stale .received.txt behind is how a "fixed" test still looks broken
            // in the working tree.
            Delete(receivedPath);
            return;
        }

        Write(receivedPath, received);
        Assert.Fail(
            $"Snapshot {name} differs.{Environment.NewLine}"
            + $"  expected: {verifiedPath}{Environment.NewLine}"
            + $"  actual:   {receivedPath}{Environment.NewLine}"
            + $"  accept with: {AcceptEnvironmentVariable}=1 dotnet test{Environment.NewLine}"
            + Diff(verified, received));
    }

    private static bool Accepting =>
        string.Equals(Environment.GetEnvironmentVariable(AcceptEnvironmentVariable), "1", StringComparison.Ordinal);

    /// <summary>LF endings and one trailing newline, so a snapshot cannot fail over CRLF.</summary>
    private static string Normalise(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string Diff(string expected, string actual)
    {
        string[] expectedLines = expected.Split('\n');
        string[] actualLines = actual.Split('\n');

        StringBuilder report = new();
        int reported = 0;

        for (int i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++)
        {
            string left = i < expectedLines.Length ? expectedLines[i] : "<missing>";
            string right = i < actualLines.Length ? actualLines[i] : "<missing>";

            if (string.Equals(left, right, StringComparison.Ordinal))
            {
                continue;
            }

            if (reported == MaxReportedDifferences)
            {
                report.Append("  ... further differences suppressed").Append('\n');
                break;
            }

            report.Append(CultureInfo.InvariantCulture, $"  line {i + 1}:\n    - {left}\n    + {right}\n");
            reported++;
        }

        return report.ToString();
    }
}
