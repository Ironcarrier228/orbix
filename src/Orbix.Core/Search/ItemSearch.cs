using Orbix.Core.Models;

namespace Orbix.Core.Search;

/// <summary>Result of a search through the whole item tree.</summary>
public sealed class SearchResult
{
    public static readonly SearchResult Empty = new(string.Empty, new List<RadialItem>(), new HashSet<string>(), new HashSet<string>());

    public SearchResult(string query, List<RadialItem> matches, HashSet<string> matchIds, HashSet<string> ancestorIds)
    {
        Query = query;
        Matches = matches;
        MatchIds = matchIds;
        AncestorIds = ancestorIds;
    }

    public string Query { get; }

    /// <summary>Matching items, best match first.</summary>
    public IReadOnlyList<RadialItem> Matches { get; }

    public IReadOnlySet<string> MatchIds { get; }

    /// <summary>Groups that (at any depth) contain a match - shown as "has matches inside".</summary>
    public IReadOnlySet<string> AncestorIds { get; }

    public bool IsActive => Query.Length > 0;

    public bool HasMatches => Matches.Count > 0;
}

/// <summary>
/// Type-to-search over the item names (and file names of targets). The query is also tried with the
/// keyboard layout swapped (RU &lt;-&gt; EN), so "ghbdtn" finds "Привет".
/// </summary>
public static class ItemSearch
{
    private const string Latin = "qwertyuiop[]asdfghjkl;'zxcvbnm,./`QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?~";
    private const string Cyrillic = "йцукенгшщзхъфывапролджэячсмитьбю.ёЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ,Ё";

    public static SearchResult Run(IEnumerable<RadialItem> roots, string? query)
    {
        query = query?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            return SearchResult.Empty;
        }

        string swapped = SwapKeyboardLayout(query);
        var scored = new List<(RadialItem Item, int Score, int Order)>();
        int order = 0;

        foreach (var item in ItemTree.Walk(roots))
        {
            int score = Math.Max(Score(item, query), ReferenceEquals(swapped, query) ? 0 : Score(item, swapped));
            if (score > 0)
            {
                scored.Add((item, score, order));
            }

            order++;
        }

        var matches = scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Order)
            .Select(s => s.Item)
            .ToList();

        var matchIds = new HashSet<string>(matches.Select(m => m.Id));
        var ancestors = new HashSet<string>();
        CollectAncestors(roots, matchIds, ancestors);

        return new SearchResult(query, matches, matchIds, ancestors);
    }

    /// <summary>Converts text typed in the wrong keyboard layout (EN &lt;-&gt; RU). Returns the same instance when nothing changed.</summary>
    public static string SwapKeyboardLayout(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var chars = text.ToCharArray();
        bool changed = false;
        for (int i = 0; i < chars.Length; i++)
        {
            int latin = Latin.IndexOf(chars[i]);
            if (latin >= 0)
            {
                chars[i] = Cyrillic[latin];
                changed = true;
                continue;
            }

            int cyr = Cyrillic.IndexOf(chars[i]);
            if (cyr >= 0)
            {
                chars[i] = Latin[cyr];
                changed = true;
            }
        }

        return changed ? new string(chars) : text;
    }

    private static int Score(RadialItem item, string query)
    {
        var name = item.Name ?? string.Empty;
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }

        // Start of any word: "stu" finds "Visual Studio".
        for (int i = 1; i < name.Length; i++)
        {
            if (!char.IsLetterOrDigit(name[i - 1]) && string.Compare(name, i, query, 0, query.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return 60;
            }
        }

        if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 40;
        }

        if ((item.Kind is ItemKind.App or ItemKind.Url or ItemKind.Command) && !string.IsNullOrEmpty(item.Target))
        {
            var fileName = SafeFileName(item.Target!);
            if (fileName.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                return 20;
            }
        }

        return 0;
    }

    private static string SafeFileName(string target)
    {
        int slash = target.LastIndexOfAny(new[] { '\\', '/' });
        return slash >= 0 && slash < target.Length - 1 ? target[(slash + 1)..] : target;
    }

    private static bool CollectAncestors(IEnumerable<RadialItem> level, HashSet<string> matchIds, HashSet<string> ancestors)
    {
        bool any = false;
        foreach (var item in level)
        {
            bool inside = item.Children.Count > 0 && CollectAncestors(item.Children, matchIds, ancestors);
            if (inside)
            {
                ancestors.Add(item.Id);
            }

            any |= inside || matchIds.Contains(item.Id);
        }

        return any;
    }
}
