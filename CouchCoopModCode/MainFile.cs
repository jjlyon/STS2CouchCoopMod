using CouchCoopMod.CouchCoopModCode.Server;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace CouchCoopMod.CouchCoopModCode;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "CouchCoopMod";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    private static HttpServer? _server;

    public static void Initialize()
    {
        Harmony harmony = new(ModId);
        harmony.PatchAll();

        _server = new HttpServer();
        _server.Start();

        Logger.Log("CouchCoopMod initialized");
    }
}
