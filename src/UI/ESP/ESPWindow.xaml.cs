using LoneEftDmaRadar.Misc;
using LoneEftDmaRadar.Tarkov.GameWorld;
using LoneEftDmaRadar.Tarkov.GameWorld.Exits;
using LoneEftDmaRadar.Tarkov.GameWorld.Explosives;
using LoneEftDmaRadar.Tarkov.GameWorld.Loot;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.GameWorld.Player.Helpers;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using System.Drawing;
using System.Linq;
using System.Collections.Concurrent;
using System.Windows.Input;
using System.Windows.Threading;
using LoneEftDmaRadar.UI.Skia;
using LoneEftDmaRadar.UI.Misc;
using LoneEftDmaRadar.DMA;
using SharpDX;
using SharpDX.Mathematics.Interop;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Forms.Integration;
using WinForms = System.Windows.Forms;
using SkiaSharp;
using DxColor = SharpDX.Mathematics.Interop.RawColorBGRA;
using CameraManagerNew = LoneEftDmaRadar.Tarkov.GameWorld.Camera.CameraManager;

namespace LoneEftDmaRadar.UI.ESP
{
    public partial class ESPWindow : Window
    {
        #region Fields/Properties

        public static bool ShowESP { get; set; } = true;
        private bool _dxInitFailed;

        private readonly System.Diagnostics.Stopwatch _fpsSw = new();
        private int _fpsCounter;
        private int _fps;
        private long _lastFrameTicks;
        private Timer _highFrequencyTimer;
        private int _renderPending;

        // Render surface
        private Dx9OverlayControl _dxOverlay;
        private WindowsFormsHost _dxHost;
        private bool _isClosing;

        // Cached Fonts/Paints
        private readonly SKPaint _skeletonPaint;
        private readonly SKPaint _boxPaint;
        private readonly SKPaint _crosshairPaint;
        private static readonly SKColor[] _espGroupPalette = new SKColor[]
        {
            SKColors.MediumSlateBlue,
            SKColors.MediumSpringGreen,
            SKColors.CadetBlue,
            SKColors.MediumOrchid,
            SKColors.PaleVioletRed,
            SKColors.SteelBlue,
            SKColors.DarkSeaGreen,
            SKColors.Chocolate
        };
        private static readonly ConcurrentDictionary<int, SKPaint> _espGroupPaints = new();

        private bool _isFullscreen;

        /// <summary>
        /// LocalPlayer (who is running Radar) 'Player' object.
        /// </summary>
        private static LocalPlayer LocalPlayer => Memory.LocalPlayer;

        /// <summary>
        /// All Players in Local Game World (including dead/exfil'd) 'Player' collection.
        /// </summary>
        private static IReadOnlyCollection<AbstractPlayer> AllPlayers => Memory.Players;

        private static IReadOnlyCollection<IExitPoint> Exits => Memory.Exits;

        private static IReadOnlyCollection<IExplosiveItem> Explosives => Memory.Explosives;

        private static bool InRaid => Memory.InRaid;

        // Bone Connections for Skeleton
        private static readonly (Bones From, Bones To)[] _boneConnections = new[]
        {
            (Bones.HumanHead, Bones.HumanNeck),
            (Bones.HumanNeck, Bones.HumanSpine3),
            (Bones.HumanSpine3, Bones.HumanSpine2),
            (Bones.HumanSpine2, Bones.HumanSpine1),
            (Bones.HumanSpine1, Bones.HumanPelvis),
            
            // Left Arm
            (Bones.HumanNeck, Bones.HumanLUpperarm), // Shoulder approx
            (Bones.HumanLUpperarm, Bones.HumanLForearm1),
            (Bones.HumanLForearm1, Bones.HumanLForearm2),
            (Bones.HumanLForearm2, Bones.HumanLPalm),
            
            // Right Arm
            (Bones.HumanNeck, Bones.HumanRUpperarm), // Shoulder approx
            (Bones.HumanRUpperarm, Bones.HumanRForearm1),
            (Bones.HumanRForearm1, Bones.HumanRForearm2),
            (Bones.HumanRForearm2, Bones.HumanRPalm),
            
            // Left Leg
            (Bones.HumanPelvis, Bones.HumanLThigh1),
            (Bones.HumanLThigh1, Bones.HumanLThigh2),
            (Bones.HumanLThigh2, Bones.HumanLCalf),
            (Bones.HumanLCalf, Bones.HumanLFoot),
            
            // Right Leg
            (Bones.HumanPelvis, Bones.HumanRThigh1),
            (Bones.HumanRThigh1, Bones.HumanRThigh2),
            (Bones.HumanRThigh2, Bones.HumanRCalf),
            (Bones.HumanRCalf, Bones.HumanRFoot),
        };

        #endregion

        public ESPWindow()
        {
            InitializeComponent();
            InitializeRenderSurface();
            
            // Initial sizes
            this.Width = 400;
            this.Height = 300;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // Cache paints/fonts
            _skeletonPaint = new SKPaint
            {
                Color = SKColors.White,
                StrokeWidth = 1.5f,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke
            };

            _boxPaint = new SKPaint
            {
                Color = SKColors.White,
                StrokeWidth = 1.0f,
                IsAntialias = false, // Crisper boxes
                Style = SKPaintStyle.Stroke
            };

            _crosshairPaint = new SKPaint
            {
                Color = SKColors.White,
                StrokeWidth = 1.5f,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke
            };

            _fpsSw.Start();
            _lastFrameTicks = System.Diagnostics.Stopwatch.GetTimestamp();

            _highFrequencyTimer = new System.Threading.Timer(
                callback: HighFrequencyRenderCallback,
                state: null,
                dueTime: 0,
                period: 1); // push frames as fast as dispatcher allows
        }

        private void InitializeRenderSurface()
        {
            RenderRoot.Children.Clear();

            _dxOverlay = new Dx9OverlayControl
            {
                Dock = WinForms.DockStyle.Fill
            };

            ApplyDxFontConfig();
            _dxOverlay.RenderFrame = RenderSurface;
            _dxOverlay.DeviceInitFailed += Overlay_DeviceInitFailed;
            _dxOverlay.MouseDown += GlControl_MouseDown;
            _dxOverlay.DoubleClick += GlControl_DoubleClick;
            _dxOverlay.KeyDown += GlControl_KeyDown;

            _dxHost = new WindowsFormsHost
            {
                Child = _dxOverlay
            };

            RenderRoot.Children.Add(_dxHost);
        }

        private void HighFrequencyRenderCallback(object state)
        {
            try
            {
                if (_isClosing)
                    return;

                int maxFPS = App.Config.UI.EspMaxFPS;
                long currentTicks = System.Diagnostics.Stopwatch.GetTimestamp();

                if (maxFPS > 0)
                {
                    double elapsedMs = (currentTicks - _lastFrameTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    double targetMs = 1000.0 / maxFPS;
                    if (elapsedMs < targetMs)
                        return;
                }

                _lastFrameTicks = currentTicks;

                // Must dispatch to UI thread for rendering; avoid piling up work
                if (System.Threading.Interlocked.CompareExchange(ref _renderPending, 1, 0) == 0)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        RefreshESP();
                    }), System.Windows.Threading.DispatcherPriority.Send);
                }
            }
            catch { /* Ignore errors during shutdown */ }
        }

        #region Rendering Methods

        /// <summary>
        /// Record the Rendering FPS.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void SetFPS()
        {
            if (_fpsSw.ElapsedMilliseconds >= 1000)
            {
                _fps = System.Threading.Interlocked.Exchange(ref _fpsCounter, 0);
                _fpsSw.Restart();
            }
            else
            {
                _fpsCounter++;
            }
        }

        private bool _lastInRaidState = false;

        /// <summary>
        /// Main ESP Render Event.
        /// </summary>
        private void RenderSurface(Dx9RenderContext ctx)
        {
            if (_dxInitFailed)
                return;

            float screenWidth = ctx.Width;
            float screenHeight = ctx.Height;

            SetFPS();

            // Clear with black background (transparent for fuser)
            ctx.Clear(new DxColor(0, 0, 0, 255));

            try
            {
                // Detect raid state changes and reset camera/state when leaving raid
                if (_lastInRaidState && !InRaid)
                {
                    CameraManagerNew.Reset();
                    DebugLogger.LogInfo("ESP: Detected raid end - reset all state");
                }
                _lastInRaidState = InRaid;

                if (!InRaid)
                    return;

                var localPlayer = LocalPlayer;
                var allPlayers = AllPlayers;
                
                if (localPlayer is not null && allPlayers is not null)
                {
                    if (!ShowESP)
                    {
                        DrawNotShown(ctx, screenWidth, screenHeight);
                    }
                    else
                    {
                        ApplyResolutionOverrideIfNeeded();

                        // Render Loot (background layer)
                        if (App.Config.Loot.Enabled && App.Config.UI.EspLoot)
                        {
                            DrawLoot(ctx, screenWidth, screenHeight, localPlayer);
                        }

                        if (App.Config.Loot.Enabled && App.Config.UI.EspContainers)
                        {
                            DrawStaticContainers(ctx, screenWidth, screenHeight, localPlayer);
                        }

                        // Render Exfils
                        if (Exits is not null && App.Config.UI.EspExfils)
                        {
                            foreach (var exit in Exits)
                            {
                                if (exit is Exfil exfil && (exfil.Status == Exfil.EStatus.Open || exfil.Status == Exfil.EStatus.Pending))
                                {
                                     if (WorldToScreen2(exfil.Position, out var screen, screenWidth, screenHeight))
                                     {
                                         var dotColor = exfil.Status == Exfil.EStatus.Pending
                                             ? ToColor(SKPaints.PaintExfilPending)
                                             : ToColor(SKPaints.PaintExfilOpen);
                                         var textColor = GetExfilColorForRender();

                                         ctx.DrawCircle(ToRaw(screen), 4f, dotColor, true);
                                         ctx.DrawText(exfil.Name, screen.X + 6, screen.Y + 4, textColor, DxTextSize.Medium);
                                     }
                                }
                            }
                        }

                        if (Explosives is not null && App.Config.UI.EspTripwires)
                        {
                            DrawTripwires(ctx, screenWidth, screenHeight);
                        }

                        if (Explosives is not null && App.Config.UI.EspGrenades)
                        {
                            DrawGrenades(ctx, screenWidth, screenHeight);
                        }

                        // Render players
                        foreach (var player in allPlayers)
                        {
                            DrawPlayerESP(ctx, player, localPlayer, screenWidth, screenHeight);
                        }

                        DrawDeviceAimbotTargetLine(ctx, screenWidth, screenHeight);

                        if (App.Config.Device.Enabled)
                        {
                            DrawDeviceAimbotFovCircle(ctx, screenWidth, screenHeight);
                        }

                        if (App.Config.UI.EspCrosshair)
                        {
                            DrawCrosshair(ctx, screenWidth, screenHeight);
                        }

                        DrawDeviceAimbotDebugOverlay(ctx, screenWidth, screenHeight);
                        DrawFPS(ctx, screenWidth, screenHeight);
                    }
                }
            }
            catch (System.Exception ex)
            {
                DebugLogger.LogDebug($"ESP RENDER ERROR: {ex}");
            }
        }

        private void DrawLoot(Dx9RenderContext ctx, float screenWidth, float screenHeight, LocalPlayer localPlayer)
        {
            var lootItems = Memory.Game?.Loot?.FilteredLoot;
            if (lootItems is null) return;

            var camPos = localPlayer?.Position ?? Vector3.Zero;

            foreach (var item in lootItems)
            {
                // Filter based on ESP settings
                bool isCorpse = item is LootCorpse;
                bool isQuest = item.IsQuestItem;
                if (isQuest && !App.Config.UI.EspQuestLoot)
                    continue;
                if (isCorpse && !App.Config.UI.EspCorpses)
                    continue;

                bool isContainer = item is StaticLootContainer;
                if (isContainer)
                    continue;

                bool isFood = item.IsFood;
                bool isMeds = item.IsMeds;
                bool isBackpack = item.IsBackpack;

                // Skip if it's one of these types and the setting is disabled
                if (isFood && !App.Config.UI.EspFood)
                    continue;
                if (isMeds && !App.Config.UI.EspMeds)
                    continue;
                if (isBackpack && !App.Config.UI.EspBackpacks)
                    continue;

                // Check distance to loot
                float distance = Vector3.Distance(camPos, item.Position);
                if (App.Config.UI.EspLootMaxDistance > 0 && distance > App.Config.UI.EspLootMaxDistance)
                    continue;

                if (WorldToScreen2(item.Position, out var screen, screenWidth, screenHeight))
                {
                     // Calculate cone filter based on screen position
                     bool coneEnabled = App.Config.UI.EspLootConeEnabled && App.Config.UI.EspLootConeAngle > 0f;
                     bool inCone = true;

                     if (coneEnabled)
                     {
                         // Calculate angle from screen center
                         float centerX = screenWidth / 2f;
                         float centerY = screenHeight / 2f;
                         float dx = screen.X - centerX;
                         float dy = screen.Y - centerY;

                         // Calculate angular distance from center (in screen space)
                         // Using FOV to convert screen distance to angle
                         float fov = App.Config.UI.FOV;
                         float screenAngleX = MathF.Abs(dx / centerX) * (fov / 2f);
                         float screenAngleY = MathF.Abs(dy / centerY) * (fov / 2f);
                         float screenAngle = MathF.Sqrt(screenAngleX * screenAngleX + screenAngleY * screenAngleY);

                         inCone = screenAngle <= App.Config.UI.EspLootConeAngle;
                     }

                     // Determine colors based on item type (default to user-selected loot color).
                     DxColor circleColor = GetLootColorForRender();
                     DxColor textColor = circleColor;

                     if (isQuest)
                     {
                         circleColor = ToColor(SKPaints.PaintQuestItem);
                         textColor = circleColor;
                     }
                     else if (item.Important)
                     {
                         circleColor = ToColor(SKPaints.PaintFilteredLoot);
                         textColor = circleColor;
                     }
                     else if (item.IsValuableLoot)
                     {
                         circleColor = ToColor(SKPaints.PaintImportantLoot);
                         textColor = circleColor;
                     }
                     else if (isBackpack)
                     {
                         circleColor = ToColor(SKPaints.PaintBackpacks);
                         textColor = circleColor;
                     }
                     else if (isMeds)
                     {
                         circleColor = ToColor(SKPaints.PaintMeds);
                         textColor = circleColor;
                     }
                     else if (isFood)
                     {
                         circleColor = ToColor(SKPaints.PaintFood);
                         textColor = circleColor;
                     }
                     else if (isCorpse)
                     {
                         circleColor = ToColor(SKPaints.PaintCorpse);
                         textColor = circleColor;
                     }
                     else if (item is LootAirdrop)
                     {
                         circleColor = ToColor(SKPaints.PaintAirdrop);
                         textColor = circleColor;
                     }

                     ctx.DrawCircle(ToRaw(screen), 2f, circleColor, true);

                     if (item.Important || inCone)
                     {
                         string text;
                         if (isCorpse && item is LootCorpse corpse)
                         {
                             var corpseName = corpse.Player?.Name;
                             text = string.IsNullOrWhiteSpace(corpseName) ? corpse.Name : corpseName;
                         }
                         else if (item is LootAirdrop)
                         {
                             text = "Airdrop";
                         }
                         else
                         {
                             var shortName = string.IsNullOrWhiteSpace(item.ShortName) ? item.Name : item.ShortName;
                             text = shortName;
                             if (App.Config.UI.EspLootPrice)
                             {
                                 text = item.Important
                                     ? shortName
                                     : $"{shortName} ({LoneEftDmaRadar.Misc.Utilities.FormatNumberKM(item.Price)})";
                             }
                         }
                         ctx.DrawText(text, screen.X + 4, screen.Y + 4, textColor, DxTextSize.Small);
                    }
                }
            }
        }

        private void DrawStaticContainers(Dx9RenderContext ctx, float screenWidth, float screenHeight, LocalPlayer localPlayer)
        {
            if (!App.Config.Containers.Enabled)
                return;

            var containers = Memory.Game?.Loot?.StaticContainers;
            if (containers is null)
                return;

            bool selectAll = App.Config.Containers.SelectAll;
            var selected = App.Config.Containers.Selected;
            float maxDistance = App.Config.Containers.EspDrawDistance;
            var color = GetContainerColorForRender();

            foreach (var container in containers)
            {
                var id = container.ID ?? "UNKNOWN";
                if (!(selectAll || selected.ContainsKey(id)))
                    continue;

                float distance = Vector3.Distance(localPlayer.Position, container.Position);
                if (maxDistance > 0 && distance > maxDistance)
                    continue;

                if (!WorldToScreen2(container.Position, out var screen, screenWidth, screenHeight))
                    continue;

                ctx.DrawCircle(ToRaw(screen), 3f, color, true);
                ctx.DrawText(container.Name ?? "Container", screen.X + 4, screen.Y + 4, color, DxTextSize.Small);
            }
        }

        private void DrawTripwires(Dx9RenderContext ctx, float screenWidth, float screenHeight)
        {
            if (Explosives is null)
                return;

            foreach (var explosive in Explosives)
            {
                if (explosive is null || explosive is not Tripwire tripwire || !tripwire.IsActive)
                    continue;

                try
                {
                    if (tripwire.Position == Vector3.Zero)
                        continue;

                    if (!WorldToScreen2(tripwire.Position, out var screen, screenWidth, screenHeight))
                        continue;

                    var color = GetTripwireColorForRender();
                    ctx.DrawCircle(ToRaw(screen), 5f, color, true);
                    ctx.DrawText("Tripwire", screen.X + 6, screen.Y, color, DxTextSize.Small);
                }
                catch
                {
                    // Silently skip invalid tripwires to prevent ESP from breaking
                    continue;
                }
            }
        }

        private void DrawGrenades(Dx9RenderContext ctx, float screenWidth, float screenHeight)
        {
            if (Explosives is null)
                return;

            foreach (var explosive in Explosives)
            {
                if (explosive is null || explosive is not Grenade grenade)
                    continue;

                try
                {
                    if (grenade.Position == Vector3.Zero)
                        continue;

                    if (!WorldToScreen2(grenade.Position, out var screen, screenWidth, screenHeight))
                        continue;

                    var color = GetGrenadeColorForRender();
                    ctx.DrawCircle(ToRaw(screen), 5f, color, true);
                    ctx.DrawText("Grenade", screen.X + 6, screen.Y, color, DxTextSize.Small);
                }
                catch
                {
                    // Silently skip invalid grenades to prevent ESP from breaking
                    continue;
                }
            }
        }

        /// <summary>
        /// Renders player on ESP
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void DrawPlayerESP(Dx9RenderContext ctx, AbstractPlayer player, LocalPlayer localPlayer, float screenWidth, float screenHeight)
        {
            if (player is null || player == localPlayer || !player.IsAlive || !player.IsActive)
                return;

            // Validate player position is valid (not zero or NaN/Infinity)
            var playerPos = player.Position;
            if (playerPos == Vector3.Zero || 
                float.IsNaN(playerPos.X) || float.IsNaN(playerPos.Y) || float.IsNaN(playerPos.Z) ||
                float.IsInfinity(playerPos.X) || float.IsInfinity(playerPos.Y) || float.IsInfinity(playerPos.Z))
                return;

            // Check if this is AI or player
            bool isAI = player.Type is PlayerType.AIScav or PlayerType.AIRaider or PlayerType.AIBoss or PlayerType.PScav;

            // Optimization: Skip players/AI that are too far before W2S
            float distance = Vector3.Distance(localPlayer.Position, player.Position);
            float maxDistance = isAI ? App.Config.UI.EspAIMaxDistance : App.Config.UI.EspPlayerMaxDistance;

            // If maxDistance is 0, it means unlimited, otherwise check distance
            if (maxDistance > 0 && distance > maxDistance)
                return;

            // Fallback to old MaxDistance if the new settings aren't configured
            if (maxDistance == 0 && distance > App.Config.UI.MaxDistance)
                return;

            // Get Color
            var color = GetPlayerColorForRender(player);
            bool isDeviceAimbotLocked = MemDMA.DeviceAimbot?.LockedTarget == player;
            if (isDeviceAimbotLocked)
            {
                color = ToColor(new SKColor(0, 200, 255, 220));
            }

            bool drawSkeleton = isAI ? App.Config.UI.EspAISkeletons : App.Config.UI.EspPlayerSkeletons;
            bool drawBox = isAI ? App.Config.UI.EspAIBoxes : App.Config.UI.EspPlayerBoxes;
            bool drawName = isAI ? App.Config.UI.EspAINames : App.Config.UI.EspPlayerNames;
            bool drawHealth = isAI ? App.Config.UI.EspAIHealth : App.Config.UI.EspPlayerHealth;
            bool drawDistance = isAI ? App.Config.UI.EspAIDistance : App.Config.UI.EspPlayerDistance;
            bool drawGroupId = isAI ? App.Config.UI.EspAIGroupIds : App.Config.UI.EspGroupIds;
            bool drawLabel = drawName || drawDistance || drawHealth || drawGroupId;

            // Draw Skeleton (only if not in error state to avoid frozen bones)
            if (drawSkeleton && !player.IsError)
            {
                DrawSkeleton(ctx, player, screenWidth, screenHeight, color, _skeletonPaint.StrokeWidth);
            }
            
            RectangleF bbox = default;
            bool hasBox = false;
            if (drawBox || drawLabel)
            {
                hasBox = TryGetBoundingBox(player, screenWidth, screenHeight, out bbox);
            }

            // Draw Box
            if (drawBox && hasBox)
            {
                DrawBoundingBox(ctx, bbox, color, _boxPaint.StrokeWidth);
            }

            // Draw head marker
            bool drawHeadCircle = isAI ? App.Config.UI.EspHeadCircleAI : App.Config.UI.EspHeadCirclePlayers;
            if (drawHeadCircle)
            {
                var head = player.GetBonePos(Bones.HumanHead);
                var headTop = head;
                headTop.Y += 0.18f; // small offset to estimate head height

                if (TryProject(head, screenWidth, screenHeight, out var headScreen) &&
                    TryProject(headTop, screenWidth, screenHeight, out var headTopScreen))
                {
                    float radius;
                    if (hasBox)
                    {
                        // scale with on-screen box to stay proportional to the model
                        radius = MathF.Min(bbox.Width, bbox.Height) * 0.1f;
                    }
                    else
                    {
                        // fallback: use projected head height
                        var dy = MathF.Abs(headTopScreen.Y - headScreen.Y);
                        radius = dy * 0.65f;
                    }

                    radius = Math.Clamp(radius, 2f, 12f);
                    ctx.DrawCircle(ToRaw(headScreen), radius, color, filled: false);
                }
            }

            if (drawLabel)
            {
                DrawPlayerLabel(ctx, player, distance, color, hasBox ? bbox : (RectangleF?)null, screenWidth, screenHeight, drawName, drawDistance, drawHealth, drawGroupId);
            }
        }

        private void DrawSkeleton(Dx9RenderContext ctx, AbstractPlayer player, float w, float h, DxColor color, float thickness)
        {
            foreach (var (from, to) in _boneConnections)
            {
                var p1 = player.GetBonePos(from);
                var p2 = player.GetBonePos(to);

                // Skip if either bone position is invalid (zero or NaN)
                if (p1 == Vector3.Zero || p2 == Vector3.Zero)
                    continue;

                if (TryProject(p1, w, h, out var s1) && TryProject(p2, w, h, out var s2))
                {
                    ctx.DrawLine(ToRaw(s1), ToRaw(s2), color, thickness);
                }
            }
        }

            private bool TryGetBoundingBox(AbstractPlayer player, float w, float h, out RectangleF rect)
        {
            rect = default;
            
            // Validate player position before calculating bounding box
            var playerPos = player.Position;
            if (playerPos == Vector3.Zero || 
                float.IsNaN(playerPos.X) || float.IsInfinity(playerPos.X))
                return false;
            
            var projectedPoints = new List<SKPoint>();
            var mins = new Vector3((float)-0.4, 0, (float)-0.4);
            var maxs = new Vector3((float)0.4, (float)1.75, (float)0.4);

            mins = playerPos + mins;
            maxs = playerPos + maxs;

            var points = new List<Vector3> {
                new Vector3(mins.X, mins.Y, mins.Z),
                new Vector3(mins.X, maxs.Y, mins.Z),
                new Vector3(maxs.X, maxs.Y, mins.Z),
                new Vector3(maxs.X, mins.Y, mins.Z),
                new Vector3(maxs.X, maxs.Y, maxs.Z),
                new Vector3(mins.X, maxs.Y, maxs.Z),
                new Vector3(mins.X, mins.Y, maxs.Z),
                new Vector3(maxs.X, mins.Y, maxs.Z)
            };

            foreach (var position in points)
            {
                if (TryProject(position, w, h, out var s))
                    projectedPoints.Add(s);
            }

            if (projectedPoints.Count < 2)
                return false;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var point in projectedPoints)
            {
                if (point.X < minX) minX = point.X;
                if (point.X > maxX) maxX = point.X;
                if (point.Y < minY) minY = point.Y;
                if (point.Y > maxY) maxY = point.Y;
            }

            float boxWidth = maxX - minX;
            float boxHeight = maxY - minY;

            if (boxWidth < 1f || boxHeight < 1f || boxWidth > w * 2f || boxHeight > h * 2f)
                return false;

            minX = Math.Clamp(minX, -50f, w + 50f);
            maxX = Math.Clamp(maxX, -50f, w + 50f);
            minY = Math.Clamp(minY, -50f, h + 50f);
            maxY = Math.Clamp(maxY, -50f, h + 50f);

            float padding = 2f;
            rect = new RectangleF(minX - padding, minY - padding, (maxX - minX) + padding * 2f, (maxY - minY) + padding * 2f);
            return true;
        }

        private void DrawBoundingBox(Dx9RenderContext ctx, RectangleF rect, DxColor color, float thickness)
        {
            ctx.DrawRect(rect, color, thickness);
        }

        /// <summary>
        /// Determines player color based on type
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static SKPaint GetPlayerColor(AbstractPlayer player)
        {
             if (player.IsFocused)
                return SKPaints.PaintAimviewWidgetFocused;
            if (player is LocalPlayer)
                return SKPaints.PaintAimviewWidgetLocalPlayer;

            if (player.Type == PlayerType.PMC)
            {
                if (App.Config.UI.EspGroupColors && player.GroupID >= 0 && !(player is LocalPlayer))
                {
                    return _espGroupPaints.GetOrAdd(player.GroupID, id =>
                    {
                        var color = _espGroupPalette[Math.Abs(id) % _espGroupPalette.Length];
                        return new SKPaint
                        {
                            Color = color,
                            StrokeWidth = SKPaints.PaintAimviewWidgetPMC.StrokeWidth,
                            Style = SKPaints.PaintAimviewWidgetPMC.Style,
                            IsAntialias = SKPaints.PaintAimviewWidgetPMC.IsAntialias
                        };
                    });
                }

                if (App.Config.UI.EspFactionColors)
                {
                    if (player.PlayerSide == Enums.EPlayerSide.Bear)
                        return SKPaints.PaintPMCBear;
                    if (player.PlayerSide == Enums.EPlayerSide.Usec)
                        return SKPaints.PaintPMCUsec;
                }

                return SKPaints.PaintPMC;
            }

            return player.Type switch
            {
                PlayerType.Teammate => SKPaints.PaintAimviewWidgetTeammate,
                PlayerType.AIScav => SKPaints.PaintAimviewWidgetScav,
                PlayerType.AIRaider => SKPaints.PaintAimviewWidgetRaider,
                PlayerType.AIBoss => SKPaints.PaintAimviewWidgetBoss,
                PlayerType.PScav => SKPaints.PaintAimviewWidgetPScav,
                PlayerType.SpecialPlayer => SKPaints.PaintAimviewWidgetWatchlist,
                PlayerType.Streamer => SKPaints.PaintAimviewWidgetStreamer,
                _ => SKPaints.PaintAimviewWidgetPMC
            };
        }

        /// <summary>
        /// Draws player label (name/distance) relative to the bounding box or head fallback.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void DrawPlayerLabel(Dx9RenderContext ctx, AbstractPlayer player, float distance, DxColor color, RectangleF? bbox, float screenWidth, float screenHeight, bool showName, bool showDistance, bool showHealth, bool showGroup)
        {
            if (!showName && !showDistance && !showHealth && !showGroup)
                return;

            var name = showName ? player.Name ?? "Unknown" : null;
            var distanceText = showDistance ? $"{distance:F0}m" : null;

            string healthText = null;
            if (showHealth && player is ObservedPlayer observed && observed.HealthStatus is not Enums.ETagStatus.Healthy)
                healthText = observed.HealthStatus.ToString();

            string factionText = null;
            if (App.Config.UI.EspPlayerFaction && player.IsPmc)
                factionText = player.PlayerSide.ToString();

            string groupText = null;
            if (showGroup && player.GroupID != -1 && player.IsPmc && !player.IsAI)
                groupText = $"G:{player.GroupID}";

            string text = name;
            if (!string.IsNullOrWhiteSpace(healthText))
                text = string.IsNullOrWhiteSpace(text) ? healthText : $"{text} ({healthText})";
            if (!string.IsNullOrWhiteSpace(distanceText))
                text = string.IsNullOrWhiteSpace(text) ? distanceText : $"{text} ({distanceText})";
            if (!string.IsNullOrWhiteSpace(groupText))
                text = string.IsNullOrWhiteSpace(text) ? groupText : $"{text} [{groupText}]";
            if (!string.IsNullOrWhiteSpace(factionText))
                text = string.IsNullOrWhiteSpace(text) ? factionText : $"{text} [{factionText}]";

            if (string.IsNullOrWhiteSpace(text))
                return;

            float drawX;
            float drawY;

            var bounds = ctx.MeasureText(text, DxTextSize.Medium);
            int textHeight = Math.Max(1, bounds.Bottom - bounds.Top);
            int textPadding = 6;

            var labelPos = player.IsAI ? App.Config.UI.EspLabelPositionAI : App.Config.UI.EspLabelPosition;

            if (bbox.HasValue)
            {
                var box = bbox.Value;
                drawX = box.Left + (box.Width / 2f);
                drawY = labelPos == EspLabelPosition.Top
                    ? box.Top - textHeight - textPadding
                    : box.Bottom + textPadding;
            }
            else if (TryProject(player.GetBonePos(Bones.HumanHead), screenWidth, screenHeight, out var headScreen))
            {
                drawX = headScreen.X;
                drawY = labelPos == EspLabelPosition.Top
                    ? headScreen.Y - textHeight - textPadding
                    : headScreen.Y + textPadding;
            }
            else
            {
                return;
            }

            ctx.DrawText(text, drawX, drawY, color, DxTextSize.Medium, centerX: true);
        }

        /// <summary>
        /// Draw 'ESP Hidden' notification.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void DrawNotShown(Dx9RenderContext ctx, float width, float height)
        {
            ctx.DrawText("ESP Hidden", width / 2f, height / 2f, new DxColor(255, 255, 255, 255), DxTextSize.Large, centerX: true, centerY: true);
        }

        private void DrawCrosshair(Dx9RenderContext ctx, float width, float height)
        {
            float centerX = width / 2f;
            float centerY = height / 2f;
            float length = MathF.Max(2f, App.Config.UI.EspCrosshairLength);

            var color = GetCrosshairColor();
            ctx.DrawLine(new RawVector2(centerX - length, centerY), new RawVector2(centerX + length, centerY), color, _crosshairPaint.StrokeWidth);
            ctx.DrawLine(new RawVector2(centerX, centerY - length), new RawVector2(centerX, centerY + length), color, _crosshairPaint.StrokeWidth);
        }

        private void DrawDeviceAimbotTargetLine(Dx9RenderContext ctx, float width, float height)
        {
            var DeviceAimbot = MemDMA.DeviceAimbot;
            if (DeviceAimbot?.LockedTarget is not { } target)
                return;

            var headPos = target.GetBonePos(Bones.HumanHead);
            if (!WorldToScreen2(headPos, out var screen, width, height))
                return;

            var center = new RawVector2(width / 2f, height / 2f);
            bool engaged = DeviceAimbot.IsEngaged;
            var skColor = engaged ? new SKColor(0, 200, 255, 200) : new SKColor(255, 210, 0, 180);
            ctx.DrawLine(center, ToRaw(screen), ToColor(skColor), 2f);
        }

        private void DrawDeviceAimbotFovCircle(Dx9RenderContext ctx, float width, float height)
        {
            var cfg = App.Config.Device;
            if (!cfg.ShowFovCircle || cfg.FOV <= 0)
                return;

            float radius = Math.Clamp(cfg.FOV, 5f, Math.Min(width, height));
            bool engaged = MemDMA.DeviceAimbot?.IsEngaged == true;

            // Parse color from config using SKColor.Parse (supports #AARRGGBB and #RRGGBB formats)
            var colorStr = engaged ? cfg.FovCircleColorEngaged : cfg.FovCircleColorIdle;
            var skColor = SKColor.TryParse(colorStr, out var parsed)
                ? parsed
                : new SKColor(255, 255, 255, 180); // Fallback to semi-transparent white

            ctx.DrawCircle(new RawVector2(width / 2f, height / 2f), radius, ToColor(skColor), filled: false);
        }

        private void DrawDeviceAimbotDebugOverlay(Dx9RenderContext ctx, float width, float height)
        {
            if (!App.Config.Device.ShowDebug)
                return;

            var snapshot = MemDMA.DeviceAimbot?.GetDebugSnapshot();

            var lines = snapshot == null
                ? new[] { "Device Aimbot: no data" }
                : new[]
                {
                    "=== Device Aimbot ===",
                    $"Status: {snapshot.Status}",
                    $"Key: {(snapshot.KeyEngaged ? "ENGAGED" : "Idle")} | Enabled: {snapshot.Enabled} | Device: {(snapshot.DeviceConnected ? "Connected" : "Disconnected")}",
                    $"InRaid: {snapshot.InRaid} | FOV: {snapshot.ConfigFov:F0}px | MaxDist: {snapshot.ConfigMaxDistance:F0}m | Mode: {snapshot.TargetingMode}",
                    $"Candidates t:{snapshot.CandidateTotal} type:{snapshot.CandidateTypeOk} dist:{snapshot.CandidateInDistance} skel:{snapshot.CandidateWithSkeleton} w2s:{snapshot.CandidateW2S} final:{snapshot.CandidateCount}",
                    $"Target: {(snapshot.LockedTargetName ?? "None")} [{snapshot.LockedTargetType?.ToString() ?? "-"}] valid={snapshot.TargetValid}",
                    snapshot.LockedTargetDistance.HasValue ? $"  Dist {snapshot.LockedTargetDistance.Value:F1}m | FOV { (float.IsNaN(snapshot.LockedTargetFov) ? "n/a" : snapshot.LockedTargetFov.ToString("F1")) } | Bone {snapshot.TargetBone}" : string.Empty,
                    $"Fireport: {(snapshot.HasFireport ? snapshot.FireportPosition?.ToString() : "None")}",
                    $"Ballistics: {(snapshot.BallisticsValid ? $"OK (Speed {(snapshot.BulletSpeed.HasValue ? snapshot.BulletSpeed.Value.ToString("F1") : "?")} m/s, Predict {(snapshot.PredictionEnabled ? "ON" : "OFF")})" : "Invalid/None")}"
                }.Where(l => !string.IsNullOrEmpty(l)).ToArray();

            float x = 10f;
            float y = 40f;
            float lineStep = 16f;
            var color = ToColor(SKColors.White);

            foreach (var line in lines)
            {
                ctx.DrawText(line, x, y, color, DxTextSize.Small);
                y += lineStep;
            }
        }

        private void DrawFPS(Dx9RenderContext ctx, float width, float height)
        {
            var fpsText = $"FPS: {_fps}";
            ctx.DrawText(fpsText, 10, 10, new DxColor(255, 255, 255, 255), DxTextSize.Small);
        }

        private static RawVector2 ToRaw(SKPoint point) => new(point.X, point.Y);

        private static DxColor ToColor(SKPaint paint) => ToColor(paint.Color);

        private static DxColor ToColor(SKColor color) => new(color.Blue, color.Green, color.Red, color.Alpha);

        #endregion

        private DxColor GetPlayerColorForRender(AbstractPlayer player)
        {
            var cfg = App.Config.UI;
            var basePaint = GetPlayerColor(player);

            // Preserve special colouring (local, focused, watchlist/streamer, teammates).
            if (player is LocalPlayer || player.IsFocused ||
                player.Type is PlayerType.SpecialPlayer or PlayerType.Streamer or PlayerType.Teammate)
            {
                return ToColor(basePaint);
            }

            // Respect group/faction colours when enabled.
            if (!player.IsAI)
            {
                if (cfg.EspGroupColors && player.GroupID >= 0)
                    return ToColor(basePaint);
                if (cfg.EspFactionColors && player.IsPmc)
                {
                    var factionColor = player.PlayerSide switch
                    {
                        Enums.EPlayerSide.Bear => ColorFromHex(cfg.EspColorFactionBear),
                        Enums.EPlayerSide.Usec => ColorFromHex(cfg.EspColorFactionUsec),
                        _ => ColorFromHex(cfg.EspColorPlayers)
                    };
                    return ToColor(factionColor);
                }
            }

            if (player.IsAI)
            {
                var aiHex = player.Type switch
                {
                    PlayerType.AIBoss => cfg.EspColorBosses,
                    PlayerType.AIRaider => cfg.EspColorRaiders,
                    _ => cfg.EspColorAI
                };

                return ToColor(ColorFromHex(aiHex));
            }

            // Handle Player Scavs specifically.
            if (player.Type == PlayerType.PScav)
            {
                return ToColor(ColorFromHex(cfg.EspColorPlayerScavs));
            }

            // Fallback to user-configured player colours.
            return ToColor(ColorFromHex(cfg.EspColorPlayers));
        }

        private DxColor GetLootColorForRender() => ToColor(ColorFromHex(App.Config.UI.EspColorLoot));
        private DxColor GetExfilColorForRender() => ToColor(ColorFromHex(App.Config.UI.EspColorExfil));
        private DxColor GetTripwireColorForRender() => ToColor(ColorFromHex(App.Config.UI.EspColorTripwire));
        private DxColor GetGrenadeColorForRender() => ToColor(ColorFromHex(App.Config.UI.EspColorGrenade));
        private DxColor GetContainerColorForRender() => ToColor(ColorFromHex(App.Config.UI.EspColorContainers));
        private DxColor GetCrosshairColor() => ToColor(ColorFromHex(App.Config.UI.EspColorCrosshair));

        private static SKColor ColorFromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return SKColors.White;
            try { return SKColor.Parse(hex); }
            catch { return SKColors.White; }
        }

        private void ApplyDxFontConfig()
        {
            var ui = App.Config.UI;
            _dxOverlay?.SetFontConfig(
                ui.EspFontFamily,
                ui.EspFontSizeSmall,
                ui.EspFontSizeMedium,
                ui.EspFontSizeLarge);
        }

        #region DX Init Handling

        private void Overlay_DeviceInitFailed(Exception ex)
        {
            _dxInitFailed = true;
            DebugLogger.LogDebug($"ESP DX init failed: {ex}");

            Dispatcher.BeginInvoke(new Action(() =>
            {
                RenderRoot.Children.Clear();
                RenderRoot.Children.Add(new TextBlock
                {
                    Text = "DX overlay init failed. See log for details.",
                    Foreground = System.Windows.Media.Brushes.White,
                    Background = System.Windows.Media.Brushes.Black,
                    Margin = new Thickness(12)
                });
            }), DispatcherPriority.Send);
        }

        #endregion

        #region WorldToScreen Conversion

        /// <summary>
        /// Resets ESP state when a raid ends (ensures clean slate next raid).
        /// </summary>
        public void OnRaidStopped()
        {
            _lastInRaidState = false;
            _espGroupPaints.Clear();
            CameraManagerNew.Reset();
            RefreshESP();
            DebugLogger.LogInfo("ESP: RaidStopped -> state reset");
        }

        private bool WorldToScreen2(in Vector3 world, out SKPoint scr, float screenWidth, float screenHeight)
        {
            return CameraManagerNew.WorldToScreen(in world, out scr, true, true);
        }

        private bool TryProject(in Vector3 world, float w, float h, out SKPoint screen)
        {
            screen = default;
            if (world == Vector3.Zero)
                return false;
            if (!WorldToScreen2(world, out screen, w, h))
                return false;
            if (float.IsNaN(screen.X) || float.IsInfinity(screen.X) ||
                float.IsNaN(screen.Y) || float.IsInfinity(screen.Y))
                return false;

            const float margin = 200f; 
            if (screen.X < -margin || screen.X > w + margin ||
                screen.Y < -margin || screen.Y > h + margin)
                return false;

            return true;
        }

        #endregion

        #region Window Management

        private void GlControl_MouseDown(object sender, WinForms.MouseEventArgs e)
        {
            if (e.Button == WinForms.MouseButtons.Left)
            {
                try { this.DragMove(); } catch { /* ignore dragging errors */ }
            }
        }

        private void GlControl_DoubleClick(object sender, EventArgs e)
        {
            ToggleFullscreen();
        }

        private void GlControl_KeyDown(object sender, WinForms.KeyEventArgs e)
        {
            if (e.KeyCode == WinForms.Keys.F12)
            {
                ForceReleaseCursorAndHide();
                return;
            }

            if (e.KeyCode == WinForms.Keys.Escape && this.WindowState == WindowState.Maximized)
            {
                ToggleFullscreen();
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Allow dragging the window
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        protected override void OnClosed(System.EventArgs e)
        {
            _isClosing = true;
            try
            {
                _highFrequencyTimer?.Dispose();
                _dxOverlay?.Dispose();
                _skeletonPaint.Dispose();
                _boxPaint.Dispose();
                _crosshairPaint.Dispose();
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"ESP: OnClosed cleanup error: {ex}");
            }
            finally
            {
                base.OnClosed(e);
            }
        }

        // Method to force refresh
        public void RefreshESP()
        {
            if (_isClosing)
                return;

            try
            {
                _dxOverlay?.Render();
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"ESP Refresh error: {ex}");
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _renderPending, 0);
            }
        }

        private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ToggleFullscreen();
        }

        // Handler for keys (ESC to exit fullscreen)
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F12)
            {
                ForceReleaseCursorAndHide();
                return;
            }

            if (e.Key == Key.Escape && this.WindowState == WindowState.Maximized)
            {
                ToggleFullscreen();
            }
        }

        // Simple fullscreen toggle
        public void ToggleFullscreen()
        {
            if (_isFullscreen)
            {
                this.WindowState = WindowState.Normal;
                this.WindowStyle = WindowStyle.SingleBorderWindow;
                this.Topmost = false;
                this.ResizeMode = ResizeMode.CanResize;
                this.Width = 400;
                this.Height = 300;
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                _isFullscreen = false;
            }
            else
            {
                this.WindowStyle = WindowStyle.None;
                this.ResizeMode = ResizeMode.NoResize;
                this.Topmost = true;
                this.WindowState = WindowState.Normal;

                var monitor = GetTargetMonitor();
                var (width, height) = GetConfiguredResolution(monitor);

                this.Left = monitor?.Left ?? 0;
                this.Top = monitor?.Top ?? 0;

                this.Width = width;
                this.Height = height;
                _isFullscreen = true;
            }

            this.RefreshESP();
        }

        public void ApplyResolutionOverride()
        {
            if (!_isFullscreen)
                return;

            var monitor = GetTargetMonitor();
            var (width, height) = GetConfiguredResolution(monitor);
            this.Left = monitor?.Left ?? 0;
            this.Top = monitor?.Top ?? 0;
            this.Width = width;
            this.Height = height;
            this.RefreshESP();
        }

        private (double width, double height) GetConfiguredResolution(MonitorInfo monitor)
        {
            double width = App.Config.UI.EspScreenWidth > 0
                ? App.Config.UI.EspScreenWidth
                : monitor?.Width ?? SystemParameters.PrimaryScreenWidth;
            double height = App.Config.UI.EspScreenHeight > 0
                ? App.Config.UI.EspScreenHeight
                : monitor?.Height ?? SystemParameters.PrimaryScreenHeight;
            return (width, height);
        }

        private void ApplyResolutionOverrideIfNeeded()
        {
            if (!_isFullscreen)
                return;

            if (App.Config.UI.EspScreenWidth <= 0 && App.Config.UI.EspScreenHeight <= 0)
                return;

            var monitor = GetTargetMonitor();
            var target = GetConfiguredResolution(monitor);
            if (Math.Abs(Width - target.width) > 0.5 || Math.Abs(Height - target.height) > 0.5)
            {
                Width = target.width;
                Height = target.height;
                Left = monitor?.Left ?? 0;
                Top = monitor?.Top ?? 0;
            }
        }

        private MonitorInfo GetTargetMonitor()
        {
            return MonitorInfo.GetMonitor(App.Config.UI.EspTargetScreen);
        }

        public void ApplyFontConfig()
        {
            ApplyDxFontConfig();
            RefreshESP();
        }

        /// <summary>
        /// Emergency escape hatch if the overlay ever captures the cursor:
        /// releases capture, resets cursors, hides the ESP, and drops Topmost.
        /// Bound to F12 on both WPF and WinForms handlers.
        /// </summary>
        private void ForceReleaseCursorAndHide()
        {
            try
            {
                Mouse.Capture(null);
                WinForms.Cursor.Current = WinForms.Cursors.Default;
                this.Cursor = System.Windows.Input.Cursors.Arrow;
                Mouse.OverrideCursor = null;
                if (_dxOverlay != null)
                {
                    _dxOverlay.Capture = false;
                }
                this.Topmost = false;
                ShowESP = false;
                Hide();
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"ESP: ForceReleaseCursor failed: {ex}");
            }
        }

        #endregion
    }
}
