#!/bin/sh

# https://stackoverflow.com/a/246128/1083771
SCRIPT_DIR="$( cd -- "$( dirname -- "$0" )" &> /dev/null && pwd )"

for f in $SCRIPT_DIR/*.png
do
    fn=${f##*/}
    fn_webp=${fn%.png}.webp
    cwebp -mt -z 9 -o "$SCRIPT_DIR/../$fn_webp" "$f"
done

# assumption: all .gif files are animated
for f in $SCRIPT_DIR/*.gif
do
    fn=${f##*/}
    fn_webp=${fn%.gif}.webp
    gif2webp -mt -min_size -o "$SCRIPT_DIR/../$fn_webp" "$f"
done
