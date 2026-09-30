using System.Windows;
using System.Windows.Markup;
using Orbix.Core.Localization;

namespace Orbix.UI.Settings;

/// <summary>
/// XAML markup extension for localized strings: <c>Content="{loc:Loc Settings.Title}"</c>.
/// Every binding is registered so that <see cref="LocBinder.Refresh"/> can re-translate an open
/// window immediately after the language changes.
/// </summary>
internal sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget target &&
            target.TargetObject is DependencyObject obj &&
            target.TargetProperty is DependencyProperty property)
        {
            LocBinder.Register(obj, property, Key);
        }

        return Loc.T(Key);
    }
}

/// <summary>Keeps weak references to every target of <see cref="LocExtension"/> for live re-translation.</summary>
internal static class LocBinder
{
    private sealed class Entry
    {
        public WeakReference<DependencyObject> Target = null!;
        public DependencyProperty Property = null!;
        public string Key = string.Empty;
    }

    private static readonly List<Entry> Entries = new();
    private static readonly object Gate = new();

    public static void Register(DependencyObject target, DependencyProperty property, string key)
    {
        lock (Gate)
        {
            Entries.Add(new Entry { Target = new WeakReference<DependencyObject>(target), Property = property, Key = key });
            if (Entries.Count > 4096)
            {
                Prune();
            }
        }
    }

    /// <summary>Re-applies every live translation (called after <c>Loc.SetLanguage</c>).</summary>
    public static void Refresh()
    {
        lock (Gate)
        {
            Prune();
            foreach (var entry in Entries)
            {
                if (entry.Target.TryGetTarget(out var obj))
                {
                    obj.SetValue(entry.Property, Loc.T(entry.Key));
                }
            }
        }
    }

    private static void Prune()
    {
        Entries.RemoveAll(e => !e.Target.TryGetTarget(out _));
    }
}
