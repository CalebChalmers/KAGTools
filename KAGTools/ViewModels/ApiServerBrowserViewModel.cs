﻿using GalaSoft.MvvmLight.Command;
using KAGTools.Data.API;
using KAGTools.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace KAGTools.ViewModels
{
    public class ApiServerBrowserViewModel : FilterListViewModelBase<ApiServer>
    {
        private BitmapImage _minimapBitmap;
        private string _searchFilter = "";

        private CancellationTokenSource MinimapCancellationSource { get; set; } = new CancellationTokenSource();
        private bool RefreshingServers { get; set; } = false;

        public ApiServerBrowserViewModel()
            : base()
        {
            RefreshServersCommand = new RelayCommand(ExecuteRefreshServersCommand);

            SortDescriptions.Add(new SortDescription("PlayerCount", ListSortDirection.Descending));

            PropertyChanged += async (s, e) =>
            {
                if (e.PropertyName == "Selected" && Selected != null)
                {
                    await UpdateMinimapAsync();
                }
            };

            var tmp = RefreshServersAsync();
        }

        protected override bool FilterItem(ApiServer item)
        {
            return item.Name.IndexOf(SearchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public BitmapImage MinimapBitmap
        {
            get { return _minimapBitmap; }
            set
            {
                if (_minimapBitmap != value)
                {
                    _minimapBitmap = value;
                    RaisePropertyChanged();
                }
            }
        }

        public string SearchFilter
        {
            get { return _searchFilter; }
            set
            {
                if (_searchFilter != value)
                {
                    _searchFilter = value;
                    RaisePropertyChanged();
                    RefreshFilters();
                }
            }
        }

        public int ServerCount
        {
            get { return Items.Count; }
        }

        public int PlayerCount
        {
            get
            {
                int total = 0;
                foreach (int count in Items.Select(s => s.PlayerCount))
                {
                    total += count;
                }
                return total;
            }
        }

        public ICommand RefreshServersCommand { get; private set; }

        private async void ExecuteRefreshServersCommand()
        {
            await RefreshServersAsync();
        }

        private async Task RefreshServersAsync()
        {
            if (RefreshingServers) return;
            RefreshingServers = true;

            Items.Clear();

            try
            {
                ApiServer[] results = await ApiHelper.GetServers(
                    new ApiFilter[] {
                        new ApiFilter("current", true)
                    },
                    CancellationToken.None
                    );
                Items = new ObservableCollection<ApiServer>(results);
            }
            catch (System.Net.Http.HttpRequestException) { }

            RefreshingServers = false;

            RaisePropertyChanged("ServerCount");
            RaisePropertyChanged("PlayerCount");
        }

        private async Task UpdateMinimapAsync()
        {
            MinimapCancellationSource.Cancel();
            MinimapCancellationSource = new CancellationTokenSource();

            MinimapBitmap = null;

            try
            {
                var bitmap = await ApiHelper.GetServerMinimap(Selected.IPv4Address, Selected.Port, MinimapCancellationSource.Token);
                MinimapBitmap = bitmap;
            }
            catch (TaskCanceledException) { }
            catch (System.Net.Http.HttpRequestException) { }
        }
    }
}
