﻿using Dopamine.Core.Alex;  //Digimezzo.Foundation.Core.Settings
using Dopamine.Services.Appearance;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;

namespace Dopamine.ViewModels.Common
{
    public class AppearanceThemeViewModel : BindableBase
    {
        private IAppearanceService appearanceService;
        private ObservableCollection<string> themes = new ObservableCollection<string>();
        private ObservableCollection<ColorScheme> colorSchemes = new ObservableCollection<ColorScheme>();
        private ColorScheme selectedColorScheme;
        private bool checkBoxWindowsColorChecked;
        private bool checkBoxAlbumCoverColorChecked;
        private bool checkBoxThemeChecked;

        public ObservableCollection<string> Themes
        {
            get { return this.themes; }
            set { SetProperty<ObservableCollection<string>>(ref this.themes, value); }
        }

        public bool CheckBoxThemeChecked
        {
            get { return this.checkBoxThemeChecked; }
            set
            {
                SettingsClient.Set<bool>("Appearance", "EnableLightTheme", value);
                Application.Current.Dispatcher.Invoke(() => this.appearanceService.ApplyTheme(value));
                SetProperty<bool>(ref this.checkBoxThemeChecked, value);
            }
        }

        public ObservableCollection<ColorScheme> ColorSchemes
        {
            get { return this.colorSchemes; }
            set { SetProperty<ObservableCollection<ColorScheme>>(ref this.colorSchemes, value); }
        }

        public ColorScheme SelectedColorScheme
        {
            get { return this.selectedColorScheme; }

            set
            {
                // value can be null when a ColorScheme is removed from the ColorSchemes directory
                if (value != null)
                {
                    SettingsClient.Set<string>("Appearance", "ColorScheme", value.Name);
                    this.ApplyColorScheme();
                }

                SetProperty<ColorScheme>(ref this.selectedColorScheme, value);
            }
        }

        public bool CheckBoxWindowsColorChecked
        {
            get { return this.checkBoxWindowsColorChecked; }

            set
            {
                this.checkBoxAlbumCoverColorChecked = false;
                this.RaisePropertyChanged(nameof(this.CheckBoxAlbumCoverColorChecked));

                SettingsClient.Set<bool>("Appearance", "FollowWindowsColor", value);
                SettingsClient.Set<bool>("Appearance", "FollowAlbumCoverColor", false);

                this.ApplyColorScheme();

                SetProperty<bool>(ref this.checkBoxWindowsColorChecked, value);
                this.RaisePropertyChanged(nameof(this.CanChooseColor));
            }
        }

        public bool CheckBoxAlbumCoverColorChecked
        {
            get { return this.checkBoxAlbumCoverColorChecked; }

            set
            {
                this.checkBoxWindowsColorChecked = false;
                this.RaisePropertyChanged(nameof(this.CheckBoxWindowsColorChecked));

                SettingsClient.Set<bool>("Appearance", "FollowWindowsColor", false);
                SettingsClient.Set<bool>("Appearance", "FollowAlbumCoverColor", value);

                this.ApplyColorScheme();

                SetProperty<bool>(ref this.checkBoxAlbumCoverColorChecked, value);
                this.RaisePropertyChanged(nameof(this.CanChooseColor));
            }
        }

        public bool CanChooseColor
        {
            get { return !this.CheckBoxWindowsColorChecked & !this.CheckBoxAlbumCoverColorChecked; }
        }

        public AppearanceThemeViewModel(IAppearanceService appearanceService)
        {
            this.appearanceService = appearanceService;

            this.GetColorSchemesAsync();
            this.GetCheckBoxesAsync();

            this.appearanceService.ColorSchemesChanged += ColorSchemesChangedHandler;
        }

        private async void GetColorSchemesAsync()
        {
            ObservableCollection<ColorScheme> localColorSchemes = new ObservableCollection<ColorScheme>();

            await Task.Run(() =>
            {
                foreach (ColorScheme cs in this.appearanceService.GetColorSchemes())
                {
                    localColorSchemes.Add(cs);
                }
            });

            this.ColorSchemes = localColorSchemes;

            string savedColorSchemeName = SettingsClient.Get<string>("Appearance", "ColorScheme");

            if (!string.IsNullOrEmpty(savedColorSchemeName))
            {
                this.SelectedColorScheme = this.appearanceService.GetColorScheme(savedColorSchemeName);
            }
            else
            {
                this.SelectedColorScheme = this.appearanceService.GetColorSchemes()[0];
            }
        }

        private void ApplyColorScheme()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                this.appearanceService.ApplyColorSchemeAsync(
                      SettingsClient.Get<string>("Appearance", "ColorScheme"),
                      SettingsClient.Get<bool>("Appearance", "FollowWindowsColor"),
                      SettingsClient.Get<bool>("Appearance", "FollowAlbumCoverColor"));
            });
        }

        private async void GetCheckBoxesAsync()
        {
            await Task.Run(() =>
            {
                this.checkBoxThemeChecked = SettingsClient.Get<bool>("Appearance", "EnableLightTheme");
                this.checkBoxWindowsColorChecked = SettingsClient.Get<bool>("Appearance", "FollowWindowsColor");
                this.checkBoxAlbumCoverColorChecked = SettingsClient.Get<bool>("Appearance", "FollowAlbumCoverColor");
            });
        }

        private void ColorSchemesChangedHandler(object sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() => this.GetColorSchemesAsync());
        }
    }
}
