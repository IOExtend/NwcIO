using System.Globalization;
using System.Net;
using System.Text;
using NwcIO.Model;

namespace NwcIO.Internal.Export;

/// <summary>
/// Generates an interactive, self-contained HTML clash report with cross-discipline clash matrix,
/// summary KPI cards, and filterable/sortable table of detected clashes.
/// </summary>
internal static class HtmlClashReportExporter
{
    public static void WriteFile(
        IReadOnlyList<NwcClash> clashes,
        string path,
        string? assemblyName = null,
        int modelCount = 0,
        int meshCount = 0)
    {
        ArgumentNullException.ThrowIfNull(clashes);
        ArgumentNullException.ThrowIfNull(path);

        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var sw = new StreamWriter(path, false, new UTF8Encoding(false));
        Write(clashes, sw, assemblyName, modelCount, meshCount);
    }

    public static void Write(
        IReadOnlyList<NwcClash> clashes,
        TextWriter writer,
        string? assemblyName = null,
        int modelCount = 0,
        int meshCount = 0)
    {
        ArgumentNullException.ThrowIfNull(clashes);
        ArgumentNullException.ThrowIfNull(writer);

        assemblyName = string.IsNullOrWhiteSpace(assemblyName) ? "Federated Assembly" : assemblyName;
        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC", CultureInfo.InvariantCulture);

        // Aggregate statistics
        double totalVolume = 0.0;
        double maxVolume = 0.0;
        var modelNamesSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Model pair clash counts for matrix
        var matrixCounts = new Dictionary<(string, string), int>();

        for (int i = 0; i < clashes.Count; i++)
        {
            var clash = clashes[i];
            totalVolume += clash.Volume;
            if (clash.Volume > maxVolume) maxVolume = clash.Volume;

            modelNamesSet.Add(clash.ModelAName);
            modelNamesSet.Add(clash.ModelBName);

            string a = clash.ModelAName;
            string b = clash.ModelBName;
            var key1 = (a, b);
            var key2 = (b, a);

            matrixCounts[key1] = matrixCounts.GetValueOrDefault(key1) + 1;
            matrixCounts[key2] = matrixCounts.GetValueOrDefault(key2) + 1;
        }

        var uniqueModels = modelNamesSet.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
        if (modelCount <= 0) modelCount = Math.Max(uniqueModels.Count, 2);

        writer.WriteLine("<!DOCTYPE html>");
        writer.WriteLine("<html lang=\"en\">");
        writer.WriteLine("<head>");
        writer.WriteLine("  <meta charset=\"utf-8\" />");
        writer.WriteLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />");
        writer.WriteLine($"  <title>NwcIO Clash Report — {WebUtility.HtmlEncode(assemblyName)}</title>");
        writer.WriteLine("  <style>");
        writer.WriteLine(CssStyles);
        writer.WriteLine("  </style>");
        writer.WriteLine("</head>");
        writer.WriteLine("<body>");

        // Top Navigation / Header
        writer.WriteLine("  <header class=\"header\">");
        writer.WriteLine("    <div class=\"brand\">");
        writer.WriteLine("      <div class=\"logo-badge\"><span class=\"badge-dot green\"></span><span class=\"badge-dot blue\"></span></div>");
        writer.WriteLine("      <div class=\"brand-title\">");
        writer.WriteLine($"        <h1>NwcIO Clash Detection Report</h1>");
        writer.WriteLine($"        <div class=\"subtitle\">Assembly: <strong>{WebUtility.HtmlEncode(assemblyName)}</strong> &bull; Generated: {timestamp}</div>");
        writer.WriteLine("      </div>");
        writer.WriteLine("    </div>");
        writer.WriteLine("    <div class=\"header-tag\">Pure Managed .NET BIM</div>");
        writer.WriteLine("  </header>");

        writer.WriteLine("  <main class=\"container\">");

        // Metrics KPI Cards
        writer.WriteLine("    <section class=\"kpi-grid\">");
        writer.WriteLine($"      <div class=\"kpi-card danger\"><div class=\"kpi-val\">{clashes.Count}</div><div class=\"kpi-label\">Total Clashes</div></div>");
        writer.WriteLine($"      <div class=\"kpi-card\"><div class=\"kpi-val\">{modelCount}</div><div class=\"kpi-label\">Federated Models</div></div>");
        if (meshCount > 0)
        {
            writer.WriteLine($"      <div class=\"kpi-card\"><div class=\"kpi-val\">{meshCount}</div><div class=\"kpi-label\">Total Meshes</div></div>");
        }
        writer.WriteLine($"      <div class=\"kpi-card\"><div class=\"kpi-val\">{totalVolume:F4} m³</div><div class=\"kpi-label\">Total Clash Volume</div></div>");
        writer.WriteLine($"      <div class=\"kpi-card\"><div class=\"kpi-val\">{maxVolume:F4} m³</div><div class=\"kpi-label\">Max Overlap Volume</div></div>");
        writer.WriteLine("    </section>");

        // Discipline Matrix Section (if at least 2 models)
        if (uniqueModels.Count >= 2)
        {
            writer.WriteLine("    <section class=\"section-card\">");
            writer.WriteLine("      <h2>Discipline Cross-Clash Matrix</h2>");
            writer.WriteLine("      <p class=\"section-desc\">Pairwise conflict frequency between contributing discipline models. Click on a highlighted cell to filter the clash list.</p>");
            writer.WriteLine("      <div class=\"table-responsive\">");
            writer.WriteLine("        <table class=\"matrix-table\">");
            writer.WriteLine("          <thead>");
            writer.WriteLine("            <tr>");
            writer.WriteLine("              <th>Model Discipline</th>");
            foreach (var col in uniqueModels)
            {
                writer.WriteLine($"              <th title=\"{WebUtility.HtmlEncode(col)}\">{WebUtility.HtmlEncode(col)}</th>");
            }
            writer.WriteLine("            </tr>");
            writer.WriteLine("          </thead>");
            writer.WriteLine("          <tbody>");

            foreach (var row in uniqueModels)
            {
                writer.WriteLine("            <tr>");
                writer.WriteLine($"              <th class=\"row-header\">{WebUtility.HtmlEncode(row)}</th>");
                foreach (var col in uniqueModels)
                {
                    if (string.Equals(row, col, StringComparison.OrdinalIgnoreCase))
                    {
                        writer.WriteLine("              <td class=\"cell-diag\">&mdash;</td>");
                    }
                    else
                    {
                        int count = matrixCounts.GetValueOrDefault((row, col));
                        if (count > 0)
                        {
                            // The names are data attributes, never JavaScript: the browser decodes &#39; back to ' in an
                            // attribute before running its handler, so a name inside the handler's string could end it
                            writer.WriteLine($"              <td class=\"cell-clash\" data-model-a=\"{WebUtility.HtmlEncode(row)}\" data-model-b=\"{WebUtility.HtmlEncode(col)}\" onclick=\"filterPair(this.dataset.modelA, this.dataset.modelB)\" title=\"Filter clashes between {WebUtility.HtmlEncode(row)} and {WebUtility.HtmlEncode(col)}\"><span class=\"clash-badge\">{count}</span></td>");
                        }
                        else
                        {
                            writer.WriteLine("              <td class=\"cell-zero\">0</td>");
                        }
                    }
                }
                writer.WriteLine("            </tr>");
            }

            writer.WriteLine("          </tbody>");
            writer.WriteLine("        </table>");
            writer.WriteLine("      </div>");
            writer.WriteLine("    </section>");
        }

        // Clash Details Table
        writer.WriteLine("    <section class=\"section-card\">");
        writer.WriteLine("      <div class=\"table-header-row\">");
        writer.WriteLine("        <div>");
        writer.WriteLine("          <h2>Clash Details List</h2>");
        writer.WriteLine("          <p class=\"section-desc\">Interactive inspection of 3D spatial intersections with coordinates, volumes, and buildingSMART IFC GUIDs.</p>");
        writer.WriteLine("        </div>");
        writer.WriteLine("        <div class=\"filter-controls\">");
        writer.WriteLine("          <input type=\"text\" id=\"searchInput\" placeholder=\"Search model, mesh ID, GUID...\" onkeyup=\"applyFilters()\" />");
        writer.WriteLine("          <button id=\"resetBtn\" class=\"btn-reset\" onclick=\"resetFilters()\">Reset Filters</button>");
        writer.WriteLine("        </div>");
        writer.WriteLine("      </div>");

        writer.WriteLine("      <div class=\"table-responsive\">");
        writer.WriteLine("        <table id=\"clashTable\" class=\"clash-table\">");
        writer.WriteLine("          <thead>");
        writer.WriteLine("            <tr>");
        writer.WriteLine("              <th onclick=\"sortTable(0)\" class=\"sortable\">#</th>");
        writer.WriteLine("              <th onclick=\"sortTable(1)\" class=\"sortable\">Model A</th>");
        writer.WriteLine("              <th>Mesh A (Id / IFC GUID)</th>");
        writer.WriteLine("              <th onclick=\"sortTable(3)\" class=\"sortable\">Model B</th>");
        writer.WriteLine("              <th>Mesh B (Id / IFC GUID)</th>");
        writer.WriteLine("              <th>Center (X, Y, Z)</th>");
        writer.WriteLine("              <th onclick=\"sortTable(6)\" class=\"sortable\">Volume (m³)</th>");
        writer.WriteLine("              <th>Intersection Box [Min &rarr; Max]</th>");
        writer.WriteLine("            </tr>");
        writer.WriteLine("          </thead>");
        writer.WriteLine("          <tbody>");

        for (int i = 0; i < clashes.Count; i++)
        {
            var clash = clashes[i];
            var box = clash.IntersectionBox;
            writer.WriteLine($"            <tr data-model-a=\"{WebUtility.HtmlEncode(clash.ModelAName)}\" data-model-b=\"{WebUtility.HtmlEncode(clash.ModelBName)}\">");
            writer.WriteLine($"              <td class=\"col-idx\">{i + 1}</td>");
            writer.WriteLine($"              <td class=\"col-model\"><span class=\"model-pill a\">{WebUtility.HtmlEncode(clash.ModelAName)}</span></td>");
            writer.WriteLine($"              <td class=\"col-guid\"><strong>#{clash.MeshA.Id}</strong><br /><code class=\"guid\">{clash.IfcGuidA}</code></td>");
            writer.WriteLine($"              <td class=\"col-model\"><span class=\"model-pill b\">{WebUtility.HtmlEncode(clash.ModelBName)}</span></td>");
            writer.WriteLine($"              <td class=\"col-guid\"><strong>#{clash.MeshB.Id}</strong><br /><code class=\"guid\">{clash.IfcGuidB}</code></td>");
            writer.WriteLine($"              <td class=\"col-center\">({clash.Center.X:F3}, {clash.Center.Y:F3}, {clash.Center.Z:F3})</td>");
            writer.WriteLine($"              <td class=\"col-vol\" data-val=\"{clash.Volume.ToString("G", CultureInfo.InvariantCulture)}\"><strong>{clash.Volume:F4}</strong></td>");
            writer.WriteLine($"              <td class=\"col-box\"><small>[{box.Min.X:F2}, {box.Min.Y:F2}, {box.Min.Z:F2}] &rarr; [{box.Max.X:F2}, {box.Max.Y:F2}, {box.Max.Z:F2}]</small></td>");
            writer.WriteLine("            </tr>");
        }

        if (clashes.Count == 0)
        {
            writer.WriteLine("            <tr><td colspan=\"8\" class=\"no-data\">No geometric clashes detected. Models are clear.</td></tr>");
        }

        writer.WriteLine("          </tbody>");
        writer.WriteLine("        </table>");
        writer.WriteLine("      </div>");
        writer.WriteLine("    </section>");

        writer.WriteLine("  </main>");

        // Footer
        writer.WriteLine("  <footer class=\"footer\">");
        writer.WriteLine("    Generated by <a href=\"https://github.com/IOExtend/NwcIO\" target=\"_blank\" rel=\"noreferrer\">NwcIO</a> &bull; Pure managed .NET BIM Collaboration and Spatial Engine");
        writer.WriteLine("  </footer>");

        // Client Scripts
        writer.WriteLine("  <script>");
        writer.WriteLine(ClientJs);
        writer.WriteLine("  </script>");

        writer.WriteLine("</body>");
        writer.WriteLine("</html>");
    }

    private const string CssStyles = """
      :root {
        --bg-main: #0B0F17;
        --bg-card: #151D2A;
        --bg-card-hover: #1E293B;
        --bg-header: #0F172A;
        --border-color: #334155;
        --text-primary: #F8FAFC;
        --text-secondary: #94A3B8;
        --text-muted: #64748B;
        --accent-green: #22C55E;
        --accent-blue: #3B82F6;
        --accent-rose: #F43F5E;
        --accent-amber: #F59E0B;
        --font-sans: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
        --font-mono: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace;
      }
      * { box-sizing: border-box; margin: 0; padding: 0; }
      body {
        background-color: var(--bg-main);
        color: var(--text-primary);
        font-family: var(--font-sans);
        line-height: 1.5;
        min-height: 100vh;
        display: flex;
        flex-direction: column;
      }
      .header {
        background: linear-gradient(180deg, #111827 0%, #0F172A 100%);
        border-bottom: 1px solid var(--border-color);
        padding: 1.25rem 2rem;
        display: flex;
        justify-content: space-between;
        align-items: center;
      }
      .brand { display: flex; align-items: center; gap: 1rem; }
      .logo-badge { display: flex; gap: 0.35rem; }
      .badge-dot { width: 14px; height: 14px; border-radius: 50%; display: inline-block; }
      .badge-dot.green { background: var(--accent-green); box-shadow: 0 0 10px rgba(34, 197, 94, 0.6); }
      .badge-dot.blue { background: var(--accent-blue); box-shadow: 0 0 10px rgba(59, 130, 246, 0.6); }
      .brand-title h1 { font-size: 1.35rem; font-weight: 700; color: #FFFFFF; letter-spacing: -0.02em; }
      .subtitle { font-size: 0.85rem; color: var(--text-secondary); margin-top: 0.15rem; }
      .header-tag {
        font-size: 0.75rem;
        font-weight: 600;
        background: rgba(59, 130, 246, 0.15);
        color: #60A5FA;
        border: 1px solid rgba(59, 130, 246, 0.3);
        padding: 0.35rem 0.75rem;
        border-radius: 9999px;
        text-transform: uppercase;
        letter-spacing: 0.05em;
      }
      .container { max-width: 1380px; width: 100%; margin: 2rem auto; padding: 0 1.5rem; flex: 1; }
      .kpi-grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
        gap: 1.25rem;
        margin-bottom: 2rem;
      }
      .kpi-card {
        background: var(--bg-card);
        border: 1px solid var(--border-color);
        border-radius: 0.75rem;
        padding: 1.25rem;
        text-align: center;
        box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.3);
        transition: transform 0.15s ease, border-color 0.15s ease;
      }
      .kpi-card:hover { transform: translateY(-2px); border-color: var(--accent-blue); }
      .kpi-card.danger { border-left: 4px solid var(--accent-rose); }
      .kpi-val { font-size: 2rem; font-weight: 700; color: #FFFFFF; font-feature-settings: "tnum"; }
      .kpi-card.danger .kpi-val { color: #FB7185; }
      .kpi-label { font-size: 0.8rem; text-transform: uppercase; letter-spacing: 0.05em; color: var(--text-muted); margin-top: 0.25rem; }
      .section-card {
        background: var(--bg-card);
        border: 1px solid var(--border-color);
        border-radius: 0.75rem;
        padding: 1.5rem;
        margin-bottom: 2rem;
        box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.25);
      }
      .section-card h2 { font-size: 1.2rem; font-weight: 600; color: #FFFFFF; margin-bottom: 0.25rem; }
      .section-desc { font-size: 0.85rem; color: var(--text-secondary); margin-bottom: 1.25rem; }
      .table-header-row { display: flex; justify-content: space-between; align-items: flex-end; flex-wrap: wrap; gap: 1rem; margin-bottom: 1rem; }
      .filter-controls { display: flex; gap: 0.75rem; align-items: center; }
      #searchInput {
        background: #0F172A;
        border: 1px solid var(--border-color);
        color: var(--text-primary);
        padding: 0.5rem 1rem;
        border-radius: 0.5rem;
        font-size: 0.875rem;
        width: 280px;
        outline: none;
      }
      #searchInput:focus { border-color: var(--accent-blue); box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.2); }
      .btn-reset {
        background: transparent;
        border: 1px solid var(--border-color);
        color: var(--text-secondary);
        padding: 0.5rem 0.85rem;
        border-radius: 0.5rem;
        font-size: 0.8rem;
        cursor: pointer;
        transition: all 0.15s ease;
      }
      .btn-reset:hover { background: #334155; color: #FFFFFF; }
      .table-responsive { overflow-x: auto; }
      .matrix-table, .clash-table { width: 100%; border-collapse: collapse; font-size: 0.875rem; text-align: left; }
      .matrix-table th, .matrix-table td, .clash-table th, .clash-table td {
        padding: 0.75rem 1rem;
        border: 1px solid var(--border-color);
      }
      .matrix-table thead th, .clash-table thead th {
        background: #0F172A;
        color: var(--text-secondary);
        font-weight: 600;
        font-size: 0.8rem;
        text-transform: uppercase;
        letter-spacing: 0.05em;
      }
      .matrix-table .row-header { background: #0F172A; font-weight: 600; color: #E2E8F0; }
      .matrix-table td { text-align: center; }
      .cell-diag { color: var(--text-muted); background: rgba(15, 23, 42, 0.5); }
      .cell-zero { color: var(--text-muted); }
      .cell-clash { cursor: pointer; transition: background 0.15s ease; }
      .cell-clash:hover { background: rgba(244, 63, 94, 0.2); }
      .clash-badge {
        display: inline-block;
        background: #E11D48;
        color: #FFFFFF;
        font-weight: 700;
        font-size: 0.8rem;
        padding: 0.2rem 0.6rem;
        border-radius: 9999px;
        box-shadow: 0 0 8px rgba(225, 29, 72, 0.4);
      }
      .clash-table tbody tr { transition: background 0.15s ease; }
      .clash-table tbody tr:hover { background: var(--bg-card-hover); }
      .sortable { cursor: pointer; user-select: none; }
      .sortable:hover { color: #FFFFFF; }
      .model-pill {
        display: inline-block;
        font-size: 0.75rem;
        font-weight: 600;
        padding: 0.2rem 0.5rem;
        border-radius: 0.35rem;
      }
      .model-pill.a { background: rgba(244, 63, 94, 0.15); color: #FB7185; border: 1px solid rgba(244, 63, 94, 0.3); }
      .model-pill.b { background: rgba(59, 130, 246, 0.15); color: #60A5FA; border: 1px solid rgba(59, 130, 246, 0.3); }
      .col-idx { color: var(--text-muted); font-weight: 600; }
      .guid { font-family: var(--font-mono); font-size: 0.75rem; color: #A5B4FC; }
      .col-center, .col-box { font-family: var(--font-mono); font-size: 0.8rem; color: #CBD5E1; }
      .col-vol { color: #FBBF24; font-family: var(--font-mono); }
      .no-data { text-align: center; padding: 2rem; color: var(--accent-green); font-size: 1.1rem; }
      .footer {
        background: #0F172A;
        border-top: 1px solid var(--border-color);
        padding: 1.5rem;
        text-align: center;
        font-size: 0.8rem;
        color: var(--text-muted);
      }
      .footer a { color: var(--accent-blue); text-decoration: none; }
      .footer a:hover { text-decoration: underline; }
      """;

    private const string ClientJs = """
      let activeFilterModelA = null;
      let activeFilterModelB = null;

      function filterPair(modelA, modelB) {
        activeFilterModelA = modelA;
        activeFilterModelB = modelB;
        applyFilters();
      }

      function resetFilters() {
        activeFilterModelA = null;
        activeFilterModelB = null;
        document.getElementById('searchInput').value = '';
        applyFilters();
      }

      function applyFilters() {
        const query = document.getElementById('searchInput').value.toLowerCase().trim();
        const rows = document.querySelectorAll('#clashTable tbody tr');
        rows.forEach(row => {
          if (row.classList.contains('no-data')) return;
          const mA = row.getAttribute('data-model-a') || '';
          const mB = row.getAttribute('data-model-b') || '';
          const text = row.innerText.toLowerCase();

          let matchPair = true;
          if (activeFilterModelA && activeFilterModelB) {
            matchPair = (mA === activeFilterModelA && mB === activeFilterModelB) ||
                        (mA === activeFilterModelB && mB === activeFilterModelA);
          }

          let matchSearch = true;
          if (query) {
            matchSearch = text.includes(query);
          }

          row.style.display = (matchPair && matchSearch) ? '' : 'none';
        });
      }

      let sortDirection = {};
      function sortTable(colIndex) {
        const table = document.getElementById('clashTable');
        const tbody = table.querySelector('tbody');
        const rows = Array.from(tbody.querySelectorAll('tr'));
        if (rows.length <= 1) return;

        sortDirection[colIndex] = !sortDirection[colIndex];
        const dir = sortDirection[colIndex] ? 1 : -1;

        rows.sort((a, b) => {
          let cellA = a.children[colIndex];
          let cellB = b.children[colIndex];
          if (!cellA || !cellB) return 0;

          let valA = cellA.getAttribute('data-val') || cellA.innerText.trim();
          let valB = cellB.getAttribute('data-val') || cellB.innerText.trim();

          let numA = parseFloat(valA);
          let numB = parseFloat(valB);
          if (!isNaN(numA) && !isNaN(numB)) {
            return (numA - numB) * dir;
          }
          return valA.localeCompare(valB) * dir;
        });

        rows.forEach(r => tbody.appendChild(r));
      }
      """;
}
