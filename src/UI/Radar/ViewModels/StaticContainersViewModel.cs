using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using LoneEftDmaRadar.Tarkov;
using LoneEftDmaRadar.UI.Data;

namespace LoneEftDmaRadar.UI.Radar.ViewModels
{
    public sealed class StaticContainersViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public StaticContainersViewModel()
        {
            InitializeContainers();
        }

        public ObservableCollection<StaticContainerEntry> StaticContainers { get; } = new();

        public bool StaticContainersSelectAll
        {
            get => App.Config.Containers.SelectAll;
            set
            {
                if (App.Config.Containers.SelectAll != value)
                {
                    App.Config.Containers.SelectAll = value;
                    foreach (var item in StaticContainers)
                        item.IsTracked = value;
                    OnPropertyChanged(nameof(StaticContainersSelectAll));
                }
            }
        }

        private void InitializeContainers()
        {
            var entries = TarkovDataManager.AllContainers.Values
                .OrderBy(x => x.Name)
                .Select(x => new StaticContainerEntry(x));
            foreach (var entry in entries)
            {
                entry.PropertyChanged += Entry_PropertyChanged;
                StaticContainers.Add(entry);
            }

            // If SelectAll is true but Selected dictionary is empty, populate it
            // This ensures containers appear when SelectAll is unchecked
            if (App.Config.Containers.SelectAll && App.Config.Containers.Selected.IsEmpty)
            {
                foreach (var entry in StaticContainers)
                {
                    App.Config.Containers.Selected.TryAdd(entry.Id, 0);
                }
            }
        }

        private void Entry_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StaticContainerEntry.IsTracked))
            {
                var allSelected = StaticContainers.All(x => x.IsTracked);
                if (App.Config.Containers.SelectAll != allSelected)
                {
                    App.Config.Containers.SelectAll = allSelected;
                    OnPropertyChanged(nameof(StaticContainersSelectAll));
                }
            }
        }

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
