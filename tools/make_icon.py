"""Genera los iconos del lanzador (mipmap-*/ic_launcher.png). Uso: python3 make_icon.py RES_DIR"""
import os, sys
from PIL import Image, ImageDraw

SIZES = {"mdpi": 48, "hdpi": 72, "xhdpi": 96, "xxhdpi": 144, "xxxhdpi": 192}


def draw(size=768):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    # fondo degradado redondeado
    grad = Image.new("RGBA", (size, size))
    gd = ImageDraw.Draw(grad)
    top, bot = (124, 92, 255), (63, 47, 176)
    for y in range(size):
        t = y / (size - 1)
        c = tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3)) + (255,)
        gd.line([(0, y), (size, y)], fill=c)
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([size * 0.04, size * 0.04, size * 0.96, size * 0.96], radius=size * 0.22, fill=255)
    img.paste(grad, (0, 0), mask)
    d = ImageDraw.Draw(img)
    s = size / 768.0
    # barras tipo "piano roll" / MIDI
    bars = [(150, 470, 250, 510), (270, 400, 390, 440), (410, 330, 530, 370), (550, 260, 640, 300)]
    for x0, y0, x1, y1 in bars:
        d.rounded_rectangle([x0 * s, y0 * s, x1 * s, y1 * s], radius=12 * s, fill=(255, 255, 255, 90))
    # corchea blanca
    white = (255, 255, 255, 255)
    d.ellipse([230 * s, 470 * s, 380 * s, 580 * s], fill=white)           # cabeza 1
    d.ellipse([440 * s, 420 * s, 590 * s, 530 * s], fill=white)           # cabeza 2
    d.rectangle([348 * s, 190 * s, 378 * s, 525 * s], fill=white)          # plica 1
    d.rectangle([558 * s, 140 * s, 588 * s, 475 * s], fill=white)          # plica 2
    d.polygon([(348 * s, 190 * s), (588 * s, 140 * s), (588 * s, 215 * s), (348 * s, 265 * s)], fill=white)  # barra
    return img


def main(res):
    big = draw()
    for name, px in SIZES.items():
        d = os.path.join(res, "mipmap-" + name)
        os.makedirs(d, exist_ok=True)
        big.resize((px, px), Image.LANCZOS).save(os.path.join(d, "ic_launcher.png"))
    big.resize((512, 512), Image.LANCZOS).save(os.path.join(os.path.dirname(res.rstrip("/")), "icon512.png"))


if __name__ == "__main__":
    main(sys.argv[1])
