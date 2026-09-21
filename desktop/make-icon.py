# -*- coding: utf-8 -*-
# 应用图标生成：开源图标（Carbon "api"，Apache-2.0 许可）+ 苹果风合成
# 支持 M/L/H/V/Z/C/Q/A 的 SVG path 解析 -> Pillow 超采样绘制 -> 1024 图标 -> 多尺寸 ICO
# 全透明画布绘制，无白底抠图，边缘硬裁剪防白边
import os, re, math
from PIL import Image, ImageDraw, ImageFilter

BASE = r"F:\soft\chrome-http-tool\desktop"
OUT_PNG = os.path.join(BASE, "app-icon.png")
OUT_ICO = os.path.join(BASE, "app.ico")

SVG_PATH = ("M26 22a3.86 3.86 0 0 0-2 .57l-3.09-3.1a6 6 0 0 0 0-6.94L24 9.43a3.86 3.86 0 0 0 2 .57a4 4 0 1 0-4-4a3.86 3.86 0 0 0 .57 2l-3.1 3.09a6 6 0 0 0-6.94 0L9.43 8A3.86 3.86 0 0 0 10 6a4 4 0 1 0-4 4a3.86 3.86 0 0 0 2-.57l3.09 3.1a6 6 0 0 0 0 6.94L8 22.57A3.86 3.86 0 0 0 6 22a4 4 0 1 0 4 4a3.86 3.86 0 0 0-.57-2l3.1-3.09a6 6 0 0 0 6.94 0l3.1 3.09a3.86 3.86 0 0 0-.57 2a4 4 0 1 0 4-4m0-18a2 2 0 1 1-2 2a2 2 0 0 1 2-2M4 6a2 2 0 1 1 2 2a2 2 0 0 1-2-2m2 22a2 2 0 1 1 2-2a2 2 0 0 1-2 2m10-8a4 4 0 1 1 4-4a4 4 0 0 1-4 4m10 8a2 2 0 1 1 2-2a2 2 0 0 1 2-2")
VIEWBOX = 32.0
NUM = re.compile(r"^-?[\d.]")

def arc_points(x0, y0, rx, ry, phi_deg, laf, sf, x1, y1, steps=28):
    """SVG 弧线端点参数化 -> 采样点列（W3C 规范实现）"""
    phi = math.radians(phi_deg)
    rx, ry = abs(rx), abs(ry)
    if rx == 0 or ry == 0:
        return [(x1, y1)]
    dx2, dy2 = (x0 - x1) / 2.0, (y0 - y1) / 2.0
    x1p = math.cos(phi) * dx2 + math.sin(phi) * dy2
    y1p = -math.sin(phi) * dx2 + math.cos(phi) * dy2
    lam = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry)
    if lam > 1:
        s = math.sqrt(lam)
        rx, ry = rx * s, ry * s
    num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p
    den = rx * rx * y1p * y1p + ry * ry * x1p * x1p
    coef = 0.0 if den == 0 else math.sqrt(max(0.0, num / den))
    if laf == sf:
        coef = -coef
    cxp = coef * (rx * y1p / ry)
    cyp = coef * (-ry * x1p / rx)
    cx = math.cos(phi) * cxp - math.sin(phi) * cyp + (x0 + x1) / 2.0
    cy = math.sin(phi) * cxp + math.cos(phi) * cyp + (y0 + y1) / 2.0

    def ang(ux, uy, vx, vy):
        dot = ux * vx + uy * vy
        ln = math.sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy))
        a = math.acos(max(-1.0, min(1.0, dot / ln if ln else 0.0)))
        return -a if ux * vy - uy * vx < 0 else a

    th1 = ang(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry)
    dth = ang((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry)
    if not sf and dth > 0:
        dth -= 2 * math.pi
    elif sf and dth < 0:
        dth += 2 * math.pi
    pts = []
    for i in range(1, steps + 1):
        th = th1 + dth * i / steps
        pts.append((cx + rx * math.cos(th) * math.cos(phi) - ry * math.sin(th) * math.sin(phi),
                    cy + rx * math.cos(th) * math.sin(phi) + ry * math.sin(th) * math.cos(phi)))
    return pts

def parse_path(d):
    tokens = re.findall(r"[MmLlHhVvZzCcQqAa]|-?\d*\.?\d+(?:[eE][+-]?\d+)?", d)
    subs, cur, x, y, sx, sy = [], [], 0.0, 0.0, 0.0, 0.0
    i, cmd = 0, None
    def close_sub():
        nonlocal cur
        if len(cur) >= 3:
            subs.append(cur)
        cur = []
    while i < len(tokens):
        t = tokens[i]
        if re.match(r"^[MmLlHhVvZzCcQqAa]$", t):
            cmd = t
            i += 1
            continue
        num = float(t)
        def take(n):
            nonlocal i
            nums = [num]
            while len(nums) < n and i + 1 < len(tokens) and NUM.match(tokens[i + 1]):
                nums.append(float(tokens[i + 1]))
                i += 1
            i += 1
            return nums
        if cmd in "Mm":
            if i + 1 < len(tokens) and NUM.match(tokens[i + 1]):
                px, py = num, float(tokens[i + 1])
                i += 2
            else:
                break
            x, y = (x + px, y + py) if cmd == "m" else (px, py)
            close_sub()
            cur = [(x, y)]
            sx, sy = x, y
        elif cmd in "Ll":
            if i + 1 < len(tokens) and NUM.match(tokens[i + 1]):
                px, py = num, float(tokens[i + 1])
                i += 2
            else:
                break
            x = x + px if cmd == "l" else px
            y = y + py if cmd == "l" else py
            cur.append((x, y))
        elif cmd in "Hh":
            x = x + num if cmd == "h" else num
            cur.append((x, y))
            i += 1
        elif cmd in "Vv":
            y = y + num if cmd == "v" else num
            cur.append((x, y))
            i += 1
        elif cmd in "Cc":
            nums = take(6)
            if len(nums) < 6:
                break
            rel = cmd == "c"
            x1, y1 = (x + nums[0], y + nums[1]) if rel else (nums[0], nums[1])
            x2, y2 = (x + nums[2], y + nums[3]) if rel else (nums[2], nums[3])
            x3, y3 = (x + nums[4], y + nums[5]) if rel else (nums[4], nums[5])
            for k in range(1, 17):
                t2 = k / 16.0
                u = 1 - t2
                cur.append((u**3*x + 3*u*u*t2*x1 + 3*u*t2*t2*x2 + t2**3*x3,
                            u**3*y + 3*u*u*t2*y1 + 3*u*t2*t2*y2 + t2**3*y3))
            x, y = x3, y3
        elif cmd in "Qq":
            nums = take(4)
            if len(nums) < 4:
                break
            rel = cmd == "q"
            x1, y1 = (x + nums[0], y + nums[1]) if rel else (nums[0], nums[1])
            x2, y2 = (x + nums[2], y + nums[3]) if rel else (nums[2], nums[3])
            for k in range(1, 17):
                t2 = k / 16.0
                u = 1 - t2
                cur.append((u*u*x + 2*u*t2*x1 + t2*t2*x2,
                            u*u*y + 2*u*t2*y1 + t2*t2*y2))
            x, y = x2, y2
        elif cmd in "Aa":
            nums = take(7)
            if len(nums) < 7:
                break
            rel = cmd == "a"
            rx, ry, phi = nums[0], nums[1], nums[2]
            laf, sf = int(nums[3]), int(nums[4])
            x2, y2 = (x + nums[5], y + nums[6]) if rel else (nums[5], nums[6])
            for p in arc_points(x, y, rx, ry, phi, laf, sf, x2, y2):
                cur.append(p)
            x, y = x2, y2
        elif cmd in "Zz":
            if cur and (abs(cur[-1][0] - sx) > 1e-6 or abs(cur[-1][1] - sy) > 1e-6):
                cur.append((sx, sy))
            close_sub()
            x, y = sx, sy
            i += 1
    close_sub()
    return subs

def render_glyph(subs, size, vb, ss=4):
    big = size * ss
    scale = big / vb
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for sub in subs:
        pts = [(x * scale, y * scale) for x, y in sub]
        d.polygon(pts, fill=(255, 255, 255, 255))
    return img.resize((size, size), Image.LANCZOS)

subs = parse_path(SVG_PATH)
print("子路径:", len(subs), "点数:", sum(len(s) for s in subs))

S = 1024
# 蓝色渐变（#0A84FF -> #0055D4），画布全透明
grad = Image.new("RGB", (S, S))
gp = grad.load()
for y in range(S):
    t = y / (S - 1)
    r = int(0x0A + (0x00 - 0x0A) * t)
    g = int(0x84 + (0x55 - 0x84) * t)
    b = int(0xFF + (0xD4 - 0xFF) * t)
    for x in range(S):
        gp[x, y] = (r, g, b)

# 超椭圆 squircle 遮罩：内缩 6px + 硬边缘（v>1 直接 alpha=0，杜绝白边）
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

base = Image.new("RGBA", (S, S), (0, 0, 0, 0))
base.paste(grad, (0, 0), mask)

# 顶部柔光（遮罩裁剪）
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
gd = ImageDraw.Draw(glow)
gd.ellipse([-220, -320, 760, 420], fill=(255, 255, 255, 56))
glow = glow.filter(ImageFilter.GaussianBlur(120))
glow_clip = Image.new("RGBA", (S, S), (0, 0, 0, 0))
glow_clip.paste(glow, (0, 0), mask)
base = Image.alpha_composite(base, glow_clip)

# 白色图形居中（carbon api 节点图）
gs = int(S * 0.52)
glyph = render_glyph(subs, gs, VIEWBOX)
gx = (S - gs) // 2
gy = (S - gs) // 2
base.paste(glyph, (gx, gy), glyph)

base.save(OUT_PNG)
print("icon:", OUT_PNG, base.size)

sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
base.save(OUT_ICO, format="ICO", sizes=[(s, s) for s in sizes])
print("ico:", OUT_ICO, os.path.getsize(OUT_ICO), "bytes")
