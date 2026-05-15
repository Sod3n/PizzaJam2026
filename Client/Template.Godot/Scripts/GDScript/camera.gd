# Attach this to your Camera3D. Camera is authored as a child of the player so
# its initial transform defines the framing offset; at runtime it detaches
# (top_level), tracks the player rigidly, and leans very slightly in the
# direction of movement so the world ahead of the player is a touch more visible.
extends Camera3D

@export var pixels_per_meter: float = 32.0
@export var lead_smoothing: float = 2.5    # how fast the lean ramps in/out
@export var lead_distance: float = 0.5     # max world-units of lean at full speed
@export var lead_speed_ref: float = 6.0    # player speed (u/s) at which lean reaches max

var _target: Node3D
var _offset: Vector3
var _prev_target_pos: Vector3
var _lead: Vector3 = Vector3.ZERO

var _panning: bool = false
var _pan_tween: Tween = null

# Smooth release: blend from the pan position back to target+offset over a duration
# instead of snapping. Tracks the moving target the whole time.
var _returning: bool = false
var _return_t: float = 0.0
var _return_duration: float = 0.0
var _return_start: Vector3 = Vector3.ZERO

func pan_to(world_pos: Vector3, duration: float) -> void:
	_panning = true
	_returning = false
	var start := global_position
	var end := world_pos + _offset
	if _pan_tween:
		_pan_tween.kill()
	_pan_tween = create_tween()
	_pan_tween.tween_method(_apply_pan, start, end, duration)\
		.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)

func _apply_pan(pos: Vector3) -> void:
	global_position = pos

func release_override(duration: float = 0.0) -> void:
	if _pan_tween:
		_pan_tween.kill()
		_pan_tween = null
	_lead = Vector3.ZERO
	if _target:
		_prev_target_pos = _target.global_position
	if duration > 0.0:
		_panning = false
		_returning = true
		_return_t = 0.0
		_return_duration = duration
		_return_start = global_position
	else:
		_panning = false
		_returning = false

func _ready() -> void:
	_target = get_parent() as Node3D
	if _target == null:
		return
	_offset = global_position - _target.global_position
	_prev_target_pos = _target.global_position
	top_level = true
	# Run after ViewSmoothingManager so we read the smoothed player position
	# from this frame, not last frame's.
	process_priority = 1000

func _process(delta: float) -> void:
	if _target == null or delta <= 0.0 or _panning:
		return

	var target_pos := _target.global_position

	# Smooth release: ease from the panned position toward the moving target.
	# Skip the lead lean during the blend so it doesn't fight the catch-up.
	if _returning:
		_return_t += delta
		var u: float = clamp(_return_t / _return_duration, 0.0, 1.0)
		# Sine ease-in-out matches the pan_to tween for a symmetric feel.
		var eased: float = 0.5 - 0.5 * cos(u * PI)
		global_position = _return_start.lerp(target_pos + _offset, eased)
		_prev_target_pos = target_pos
		if u >= 1.0:
			_returning = false
			global_position = target_pos + _offset
		return

	var vel := (target_pos - _prev_target_pos) / delta
	_prev_target_pos = target_pos

	# Project velocity onto camera's screen axes so the lean shifts along
	# screen X/Y instead of into/out of the ground plane.
	var cam_right := global_transform.basis.x
	var cam_up := global_transform.basis.y
	var planar := cam_right * vel.dot(cam_right) + cam_up * vel.dot(cam_up)

	var desired_lead := Vector3.ZERO
	var speed := planar.length()
	if speed > 0.01:
		var amt: float = clamp(speed / lead_speed_ref, 0.0, 1.0)
		desired_lead = planar.normalized() * (lead_distance * amt)

	var t: float = 1.0 - exp(-lead_smoothing * delta)
	_lead = _lead.lerp(desired_lead, t)

	global_position = target_pos + _offset + _lead
