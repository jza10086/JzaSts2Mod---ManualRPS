extends Node

signal packet_received(action: String, packet: Dictionary, sender_ids: Array)

const _NETWORK_INIT_CLASS_CANDIDATES: Array[StringName] = [&"NetworkInit", &"Test.Scripts.NetworkInit"]
const _NETWORK_INIT_BRIDGE_NODE_PATH: String = "/root/NetworkInitBridge"
const _SEND_TO_HOST_METHOD_CANDIDATES: Array[StringName] = [&"SendPacketToHost", &"send_packet_to_host"]
const _BROADCAST_METHOD_CANDIDATES: Array[StringName] = [&"BroadcastPacketFromHost", &"broadcast_packet_from_host"]
const _TARGETED_FORWARD_METHOD_CANDIDATES: Array[StringName] = [&"SendPacketToClientFromHost", &"send_packet_to_client_from_host"]

var _last_host_sender_id: int = 0


func on_packet_received(packet_json: String) -> void:
	var parsed: Variant = JSON.parse_string(packet_json)
	if typeof(parsed) != TYPE_DICTIONARY:
		push_warning("NetworkRouter 收到非字典报文: %s" % packet_json)
		return

	var packet: Dictionary = parsed
	var action: String = str(packet.get("action", ""))
	var sender_ids: Array = _extract_sender_ids(packet)
	if sender_ids.size() > 0:
		_last_host_sender_id = int(sender_ids[0])

	packet_received.emit(action, packet, sender_ids)


func send_to_host(action: String, payload: Variant) -> bool:
	var reciver_ids: Array = []
	if _last_host_sender_id > 0:
		reciver_ids.append(_last_host_sender_id)

	var packet := _build_packet(action, payload, reciver_ids)
	var packet_json := JSON.stringify(packet)
	var result: Variant = _call_network_init(_SEND_TO_HOST_METHOD_CANDIDATES, [packet_json])
	if typeof(result) == TYPE_BOOL:
		return result
	return false


func broadcast_to_all_clients(action: String, payload: Variant, include_self: bool = true) -> bool:
	var packet := _build_packet(action, payload, [])
	var packet_json := JSON.stringify(packet)
	var result: Variant = _call_network_init(_BROADCAST_METHOD_CANDIDATES, [packet_json, include_self])
	if typeof(result) == TYPE_BOOL:
		return result
	return false


func forward_to_client(action: String, payload: Variant, target_client_id: int) -> bool:
	var packet := _build_packet(action, payload, [target_client_id])
	var packet_json := JSON.stringify(packet)
	var result: Variant = _call_network_init(_TARGETED_FORWARD_METHOD_CANDIDATES, [packet_json, target_client_id])
	if typeof(result) == TYPE_BOOL:
		return result
	return false


func _build_packet(action: String, payload: Variant, reciver_ids: Array) -> Dictionary:
	return {
		"action": action,
		"sender_id": 0,
		"reciver_ids": reciver_ids,
		"timestamp": Time.get_unix_time_from_system(),
		"payload": payload
	}


func _extract_sender_ids(packet: Dictionary) -> Array:
	if packet.has("sender_ids") and packet["sender_ids"] is Array:
		return (packet["sender_ids"] as Array).duplicate()

	var sender_ids: Array = []
	if packet.has("sender_id"):
		sender_ids.append(int(packet["sender_id"]))

	return sender_ids


func _call_network_init(method_candidates: Array[StringName], args: Array) -> Variant:
	for target_class in _NETWORK_INIT_CLASS_CANDIDATES:
		if not ClassDB.class_exists(target_class):
			continue

		for method_name in method_candidates:
			if not ClassDB.class_has_method(target_class, method_name):
				continue

			var call_args: Array = [target_class, method_name]
			call_args.append_array(args)
			return Callable(ClassDB, "class_call_static").callv(call_args)

	var bridge_node := get_node_or_null(_NETWORK_INIT_BRIDGE_NODE_PATH)
	if bridge_node != null:
		for method_name in method_candidates:
			if not bridge_node.has_method(method_name):
				continue
			return bridge_node.callv(method_name, args)

	push_warning("NetworkRouter 无法调用 NetworkInit（静态或桥接节点），请确认初始化注入成功。")
	return null
