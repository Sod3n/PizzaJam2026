extends RichTextLabel

@onready var container: Control = $".."
@onready var char_left: TextureRect = $"../../Character3"
@onready var char_right: TextureRect = $"../../Character4"
@onready var dialogue_label: RichTextLabel = $"."
@onready var type_timer: Timer = $Timer
@onready var texture_rect_4: TextureRect = $"../../TextureRect4"

# Storage for initial positions
var pos_left_origin: Vector2
var pos_right_origin: Vector2
var peak_offset: float = 40.0 # How many pixels they move forward

var dialogue_data: Array[Dictionary] = [
	{"name": "", "side": "center", "text": "The cow-girls followed me with their gaze."},
	{"name": "", "side": "center", "text": "Getting into my new car and inviting 'her' to dinner, I drove to the place, but..."},
	{"name": "", "side": "center", "text": "...something gnaws at me from within."},
	{"name": "Girl", "side": "right", "text": "Wow!"},
	{"name": "", "side": "center", "text": "The girl exulted, standing next to the car."},
	{"name": "Girl", "side": "right", "text": "It's a Porsche Cayenne Turbo GT, fully loaded, in black!"},
	{"name": "Girl", "side": "right", "text": "This car and I definitely have similar vibes!"},
	{"name": "Girl", "side": "right", "text": "I knew you could achieve your goal."},
	{"name": "", "side": "center", "text": "And just as she was about to get into the car..."},
	{"name": "Alexander", "side": "left", "text": "No!"},
	{"name": "", "side": "center", "text": "All this time I chased a foolish dream, even though happiness was so close."},
	{"name": "", "side": "center", "text": "Grandfather was right, the farm changed me."},
	{"name": "", "side": "center", "text": "But I suppose it's for the better."},
	{"name": "Alexander", "side": "left", "text": "Goodbye!"},
	{"name": "", "side": "center", "text": "Slamming the door in her face, I, with tears of happiness streaming down my cheeks, headed back to the farm to my beloved cow-girls."}
]

var current_line_index: int = 0
var text_speed: float = 0.04
var is_active: bool = false

func _ready() -> void:
	type_timer.timeout.connect(_on_timer_timeout)
	
	# --- STORE INITIAL POSITIONS ---
	pos_left_origin = char_left.position
	pos_right_origin = char_right.position
	
	# Setup initial state
	container.modulate.a = 0
	char_left.modulate.a = 0
	char_right.modulate.a = 0
	
	Global.on_finale.connect(start_game_intro)

func start_game_intro() -> void:
	is_active = true
	var tween = create_tween()
	tween.tween_property(container, "modulate:a", 1.0, 0.5)
	$"..".visible = true
	await tween.finished
	show_line()

func show_line() -> void:
	var data = dialogue_data[current_line_index]
	
	# Format text with name if present
	if data["name"] != "":
		dialogue_label.text = "[b]" + data["name"] + ":[/b] " + data["text"]
	else:
		dialogue_label.text = data["text"]
	
	dialogue_label.visible_characters = 0
	
	animate_characters(data["side"])
	type_timer.start(text_speed)

func animate_characters(active_side: String) -> void:
	var tween = create_tween().set_parallel(true)
	
	if active_side == "left":
		# Left steps forward and brightens
		tween.tween_property(char_left, "modulate", Color(1, 1, 1, 1), 0.3)
		tween.tween_property(char_left, "position:x", pos_left_origin.x + peak_offset, 0.3).set_trans(Tween.TRANS_QUAD)
		
		# Right steps back and dims
		tween.tween_property(char_right, "modulate", Color(0.3, 0.3, 0.3, 0.5), 0.3)
		tween.tween_property(char_right, "position:x", pos_right_origin.x, 0.3)
		
	elif active_side == "right":
		# Right steps forward (moving left relative to its origin) and brightens
		tween.tween_property(char_right, "modulate", Color(1, 1, 1, 1), 0.3)
		tween.tween_property(char_right, "position:x", pos_right_origin.x - peak_offset, 0.3).set_trans(Tween.TRANS_QUAD)
		
		# Left steps back and dims
		tween.tween_property(char_left, "modulate", Color(0.3, 0.3, 0.3, 0.5), 0.3)
		tween.tween_property(char_left, "position:x", pos_left_origin.x, 0.3)
	
	else: # "center" - narration mode, both characters dimmed and reset
		tween.tween_property(char_left, "modulate", Color(0.3, 0.3, 0.3, 0.5), 0.3)
		tween.tween_property(char_left, "position:x", pos_left_origin.x, 0.3)
		tween.tween_property(char_right, "modulate", Color(0.3, 0.3, 0.3, 0.5), 0.3)
		tween.tween_property(char_right, "position:x", pos_right_origin.x, 0.3)

func _input(event: InputEvent) -> void:
	if not is_active: return
	if event.is_action_pressed("ui_accept") or (event is InputEventMouseButton and event.pressed):
		if dialogue_label.visible_characters < dialogue_label.get_total_character_count():
			dialogue_label.visible_characters = dialogue_label.get_total_character_count()
		else:
			advance_dialogue()

func advance_dialogue() -> void:
	current_line_index += 1
	if current_line_index < dialogue_data.size():
		show_line()
	else:
		finish_dialogue()

func finish_dialogue() -> void:
	is_active = false
	var tween = create_tween().set_parallel(true)
	tween.tween_property(container, "modulate:a", 0.0, 0.5)
	tween.tween_property(char_left, "modulate:a", 0.0, 0.5)
	tween.tween_property(char_right, "modulate:a", 0.0, 0.5)
	tween.tween_property(texture_rect_4, "modulate:a", 0.0, 0.5)

func _on_timer_timeout() -> void:
	if dialogue_label.visible_characters < dialogue_label.get_total_character_count():
		dialogue_label.visible_characters += 1
