using CouchCoopMod.CouchCoopModCode.QRCode;
using CouchCoopMod.CouchCoopModCode.Server;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace CouchCoopMod.CouchCoopModCode;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "CouchCoopMod";
    public const int Port = 8080;

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    private static HttpServer? _server;
    private QRCodeOverlay? _overlay;

    public static void Initialize()
    {
        Harmony harmony = new(ModId);
        harmony.PatchAll();

        var instance = new MainFile();
        ((SceneTree)Engine.GetMainLoop()).Root.CallDeferred("add_child", instance);
    }

    public override void _Ready()
    {
        _server = new HttpServer(Port);
        _server.Start();

        var ip = NetworkHelper.GetLocalIp();
        var url = $"http://{ip}:{Port}/";

        _overlay = new QRCodeOverlay();
        _overlay.Setup(url);
        _overlay.Visible = false;
        AddChild(_overlay);

        Logger.Log($"CouchCoopMod ready — scan QR or visit {url}");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.F9 })
        {
            _overlay?.Toggle();
            GetViewport().SetInputAsHandled();
        }
    }
}
