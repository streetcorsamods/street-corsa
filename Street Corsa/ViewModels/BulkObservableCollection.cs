using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace StreetCorsa.ViewModels
{
    /// <summary>
    /// An <see cref="ObservableCollection{T}"/> a view model fills again in one go: <see cref="ReplaceAll"/> swaps every
    /// item and tells the list once (a Reset), where <c>Clear</c> and <c>Add</c> item by item would tell it once an item
    /// and have it rebuild its rows as many times. For the collections bound to a list that is filled again after every
    /// change (the shelf, a part's jobs, a counter's pieces).
    /// </summary>
    public sealed class BulkObservableCollection<T> : ObservableCollection<T>
    {
        /// <summary>Every item for <paramref name="items"/>, told as one change</summary>
        public void ReplaceAll(IEnumerable<T> items)
        {
            CheckReentrancy();
            Items.Clear();
            foreach (var item in items) Items.Add(item);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
