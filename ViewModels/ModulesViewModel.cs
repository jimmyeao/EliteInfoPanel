﻿
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using EliteInfoPanel.Controls;
using EliteInfoPanel.Core;
using EliteInfoPanel.Util;
using Serilog;
using static MaterialDesignThemes.Wpf.Theme.ToolBar;

namespace EliteInfoPanel.ViewModels
{
    public class ModulesViewModel : CardViewModel
    {
        private readonly GameStateService _gameState;
        private int _currentPage = 0;
        private readonly DispatcherTimer _pageTimer;
        public double AvailableHeight { get; set; }
        private List<ModuleItemViewModel> _allModules = new();


        public ObservableCollection<ModuleItemViewModel> LeftItems { get; } = new();
        public ObservableCollection<ModuleItemViewModel> RightItems { get; } = new();
        public Func<int, Task>? OnRequestPageFade { get; set; }

        private int _fontSize = 14;
        public override double FontSize
        {
            get => base.FontSize;
            set
            {
                if (base.FontSize != value)
                {
                    base.FontSize = value;

                    foreach (var item in LeftItems)
                        item.FontSize = (int)value;

                    foreach (var item in RightItems)
                        item.FontSize = (int)value;
                }
            }
        }




        private List<List<ModuleItemViewModel>> _pagedLeft = new();
        private List<List<ModuleItemViewModel>> _pagedRight = new();



        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (SetProperty(ref _currentPage, value))
                {
                    LoadCurrentPage(); // Only load, don’t rebuild anything
                }
            }
        }




        public ModulesViewModel(GameStateService gameState) : base("Ship Modules")
        {
            _gameState = gameState;
            // Remove ColumnSpan = 2 assignment

            // Subscribe to game state events
            _gameState.DataUpdated += UpdateModules;

            // Initial update
            try
            {
                UpdateModules();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Exception during initial UpdateModules call.");
            }
            _pageTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _pageTimer.Tick += PageTimer_Tick;
            _pageTimer.Start();



        }
        public void Dispose()
        {
            _pageTimer.Stop();
        }
        private void PageTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (_pagedLeft != null && _pagedLeft.Count > 0)
                {
                    NextPage();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unhandled exception in module page timer.");
            }
        }


        public async void NextPage()
        {
            if (_pagedLeft == null || _pagedLeft.Count == 0)
                return;

            int nextPage = (CurrentPage + 1) % _pagedLeft.Count;

            if (OnRequestPageFade != null)
            {
                await OnRequestPageFade.Invoke(nextPage); // pass target page
            }
            else
            {
                CurrentPage = nextPage;
                LoadCurrentPage();
            }
        }




        public void LoadCurrentPage()
        {
            Log.Information("📥 Loading page {PageIndex}", CurrentPage);
            if (_pagedLeft == null || _pagedRight == null ||
                _pagedLeft.Count == 0 || _pagedRight.Count == 0 ||
                CurrentPage >= _pagedLeft.Count || CurrentPage >= _pagedRight.Count)
                return;

            LeftItems.Clear();
            RightItems.Clear();

            foreach (var item in _pagedLeft[CurrentPage])
            {
                if (item == null)
                {
                    Log.Error("Null item found in _pagedRight[{page}]", CurrentPage);
                }
                if (item != null)
                    // log the item
                    //   Log.Debug("Adding LeftItem: {name} ({slot} on page {page})", item.Name, item.Slot, CurrentPage     );
                    Log.Information("⬅️ Left: {Name} ({Health:P0})", item.Name, item.Health);
                LeftItems.Add(item);
            }

            foreach (var item in _pagedRight[CurrentPage])
            {
                if (item == null)
                {
                    Log.Error("Null item found in _pagedRight[{page}]", CurrentPage);
                }
                else
                {
                    //  Log.Debug("Adding RightItem: {name} ({slot}) - Health: {health}", item.Name, item.Slot, item.Health);
                    // log the item
                    Log.Information("➡️ Right: {Name} ({Health:P0})", item.Name, item.Health);
                    RightItems.Add(item);
                }
            }
        }


        private int CalculateRowsPerColumn()
        {
            double estimatedRowHeight = FontSize * 1.8; // accounts for wrapping/margins
            if (estimatedRowHeight <= 0 || AvailableHeight <= 0)
                return 10;

            int rows = (int)(AvailableHeight / estimatedRowHeight);
            Log.Information("🧮 Rows per column based on {Height}px available: {Rows}", AvailableHeight, rows);
            return Math.Max(rows, 2); // minimum of 2
        }



        public void UpdateModules()
        {
            RunOnUIThread(() =>
            {
                try
                {
                    LeftItems.Clear();
                    RightItems.Clear();

                    // ✅ Preserve the current page before clearing
                    int previousPage = CurrentPage;

                    _pagedLeft.Clear();
                    _pagedRight.Clear();

                    var status = _gameState.CurrentStatus;

                    IsVisible = status != null &&
                                status.Flags.HasFlag(Flag.InMainShip) &&
                                !status.OnFoot &&
                                !status.Flags.HasFlag(Flag.InSRV) &&
                                !status.Flags.HasFlag(Flag.InFighter);

                    if (!IsVisible || _gameState.CurrentLoadout?.Modules == null)
                        return;

                    var rawModules = _gameState.CurrentLoadout.Modules;

                    if (rawModules == null || rawModules.Count == 0)
                        return;

                    var fontSize = (int)this.FontSize;

                    var modules = rawModules
                        .Where(m =>
                            m != null &&
                            !string.IsNullOrWhiteSpace(m.Item) &&
                            !m.Item.StartsWith("Decal_", StringComparison.OrdinalIgnoreCase) &&
                            !m.Item.StartsWith("Nameplate_", StringComparison.OrdinalIgnoreCase) &&
                            !m.Item.StartsWith("PaintJob_", StringComparison.OrdinalIgnoreCase) &&
                            !m.Item.StartsWith("VoicePack_", StringComparison.OrdinalIgnoreCase) &&
                            !m.Item.Contains("spoiler", StringComparison.OrdinalIgnoreCase) &&
                            !m.Item.Contains("bumper", StringComparison.OrdinalIgnoreCase) &&
                            !m.Item.Contains("bobble", StringComparison.OrdinalIgnoreCase) &&
                            !m.Item.Contains("weaponcustomisation", StringComparison.OrdinalIgnoreCase) &&
                            !m.Item.Contains("enginecustomisation", StringComparison.OrdinalIgnoreCase) &&
                            !m.Item.Contains("wings", StringComparison.OrdinalIgnoreCase)
                        )
                        .Select(module => new ModuleItemViewModel(
                            ModuleNameMapper.GetFriendlyName(module.ItemLocalised ?? module.Item),
                            module.Health,
                            module.Slot,
                            module.On)
                        {
                            FontSize = fontSize
                        })
                        .ToList();
                    Log.Information("🔧 Found {Count} valid modules", modules.Count);
                    _allModules = modules;

                    int rowsPerColumn = CalculateRowsPerColumn();
                    int itemsPerPage = rowsPerColumn * 2;

                    var pages = new List<(List<ModuleItemViewModel> Left, List<ModuleItemViewModel> Right)>();

                    for (int i = 0; i < modules.Count; i += itemsPerPage)
                    {
                        var pageItems = modules.Skip(i).Take(itemsPerPage).ToList();
                        int half = (int)Math.Ceiling(pageItems.Count / 2.0);
                        var left = pageItems.Take(half).ToList();
                        var right = pageItems.Skip(half).ToList();

                        pages.Add((left, right));
                        Log.Information("📄 Page {Page}: {LeftCount} left, {RightCount} right", pages.Count - 1, left.Count, right.Count);
                    }

                    _pagedLeft = pages.Select(p => p.Left).ToList();
                    _pagedRight = pages.Select(p => p.Right).ToList();
                    Log.Information("✅ Generated {PageCount} pages", _pagedLeft.Count);


                    if (_currentPage >= _pagedLeft.Count)
                        CurrentPage = Math.Max(0, _pagedLeft.Count - 1);
                    else
                        LoadCurrentPage(); // ✅ otherwise, reuse current page





                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Unhandled exception in UpdateModules");
                }
            });
        }

        public void RebuildPaginationOnly()
        {
            if (_allModules == null || !_allModules.Any()) return;

            int rowsPerColumn = CalculateRowsPerColumn();
            int itemsPerPage = rowsPerColumn * 2;
            int totalPages = (int)Math.Ceiling(_allModules.Count / (double)itemsPerPage);

            _pagedLeft.Clear();
            _pagedRight.Clear();

            for (int i = 0; i < totalPages; i++)
            {
                var pageItems = _allModules.Skip(i * itemsPerPage).Take(itemsPerPage).ToList();
                int half = (int)Math.Ceiling(pageItems.Count / 2.0);
                _pagedLeft.Add(pageItems.Take(half).ToList());
                _pagedRight.Add(pageItems.Skip(half).ToList());
            }

            Log.Information("🌀 Rebuilt pagination: {PageCount} pages", totalPages);

            if (_currentPage >= _pagedLeft.Count)
                _currentPage = Math.Max(0, _pagedLeft.Count - 1);

            LoadCurrentPage();
        }

        public class ModuleItemViewModel : ViewModelBase
        {
            private string _name;
            private float _health;
            private string _slot;
            private bool _isOn;
            private int _fontSize = 14;
            public int FontSize
            {
                get => _fontSize;
                set => SetProperty(ref _fontSize, value);
            }
            public string Name
            {
                get => _name;
                set => SetProperty(ref _name, value);
            }

            public float Health
            {
                get => _health;
                set => SetProperty(ref _health, value);
            }

            public string Slot
            {
                get => _slot;
                set => SetProperty(ref _slot, value);
            }

            public bool IsOn
            {
                get => _isOn;
                set => SetProperty(ref _isOn, value);
            }

            public Brush HealthColor
            {
                get
                {
                    try
                    {
                        if (float.IsNaN(Health) || float.IsInfinity(Health))
                            return Brushes.Gray;

                        return Health < 0.7f ? Brushes.Red :
                        Health < 0.95f ? Brushes.Orange :
                               Brushes.White;
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error computing HealthColor for {Name}", Name);
                        return Brushes.Gray;
                    }
                }
            }


            public ModuleItemViewModel(string name, float health, string slot, bool isOn)
            {
                _name = name;
                _health = health;
                _slot = slot;
                _isOn = isOn;




            }

        }
    }
}