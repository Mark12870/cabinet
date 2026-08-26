set -eu

bsdtar -xOf "$CABINET_ARCHIVE" data.tar.xz \
    | bsdtar -xf - -C "$CABINET_WORK"

bundle="$CABINET_WORK/usr/lib/lv2/drumgizmo.lv2"

if [ ! -f "$bundle/drumgizmo.so" ] || [ ! -f "$bundle/manifest.ttl" ]; then
    echo "$CABINET_NAME's Debian package contains no complete LV2 bundle" >&2
    exit 1
fi

cp -a "$bundle" "$CABINET_DEST/"
