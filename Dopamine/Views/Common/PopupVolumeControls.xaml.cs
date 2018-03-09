﻿using Digimezzo.Utilities.Log;
using Dopamine.Views.Common.Base;
using Dopamine.Presentation.Views.Base;
using Dopamine.Services.Contracts.Playback;
using Dopamine.Services.Playback;
using CommonServiceLocator;
using System;
using System.Timers;
using System.Windows;
using System.Windows.Input;

namespace Dopamine.Views.Common
{
    public partial class PopupVolumeControls : VolumeControlViewBase
    {
        private IPlaybackService playBackService;
        private Timer mouseWheelTimer;
        private double mouseWheelTimeout = 0.5;
        private bool keepOpenAfterScrolling;
     
        public new object DataContext
        {
            get { return base.DataContext; }
            set { base.DataContext = value; }
        }
      
        public PopupVolumeControls()
        {
            InitializeComponent();

            // We need a parameterless constructor to be able to use this UserControl in other UserControls without dependency injection.
            // So for now there is no better solution than to find the EventAggregator by using the ServiceLocator.
            this.playBackService = ServiceLocator.Current.GetInstance<IPlaybackService>();

            this.mouseWheelTimer = new Timer();
            this.mouseWheelTimer.Interval = TimeSpan.FromSeconds(this.mouseWheelTimeout).TotalMilliseconds;
            this.mouseWheelTimer.Elapsed += new ElapsedEventHandler(this.MouseWheelTimerElapsed);
            this.VolumeButtonPopup.Closed += (sender, e) => this.keepOpenAfterScrolling = false;

            // This doesn't work with binding
            this.VolumeButton.Width = this.Width;
            this.VolumeButton.Height = this.Height;
        }
    
        private void VolumeButton_Click(object sender, RoutedEventArgs e)
        {
            this.keepOpenAfterScrolling = true;
            this.mouseWheelTimer.Stop();
            VolumeButtonPopup.Open();
        }

        private void Grid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!VolumeButtonPopup.IsOpen)
            {
                VolumeButtonPopup.Open();
            }

            this.mouseWheelTimer.Stop();

            if (!this.keepOpenAfterScrolling)
                this.mouseWheelTimer.Start();

            try
            {
                this.playBackService.Volume = Convert.ToSingle(this.playBackService.Volume + this.CalculateVolumeDelta(e.Delta));
            }
            catch (Exception ex)
            {
                LogClient.Error("There was a problem changing the volume by mouse scroll. Exception: {0}", ex.Message);
            }
        }

        private void MouseWheelTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (this.mouseWheelTimer != null)
                this.mouseWheelTimer.Stop();

            Application.Current.Dispatcher.BeginInvoke(new Action(() => VolumeButtonPopup.Close()));
        }

        private void Border_MouseEnter(object sender, MouseEventArgs e)
        {
            this.keepOpenAfterScrolling = true;
            this.mouseWheelTimer.Stop();
        }

        private void Border_MouseLeave(object sender, MouseEventArgs e)
        {
            this.mouseWheelTimer.Start();
        }
    }
}
