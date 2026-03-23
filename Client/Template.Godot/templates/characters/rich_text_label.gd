extends RichTextLabel

@onready var container: Control = $".."
@onready var char_left: TextureRect = $"../../Character1"
@onready var char_right: TextureRect = $"../../Character2"
@onready var dialogue_label: RichTextLabel = $"."
@onready var type_timer: Timer = $Timer
@onready var texture_rect_4: TextureRect = $"../../TextureRect4"

# Storage for initial positions
var pos_left_origin: Vector2
var pos_right_origin: Vector2
var peak_offset: float = 40.0 # How many pixels they move forward

var dialogue_data: Array[Dictionary] = [
	{"name": "", "side": "center", "text": "My name is Bykov Alexander Ivanovich. Throughout my short life, I was completely alone and depressed."},
	{"name": "", "side": "center", "text": "Since high school, I dreamed of dating a sweet and modest girl with whom I could vibe and cuddle under a blanket in the evenings."},
	{"name": "", "side": "center", "text": "However, I never had the courage to make the first move."},
	{"name": "", "side": "center", "text": "Until this day."},
	{"name": "Alexander", "side": "left", "text": "Well, you see, I..."},
	{"name": "", "side": "center", "text": "The girl indifferently stared at her phone, leisurely sipping a latte with 'special' milk against the backdrop of people calmly walking through the park."},
	{"name": "Alexander", "side": "left", "text": "...in short, I love you!"},
	{"name": "Alexander", "side": "left", "text": "Please, become my girlfriend!"},
	{"name": "", "side": "center", "text": "Without changing her expression, the girl looked me over from head to toe with cold eyes and exhaled disappointedly."},
	{"name": "Girl", "side": "right", "text": "Sorry, Alexander, your sense of style doesn't match my vibe at all, but..."},
	{"name": "Alexander", "side": "left", "text": "But?!"},
	{"name": "Alexander", "side": "left", "text": "Do I really have a chance after all?"},
	{"name": "Girl", "side": "right", "text": "I've always dreamed of riding with a guy in a Porsche Cayenne Turbo GT, fully loaded, in black!"},
	{"name": "Girl", "side": "right", "text": "If you had such a car..."},
	{"name": "Girl", "side": "right", "text": "...I would consider your candidacy for the role of my boyfriend."},
	{"name": "Alexander", "side": "left", "text": "Excellent!"},
	{"name": "", "side": "center", "text": "(Scene changes to the farm)"},
	{"name": "", "side": "center", "text": "My grandfather owned a business selling 'special' milk many years ago."},
	{"name": "", "side": "center", "text": "As an inheritance, he left me land and gave me the number of an acquaintance ready to sell me cows at a great price."},
	{"name": "", "side": "center", "text": "But grandfather also said: 'If you follow in my footsteps... Remember, the main thing is not to lose yourself. This business requires titanic endurance or great... Well, remember...'"},
	{"name": "", "side": "center", "text": "'Working in this field brings certain [b]side effects[/b].'"}
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
	
	start_game_intro()

func start_game_intro() -> void:
	is_active = true
	var tween = create_tween()
	tween.tween_property(container, "modulate:a", 1.0, 0.5)
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
