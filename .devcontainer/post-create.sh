#!/usr/bin/env bash
set -euo pipefail

# The bind mount for /home/vscode/.local/share/opencode makes Docker
# create /home/vscode/.local as root-owned, so opencode fails with
# "EACCES: permission denied, mkdir '/home/vscode/.local/state'".
sudo mkdir -p /home/vscode/.local/state /home/vscode/.local/share/opencode
sudo chown -R vscode:vscode /home/vscode/.local

dotnet restore

cd src/Web
npm install

curl -fsSL https://opencode.ai/install | bash

