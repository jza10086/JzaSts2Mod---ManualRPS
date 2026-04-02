extends CanvasLayer

@export var rock_btn: Button
@export var paper_btn: Button
@export var scissors_btn: Button
@export var status_label: Label

enum RpsChoice {
    ROCK = 1,
    PAPER = 2,
    SCISSORS = 3,
}

var _send_bridge: Callable = Callable()
var _local_net_id: int = -1
var _local_role: String = "未知"

func _ready() -> void:
    if rock_btn:
        rock_btn.pressed.connect(func(): _on_button_pressed(RpsChoice.ROCK))
    if paper_btn:
        paper_btn.pressed.connect(func(): _on_button_pressed(RpsChoice.PAPER))
    if scissors_btn:
        scissors_btn.pressed.connect(func(): _on_button_pressed(RpsChoice.SCISSORS))
    _dbg("本地手动猜拳界面已加载")
    _set_status("等待 C# 网络上下文...")


func inject_csharp_bridge(bridge: Callable) -> void:
    _send_bridge = bridge
    _dbg("C# 发送桥已注入")


func set_network_context(local_net_id: int, local_role: String) -> void:
    _local_net_id = local_net_id
    _local_role = local_role
    _dbg("[上下文] %s | 用户网络id: %d" % [_local_role, _local_net_id])
    _set_status("当前身份：%s (%d)" % [_local_role, _local_net_id])


func set_expected_players(player_ids: Array) -> void:
    _dbg("[目标玩家] %s" % str(player_ids))


func _on_button_pressed(choice: int) -> void:
    _dbg("[发送] %s | 用户网络id: %d | 猜拳输入enum: %d" % [_local_role, _local_net_id, choice])
    if _send_bridge.is_valid():
        _send_bridge.call(choice)
    else:
        _dbg("发送失败：C# 发送桥未注入")


func receive_network_choice(peer_id: int, choice: int, peer_role: String = "远端") -> void:
    _dbg("[接收] %s | 用户网络id: %d | 猜拳输入enum: %d" % [peer_role, peer_id, choice])


func on_choice_progress(collected: int, expected: int) -> void:
    _set_status("已收集 %d/%d" % [collected, expected])


func on_csharp_result_applied(winner_id: int, summary: String) -> void:
    if winner_id == _local_net_id:
        _set_status("你赢了！ " + summary)
    else:
        _set_status(summary)
    _dbg("[结算] winner=%d summary=%s" % [winner_id, summary])


func _set_status(text: String) -> void:
    if status_label:
        status_label.text = text

func _dbg(message: String) -> void:
    print_rich("[color=green][ManualRps] %s[/color]" % message)
