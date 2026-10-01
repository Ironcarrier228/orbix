using System.Text.RegularExpressions;
using Orbix.Core.Localization;
using Xunit;

namespace Orbix.Core.Tests;

/// <summary>
/// Every localization key referenced by the UI code or markup must exist in the string table. (The reverse direction is not checked: some keys are composed
/// at run time, e.g. launch error keys travel as strings.)
/// </summary>
public sealed class LocUsageTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Orbix.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IEnumerable<string> SourceFiles()
    {
        var root = FindRepoRoot();
        foreach (var sub in new[] { "src", "tests" })
        {
            var dir = Path.Combine(root, sub);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                if (ext is ".cs" or ".xaml")
                {
                    yield return file;
                }
            }
        }
    }

    private static HashSet<string> UsedKeys()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var code = new Regex("Loc\\.T\\(\\s*\"([A-Za-z0-9_.]+)\"");
        var xaml = new Regex("loc:Loc\\s+([A-Za-z0-9_.]+)");
        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (Match m in code.Matches(text))
            {
                used.Add(m.Groups[1].Value);
            }

            foreach (Match m in xaml.Matches(text))
            {
                used.Add(m.Groups[1].Value);
            }
        }

        return used;
    }

    [Fact]
    public void Every_Used_Key_Exists_In_Table()
    {
        var missing = UsedKeys().Where(k => !Loc.Has(k)).OrderBy(k => k).ToList();
        Assert.True(missing.Count == 0, "keys used but not declared: " + string.Join(", ", missing));
    }
}
