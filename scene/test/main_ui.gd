extends CanvasLayer

@export var open_button: Button
@export var popup_panel: Control
@export var close_button: Button

func _ready():
	# 绑定信号
	open_button.pressed.connect(_on_open_pressed)
	close_button.pressed.connect(_on_close_pressed)

	# 确保一开始弹窗是关闭的
	popup_panel.hide()

func _on_open_pressed():
	popup_panel.show()

func _on_close_pressed():
	popup_panel.hide()
