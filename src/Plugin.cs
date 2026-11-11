using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Bind = SteelInput.KeyBinds;

namespace RavenfieldSplit
{
    [BepInPlugin(Id, "Ravenfield Split Screen", "0.1.8")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "local.ravenfield.splitscreen";
        private static readonly Guid SupportedBuild = new Guid("15ab02d8-2bbf-4ac1-90d1-bef784c37587");
        internal static Plugin Instance;
        internal readonly PlayerOneInput FirstInput = new PlayerOneInput();
        private readonly Pad[] pads = { new Pad(1), new Pad(2), new Pad(3), new Pad(4) };
        private Pad[] directPads;
        private SdlControllers directControllers;
        internal Pad FirstPad { get; private set; }
        internal LoadoutPicker FirstLoadout { get; private set; }
        internal DeploymentPicker FirstDeployment { get; private set; }
        internal SquadOrders FirstOrders { get; private set; }
        internal Settings Settings;
        internal SecondPlayer Second;
        private SplitView view;
        private Harmony harmony;
        private ActorManager match;
        private bool failedThisMatch;
        private int firstMenuFrame = -1;
        private string joinMessage;
        private float joinMessageUntil;

        private void Awake()
        {
            Settings = new Settings(Config);
            if (!Settings.Enabled.Value) return;
            Guid installedBuild = typeof(Actor).Module.ModuleVersionId;
            if (installedBuild != SupportedBuild)
            {
                Logger.LogWarning($"Unsupported Assembly-CSharp MVID {installedBuild}. Split screen is disabled.");
                return;
            }

            harmony = new Harmony(Id);
            try
            {
                Patches.Install(harmony);
                directControllers = new SdlControllers(
                    Path.Combine(Path.GetDirectoryName(Info.Location), "native", "SDL2.dll"), Warn, Logger.LogInfo);
                directPads = new[] { new Pad(1, directControllers), new Pad(2, directControllers),
                    new Pad(3, directControllers), new Pad(4, directControllers) };
                view = new SplitView();
                Instance = this;
                Logger.LogInfo("Split screen ready. Deploy Player 1, then press F7 or Start on Player 2's controller.");
            }
            catch (Exception error)
            {
                harmony.UnpatchSelf();
                Instance = null;
                Logger.LogError(error);
            }
        }

        internal static SecondPlayer For(Actor actor)
        {
            SecondPlayer player = Instance?.Second;
            return player != null && ReferenceEquals(player.Actor, actor) ? player : null;
        }

        internal static SecondPlayer For(AiActorController controller)
        {
            SecondPlayer player = Instance?.Second;
            return player != null && ReferenceEquals(player.Controller, controller) ? player : null;
        }

        internal bool RouteFirstInput => Second != null && !failedThisMatch && SteelInput.IsInitialized();
        internal bool CombatInputAllowed => Application.isFocused && !GameManager.IsPaused() &&
            !GameManager.gameOver && !GameManager.gameplayIsFrozen && LocalPlayer.controller != null &&
            !LocalPlayer.controller.IsCursorFree() && !LocalPlayer.controller.inPhotoMode;

        internal void SampleFirstInput()
        {
            FirstInput.Sample(Settings, FirstPad);
            if (firstMenuFrame == Time.frameCount) return;
            firstMenuFrame = Time.frameCount;
            bool combat = CombatInputAllowed;
            FirstLoadout?.Update(FirstPad, combat && FirstOrders?.BlocksInput != true, FirstInput.Down(Bind.OpenLoadout));
            bool free = combat && FirstLoadout?.BlocksInput != true;
            bool shortcut = FirstPad != null ? FirstPad.Down(Buttons.Right) : FirstInput.Down(Bind.SquadLeaderKit);
            FirstOrders?.Update(FirstPad, free, shortcut, LocalPlayer.controller.GetActiveCamera());
            FirstDeployment?.Update(FirstPad, free);
            if (FirstDeployment?.Active == true && FirstDeployment.Confirmed)
                LocalPlayer.controller.hasAcceptedLoadoutAfterDeath = true;
        }

        internal bool SuppressFirstInput(Bind bind) =>
            (FirstLoadout != null && bind == Bind.OpenLoadout) ||
            (FirstOrders != null && bind == Bind.SquadLeaderKit) ||
            (bind != Bind.TogglePauseMenu &&
                (FirstLoadout?.BlocksInput == true || FirstOrders?.BlocksInput == true || FirstDeployment?.Active == true));

        private void Update()
        {
            if (Instance != this) return;
            if (!ReferenceEquals(match, ActorManager.instance))
            {
                ClearMatch();
                match = ActorManager.instance;
                failedThisMatch = false;
            }
            try
            {
                bool keyboardJoin = Input.GetKeyDown(KeyCode.F7);
                if (Second == null && (keyboardJoin || IsJoinPress()))
                {
                    TryJoin(keyboardJoin);
                }
                if (RouteFirstInput) SampleFirstInput();
                if (!failedThisMatch && CanJoinMatch()) Second?.UpdateDeployment();
            }
            catch (Exception error) { DisableForMatch(error); }
        }

        internal bool IsJoinPress()
        {
            if (Second != null || JoinBlocker() != null) return false;
            Pad second = FindSecondPad(false, out _);
            return second != null && second.Down(Buttons.Start);
        }

        private void TryJoin(bool keyboardJoin)
        {
            string blocker = JoinBlocker();
            if (blocker != null) { ReportJoinBlocker(blocker); return; }

            Pad second = FindSecondPad(keyboardJoin, out Pad first);
            if (second == null)
            {
                ReportJoinBlocker("No gamepad detected. Connect your controllers and press F7 again.");
                return;
            }

            Second = new SecondPlayer(this, match.CreateAIActor(LocalPlayer.actor.team), second);
            FirstPad = first;
            FirstDeployment = new DeploymentPicker(LocalPlayer.actor, Second.Actor);
            FirstOrders = new SquadOrders(LocalPlayer.actor);
            LoadoutUi native = LoadoutUi.instance;
            if (native != null && native.loadout != null)
            {
                FirstLoadout = new LoadoutPicker(native.loadout, _ =>
                {
                    native.SaveCurrentLoadout();
                    Patches.RefreshNativeLoadout(native);
                    if (LocalPlayer.actor.dead) LocalPlayer.controller.hasAcceptedLoadoutAfterDeath = true;
                });
            }
            string firstDevice = FirstPad?.Label ?? "keyboard/mouse";
            Logger.LogInfo($"Player 2 joined team {Second.Actor.team}. P1 {firstDevice}, P2 {second.Label}.");
            ShowJoinMessage($"P1 {firstDevice}   |   P2 {second.Label}");
        }

        private Pad FindSecondPad(bool allowKeyboardFallback, out Pad first)
        {
            Pad second = Assign(pads, false, out first) ?? Assign(directPads, false, out first);
            if (second != null || !allowKeyboardFallback) return second;
            return Assign(pads, true, out first) ?? Assign(directPads, true, out first);
        }

        private Pad Assign(Pad[] candidates, bool allowKeyboardFallback, out Pad first)
        {
            int connected = 0;
            foreach (Pad pad in candidates)
            {
                pad.Poll();
                if (pad.Connected) connected |= ControllerSlots.Bit(pad.Slot);
            }
            ControllerSlots.Assign(connected, Settings.FirstSlot, Settings.SecondSlot, Settings.Keyboard.Value,
                allowKeyboardFallback, out int firstSlot, out int secondSlot);
            first = firstSlot == 0 ? null : candidates[firstSlot - 1];
            return secondSlot == 0 ? null : candidates[secondSlot - 1];
        }

        private string JoinBlocker()
        {
            if (failedThisMatch) return "Split screen stopped after an error. See the BepInEx log.";
            if (match == null || !match.isInitialized || LocalPlayer.actor == null)
                return "Start an Instant Action match and deploy Player 1 first.";
            if (!CanJoinMatch())
                return "Split screen supports Instant Action in Point Match, Battle, and Domination.";
            if (LocalPlayer.actor.dead) return "Deploy Player 1 before joining.";
            return CombatInputAllowed ? null : "Return to active gameplay before joining.";
        }

        private void ReportJoinBlocker(string message)
        {
            Logger.LogWarning("Player 2 could not join. " + message);
            ShowJoinMessage(message);
        }

        private void ShowJoinMessage(string message)
        {
            joinMessage = message;
            joinMessageUntil = Time.unscaledTime + 6f;
        }

        private bool CanJoinMatch()
        {
            if (match == null || !match.isInitialized || LocalPlayer.actor == null ||
                LocalPlayer.actor.team < 0 || LocalPlayer.actor.team > 1 ||
                GameManager.IsPlayingCampaign() || GameManager.IsSpectating()) return false;
            GameModeBase mode = GameModeBase.activeGameMode;
            return mode != null && mode.allowDefaultRespawn &&
                (mode.GetType() == typeof(PointMatch) || mode.GetType() == typeof(BattleMode) ||
                 mode.GetType() == typeof(DominationMode));
        }

        private void LateUpdate()
        {
            if (view == null) return;
            try
            {
                if (Second != null && Second.Actor != null && !failedThisMatch && CanJoinMatch() &&
                    !GameManager.gameOver && !LoadoutUi.IsOpen() && !IngameMenuUi.IsOpen() &&
                    !LocalPlayer.controller.inPhotoMode)
                    view.Update(Second, Settings.SideBySide.Value);
                else view.Suspend();
            }
            catch (Exception error) { DisableForMatch(error); }
        }

        private void OnGUI()
        {
            if (view != null && view.Active && Second != null && LocalPlayer.actor != null)
                view.Draw(LocalPlayer.actor, Second, FirstPad == null || FirstPad.Connected,
                    FirstLoadout, FirstPad == null, FirstDeployment, FirstOrders);
            if (joinMessage != null && Time.unscaledTime < joinMessageUntil)
            {
                float width = Mathf.Min(610f, Screen.width - 20f);
                GUI.Box(new Rect((Screen.width - width) * 0.5f, 24f, width, 28f), joinMessage);
            }
        }

        internal void DisableForMatch(Exception error)
        {
            if (failedThisMatch) return;
            failedThisMatch = true;
            Logger.LogError("Split screen stopped for this match. " + error);
            ShowJoinMessage("Split screen stopped after an error. See the BepInEx log.");
            try { view?.Suspend(); }
            finally
            {
                try { Second?.Deactivate(); }
                catch (Exception cleanupError) { Logger.LogError(cleanupError); }
            }
        }

        internal void Warn(string message) => Logger.LogWarning(message);
        internal void LogCombat(string message) => Logger.LogInfo(message);

        private void ClearMatch()
        {
            view?.Suspend();
            Second?.Dispose();
            Second = null;
            FirstPad = null;
            FirstLoadout = null;
            FirstDeployment = null;
            FirstOrders = null;
            firstMenuFrame = -1;
            joinMessage = null;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            if (Second != null && Second.Actor != null) Second.Deactivate();
            ClearMatch();
            view?.Dispose();
            directControllers?.Dispose();
            harmony.UnpatchSelf();
            Instance = null;
        }
    }

    internal sealed class Settings
    {
        private readonly ConfigFile config;
        internal readonly ConfigEntry<bool> Enabled, Keyboard, SideBySide, InvertFirst, InvertSecond;
        internal readonly ConfigEntry<int> FirstIndex, SecondIndex;
        internal readonly ConfigEntry<float> StickDeadzone, FirstLook, SecondLook, RespawnDelay;
        internal readonly ConfigEntry<string> Primary, Secondary, Gear1, Gear2, Gear3;
        internal int FirstSlot => FirstIndex.Value;
        internal int SecondSlot => SecondIndex.Value;
        internal float Deadzone => StickDeadzone.Value;

        internal Settings(ConfigFile config)
        {
            this.config = config;
            Enabled = config.Bind("General", "Enabled", true, "Enable split screen on supported builds.");
            SideBySide = config.Bind("Display", "SideBySide", false, "True for left/right. False for top/bottom.");
            Keyboard = config.Bind("Controllers", "PlayerOneKeyboardMouse", false, "Use Ravenfield keyboard/mouse bindings for Player 1.");
            FirstIndex = config.Bind("Controllers", "PlayerOneSlot", 1,
                new ConfigDescription("Preferred gamepad slot, starting at 1.", new AcceptableValueRange<int>(1, 4)));
            SecondIndex = config.Bind("Controllers", "PlayerTwoSlot", 2,
                new ConfigDescription("Preferred gamepad slot, starting at 1.", new AcceptableValueRange<int>(1, 4)));
            StickDeadzone = config.Bind("Controllers", "Deadzone", 0.18f,
                new ConfigDescription("Radial stick deadzone.", new AcceptableValueRange<float>(0.05f, 0.8f)));
            FirstLook = config.Bind("Controllers", "PlayerOneLookMultiplier", 1f,
                new ConfigDescription("Multiplier on Ravenfield's native stick sensitivity.", new AcceptableValueRange<float>(0.1f, 3f)));
            SecondLook = config.Bind("Controllers", "PlayerTwoLookDegreesPerSecond", 165f,
                new ConfigDescription("Player 2's maximum look speed.", new AcceptableValueRange<float>(30f, 500f)));
            InvertFirst = config.Bind("Controllers", "PlayerOneInvertY", false, "Invert Player 1 gamepad look.");
            InvertSecond = config.Bind("Controllers", "PlayerTwoInvertY", false, "Invert Player 2 gamepad look.");
            RespawnDelay = config.Bind("General", "RespawnDelaySeconds", 6f,
                new ConfigDescription("Minimum time before Player 2 can deploy.", new AcceptableValueRange<float>(1f, 60f)));
            const string weaponHelp = "Exact in-game weapon name. Blank copies Player 1's initial selection; 'none' leaves the slot empty.";
            Primary = config.Bind("PlayerTwoLoadout", "Primary", "", weaponHelp);
            Secondary = config.Bind("PlayerTwoLoadout", "Secondary", "", weaponHelp);
            Gear1 = config.Bind("PlayerTwoLoadout", "Gear1", "", weaponHelp);
            Gear2 = config.Bind("PlayerTwoLoadout", "Gear2", "", weaponHelp);
            Gear3 = config.Bind("PlayerTwoLoadout", "Gear3", "", weaponHelp);
        }

        internal void SaveSecondLoadout(WeaponManager.LoadoutSet loadout)
        {
            bool saveOnSet = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try
            {
                Primary.Value = loadout.primary?.name ?? "none";
                Secondary.Value = loadout.secondary?.name ?? "none";
                Gear1.Value = loadout.gear1?.name ?? "none";
                Gear2.Value = loadout.gear2?.name ?? "none";
                Gear3.Value = loadout.gear3?.name ?? "none";
                config.Save();
            }
            finally { config.SaveOnConfigSet = saveOnSet; }
        }
    }
}
