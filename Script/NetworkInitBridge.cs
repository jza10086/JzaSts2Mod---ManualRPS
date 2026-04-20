using Godot;

namespace Test.Scripts;

/// <summary>
/// GDScript 到 NetworkInit 的实例桥接节点，避免依赖 ClassDB 静态调用注册。
/// </summary>
public partial class NetworkInitBridge : Node
{
	public bool SendPacketToHost(string packetJson)
	{
		return NetworkInit.SendPacketToHost(packetJson ?? string.Empty);
	}

	public bool BroadcastPacketFromHost(string packetJson, bool includeSelf = true)
	{
		return NetworkInit.BroadcastPacketFromHost(packetJson ?? string.Empty, includeSelf);
	}

	public bool SendPacketToClientFromHost(string packetJson, long targetClientId)
	{
		if (targetClientId < 0)
		{
			return false;
		}

		return NetworkInit.SendPacketToClientFromHost(packetJson ?? string.Empty, (ulong)targetClientId);
	}
}
