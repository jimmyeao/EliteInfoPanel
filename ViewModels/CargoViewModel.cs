﻿// CargoViewModel.cs
using System.Collections.ObjectModel;
using EliteInfoPanel.Core;
using EliteInfoPanel.Util;
using System.Linq;
using System.Windows;

namespace EliteInfoPanel.ViewModels
{
    public class CargoViewModel : CardViewModel
    {
        private readonly GameStateService _gameState;

        public ObservableCollection<CargoItemViewModel> Items { get; } = new();

        public CargoViewModel(GameStateService gameState) : base("Cargo")
        {
            _gameState = gameState;

            // Subscribe to game state updates
            _gameState.DataUpdated += UpdateCargo;

            // Initial update
            UpdateCargo();
        }

        private void UpdateCargo()
        {
            RunOnUIThread(() =>
            {
                Items.Clear();

                if (_gameState.CurrentCargo?.Inventory == null)
                    return;

                IsVisible = _gameState.CurrentCargo.Inventory.Count > 0;

                foreach (var item in _gameState.CurrentCargo.Inventory.OrderByDescending(i => i.Count))
                {
                    Items.Add(new CargoItemViewModel(
                        CommodityMapper.GetDisplayName(item.Name),
                        item.Count));
                }

                UpdateCargoTitle(); // ✅ Call this at the end
            });
        }

        private void UpdateCargoTitle()
        {
            int used = 0;
            int total = 0;

            if (_gameState.CurrentCargo?.Inventory != null)
                used = _gameState.CurrentCargo.Inventory.Sum(i => i.Count);

            if (_gameState.CurrentLoadout != null)
                total = _gameState.CurrentLoadout.CargoCapacity;

            Title = $"Cargo {used}/{total}";
        }

    }

    public class CargoItemViewModel : ViewModelBase
    {
        private string _name;
        private int _count;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public int Count
        {
            get => _count;
            set => SetProperty(ref _count, value);
        }

        public CargoItemViewModel(string name, int count)
        {
            _name = name;
            _count = count;
        }
    }
}
