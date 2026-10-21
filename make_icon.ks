import "k:objk"

func hexByte(value) {
    let digits = "0123456789abcdef"
    let n = toInt(value)
    emit substring(digits, n / 16, n / 16 + 1) + substring(digits, n % 16, n % 16 + 1)
}

func hexU32BE(value) {
    let n = toInt(value)
    emit hexByte(n / 16777216) + hexByte((n / 65536) % 256) +
         hexByte((n / 256) % 256) + hexByte(n % 256)
}

func fileSize(path) {
    emit toInt(trim(exec("stat -f%z \"" + path + "\"")))
}

func appendIcnsChunk(output, kind, path) {
    let header = "" + kind + hexU32BE(fileSize(path) + 8)
    exec("printf '" + header + "' | xxd -r -p >> \"" + output + "\"")
    exec("cat \"" + path + "\" >> \"" + output + "\"")
}

func buildIcns(folder, output) {
    let p16 = folder + "/icon_16x16.png"
    let p32 = folder + "/icon_32x32.png"
    let p64 = folder + "/icon_32x32@2x.png"
    let p128 = folder + "/icon_128x128.png"
    let p256 = folder + "/icon_256x256.png"
    let p512 = folder + "/icon_512x512.png"
    let p1024 = folder + "/icon_512x512@2x.png"
    let total = 8 +
                8 + fileSize(p16) + 8 + fileSize(p32) + 8 + fileSize(p64) +
                8 + fileSize(p128) + 8 + fileSize(p256) + 8 + fileSize(p512) +
                8 + fileSize(p1024)
    exec("printf '69636e73" + hexU32BE(total) + "' | xxd -r -p > \"" + output + "\"")
    appendIcnsChunk(output, "69637034", p16)
    appendIcnsChunk(output, "69637035", p32)
    appendIcnsChunk(output, "69637036", p64)
    appendIcnsChunk(output, "69633037", p128)
    appendIcnsChunk(output, "69633038", p256)
    appendIcnsChunk(output, "69633039", p512)
    appendIcnsChunk(output, "69633130", p1024)
}

func renderIcon(folder, size, name) {
    let source = "assets/stem-brain.png"
    let output = folder + "/" + name
    exec("sips -z " + toString(size) + " " + toString(size) + " \"" + source + "\" --out \"" + output + "\" >/dev/null")
    if trim(exec("test -s \"" + output + "\" && echo yes")) != "yes" {
        kp("PNG resize failed: " + name)
        exit("1")
    }
}

just run {
    let temporary = trim(exec("mktemp -d /tmp/stem-icon.XXXXXX"))
    if temporary == "" { kp("Cannot create iconset")  exit("1") }
    let folder = temporary + ".iconset"
    exec("mv \"" + temporary + "\" \"" + folder + "\"")

    renderIcon(folder, 16, "icon_16x16.png")
    renderIcon(folder, 32, "icon_16x16@2x.png")
    renderIcon(folder, 32, "icon_32x32.png")
    renderIcon(folder, 64, "icon_32x32@2x.png")
    renderIcon(folder, 128, "icon_128x128.png")
    renderIcon(folder, 256, "icon_128x128@2x.png")
    renderIcon(folder, 256, "icon_256x256.png")
    renderIcon(folder, 512, "icon_256x256@2x.png")
    renderIcon(folder, 512, "icon_512x512.png")
    renderIcon(folder, 1024, "icon_512x512@2x.png")

    let output = temporary + ".icns"
    // macOS 27 iconutil rejects even iconsets it extracted itself. ICNS is a
    // small big-endian chunk container, so assemble it directly from our PNGs.
    buildIcns(folder, output)
    if trim(exec("test -s \"" + output + "\" && echo yes")) != "yes" {
        kp("ICNS assembly failed")
        exit("1")
    }
    exec("mv \"" + output + "\" stem.icns")
    exec("rm -rf \"" + folder + "\"")
    kp("built stem.icns")
}
