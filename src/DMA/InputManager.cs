/*
 * Lone EFT DMA Radar
 * Brought to you by Lone (Lone DMA)
 * 
 * MIT License
 */

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using LoneEftDmaRadar.Misc.Workers;
using LoneEftDmaRadar.UI.Hotkeys;
using LoneEftDmaRadar.UI.Misc;
using VmmSharpEx;
using VmmSharpEx.Extensions.Input;

namespace LoneEftDmaRadar.DMA
{
    /// <summary>
    /// Central input poller for hotkeys.
    /// - Primary source: VmmInputManager (Win32 keyboard/mouse via MemProcFS)
    /// - Failsafe / secondary source: DeviceAimbot device (mouse buttons)
    /// </summary>
    public sealed class InputManager
    {
        private readonly VmmInputManager _input;   // may be null if Win32 backend failed
        private readonly WorkerThread _thread;

        /// <summary>
        /// True if VmmInputManager (Win32) backend is available.
        /// </summary>
        public bool IsWin32BackendAvailable => _input is not null;

        public InputManager(Vmm vmm)
        {
            try
            {
                _input = new VmmInputManager(vmm);
                DebugLogger.LogDebug("[InputManager] VmmInputManager initialized.");
            }
            catch (Exception ex)
            {
                // Do NOT throw; this is our failsafe.
                _input = null;
                DebugLogger.LogDebug($"[InputManager] Failed to initialize VmmInputManager (Win32 backend). " +
                                $"Hotkeys will use DeviceAimbot-only fallback if available. {ex}");
            }

            _thread = new WorkerThread
            {
                Name = nameof(InputManager),
                SleepDuration = TimeSpan.FromMilliseconds(12),
                SleepMode = WorkerThreadSleepMode.DynamicSleep
            };
            _thread.PerformWork += InputManager_PerformWork;
            _thread.Start();
        }

        private void InputManager_PerformWork(object sender, WorkerThreadArgs e)
        {
            var hotkeys = HotkeyManagerViewModel.Hotkeys.AsEnumerable();
            if (!hotkeys.Any())
                return;

            bool haveWin32 = _input is not null;

            // Update Win32 state if backend is present.
            if (haveWin32)
            {
                try
                {
                    _input.UpdateKeys();
                }
                catch (Exception ex)
                {
                    // If Win32 backend dies mid-run, we just fall back to DeviceAimbot.
                    DebugLogger.LogDebug($"[InputManager] VmmInputManager.UpdateKeys failed: {ex}");
                    // We keep _input non-null but effectively ignore it after this tick.
                    haveWin32 = false;
                }
            }

            foreach (var kvp in hotkeys)
            {
                var vk = kvp.Key;
                var action = kvp.Value;

                bool isDownWin32 = false;
                if (haveWin32)
                {
                    try
                    {
                        isDownWin32 = _input.IsKeyDown(vk);
                    }
                    catch
                    {
                        // treat as not pressed if backend misbehaves
                        isDownWin32 = false;
                    }
                }

                bool isDownDeviceAimbot = IsDeviceAimbotKeyDown(vk);
                bool isDownMouseFallback = IsMouseVirtualKey(vk) && IsMouseAsyncDown(vk);

                // FINAL state: key is considered down if EITHER backend reports it.
                bool isKeyDown = isDownWin32 || isDownDeviceAimbot || isDownMouseFallback;

                action.Execute(isKeyDown);
            }
        }

        /// <summary>
        /// Maps some Win32 virtual keys (mouse buttons) to DeviceAimbot buttons
        /// and returns whether that logical key is down according to DeviceAimbot.
        /// 
        /// This gives us:
        /// - LBUTTON   �� DeviceAimbotMouseButton.Left
        /// - RBUTTON   �� DeviceAimbotMouseButton.Right
        /// - MBUTTON   �� DeviceAimbotMouseButton.Middle
        /// - XBUTTON1  �� DeviceAimbotMouseButton.mouse4
        /// - XBUTTON2  �� DeviceAimbotMouseButton.mouse5
        /// 
        /// So users can bind hotkeys to those keys in the hotkey UI ?????
        /// they will work even when VmmInputManager is unavailable, as long
        /// as the DeviceAimbot device is connected.
        /// </summary>
        private static bool IsDeviceAimbotKeyDown(Win32VirtualKey vk)
        {
            if (!Device.connected || Device.bState == null)
                return false;

            DeviceAimbotMouseButton button;

            switch (vk)
            {
                case Win32VirtualKey.LBUTTON:
                    button = DeviceAimbotMouseButton.Left;
                    break;

                case Win32VirtualKey.RBUTTON:
                    button = DeviceAimbotMouseButton.Right;
                    break;

                case Win32VirtualKey.MBUTTON:
                    button = DeviceAimbotMouseButton.Middle;
                    break;

                case Win32VirtualKey.XBUTTON1:
                    button = DeviceAimbotMouseButton.mouse4;
                    break;

                case Win32VirtualKey.XBUTTON2:
                    button = DeviceAimbotMouseButton.mouse5;
                    break;

                default:
                    // any non-mouse key is not handled by DeviceAimbot
                    return false;
            }

            return Device.button_pressed(button);
        }

        private static bool IsMouseVirtualKey(Win32VirtualKey vk) =>
            vk is Win32VirtualKey.LBUTTON
            or Win32VirtualKey.RBUTTON
            or Win32VirtualKey.MBUTTON
            or Win32VirtualKey.XBUTTON1
            or Win32VirtualKey.XBUTTON2;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static bool IsMouseAsyncDown(Win32VirtualKey vk)
        {
            var state = GetAsyncKeyState((int)vk);
            return (state & 0x8000) != 0;
        }
    }
}