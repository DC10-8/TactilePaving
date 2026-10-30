#!/bin/sh
# Mono (mcs) でビルドする。ゲームの Managed フォルダを引数か GAME_MANAGED で指定する
# 例: ./build.sh "/path/to/Cities_Skylines/Cities_Data/Managed"
# 出力: bin/TactilePaving.dll, bin/UnifiedUILib.dll, bin/PreviewImage.png
set -e
cd "$(dirname "$0")"
GAME_MANAGED="${1:-$GAME_MANAGED}"
if [ -z "$GAME_MANAGED" ]; then
    echo "ゲームの Managed フォルダを指定してください / Specify the game's Managed folder" >&2
    exit 1
fi
UUI=ThirdParty/UnifiedUILib
mkdir -p bin
mcs -target:library -sdk:2 -optimize+ -out:bin/TactilePaving.dll \
    -r:"$GAME_MANAGED/Assembly-CSharp.dll" \
    -r:"$GAME_MANAGED/ColossalManaged.dll" \
    -r:"$GAME_MANAGED/ICities.dll" \
    -r:"$GAME_MANAGED/UnityEngine.dll" \
    -r:$UUI/UnifiedUILib.dll \
    -recurse:'*.cs'
cp $UUI/UnifiedUILib.dll PreviewImage.png bin/
echo "ビルド完了 / Build finished: bin/TactilePaving.dll"
