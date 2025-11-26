/*
 * Lone EFT DMA Radar
 * Brought to you by Lone (Lone DMA)
 * 
MIT License

Copyright (c) 2025 Lone DMA

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
 *
*/

using LoneEftDmaRadar.UI.Hotkeys;
using LoneEftDmaRadar.UI.Radar.ViewModels;
using LoneEftDmaRadar.UI.ESP;

namespace LoneEftDmaRadar
{
    public sealed class MainWindowViewModel
    {
        private readonly MainWindow _parent;
        //public event PropertyChangedEventHandler PropertyChanged;

        public MainWindowViewModel(MainWindow parent)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
            EnsureHotkeysRegistered();
        }

        public void ToggleFullscreen(bool toFullscreen)
        {
            if (toFullscreen)
            {
                // Full‐screen
                _parent.WindowStyle = WindowStyle.None;
                _parent.ResizeMode = ResizeMode.NoResize;
                _parent.Topmost = true;
                _parent.WindowState = WindowState.Maximized;
            }
            else
            {
                _parent.WindowStyle = WindowStyle.SingleBorderWindow;
                _parent.ResizeMode = ResizeMode.CanResize;
                _parent.Topmost = false;
                _parent.WindowState = WindowState.Normal;
            }
        }

        #region Hotkey Manager

        private const int HK_ZOOMTICKAMT = 5; // amt to zoom
        private const int HK_ZOOMTICKDELAY = 120; // ms

        /// <summary>
        /// Loads Hotkey Manager resources.
        /// Only call from Primary Thread/Window (ONCE!)
        /// </summary>
        private bool _hotkeysRegistered;

        internal void EnsureHotkeysRegistered()
        {
            if (_hotkeysRegistered)
                return;
            LoadHotkeyManager();
            _hotkeysRegistered = true;
        }

        private void LoadHotkeyManager()
        {
            var zoomIn = new HotkeyActionController("雷达放大");
            zoomIn.Delay = HK_ZOOMTICKDELAY;
            zoomIn.HotkeyDelayElapsed += ZoomIn_HotkeyDelayElapsed;
            var zoomOut = new HotkeyActionController("雷达缩小");
            zoomOut.Delay = HK_ZOOMTICKDELAY;
            zoomOut.HotkeyDelayElapsed += ZoomOut_HotkeyDelayElapsed;
            var toggleLoot = new HotkeyActionController("开关雷达战利品");
            toggleLoot.HotkeyStateChanged += ToggleLoot_HotkeyStateChanged;
            var toggleAimviewWidget = new HotkeyActionController("开关自瞄范围显示");
            toggleAimviewWidget.HotkeyStateChanged += ToggleAimviewWidget_HotkeyStateChanged;
            var toggleNames = new HotkeyActionController("开关玩家名字显示");
            toggleNames.HotkeyStateChanged += ToggleNames_HotkeyStateChanged;
            var toggleInfo = new HotkeyActionController("开关对局信息显示");
            toggleInfo.HotkeyStateChanged += ToggleInfo_HotkeyStateChanged;
            var toggleShowFood = new HotkeyActionController("开关食品显示");
            toggleShowFood.HotkeyStateChanged += ToggleShowFood_HotkeyStateChanged;
            var toggleShowMeds = new HotkeyActionController("开关药物显示");
            toggleShowMeds.HotkeyStateChanged += ToggleShowMeds_HotkeyStateChanged;
            var engageAimbotDeviceAimbot = new HotkeyActionController("开关自瞄");
            engageAimbotDeviceAimbot.HotkeyStateChanged += EngageAimbotDeviceAimbot_HotkeyStateChanged;
            var toggleDeviceAimbotEnabled = new HotkeyActionController("开关盒子连接");
            toggleDeviceAimbotEnabled.HotkeyStateChanged += ToggleDeviceAimbotEnabled_HotkeyStateChanged;
            
            var toggleESP = new HotkeyActionController("开关ESP绘制");
            toggleESP.HotkeyStateChanged += ToggleESP_HotkeyStateChanged;
            var toggleESPPlayers = new HotkeyActionController("开关ESP 玩家绘制");
            toggleESPPlayers.HotkeyStateChanged += ToggleESPPlayers_HotkeyStateChanged;
            var toggleESPScavs = new HotkeyActionController("开关Scav/AI 绘制");
            toggleESPScavs.HotkeyStateChanged += ToggleESPScavs_HotkeyStateChanged;
            var toggleESPLoot = new HotkeyActionController("开关ESP 物品绘制");
            toggleESPLoot.HotkeyStateChanged += ToggleESPLoot_HotkeyStateChanged;
            var toggleESPExfils = new HotkeyActionController("开关ESP 撤离点绘制");
            toggleESPExfils.HotkeyStateChanged += ToggleESPExfils_HotkeyStateChanged;
            
            // Add to Static Collection:
            HotkeyAction.RegisterController(zoomIn);
            HotkeyAction.RegisterController(zoomOut);
            HotkeyAction.RegisterController(toggleLoot);
            HotkeyAction.RegisterController(toggleAimviewWidget);
            HotkeyAction.RegisterController(toggleNames);
            HotkeyAction.RegisterController(toggleInfo);
            HotkeyAction.RegisterController(toggleShowFood);
            HotkeyAction.RegisterController(toggleShowMeds);
            HotkeyAction.RegisterController(toggleESP);
            HotkeyAction.RegisterController(toggleESPPlayers);
            HotkeyAction.RegisterController(toggleESPScavs);
            HotkeyAction.RegisterController(toggleESPLoot);
            HotkeyAction.RegisterController(toggleESPExfils);
            HotkeyAction.RegisterController(engageAimbotDeviceAimbot);
            HotkeyAction.RegisterController(toggleDeviceAimbotEnabled);
            HotkeyManagerViewModel.NotifyControllersRegistered();
        }

        internal static void EnsureHotkeysRegisteredStatic()
        {
            MainWindow.Instance?.ViewModel?.EnsureHotkeysRegistered();
        }

        private void ToggleAimviewWidget_HotkeyStateChanged(object sender, HotkeyEventArgs e)
        {
            if (e.State && _parent.Settings?.ViewModel is SettingsViewModel vm)
                vm.AimviewWidget = !vm.AimviewWidget;
        }

        private void ToggleShowMeds_HotkeyStateChanged(object sender, HotkeyEventArgs e)
        {
            if (e.State && _parent.Radar?.Overlay?.ViewModel is RadarOverlayViewModel vm)
            {
                vm.ShowMeds = !vm.ShowMeds;
            }
        }

        private void EngageAimbotDeviceAimbot_HotkeyStateChanged(object sender, HotkeyEventArgs e)
        {
            if (_parent.DeviceAimbot?.ViewModel is DeviceAimbotViewModel DeviceAimbotAim)
            {
                DeviceAimbotAim.IsEngaged = e.State;
            }
        }

        private void ToggleDeviceAimbotEnabled_HotkeyStateChanged(object sender, HotkeyEventArgs e)
        {
            if (!e.State)
                return;

            if (_parent.DeviceAimbot?.ViewModel is DeviceAimbotViewModel vm)
            {
                vm.Enabled = !vm.Enabled;
            }
        }

        private void ToggleShowFood_HotkeyStateChanged(object sender, HotkeyEventArgs e)
        {
            if (e.State && _parent.Radar?.Overlay?.ViewModel is RadarOverlayViewModel vm)
            {
                vm.ShowFood = !vm.ShowFood;
            }
        }

        private void ToggleInfo_HotkeyStateChanged(object sender, HotkeyEventArgs e)
        {
            if (e.State && _parent.Settings?.ViewModel is SettingsViewModel vm)
                vm.PlayerInfoWidget = !vm.PlayerInfoWidget;
        }

        private void ToggleNames_HotkeyStateChanged(object sender, HotkeyEventArgs e)
        {
            if (e.State && _parent.Settings?.ViewModel is SettingsViewModel vm)
                vm.HideNames = !vm.HideNames;
        }

        private void ToggleLoot_HotkeyStateChanged(object sender, HotkeyEventArgs e)
        {
            if (e.State && _parent.Settings?.ViewModel is SettingsViewModel vm)
                vm.ShowLoot = !vm.ShowLoot;
        }

        private void ZoomOut_HotkeyDelayElapsed(object sender, EventArgs e)
        {
            _parent.Radar?.ViewModel?.ZoomOut(HK_ZOOMTICKAMT);
        }

        private void ZoomIn_HotkeyDelayElapsed(object sender, EventArgs e)
        {
            _parent.Radar?.ViewModel?.ZoomIn(HK_ZOOMTICKAMT);
        }

        private void ToggleESP_HotkeyStateChanged(object sender, HotkeyEventArgs e)
        {
            if (e.State)
            {
                ESPManager.ToggleESP();
            }
        }

        private void ToggleESPPlayers_HotkeyStateChanged(object sender, HotkeyEventArgs e)
        {
            if (e.State)
            {
                bool newState = !App.Config.UI.EspPlayerSkeletons;
                App.Config.UI.EspPlayerSkeletons = newState;
                App.Config.UI.EspPlayerBoxes = newState;
                App.Config.UI.EspPlayerNames = newState;
                App.Config.UI.EspPlayerDistance = newState;
            }
        }

        private void ToggleESPScavs_HotkeyStateChanged(object sender, HotkeyEventArgs e)
        {
            if (e.State)
            {
                bool newState = !App.Config.UI.EspAISkeletons;
                App.Config.UI.EspAISkeletons = newState;
                App.Config.UI.EspAIBoxes = newState;
                App.Config.UI.EspAINames = newState;
                App.Config.UI.EspAIDistance = newState;
            }
        }

        private void ToggleESPLoot_HotkeyStateChanged(object sender, HotkeyEventArgs e)
        {
            if (e.State)
            {
                App.Config.UI.EspLoot = !App.Config.UI.EspLoot;
            }
        }

        private void ToggleESPExfils_HotkeyStateChanged(object sender, HotkeyEventArgs e)
        {
            if (e.State)
            {
                App.Config.UI.EspExfils = !App.Config.UI.EspExfils;
            }
        }

        #endregion
    }
}
