import os
import numpy as np
from PIL import Image, ImageDraw

# 6x9 pixel grid for each character (w is 8 cells wide, so that its three strokes fit):
# 3: Highlight, 2: Bright tone, 1: Mid tone, ' ': Empty

N = [
    '33  33',
    '333 33',
    '323 32',
    '222222',
    '222222',
    '12 212',
    '11 111',
    '11  11',
    '11  11'
]

W = [
    '        ',
    '        ',
    '        ',
    '22    22',
    '22    22',
    '12 21 12',
    '11 11 11',
    '11111111',
    ' 11  11 '
]

C = [
    '      ',
    '      ',
    '      ',
    ' 2222 ',
    '222222',
    '12    ',
    '11    ',
    '111111',
    ' 1111 '
]

I = [
    '333333',
    '333333',
    '  32  ',
    '  22  ',
    '  22  ',
    '  12  ',
    '  11  ',
    '111111',
    '111111'
]

O = [
    ' 3333 ',
    '333333',
    '32  32',
    '22  22',
    '22  22',
    '12  12',
    '11  11',
    '111111',
    ' 1111 '
]

# Color palettes:
# Nwc: Navisworks Emerald Green
pal_nwc = {
    0: '#052E16', # Deep shadow
    1: '#15803D', # Mid tone
    2: '#22C55E', # Bright tone
    3: '#86EFAC', # Highlight
}

# IO: Electric Royal Blue / Azure (UNIQUE for NwcIO!)
pal_io = {
    0: '#0B1B3D', # Deep shadow
    1: '#1D4ED8', # Mid tone
    2: '#3B82F6', # Bright tone
    3: '#93C5FD', # Highlight
}

def build_svg(width, height, rx, block_size, base_xs, start_y, shadow_offset, letters, title):
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}" width="{width}" height="{height}" role="img"><title>{title}</title><rect width="{width}" height="{height}" rx="{rx}" fill="#12161C"/><g shape-rendering="crispEdges">']
    
    # Render in two letter groups: NWC, then IO
    for group_name, letter_indices, pal in [('nwc', [0, 1, 2], pal_nwc), ('io', [3, 4], pal_io)]:
        # Shadow layers (6 extrusion steps)
        shadow_rects = []
        for idx in letter_indices:
            grid = letters[idx]
            base_x = base_xs[idx]
            for step in range(1, 7):
                ox = base_x + step * shadow_offset
                oy = start_y + step * shadow_offset
                for r in range(9):
                    c = 0
                    while c < len(grid[r]):
                        if grid[r][c] != ' ':
                            start_c = c
                            while c < len(grid[r]) and grid[r][c] != ' ':
                                c += 1
                            span_w = (c - start_c) * block_size
                            x = ox + start_c * block_size
                            y = oy + r * block_size
                            
                            x_str = f"{x:.1f}".rstrip('0').rstrip('.') if isinstance(x, float) else str(x)
                            y_str = f"{y:.1f}".rstrip('0').rstrip('.') if isinstance(y, float) else str(y)
                            w_str = f"{span_w:.1f}".rstrip('0').rstrip('.') if isinstance(span_w, float) else str(span_w)
                            h_str = f"{block_size:.1f}".rstrip('0').rstrip('.') if isinstance(block_size, float) else str(block_size)
                            shadow_rects.append(f"M{x_str} {y_str}h{w_str}v{h_str}h-{w_str}z")
                        else:
                            c += 1
        out.append(f'<path fill="{pal[0]}" d="{"".join(shadow_rects)}"/>')

        for color_val in [3, 2, 1]:
            face_rects = []
            for idx in letter_indices:
                grid = letters[idx]
                base_x = base_xs[idx]
                ox = base_x
                oy = start_y
                for r in range(9):
                    c = 0
                    while c < len(grid[r]):
                        if grid[r][c] == str(color_val):
                            start_c = c
                            while c < len(grid[r]) and grid[r][c] == str(color_val):
                                c += 1
                            span_w = (c - start_c) * block_size
                            x = ox + start_c * block_size
                            y = oy + r * block_size
                            
                            x_str = f"{x:.1f}".rstrip('0').rstrip('.') if isinstance(x, float) else str(x)
                            y_str = f"{y:.1f}".rstrip('0').rstrip('.') if isinstance(y, float) else str(y)
                            w_str = f"{span_w:.1f}".rstrip('0').rstrip('.') if isinstance(span_w, float) else str(span_w)
                            h_str = f"{block_size:.1f}".rstrip('0').rstrip('.') if isinstance(block_size, float) else str(block_size)
                            face_rects.append(f"M{x_str} {y_str}h{w_str}v{h_str}h-{w_str}z")
                        else:
                            c += 1
            if face_rects:
                out.append(f'<path fill="{pal[color_val]}" d="{"".join(face_rects)}"/>')

    out.append('</g></svg>')
    return "".join(out)

def render_png(width, height, rx, block_size, base_xs, start_y, shadow_offset, letters, scale=1, alpha_bg=False):
    sw = int(width * scale)
    sh = int(height * scale)
    mode = 'RGBA' if alpha_bg else 'RGB'
    bg = (0, 0, 0, 0) if alpha_bg else '#12161C'
    img = Image.new(mode, (sw, sh), bg)
    draw = ImageDraw.Draw(img)

    if alpha_bg:
        draw.rounded_rectangle([0, 0, sw - 1, sh - 1], radius=int(rx * scale), fill='#12161C')

    # Draw shadow
    for group_name, letter_indices, pal in [('nwc', [0, 1, 2], pal_nwc), ('io', [3, 4], pal_io)]:
        shadow_color = pal[0]
        for idx in letter_indices:
            grid = letters[idx]
            base_x = base_xs[idx]
            for step in range(1, 7):
                ox = (base_x + step * shadow_offset) * scale
                oy = (start_y + step * shadow_offset) * scale
                for r in range(9):
                    for c in range(len(grid[r])):
                        if grid[r][c] != ' ':
                            x1 = int(ox + c * block_size * scale)
                            y1 = int(oy + r * block_size * scale)
                            x2 = int(ox + (c + 1) * block_size * scale - 1)
                            y2 = int(oy + (r + 1) * block_size * scale - 1)
                            draw.rectangle([x1, y1, x2, y2], fill=shadow_color)

    # Draw faces
    for group_name, letter_indices, pal in [('nwc', [0, 1, 2], pal_nwc), ('io', [3, 4], pal_io)]:
        for idx in letter_indices:
            grid = letters[idx]
            base_x = base_xs[idx]
            ox = base_x * scale
            oy = start_y * scale
            for r in range(9):
                for c in range(len(grid[r])):
                    val = grid[r][c]
                    if val != ' ':
                        color = pal[int(val)]
                        x1 = int(ox + c * block_size * scale)
                        y1 = int(oy + r * block_size * scale)
                        x2 = int(ox + (c + 1) * block_size * scale - 1)
                        y2 = int(oy + (r + 1) * block_size * scale - 1)
                        draw.rectangle([x1, y1, x2, y2], fill=color)

    return img

def render_canonical_icon():
    # Remap RvtIO icon directly to ensure exact 100% parity with RvtIO's glyph height, font, and bounds
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    rvt_icon_path = os.environ.get('RVT_ICON_PATH') or os.path.abspath(os.path.join(root, '..', 'RvtIO', 'src', 'RvtIO', 'icon.png'))
    if not os.path.exists(rvt_icon_path):
        rvt_icon_path = os.path.abspath(os.path.join(root, '..', 'mahach666', 'RvtIO', 'src', 'RvtIO', 'icon.png'))
    if not os.path.exists(rvt_icon_path):
        rvt_icon_path = os.path.join(root, 'src', 'NwcIO', 'icon.png')
    im_rvt = Image.open(rvt_icon_path)
    arr = np.array(im_rvt, dtype=np.float64)

    c_bg = np.array([18.0, 22.0, 28.0])
    c_0  = np.array([122.0, 74.0, 18.0])  # shadow
    c_1  = np.array([201.0, 122.0, 43.0]) # mid
    c_2  = np.array([242.0, 163.0, 58.0]) # bright
    c_3  = np.array([255.0, 216.0, 74.0]) # highlight
    bases_rvt = [c_bg, c_0, c_1, c_2, c_3]

    bases_target = [
        np.array([18.0, 22.0, 28.0]),   # bg
        np.array([11.0, 27.0, 61.0]),   # shadow #0B1B3D
        np.array([29.0, 78.0, 216.0]),  # mid #1D4ED8
        np.array([59.0, 130.0, 246.0]), # bright #3B82F6
        np.array([147.0, 197.0, 253.0]) # highlight #93C5FD
    ]

    def decompose(rgb):
        for i, b in enumerate(bases_rvt):
            if np.allclose(rgb, b, atol=1.0):
                return [(i, 1.0)]
        pairs = [(0, 1), (1, 2), (2, 3), (3, 4), (0, 2), (0, 3), (0, 4), (1, 3), (1, 4), (2, 4)]
        best_pair = None
        best_dist = 1e9
        best_t = 0.0
        for i, j in pairs:
            v = bases_rvt[j] - bases_rvt[i]
            v_norm2 = np.dot(v, v)
            if v_norm2 < 1e-6: continue
            t = np.clip(np.dot(rgb - bases_rvt[i], v) / v_norm2, 0.0, 1.0)
            proj = bases_rvt[i] + t * v
            dist = np.linalg.norm(rgb - proj)
            if dist < best_dist:
                best_dist = dist
                best_pair = (i, j)
                best_t = t
        return [(best_pair[0], 1.0 - best_t), (best_pair[1], best_t)]

    unique_rgbs = {}
    for y in range(128):
        for x in range(128):
            rgb = tuple(arr[y, x, :3].astype(int))
            if rgb not in unique_rgbs:
                unique_rgbs[rgb] = decompose(np.array(rgb, dtype=np.float64))

    out = np.zeros_like(arr, dtype=np.uint8)
    for y in range(128):
        for x in range(128):
            alpha = arr[y, x, 3]
            if alpha == 0:
                out[y, x] = [0, 0, 0, 0]
            elif alpha < 255:
                out[y, x, :3] = bases_target[0]
                out[y, x, 3] = int(alpha)
            else:
                rgb = tuple(arr[y, x, :3].astype(int))
                decomp = unique_rgbs[rgb]
                rec = sum(w * bases_target[idx] for idx, w in decomp)
                rec = np.clip(np.round(rec), 0, 255).astype(np.uint8)
                out[y, x, :3] = rec
                out[y, x, 3] = 255
    return Image.fromarray(out, 'RGBA')

def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    docs_images = os.path.join(root, 'docs', 'images')
    os.makedirs(docs_images, exist_ok=True)
    letters = [N, W, C, I, O]

    # 1. nwcio-logo.svg (618x214, rx=18, block=14, base_xs=[23, 135, 275, 387, 499], start_y=38, shadow=2)
    logo_svg = build_svg(618, 214, 18, 14, [23, 135, 275, 387, 499], 38, 2, letters, "NwcIO")
    with open(os.path.join(docs_images, 'nwcio-logo.svg'), 'w', encoding='utf-8') as f:
        f.write(logo_svg)
    print("Wrote docs/images/nwcio-logo.svg")

    # 2. nwcio-logo.png (1236x428 2x scale RGBA)
    logo_png = render_png(618, 214, 18, 14, [23, 135, 275, 387, 499], 38, 2, letters, scale=2, alpha_bg=True)
    logo_png.save(os.path.join(docs_images, 'nwcio-logo.png'))
    print("Wrote docs/images/nwcio-logo.png")

    # 3. nwcio-banner.svg (1280x320, rx=0, block=24, base_xs=[154, 346, 586, 778, 970], start_y=46, shadow=3.4)
    banner_svg = build_svg(1280, 320, 0, 24, [154, 346, 586, 778, 970], 46, 3.4, letters, "NwcIO")
    with open(os.path.join(docs_images, 'nwcio-banner.svg'), 'w', encoding='utf-8') as f:
        f.write(banner_svg)
    print("Wrote docs/images/nwcio-banner.svg")

    # 4. nwcio-banner.png (1280x320 RGB)
    banner_png = render_png(1280, 320, 0, 24, [154, 346, 586, 778, 970], 46, 3.4, letters, scale=1, alpha_bg=False)
    banner_png.save(os.path.join(docs_images, 'nwcio-banner.png'))
    print("Wrote docs/images/nwcio-banner.png")

    # 5. icon.png for packages (src/NwcIO/icon.png and src/NwcIO.Cli/icon.png)
    icon_png = render_canonical_icon()
    os.makedirs(os.path.join(root, 'src', 'NwcIO'), exist_ok=True)
    os.makedirs(os.path.join(root, 'src', 'NwcIO.Cli'), exist_ok=True)
    icon_png.save(os.path.join(root, 'src', 'NwcIO', 'icon.png'))
    icon_png.save(os.path.join(root, 'src', 'NwcIO.Cli', 'icon.png'))
    print("Wrote src/NwcIO/icon.png and src/NwcIO.Cli/icon.png")

if __name__ == '__main__':
    main()
