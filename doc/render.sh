#!/bin/sh

# https://stackoverflow.com/a/35374073/1083771
SCRIPT_DIR="$(dirname "$(realpath "$0")")"

dot -Tpng -O "$SCRIPT_DIR/cool-world.dot"
dot -Tsvg -O "$SCRIPT_DIR/simple-progression.dot"
pushd "$SCRIPT_DIR/../tools/FixupSimpleProgressionSvg"
dotnet run -c Release -- 0 "$SCRIPT_DIR/simple-progression.dot.svg" "$SCRIPT_DIR/../assets/images/simple-progression.dot.svg"
dotnet run -c Release -- 1 "$SCRIPT_DIR/simple-progression.dot.svg" "$SCRIPT_DIR/simple-progression.for-tracker.svg"
dotnet run -c Release -- 2 "$SCRIPT_DIR/simple-progression.dot.svg" "$SCRIPT_DIR/simple-progression.dot.svg"
popd
