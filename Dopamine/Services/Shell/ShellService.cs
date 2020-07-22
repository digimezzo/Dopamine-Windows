﻿using Dopamine.Core.Alex;  //Digimezzo.Foundation.Core.Settings
using Dopamine.Core.Base;
using Dopamine.Core.Enums;
using Dopamine.Core.Prism;
using Dopamine.Services.WindowsIntegration;
using Dopamine.Views;
using Prism.Commands;
using Prism.Events;
using Prism.Regions;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace Dopamine.Services.Shell
{
    public class ShellService : IShellService
    {
        private IRegionManager regionManager;
        private IWindowsIntegrationService windowsIntegrationService;
        private IEventAggregator eventAggregator;
        private ActiveMiniPlayerPlaylist activeMiniPlayerPlaylist = ActiveMiniPlayerPlaylist.None;
        private bool canSaveWindowGeometry;
        private bool isMiniPlayerActive;
        string nowPlayingPage;
        private string fullPlayerPage;
        private string coverPlayerPage;
        private string microplayerPage;
        private string nanoPlayerPage;

        public event WindowStateChangedEventHandler WindowStateChanged = delegate { };
        public event WindowStateChangeRequestedEventHandler WindowStateChangeRequested = delegate { };
        public event PlaylistVisibilityChangeRequestedEventHandler PlaylistVisibilityChangeRequested = delegate { };
        public event IsMovableChangeRequestedEventHandler IsMovableChangeRequested = delegate { };
        public event ResizeModeChangeRequestedEventHandler ResizeModeChangeRequested = delegate { };
        public event TopmostChangeRequestedEventHandler TopmostChangeRequested = delegate { };
        public event MinimumSizeChangeRequestedEventHandler MinimumSizeChangeRequested = delegate { };
        public event GeometryChangeRequestedEventHandler GeometryChangeRequested = delegate { };

        public DelegateCommand ShowNowPlayingCommand { get; set; }

        public DelegateCommand ShowFullPlayerCommmand { get; set; }

        public DelegateCommand TogglePlayerCommand { get; set; }

        public DelegateCommand<string> ChangePlayerTypeCommand { get; set; }

        public DelegateCommand<bool?> CoverPlayerPlaylistButtonCommand { get; set; }

        public DelegateCommand<bool?> MicroPlayerPlaylistButtonCommand { get; set; }

        public DelegateCommand<bool?> NanoPlayerPlaylistButtonCommand { get; set; }

        public DelegateCommand ToggleMiniPlayerPositionLockedCommand { get; set; }

        public DelegateCommand ToggleMiniPlayerAlwaysOnTopCommand { get; set; }

        public WindowState WindowState { get; set; }

        public ShellService(IRegionManager regionManager, IWindowsIntegrationService windowsIntegrationService, IEventAggregator eventAggregator,
            string nowPlayingPage, string fullPlayerPage, string coverPlayerPage, string microplayerPage, string nanoPlayerPage)
        {
            this.regionManager = regionManager;
            this.windowsIntegrationService = windowsIntegrationService;
            this.eventAggregator = eventAggregator;
            this.nowPlayingPage = nowPlayingPage;
            this.fullPlayerPage = fullPlayerPage;
            this.coverPlayerPage = coverPlayerPage;
            this.microplayerPage = microplayerPage;
            this.nanoPlayerPage = nanoPlayerPage;

            this.ShowNowPlayingCommand = new DelegateCommand(() =>
            {
                this.regionManager.RequestNavigate(RegionNames.PlayerTypeRegion, this.nowPlayingPage);
                SettingsClient.Set<bool>("FullPlayer", "IsNowPlayingSelected", true);
                this.eventAggregator.GetEvent<IsNowPlayingPageActiveChanged>().Publish(true);
            });

            ApplicationCommands.ShowNowPlayingCommand.RegisterCommand(this.ShowNowPlayingCommand);

            this.ShowFullPlayerCommmand = new DelegateCommand(() =>
            {
                this.regionManager.RequestNavigate(RegionNames.PlayerTypeRegion, this.fullPlayerPage);
                SettingsClient.Set<bool>("FullPlayer", "IsNowPlayingSelected", false);
                this.eventAggregator.GetEvent<IsNowPlayingPageActiveChanged>().Publish(false);
            });

            ApplicationCommands.ShowFullPlayerCommand.RegisterCommand(this.ShowFullPlayerCommmand);

            // Window state
            this.WindowState = SettingsClient.Get<bool>("FullPlayer", "IsMaximized") ? WindowState.Maximized : WindowState.Normal;

            // Player type
            this.ChangePlayerTypeCommand = new DelegateCommand<string>((miniPlayerType) =>
            this.SetPlayer(true, (MiniPlayerType)Convert.ToInt32(miniPlayerType)));
            ApplicationCommands.ChangePlayerTypeCommand.RegisterCommand(this.ChangePlayerTypeCommand);

            this.TogglePlayerCommand = new DelegateCommand(() =>
            {
                // If tablet mode is enabled, we should not be able to toggle the player.
                if (!this.windowsIntegrationService.IsTabletModeEnabled)
                {
                    this.TogglePlayer();
                }
            });

            ApplicationCommands.TogglePlayerCommand.RegisterCommand(this.TogglePlayerCommand);

            // Mini Player Playlist
            this.CoverPlayerPlaylistButtonCommand = new DelegateCommand<bool?>(isPlaylistButtonChecked =>
            {
                this.ToggleMiniPlayerPlaylist(MiniPlayerType.CoverPlayer, isPlaylistButtonChecked.Value);
            });

            ApplicationCommands.CoverPlayerPlaylistButtonCommand.RegisterCommand(this.CoverPlayerPlaylistButtonCommand);

            this.MicroPlayerPlaylistButtonCommand = new DelegateCommand<bool?>(isPlaylistButtonChecked =>
            {
                this.ToggleMiniPlayerPlaylist(MiniPlayerType.MicroPlayer, isPlaylistButtonChecked.Value);
            });

            ApplicationCommands.MicroPlayerPlaylistButtonCommand.RegisterCommand(this.MicroPlayerPlaylistButtonCommand);

            this.NanoPlayerPlaylistButtonCommand = new DelegateCommand<bool?>(isPlaylistButtonChecked =>
            {
                this.ToggleMiniPlayerPlaylist(MiniPlayerType.NanoPlayer, isPlaylistButtonChecked.Value);
            });

            ApplicationCommands.NanoPlayerPlaylistButtonCommand.RegisterCommand(this.NanoPlayerPlaylistButtonCommand);

            // Mini Player
            this.ToggleMiniPlayerPositionLockedCommand = new DelegateCommand(() =>
            {
                bool isMiniPlayerPositionLocked = SettingsClient.Get<bool>("Behaviour", "MiniPlayerPositionLocked");
                SettingsClient.Set<bool>("Behaviour", "MiniPlayerPositionLocked", !isMiniPlayerPositionLocked);
                this.SetWindowPositionLockedFromSettings();
            });

            ApplicationCommands.ToggleMiniPlayerPositionLockedCommand.RegisterCommand(this.ToggleMiniPlayerPositionLockedCommand);

            this.ToggleMiniPlayerAlwaysOnTopCommand = new DelegateCommand(() =>
            {
                bool topmost = SettingsClient.Get<bool>("Behaviour", "MiniPlayerOnTop");
                SettingsClient.Set<bool>("Behaviour", "MiniPlayerOnTop", !topmost);
                this.SetWindowTopmostFromSettings();
            });

            ApplicationCommands.ToggleMiniPlayerAlwaysOnTopCommand.RegisterCommand(this.ToggleMiniPlayerAlwaysOnTopCommand);
        }

        private void TogglePlayer()
        {
            if (this.isMiniPlayerActive)
            {
                // Show the Full Player
                this.SetPlayer(false, MiniPlayerType.CoverPlayer);
            }
            else
            {
                // Show the Mini Player, with the player type which is saved in the settings
                this.SetPlayer(true, (MiniPlayerType)SettingsClient.Get<int>("General", "MiniPlayerType"));
            }
        }

        public async void SetPlayer(bool isMiniPlayer, MiniPlayerType miniPlayerType, bool isInitializing = false)
        {
            string screenName = typeof(Empty).FullName;

            // Clear player content
            this.regionManager.RequestNavigate(RegionNames.PlayerTypeRegion, typeof(Empty).FullName);

            // Save the player type in the settings
            SettingsClient.Set<bool>("General", "IsMiniPlayer", isMiniPlayer);

            // Only save the Mini Player Type in the settings if the current player is set to the Mini Player
            if (isMiniPlayer) SettingsClient.Set<int>("General", "MiniPlayerType", (int)miniPlayerType);

            // Prevents saving window state and size to the Settings XML while switching players
            this.canSaveWindowGeometry = false;

            // Sets the geometry of the player
            if (isMiniPlayer | (!this.windowsIntegrationService.IsTabletModeEnabled & this.windowsIntegrationService.IsStartedFromExplorer))
            {
                switch (miniPlayerType)
                {
                    case MiniPlayerType.CoverPlayer:
                        this.SetMiniPlayer(MiniPlayerType.CoverPlayer, this.activeMiniPlayerPlaylist == ActiveMiniPlayerPlaylist.CoverPlayer);
                        screenName = this.coverPlayerPage;
                        break;
                    case MiniPlayerType.MicroPlayer:
                        this.SetMiniPlayer(MiniPlayerType.MicroPlayer, this.activeMiniPlayerPlaylist == ActiveMiniPlayerPlaylist.MicroPlayer);
                        screenName = this.microplayerPage;
                        break;
                    case MiniPlayerType.NanoPlayer:
                        this.SetMiniPlayer(MiniPlayerType.NanoPlayer, this.activeMiniPlayerPlaylist == ActiveMiniPlayerPlaylist.NanoPlayer);
                        screenName = this.nanoPlayerPage;
                        break;
                    default:
                        break;
                        // Doesn't happen
                }
            }
            else
            {
                this.SetFullPlayer();

                // Default case
                screenName = this.fullPlayerPage;

                // Special cases
                if (SettingsClient.Get<bool>("FullPlayer", "IsNowPlayingSelected"))
                {
                    if (isInitializing)
                    {
                        if (SettingsClient.Get<bool>("Startup", "ShowLastSelectedPage"))
                        {
                            screenName = this.nowPlayingPage;
                        }
                    }
                    else
                    {
                        screenName = this.nowPlayingPage;
                    }
                }
            }

            // Determine if the player position is locked
            this.SetWindowPositionLockedFromSettings();

            // Determine if the player is 
            this.SetWindowTopmostFromSettings();

            // Delay, otherwise content is never shown (probably because regions don't exist yet at startup)
            await Task.Delay(150);

            // Navigate to content
            this.regionManager.RequestNavigate(RegionNames.PlayerTypeRegion, screenName);

            this.canSaveWindowGeometry = true;
        }

        private void SetFullPlayer()
        {
            this.isMiniPlayerActive = false;

            this.PlaylistVisibilityChangeRequested(this, new PlaylistVisibilityChangeRequestedEventArgs(false, MiniPlayerType.CoverPlayer));

            this.ResizeModeChangeRequested(this, new ResizeModeChangeRequestedEventArgs(ResizeMode.CanResize));

            if (SettingsClient.Get<bool>("FullPlayer", "IsMaximized"))
            {
                this.WindowStateChangeRequested(this, new WindowStateChangeRequestedEventArgs(WindowState.Maximized));
            }
            else
            {
                this.WindowStateChangeRequested(this, new WindowStateChangeRequestedEventArgs(WindowState.Normal));

                this.GeometryChangeRequested(this, new GeometryChangeRequestedEventArgs(
                    SettingsClient.Get<int>("FullPlayer", "Top"),
                    SettingsClient.Get<int>("FullPlayer", "Left"),
                    new Size(SettingsClient.Get<int>("FullPlayer", "Width"), SettingsClient.Get<int>("FullPlayer", "Height"))));
            }

            // Set MinWidth and MinHeight AFTER SetGeometry(). This prevents flicker.
            this.MinimumSizeChangeRequested(this, new MinimumSizeChangeRequestedEventArgs(new Size(Constants.MinShellWidth, Constants.MinShellHeight)));
        }

        private void SetMiniPlayer(MiniPlayerType miniPlayerType, bool openPlaylist)
        {
            this.isMiniPlayerActive = true;

            // Hide the playlist BEFORE changing window dimensions to avoid strange behaviour
            this.PlaylistVisibilityChangeRequested(this, new PlaylistVisibilityChangeRequestedEventArgs(false, MiniPlayerType.CoverPlayer));

            this.WindowStateChangeRequested(this, new WindowStateChangeRequestedEventArgs(WindowState.Normal));
            this.ResizeModeChangeRequested(this, new ResizeModeChangeRequestedEventArgs(ResizeMode.CanMinimize));

            double width = 0;
            double height = 0;

            switch (miniPlayerType)
            {
                case MiniPlayerType.CoverPlayer:

                    width = Constants.CoverPlayerWidth;
                    height = Constants.CoverPlayerHeight;
                    break;
                case MiniPlayerType.MicroPlayer:
                    width = Constants.MicroPlayerWidth;
                    height = Constants.MicroPlayerHeight;
                    break;
                case MiniPlayerType.NanoPlayer:
                    width = Constants.NanoPlayerWidth;
                    height = Constants.NanoPlayerHeight;
                    break;
                default:
                    // Can't happen
                    break;
            }

            // Set MinWidth and MinHeight BEFORE SetMiniPlayerDimensions(). This prevents flicker.
            Size minimumSize = new Size(width, height);

            if (SettingsClient.Get<bool>("Appearance", "ShowWindowBorder"))
            {
                // Correction to take into account the window border, otherwise the content 
                // misses 2px horizontally and vertically when displaying the window border
                minimumSize = new Size(width + 2, height + 2);
            }

            this.MinimumSizeChangeRequested(this, new MinimumSizeChangeRequestedEventArgs(minimumSize));

            this.GeometryChangeRequested(this, new GeometryChangeRequestedEventArgs(
                SettingsClient.Get<int>("MiniPlayer", "Top"),
                SettingsClient.Get<int>("MiniPlayer", "Left"),
                minimumSize));

            // Show the playlist AFTER changing window dimensions to avoid strange behavior
            if (openPlaylist)
            {
                this.PlaylistVisibilityChangeRequested(this, new PlaylistVisibilityChangeRequestedEventArgs(true, miniPlayerType));
            }
        }

        public void CheckIfTabletMode(bool isInitializing)
        {
            if (this.windowsIntegrationService.IsTabletModeEnabled)
            {
                // Always revert to full player when tablet mode is enabled. Maximizing will be done by Windows.
                this.SetPlayer(false, (MiniPlayerType)SettingsClient.Get<int>("General", "MiniPlayerType"), isInitializing);
            }
            else
            {
                bool isMiniPlayer = SettingsClient.Get<bool>("General", "IsMiniPlayer");
                bool isMaximized = SettingsClient.Get<bool>("FullPlayer", "IsMaximized");
                this.WindowStateChangeRequested(this, new WindowStateChangeRequestedEventArgs(isMaximized & !isMiniPlayer ? WindowState.Maximized : WindowState.Normal));

                this.SetPlayer(isMiniPlayer, (MiniPlayerType)SettingsClient.Get<int>("General", "MiniPlayerType"), isInitializing);
            }
        }

        public void SaveWindowLocation(double top, double left, WindowState state)
        {
            if (this.canSaveWindowGeometry)
            {
                if (this.isMiniPlayerActive)
                {
                    SettingsClient.Set<int>("MiniPlayer", "Top", Convert.ToInt32(top));
                    SettingsClient.Set<int>("MiniPlayer", "Left", Convert.ToInt32(left));
                }
                else if (state != WindowState.Maximized)
                {
                    SettingsClient.Set<int>("FullPlayer", "Top", Convert.ToInt32(top));
                    SettingsClient.Set<int>("FullPlayer", "Left", Convert.ToInt32(left));
                }
            }
        }

        private void SetWindowTopmostFromSettings()
        {
            if (this.isMiniPlayerActive)
            {
                this.TopmostChangeRequested(this, new TopmostChangeRequestedEventArgs(SettingsClient.Get<bool>("Behaviour", "MiniPlayerOnTop")));
            }
            else
            {
                // Full player is never topmost
                this.TopmostChangeRequested(this, new TopmostChangeRequestedEventArgs(false));
            }
        }

        private void SetWindowPositionLockedFromSettings()
        {
            // Only lock position when the mini player is active
            if (this.isMiniPlayerActive)
            {
                this.IsMovableChangeRequested(this, new IsMovableChangeRequestedEventArgs(!SettingsClient.Get<bool>("Behaviour", "MiniPlayerPositionLocked")));
            }
            else
            {
                this.IsMovableChangeRequested(this, new IsMovableChangeRequestedEventArgs(true));
            }
        }

        private void ToggleMiniPlayerPlaylist(MiniPlayerType miniPlayerType, bool isPlaylistVisible)
        {
            if (isPlaylistVisible)
            {
                switch (miniPlayerType)
                {
                    case MiniPlayerType.CoverPlayer:
                        this.activeMiniPlayerPlaylist = ActiveMiniPlayerPlaylist.CoverPlayer;
                        break;
                    case MiniPlayerType.MicroPlayer:
                        this.activeMiniPlayerPlaylist = ActiveMiniPlayerPlaylist.MicroPlayer;
                        break;
                    case MiniPlayerType.NanoPlayer:
                        this.activeMiniPlayerPlaylist = ActiveMiniPlayerPlaylist.NanoPlayer;
                        break;
                    default:
                        break;
                        // Shouldn't happen
                }
            }
            else
            {
                this.activeMiniPlayerPlaylist = ActiveMiniPlayerPlaylist.None;
            }

            this.PlaylistVisibilityChangeRequested(this, new PlaylistVisibilityChangeRequestedEventArgs(isPlaylistVisible, miniPlayerType));
        }

        public void SaveWindowState(WindowState state)
        {
            this.WindowState = state;

            // Only save window state when not in tablet mode. Tablet mode maximizes the screen. 
            // We don't want to save that, as we want to be able to restore to the original state when leaving tablet mode.
            if (this.canSaveWindowGeometry & !this.windowsIntegrationService.IsTabletModeEnabled)
            {
                SettingsClient.Set<bool>("FullPlayer", "IsMaximized", state == WindowState.Maximized ? true : false);
            }

            this.WindowStateChanged(this, new WindowStateChangedEventArgs(state));
        }

        public void SaveWindowSize(WindowState state, Size size)
        {
            if (this.canSaveWindowGeometry)
            {
                if (!this.isMiniPlayerActive & state != WindowState.Maximized)
                {
                    SettingsClient.Set<int>("FullPlayer", "Width", Convert.ToInt32(size.Width));
                    SettingsClient.Set<int>("FullPlayer", "Height", Convert.ToInt32(size.Height));
                }
            }
        }
    }
}
