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
    public bool IncludeMeasure { get; set; } = true;
    public bool IncludeGrids { get; set; } = true;
    public bool IncludeTimeLiner { get; set; } = true;
    public TimeLiner.NwcTimeLinerSchedule? TimeLiner { get; set; }
    public bool IncludeSelectionSets { get; set; } = true;
    public IReadOnlyList<Sets.NwcSetItem>? SelectionSets { get; set; }
    public Grids.NwcGridSystem? GridSystem { get; set; }
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
        options.GridSystem ??= doc.GridSystem;
        options.TimeLiner ??= doc.TimeLiner;
        options.SelectionSets ??= doc.SelectionSets;
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
        options.GridSystem ??= assembly.GridSystem;
        options.TimeLiner ??= assembly.TimeLiner;
        options.SelectionSets ??= assembly.SelectionSets;
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
.measure-panel { position: absolute; top: 16px; right: 16px; background: rgba(25,25,25,0.92); border: 1px solid #007acc; border-radius: 6px; padding: 10px 14px; font-size: 11px; z-index: 6; box-shadow: 0 4px 14px rgba(0,0,0,0.5); min-width: 180px; }
.measure-panel .title { font-weight: 600; color: #4dabf7; margin-bottom: 4px; display: flex; justify-content: space-between; }
.measure-panel .row { display: flex; justify-content: space-between; margin: 2px 0; color: #ccc; }
.measure-panel .val { font-weight: 600; color: #fff; }
.timeliner-bar { position: absolute; bottom: 16px; left: 50%; transform: translateX(-50%); background: rgba(25,25,25,0.94); border: 1px solid #007acc; border-radius: 6px; padding: 8px 16px; display: flex; gap: 12px; align-items: center; z-index: 6; box-shadow: 0 4px 14px rgba(0,0,0,0.5); min-width: 480px; }
.timeliner-bar button { background: #2a2a2a; color: #fff; border: 1px solid #555; border-radius: 4px; padding: 4px 10px; cursor: pointer; font-size: 12px; }
.timeliner-bar button:hover { background: #3a3a3a; }
.timeliner-bar input[type=range] { flex: 1; accent-color: #007acc; cursor: pointer; }
.timeliner-date { font-family: monospace; font-size: 12px; font-weight: 600; color: #4dabf7; min-width: 130px; text-align: center; }
</style>
""";
    }

    private static string GetHeaderAndLayout(string title)
    {
        return $"""
<header>
  <h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>
  <div class="toolbar">
    <button class="btn" id="btn-timeliner" style="display:none;">4D TimeLiner</button>
    <button class="btn" id="btn-measure">Measure</button>
    <button class="btn" id="btn-toggle-sec">Sectioning</button>
    <button class="btn" id="btn-turntable">Turntable</button>
    <button class="btn" id="btn-wireframe">Wireframe</button>
    <button class="btn" id="btn-reset-cam">Reset View</button>
    <button class="btn" id="btn-fullscreen">Fullscreen</button>
  </div>
</header>
<div class="container">
  <div class="left-bar">
    <div class="tab-row">
      <button class="tab-btn active" data-tab="tab-vps">Viewpoints</button>
      <button class="tab-btn" data-tab="tab-clashes">Clashes</button>
      <button class="tab-btn" data-tab="tab-tree">Hierarchy</button>
      <button class="tab-btn" id="tab-btn-sets" data-tab="tab-sets" style="display:none;">Sets</button>
    </div>
    <div class="panel-content" id="tab-vps">
      <div style="display:flex;gap:6px;margin-bottom:8px;">
        <button class="btn" id="btn-play-tour" style="flex:1;">▶ Play Tour</button>
      </div>
      <div id="vps-list"></div>
    </div>
    <div class="panel-content" id="tab-clashes" style="display:none;"></div>
    <div class="panel-content" id="tab-tree" style="display:none;"></div>
    <div class="panel-content" id="tab-sets" style="display:none;"></div>
  </div>
  <div id="viewport-container">
    <div class="hud-stats" id="hud-stats">Meshes: 0 | Vertices: 0</div>
    <canvas id="glcanvas"></canvas>
    <canvas id="overlay"></canvas>
    <div class="measure-panel" id="measure-panel" style="display:none;">
      <div class="title"><span>3D Measure</span><span id="measure-clear" style="cursor:pointer;color:#888;">&times;</span></div>
      <div id="measure-status" style="color:#aaa;margin-bottom:4px;">Click 2 points to measure</div>
      <div id="measure-results" style="display:none;">
        <div class="row"><span>Distance:</span><span class="val" id="m-dist">0.00 m</span></div>
        <div class="row"><span>Horiz Dist:</span><span class="val" id="m-horiz">0.00 m</span></div>
        <div class="row"><span>dX / dY / dZ:</span><span class="val" id="m-deltas">0 / 0 / 0</span></div>
        <div class="row"><span>Pitch:</span><span class="val" id="m-pitch">0.0°</span></div>
      </div>
    </div>
    <div class="timeliner-bar" id="timeliner-bar" style="display:none;">
      <button id="tl-play">▶ Play</button>
      <input type="range" id="tl-slider" min="0" max="1000" value="0">
      <span class="timeliner-date" id="tl-date">--/--/----</span>
      <select id="tl-speed" style="background:#222;color:#ccc;border:1px solid #444;border-radius:3px;padding:3px;font-size:11px;">
        <option value="1">1x</option>
        <option value="5">5x</option>
        <option value="15">15x</option>
        <option value="60">60x</option>
      </select>
    </div>
    <div class="section-panel" id="sec-controls" style="display:none;">
      <div style="display:flex;gap:8px;align-items:center;border-right:1px solid #444;padding-right:10px;margin-right:4px;">
        <label><input type="radio" name="sec-mode" id="sec-mode-planes" value="planes" checked> Planes</label>
        <label><input type="radio" name="sec-mode" id="sec-mode-box" value="box"> Box</label>
      </div>
      <div id="sec-planes-ui" style="display:flex;gap:10px;align-items:center;">
        <label><input type="checkbox" id="clip-x-en"> Cut X</label>
        <input type="range" id="clip-x-val" min="-100" max="100" value="0">
        <label><input type="checkbox" id="clip-y-en"> Cut Y</label>
        <input type="range" id="clip-y-val" min="-100" max="100" value="0">
        <label><input type="checkbox" id="clip-z-en"> Cut Z</label>
        <input type="range" id="clip-z-val" min="-100" max="100" value="0">
      </div>
      <div id="sec-box-ui" style="display:none;gap:10px;align-items:center;">
        <label><input type="checkbox" id="clip-box-en"> Enable Box</label>
        <label><input type="checkbox" id="clip-box-invert"> Invert</label>
        <button class="btn" id="btn-fit-box" style="padding:2px 8px;font-size:11px;">Fit Selection</button>
        <button class="btn" id="btn-reset-box" style="padding:2px 8px;font-size:11px;">Reset Box</button>
        <label>Scale <input type="range" id="clip-box-scale" min="10" max="150" value="100" style="width:70px;"></label>
      </div>
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
  let selectedSetMeshIndices = new Set();
  let wireframeMode = false;
  let measureMode = false;
  let measureP1 = null;
  let measureP2 = null;
  let lastPMat = null;
  let lastVMat = null;

  // Clipping Planes State
  const clipPlanes = {
    xEn: false, xVal: 0,
    yEn: false, yVal: 0,
    zEn: false, zVal: 0
  };

  // Compute Model Bounds & Center
  const bbox = modelData.boundingBox || { min: [-10, -10, -10], max: [10, 10, 10] };
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

  // 6-Sided Section Box State
  const clipBox = {
    en: false,
    invert: false,
    min: [bbox.min[0], bbox.min[1], bbox.min[2]],
    max: [bbox.max[0], bbox.max[1], bbox.max[2]]
  };

  let turntableActive = false;
  let tourActive = false;
  let tourIndex = 0;
  let tourTimer = null;
  let currentCamAnim = null;

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
    uniform vec4 uClipBoxMin;
    uniform vec4 uClipBoxMax;
    void main() {
      if (uClipX.w > 0.5 && vFragPos.x > uClipX.x) discard;
      if (uClipY.w > 0.5 && vFragPos.y > uClipY.y) discard;
      if (uClipZ.w > 0.5 && vFragPos.z > uClipZ.z) discard;

      if (uClipBoxMin.w > 0.5) {
        bool inside = (vFragPos.x >= uClipBoxMin.x && vFragPos.x <= uClipBoxMax.x &&
                       vFragPos.y >= uClipBoxMin.y && vFragPos.y <= uClipBoxMax.y &&
                       vFragPos.z >= uClipBoxMin.z && vFragPos.z <= uClipBoxMax.z);
        if (uClipBoxMax.w > 0.5) {
          if (inside) discard;
        } else {
          if (!inside) discard;
        }
      }

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
    uClipZ: gl.getUniformLocation(prog, 'uClipZ'),
    uClipBoxMin: gl.getUniformLocation(prog, 'uClipBoxMin'),
    uClipBoxMax: gl.getUniformLocation(prog, 'uClipBoxMax')
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

    let mbMin = [Infinity, Infinity, Infinity];
    let mbMax = [-Infinity, -Infinity, -Infinity];
    for (let i = 0; i < m.vertices.length; i += 3) {
      const vx = m.vertices[i], vy = m.vertices[i+1], vz = m.vertices[i+2];
      if (vx < mbMin[0]) mbMin[0] = vx; if (vx > mbMax[0]) mbMax[0] = vx;
      if (vy < mbMin[1]) mbMin[1] = vy; if (vy > mbMax[1]) mbMax[1] = vy;
      if (vz < mbMin[2]) mbMin[2] = vz; if (vz > mbMax[2]) mbMax[2] = vz;
    }
    const meshBounds = (mbMin[0] <= mbMax[0]) ? { min: mbMin, max: mbMax } : null;

    return { vbo, nbo, cbo, ibo, count: m.indices.length, raw: m, index: idx, bounds: meshBounds };
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
    gl.uniform4f(loc.uClipBoxMin, clipBox.min[0], clipBox.min[1], clipBox.min[2], clipBox.en ? 1.0 : 0.0);
    gl.uniform4f(loc.uClipBoxMax, clipBox.max[0], clipBox.max[1], clipBox.max[2], clipBox.invert ? 1.0 : 0.0);

    lastPMat = pMat;
    lastVMat = vMat;

    for (const gm of gpuMeshes) {
      if (gm.hidden) continue;

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
      } else if (selectedSetMeshIndices.has(gm.index)) {
        gl.uniform4f(loc.uHigh, 0.0, 0.7, 1.0, 0.7);
      } else if (gm.simColor) {
        gl.uniform4f(loc.uHigh, gm.simColor[0], gm.simColor[1], gm.simColor[2], gm.simColor[3]);
      } else {
        gl.uniform4f(loc.uHigh, 0, 0, 0, 0);
      }

      const mode = wireframeMode ? gl.LINES : gl.TRIANGLES;
      gl.drawElements(mode, gm.count, gl.UNSIGNED_SHORT, 0);
    }

    drawOverlay(pMat, vMat);
  }

  function worldToScreen(p, pMat, vMat) {
    if (!pMat || !vMat) return null;
    const x = p[0], y = p[1], z = p[2];
    const vx = vMat[0]*x + vMat[4]*y + vMat[8]*z + vMat[12];
    const vy = vMat[1]*x + vMat[5]*y + vMat[9]*z + vMat[13];
    const vz = vMat[2]*x + vMat[6]*y + vMat[10]*z + vMat[14];
    const vw = vMat[3]*x + vMat[7]*y + vMat[11]*z + vMat[15];

    const cx = pMat[0]*vx + pMat[4]*vy + pMat[8]*vz + pMat[12]*vw;
    const cy = pMat[1]*vx + pMat[5]*vy + pMat[9]*vz + pMat[13]*vw;
    const cz = pMat[2]*vx + pMat[6]*vy + pMat[10]*vz + pMat[14]*vw;
    const cw = pMat[3]*vx + pMat[7]*vy + pMat[11]*vz + pMat[15]*vw;

    if (cw <= 0.001) return null;
    const ndcX = cx / cw;
    const ndcY = cy / cw;
    if (ndcX < -1.5 || ndcX > 1.5 || ndcY < -1.5 || ndcY > 1.5) return null;

    return [
      (ndcX * 0.5 + 0.5) * overlay.width,
      (1.0 - (ndcY * 0.5 + 0.5)) * overlay.height
    ];
  }

  function drawOverlay(pMat, vMat) {
    const ctx = overlay.getContext('2d');
    ctx.clearRect(0, 0, overlay.width, overlay.height);

    if (modelData.grids && modelData.grids.length > 0) {
      ctx.save();
      ctx.lineWidth = 1;
      ctx.strokeStyle = 'rgba(77, 171, 247, 0.45)';
      ctx.setLineDash([6, 6]);
      ctx.fillStyle = '#4dabf7';
      ctx.font = 'bold 11px sans-serif';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';

      for (const g of modelData.grids) {
        const s = worldToScreen(g.start, pMat, vMat);
        const e = worldToScreen(g.end, pMat, vMat);
        if (s && e) {
          ctx.beginPath();
          ctx.moveTo(s[0], s[1]);
          ctx.lineTo(e[0], e[1]);
          ctx.stroke();

          ctx.setLineDash([]);
          ctx.beginPath();
          ctx.arc(s[0], s[1], 10, 0, Math.PI * 2);
          ctx.fillStyle = '#111';
          ctx.fill();
          ctx.strokeStyle = '#4dabf7';
          ctx.stroke();
          ctx.fillStyle = '#4dabf7';
          ctx.fillText(g.name, s[0], s[1]);

          ctx.beginPath();
          ctx.arc(e[0], e[1], 10, 0, Math.PI * 2);
          ctx.fillStyle = '#111';
          ctx.fill();
          ctx.stroke();
          ctx.fillStyle = '#4dabf7';
          ctx.fillText(g.name, e[0], e[1]);
          ctx.setLineDash([6, 6]);
        }
      }
      ctx.restore();
    }

    if (measureP1) {
      const s1 = worldToScreen(measureP1, pMat, vMat);
      if (s1) {
        ctx.beginPath();
        ctx.arc(s1[0], s1[1], 5, 0, Math.PI * 2);
        ctx.fillStyle = '#ff922b';
        ctx.fill();
        ctx.strokeStyle = '#fff';
        ctx.lineWidth = 2;
        ctx.stroke();
      }
    }
    if (measureP1 && measureP2) {
      const s1 = worldToScreen(measureP1, pMat, vMat);
      const s2 = worldToScreen(measureP2, pMat, vMat);
      if (s1 && s2) {
        ctx.beginPath();
        ctx.arc(s2[0], s2[1], 5, 0, Math.PI * 2);
        ctx.fillStyle = '#ff922b';
        ctx.fill();
        ctx.strokeStyle = '#fff';
        ctx.lineWidth = 2;
        ctx.stroke();

        ctx.beginPath();
        ctx.moveTo(s1[0], s1[1]);
        ctx.lineTo(s2[0], s2[1]);
        ctx.strokeStyle = '#ffa94d';
        ctx.lineWidth = 2;
        ctx.setLineDash([4, 4]);
        ctx.stroke();
        ctx.setLineDash([]);

        const midX = (s1[0] + s2[0]) * 0.5;
        const midY = (s1[1] + s2[1]) * 0.5;
        const dx = measureP2[0] - measureP1[0];
        const dy = measureP2[1] - measureP1[1];
        const dz = measureP2[2] - measureP1[2];
        const d = Math.hypot(dx, dy, dz);

        ctx.fillStyle = 'rgba(20,20,20,0.85)';
        const text = `${d.toFixed(2)} m`;
        ctx.font = 'bold 12px sans-serif';
        const tw = ctx.measureText(text).width;
        ctx.fillRect(midX - tw / 2 - 4, midY - 10, tw + 8, 20);
        ctx.strokeStyle = '#ffa94d';
        ctx.lineWidth = 1;
        ctx.strokeRect(midX - tw / 2 - 4, midY - 10, tw + 8, 20);
        ctx.fillStyle = '#fff';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText(text, midX, midY);
      }
    }

    if (clipBox.en) {
      const bMin = clipBox.min;
      const bMax = clipBox.max;
      const corners = [
        [bMin[0], bMin[1], bMin[2]],
        [bMax[0], bMin[1], bMin[2]],
        [bMax[0], bMax[1], bMin[2]],
        [bMin[0], bMax[1], bMin[2]],
        [bMin[0], bMin[1], bMax[2]],
        [bMax[0], bMin[1], bMax[2]],
        [bMax[0], bMax[1], bMax[2]],
        [bMin[0], bMax[1], bMax[2]]
      ];
      const sc = corners.map(c => worldToScreen(c, pMat, vMat));
      const edges = [
        [0, 1], [1, 2], [2, 3], [3, 0],
        [4, 5], [5, 6], [6, 7], [7, 4],
        [0, 4], [1, 5], [2, 6], [3, 7]
      ];
      ctx.save();
      ctx.strokeStyle = clipBox.invert ? '#ff6b6b' : '#339af0';
      ctx.lineWidth = 1.5;
      ctx.setLineDash([4, 4]);
      ctx.beginPath();
      for (const [i0, i1] of edges) {
        if (sc[i0] && sc[i1]) {
          ctx.moveTo(sc[i0][0], sc[i0][1]);
          ctx.lineTo(sc[i1][0], sc[i1][1]);
        }
      }
      ctx.stroke();
      ctx.restore();
    }
  }

  function easeInOutCubic(t) {
    return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
  }

  function animateCameraTo(newTarget, newRadius, newTheta, newPhi, durationMs = 800, onComplete = null) {
    if (currentCamAnim) cancelAnimationFrame(currentCamAnim);

    const startTarget = [...camTarget];
    const startRadius = camRadius;
    const startTheta = camTheta;
    const startPhi = camPhi;
    const startTime = performance.now();

    function step(now) {
      const elapsed = now - startTime;
      const progress = Math.min(1.0, elapsed / durationMs);
      const ease = easeInOutCubic(progress);

      camTarget = [
        startTarget[0] + (newTarget[0] - startTarget[0]) * ease,
        startTarget[1] + (newTarget[1] - startTarget[1]) * ease,
        startTarget[2] + (newTarget[2] - startTarget[2]) * ease
      ];
      camRadius = startRadius + (newRadius - startRadius) * ease;
      camTheta = startTheta + (newTheta - startTheta) * ease;
      camPhi = startPhi + (newPhi - startPhi) * ease;

      render();

      if (progress < 1.0) {
        currentCamAnim = requestAnimationFrame(step);
      } else {
        currentCamAnim = null;
        if (onComplete) onComplete();
      }
    }

    currentCamAnim = requestAnimationFrame(step);
  }

  function toggleTurntable() {
    turntableActive = !turntableActive;
    const btn = document.getElementById('btn-turntable');
    if (btn) btn.classList.toggle('active', turntableActive);
    if (turntableActive) {
      if (tourActive) stopTour();
      animateTurntable();
    }
  }

  function animateTurntable() {
    if (!turntableActive) return;
    camPhi += 0.006;
    render();
    requestAnimationFrame(animateTurntable);
  }

  function startTour() {
    if (!modelData.viewpoints || modelData.viewpoints.length === 0) return;
    tourActive = true;
    const btn = document.getElementById('btn-play-tour');
    if (btn) {
      btn.textContent = '⏸ Pause Tour';
      btn.classList.add('active');
    }
    if (turntableActive) toggleTurntable();
    playNextTourStop();
  }

  function stopTour() {
    tourActive = false;
    if (tourTimer) clearTimeout(tourTimer);
    const btn = document.getElementById('btn-play-tour');
    if (btn) {
      btn.textContent = '▶ Play Tour';
      btn.classList.remove('active');
    }
  }

  function playNextTourStop() {
    if (!tourActive || !modelData.viewpoints || modelData.viewpoints.length === 0) return;
    const vp = modelData.viewpoints[tourIndex];
    if (vp && vp.camera && vp.camera.position && vp.camera.target) {
      const newTarget = [...vp.camera.target];
      const ex = vp.camera.position[0] - newTarget[0];
      const ey = vp.camera.position[1] - newTarget[1];
      const ez = vp.camera.position[2] - newTarget[2];
      const newRadius = Math.hypot(ex, ey, ez) || 10;
      const newTheta = Math.acos(ez / newRadius);
      const newPhi = Math.atan2(ey, ex);

      animateCameraTo(newTarget, newRadius, newTheta, newPhi, 1200, () => {
        if (!tourActive) return;
        tourIndex = (tourIndex + 1) % modelData.viewpoints.length;
        tourTimer = setTimeout(playNextTourStop, 2000);
      });
    } else {
      tourIndex = (tourIndex + 1) % modelData.viewpoints.length;
      tourTimer = setTimeout(playNextTourStop, 1000);
    }
  }

  function pickPoint(clientX, clientY) {
    const rect = canvas.getBoundingClientRect();
    const mx = clientX - rect.left;
    const my = clientY - rect.top;

    let bestDist = 40;
    let bestPt = null;

    for (const m of modelData.meshes) {
      const v = m.vertices;
      for (let i = 0; i < v.length; i += 3) {
        const pt = [v[i], v[i+1], v[i+2]];
        const sp = worldToScreen(pt, lastPMat, lastVMat);
        if (sp) {
          const d = Math.hypot(sp[0] - mx, sp[1] - my);
          if (d < bestDist) {
            bestDist = d;
            bestPt = pt;
          }
        }
      }
    }

    return bestPt || [camTarget[0], camTarget[1], camTarget[2]];
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

  // Element Selection & Property Inspector & Measure
  canvas.addEventListener('click', e => {
    if (Math.abs(e.clientX - lastMouseX) > 3 || Math.abs(e.clientY - lastMouseY) > 3) return;

    if (measureMode) {
      const pt = pickPoint(e.clientX, e.clientY);
      if (!measureP1) {
        measureP1 = pt;
        document.getElementById('measure-status').textContent = 'Click second point';
      } else {
        measureP2 = pt;
        const dx = measureP2[0] - measureP1[0];
        const dy = measureP2[1] - measureP1[1];
        const dz = measureP2[2] - measureP1[2];
        const dist = Math.hypot(dx, dy, dz);
        const horiz = Math.hypot(dx, dy);
        const pitch = dist > 1e-6 ? Math.asin(Math.max(-1, Math.min(1, dz / dist))) * 180 / Math.PI : 0;

        document.getElementById('measure-status').textContent = 'Measurement complete:';
        document.getElementById('measure-results').style.display = 'block';
        document.getElementById('m-dist').textContent = `${dist.toFixed(3)} m`;
        document.getElementById('m-horiz').textContent = `${horiz.toFixed(3)} m`;
        document.getElementById('m-deltas').textContent = `${dx.toFixed(2)} / ${dy.toFixed(2)} / ${dz.toFixed(2)} m`;
        document.getElementById('m-pitch').textContent = `${pitch.toFixed(1)}°`;
      }
      requestAnimationFrame(render);
      return;
    }

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
  const vpsContainer = document.getElementById('vps-list') || document.getElementById('tab-vps');
  if (modelData.viewpoints && modelData.viewpoints.length > 0) {
    modelData.viewpoints.forEach(vp => {
      const item = document.createElement('div');
      item.className = 'tree-item';
      item.textContent = vp.name;
      item.addEventListener('click', () => {
        if (vp.camera && vp.camera.position && vp.camera.target) {
          const newTarget = [...vp.camera.target];
          const ex = vp.camera.position[0]-newTarget[0];
          const ey = vp.camera.position[1]-newTarget[1];
          const ez = vp.camera.position[2]-newTarget[2];
          const newRadius = Math.hypot(ex, ey, ez) || 10;
          const newTheta = Math.acos(ez / newRadius);
          const newPhi = Math.atan2(ey, ex);
          animateCameraTo(newTarget, newRadius, newTheta, newPhi);
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
            animateCameraTo([...clash.point], 4.0, camTheta, camPhi);
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

  // Sets Tab
  const setsTabBtn = document.getElementById('tab-btn-sets');
  const setsContainer = document.getElementById('tab-sets');
  if (modelData.selectionSets && modelData.selectionSets.length > 0) {
    if (setsTabBtn) setsTabBtn.style.display = 'inline-block';

    function renderSetItems(items, container) {
      items.forEach(item => {
        if (item.type === 'folder') {
          const folderDiv = document.createElement('div');
          folderDiv.style.fontWeight = '600';
          folderDiv.style.fontSize = '12px';
          folderDiv.style.margin = '6px 0 2px 0';
          folderDiv.textContent = '📁 ' + item.name;
          container.appendChild(folderDiv);
          if (item.children && item.children.length > 0) {
            const subDiv = document.createElement('div');
            subDiv.style.paddingLeft = '12px';
            renderSetItems(item.children, subDiv);
            container.appendChild(subDiv);
          }
        } else {
          const itemDiv = document.createElement('div');
          itemDiv.className = 'tree-item';
          itemDiv.textContent = (item.type === 'search' ? '🔍 ' : '📌 ') + item.name;
          itemDiv.addEventListener('click', () => {
            selectedSetMeshIndices.clear();
            if (item.nodeIds && item.nodeIds.length > 0) {
              const idSet = new Set(item.nodeIds);
              gpuMeshes.forEach(gm => {
                if ((gm.raw.nodeId !== undefined && idSet.has(gm.raw.nodeId)) || idSet.has(gm.raw.id)) {
                  selectedSetMeshIndices.add(gm.index);
                }
              });
            }
            requestAnimationFrame(render);
          });
          container.appendChild(itemDiv);
        }
      });
    }

    renderSetItems(modelData.selectionSets, setsContainer);
  } else if (setsContainer) {
    setsContainer.innerHTML = '<div style="color:#666;font-size:11px;padding:8px;">No selection sets.</div>';
  }

  // 4D TimeLiner Controls
  const btnTl = document.getElementById('btn-timeliner');
  const tlBar = document.getElementById('timeliner-bar');
  const tlPlay = document.getElementById('tl-play');
  const tlSlider = document.getElementById('tl-slider');
  const tlDate = document.getElementById('tl-date');
  const tlSpeed = document.getElementById('tl-speed');

  if (modelData.timeliner && modelData.timeliner.tasks && modelData.timeliner.tasks.length > 0) {
    btnTl.style.display = 'inline-block';
    const tlStart = new Date(modelData.timeliner.start).getTime();
    const tlEnd = new Date(modelData.timeliner.end).getTime();
    const tlSpan = Math.max(1, tlEnd - tlStart);
    let tlActive = false;
    let tlPlaying = false;
    let tlInterval = null;

    btnTl.addEventListener('click', function() {
      tlActive = !tlActive;
      tlBar.style.display = tlActive ? 'flex' : 'none';
      this.classList.toggle('active', tlActive);
      if (!tlActive && tlPlaying) stopTlPlay();
      updateTlSimulation();
    });

    function getSimDate(val) {
      return new Date(tlStart + (val / 1000.0) * tlSpan);
    }

    function updateTlSimulation() {
      const simDate = getSimDate(parseFloat(tlSlider.value));
      tlDate.textContent = simDate.toISOString().slice(0, 10);

      if (!tlActive) {
        gpuMeshes.forEach(gm => { gm.hidden = false; gm.simColor = null; });
        requestAnimationFrame(render);
        return;
      }

      const simTime = simDate.getTime();
      const nodeState = new Map();
      modelData.timeliner.tasks.forEach(t => {
        const tStart = new Date(t.start).getTime();
        const tEnd = new Date(t.end).getTime();
        let state = 'NotStarted';
        if (simTime >= tEnd) state = 'Completed';
        else if (simTime >= tStart) state = 'InProgress';

        (t.nodeIds || []).forEach(nid => {
          nodeState.set(nid, { type: t.taskType, state: state });
        });
      });

      gpuMeshes.forEach(gm => {
        const nid = gm.raw.nodeId !== undefined ? gm.raw.nodeId : gm.raw.id;
        const st = nodeState.get(nid);
        if (!st) {
          gm.hidden = false;
          gm.simColor = null;
          return;
        }

        if (st.type === 'Construct') {
          if (st.state === 'NotStarted') {
            gm.hidden = true;
            gm.simColor = null;
          } else if (st.state === 'InProgress') {
            gm.hidden = false;
            gm.simColor = [0.2, 0.95, 0.2, 0.85];
          } else {
            gm.hidden = false;
            gm.simColor = null;
          }
        } else if (st.type === 'Demolish') {
          if (st.state === 'NotStarted') {
            gm.hidden = false;
            gm.simColor = null;
          } else if (st.state === 'InProgress') {
            gm.hidden = false;
            gm.simColor = [0.95, 0.2, 0.2, 0.85];
          } else {
            gm.hidden = true;
            gm.simColor = null;
          }
        } else if (st.type === 'Temporary') {
          if (st.state === 'InProgress') {
            gm.hidden = false;
            gm.simColor = [0.95, 0.85, 0.2, 0.85];
          } else {
            gm.hidden = true;
            gm.simColor = null;
          }
        }
      });

      requestAnimationFrame(render);
    }

    tlSlider.addEventListener('input', () => {
      updateTlSimulation();
    });

    function startTlPlay() {
      tlPlaying = true;
      tlPlay.textContent = '⏸ Pause';
      const speed = parseFloat(tlSpeed.value) || 1;
      const step = 2 * speed;
      tlInterval = setInterval(() => {
        let val = parseFloat(tlSlider.value) + step;
        if (val >= 1000) {
          val = 1000;
          tlSlider.value = val;
          stopTlPlay();
        } else {
          tlSlider.value = val;
        }
        updateTlSimulation();
      }, 50);
    }

    function stopTlPlay() {
      tlPlaying = false;
      tlPlay.textContent = '▶ Play';
      if (tlInterval) {
        clearInterval(tlInterval);
        tlInterval = null;
      }
    }

    tlPlay.addEventListener('click', () => {
      if (tlPlaying) {
        stopTlPlay();
      } else {
        if (parseFloat(tlSlider.value) >= 1000) tlSlider.value = 0;
        startTlPlay();
      }
    });

    tlSpeed.addEventListener('change', () => {
      if (tlPlaying) {
        stopTlPlay();
        startTlPlay();
      }
    });
  }

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
    const resetTarget = [
      (bbox.min[0] + bbox.max[0]) * 0.5,
      (bbox.min[1] + bbox.max[1]) * 0.5,
      (bbox.min[2] + bbox.max[2]) * 0.5
    ];
    const resetRadius = Math.max(Math.hypot(bbox.max[0]-bbox.min[0], bbox.max[1]-bbox.min[1], bbox.max[2]-bbox.min[2]) * 1.2, 5.0);
    animateCameraTo(resetTarget, resetRadius, 0.8, 0.5);
  });

  const btnTurntable = document.getElementById('btn-turntable');
  if (btnTurntable) {
    btnTurntable.addEventListener('click', toggleTurntable);
  }

  const btnTour = document.getElementById('btn-play-tour');
  if (btnTour) {
    btnTour.addEventListener('click', () => {
      if (tourActive) stopTour();
      else startTour();
    });
  }

  document.getElementById('btn-wireframe').addEventListener('click', function() {
    wireframeMode = !wireframeMode;
    this.classList.toggle('active', wireframeMode);
    requestAnimationFrame(render);
  });

  document.getElementById('btn-measure').addEventListener('click', function() {
    measureMode = !measureMode;
    this.classList.toggle('active', measureMode);
    document.getElementById('measure-panel').style.display = measureMode ? 'block' : 'none';
    if (!measureMode) {
      measureP1 = null; measureP2 = null;
      document.getElementById('measure-results').style.display = 'none';
      document.getElementById('measure-status').textContent = 'Click 2 points to measure';
    }
    requestAnimationFrame(render);
  });

  document.getElementById('measure-clear').addEventListener('click', () => {
    measureP1 = null; measureP2 = null;
    document.getElementById('measure-results').style.display = 'none';
    document.getElementById('measure-status').textContent = 'Click 2 points to measure';
    requestAnimationFrame(render);
  });

  const secControls = document.getElementById('sec-controls');
  document.getElementById('btn-toggle-sec').addEventListener('click', function() {
    const isVis = secControls.style.display !== 'none';
    secControls.style.display = isVis ? 'none' : 'flex';
    this.classList.toggle('active', !isVis);
  });

  // Sectioning: Planes vs Box Mode
  const secPlanesUi = document.getElementById('sec-planes-ui');
  const secBoxUi = document.getElementById('sec-box-ui');
  const secModePlanes = document.getElementById('sec-mode-planes');
  const secModeBox = document.getElementById('sec-mode-box');
  if (secModePlanes && secModeBox) {
    secModePlanes.addEventListener('change', () => {
      if (secPlanesUi) secPlanesUi.style.display = 'flex';
      if (secBoxUi) secBoxUi.style.display = 'none';
      requestAnimationFrame(render);
    });
    secModeBox.addEventListener('change', () => {
      if (secPlanesUi) secPlanesUi.style.display = 'none';
      if (secBoxUi) secBoxUi.style.display = 'flex';
      requestAnimationFrame(render);
    });
  }

  document.getElementById('clip-x-en').addEventListener('change', e => { clipPlanes.xEn = e.target.checked; requestAnimationFrame(render); });
  document.getElementById('clip-y-en').addEventListener('change', e => { clipPlanes.yEn = e.target.checked; requestAnimationFrame(render); });
  document.getElementById('clip-z-en').addEventListener('change', e => { clipPlanes.zEn = e.target.checked; requestAnimationFrame(render); });

  document.getElementById('clip-x-val').addEventListener('input', e => { clipPlanes.xVal = (e.target.value/100)*(bbox.max[0]-bbox.min[0]) + camTarget[0]; requestAnimationFrame(render); });
  document.getElementById('clip-y-val').addEventListener('input', e => { clipPlanes.yVal = (e.target.value/100)*(bbox.max[1]-bbox.min[1]) + camTarget[1]; requestAnimationFrame(render); });
  document.getElementById('clip-z-val').addEventListener('input', e => { clipPlanes.zVal = (e.target.value/100)*(bbox.max[2]-bbox.min[2]) + camTarget[2]; requestAnimationFrame(render); });

  // Section Box Controls
  const clipBoxEn = document.getElementById('clip-box-en');
  if (clipBoxEn) {
    clipBoxEn.addEventListener('change', e => {
      clipBox.en = e.target.checked;
      requestAnimationFrame(render);
    });
  }

  const clipBoxInvert = document.getElementById('clip-box-invert');
  if (clipBoxInvert) {
    clipBoxInvert.addEventListener('change', e => {
      clipBox.invert = e.target.checked;
      requestAnimationFrame(render);
    });
  }

  function resetSectionBox() {
    clipBox.min = [bbox.min[0], bbox.min[1], bbox.min[2]];
    clipBox.max = [bbox.max[0], bbox.max[1], bbox.max[2]];
    const scaleSlider = document.getElementById('clip-box-scale');
    if (scaleSlider) scaleSlider.value = '100';
    requestAnimationFrame(render);
  }

  const btnResetBox = document.getElementById('btn-reset-box');
  if (btnResetBox) {
    btnResetBox.addEventListener('click', resetSectionBox);
  }

  const btnFitBox = document.getElementById('btn-fit-box');
  if (btnFitBox) {
    btnFitBox.addEventListener('click', () => {
      if (selectedMeshIndex >= 0 && selectedMeshIndex < gpuMeshes.length) {
        const gm = gpuMeshes[selectedMeshIndex];
        const b = gm.bounds;
        if (b && b.min && b.max) {
          clipBox.min = [...b.min];
          clipBox.max = [...b.max];
          const scaleSlider = document.getElementById('clip-box-scale');
          if (scaleSlider) scaleSlider.value = '100';
          if (clipBoxEn) clipBoxEn.checked = true;
          clipBox.en = true;
          requestAnimationFrame(render);
          return;
        }
      }
      resetSectionBox();
    });
  }

  const clipBoxScale = document.getElementById('clip-box-scale');
  if (clipBoxScale) {
    clipBoxScale.addEventListener('input', e => {
      const scale = parseFloat(e.target.value) / 100.0;
      const cx = (bbox.min[0] + bbox.max[0]) * 0.5;
      const cy = (bbox.min[1] + bbox.max[1]) * 0.5;
      const cz = (bbox.min[2] + bbox.max[2]) * 0.5;
      const hx = (bbox.max[0] - bbox.min[0]) * 0.5 * scale;
      const hy = (bbox.max[1] - bbox.min[1]) * 0.5 * scale;
      const hz = (bbox.max[2] - bbox.min[2]) * 0.5 * scale;
      clipBox.min = [cx - hx, cy - hy, cz - hz];
      clipBox.max = [cx + hx, cy + hy, cz + hz];
      requestAnimationFrame(render);
    });
  }

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
            writer.WriteNumber("id", mesh.Id);
            if (mesh.Node is not null)
            {
                writer.WriteNumber("nodeId", mesh.Node.Id);
            }
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

        // Grids
        if (options.IncludeGrids && options.GridSystem is not null && options.GridSystem.Lines.Count > 0)
        {
            writer.WriteStartArray("grids");
            foreach (var line in options.GridSystem.Lines)
            {
                writer.WriteStartObject();
                writer.WriteString("name", line.Name);
                writer.WriteStartArray("start");
                writer.WriteNumberValue(line.Start.X);
                writer.WriteNumberValue(line.Start.Y);
                writer.WriteNumberValue(line.Start.Z);
                writer.WriteEndArray();
                writer.WriteStartArray("end");
                writer.WriteNumberValue(line.End.X);
                writer.WriteNumberValue(line.End.Y);
                writer.WriteNumberValue(line.End.Z);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        // Selection Sets
        if (options.IncludeSelectionSets && options.SelectionSets is not null && options.SelectionSets.Count > 0)
        {
            writer.WriteStartArray("selectionSets");
            foreach (var item in options.SelectionSets)
            {
                WriteSetItem(writer, item);
            }
            writer.WriteEndArray();
        }

        // 4D TimeLiner
        if (options.IncludeTimeLiner && options.TimeLiner is not null && options.TimeLiner.Tasks.Count > 0)
        {
            var start = options.TimeLiner.OverallStart;
            var end = options.TimeLiner.OverallEnd;
            if (start.HasValue && end.HasValue)
            {
                writer.WriteStartObject("timeliner");
                writer.WriteString("start", start.Value.ToString("o", CultureInfo.InvariantCulture));
                writer.WriteString("end", end.Value.ToString("o", CultureInfo.InvariantCulture));
                writer.WriteStartArray("tasks");
                foreach (var task in options.TimeLiner.FlattenTasks())
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", task.Id);
                    writer.WriteString("name", task.Name);
                    writer.WriteString("taskType", task.TaskType.ToString());
                    writer.WriteString("start", task.PlannedStart.ToString("o", CultureInfo.InvariantCulture));
                    writer.WriteString("end", task.PlannedEnd.ToString("o", CultureInfo.InvariantCulture));
                    writer.WriteStartArray("nodeIds");
                    foreach (var nodeId in task.AttachedNodeIds)
                    {
                        writer.WriteNumberValue(nodeId);
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
        }

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteSetItem(Utf8JsonWriter writer, Sets.NwcSetItem item)
    {
        writer.WriteStartObject();
        writer.WriteString("name", item.Name);
        if (item is Sets.NwcSelectionSet sel)
        {
            writer.WriteString("type", "selection");
            writer.WriteStartArray("nodeIds");
            foreach (var id in sel.NodeIds)
            {
                writer.WriteNumberValue(id);
            }
            writer.WriteEndArray();
        }
        else if (item is Sets.NwcSelectionSetFolder folder)
        {
            writer.WriteString("type", "folder");
            writer.WriteStartArray("children");
            foreach (var child in folder.Children)
            {
                WriteSetItem(writer, child);
            }
            writer.WriteEndArray();
        }
        else if (item is Sets.NwcSearchSet search)
        {
            writer.WriteString("type", "search");
            writer.WriteNumber("conditionCount", search.Conditions.Count);
        }
        writer.WriteEndObject();
    }
}
