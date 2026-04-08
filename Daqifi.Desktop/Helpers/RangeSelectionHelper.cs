using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Daqifi.Desktop.Helpers;

/// <summary>
/// Helper class for managing range selection behavior in lists
/// </summary>
/// <typeparam name="T">Type of items being selected</typeparam>
public class RangeSelectionHelper<T> : INotifyPropertyChanged where T : class
{
    private int? _lastClickedIndex;
    private readonly ObservableCollection<T> _selectedItems = new();

    public ObservableCollection<T> SelectedItems => _selectedItems;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Handles item selection with support for Ctrl+Click and Shift+Click
    /// </summary>
    /// <param name="items">The source collection of all items</param>
    /// <param name="clickedIndex">Index of the clicked item</param>
    /// <param name="isCtrlPressed">Whether Ctrl key was pressed</param>
    /// <param name="isShiftPressed">Whether Shift key was pressed</param>
    public void HandleItemClick(IList<T> items, int clickedIndex, bool isCtrlPressed, bool isShiftPressed)
    {
        if (clickedIndex < 0 || clickedIndex >= items.Count)
            return;

        var clickedItem = items[clickedIndex];

        if (isShiftPressed && _lastClickedIndex.HasValue)
        {
            // Range selection
            var startIndex = Math.Min(_lastClickedIndex.Value, clickedIndex);
            var endIndex = Math.Max(_lastClickedIndex.Value, clickedIndex);

            if (!isCtrlPressed)
            {
                // Clear current selection unless Ctrl is also pressed
                ClearSelection();
            }

            // Select range
            for (int i = startIndex; i <= endIndex; i++)
            {
                var item = items[i];
                if (!_selectedItems.Contains(item))
                {
                    _selectedItems.Add(item);
                }
            }
        }
        else if (isCtrlPressed)
        {
            // Toggle selection of clicked item
            if (_selectedItems.Contains(clickedItem))
            {
                _selectedItems.Remove(clickedItem);
            }
            else
            {
                _selectedItems.Add(clickedItem);
            }
        }
        else
        {
            // Single selection (clear others)
            ClearSelection();
            _selectedItems.Add(clickedItem);
        }

        _lastClickedIndex = clickedIndex;
        OnPropertyChanged(nameof(SelectedItems));
    }

    /// <summary>
    /// Selects all items in the collection
    /// </summary>
    public void SelectAll(IList<T> items)
    {
        ClearSelection();
        foreach (var item in items)
        {
            _selectedItems.Add(item);
        }
        OnPropertyChanged(nameof(SelectedItems));
    }

    /// <summary>
    /// Clears all selected items
    /// </summary>
    public void ClearSelection()
    {
        _selectedItems.Clear();
        OnPropertyChanged(nameof(SelectedItems));
    }

    /// <summary>
    /// Checks if an item is currently selected
    /// </summary>
    public bool IsSelected(T item)
    {
        return _selectedItems.Contains(item);
    }

    /// <summary>
    /// Gets the number of selected items
    /// </summary>
    public int SelectedCount => _selectedItems.Count;

    /// <summary>
    /// Handles keyboard shortcuts
    /// </summary>
    public void HandleKeyboardShortcut(IList<T> items, string shortcut)
    {
        switch (shortcut.ToUpper())
        {
            case "CTRL+A":
                SelectAll(items);
                break;
            case "CTRL+SHIFT+A":
                ClearSelection();
                break;
        }
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}