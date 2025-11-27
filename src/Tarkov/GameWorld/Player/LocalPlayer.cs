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

using System.Diagnostics;
using Collections.Pooled;
using LoneEftDmaRadar.Tarkov.Unity;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.UI.Misc;
using VmmSharpEx.Scatter;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Player
{
    public sealed class LocalPlayer : ClientPlayer
    {
        /// <summary>
        /// Firearm Manager for tracking weapon/ammo/ballistics.
        /// </summary>
        public FirearmManager FirearmManager { get; private set; }

        private UnityTransform _lookRaycastTransform;

        /// <summary>
        /// Local Player's "look" position for accurate POV in aimview.
        /// Falls back to root position if unavailable.
        /// </summary>
        public Vector3 LookPosition => _lookRaycastTransform?.Position ?? this.Position;

        /// <summary>
        /// Public accessor for Hands Controller (used by FirearmManager).
        /// </summary>
        public new ulong HandsController
        {
            get => base.HandsController;
            private set => base.HandsController = value;
        }

        /// <summary>
        /// Spawn Point.
        /// </summary>
        public string EntryPoint { get; }

        /// <summary>
        /// Profile ID (if Player Scav). Used for Exfils.
        /// </summary>
        public string ProfileId { get; }

        /// <summary>
        /// Spawn Point.
        /// </summary>
        public override string Name
        {
            get => "localPlayer";
            set { }
        }
        /// <summary>
        /// Player is Human-Controlled.
        /// </summary>
        public override bool IsHuman { get; }

        public LocalPlayer(ulong playerBase) : base(playerBase)
        {
            string classType = ObjectClass.ReadName(this);
            if (!(classType == "LocalPlayer" || classType == "ClientPlayer"))
                throw new ArgumentOutOfRangeException(nameof(classType));
            IsHuman = true;
            FirearmManager = new FirearmManager(this);

            if (IsPmc)
            {
                var entryPtr = Memory.ReadPtr(Info + Offsets.PlayerInfo.EntryPoint);
                EntryPoint = Memory.ReadUnicodeString(entryPtr);
            }
            else if (IsScav)
            {
                var profileIdPtr = Memory.ReadPtr(Profile + Offsets.Profile.Id);
                ProfileId = Memory.ReadUnicodeString(profileIdPtr);
            }
        }

        /// <summary>
        /// Update FirearmManager (call this before using weapon data).
        /// </summary>
        public void UpdateFirearmManager()
        {
            try
            {
                HandsController = Memory.ReadPtr(Base + Offsets.Player._handsController, false);
                FirearmManager?.Update();
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[LocalPlayer] FirearmManager update failed: {ex}");
            }
        }

        /// <summary>
        /// Checks if LocalPlayer is Aiming (ADS).
        /// </summary>
        public bool CheckIfADS()
        {
            try
            {
                return Memory.ReadValue<bool>(PWA + Offsets.ProceduralWeaponAnimation._isAiming, false);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Initializes and updates the look raycast transform using memory scatter reads.
        /// Called when aimview is enabled.
        /// </summary>
        public override void OnRealtimeLoop(VmmScatter scatter)
        {
            base.OnRealtimeLoop(scatter);

            try
            {
                var transformPtr = Memory.ReadPtr(Base + Offsets.Player._playerLookRaycastTransform, false);

                if (transformPtr != 0x0)
                    _lookRaycastTransform = new UnityTransform(transformPtr);
                else
                    _lookRaycastTransform = null;
            }
            catch
            {
                _lookRaycastTransform = null;
            }
        }

        public override void OnValidateTransforms()
        {
            base.OnValidateTransforms();

            if (_lookRaycastTransform is null)
                return;

            try
            {
                var hierarchy = Memory.ReadPtr(_lookRaycastTransform.TransformInternal + UnitySDK.UnityOffsets.TransformAccess_HierarchyOffset);
                var vertexAddr = Memory.ReadPtr(hierarchy + UnitySDK.UnityOffsets.Hierarchy_VerticesOffset);
                if (vertexAddr == 0x0)
                {
                    DebugLogger.LogWarning("LookRaycast transform changed, updating cached transform...");
                    var transformPtr = Memory.ReadPtr(Base + Offsets.Player._playerLookRaycastTransform, false);
                    if (transformPtr != 0x0)
                        _lookRaycastTransform = new UnityTransform(transformPtr);
                    else
                        _lookRaycastTransform = null;
                }
            }
            catch
            {
                _lookRaycastTransform = null;
            }
        }
    }
}
