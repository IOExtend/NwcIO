using System.Globalization;
using System.Text;
using System.Text.Json;
using NwcIO.Model.Clash;

namespace NwcIO.Model.Viewer;

/// <summary>
/// Configuration options for the standalone 3D HTML WebGL viewer export.
/// </summary>
public sealed class NwcHtmlViewerOptions
{
    public string Title { get; set; } = "Navisworks 3D Model";
    public bool IncludeProperties { get; set; } = true;
    public bool IncludeClashes { get; set; } = true;
    public bool IncludeViewpoints { get; set; } = true;
    public bool IncludeRedlines { get; set; } = true;
    public bool IncludeSectioning { get; set; } = true;
    public string Theme { get; set; } = "dark";
    public int MaxMeshVertices { get; set; } = 500000;
}

/// <summary>
/// Exports a Navisworks document or federated assembly to an interactive, self-contained
/// 3D WebGL HTML viewer (.html) with zero external script or server dependencies.
/// </summary>
public static class NwcHtmlViewerExporter
{
    /// <summary>
    /// Exports an <see cref="NwcDocument"/> to a standalone interactive 3D HTML viewer file.
    /// </summary>
    public static void Save(string path, NwcDocument doc, NwcHtmlViewerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(doc);

        options ??= new NwcHtmlViewerOptions { Title = Path.GetFileNameWithoutExtension(path) };
        using var fs = File.Create(path);
        using var writer = new StreamWriter(fs, Encoding.UTF8);
        Write(writer, doc.Meshes, doc.SavedViewpoints, doc.ClashTests, doc.BoundingBox, options);
    }

    /// <summary>
    /// Exports an <see cref="NwcAssembly"/> to a standalone interactive 3D HTML viewer file.
    /// </summary>
    public static void Save(string path, NwcAssembly assembly, NwcHtmlViewerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(assembly);

        options ??= new NwcHtmlViewerOptions { Title = assembly.Name };
        using var fs = File.Create(path);
        using var writer = new StreamWriter(fs, Encoding.UTF8);
        Write(writer, assembly.AllMeshes, assembly.SavedViewpoints, assembly.ClashTests, assembly.BoundingBox, options);
    }

    /// <summary>
    /// Writes the standalone 3D HTML WebGL viewer to a <see cref="TextWriter"/>.
    /// </summary>
    public static void Write(
        TextWriter writer,
        IReadOnlyList<NwcMesh> meshes,
        IReadOnlyList<NwcSavedViewpoint> viewpoints,
        IReadOnlyList<NwcClashTest> clashTests,
        BoundingBox3d bounds,
        NwcHtmlViewerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(meshes);
        options ??= new NwcHtmlViewerOptions();

        string jsonPayload = BuildJsonPayload(meshes, viewpoints, clashTests, bounds, options);

        writer.WriteLine("<!DOCTYPE html>");
        writer.WriteLine("<html lang=\"en\">");
        writer.WriteLine("<head>");
        writer.WriteLine("<meta charset=\"UTF-8\">");
        writer.WriteLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        writer.Write("<title>");
        writer.Write(System.Net.WebUtility.HtmlEncode(options.Title));
        writer.WriteLine("</title>");
        writer.WriteLine(GetCss());
        writer.WriteLine("</head>");
        writer.WriteLine("<body>");
        writer.WriteLine(GetHeaderAndLayout(options.Title));
        writer.WriteLine("<script id=\"nwc-data\" type=\"application/json\">");
        writer.WriteLine(jsonPayload);
        writer.WriteLine("</script>");
        writer.WriteLine(GetViewerScript());
        writer.WriteLine("</body>");
        writer.WriteLine("</html>");
    }

    private static string GetCss()
    {
        return """
<style>
* { box-sizing: border-box; margin: 0; padding: 0; }
body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; background: #1a1a1a; color: #e0e0e0; overflow: hidden; height: 100vh; display: flex; flex-direction: column; }
header { background: #111; border-bottom: 1px solid #333; padding: 10px 16px; display: flex; align-items: center; justify-content: space-between; z-index: 10; }
header h1 { font-size: 15px; font-weight: 600; color: #fff; letter-spacing: 0.5px; }
.toolbar { display: flex; gap: 8px; align-items: center; }
.btn { background: #2a2a2a; color: #ddd; border: 1px solid #444; border-radius: 4px; padding: 6px 12px; font-size: 12px; cursor: pointer; transition: background 0.15s; }
.btn:hover { background: #3a3a3a; color: #fff; border-color: #666; }
.btn.active { background: #007acc; color: #fff; border-color: #0098ff; }
.container { display: flex; flex: 1; position: relative; overflow: hidden; }
#viewport-container { flex: 1; position: relative; background: #161616; }
canvas#glcanvas { width: 100%; height: 100%; display: block; }
canvas#overlay { position: absolute; top: 0; left: 0; width: 100%; height: 100%; pointer-events: none; }
.sidebar { width: 320px; background: #222; border-left: 1px solid #333; display: flex; flex-direction: column; overflow: hidden; }
.left-bar { width: 280px; background: #202020; border-right: 1px solid #333; display: flex; flex-direction: column; }
.panel-header { padding: 10px 14px; font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px; color: #aaa; background: #1b1b1b; border-bottom: 1px solid #333; display: flex; justify-content: space-between; align-items: center; }
.panel-content { flex: 1; overflow-y: auto; padding: 8px; }
.tab-row { display: flex; border-bottom: 1px solid #333; background: #181818; }
.tab-btn { flex: 1; padding: 8px; font-size: 11px; background: none; border: none; color: #888; cursor: pointer; text-align: center; border-bottom: 2px solid transparent; }
.tab-btn.active { color: #fff; border-bottom: 2px solid #007acc; background: #222; }
.tree-item { padding: 6px 8px; font-size: 12px; cursor: pointer; border-radius: 3px; display: flex; align-items: center; gap: 6px; user-select: none; }
.tree-item:hover { background: #2c2c2c; }
.tree-item.selected { background: #007acc; color: #fff; }
.prop-category { margin-bottom: 12px; }
.prop-category-title { font-size: 11px; font-weight: 600; color: #888; text-transform: uppercase; margin-bottom: 4px; padding-bottom: 2px; border-bottom: 1px solid #333; }
.prop-table { width: 100%; font-size: 11px; border-collapse: collapse; }
.prop-table td { padding: 4px 6px; border-bottom: 1px solid #282828; }
.prop-table td.name { color: #aaa; width: 40%; }
.prop-table td.val { color: #eee; word-break: break-all; }
.section-panel { position: absolute; bottom: 16px; left: 50%; transform: translateX(-50%); background: rgba(30,30,30,0.92); border: 1px solid #444; border-radius: 6px; padding: 10px 16px; display: flex; gap: 14px; align-items: center; z-index: 5; box-shadow: 0 4px 12px rgba(0,0,0,0.4); }
.section-panel label { font-size: 11px; color: #bbb; display: flex; align-items: center; gap: 6px; }
.section-panel input[type=range] { width: 100px; }
.hud-stats { position: absolute; top: 12px; left: 12px; font-size: 11px; color: #777; background: rgba(0,0,0,0.5); padding: 4px 8px; border-radius: 4px; pointer-events: none; }
</style>
""";
    }

    private static string GetHeaderAndLayout(string title)
    {
        return $"""
<header>
  <h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>
  <div class="toolbar">
    <button class="btn" id="btn-reset-cam">Reset View</button>
    <button class="btn" id="btn-toggle-sec">Sectioning</button>
    <button class="btn" id="btn-wireframe">Wireframe</button>
    <button class="btn" id="btn-fullscreen">Fullscreen</button>
  </div>
</header>
<div class="container">
  <div class="left-bar">
    <div class="tab-row">
      <button class="tab-btn active" data-tab="tab-vps">Viewpoints</button>
      <button class="tab-btn" data-tab="tab-clashes">Clashes</button>
      <button class="tab-btn" data-tab="tab-tree">Hierarchy</button>
    </div>
    <div class="panel-content" id="tab-vps"></div>
    <div class="panel-content" id="tab-clashes" style="display:none;"></div>
    <div class="panel-content" id="tab-tree" style="display:none;"></div>
  </div>
  <div id="viewport-container">
    <div class="hud-stats" id="hud-stats">Meshes: 0 | Vertices: 0</div>
    <canvas id="glcanvas"></canvas>
    <canvas id="overlay"></canvas>
    <div class="section-panel" id="sec-controls" style="display:none;">
      <label><input type="checkbox" id="clip-x-en"> Cut X</label>
      <input type="range" id="clip-x-val" min="-100" max="100" value="0">
      <label><input type="checkbox" id="clip-y-en"> Cut Y</label>
      <input type="range" id="clip-y-val" min="-100" max="100" value="0">
      <label><input type="checkbox" id="clip-z-en"> Cut Z</label>
      <input type="range" id="clip-z-val" min="-100" max="100" value="0">
    </div>
  </div>
  <div class="sidebar">
    <div class="panel-header">Property Inspector</div>
    <div class="panel-content" id="prop-inspector">
      <div style="color: #666; font-size: 12px; text-align: center; margin-top: 40px;">Select an element to inspect properties</div>
    </div>
  </div>
</div>
""";
    }

    private static string GetViewerScript()
    {
        return """
<script>
(function() {
  const modelData = JSON.parse(document.getElementById('nwc-data').textContent);
  const canvas = document.getElementById('glcanvas');
  const overlay = document.getElementById('overlay');
  const gl = canvas.getContext('webgl') || canvas.getContext('experimental-webgl');

  if (!gl) {
    alert('WebGL is not supported by your browser.');
    return;
  }

  // Camera State
  let camTarget = [0, 0, 0];
  let camRadius = 10;
  let camTheta = 0.8;
  let camPhi = 0.5;
  let isDragging = false;
  let isPanning = false;
  let lastMouseX = 0;
  let lastMouseY = 0;
  let selectedMeshIndex = -1;
  let wireframeMode = false;

  // Clipping Planes State
  const clipPlanes = {
    xEn: false, xVal: 0,
    yEn: false, yVal: 0,
    zEn: false, zVal: 0
  };

  // Compute Model Bounds & Center
  const bbox = modelData.boundingBox;
  if (bbox && bbox.min && bbox.max) {
    camTarget = [
      (bbox.min[0] + bbox.max[0]) * 0.5,
      (bbox.min[1] + bbox.max[1]) * 0.5,
      (bbox.min[2] + bbox.max[2]) * 0.5
    ];
    const dx = bbox.max[0] - bbox.min[0];
    const dy = bbox.max[1] - bbox.min[1];
    const dz = bbox.max[2] - bbox.min[2];
    camRadius = Math.max(Math.sqrt(dx*dx + dy*dy + dz*dz) * 1.2, 5.0);
  }

  // Update HUD
  const totalVerts = modelData.meshes.reduce((acc, m) => acc + (m.vertices.length / 3), 0);
  document.getElementById('hud-stats').textContent = `Meshes: ${modelData.meshes.length} | Vertices: ${Math.round(totalVerts).toLocaleString()}`;

  // Shaders
  const vsSource = `
    attribute vec3 aPosition;
    attribute vec3 aNormal;
    attribute vec4 aColor;
    uniform mat4 uProjection;
    uniform mat4 uView;
    uniform mat4 uModel;
    varying vec3 vNormal;
    varying vec3 vFragPos;
    varying vec4 vColor;
    void main() {
      vec4 worldPos = uModel * vec4(aPosition, 1.0);
      vFragPos = worldPos.xyz;
      vNormal = (uModel * vec4(aNormal, 0.0)).xyz;
      vColor = aColor;
      gl_Position = uProjection * uView * worldPos;
    }
  `;

  const fsSource = `
    precision mediump float;
    varying vec3 vNormal;
    varying vec3 vFragPos;
    varying vec4 vColor;
    uniform vec3 uLightDir;
    uniform vec4 uHighlightColor;
    uniform vec4 uClipX;
    uniform vec4 uClipY;
    uniform vec4 uClipZ;
    void main() {
      if (uClipX.w > 0.5 && vFragPos.x > uClipX.x) discard;
      if (uClipY.w > 0.5 && vFragPos.y > uClipY.y) discard;
      if (uClipZ.w > 0.5 && vFragPos.z > uClipZ.z) discard;

      vec3 norm = normalize(vNormal);
      vec3 light = normalize(uLightDir);
      float diff = max(dot(norm, light), 0.0);
      float ambient = 0.35;
      vec3 base = (ambient + diff * 0.65) * vColor.rgb;
      if (uHighlightColor.a > 0.0) {
        base = mix(base, uHighlightColor.rgb, uHighlightColor.a);
      }
      gl_FragColor = vec4(base, vColor.a);
    }
  `;

  function createShader(gl, type, source) {
    const s = gl.createShader(type);
    gl.shaderSource(s, source);
    gl.compileShader(s);
    return s;
  }

  const prog = gl.createProgram();
  gl.attachShader(prog, createShader(gl, gl.VERTEX_SHADER, vsSource));
  gl.attachShader(prog, createShader(gl, gl.FRAGMENT_SHADER, fsSource));
  gl.linkProgram(prog);
  gl.useProgram(prog);

  const loc = {
    aPos: gl.getAttribLocation(prog, 'aPosition'),
    aNorm: gl.getAttribLocation(prog, 'aNormal'),
    aCol: gl.getAttribLocation(prog, 'aColor'),
    uProj: gl.getUniformLocation(prog, 'uProjection'),
    uView: gl.getUniformLocation(prog, 'uView'),
    uModel: gl.getUniformLocation(prog, 'uModel'),
    uLight: gl.getUniformLocation(prog, 'uLightDir'),
    uHigh: gl.getUniformLocation(prog, 'uHighlightColor'),
    uClipX: gl.getUniformLocation(prog, 'uClipX'),
    uClipY: gl.getUniformLocation(prog, 'uClipY'),
    uClipZ: gl.getUniformLocation(prog, 'uClipZ')
  };

  // Upload Meshes to GPU
  const gpuMeshes = modelData.meshes.map((m, idx) => {
    const vbo = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(m.vertices), gl.STATIC_DRAW);

    // Compute normals
    const norms = new Float32Array(m.vertices.length);
    for (let i = 0; i < m.indices.length; i += 3) {
      const i0 = m.indices[i]*3, i1 = m.indices[i+1]*3, i2 = m.indices[i+2]*3;
      const ax = m.vertices[i1]-m.vertices[i0], ay = m.vertices[i1+1]-m.vertices[i0+1], az = m.vertices[i1+2]-m.vertices[i0+2];
      const bx = m.vertices[i2]-m.vertices[i0], by = m.vertices[i2+1]-m.vertices[i0+1], bz = m.vertices[i2+2]-m.vertices[i0+2];
      const nx = ay*bz - az*by, ny = az*bx - ax*bz, nz = ax*by - ay*bx;
      norms[i0]+=nx; norms[i0+1]+=ny; norms[i0+2]+=nz;
      norms[i1]+=nx; norms[i1+1]+=ny; norms[i1+2]+=nz;
      norms[i2]+=nx; norms[i2+1]+=ny; norms[i2+2]+=nz;
    }
    const nbo = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, nbo);
    gl.bufferData(gl.ARRAY_BUFFER, norms, gl.STATIC_DRAW);

    // Colors
    const colArr = new Float32Array(m.vertices.length / 3 * 4);
    const c = m.color || [0.8, 0.8, 0.8, 1.0];
    for (let i = 0; i < colArr.length; i += 4) {
      colArr[i]=c[0]; colArr[i+1]=c[1]; colArr[i+2]=c[2]; colArr[i+3]=c[3];
    }
    const cbo = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, cbo);
    gl.bufferData(gl.ARRAY_BUFFER, colArr, gl.STATIC_DRAW);

    const ibo = gl.createBuffer();
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ibo);
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, new Uint16Array(m.indices), gl.STATIC_DRAW);

    return { vbo, nbo, cbo, ibo, count: m.indices.length, raw: m, index: idx };
  });

  // Matrix Math
  function mat4Identity() {
    return [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
  }

  function mat4Perspective(fovy, aspect, near, far) {
    const f = 1.0 / Math.tan(fovy / 2);
    const nf = 1 / (near - far);
    return [
      f / aspect, 0, 0, 0,
      0, f, 0, 0,
      0, 0, (far + near) * nf, -1,
      0, 0, (2 * far * near) * nf, 0
    ];
  }

  function mat4LookAt(eye, center, up) {
    let zx = eye[0]-center[0], zy = eye[1]-center[1], zz = eye[2]-center[2];
    let len = Math.hypot(zx, zy, zz) || 1;
    zx /= len; zy /= len; zz /= len;

    let xx = up[1]*zz - up[2]*zy, xy = up[2]*zx - up[0]*zz, xz = up[0]*zy - up[1]*zx;
    len = Math.hypot(xx, xy, xz) || 1;
    xx /= len; xy /= len; xz /= len;

    let yx = zy*xz - zz*xy, yy = zz*xx - zx*xz, yz = zx*xy - zy*xx;

    return [
      xx, yx, zx, 0,
      xy, yy, zy, 0,
      xz, yz, zz, 0,
      -(xx*eye[0] + xy*eye[1] + xz*eye[2]),
      -(yx*eye[0] + yy*eye[1] + yz*eye[2]),
      -(zx*eye[0] + zy*eye[1] + zz*eye[2]),
      1
    ];
  }

  // Render Loop
  function render() {
    resizeCanvas();
    gl.viewport(0, 0, canvas.width, canvas.height);
    gl.enable(gl.DEPTH_TEST);
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    gl.clearColor(0.1, 0.1, 0.12, 1.0);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);

    const eye = [
      camTarget[0] + camRadius * Math.sin(camTheta) * Math.cos(camPhi),
      camTarget[1] + camRadius * Math.sin(camTheta) * Math.sin(camPhi),
      camTarget[2] + camRadius * Math.cos(camTheta)
    ];

    const aspect = canvas.clientWidth / (canvas.clientHeight || 1);
    const pMat = mat4Perspective(Math.PI / 4, aspect, 0.1, camRadius * 10);
    const vMat = mat4LookAt(eye, camTarget, [0, 0, 1]);
    const mMat = mat4Identity();

    gl.uniformMatrix4fv(loc.uProj, false, pMat);
    gl.uniformMatrix4fv(loc.uView, false, vMat);
    gl.uniformMatrix4fv(loc.uModel, false, mMat);
    gl.uniform3f(loc.uLight, 0.5, 0.8, 1.0);

    gl.uniform4f(loc.uClipX, clipPlanes.xVal, 0, 0, clipPlanes.xEn ? 1.0 : 0.0);
    gl.uniform4f(loc.uClipY, 0, clipPlanes.yVal, 0, clipPlanes.yEn ? 1.0 : 0.0);
    gl.uniform4f(loc.uClipZ, 0, 0, clipPlanes.zVal, clipPlanes.zEn ? 1.0 : 0.0);

    for (const gm of gpuMeshes) {
      gl.bindBuffer(gl.ARRAY_BUFFER, gm.vbo);
      gl.vertexAttribPointer(loc.aPos, 3, gl.FLOAT, false, 0, 0);
      gl.enableVertexAttribArray(loc.aPos);

      gl.bindBuffer(gl.ARRAY_BUFFER, gm.nbo);
      gl.vertexAttribPointer(loc.aNorm, 3, gl.FLOAT, false, 0, 0);
      gl.enableVertexAttribArray(loc.aNorm);

      gl.bindBuffer(gl.ARRAY_BUFFER, gm.cbo);
      gl.vertexAttribPointer(loc.aCol, 4, gl.FLOAT, false, 0, 0);
      gl.enableVertexAttribArray(loc.aCol);

      gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, gm.ibo);

      if (gm.index === selectedMeshIndex) {
        gl.uniform4f(loc.uHigh, 1.0, 0.75, 0.0, 0.7);
      } else {
        gl.uniform4f(loc.uHigh, 0, 0, 0, 0);
      }

      const mode = wireframeMode ? gl.LINES : gl.TRIANGLES;
      gl.drawElements(mode, gm.count, gl.UNSIGNED_SHORT, 0);
    }
  }

  function resizeCanvas() {
    const w = canvas.parentElement.clientWidth;
    const h = canvas.parentElement.clientHeight;
    if (canvas.width !== w || canvas.height !== h) {
      canvas.width = w; canvas.height = h;
      overlay.width = w; overlay.height = h;
    }
  }

  // Mouse & Touch Orbit Controls
  canvas.addEventListener('mousedown', e => {
    isDragging = true;
    isPanning = (e.button === 1 || e.button === 2 || e.shiftKey);
    lastMouseX = e.clientX;
    lastMouseY = e.clientY;
  });

  window.addEventListener('mouseup', () => { isDragging = false; });
  window.addEventListener('contextmenu', e => e.preventDefault());

  window.addEventListener('mousemove', e => {
    if (!isDragging) return;
    const dx = e.clientX - lastMouseX;
    const dy = e.clientY - lastMouseY;
    lastMouseX = e.clientX;
    lastMouseY = e.clientY;

    if (isPanning) {
      const factor = camRadius * 0.0015;
      camTarget[0] -= dx * factor;
      camTarget[1] += dy * factor;
    } else {
      camPhi -= dx * 0.008;
      camTheta -= dy * 0.008;
      camTheta = Math.max(0.01, Math.min(Math.PI - 0.01, camTheta));
    }
    requestAnimationFrame(render);
  });

  canvas.addEventListener('wheel', e => {
    e.preventDefault();
    camRadius *= (e.deltaY > 0 ? 1.1 : 0.9);
    camRadius = Math.max(0.2, camRadius);
    requestAnimationFrame(render);
  }, { passive: false });

  // Element Selection & Property Inspector
  canvas.addEventListener('click', e => {
    if (Math.abs(e.clientX - lastMouseX) > 3 || Math.abs(e.clientY - lastMouseY) > 3) return;
    if (gpuMeshes.length === 0) return;
    selectedMeshIndex = (selectedMeshIndex + 1) % gpuMeshes.length;
    showProperties(gpuMeshes[selectedMeshIndex].raw);
    requestAnimationFrame(render);
  });

  function showProperties(mesh) {
    const pane = document.getElementById('prop-inspector');
    let html = `<div style="font-weight:600;font-size:13px;margin-bottom:8px;color:#fff;">${mesh.name || 'Unnamed Element'}</div>`;
    html += `<div style="font-size:11px;color:#888;margin-bottom:12px;">Layer: ${mesh.layer || 'Default'}</div>`;

    if (mesh.properties && Object.keys(mesh.properties).length > 0) {
      for (const [cat, props] of Object.entries(mesh.properties)) {
        html += `<div class="prop-category"><div class="prop-category-title">${cat}</div><table class="prop-table">`;
        for (const [pname, pval] of Object.entries(props)) {
          html += `<tr><td class="name">${pname}</td><td class="val">${pval}</td></tr>`;
        }
        html += `</table></div>`;
      }
    } else {
      html += `<div style="color:#777;font-size:11px;">No custom properties recorded.</div>`;
    }
    pane.innerHTML = html;
  }

  // Populate Viewpoints Tab
  const vpsContainer = document.getElementById('tab-vps');
  if (modelData.viewpoints && modelData.viewpoints.length > 0) {
    modelData.viewpoints.forEach(vp => {
      const item = document.createElement('div');
      item.className = 'tree-item';
      item.textContent = vp.name;
      item.addEventListener('click', () => {
        if (vp.camera && vp.camera.position && vp.camera.target) {
          camTarget = [...vp.camera.target];
          const ex = vp.camera.position[0]-camTarget[0];
          const ey = vp.camera.position[1]-camTarget[1];
          const ez = vp.camera.position[2]-camTarget[2];
          camRadius = Math.hypot(ex, ey, ez) || 10;
          camTheta = Math.acos(ez / camRadius);
          camPhi = Math.atan2(ey, ex);
          requestAnimationFrame(render);
        }
      });
      vpsContainer.appendChild(item);
    });
  } else {
    vpsContainer.innerHTML = '<div style="color:#666;font-size:11px;padding:8px;">No saved viewpoints.</div>';
  }

  // Populate Clashes Tab
  const clashesContainer = document.getElementById('tab-clashes');
  if (modelData.clashTests && modelData.clashTests.length > 0) {
    modelData.clashTests.forEach(test => {
      const tDiv = document.createElement('div');
      tDiv.style.fontWeight = '600';
      tDiv.style.fontSize = '12px';
      tDiv.style.margin = '6px 0';
      tDiv.textContent = `${test.name} (${test.allClashes ? test.allClashes.length : 0})`;
      clashesContainer.appendChild(tDiv);

      (test.allClashes || []).forEach(clash => {
        const cItem = document.createElement('div');
        cItem.className = 'tree-item';
        cItem.textContent = `${clash.name}: ${clash.item1?.name || 'A'} vs ${clash.item2?.name || 'B'}`;
        cItem.addEventListener('click', () => {
          if (clash.point) {
            camTarget = [...clash.point];
            camRadius = 4.0;
            requestAnimationFrame(render);
          }
        });
        clashesContainer.appendChild(cItem);
      });
    });
  } else {
    clashesContainer.innerHTML = '<div style="color:#666;font-size:11px;padding:8px;">No clash detection tests.</div>';
  }

  // Hierarchy Tab
  const treeContainer = document.getElementById('tab-tree');
  modelData.meshes.forEach((m, idx) => {
    const item = document.createElement('div');
    item.className = 'tree-item';
    item.textContent = m.name || `Mesh ${idx+1}`;
    item.addEventListener('click', () => {
      selectedMeshIndex = idx;
      showProperties(m);
      requestAnimationFrame(render);
    });
    treeContainer.appendChild(item);
  });

  // Tab Switching
  document.querySelectorAll('.tab-btn').forEach(btn => {
    btn.addEventListener('click', () => {
      document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
      document.querySelectorAll('.left-bar .panel-content').forEach(p => p.style.display = 'none');
      btn.classList.add('active');
      document.getElementById(btn.dataset.tab).style.display = 'block';
    });
  });

  // Toolbar Actions
  document.getElementById('btn-reset-cam').addEventListener('click', () => {
    camTarget = [
      (bbox.min[0] + bbox.max[0]) * 0.5,
      (bbox.min[1] + bbox.max[1]) * 0.5,
      (bbox.min[2] + bbox.max[2]) * 0.5
    ];
    camRadius = Math.max(Math.hypot(bbox.max[0]-bbox.min[0], bbox.max[1]-bbox.min[1], bbox.max[2]-bbox.min[2]) * 1.2, 5.0);
    camTheta = 0.8; camPhi = 0.5;
    requestAnimationFrame(render);
  });

  document.getElementById('btn-wireframe').addEventListener('click', function() {
    wireframeMode = !wireframeMode;
    this.classList.toggle('active', wireframeMode);
    requestAnimationFrame(render);
  });

  const secControls = document.getElementById('sec-controls');
  document.getElementById('btn-toggle-sec').addEventListener('click', function() {
    const isVis = secControls.style.display !== 'none';
    secControls.style.display = isVis ? 'none' : 'flex';
    this.classList.toggle('active', !isVis);
  });

  document.getElementById('clip-x-en').addEventListener('change', e => { clipPlanes.xEn = e.target.checked; requestAnimationFrame(render); });
  document.getElementById('clip-y-en').addEventListener('change', e => { clipPlanes.yEn = e.target.checked; requestAnimationFrame(render); });
  document.getElementById('clip-z-en').addEventListener('change', e => { clipPlanes.zEn = e.target.checked; requestAnimationFrame(render); });

  document.getElementById('clip-x-val').addEventListener('input', e => { clipPlanes.xVal = (e.target.value/100)*(bbox.max[0]-bbox.min[0]) + camTarget[0]; requestAnimationFrame(render); });
  document.getElementById('clip-y-val').addEventListener('input', e => { clipPlanes.yVal = (e.target.value/100)*(bbox.max[1]-bbox.min[1]) + camTarget[1]; requestAnimationFrame(render); });
  document.getElementById('clip-z-val').addEventListener('input', e => { clipPlanes.zVal = (e.target.value/100)*(bbox.max[2]-bbox.min[2]) + camTarget[2]; requestAnimationFrame(render); });

  document.getElementById('btn-fullscreen').addEventListener('click', () => {
    if (!document.fullscreenElement) document.documentElement.requestFullscreen();
    else document.exitFullscreen();
  });

  window.addEventListener('resize', () => requestAnimationFrame(render));
  render();
})();
</script>
""";
    }

    private static string BuildJsonPayload(
        IReadOnlyList<NwcMesh> meshes,
        IReadOnlyList<NwcSavedViewpoint> viewpoints,
        IReadOnlyList<NwcClashTest> clashTests,
        BoundingBox3d bounds,
        NwcHtmlViewerOptions options)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });

        writer.WriteStartObject();
        writer.WriteString("title", options.Title);

        // Bounding Box
        writer.WriteStartObject("boundingBox");
        writer.WriteStartArray("min");
        writer.WriteNumberValue(bounds.Min.X);
        writer.WriteNumberValue(bounds.Min.Y);
        writer.WriteNumberValue(bounds.Min.Z);
        writer.WriteEndArray();
        writer.WriteStartArray("max");
        writer.WriteNumberValue(bounds.Max.X);
        writer.WriteNumberValue(bounds.Max.Y);
        writer.WriteNumberValue(bounds.Max.Z);
        writer.WriteEndArray();
        writer.WriteEndObject();

        // Meshes
        writer.WriteStartArray("meshes");
        int totalVerts = 0;
        foreach (var mesh in meshes)
        {
            if (totalVerts >= options.MaxMeshVertices) break;

            writer.WriteStartObject();
            writer.WriteString("name", mesh.Name ?? $"Mesh {mesh.Id}");
            writer.WriteString("layer", mesh.Node?.Class ?? "Default");

            // RGBA color
            var col = mesh.Material?.Color ?? new RgbaColor(200, 200, 200);
            writer.WriteStartArray("color");
            writer.WriteNumberValue(col.R / 255.0);
            writer.WriteNumberValue(col.G / 255.0);
            writer.WriteNumberValue(col.B / 255.0);
            writer.WriteNumberValue(col.A / 255.0);
            writer.WriteEndArray();

            // Vertices
            writer.WriteStartArray("vertices");
            foreach (var v in mesh.Vertices)
            {
                writer.WriteNumberValue(v.X);
                writer.WriteNumberValue(v.Y);
                writer.WriteNumberValue(v.Z);
            }
            writer.WriteEndArray();

            // Indices
            writer.WriteStartArray("indices");
            foreach (var idx in mesh.Indices)
            {
                writer.WriteNumberValue(idx);
            }
            writer.WriteEndArray();

            // Properties
            if (options.IncludeProperties && mesh.Node is not null && mesh.Node.Properties.Count > 0)
            {
                writer.WriteStartObject("properties");
                var groupedProps = mesh.Node.Properties.GroupBy(p => string.IsNullOrEmpty(p.Class) ? "General" : p.Class);
                foreach (var group in groupedProps)
                {
                    writer.WriteStartObject(group.Key);
                    foreach (var prop in group)
                    {
                        if (!string.IsNullOrEmpty(prop.Name) && !string.IsNullOrEmpty(prop.Value))
                        {
                            writer.WriteString(prop.Name, prop.Value);
                        }
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            totalVerts += mesh.Vertices.Count;
        }
        writer.WriteEndArray(); // meshes

        // Viewpoints
        if (options.IncludeViewpoints && viewpoints.Count > 0)
        {
            writer.WriteStartArray("viewpoints");
            foreach (var vp in viewpoints)
            {
                writer.WriteStartObject();
                writer.WriteString("name", vp.Name);
                writer.WriteStartObject("camera");

                writer.WriteStartArray("position");
                writer.WriteNumberValue(vp.Camera.Position.X);
                writer.WriteNumberValue(vp.Camera.Position.Y);
                writer.WriteNumberValue(vp.Camera.Position.Z);
                writer.WriteEndArray();

                writer.WriteStartArray("target");
                writer.WriteNumberValue(vp.Camera.Target.X);
                writer.WriteNumberValue(vp.Camera.Target.Y);
                writer.WriteNumberValue(vp.Camera.Target.Z);
                writer.WriteEndArray();

                writer.WriteEndObject(); // camera
                writer.WriteEndObject(); // viewpoint
            }
            writer.WriteEndArray();
        }

        // Clash Tests
        if (options.IncludeClashes && clashTests.Count > 0)
        {
            writer.WriteStartArray("clashTests");
            foreach (var test in clashTests)
            {
                writer.WriteStartObject();
                writer.WriteString("name", test.Name);
                writer.WriteNumber("tolerance", test.Tolerance);

                if (test.Groups.Count > 0)
                {
                    writer.WriteStartArray("groups");
                    foreach (var grp in test.Groups)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("name", grp.Name);
                        writer.WriteString("status", grp.Status.ToString());
                        if (!string.IsNullOrEmpty(grp.Description))
                        {
                            writer.WriteString("description", grp.Description);
                        }
                        writer.WriteNumber("clashCount", grp.Clashes.Count);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }

                writer.WriteStartArray("allClashes");
                foreach (var clash in test.AllClashes)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", clash.Name);
                    writer.WriteString("status", clash.Status.ToString());
                    writer.WriteNumber("distance", clash.Distance);

                    writer.WriteStartArray("point");
                    writer.WriteNumberValue(clash.ClashPoint.X);
                    writer.WriteNumberValue(clash.ClashPoint.Y);
                    writer.WriteNumberValue(clash.ClashPoint.Z);
                    writer.WriteEndArray();

                    writer.WriteStartObject("item1");
                    writer.WriteString("name", clash.Item1.Name);
                    writer.WriteEndObject();

                    writer.WriteStartObject("item2");
                    writer.WriteString("name", clash.Item2.Name);
                    writer.WriteEndObject();

                    writer.WriteEndObject();
                }
                writer.WriteEndArray(); // allClashes

                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
