@tool
class_name LandTypeIconSet
extends Resource

enum LandType {
	House = 0,
	LoveHouse = 1,
	SellPoint = 2,
	FinalStructure = 3,
	CarrotFarm = 4,
	AppleOrchard = 5,
	MushroomCave = 6,
	HelperAssistant = 7,
	Decoration = 12,
	Warehouse = 13,
	PlayerHouse = 14,
	Library = 15,
	Smithy = 16,
}

@export var icons: Dictionary[LandType, Texture2D] = {}

func try_get(land_type: int) -> Texture2D:
	return icons.get(land_type)
