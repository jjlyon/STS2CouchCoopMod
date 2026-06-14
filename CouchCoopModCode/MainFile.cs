using CouchCoopMod.CouchCoopModCode.QRCode;
using CouchCoopMod.CouchCoopModCode.Server;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using System.Reflection;

namespace CouchCoopMod.CouchCoopModCode;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "CouchCoopMod";
    public const int Port = 8080;

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    private static HttpServer? _server;
    private QRCodeOverlay? _overlay;
    private bool _f9WasPressed;

    public static void Initialize()
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveModAssembly;

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
        var host = _server.IsPubliclyReachable ? ip : "localhost";
        var url = $"http://{host}:{Port}/";

        _overlay = new QRCodeOverlay();
        _overlay.Setup(url);
        _overlay.Visible = false;
        AddChild(_overlay);
        SetProcess(true);

        Logger.Info($"CouchCoopMod ready - scan QR or visit {url}", 0);
    }

    private static Assembly? ResolveModAssembly(object? sender, ResolveEventArgs args)
    {
        var assemblyName = new AssemblyName(args.Name).Name;
        if (assemblyName == null) return null;

        var modDir = Path.GetDirectoryName(typeof(MainFile).Assembly.Location);
        if (modDir == null) return null;

        var candidate = Path.Combine(modDir, assemblyName + ".dll");
        return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
    }

    public override void _Process(double delta)
    {
        var f9Pressed = Input.IsKeyPressed(Key.F9);
        if (f9Pressed && !_f9WasPressed)
        {
            ToggleOverlay();
        }

        _f9WasPressed = f9Pressed;
    }

    private void ToggleOverlay()
    {
        _overlay?.Toggle();
        Logger.Info($"CouchCoopMod overlay visible: {_overlay?.Visible}", 0);
    }
}
