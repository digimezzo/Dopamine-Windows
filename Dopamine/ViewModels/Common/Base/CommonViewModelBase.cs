﻿using Digimezzo.Utilities.Settings;
using Digimezzo.Utilities.Utils;
using Dopamine.Core.Utils;
using Dopamine.Data.Contracts;
using Dopamine.Services.Contracts.Collection;
using Dopamine.Services.Contracts.Dialog;
using Dopamine.Services.Contracts.I18n;
using Dopamine.Services.Contracts.Indexing;
using Dopamine.Services.Contracts.Metadata;
using Dopamine.Services.Contracts.Playback;
using Dopamine.Services.Contracts.Playlist;
using Dopamine.Services.Contracts.Search;
using Dopamine.Views.Common;
using Prism.Commands;
using Prism.Events;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity;
using Unity.Resolution;

namespace Dopamine.ViewModels.Common.Base
{
    public abstract class CommonViewModelBase : ContextMenuViewModelBase
    {
        private IUnityContainer container;
        private IIndexingService indexingService;
        private ICollectionService collectionService;
        private IMetadataService metadataService;
        private II18nService i18nService;
        private IPlaybackService playbackService;
        private IDialogService dialogService;
        private ISearchService searchService;
        private IPlaylistService playlistService;
        private IEventAggregator eventAggregator;
        private bool enableRating;
        private bool enableLove;
        private bool isFirstLoad = true;
        private bool isIndexing;
        private long tracksCount;
        private long totalDuration;
        private long totalSize;
        private TrackOrder trackOrder;
        private string trackOrderText;
        private string searchTextBeforeInactivate = string.Empty;

        public DelegateCommand ToggleTrackOrderCommand { get; set; }
        public DelegateCommand RemoveSelectedTracksCommand { get; set; }
        public DelegateCommand RemoveSelectedTracksFromDiskCommand { get; set; }
        public DelegateCommand<string> AddTracksToPlaylistCommand { get; set; }
        public DelegateCommand ShowSelectedTrackInformationCommand { get; set; }
        public DelegateCommand<object> SelectedTracksCommand { get; set; }
        public DelegateCommand EditTracksCommand { get; set; }
        public DelegateCommand PlaySelectedCommand { get; set; }
        public DelegateCommand PlayNextCommand { get; set; }
        public DelegateCommand AddTracksToNowPlayingCommand { get; set; }
        public DelegateCommand ShuffleAllCommand { get; set; }
        public DelegateCommand LoadedCommand { get; set; }

        public string TotalSizeInformation => this.totalSize > 0 ? FormatUtils.FormatFileSize(this.totalSize, false) : string.Empty;
        public string TotalDurationInformation => this.totalDuration > 0 ? FormatUtils.FormatDuration(this.totalDuration) : string.Empty;
        public string TrackOrderText => this.trackOrderText;

        public long TracksCount
        {
            get { return this.tracksCount; }
            set { SetProperty<long>(ref this.tracksCount, value); }
        }

        public bool EnableRating
        {
            get { return this.enableRating; }
            set { SetProperty<bool>(ref this.enableRating, value); }
        }

        public bool EnableLove
        {
            get { return this.enableLove; }
            set { SetProperty<bool>(ref this.enableLove, value); }
        }

        public bool IsIndexing
        {
            get { return this.isIndexing; }
            set { SetProperty<bool>(ref this.isIndexing, value); }
        }

        public TrackOrder TrackOrder
        {
            get { return this.trackOrder; }
            set
            {
                SetProperty<TrackOrder>(ref this.trackOrder, value);
                this.UpdateTrackOrderText(value);
            }
        }

        public CommonViewModelBase(IUnityContainer container) : base(container)
        {
            // Dependency injection
            this.container = container;
            this.eventAggregator = container.Resolve<IEventAggregator>();
            this.indexingService = container.Resolve<IIndexingService>();
            this.playbackService = container.Resolve<IPlaybackService>();
            this.searchService = container.Resolve<ISearchService>();
            this.dialogService = container.Resolve<IDialogService>();
            this.collectionService = container.Resolve<ICollectionService>();
            this.metadataService = container.Resolve<IMetadataService>();
            this.i18nService = container.Resolve<II18nService>();
            this.playlistService = container.Resolve<IPlaylistService>();

            // Commands
            this.ShowSelectedTrackInformationCommand = new DelegateCommand(() => this.ShowSelectedTrackInformation());
            this.SelectedTracksCommand = new DelegateCommand<object>((parameter) => this.SelectedTracksHandler(parameter));
            this.EditTracksCommand = new DelegateCommand(() => this.EditSelectedTracks(), () => !this.IsIndexing);
            this.LoadedCommand = new DelegateCommand(async () => await this.LoadedCommandAsync());
            this.ShuffleAllCommand = new DelegateCommand(() => this.playbackService.EnqueueAsync(true, false));

            // Events
            this.playbackService.PlaybackFailed += (_, __) => this.ShowPlayingTrackAsync();
            this.playbackService.PlaybackPaused += (_, __) => this.ShowPlayingTrackAsync();
            this.playbackService.PlaybackResumed += (_, __) => this.ShowPlayingTrackAsync();
            this.playbackService.PlaybackStopped += (_, __) => this.ShowPlayingTrackAsync();
            this.playbackService.PlaybackSuccess += (_,__) => this.ShowPlayingTrackAsync();
            this.collectionService.CollectionChanged += async (_, __) => await this.FillListsAsync(); // Refreshes the lists when the Collection has changed
            this.indexingService.RefreshLists += async (_, __) => await this.FillListsAsync(); // Refreshes the lists when the indexer has finished indexing
            this.indexingService.IndexingStarted += (_, __) => this.SetEditCommands();
            this.indexingService.IndexingStopped += (_, __) => this.SetEditCommands();
            this.searchService.DoSearch += (searchText) => this.FilterLists();
            this.metadataService.RatingChanged += MetadataService_RatingChangedAsync;
            this.metadataService.LoveChanged += MetadataService_LoveChangedAsync;

            // Flags
            this.EnableRating = SettingsClient.Get<bool>("Behaviour", "EnableRating");
            this.EnableLove = SettingsClient.Get<bool>("Behaviour", "EnableLove");

            // This makes sure the IsIndexing is correct even when this ViewModel is 
            // created after the Indexer is started, and thus after triggering the 
            // IndexingService.IndexerStarted event.
            this.SetEditCommands();
        }

        protected bool IsFirstLoad()
        {
            bool originalIsFirstLoad = this.isFirstLoad;
            if (this.isFirstLoad) this.isFirstLoad = false;

            return originalIsFirstLoad;
        }

        protected void SetSizeInformation(long totalDuration, long totalSize)
        {
            this.totalDuration = totalDuration;
            this.totalSize = totalSize;
        }

        protected void UpdateTrackOrderText(TrackOrder trackOrder)
        {
            switch (trackOrder)
            {
                case TrackOrder.Alphabetical:
                    this.trackOrderText = ResourceUtils.GetString("Language_A_Z");
                    break;
                case TrackOrder.ReverseAlphabetical:
                    this.trackOrderText = ResourceUtils.GetString("Language_Z_A");
                    break;
                case TrackOrder.ByAlbum:
                    this.trackOrderText = ResourceUtils.GetString("Language_By_Album");
                    break;
                case TrackOrder.ByRating:
                    this.trackOrderText = ResourceUtils.GetString("Language_By_Rating");
                    break;
                default:
                    // Cannot happen, but just in case.
                    this.trackOrderText = ResourceUtils.GetString("Language_By_Album");
                    break;
            }

            RaisePropertyChanged(nameof(this.TrackOrderText));
        }

        protected bool CheckAllSelectedFilesExist(List<string> paths)
        {
            bool allSelectedTracksExist = true;

            foreach (string path in paths)
            {
                if (!System.IO.File.Exists(path))
                {
                    allSelectedTracksExist = false;
                    break;
                }
            }

            if (!allSelectedTracksExist)
            {
                string message = ResourceUtils.GetString("Language_Song_Cannot_Be_Found_Refresh_Collection");
                if (paths.Count > 1) message = ResourceUtils.GetString("Language_Songs_Cannot_Be_Found_Refresh_Collection");

                if (this.dialogService.ShowConfirmation(0xe11b, 16, ResourceUtils.GetString("Language_Refresh"), message, ResourceUtils.GetString("Language_Yes"), ResourceUtils.GetString("Language_No")))
                {
                    this.indexingService.RefreshCollectionImmediatelyAsync();
                }
            }

            return allSelectedTracksExist;
        }

        protected void ShowFileInformation(List<string> paths)
        {
            if (this.CheckAllSelectedFilesExist(paths))
            {
                FileInformation view = this.container.Resolve<FileInformation>();
                view.DataContext = this.container.Resolve<FileInformationViewModel>(new DependencyOverride(typeof(string), paths.First()));

                this.dialogService.ShowCustomDialog(
                    0xe8d6,
                    16,
                    ResourceUtils.GetString("Language_Information"),
                    view,
                    400,
                    620,
                    true,
                    true,
                    true,
                    false,
                    ResourceUtils.GetString("Language_Ok"),
                    string.Empty,
                    null);
            }
        }

        protected void EditFiles(List<string> paths)
        {
            if (this.CheckAllSelectedFilesExist(paths))
            {
                EditTrack view = this.container.Resolve<EditTrack>();
                view.DataContext = this.container.Resolve<EditTrackViewModel>(new DependencyOverride(typeof(IList<string>), paths));

                this.dialogService.ShowCustomDialog(
                    0xe104,
                    14,
                    ResourceUtils.GetString("Language_Edit_Song"),
                    view,
                    620,
                    660,
                    false,
                    false,
                    false,
                    true,
                    ResourceUtils.GetString("Language_Ok"),
                    ResourceUtils.GetString("Language_Cancel"),
                ((EditTrackViewModel)view.DataContext).SaveTracksAsync);
            }
        }

        protected virtual void SetEditCommands()
        {
            this.IsIndexing = this.indexingService.IsIndexing;

            if (this.EditTracksCommand != null) this.EditTracksCommand.RaiseCanExecuteChanged();
            if (this.RemoveSelectedTracksCommand != null) this.RemoveSelectedTracksCommand.RaiseCanExecuteChanged();
        }

        protected abstract Task ShowPlayingTrackAsync();
        protected abstract Task FillListsAsync();
        protected abstract void FilterLists();
        protected abstract void ConditionalScrollToPlayingTrack();
        protected abstract void MetadataService_RatingChangedAsync(RatingChangedEventArgs e);
        protected abstract void MetadataService_LoveChangedAsync(LoveChangedEventArgs e);
        protected abstract void ShowSelectedTrackInformation();
        protected abstract Task LoadedCommandAsync();
        protected abstract void EditSelectedTracks();
        protected abstract void SelectedTracksHandler(object parameter);
    }
}
