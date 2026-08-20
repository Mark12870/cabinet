work="$CABINET_WORK/unpacked"
mkdir -p "$work"
tar -xf "$CABINET_ARCHIVE" -C "$work"

release=$(find "$work" -mindepth 1 -maxdepth 1 -type d)
product=$(basename "$(find "$release" -mindepth 1 -maxdepth 1 -type d)")
shipped="$release/$product"
binary="$shipped/$product.64.so"

if [ "$(basename "$CABINET_DATA")" != "$product" ]; then
    echo "$CABINET_NAME ships $product, which is not what Data: names" >&2
    exit 1
fi

if [ ! -f "$binary" ]; then
    echo "$CABINET_NAME's archive holds no $product.64.so" >&2
    exit 1
fi

contents="$CABINET_DEST/$product.vst3/Contents"
mkdir -p "$contents/x86_64-linux" "$contents/Resources/Documentation"
mv "$binary" "$contents/x86_64-linux/$product.so"
echo "  $product.vst3"

for document in "$shipped"/*.pdf; do
    if [ -f "$document" ]; then
        mv "$document" "$contents/Resources/Documentation/"
    fi
done

if grep -qa clap_entry "$contents/x86_64-linux/$product.so"; then
    ln -s "$product.vst3/Contents/x86_64-linux/$product.so" "$CABINET_DEST/$product.clap"
    echo "  $product.clap"
fi

rm -f "$shipped/dialog" "$shipped/dialog.32" "$shipped/dialog.64"
mv "$shipped"/* "$CABINET_DATA/"
