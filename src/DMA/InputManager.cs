/*
 * Lone EFT DMA Radar
 * Brought to you by Lone (Lone DMA)
 *
 * MIT License
 *
 * InputManager - Based on Metick's DMALibrary implementation
 * Direct kernel memory access to gafAsyncKeyState for improved Win10/11 compatibility
 */

using System;
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
    /// Central input poller for hotkeys using direct kernel memory access to gafAsyncKeyState.
    /// Supports Windows 10/11 with automatic version detection and signature scanning.
    /// </summary>
    public sealed class InputManager : IDisposable
    {
        private readonly Vmm _vmm;
        private readonly WorkerThread _thread;

        // Kernel state tracking
        private ulong _gafAsyncKeyStateExport = 0;
        private uint _winLogonPid = 0;
        private readonly byte[] _stateBitmap = new byte[64];
        private readonly byte[] _previousStateBitmap = new byte[256 / 8];
        private DateTime _lastUpdate = DateTime.MinValue;

        private bool _initialized = false;

        /// <summary>
        /// True if the kernel input backend is available and initialized.
        /// </summary>
        public bool IsBackendAvailable => _initialized && _gafAsyncKeyStateExport > 0x7FFFFFFFFFFF;

        public InputManager(Vmm vmm)
        {
            _vmm = vmm ?? throw new ArgumentNullException(nameof(vmm));

            // Initialize keyboard state tracking
            _initialized = InitKeyboard();

            if (_initialized)
            {
                DebugLogger.LogDebug($"[InputManager] Kernel input initialized successfully (gafAsyncKeyState: 0x{_gafAsyncKeyStateExport:X})");
            }
            else
            {
                DebugLogger.LogDebug("[InputManager] Failed to initialize kernel input. Falling back to GetAsyncKeyState.");
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

        /// <summary>
        /// Initializes keyboard tracking by locating gafAsyncKeyState in kernel memory.
        /// Supports both modern Windows (22000+) and legacy versions.
        /// </summary>
        private bool InitKeyboard()
        {
            try
            {
                // Get Windows version
                int winVer = GetWindowsBuildNumber();
                if (winVer == 0)
                {
                    DebugLogger.LogDebug("[InputManager] Failed to get Windows build number");
                    return false;
                }

                DebugLogger.LogDebug($"[InputManager] Windows build: {winVer}");

                // Get winlogon.exe PID
                if (!_vmm.PidGetFromName("winlogon.exe", out uint winlogonPid))
                {
                    DebugLogger.LogDebug("[InputManager] Failed to find winlogon.exe");
                    return false;
                }
                _winLogonPid = winlogonPid;

                // Modern Windows (11+) - use signature scanning
                if (winVer > 22000)
                {
                    return InitKeyboardModern();
                }
                // Legacy Windows (10) - use EAT and PDB
                else
                {
                    return InitKeyboardLegacy();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[InputManager] InitKeyboard failed: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Modern Windows (22000+) initialization using signature scanning.
        /// </summary>
        private bool InitKeyboardModern()
        {
            // Try to find csrss.exe - on Windows it typically runs in Session 0 and Session 1
            // We'll attempt to scan both
            var csrssPids = new List<uint>();

            // Try to find csrss.exe by iterating through possible session IDs
            for (uint sessionId = 0; sessionId < 4; sessionId++)
            {
                if (_vmm.PidGetFromName("csrss.exe", out uint pid))
                {
                    if (!csrssPids.Contains(pid))
                        csrssPids.Add(pid);
                }
            }

            // Fallback: manually try common PID ranges for csrss
            if (csrssPids.Count == 0)
            {
                DebugLogger.LogDebug("[InputManager] Could not find csrss.exe via PidGetFromName, attempting fallback");
                // Try a few PIDs in typical csrss range (usually <1000)
                for (uint testPid = 100; testPid < 1000; testPid += 4)
                {
                    if (_vmm.Map_GetModuleFromName(testPid, "win32k.sys", out _) ||
                        _vmm.Map_GetModuleFromName(testPid, "win32ksgd.sys", out _))
                    {
                        csrssPids.Add(testPid);
                        if (csrssPids.Count >= 3) break; // Usually 2-3 csrss processes
                    }
                }
            }

            if (csrssPids.Count == 0)
            {
                DebugLogger.LogDebug("[InputManager] Failed to find csrss.exe");
                return false;
            }

            DebugLogger.LogDebug($"[InputManager] Found {csrssPids.Count} csrss.exe instance(s)");

            foreach (var pid in csrssPids)
            {
                try
                {
                    // Try win32ksgd.sys first, then win32k.sys
                    ulong win32kBase = 0;
                    uint win32kSize = 0;

                    if (_vmm.Map_GetModuleFromName(pid, "win32ksgd.sys", out var win32ksgdModule))
                    {
                        win32kBase = win32ksgdModule.vaBase;
                        win32kSize = win32ksgdModule.cbImageSize;
                    }
                    else if (_vmm.Map_GetModuleFromName(pid, "win32k.sys", out var win32kModule))
                    {
                        win32kBase = win32kModule.vaBase;
                        win32kSize = win32kModule.cbImageSize;
                    }
                    else
                    {
                        continue;
                    }

                    // Find g_SessionGlobalSlots signature
                    var gSessionPtr = FindSignature(pid, win32kBase, win32kSize,
                        new byte[] { 0x48, 0x8B, 0x05, 0x00, 0x00, 0x00, 0x00, 0x48, 0x8B, 0x04, 0xC8 },
                        "xxx????xxxx");

                    if (gSessionPtr == 0)
                    {
                        // Try alternate signature
                        gSessionPtr = FindSignature(pid, win32kBase, win32kSize,
                            new byte[] { 0x48, 0x8B, 0x05, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xC9 },
                            "xxx????xx");

                        if (gSessionPtr == 0)
                            continue;
                    }

                    // Read relative offset
                    Span<byte> relativeBytes = stackalloc byte[4];
                    if (!_vmm.MemReadSpan(pid, gSessionPtr + 3, relativeBytes))
                        continue;
                    int relative = BitConverter.ToInt32(relativeBytes);
                    ulong gSessionGlobalSlots = gSessionPtr + 7 + (ulong)(uint)relative;

                    // Find user session state
                    ulong userSessionState = 0;
                    Span<byte> slotsPtrBytes = stackalloc byte[8];
                    if (!_vmm.MemReadSpan(pid, gSessionGlobalSlots, slotsPtrBytes))
                        continue;
                    ulong slotsPtr = BitConverter.ToUInt64(slotsPtrBytes);

                    for (int i = 0; i < 4; i++)
                    {
                        Span<byte> slotEntryBytes = stackalloc byte[8];
                        if (!_vmm.MemReadSpan(pid, slotsPtr + (ulong)(8 * i), slotEntryBytes))
                            continue;
                        ulong slotEntry = BitConverter.ToUInt64(slotEntryBytes);

                        Span<byte> userSessionBytes = stackalloc byte[8];
                        if (!_vmm.MemReadSpan(pid, slotEntry, userSessionBytes))
                            continue;
                        userSessionState = BitConverter.ToUInt64(userSessionBytes);

                        if (userSessionState > 0x7FFFFFFFFFFF)
                            break;
                    }

                    // Get win32kbase.sys module
                    if (!_vmm.Map_GetModuleFromName(pid, "win32kbase.sys", out var win32kbaseModule))
                        continue;

                    ulong win32kbaseBase = win32kbaseModule.vaBase;
                    uint win32kbaseSize = win32kbaseModule.cbImageSize;

                    // Find gafAsyncKeyState offset signature (from PostUpdateKeyStateEvent)
                    var ptr = FindSignature(pid, win32kbaseBase, win32kbaseSize,
                        new byte[] { 0x48, 0x8D, 0x90, 0x00, 0x00, 0x00, 0x00, 0xE8, 0x00, 0x00, 0x00, 0x00, 0x0F, 0x57, 0xC0 },
                        "xxx????x????xxx");

                    if (ptr == 0)
                    {
                        DebugLogger.LogDebug("[InputManager] Failed to find gafAsyncKeyState offset signature");
                        continue;
                    }

                    Span<byte> sessionOffsetBytes = stackalloc byte[4];
                    if (!_vmm.MemReadSpan(pid, ptr + 3, sessionOffsetBytes))
                        continue;
                    uint sessionOffset = BitConverter.ToUInt32(sessionOffsetBytes);
                    _gafAsyncKeyStateExport = userSessionState + sessionOffset;

                    if (_gafAsyncKeyStateExport > 0x7FFFFFFFFFFF)
                    {
                        DebugLogger.LogDebug($"[InputManager] Found gafAsyncKeyState at: 0x{_gafAsyncKeyStateExport:X}");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogDebug($"[InputManager] Error processing csrss.exe PID {pid}: {ex.Message}");
                    continue;
                }
            }

            return false;
        }

        /// <summary>
        /// Legacy Windows initialization using EAT and PDB symbols.
        /// </summary>
        private bool InitKeyboardLegacy()
        {
            try
            {
                // Access kernel memory via winlogon.exe
                uint kernelPid = _winLogonPid | 0x80000000; // VMMDLL_PID_PROCESS_WITH_KERNELMEMORY

                // Try to get module for win32kbase.sys
                if (!_vmm.Map_GetModuleFromName(kernelPid, "win32kbase.sys", out var win32kbaseModule))
                {
                    DebugLogger.LogDebug("[InputManager] Failed to get win32kbase.sys module");
                    return false;
                }

                // Try to load and use PDB to find gafAsyncKeyState
                try
                {
                    ulong symbolAddr = 0;
                    string szModuleName = "win32kbase";
                    if (_vmm.PdbSymbolAddress(szModuleName, "gafAsyncKeyState", out symbolAddr))
                    {
                        _gafAsyncKeyStateExport = symbolAddr;
                        DebugLogger.LogDebug($"[InputManager] Found gafAsyncKeyState via PDB at: 0x{_gafAsyncKeyStateExport:X}");
                        return _gafAsyncKeyStateExport > 0x7FFFFFFFFFFF;
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogDebug($"[InputManager] PDB symbol lookup failed: {ex.Message}");
                }

                // Fallback: Try signature scanning on Windows 10
                DebugLogger.LogDebug("[InputManager] Trying signature scanning fallback for Windows 10");
                return InitKeyboardModern();
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[InputManager] InitKeyboardLegacy failed: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Updates the keyboard state by reading the kernel memory bitmap.
        /// </summary>
        private void UpdateKeys()
        {
            if (!_initialized || _gafAsyncKeyStateExport < 0x7FFFFFFFFFFF)
                return;

            try
            {
                // Save previous state
                Buffer.BlockCopy(_stateBitmap, 0, _previousStateBitmap, 0, 64);

                // Read current state from kernel memory
                uint kernelPid = _winLogonPid | 0x80000000; // VMMDLL_PID_PROCESS_WITH_KERNELMEMORY
                _vmm.MemReadSpan(kernelPid, _gafAsyncKeyStateExport, _stateBitmap.AsSpan());

                _lastUpdate = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[InputManager] UpdateKeys failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if a virtual key is currently pressed.
        /// </summary>
        private bool IsKeyDown(Win32VirtualKey vk)
        {
            if (!_initialized || _gafAsyncKeyStateExport < 0x7FFFFFFFFFFF)
                return false;

            // Rate limit updates to every 100ms
            if ((DateTime.UtcNow - _lastUpdate).TotalMilliseconds > 100)
            {
                UpdateKeys();
            }

            int keyCode = (int)vk;
            int byteIndex = (keyCode * 2) / 8;
            int bitIndex = (keyCode % 4) * 2;

            if (byteIndex >= 0 && byteIndex < _stateBitmap.Length)
            {
                return (_stateBitmap[byteIndex] & (1 << bitIndex)) != 0;
            }

            return false;
        }

        private void InputManager_PerformWork(object sender, WorkerThreadArgs e)
        {
            var hotkeys = HotkeyManagerViewModel.Hotkeys.AsEnumerable();
            if (!hotkeys.Any())
                return;

            foreach (var kvp in hotkeys)
            {
                var vk = kvp.Key;
                var action = kvp.Value;

                bool isDownKernel = false;

                // Try kernel memory backend first
                if (_initialized)
                {
                    try
                    {
                        isDownKernel = IsKeyDown(vk);
                    }
                    catch
                    {
                        isDownKernel = false;
                    }
                }

                // Fallback to GetAsyncKeyState for mouse buttons
                bool isDownFallback = IsMouseVirtualKey(vk) && IsMouseAsyncDown(vk);

                // Additional fallback to DeviceAimbot if available
                bool isDownDeviceAimbot = IsDeviceAimbotKeyDown(vk);

                // Key is down if ANY backend reports it
                bool isKeyDown = isDownKernel || isDownFallback || isDownDeviceAimbot;

                action.Execute(isKeyDown);
            }
        }

        #region Helper Methods

        private int GetWindowsBuildNumber()
        {
            try
            {
                var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (key != null)
                {
                    var buildStr = key.GetValue("CurrentBuild") as string;
                    if (int.TryParse(buildStr, out int build))
                        return build;
                }
            }
            catch { }
            return 0;
        }

        private ulong FindSignature(uint pid, ulong startAddress, uint size, byte[] pattern, string mask)
        {
            try
            {
                const int chunkSize = 0x10000; // 64KB chunks
                byte[] buffer = new byte[chunkSize];

                for (ulong offset = 0; offset < size; offset += chunkSize - (ulong)pattern.Length)
                {
                    ulong currentAddr = startAddress + offset;
                    int readSize = (int)Math.Min(chunkSize, size - offset);

                    if (!_vmm.MemReadSpan(pid, currentAddr, buffer.AsSpan(0, readSize)))
                        continue;

                    for (int i = 0; i < readSize - pattern.Length; i++)
                    {
                        bool found = true;
                        for (int j = 0; j < pattern.Length; j++)
                        {
                            if (mask[j] == 'x' && buffer[i + j] != pattern[j])
                            {
                                found = false;
                                break;
                            }
                        }

                        if (found)
                        {
                            return currentAddr + (ulong)i;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[InputManager] FindSignature failed: {ex.Message}");
            }

            return 0;
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

        /// <summary>
        /// Maps some Win32 virtual keys (mouse buttons) to DeviceAimbot buttons
        /// and returns whether that logical key is down according to DeviceAimbot.
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
                    return false;
            }

            return Device.button_pressed(button);
        }

        #endregion

        public void Dispose()
        {
            _thread?.Dispose();
        }
    }
}
