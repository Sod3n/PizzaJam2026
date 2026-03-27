#!/bin/bash
# Rebuilds Template.Shared + Godot project, restarting the Godot editor automatically.
# Usage: ./rebuild.sh

set -e

PROJECT_DIR="$(cd "$(dirname "$0")" && pwd)"
GODOT_PROJECT="$PROJECT_DIR/Client/Template.Godot"
GODOT_APP="/Applications/Godot_mono.app/Contents/MacOS/Godot"

# Kill Godot if running
pkill -x Godot 2>/dev/null && echo "Closed Godot editor." && sleep 1 || true

# Build
echo "Building..."
dotnet build "$GODOT_PROJECT/Template.Godot.csproj" --nologo -v q

echo "Build succeeded. Reopening Godot..."
open -a "Godot_mono" "$GODOT_PROJECT/project.godot"
