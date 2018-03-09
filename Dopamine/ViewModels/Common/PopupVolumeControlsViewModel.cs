﻿using Dopamine.Presentation.ViewModels;
using Dopamine.Services.Contracts.Playback;
using CommonServiceLocator;

namespace Dopamine.ViewModels.Common
{
    public class PopupVolumeControlsViewModel : VolumeControlsViewModel
    {
        public PopupVolumeControlsViewModel() : base(ServiceLocator.Current.GetInstance<IPlaybackService>())
        {
        }
    }
}
