using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace Test.Scripts;

/// <summary>
/// 多人网络辅助入口，生命周期由 C# 自动管理，仅保留报文发送与接收转发能力。
/// </summary>
public static class NetworkInit
{
	private const string NetworkRouterNodePath = "/root/NetworkRouter";

	private static readonly StringName NetworkRouterReceiveMethod = new StringName("on_packet_received");

	private static INetGameService? _activeNetService;

	private static INetGameService? _boundNetService;

	private static readonly MessageHandlerDelegate<JzaSts2ModTextNetMessage> TextMessageHandler = OnTextMessageReceived;

	/// <summary>
	/// 客机向主机发送一条完整 JSON 报文。
	/// </summary>
	/// <param name="packetJson">完整报文 JSON。</param>
	/// <returns>发送成功返回 true，否则返回 false。</returns>
	public static bool SendPacketToHost(string packetJson)
	{
		INetGameService? netService = ResolveCurrentService();
		if (netService is not NetClientGameService clientService || !clientService.IsConnected)
		{
			return false;
		}

		netService.SendMessage(new JzaSts2ModTextNetMessage
		{
			payload = packetJson ?? string.Empty
		});

		return true;
	}

	/// <summary>
	/// 主机向房间内广播一条完整 JSON 报文。
	/// </summary>
	/// <param name="packetJson">完整报文 JSON。</param>
	/// <param name="includeSelf">是否在本机也回投该报文。</param>
	/// <returns>广播成功返回 true，否则返回 false。</returns>
	public static bool BroadcastPacketFromHost(string packetJson, bool includeSelf = true)
	{
		INetGameService? netService = ResolveCurrentService();
		if (netService is not NetHostGameService hostService || !hostService.IsConnected)
		{
			return false;
		}

		string normalizedPayload = packetJson ?? string.Empty;
		JzaSts2ModTextNetMessage message = new JzaSts2ModTextNetMessage
		{
			payload = normalizedPayload
		};

		foreach (var connectedPeer in hostService.ConnectedPeers)
		{
			netService.SendMessage(message, connectedPeer.peerId);
		}

		if (includeSelf)
		{
			ForwardPacketToNetworkRouter(normalizedPayload);
		}

		return true;
	}

	/// <summary>
	/// 主机向指定客户端转发一条完整 JSON 报文。
	/// </summary>
	/// <param name="packetJson">完整报文 JSON。</param>
	/// <param name="targetPeerId">目标客户端网络 ID。</param>
	/// <returns>发送成功返回 true，否则返回 false。</returns>
	public static bool SendPacketToClientFromHost(string packetJson, ulong targetPeerId)
	{
		INetGameService? netService = ResolveCurrentService();
		if (netService is not NetHostGameService hostService || !hostService.IsConnected)
		{
			return false;
		}

		netService.SendMessage(new JzaSts2ModTextNetMessage
		{
			payload = packetJson ?? string.Empty
		}, targetPeerId);

		return true;
	}

	/// <summary>
	/// 解析并缓存当前可用的联机服务实例。
	/// </summary>
	/// <returns>可用联机服务；不可用时返回 null。</returns>
	private static INetGameService? ResolveCurrentService()
	{
		INetGameService? runService = RunManager.Instance?.NetService;
		if (runService != null && IsUsableMultiplayerService(runService))
		{
			SetActiveService(runService);
			return runService;
		}

		INetGameService? activeService = _activeNetService;
		if (activeService != null && IsUsableMultiplayerService(activeService))
		{
			EnsureBoundService(activeService);
			return activeService;
		}

		UnbindMessageHandler();
		_activeNetService = null;
		return null;
	}

	/// <summary>
	/// 判断给定网络服务是否可用于多人消息收发。
	/// </summary>
	/// <param name="netService">待检查的网络服务。</param>
	/// <returns>可用于多人联机返回 true，否则返回 false。</returns>
	private static bool IsUsableMultiplayerService(INetGameService? netService)
	{
		if (netService == null || !netService.IsConnected)
		{
			return false;
		}

		if (netService.Type == NetGameType.Singleplayer || netService.Type == NetGameType.Replay)
		{
			return false;
		}

		return true;
	}

	/// <summary>
	/// 设置当前活动联机服务并确保消息处理器绑定。
	/// </summary>
	/// <param name="netService">当前活动服务。</param>
	private static void SetActiveService(INetGameService netService)
	{
		_activeNetService = netService;
		EnsureBoundService(netService);
	}

	/// <summary>
	/// 若服务匹配则执行解绑与缓存清理。
	/// </summary>
	/// <param name="netService">待清理的服务实例。</param>
	private static void ClearServiceIfMatches(INetGameService netService)
	{
		if (ReferenceEquals(_activeNetService, netService))
		{
			_activeNetService = null;
		}

		if (ReferenceEquals(_boundNetService, netService))
		{
			UnbindMessageHandler();
		}
	}

	/// <summary>
	/// 将文本消息处理器绑定到当前活动联机服务。
	/// </summary>
	/// <param name="netService">当前活动服务。</param>
	private static void EnsureBoundService(INetGameService netService)
	{
		if (ReferenceEquals(_boundNetService, netService))
		{
			return;
		}

		if (_boundNetService != null)
		{
			_boundNetService.UnregisterMessageHandler(TextMessageHandler);
		}

		netService.RegisterMessageHandler(TextMessageHandler);
		_boundNetService = netService;
	}

	/// <summary>
	/// 解绑当前消息处理器。
	/// </summary>
	private static void UnbindMessageHandler()
	{
		if (_boundNetService == null)
		{
			return;
		}

		_boundNetService.UnregisterMessageHandler(TextMessageHandler);
		_boundNetService = null;
	}

	/// <summary>
	/// 收到网络报文时，将整包 JSON 原样转发给 GDScript 全局单例 NetworkRouter。
	/// </summary>
	/// <param name="message">文本消息。</param>
	/// <param name="senderId">底层发送者网络 ID。</param>
	private static void OnTextMessageReceived(JzaSts2ModTextNetMessage message, ulong senderId)
	{
		_ = senderId;
		ForwardPacketToNetworkRouter(message.payload ?? string.Empty);
	}

	/// <summary>
	/// 将报文转发给 /root/NetworkRouter.on_packet_received。
	/// </summary>
	/// <param name="packetJson">完整报文 JSON。</param>
	private static void ForwardPacketToNetworkRouter(string packetJson)
	{
		SceneTree? sceneTree = Engine.GetMainLoop() as SceneTree;
		Node? networkRouter = sceneTree?.Root?.GetNodeOrNull(NetworkRouterNodePath);
		if (networkRouter == null || !networkRouter.HasMethod(NetworkRouterReceiveMethod))
		{
			return;
		}

		networkRouter.Call(NetworkRouterReceiveMethod, packetJson ?? string.Empty);
	}

	private static void ForwardConnectionDebugEvent(string action, ulong networkId)
	{
		var payload = new Dictionary<string, object?>
		{
			["event"] = action,
			["network_id"] = networkId
		};

		var packet = new Dictionary<string, object?>
		{
			["action"] = action,
			["sender_id"] = 0,
			["sender_ids"] = new[] { networkId },
			["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
			["payload"] = payload
		};

		ForwardPacketToNetworkRouter(JsonSerializer.Serialize(packet));
	}

	[HarmonyPatch(typeof(NetHostGameService))]
	private static class NetHostGameServicePatches
	{
		/// <summary>
		/// 主机 ENet 启动成功后捕获活跃服务。
		/// </summary>
		[HarmonyPatch(nameof(NetHostGameService.StartENetHost))]
		[HarmonyPostfix]
		private static void StartENetHostPostfix(NetHostGameService __instance, NetErrorInfo? __result)
		{
			if (!__result.HasValue && __instance.IsConnected)
			{
				SetActiveService(__instance);
				ForwardConnectionDebugEvent("on_connected", __instance.NetId);
			}
		}

		/// <summary>
		/// 主机每帧更新时刷新活跃服务引用。
		/// </summary>
		[HarmonyPatch(nameof(NetHostGameService.Update))]
		[HarmonyPostfix]
		private static void UpdatePostfix(NetHostGameService __instance)
		{
			if (__instance.IsConnected)
			{
				SetActiveService(__instance);
			}
		}

		/// <summary>
		/// 主机断线前清理绑定状态。
		/// </summary>
		[HarmonyPatch(nameof(NetHostGameService.OnDisconnected))]
		[HarmonyPrefix]
		private static void OnDisconnectedPrefix(NetHostGameService __instance)
		{
			if (__instance.IsConnected)
			{
				ForwardConnectionDebugEvent("on_disconnected", __instance.NetId);
			}

			ClearServiceIfMatches(__instance);
		}

		[HarmonyPatch(nameof(NetHostGameService.OnPeerConnected))]
		[HarmonyPostfix]
		private static void OnPeerConnectedPostfix(ulong peerId)
		{
			ForwardConnectionDebugEvent("on_connected", peerId);
		}

		[HarmonyPatch(nameof(NetHostGameService.OnPeerDisconnected))]
		[HarmonyPrefix]
		private static void OnPeerDisconnectedPrefix(ulong peerId, NetErrorInfo info)
		{
			_ = info;
			ForwardConnectionDebugEvent("on_disconnected", peerId);
		}
	}

	[HarmonyPatch(typeof(NetClientGameService))]
	private static class NetClientGameServicePatches
	{
		/// <summary>
		/// 客机连上主机后捕获活跃服务。
		/// </summary>
		[HarmonyPatch(nameof(NetClientGameService.OnConnectedToHost))]
		[HarmonyPostfix]
		private static void OnConnectedToHostPostfix(NetClientGameService __instance)
		{
			if (__instance.IsConnected)
			{
				SetActiveService(__instance);
				ForwardConnectionDebugEvent("on_connected", __instance.NetId);
			}
		}

		/// <summary>
		/// 客机每帧更新时刷新活跃服务引用。
		/// </summary>
		[HarmonyPatch(nameof(NetClientGameService.Update))]
		[HarmonyPostfix]
		private static void UpdatePostfix(NetClientGameService __instance)
		{
			if (__instance.IsConnected)
			{
				SetActiveService(__instance);
			}
		}

		/// <summary>
		/// 客机断线前清理绑定状态。
		/// </summary>
		[HarmonyPatch(nameof(NetClientGameService.OnDisconnectedFromHost))]
		[HarmonyPrefix]
		private static void OnDisconnectedFromHostPrefix(NetClientGameService __instance)
		{
			if (__instance.IsConnected)
			{
				ForwardConnectionDebugEvent("on_disconnected", __instance.NetId);
				ForwardConnectionDebugEvent("on_disconnected", __instance.HostNetId);
			}

			ClearServiceIfMatches(__instance);
		}
	}

}

/// <summary>
/// 自定义字符串网络消息。
/// </summary>
public struct JzaSts2ModTextNetMessage : INetMessage
{
	public string payload;

	public bool ShouldBroadcast => false;

	public NetTransferMode Mode => NetTransferMode.Reliable;

	public LogLevel LogLevel => LogLevel.Debug;

	/// <summary>
	/// 将消息写入网络序列化流。
	/// </summary>
	/// <param name="writer">网络写入器。</param>
	public void Serialize(PacketWriter writer)
	{
		writer.WriteString(payload ?? string.Empty);
	}

	/// <summary>
	/// 从网络序列化流读取消息。
	/// </summary>
	/// <param name="reader">网络读取器。</param>
	public void Deserialize(PacketReader reader)
	{
		payload = reader.ReadString();
	}

	/// <summary>
	/// 返回便于日志查看的文本表示。
	/// </summary>
	/// <returns>消息字符串。</returns>
	public override readonly string ToString()
	{
		return $"JzaSts2ModTextNetMessage {payload}";
	}
}
