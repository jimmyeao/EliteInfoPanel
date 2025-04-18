﻿using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using EliteInfoPanel.Core;
using EliteInfoPanel.Dialogs;
using EliteInfoPanel.Util;
using EliteInfoPanel.ViewModels;
using Serilog;
using WpfScreenHelper;

namespace EliteInfoPanel
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private Screen _currentScreen;
        private readonly AppSettings _appSettings;

        public MainWindow()
        {
            InitializeComponent();

            // Configure logging
            LoggingConfig.Configure(enableDebugLogging: false);

            // Load settings
            _appSettings = SettingsManager.Load();

            // Initialize default font scales if needed
            if (_appSettings.FullscreenFontScale <= 0)
                _appSettings.FullscreenFontScale = 1.0;

            if (_appSettings.FloatingFontScale <= 0)
                _appSettings.FloatingFontScale = 1.0;

            // Initialize the GameStateService
            var gamePath = EliteDangerousPaths.GetSavedGamesPath();
            var gameState = new GameStateService(gamePath);

            // Create and set ViewModel
            var settings = SettingsManager.Load();
            _viewModel = new MainViewModel(gameState, settings.UseFloatingWindow);

            _viewModel.SetMainGrid(MainGrid);
            DataContext = _viewModel;

            // Connect OpenOptionsCommand to event handler
            _viewModel.OpenOptionsCommand = new RelayCommand(_ => OpenOptions());

            // Set up event handlers
            Loaded += Window_Loaded;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            Closing += MainWindow_Closing;
            _viewModel.ApplyWindowModeFromSettings();
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            SaveWindowPosition();
        }

        private void SaveWindowPosition()
        {
            if (_appSettings.UseFloatingWindow && WindowState == WindowState.Normal)
            {
                _appSettings.FloatingWindowLeft = Left;
                _appSettings.FloatingWindowTop = Top;
                _appSettings.FloatingWindowWidth = Width;
                _appSettings.FloatingWindowHeight = Height;
                SettingsManager.Save(_appSettings);

                Log.Information("Saved floating window position: {Left}x{Top} {Width}x{Height}",
                    Left, Top, Width, Height);
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Apply window mode settings
            ApplyWindowSettings();
        }

        private void ApplyWindowSettings()
        {
            if (_appSettings.UseFloatingWindow)
            {
                // Apply floating window settings
                WindowStyle = WindowStyle.SingleBorderWindow;
                ResizeMode = ResizeMode.CanResize;
                Topmost = _appSettings.AlwaysOnTop;
                WindowState = WindowState.Normal;

                // Set size and position from saved settings
                Left = _appSettings.FloatingWindowLeft;
                Top = _appSettings.FloatingWindowTop;
                Width = _appSettings.FloatingWindowWidth;
                Height = _appSettings.FloatingWindowHeight;

                // Ensure window is within visible screen bounds
                EnsureWindowIsVisible();
                _viewModel.IsFullScreenMode = !_appSettings.UseFloatingWindow;
                Log.Information("Applied floating window settings: {Left}x{Top} {Width}x{Height}",
                    Left, Top, Width, Height);

                // Show the floating title bar
                //FloatingTitleBar.Visibility = Visibility.Visible;
            }
            else
            {
                // Position on the selected screen in full-screen mode
                var allScreens = Screen.AllScreens;

                _currentScreen = allScreens.FirstOrDefault(s =>
                    s.DeviceName == _appSettings.SelectedScreenId) ?? allScreens.FirstOrDefault();

                if (_currentScreen == null)
                {
                    var dialog = new SelectScreenDialog(allScreens.ToList(), this);

                    if (dialog.ShowDialog() == true && dialog.SelectedScreen != null)
                    {
                        _currentScreen = dialog.SelectedScreen;
                        _appSettings.UseFloatingWindow = false;
                        _appSettings.SelectedScreenId = _currentScreen.DeviceName;
                        _appSettings.SelectedScreenBounds = _currentScreen.WpfBounds;

                        SettingsManager.Save(_appSettings); // ✅ ensure saved
                    }
                    else
                    {
                        _currentScreen = allScreens.FirstOrDefault();
                    }
                }
                else
                {
                    // Even if screen was already known, save if switching to fullscreen
                    _appSettings.UseFloatingWindow = false;
                    SettingsManager.Save(_appSettings); // ✅ ensure saved
                }

                ApplyScreenBounds(_currentScreen);

                // Hide the floating title bar in fullscreen mode
               // FloatingTitleBar.Visibility = Visibility.Collapsed;

                Log.Information("Applied full-screen settings on screen: {Screen}",
                    _currentScreen?.DeviceName ?? "Unknown");
            }

            // ✅ Update font scaling for all cards
            UpdateFontSizes();
        }

        public void UpdateFontSizes()
        {
            double fontScale = _appSettings.UseFloatingWindow
                ? _appSettings.FloatingFontScale
                : _appSettings.FullscreenFontScale;

            double baseFontSize = _appSettings.UseFloatingWindow
                ? AppSettings.DEFAULT_FLOATING_BASE * fontScale
                : AppSettings.DEFAULT_FULLSCREEN_BASE * fontScale;

            Log.Debug("Updating font sizes with scale {Scale}, resulting in base size {BaseSize}",
                fontScale, baseFontSize);

            foreach (var card in _viewModel.Cards)
            {
                card.FontSize = baseFontSize;
            }

            // Update font resources for dynamic styles
            UpdateFontResources();

            // Force layout update
            _viewModel.RefreshLayout();
        }

        private void EnsureWindowIsVisible()
        {
            // Make sure the window isn't positioned off-screen
            var virtualScreenWidth = SystemParameters.VirtualScreenWidth;
            var virtualScreenHeight = SystemParameters.VirtualScreenHeight;

            if (Left < 0) Left = 0;
            if (Top < 0) Top = 0;
            if (Left + Width > virtualScreenWidth)
                Left = Math.Max(0, virtualScreenWidth - Width);
            if (Top + Height > virtualScreenHeight)
                Top = Math.Max(0, virtualScreenHeight - Height);

            // Ensure minimum size
            Width = Math.Max(Width, 400);
            Height = Math.Max(Height, 300);
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F12)
            {
                OpenCurrentLogFile();
            }
            else if (e.Key == Key.Escape && _appSettings.UseFloatingWindow)
            {
                // Allow ESC to close in floating window mode
                Close();
            }
        }

        private void OpenCurrentLogFile()
        {
            try
            {
                string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string logDirectory = System.IO.Path.Combine(appDataFolder, "EliteInfoPanel");

                if (!System.IO.Directory.Exists(logDirectory))
                {
                    MessageBox.Show("Log directory not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Find the most recently modified log file
                var latestLog = System.IO.Directory.GetFiles(logDirectory, "EliteInfoPanel_Log*.log")
                                         .OrderByDescending(System.IO.File.GetLastWriteTime)
                                         .FirstOrDefault();

                if (latestLog == null)
                {
                    MessageBox.Show("No log files found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = latestLog,
                    UseShellExecute = true
                });

                // Also open explorer to the Elite Dangerous saved games folder
                string gameSavePath = EliteDangerousPaths.GetSavedGamesPath();
                if (System.IO.Directory.Exists(gameSavePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = gameSavePath,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open log file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void FloatingTitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Enable dragging of the window when user clicks and drags the title bar
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            // Minimize the window
            this.WindowState = WindowState.Minimized;
        }
        private void CloseButton_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                Serilog.Log.Information("CloseButton_Loaded → IsFullScreenMode = {Fullscreen}", vm.IsFullScreenMode);
            }
            else
            {
                Serilog.Log.Warning("CloseButton_Loaded: DataContext not set or incorrect type.");
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Close the application
            this.Close();
        }
        // Make sure this method exists in MainWindow.xaml.cs:

        private void UpdateFontResources()
        {
            // Get the current font scale based on window mode
            double fontScale = _appSettings.UseFloatingWindow
                ? _appSettings.FloatingFontScale
                : _appSettings.FullscreenFontScale;

            // Apply the scale to the base font sizes
            double baseFontSize = _appSettings.UseFloatingWindow
                ? AppSettings.DEFAULT_FLOATING_BASE * fontScale
                : AppSettings.DEFAULT_FULLSCREEN_BASE * fontScale;

            double headerFontSize = _appSettings.UseFloatingWindow
                ? AppSettings.DEFAULT_FLOATING_HEADER * fontScale
                : AppSettings.DEFAULT_FULLSCREEN_HEADER * fontScale;

            double smallFontSize = _appSettings.UseFloatingWindow
                ? AppSettings.DEFAULT_FLOATING_SMALL * fontScale
                : AppSettings.DEFAULT_FULLSCREEN_SMALL * fontScale;

            // Update application resources
            if (Application.Current != null && Application.Current.Resources != null)
            {
                Application.Current.Resources["BaseFontSize"] = baseFontSize;
                Application.Current.Resources["HeaderFontSize"] = headerFontSize;
                Application.Current.Resources["SmallFontSize"] = smallFontSize;
            }

            // Update each card's font size directly
            if (_viewModel != null)
            {
                foreach (var card in _viewModel.Cards)
                {
                    card.FontSize = baseFontSize;
                }
            }

            Log.Debug("Updated font resources for {Mode} mode with scale {Scale}: Base={Base}, Header={Header}, Small={Small}",
                _appSettings.UseFloatingWindow ? "floating window" : "full screen",
                fontScale, baseFontSize, headerFontSize, smallFontSize);
        }

        private void ApplyScreenBounds(Screen targetScreen)
        {
            WindowState = WindowState.Normal; // Force out of maximized state
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;

            Left = targetScreen.WpfBounds.Left;
            Top = targetScreen.WpfBounds.Top;
            Width = targetScreen.WpfBounds.Width;
            Height = targetScreen.WpfBounds.Height;

            Topmost = true;
            WindowState = WindowState.Maximized;
        }

        // Replace only the OpenOptions() method in MainWindow.xaml.cs with this:

        // Replace only the OpenOptions() method in MainWindow.xaml.cs with this:

        // Use this version of the OpenOptions() method in MainWindow.xaml.cs

        private void OpenOptions()
        {
            var options = new OptionsWindow();

            // Important: Set initial settings reference
            options.Settings.FloatingFontScale = _appSettings.FloatingFontScale;
            options.Settings.FullscreenFontScale = _appSettings.FullscreenFontScale;

            options.ScreenChanged += screen =>
            {
                _currentScreen = screen;

                if (!_appSettings.UseFloatingWindow)
                {
                    ApplyScreenBounds(screen);
                }
            };

            // This is the key handler for real-time updates
            options.FontSizeChanged += () =>
            {
                // Directly update our app settings from the dialog's settings to get real-time values
                _appSettings.FloatingFontScale = options.Settings.FloatingFontScale;
                _appSettings.FullscreenFontScale = options.Settings.FullscreenFontScale;

                // Now apply the changes immediately
                UpdateFontResources();
                App.RefreshResources();
                InvalidateVisual();
                _viewModel.RefreshLayout();
                UpdateLayout();

                Log.Debug("Live font update - Current scale: {0}, Floating: {1}, Fullscreen: {2}",
                    _appSettings.UseFloatingWindow ? _appSettings.FloatingFontScale : _appSettings.FullscreenFontScale,
                    _appSettings.FloatingFontScale,
                    _appSettings.FullscreenFontScale);
            };

            // If dialog is closed with OK
            if (options.ShowDialog() == true)
            {
                // Since we can't reassign _appSettings, we'll manually update the important properties
                var updatedSettings = SettingsManager.Load();

                // Check if window mode changed
                bool modeChanged = updatedSettings.UseFloatingWindow != _appSettings.UseFloatingWindow;

                // Update individual properties instead of the whole object
                _appSettings.UseFloatingWindow = updatedSettings.UseFloatingWindow;
                _appSettings.FloatingFontScale = updatedSettings.FloatingFontScale;
                _appSettings.FullscreenFontScale = updatedSettings.FullscreenFontScale;
                _appSettings.AlwaysOnTop = updatedSettings.AlwaysOnTop;

                // Update window settings if mode changed
                if (modeChanged)
                {
                    Log.Information("Window mode changed - reapplying window settings");
                    ApplyWindowSettings();
                    _viewModel.ApplyWindowModeFromSettings(); // 🔥 Ensure IsFullScreenMode gets updated

                }
                else
                {
                    // Always update font resources to ensure consistency
                    UpdateFontResources();
                    App.RefreshResources();
                    InvalidateVisual();
                    _viewModel.RefreshLayout();
                    UpdateLayout();
                }
            }
        }
    }
}