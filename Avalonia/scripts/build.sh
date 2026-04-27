#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/.."

echo "Building GitHubRunnerTray..."

cd "$PROJECT_DIR"

dotnet restore GitHubRunnerTray.sln

dotnet build GitHubRunnerTray.sln -c Release

echo "Build completed successfully."