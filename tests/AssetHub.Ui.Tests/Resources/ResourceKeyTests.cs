using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;

namespace AssetHub.Ui.Tests.Resources;

/// <summary>
/// CI guards for the two localisation failures that fail silently at runtime:
/// a key missing from the Swedish file (the UI falls back to English), and a
/// literal key that exists in no resource at all (the UI shows the raw key).
/// </summary>
/// <remarks>
/// The bUnit stub localizer echoes keys, so component tests pass for a key that
/// does not exist — which is how <c>AssetLoc["NoTags"]</c> shipped. These tests
/// read the real resources instead. Only literal keys are checked; lookups built
/// at runtime (<c>Event_{type}</c> and the like) are out of reach.
/// </remarks>
public class ResourceKeyTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly char[] PathSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    // `IStringLocalizer<CommonResource> CommonLoc` (an @inject, a field or a ctor parameter).
    private static readonly Regex LocalizerDeclaration =
        new(@"IStringLocalizer<([\w.]+)>\s+(\w+)", RegexOptions.None, RegexTimeout);

    // `CommonLoc["Key"]` and `CommonLoc["Key", arg]` — a literal key only.
    private static readonly Regex LiteralLookup =
        new(@"\b(\w+)\[""([^""{}]+)""\s*[,\]]", RegexOptions.None, RegexTimeout);

    public static TheoryData<string> ResourceTypeNames()
    {
        var data = new TheoryData<string>();
        foreach (var type in ResourceTypes())
            data.Add(type.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(ResourceTypeNames))]
    public void SwedishResources_ComparedToEnglish_HaveIdenticalKeys(string resourceTypeName)
    {
        var manager = Manager(ResourceTypes().Single(t => t.Name == resourceTypeName));

        var english = Keys(manager, CultureInfo.InvariantCulture);
        var swedish = Keys(manager, CultureInfo.GetCultureInfo("sv"));

        Assert.True(english.SetEquals(swedish),
            $"{resourceTypeName}: missing in Swedish [{string.Join(", ", english.Except(swedish).Order())}]; "
            + $"only in Swedish [{string.Join(", ", swedish.Except(english).Order())}].");
    }

    [Fact]
    public void LocalizerLookups_WithLiteralKey_ExistInTheirResource()
    {
        var keysByResource = ResourceTypes().ToDictionary(
            t => t.Name, t => Keys(Manager(t), CultureInfo.InvariantCulture));
        var root = RepositoryRoot();
        var misses = new List<string>();
        var checkedLookups = 0;

        // A component's .razor and .razor.cs share their localizers, so map them per stem.
        foreach (var component in SourceFiles(root).GroupBy(f => f.Replace(".cs", string.Empty).Replace(".razor", string.Empty)))
        {
            var sources = component.Select(f => (File: f, Text: File.ReadAllText(f))).ToList();
            var localizers = sources
                .SelectMany(s => LocalizerDeclaration.Matches(s.Text))
                .Select(m => (Variable: m.Groups[2].Value, Resource: m.Groups[1].Value.Split('.')[^1]))
                .Where(l => keysByResource.ContainsKey(l.Resource))
                .DistinctBy(l => l.Variable)
                .ToDictionary(l => l.Variable, l => l.Resource);

            foreach (var (file, text) in sources)
            {
                var lookups = LiteralLookup.Matches(text)
                    .Where(m => localizers.ContainsKey(m.Groups[1].Value))
                    .Select(m => (Variable: m.Groups[1].Value, Key: m.Groups[2].Value, Resource: localizers[m.Groups[1].Value]))
                    .ToList();
                checkedLookups += lookups.Count;
                misses.AddRange(lookups
                    .Where(l => !keysByResource[l.Resource].Contains(l.Key))
                    .Select(l => $"{Path.GetRelativePath(root, file)}: {l.Variable}[\"{l.Key}\"] -> {l.Resource}"));
            }
        }

        Assert.True(checkedLookups > 500, $"Only {checkedLookups} lookups found — the scan has stopped seeing the source.");
        Assert.True(misses.Count == 0, "Keys missing from their resource:\n" + string.Join("\n", misses));
    }

    private static IEnumerable<Type> ResourceTypes() =>
        typeof(CommonResource).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(CommonResource).Namespace && t.Name.EndsWith("Resource", StringComparison.Ordinal))
            .OrderBy(t => t.Name, StringComparer.Ordinal);

    private static ResourceManager Manager(Type marker) => new(marker.FullName!, marker.Assembly);

    private static HashSet<string> Keys(ResourceManager manager, CultureInfo culture)
    {
        var set = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException($"No '{culture.Name}' resources for {manager.BaseName}.");
        return set.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet(StringComparer.Ordinal);
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AssetHub.sln")))
                return dir.FullName;
        }
        throw new InvalidOperationException("AssetHub.sln not found above " + AppContext.BaseDirectory);
    }

    private static IEnumerable<string> SourceFiles(string root)
    {
        static bool IsBuildOutput(string path) =>
            path.Split(PathSeparators) is var parts
            && (parts.Contains("bin") || parts.Contains("obj"));

        var ui = Path.Combine(root, "src", "AssetHub.Ui");
        var ownUi = Directory.EnumerateFiles(ui, "*.razor", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(ui, "*.cs", SearchOption.AllDirectories));
        var host = Directory.EnumerateFiles(Path.Combine(root, "src", "AssetHub.Api", "Components"), "*.razor");
        return ownUi.Concat(host).Where(f => !IsBuildOutput(Path.GetRelativePath(root, f)));
    }
}
