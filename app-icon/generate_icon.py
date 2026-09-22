# -*- coding: utf-8 -*-
"""Generate an Apple-style SVG icon for an HTTP desktop tool."""
import math

CX, CY = 512, 526          # globe center
EARTH_R = 205              # globe radius
RING_R = 290               # request-loop radius
RING_W = 34                # loop stroke width
GAP = 48                   # gap (deg) centered at top


def pt(theta_deg, r=RING_R):
    t = math.radians(theta_deg)
    return CX + r * math.cos(t), CY + r * math.sin(t)


def arrowhead(p, dir_deg, length=52, half_w=28):
    """Arrowhead whose base is centered exactly on ring endpoint p, tip
    pointing forward along dir_deg; wide enough to swallow the round cap."""
    t = math.radians(dir_deg)
    dx, dy = math.cos(t), math.sin(t)
    px, py = -dy, dx                       # perpendicular
    tip = (p[0] + length * dx, p[1] + length * dy)
    b1 = (p[0] + half_w * px, p[1] + half_w * py)
    b2 = (p[0] - half_w * px, p[1] - half_w * py)
    return (f"M {tip[0]:.1f} {tip[1]:.1f} "
            f"L {b1[0]:.1f} {b1[1]:.1f} L {b2[0]:.1f} {b2[1]:.1f} Z")


# --- single-arrow "send / refresh" loop, gap sits at the upper-right so the
#     arrow at the top points outward along clockwise travel (like Safari reload)
gap_center = 318                         # gap center (deg)
a0 = gap_center - GAP / 2                # arrow endpoint
a1 = gap_center + GAP / 2                # round-cap start point
p_start = pt(a1)
p_end = pt(a0)
arc = (f"M {p_start[0]:.1f} {p_start[1]:.1f} "
       f"A {RING_R} {RING_R} 0 1 1 {p_end[0]:.1f} {p_end[1]:.1f}")

# clockwise (sweep=1): tangent = theta + 90
ah_end = arrowhead(p_end, (a0 + 90) % 360)

# --- globe grid (orthographic front view): straight equator & prime meridian,
#     one pair of side meridians and two latitude ellipses, clipped to sphere ---
grid = (
    f'<line x1="{CX}" y1="{CY-EARTH_R}" x2="{CX}" y2="{CY+EARTH_R}"/>'
    f'<line x1="{CX-EARTH_R}" y1="{CY}" x2="{CX+EARTH_R}" y2="{CY}"/>'
    f'<ellipse cx="{CX}" cy="{CY}" rx="100" ry="{EARTH_R}"/>'
    f'<ellipse cx="{CX}" cy="{CY-88}" rx="160" ry="42"/>'
    f'<ellipse cx="{CX}" cy="{CY+88}" rx="160" ry="42"/>')

svg = f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024" width="1024" height="1024">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="0.25" y2="1">
      <stop offset="0" stop-color="#56A6FF"/>
      <stop offset="0.52" stop-color="#2F83F8"/>
      <stop offset="1" stop-color="#1259E6"/>
    </linearGradient>
    <linearGradient id="sheen" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#FFFFFF" stop-opacity="0.20"/>
      <stop offset="0.5" stop-color="#FFFFFF" stop-opacity="0.05"/>
      <stop offset="1" stop-color="#FFFFFF" stop-opacity="0"/>
    </linearGradient>
    <radialGradient id="globe" cx="0.38" cy="0.30" r="0.95">
      <stop offset="0" stop-color="#FFFFFF"/>
      <stop offset="0.7" stop-color="#F2F8FF"/>
      <stop offset="1" stop-color="#D9E8FF"/>
    </radialGradient>
    <clipPath id="sphereClip">
      <circle cx="{CX}" cy="{CY}" r="{EARTH_R}"/>
    </clipPath>
    <filter id="globeShadow" x="-30%" y="-30%" width="160%" height="160%">
      <feDropShadow dx="0" dy="18" stdDeviation="22" flood-color="#08357F" flood-opacity="0.38"/>
    </filter>
    <filter id="ringShadow" x="-20%" y="-20%" width="140%" height="140%">
      <feDropShadow dx="0" dy="10" stdDeviation="12" flood-color="#08357F" flood-opacity="0.30"/>
    </filter>
  </defs>

  <!-- squircle base -->
  <rect x="64" y="64" width="896" height="896" rx="204" fill="url(#bg)"/>
  <rect x="64" y="64" width="896" height="896" rx="204" fill="url(#sheen)"/>
  <rect x="66.5" y="66.5" width="891" height="891" rx="202" fill="none"
        stroke="#FFFFFF" stroke-opacity="0.22" stroke-width="3"/>

  <!-- globe -->
  <g filter="url(#globeShadow)">
    <circle cx="{CX}" cy="{CY}" r="{EARTH_R}" fill="url(#globe)"
            stroke="#BBD6FF" stroke-width="4"/>
  </g>
  <g clip-path="url(#sphereClip)" fill="none" stroke="#3E86F5"
     stroke-width="9" stroke-opacity="0.48" stroke-linecap="round">
    {grid}
  </g>

  <!-- request / response loop -->
  <g filter="url(#ringShadow)">
    <path d="{arc}" fill="none" stroke="#FFFFFF" stroke-width="{RING_W}"
          stroke-linecap="round"/>
    <path d="{ah_end}" fill="#FFFFFF"/>
  </g>
</svg>
'''

with open("http-tool-icon.svg", "w", encoding="utf-8") as f:
    f.write(svg)
print("written http-tool-icon.svg")
