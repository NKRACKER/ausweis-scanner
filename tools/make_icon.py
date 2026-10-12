from PIL import Image, ImageDraw, ImageFilter
import math, sys
S = 1024
def rr(d, box, r, fill): d.rounded_rectangle(box, radius=r, fill=fill)
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
# Hintergrund: abgerundetes Quadrat mit Verlauf
grad = Image.new("RGBA", (S, S))
gd = ImageDraw.Draw(grad)
for y in range(S):
    t = y / S
    gd.line([(0, y), (S, y)], fill=(int(59 + (29 - 59) * t), int(130 + (78 - 130) * t), int(246 + (216 - 246) * t), 255))
mask = Image.new("L", (S, S), 0); rr(ImageDraw.Draw(mask), (24, 24, S - 24, S - 24), 230, 255)
img.paste(grad, (0, 0), mask)
# Karte (leicht gedreht) mit Schatten
def card_layer():
    c = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(c)
    x0, y0, w, h = 512 - 350, 512 - 225, 700, 450
    rr(d, (x0, y0, x0 + w, y0 + h), 56, (255, 255, 255, 255))
    rr(d, (x0 + 46, y0 + 70, x0 + 266, y0 + 380), 28, (191, 207, 238, 255))              # Foto
    d.ellipse((x0 + 106, y0 + 110, x0 + 206, y0 + 215), fill=(96, 117, 168, 255))        # Kopf
    d.ellipse((x0 + 76, y0 + 235, x0 + 236, y0 + 420), fill=(96, 117, 168, 255))         # Schultern
    rr(d, (x0 + 306, y0 + 96, x0 + 640, y0 + 136), 20, (45, 62, 90, 255))                # Name
    rr(d, (x0 + 306, y0 + 176, x0 + 580, y0 + 206), 15, (176, 189, 210, 255))
    rr(d, (x0 + 306, y0 + 236, x0 + 520, y0 + 266), 15, (176, 189, 210, 255))
    return c.rotate(9, resample=Image.BICUBIC, center=(512, 512))
card = card_layer()
shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0)); sm = card.split()[3].point(lambda a: int(a * 0.45))
shadow.paste((10, 25, 70, 255), (0, 0), sm); shadow = shadow.filter(ImageFilter.GaussianBlur(22)); shadow = shadow.transform((S, S), Image.AFFINE, (1, 0, -10, 0, 1, -22))
img = Image.alpha_composite(img, shadow); img = Image.alpha_composite(img, card)
# Scan-Strahl
beam = Image.new("RGBA", (S, S), (0, 0, 0, 0)); bd = ImageDraw.Draw(beam)
rr(bd, (110, 585, S - 110, 625), 20, (52, 211, 153, 255))
glow = beam.filter(ImageFilter.GaussianBlur(28)); img = Image.alpha_composite(img, glow); img = Image.alpha_composite(img, glow)
core = Image.new("RGBA", (S, S), (0, 0, 0, 0)); rr(ImageDraw.Draw(core), (130, 596, S - 130, 614), 9, (236, 255, 247, 255))
img = Image.alpha_composite(img, beam); img = Image.alpha_composite(img, core)
img.resize((256, 256), Image.LANCZOS).save(sys.argv[1] + ".png")
img.save(sys.argv[1], format="ICO", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
