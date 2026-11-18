using System.Windows;
using System.Windows.Controls;
using Kotoba.Core;

namespace Kotoba.Windows;

internal sealed class SearchResults(Func<DictionaryEntry, UIElement> createRow) : ItemsControl
{
    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is ContentPresenter container && item is DictionaryEntry entry)
            container.Content = createRow(entry);
    }
}
