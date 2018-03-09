﻿using Dopamine.Services.Playback;
using CommonServiceLocator;
using System.Windows;
using Dopamine.Core.Enums;
using System.Windows.Controls;
using Dopamine.Services.Contracts.Playback;

namespace Dopamine.Views.Common
{
    public partial class SpectrumAnalyzerControl : UserControl
    {
        private IPlaybackService playbackService;
       
        public new object DataContext
        {
            get { return base.DataContext; }
            set { base.DataContext = value; }
        }
      
        public SpectrumAnalyzerControl()
        {
            InitializeComponent();

            this.playbackService = ServiceLocator.Current.GetInstance<IPlaybackService>();
            this.playbackService.PlaybackSuccess += (_,__) => this.RegisterPlayer();

            // Just in case we switched Views after the playBackService.PlaybackSuccess was triggered
            this.RegisterPlayer();
        }
       
        private void RegisterPlayer()
        {
            if(this.playbackService.Player != null)
            {
                Application.Current.Dispatcher.Invoke(() => { this.LeftSpectrumAnalyzer.RegisterSoundPlayer(this.playbackService.Player.GetWrapperSpectrumPlayer(SpectrumChannel.Left)); });
                Application.Current.Dispatcher.Invoke(() => { this.RightSpectrumAnalyzer.RegisterSoundPlayer(this.playbackService.Player.GetWrapperSpectrumPlayer(SpectrumChannel.Right)); });
            }
        }
    }
}
