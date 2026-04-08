using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;
using Daqifi.Desktop.Helpers;

namespace Daqifi.Desktop.Behaviors;

/// <summary>
/// Behavior that adds range selection support to ListView controls
/// </summary>
public class RangeSelectionBehavior : Behavior<System.Windows.Controls.ListView>
{
    public static readonly DependencyProperty SelectionHelperProperty =
        DependencyProperty.Register(nameof(SelectionHelper), typeof(object), typeof(RangeSelectionBehavior));

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(System.Collections.IList), typeof(RangeSelectionBehavior));

    /// <summary>
    /// The RangeSelectionHelper instance to use for managing selection
    /// </summary>
    public object SelectionHelper
    {
        get => GetValue(SelectionHelperProperty);
        set => SetValue(SelectionHelperProperty, value);
    }

    /// <summary>
    /// The items source for the ListView
    /// </summary>
    public System.Collections.IList ItemsSource
    {
        get => (System.Collections.IList)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        AssociatedObject.KeyDown += OnKeyDown;
        AssociatedObject.SelectionMode = System.Windows.Controls.SelectionMode.Extended;
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            AssociatedObject.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
            AssociatedObject.KeyDown -= OnKeyDown;
        }
        base.OnDetaching();
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (SelectionHelper == null || ItemsSource == null)
            return;

        // Find the clicked item
        var element = e.OriginalSource as FrameworkElement;
        while (element != null && element is not ListViewItem)
        {
            element = element.Parent as FrameworkElement ?? element.TemplatedParent as FrameworkElement;
        }

        if (element is ListViewItem listViewItem)
        {
            var index = AssociatedObject.ItemContainerGenerator.IndexFromContainer(listViewItem);
            if (index >= 0)
            {
                var isCtrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
                var isShiftPressed = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

                // Use reflection to call the generic HandleItemClick method
                var helperType = SelectionHelper.GetType();
                var method = helperType.GetMethod("HandleItemClick");
                if (method != null)
                {
                    method.Invoke(SelectionHelper, new object[] { ItemsSource, index, isCtrlPressed, isShiftPressed });
                    
                    // Update ListView selection to match our selection helper
                    UpdateListViewSelection();
                    
                    // Prevent default ListView selection behavior
                    e.Handled = true;
                }
            }
        }
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (SelectionHelper == null || ItemsSource == null)
            return;

        var isCtrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
        var isShiftPressed = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

        string? shortcut = null;
        if (isCtrlPressed && !isShiftPressed && e.Key == Key.A)
        {
            shortcut = "CTRL+A";
        }
        else if (isCtrlPressed && isShiftPressed && e.Key == Key.A)
        {
            shortcut = "CTRL+SHIFT+A";
        }

        if (shortcut != null)
        {
            // Use reflection to call the generic HandleKeyboardShortcut method
            var helperType = SelectionHelper.GetType();
            var method = helperType.GetMethod("HandleKeyboardShortcut");
            if (method != null)
            {
                method.Invoke(SelectionHelper, new object[] { ItemsSource, shortcut });
                UpdateListViewSelection();
                e.Handled = true;
            }
        }
    }

    private void UpdateListViewSelection()
    {
        if (SelectionHelper == null || AssociatedObject == null)
            return;

        // Use reflection to get the SelectedItems property
        var helperType = SelectionHelper.GetType();
        var selectedItemsProperty = helperType.GetProperty("SelectedItems");
        if (selectedItemsProperty?.GetValue(SelectionHelper) is System.Collections.IList selectedItems)
        {
            AssociatedObject.SelectedItems.Clear();
            foreach (var item in selectedItems)
            {
                AssociatedObject.SelectedItems.Add(item);
            }
        }
    }
}