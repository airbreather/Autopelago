#!/bin/bash

# https://stackoverflow.com/a/35374073/1083771
SCRIPT_DIR="$(dirname "$(realpath "$0")")"
mkdir "$SCRIPT_DIR/../Rats"
mkdir "$SCRIPT_DIR/../Locations"

for f in $SCRIPT_DIR/*.svg
do
    chmod -x "$f"
    fn=${f##*/}
    cp "$f" "$SCRIPT_DIR/../$fn"
done

for f in $SCRIPT_DIR/*.piskel
do
    chmod -x "$f"
done

for f in $SCRIPT_DIR/*.png
do
    chmod -x "$f"
    fn=${f##*/}
    fn_webp=${fn%.png}.webp
    cwebp -mt -z 9 -o "$SCRIPT_DIR/../$fn_webp" "$f" &
done

for f in $SCRIPT_DIR/Rats/*.png
do
    chmod -x "$f"
    fn=${f##*/}
    fn_webp=${fn%.png}.webp
    cwebp -mt -z 9 -o "$SCRIPT_DIR/../Rats/$fn_webp" "$f" &
done

for f in $SCRIPT_DIR/Locations/*.gif
do
    chmod -x "$f"
    fn=${f##*/}
    fn_webp=${fn%.gif}.webp
    gif2webp -mt -min_size -o "$SCRIPT_DIR/../Locations/$fn_webp" "$f" &
done

wait < <(jobs -p)
