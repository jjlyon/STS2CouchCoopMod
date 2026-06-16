using System.Collections.Concurrent;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;

namespace CouchCoopMod.CouchCoopModCode.Couch;

public partial class CouchRunBootstrapper : Node, IStartRunLobbyListener
{
    private const ushort LocalPort = 33771;
    private readonly ConcurrentQueue<Func<Task>> _mainThreadJobs = new();
    private StartRunLobby? _lobby;
    private NetHostGameService? _netService;

    public static CouchRunBootstrapper? Instance { get; private set; }
    public bool IsStarting { get; private set; }

    public override void _Ready()
    {
        Instance = this;
        SetProcess(true);
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
        Cleanup(disconnectSession: true);
    }

    public override void _Process(double delta)
    {
        _netService?.Update();

        while (_mainThreadJobs.TryDequeue(out var job))
            TaskHelper.RunSafely(job());
    }

    public Task<Dictionary<string, object?>> StartCouchRunAsync(int playerCount, IReadOnlyList<string>? characterIds = null, string? seed = null)
    {
        var tcs = new TaskCompletionSource<Dictionary<string, object?>>();
        _mainThreadJobs.Enqueue(async () =>
        {
            try
            {
                tcs.SetResult(await StartCouchRunOnMainThread(playerCount, characterIds, seed));
            }
            catch (Exception ex)
            {
                tcs.SetResult(new Dictionary<string, object?>
                {
                    ["status"] = "error",
                    ["error"] = ex.Message,
                    ["exception_type"] = ex.GetType().FullName
                });
            }
        });
        return tcs.Task;
    }

    public Task<Dictionary<string, object?>> ResetCouchRunAsync()
    {
        var tcs = new TaskCompletionSource<Dictionary<string, object?>>();
        _mainThreadJobs.Enqueue(async () =>
        {
            try
            {
                tcs.SetResult(await ResetCouchRunOnMainThread());
            }
            catch (Exception ex)
            {
                tcs.SetResult(new Dictionary<string, object?>
                {
                    ["status"] = "error",
                    ["error"] = ex.Message,
                    ["exception_type"] = ex.GetType().FullName
                });
            }
        });
        return tcs.Task;
    }

    private async Task<Dictionary<string, object?>> ResetCouchRunOnMainThread()
    {
        if (IsStarting)
            return Error("A couch run is currently starting.");

        var wasInProgress = RunManager.Instance.IsInProgress;
        var mode = wasInProgress ? RunManager.Instance.NetService.Type : NetGameType.Singleplayer;
        var game = NGame.Instance;

        if (game == null)
            return Error("NGame.Instance is not available yet.");

        if (!wasInProgress && game.MainMenu == null)
            return Error("Wait until the main menu is fully loaded before resetting couch co-op.");

        if (wasInProgress && game.CurrentRunNode == null)
            return Error("Wait until the active run scene is fully loaded before resetting couch co-op.");

        if (!wasInProgress)
        {
            Cleanup(disconnectSession: true);
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = "Couch run reset; no run was active.",
                ["was_in_progress"] = false,
                ["mode"] = mode.ToString()
            };
        }

        MainFile.Logger.Info("Couch reset: cleaning up active run", 0);
        RunManager.Instance.CleanUp(graceful: false);

        try
        {
            if (mode == NetGameType.Singleplayer)
                SaveManager.Instance.DeleteCurrentRun();
            else
                SaveManager.Instance.DeleteCurrentMultiplayerRun();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Couch reset: failed to delete current run save: {ex.Message}", 0);
        }

        Cleanup(disconnectSession: true);

        MainFile.Logger.Info("Couch reset: returning to main menu", 0);
        await game.ReturnToMainMenu();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = wasInProgress ? "Reset couch run." : "Couch run reset; no run was active.",
            ["was_in_progress"] = wasInProgress,
            ["mode"] = mode.ToString()
        };
    }

    private async Task<Dictionary<string, object?>> StartCouchRunOnMainThread(int playerCount, IReadOnlyList<string>? characterIds, string? requestedSeed)
    {
        if (IsStarting)
            return Error("A couch run is already starting.");
        if (RunManager.Instance.IsInProgress)
            return Error("A run is already in progress.");

        playerCount = Math.Clamp(playerCount, 2, 4);
        var game = NGame.Instance ?? throw new InvalidOperationException("NGame.Instance is not available yet.");
        if (game.MainMenu == null)
            return Error("Wait until the main menu is fully loaded before starting couch co-op.");

        IsStarting = true;
        try
        {
            Cleanup(disconnectSession: true);

            _netService = new NetHostGameService();
            var hostError = _netService.StartENetHost(LocalPort, playerCount);
            if (hostError.HasValue)
                return Error($"Could not start local ENet host on port {LocalPort}: {hostError.Value.GetReason()}");

            _lobby = new StartRunLobby(GameMode.Standard, _netService, this, playerCount);
            var unlockState = SaveManager.Instance.GenerateUnlockStateFromProgress().ToSerializable();
            var maxAscension = SaveManager.Instance.Progress.MaxMultiplayerAscension;
            var characters = ResolveCharacters(characterIds, playerCount);

            var localPlayer = _lobby.AddLocalHostPlayer(new UnlockState(SaveManager.Instance.Progress), maxAscension);
            if (localPlayer == null)
                return Error("Could not add local host player.");

            for (var slot = 0; slot < playerCount; slot++)
            {
                var playerId = (ulong)(slot + 1);
                var existingIndex = _lobby.Players.FindIndex(p => p.id == playerId || p.slotId == slot);
                var value = existingIndex >= 0
                    ? _lobby.Players[existingIndex]
                    : new LobbyPlayer
                    {
                        id = playerId,
                        slotId = slot,
                        unlockState = unlockState,
                        maxMultiplayerAscensionUnlocked = maxAscension
                    };

                value.id = playerId;
                value.slotId = slot;
                value.character = characters[slot];
                value.unlockState = unlockState;
                value.maxMultiplayerAscensionUnlocked = maxAscension;
                value.isReady = true;

                if (existingIndex >= 0)
                    _lobby.Players[existingIndex] = value;
                else
                    _lobby.Players.Add(value);
                PlayerConnected(value);
            }

            _lobby.SyncAscensionChange(0);
            var seed = NormalizeSeed(requestedSeed);
            var acts = ActModel.GetRandomList(new Rng((uint)StringHelper.GetDeterministicHashCode(seed)), SaveManager.Instance.GenerateUnlockStateFromProgress(), isMultiplayer: true).ToList();

            MainFile.Logger.Info("Couch bootstrap: creating multiplayer run state", 0);
            var runState = RunState.CreateForNewRun(
                _lobby.Players.Select(p => Player.CreateForNewRun(p.character, UnlockState.FromSerializable(p.unlockState), p.id)).ToList(),
                acts.Select(a => a.ToMutable()).ToList(),
                Array.Empty<ModifierModel>(),
                GameMode.Standard,
                0,
                seed);

            MainFile.Logger.Info("Couch bootstrap: setting up RunManager multiplayer state", 0);
            RunManager.Instance.SetUpNewMultiPlayer(runState, _lobby, shouldSave: true);

            MainFile.Logger.Info("Couch bootstrap: finalizing relics and launching run", 0);
            await RunManager.Instance.FinalizeStartingRelics();
            RunManager.Instance.Launch();

            MainFile.Logger.Info("Couch bootstrap: switching to NRun scene", 0);
            game.RootSceneContainer.SetCurrentScene(NRun.Create(runState));

            MainFile.Logger.Info("Couch bootstrap: loading run and act assets", 0);
            await PreloadManager.LoadRunAssets(runState.Players.Select(p => p.Character));
            await PreloadManager.LoadActAssets(runState.Act);

            MainFile.Logger.Info("Couch bootstrap: generating map", 0);
            await RunManager.Instance.GenerateMap();

            MainFile.Logger.Info("Couch bootstrap: entering map room directly", 0);
            await EnterRoomInternalDirect(new MapRoom());

            MainFile.Logger.Info("Couch bootstrap: saving run", 0);
            await SaveManager.Instance.SaveRun(null);

            _lobby.CleanUp(disconnectSession: false);
            MainFile.Logger.Info("Couch bootstrap: run started", 0);

            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = $"Started couch run with {playerCount} players.",
                ["player_count"] = playerCount,
                ["seed"] = seed,
                ["players"] = _lobby.Players.Select(p => new Dictionary<string, object?>
                {
                    ["slot"] = p.slotId,
                    ["id"] = p.id.ToString(),
                    ["character"] = p.character.Id.Entry
                }).ToList()
            };
        }
        finally
        {
            IsStarting = false;
        }
    }

    private static string NormalizeSeed(string? seed)
    {
        return string.IsNullOrWhiteSpace(seed)
            ? SeedHelper.GetRandomSeed()
            : seed.Trim();
    }

    private static List<CharacterModel> ResolveCharacters(IReadOnlyList<string>? characterIds, int playerCount)
    {
        var defaults = new CharacterModel[]
        {
            ModelDb.Character<Ironclad>(),
            ModelDb.Character<Silent>(),
            ModelDb.Character<Defect>(),
            ModelDb.Character<Necrobinder>()
        };
        var result = new List<CharacterModel>();

        for (var i = 0; i < playerCount; i++)
        {
            var requested = characterIds != null && i < characterIds.Count ? characterIds[i] : null;
            var character = !string.IsNullOrWhiteSpace(requested)
                ? ModelDb.AllCharacters.FirstOrDefault(c => string.Equals(c.Id.Entry, requested, StringComparison.OrdinalIgnoreCase))
                : null;
            result.Add(character ?? defaults[i % defaults.Length]);
        }

        return result;
    }

    private void Cleanup(bool disconnectSession)
    {
        try { _lobby?.CleanUp(disconnectSession); }
        catch { /* best effort */ }
        try { _netService?.Disconnect(NetError.Quit, now: true); }
        catch { /* best effort */ }
        _lobby = null;
        _netService = null;
    }

    private static async Task EnterRoomInternalDirect(AbstractRoom room)
    {
        var method = typeof(RunManager).GetMethod(
            "EnterRoomInternal",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
            throw new MissingMethodException(nameof(RunManager), "EnterRoomInternal");

        var task = method.Invoke(RunManager.Instance, new object[] { room, false }) as Task;
        if (task == null)
            throw new InvalidOperationException("EnterRoomInternal did not return a Task.");

        await task;
    }

    private static Dictionary<string, object?> Error(string message)
    {
        return new Dictionary<string, object?>
        {
            ["status"] = "error",
            ["error"] = message
        };
    }

    public void PlayerConnected(LobbyPlayer player)
    {
        MainFile.Logger.Info($"Couch player connected: slot={player.slotId} id={player.id} character={player.character.Id.Entry}", 0);
    }

    public void PlayerChanged(LobbyPlayer player, bool isRandomCharacterResolution) { }
    public void AscensionChanged() { }
    public void SeedChanged() { }
    public void ModifiersChanged() { }
    public void MaxAscensionChanged() { }
    public void RemotePlayerDisconnected(LobbyPlayer player) { }
    public void BeginRun(string seed, List<ActModel> acts, IReadOnlyList<ModifierModel> modifiers) { }
    public void LocalPlayerDisconnected(NetErrorInfo info) { }
}
