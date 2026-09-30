namespace Orbix.Core.Models;

/// <summary>A named set of menu items. The user can switch between profiles.</summary>
public sealed class MenuProfile : ObservableObject
{
    private string _id = RadialItem.NewId();
    private string _name = string.Empty;
    private ObservableList<RadialItem> _items = new();

    public MenuProfile()
    {
        Attach(_items);
    }

    public string Id
    {
        get => _id;
        set => Set(ref _id, string.IsNullOrWhiteSpace(value) ? RadialItem.NewId() : value);
    }

    public string Name
    {
        get => _name;
        set => Set(ref _name, value ?? string.Empty);
    }

    /// <summary>Root level of the menu.</summary>
    public ObservableList<RadialItem> Items
    {
        get => _items;
        set
        {
            var next = value ?? new ObservableList<RadialItem>();
            if (ReferenceEquals(next, _items))
            {
                return;
            }

            Detach(_items);
            _items = next;
            Attach(_items);
            Raise();
        }
    }

    public MenuProfile Clone(string? newName = null)
    {
        var copy = new MenuProfile { Name = newName ?? Name };
        foreach (var item in Items)
        {
            copy.Items.Add(item.Clone());
        }

        return copy;
    }

    public override string ToString() => Name;
}
