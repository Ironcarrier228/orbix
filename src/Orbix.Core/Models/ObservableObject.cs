using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Orbix.Core.Models;

/// <summary>
/// Base class for all configuration models.
///
/// Two notification channels are provided:
///  * <see cref="PropertyChanged"/> - classic INPC, used by WPF bindings and by services that react
///    to a concrete setting (for example "hotkey changed" or "orb size changed");
///  * <see cref="Changed"/> - a "bubbling" event: it is raised after a change of this object OR of any
///    nested observable child. <c>ConfigService</c> listens to the root one to implement auto-save.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after this object or any of its observable children has changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Sets the backing field and raises notifications when the value really changed.</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);
        return true;
    }

    /// <summary>Raises <see cref="PropertyChanged"/> and <see cref="Changed"/>.</summary>
    protected void Raise([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raises only <see cref="PropertyChanged"/> (for computed, non-persisted properties).</summary>
    protected void RaiseComputed(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Raises only the bubbling <see cref="Changed"/> event (used when a child changed).</summary>
    protected void RaiseChangedOnly() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Subscribes this object to the bubbling event of a child.</summary>
    protected void Attach(ObservableObject? child)
    {
        if (child != null)
        {
            child.Changed += OnChildChanged;
        }
    }

    /// <summary>Unsubscribes this object from the bubbling event of a child.</summary>
    protected void Detach(ObservableObject? child)
    {
        if (child != null)
        {
            child.Changed -= OnChildChanged;
        }
    }

    /// <summary>Subscribes to a collection of observable children (see <see cref="ObservableList{T}"/>).</summary>
    protected void Attach<T>(ObservableList<T>? list) where T : ObservableObject
    {
        if (list != null)
        {
            list.Changed += OnChildChanged;
        }
    }

    protected void Detach<T>(ObservableList<T>? list) where T : ObservableObject
    {
        if (list != null)
        {
            list.Changed -= OnChildChanged;
        }
    }

    private void OnChildChanged(object? sender, EventArgs e) => RaiseChangedOnly();
}

/// <summary>
/// <see cref="ObservableCollection{T}"/> of observable objects which additionally raises a
/// bubbling <see cref="Changed"/> event when the collection itself OR any element changes.
/// </summary>
public sealed class ObservableList<T> : ObservableCollection<T> where T : ObservableObject
{
    public ObservableList()
    {
    }

    public ObservableList(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            Add(item);
        }
    }

    public event EventHandler? Changed;

    protected override void InsertItem(int index, T item)
    {
        base.InsertItem(index, item);
        item.Changed += OnItemChanged;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void RemoveItem(int index)
    {
        var old = this[index];
        old.Changed -= OnItemChanged;
        base.RemoveItem(index);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void SetItem(int index, T item)
    {
        var old = this[index];
        old.Changed -= OnItemChanged;
        base.SetItem(index, item);
        item.Changed += OnItemChanged;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void ClearItems()
    {
        foreach (var item in this)
        {
            item.Changed -= OnItemChanged;
        }

        base.ClearItems();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void MoveItem(int oldIndex, int newIndex)
    {
        // The base implementation moves the element inside the inner list, so the subscription stays valid.
        base.MoveItem(oldIndex, newIndex);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnItemChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
