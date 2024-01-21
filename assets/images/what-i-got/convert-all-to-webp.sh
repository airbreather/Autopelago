#!/bin/sh

# https://stackoverflow.com/a/1482133/1083771
SCRIPT_DIR="$(dirname "$(readlink -f "$0")")"

for f in $SCRIPT_DIR/*.png
do
    fn=${f##*/}
    fn_webp=${fn%.png}.webp
    cwebp -mt -z 9 -o "$SCRIPT_DIR/../$fn_webp" "$f"
done

for f in $SCRIPT_DIR/*.gif
do
    fn=${f##*/}
    fn_webp=${fn%.gif}.webp
    gif2webp -mt -min_size -o "$SCRIPT_DIR/../$fn_webp" "$f"
done
