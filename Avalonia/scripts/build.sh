#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/.."

echo "Building GitRunnerManager..."

cd "$PROJECT_DIR"

dotnet restore GitRunnerManager.sln

dotnet build GitRunnerManager.sln -c Release

echo "Build completed successfully."