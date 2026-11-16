using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace jaytwo.Subprocess.UnitTests.Guides;

// Every C# sample in docs/guides is a compiled, running region of a test in this folder. A guide marks a sample with
// <!-- sample: id --> on the line before its fence; the test marks the same code with "// sample: id" and "// end sample".
public class GuideSampleTests
{
    private const string MarkdownTag = "<!-- sample: ";
    private const string RegionStart = "// sample: ";
    private const string RegionEnd = "// end sample";

    [Fact]
    public void every_guide_sample_matches_its_test_region()
    {
        // arrange
        string root = RepositoryRoot();
        Dictionary<string, string> samples = MarkdownSamples(Path.Combine(root, "docs", "guides"));
        Dictionary<string, string> regions = TestRegions(Path.Combine(root, "test", "jaytwo.Subprocess.UnitTests", "Guides"));

        // act (no action needed)

        // assert
        Assert.NotEmpty(samples);
        foreach (KeyValuePair<string, string> sample in samples)
        {
            Assert.True(regions.TryGetValue(sample.Key, out string? code), "No test region for guide sample '" + sample.Key + "'.");
            Assert.Equal(code, sample.Value);
        }

        Assert.Empty(regions.Keys.Except(samples.Keys));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "jaytwo.Subprocess.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root was not found above " + AppContext.BaseDirectory + ".");
    }

    private static Dictionary<string, string> MarkdownSamples(string directory)
    {
        var samples = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in Directory.GetFiles(directory, "*.md"))
        {
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith(MarkdownTag, StringComparison.Ordinal))
                {
                    continue;
                }

                string id = line.Substring(MarkdownTag.Length).Replace("-->", string.Empty).Trim();
                Assert.True(i + 1 < lines.Length && lines[i + 1].Trim() == "```csharp", "Sample '" + id + "' in " + path + " is not followed by a csharp fence.");
                int end = Array.FindIndex(lines, i + 2, l => l.Trim() == "```");
                Assert.True(end > 0, "Sample '" + id + "' in " + path + " has no closing fence.");
                Add(samples, id, lines.Skip(i + 2).Take(end - i - 2), path);
                i = end;
            }
        }

        return samples;
    }

    private static Dictionary<string, string> TestRegions(string directory)
    {
        var regions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in Directory.GetFiles(directory, "*.cs"))
        {
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith(RegionStart, StringComparison.Ordinal))
                {
                    continue;
                }

                string id = line.Substring(RegionStart.Length).Trim();
                int end = Array.FindIndex(lines, i + 1, l => l.Trim() == RegionEnd);
                Assert.True(end > 0, "Region '" + id + "' in " + path + " has no end marker.");
                Add(regions, id, lines.Skip(i + 1).Take(end - i - 1), path);
                i = end;
            }
        }

        return regions;
    }

    // Indentation differs between a test method and a markdown fence, so lines compare trimmed.
    private static void Add(Dictionary<string, string> map, string id, IEnumerable<string> lines, string path)
    {
        Assert.False(map.ContainsKey(id), "Sample id '" + id + "' is used twice (" + path + ").");
        map[id] = string.Join("\n", lines.Select(l => l.Trim())).Trim('\n');
    }
}
