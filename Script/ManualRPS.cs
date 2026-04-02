using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Flavor;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;

namespace Test.Scripts;

[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), "AwardRelics")]
public static class ManualRpsPatch
{
    private const string ManualRpsScenePath = "res://scene/manual_rps/manual_rps.tscn";
    private const string ManualRpsNodeName = "ManualRpsOverlay";
    private static Node? _currentUi;
    private static bool _isMessageHandlerRegistered;

    public static bool Prefix(TreasureRoomRelicSynchronizer __instance)
    {
        GD.Print("JzaSts2Mod: 成功拦截 AwardRelics，正在弹出 UI");
        SafeInstantiateUI();
        EnsureNetworkHandlerRegistered();
        InjectUiBridgeAndContext();
        return false;
    }

    private static void SafeInstantiateUI()
    {
        SceneTree tree = (SceneTree)Engine.GetMainLoop();
        Node oldUi = tree.Root.GetNodeOrNull<Node>(ManualRpsNodeName);
        if (oldUi != null)
        {
            oldUi.Name = ManualRpsNodeName + "_PendingKill";
            oldUi.QueueFree();
        }

        PackedScene scene = ResourceLoader.Load<PackedScene>(ManualRpsScenePath);
        if (scene == null || NGame.Instance == null)
        {
            GD.Print("JzaSts2Mod: UI 场景或 NGame.Instance 不可用，跳过创建");
            return;
        }

        _currentUi = scene.Instantiate();
        _currentUi.Name = ManualRpsNodeName;
        NGame.Instance.AddChild(_currentUi);

        if (_currentUi is CanvasLayer canvasLayer)
        {
            canvasLayer.Visible = true;
        }
    }

    private static void EnsureNetworkHandlerRegistered()
    {
        INetGameService netService = RunManager.Instance.NetService;
        if (_isMessageHandlerRegistered)
        {
            netService.UnregisterMessageHandler<MapPingMessage>(OnMapPingReceived);
        }

        netService.RegisterMessageHandler<MapPingMessage>(OnMapPingReceived);
        _isMessageHandlerRegistered = true;
    }

    private static void InjectUiBridgeAndContext()
    {
        if (_currentUi == null || !GodotObject.IsInstanceValid(_currentUi))
        {
            return;
        }

        INetGameService netService = RunManager.Instance.NetService;
        string role = netService.Type switch
        {
            NetGameType.Host => "主机",
            NetGameType.Client => "客机",
            _ => netService.Type.ToString()
        };

        _currentUi.Call("inject_csharp_bridge", Callable.From<int>(SendRpsChoiceFromUi));
        _currentUi.Call("set_network_context", (long)netService.NetId, role);
    }

    private static void SendRpsChoiceFromUi(int choice)
    {
        INetGameService netService = RunManager.Instance.NetService;
        ulong localNetId = netService.NetId;
        GD.Print($"JzaSts2Mod: [发送] role={netService.Type} netId={localNetId} choice={choice}");

        // 使用现有可序列化消息承载一个 int，真实发送者 ID 以 senderId 为准。
        MapPingMessage message = new MapPingMessage
        {
            coord = new MapCoord(choice, 0)
        };

        netService.SendMessage(message);
    }

    private static void OnMapPingReceived(MapPingMessage message, ulong senderId)
    {
        if (_currentUi == null || !GodotObject.IsInstanceValid(_currentUi))
        {
            return;
        }

        int choice = message.coord.col;
        string role = senderId == RunManager.Instance.NetService.NetId ? "本机回环" : "远端";
        _currentUi.Call("receive_network_choice", (long)senderId, choice, role);
    }
}
