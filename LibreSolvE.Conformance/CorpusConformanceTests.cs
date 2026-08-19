using System.Text.Json;
using System.Text.Json.Serialization;

namespace LibreSolvE.Conformance;

public sealed class ManifestCase
{
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("expectSucceed")] public bool ExpectSucceed { get; set; }
    [JsonPropertyName("expectedValues")] public Dictionary<string, double>? ExpectedValues { get; set; }
    [JsonPropertyName("note")] public string? Note { get; set; }
}

public sealed class Manifest
{
    [JsonPropertyName("tolerance")] public double Tolerance { get; set; }
    [JsonPropertyName("cases")] public List<ManifestCase> Cases { get; set; } = new();
}

/// <summary>
/// PLAN.md Phase 2: runs every example in examples/ through the real
/// pipeline (via LseRunner, not a subprocess) and checks it against
/// conformance-manifest.json -- both that files in the confirmed-safe
/// COMPATIBILITY.md subset produce the right NUMBERS (not just exit 0),
/// and that files outside that subset are still correctly rejected. The
/// second half is what catches an accidentally-more-permissive grammar,
/// which a valid-input-only test suite cannot detect.
/// </summary>
public class CorpusConformanceTests
{
    private static readonly Lazy<Manifest> LoadedManifest = new(() =>
    {
        string path = Path.Combine(AppContext.BaseDirectory, "conformance-manifest.json");
        string json = System.IO.File.ReadAllText(path);
        return JsonSerializer.Deserialize<Manifest>(json)
               ?? throw new InvalidOperationException("Manifest deserialized to null.");
    });

    public static IEnumerable<object[]> ManifestFileNames() =>
        LoadedManifest.Value.Cases.Select(c => new object[] { c.File });

    [Theory]
    [MemberData(nameof(ManifestFileNames))]
    public void ExampleMatchesManifest(string fileName)
    {
        var manifest = LoadedManifest.Value;
        var testCase = manifest.Cases.Single(c => c.File == fileName);

        string examplePath = Path.Combine(AppContext.BaseDirectory, "examples", fileName);
        Assert.True(System.IO.File.Exists(examplePath), $"Example file not found at {examplePath} -- csproj copy-to-output may be misconfigured.");
        string source = System.IO.File.ReadAllText(examplePath);

        var result = LseRunner.Run(source);

        if (testCase.ExpectSucceed)
        {
            Assert.True(result.Succeeded,
                $"{fileName} was expected to succeed (it is in the COMPATIBILITY.md confirmed-safe subset) " +
                $"but did not. ParseSucceeded={result.ParseSucceeded} SolveSucceeded={result.SolveSucceeded} " +
                $"FailureMessage={result.FailureMessage}");

            Assert.NotNull(result.Store);
            if (testCase.ExpectedValues is not null)
            {
                foreach (var (name, expected) in testCase.ExpectedValues)
                {
                    Assert.True(result.Store!.HasVariable(name),
                        $"{fileName}: expected variable '{name}' is missing from the solved output entirely " +
                        $"(this is exactly the failure mode the 006/CONVERT bug had -- a variable silently absent, " +
                        $"not just wrong).");

                    double actual = result.Store!.GetVariable(name);
                    double allowed = manifest.Tolerance * Math.Max(1.0, Math.Abs(expected));
                    double diff = Math.Abs(actual - expected);
                    Assert.True(diff <= allowed,
                        $"{fileName}: variable '{name}' = {actual} but expected {expected} " +
                        $"(diff {diff} exceeds tolerance {allowed}).");
                }
            }
        }
        else
        {
            Assert.False(result.Succeeded,
                $"{fileName} was expected to be REJECTED ({testCase.Note}) but it succeeded. " +
                $"Either the grammar became more permissive than COMPATIBILITY.md allows, or this file's " +
                $"scope has genuinely changed -- if the latter, update the manifest deliberately, don't just " +
                $"let this test go green silently.");
        }
    }

    [Fact]
    public void ManifestCoversExactlyTheExamplesDirectory()
    {
        // A file added to examples/ with no manifest entry would simply never
        // be checked -- silent coverage loss, the same shape as a check that
        // cannot fail. Assert the manifest's file list and the directory's
        // actual contents are the same set, in both directions.
        string examplesDir = Path.Combine(AppContext.BaseDirectory, "examples");
        var onDisk = Directory.GetFiles(examplesDir, "*.lse")
            .Select(Path.GetFileName)
            .Cast<string>()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        var inManifest = LoadedManifest.Value.Cases
            .Select(c => c.File)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var missingFromManifest = onDisk.Except(inManifest).ToList();
        var missingFromDisk = inManifest.Except(onDisk).ToList();

        Assert.True(missingFromManifest.Count == 0,
            $"examples/ contains files with no manifest entry (never checked): {string.Join(", ", missingFromManifest)}");
        Assert.True(missingFromDisk.Count == 0,
            $"Manifest references files that no longer exist in examples/: {string.Join(", ", missingFromDisk)}");
    }
}
