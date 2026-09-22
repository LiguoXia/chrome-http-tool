# -*- coding: utf-8 -*-
# 从 app-icon/icon-1024.png 生成全套图标资源：
# 1) 透明化 1024（白色主体保留、squircle 圆角外透明）-> desktop/app-icon.png
# 2) 多尺寸 ICO -> desktop/app.ico
# 3) favicon 32px PNG base64 -> .favicon.txt（供嵌入 index.html）
import os, base64
from PIL import Image, ImageDraw, ImageFilter

BASE = r"F:\soft\chrome-http-tool"
SRC = os.path.join(BASE, "app-icon", "icon-1024.png")
OUT_PNG = os.path.join(BASE, "desktop", "app-icon.png")
OUT_ICO = os.path.join(BASE, "desktop", "app.ico")
OUT_FAV = os.path.join(BASE, ".favicon.txt")

img = Image.open(SRC).convert("RGBA")
S = img.size[0]

# squircle 遮罩（苹果超椭圆 n=5），inset 6px，窄过渡带
n = 5.0
inset = 6.0
a = b = S / 2.0 - inset
cx = cy = S / 2.0
mask = Image.new("L", (S, S), 0)
mp = mask.load()
for y in range(S):
    fy = abs((y + 0.5 - cy) / b)
    for x in range(S):
        fx = abs((x + 0.5 - cx) / a)
        v = (fx ** n + fy ** n) ** (1.0 / n)
        if v <= 1.0:
            mp[x, y] = 255
        elif v < 1.012:
            mp[x, y] = max(0, int(255 * (1.0 - (v - 1.0) / 0.012)))

out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
out.paste(img, (0, 0), mask)
out.save(OUT_PNG)
print("app-icon.png saved:", out.size)

# 多尺寸 ICO
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
out.save(OUT_ICO, format="ICO", sizes=[(s, s) for s in sizes])
print("app.ico saved:", os.path.getsize(OUT_ICO), "bytes")

# favicon 32px base64
fav = out.resize((32, 32), Image.LANCZOS)
buf = base64.b64encode(fav.tobytes())
import io
bio = io.BytesIO()
fav.save(bio, format="PNG")
b64 = base64.b64encode(bio.getvalue()).decode("ascii")
open(OUT_FAV, "w", encoding="utf-8").write(b64)
print("favicon base64 length:", len(b64))
