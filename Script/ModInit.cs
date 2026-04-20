using Godot; // 别忘了引入 Godot 命名空间
using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace Test.Scripts;

// 必须要加的属性，用于注册Mod。字符串和初始化函数命名一致。
[ModInitializer("Init")]
public class ModInit
{
	private const string NetworkRouterNodeName = "NetworkRouter";
	private const string NetworkInitBridgeNodeName = "NetworkInitBridge";

	private const int MaxInjectAttempts = 30;

	private static readonly StringName NetworkRouterReceiveMethod = new("on_packet_received");

	private static readonly string[] NetworkRouterScriptPathCandidates =
	[
		"res://godot_script/network_router.gd",
		"res://mods/JzaSts2Mod/godot_script/network_router.gd"
	];

	private static bool _networkRouterInjected;
	private static bool _networkInitBridgeInjected;

	private static int _injectAttempts;
	private static int _bridgeInjectAttempts;

	// 初始化函数
	public static void Init()
	{
		// 打patch（即修改游戏代码的功能）用
		// 传入参数随意，只要不和其他人撞车即可
		var harmony = new Harmony("JzaSts2Mod");
		harmony.PatchAll();
		// 使得tscn可以加载自定义脚本
		ScriptManagerBridge.LookupScriptsInAssembly(typeof(ModInit).Assembly);
		EnsureNetworkInitBridgeInjected();
		EnsureNetworkRouterSingletonInjected();
		Log.Debug("JzaSts2Mod:Mod initialized!");

	}

	private static void EnsureNetworkRouterSingletonInjected()
	{
		if (_networkRouterInjected)
		{
			return;
		}

		SceneTree? sceneTree = Engine.GetMainLoop() as SceneTree;
		Node? root = sceneTree?.Root;
		if (root == null)
		{
			if (_injectAttempts < MaxInjectAttempts)
			{
				_injectAttempts++;
				Callable.From(EnsureNetworkRouterSingletonInjected).CallDeferred();
			}
			else
			{
				Log.Error("JzaSts2Mod: failed to inject NetworkRouter singleton because SceneTree root is unavailable.");
			}
			return;
		}

		Node? existingNode = root.GetNodeOrNull<Node>(NetworkRouterNodeName);
		if (existingNode != null)
		{
			_networkRouterInjected = true;
			GD.PushWarning("JzaSts2Mod: NetworkRouter singleton already exists at /root/NetworkRouter.");
			Log.Debug("JzaSts2Mod: NetworkRouter singleton already exists.");
			return;
		}

		Script? routerScript = LoadNetworkRouterScript();
		if (routerScript == null)
		{
			Log.Error("JzaSts2Mod: failed to inject NetworkRouter singleton because script resource is missing.");
			return;
		}

		Node networkRouterNode = new Node
		{
			Name = NetworkRouterNodeName
		};
		networkRouterNode.SetScript(routerScript);
		root.CallDeferred(Node.MethodName.AddChild, networkRouterNode);

		if (!networkRouterNode.HasMethod(NetworkRouterReceiveMethod))
		{
			Log.Error("JzaSts2Mod: NetworkRouter singleton injected but on_packet_received was not found.");
		}

		_networkRouterInjected = true;
		GD.PushWarning("JzaSts2Mod: NetworkRouter singleton injection queued for /root/NetworkRouter.");
		Log.Debug("JzaSts2Mod: NetworkRouter singleton injection queued for /root/NetworkRouter.");
	}

	private static void EnsureNetworkInitBridgeInjected()
	{
		if (_networkInitBridgeInjected)
		{
			return;
		}

		SceneTree? sceneTree = Engine.GetMainLoop() as SceneTree;
		Node? root = sceneTree?.Root;
		if (root == null)
		{
			if (_bridgeInjectAttempts < MaxInjectAttempts)
			{
				_bridgeInjectAttempts++;
				Callable.From(EnsureNetworkInitBridgeInjected).CallDeferred();
			}
			else
			{
				Log.Error("JzaSts2Mod: failed to inject NetworkInitBridge because SceneTree root is unavailable.");
			}
			return;
		}

		Node? existingNode = root.GetNodeOrNull<Node>(NetworkInitBridgeNodeName);
		if (existingNode != null)
		{
			_networkInitBridgeInjected = true;
			Log.Debug("JzaSts2Mod: NetworkInitBridge already exists.");
			return;
		}

		Node bridgeNode = new NetworkInitBridge
		{
			Name = NetworkInitBridgeNodeName
		};
		root.CallDeferred(Node.MethodName.AddChild, bridgeNode);

		_networkInitBridgeInjected = true;
		Log.Debug("JzaSts2Mod: NetworkInitBridge injection queued for /root/NetworkInitBridge.");
	}

	private static Script? LoadNetworkRouterScript()
	{
		foreach (string scriptPath in NetworkRouterScriptPathCandidates)
		{
			if (!ResourceLoader.Exists(scriptPath))
			{
				continue;
			}

			Script? script = GD.Load<Script>(scriptPath);
			if (script != null)
			{
				return script;
			}
		}

		return null;
	}
}
