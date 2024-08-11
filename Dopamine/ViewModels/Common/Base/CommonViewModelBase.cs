using System;
using Digimezzo.Foundation.Core.Settings;
using Digimezzo.Foundation.Core.Utils;
using Dopamine.Core.Utils;
using Dopamine.Data;
using Dopamine.Services.Collection;
using Dopamine.Services.Dialog;
using Dopamine.Services.I18n;
using Dopamine.Services.Indexing;
using Dopamine.Services.Metadata;
using Dopamine.Services.Playback;
using Dopamine.Services.Playlist;
using Dopamine.Services.Search;
using Dopamine.Views.Common;
using Prism.Commands;
using Prism.Events;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Prism.Ioc;
using Dopamine.Services.Folders;

namespace Dopamine.ViewModels.Common.Base
{
    public abstract class CommonViewModelBase : ContextMenuViewModelBase
    {
        private IContainerProvider container;
        private IIndexingService indexingService;
        private ICollectionService collectionService;
        private IMetadataService metadataService;
        private II18nService i18nService;
        private IPlaybackService playbackService;
        private IDialogService dialogService;
        private ISearchService searchService;
        private IPlaylistService playlistService;
        private IFoldersService foldersService;
        private IEventAggregator eventAggregator;
        private bool enableRating;
        private bool enableLove;
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

        public DelegateCommand AddToBlacklistCommand { get; set; }
        public DelegateCommand ShowSelectedTrackInformationCommand { get; set; }
        public DelegateCommand<object> SelectedTracksCommand { get; set; }
        public DelegateCommand EditTracksCommand { get; set; }
        public DelegateCommand PlaySelectedCommand { get; set; }
        public DelegateCommand PlayNextCommand { get; set; }
        public DelegateCommand AddTracksToNowPlayingCommand { get; set; }
        public DelegateCommand ShuffleAllCommand { get; set; }
        public DelegateCommand LoadedCommand { get; set; }
        public DelegateCommand UnloadedCommand { get; set; }

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

        public CommonViewModelBase(IContainerProvider container) : base(container)
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
            this.foldersService = container.Resolve<IFoldersService>();

            // Commands
            this.AddToBlacklistCommand = new DelegateCommand(() => this.AddToBlacklistAsync());
            this.ShowSelectedTrackInformationCommand = new DelegateCommand(() => this.ShowSelectedTrackInformation());
            this.SelectedTracksCommand = new DelegateCommand<object>((parameter) => this.SelectedTracksHandler(parameter));
            this.EditTracksCommand = new DelegateCommand(() => this.EditSelectedTracks(), () => !this.IsIndexing);
            this.LoadedCommand = new DelegateCommand(async () => await this.LoadedCommandAsync());
            this.UnloadedCommand = new DelegateCommand(async () => await this.UnloadedCommandAsync());
            this.ShuffleAllCommand = new DelegateCommand(() => this.playbackService.EnqueueAsync(true, false));

            // Events
            this.playbackService.PlaybackFailed += (_, __) => this.ShowPlayingTrackAsync();
            this.playbackService.PlaybackPaused += (_, __) => this.ShowPlayingTrackAsync();
            this.playbackService.PlaybackResumed += (_, __) => this.ShowPlayingTrackAsync();
            this.playbackService.PlaybackStopped += (_, __) => this.ShowPlayingTrackAsync();
            this.playbackService.PlaybackSuccess += (_,__) => this.ShowPlayingTrackAsync();
            this.collectionService.CollectionChanged += async (_, __) => await this.FillListsAsync(); // Refreshes the lists when the Collection has changed
            this.foldersService.FoldersChanged += async (_, __) => await this.FillListsAsync(); // Refreshes the lists when marked folders have changed
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
                case TrackOrder.ByModification:
                    this.trackOrderText = ResourceUtils.GetString("Language_By_Date_Created");
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
                string message = ResourceUtils.GetString("Language_Song_Cannot_Be_Found");

                if (paths.Count > 1)
                {
                    message = ResourceUtils.GetString("Language_Songs_Cannot_Be_Found");
                }

                this.dialogService.ShowNotification(
                    0xe711, 
                    16, 
                    ResourceUtils.GetString("Language_Error"), 
                    message, 
                    ResourceUtils.GetString("Language_Ok"), false, string.Empty);
            }

            return allSelectedTracksExist;
        }

        protected void ShowFileInformation(List<string> paths)
        {
            if (this.CheckAllSelectedFilesExist(paths))
            {
                FileInformation view = this.container.Resolve<FileInformation>();
                view.DataContext = this.container.Resolve<Func<string,FileInformationViewModel>>()(paths.First());

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
                view.DataContext = this.container.Resolve<Func<IList<string>, EditTrackViewModel>>()(paths);

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
        protected abstract Task EmptyListsAsync();
        protected abstract void FilterLists();
        protected abstract void ConditionalScrollToPlayingTrack();
        protected abstract void MetadataService_RatingChangedAsync(RatingChangedEventArgs e);
        protected abstract void MetadataService_LoveChangedAsync(LoveChangedEventArgs e);
        protected abstract Task AddToBlacklistAsync();
        protected abstract void ShowSelectedTrackInformation();
        protected abstract Task LoadedCommandAsync();
        protected abstract Task UnloadedCommandAsync();
        protected abstract void EditSelectedTracks();
        protected abstract void SelectedTracksHandler(object parameter);
    }
}
