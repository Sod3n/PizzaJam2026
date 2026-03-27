#!/bin/bash
# Builds server and copies ALL DLLs (including transitive deps) to Client/Template.Godot/lib/
# Run with Godot CLOSED, then reopen Godot.

set -e

echo "Building server..."
dotnet build Server/Template.Shared/Template.Shared.csproj -f net8.0 -v q --nologo 2>&1 | grep -v warning || true

LIB_DIR="Client/Template.Godot/lib"
mkdir -p "$LIB_DIR"

echo "Building additional dependencies..."
dotnet build Server/Framework/Networking/Deterministic.GameFramework.Network.LiteNetLib/Deterministic.GameFramework.Network.LiteNetLib.csproj -f net8.0 -v q --nologo 2>&1 | grep -v warning || true
dotnet build Server/Framework/Profiler/Deterministic.GameFramework.Profiler/Deterministic.GameFramework.Profiler.csproj -f net8.0 -v q --nologo 2>&1 | grep -v warning || true

echo "Publishing to collect all DLLs..."
dotnet publish Server/Template.Shared/Template.Shared.csproj -f net8.0 -o "$LIB_DIR" --no-build -v q --nologo 2>&1 | grep -v warning || true

# Copy additional DLLs not in Template.Shared's dependency tree
cp Server/Framework/Networking/Deterministic.GameFramework.Network/bin/Debug/net8.0/Deterministic.GameFramework.Network.dll "$LIB_DIR/" 2>/dev/null || true
cp Server/Framework/Networking/Deterministic.GameFramework.Network.LiteNetLib/bin/Debug/net8.0/*.dll "$LIB_DIR/" 2>/dev/null || true
cp Server/Framework/Profiler/Deterministic.GameFramework.Profiler/bin/Debug/net8.0/Deterministic.GameFramework.Profiler.dll "$LIB_DIR/" 2>/dev/null || true
cp Server/Template.Server/bin/Debug/net8.0/LiteNetLib.dll "$LIB_DIR/" 2>/dev/null || true

# Remove unnecessary files from publish
rm -f "$LIB_DIR"/*.pdb "$LIB_DIR"/*.deps.json "$LIB_DIR"/*.runtimeconfig.json 2>/dev/null

echo "Done. $(ls "$LIB_DIR"/*.dll 2>/dev/null | wc -l | tr -d ' ') DLLs synced."
echo "You can now open/build in Godot safely."
