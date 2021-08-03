#!/usr/bin/env bash

set -euo pipefail

# variables
DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
OBJDIR="$DIR/obj"
global_json_path="$DIR/../global.json"
dotnet_install_path="$DIR/../.dotnet"
install_script_url="https://dot.net/v1/dotnet-install.sh"
install_script_path="$OBJDIR/dotnet-install.sh"

# functions
ensure_dir() {
    [ -d $1 ] || mkdir $1
}

# main

# resolve SDK version
sdk_version=$(jq -r .sdk.version $global_json_path)

# download dotnet-install.sh
ensure_dir $OBJDIR

echo "Downloading install script: $install_script_url => $install_script_path"
curl -sSL -o $install_script_path $install_script_url
chmod +x $install_script_path

# Install .NET Core 3.x SDK to run 3.x test targets
$install_script_path -v 3.1.300 -i $dotnet_install_path

# Install .NET 5 SDK to run 5.0 test targets
$install_script_path -v 5.0.302 -i $dotnet_install_path

# Install .NET version specified by global.json
$install_script_path -v $sdk_version -i $dotnet_install_path
