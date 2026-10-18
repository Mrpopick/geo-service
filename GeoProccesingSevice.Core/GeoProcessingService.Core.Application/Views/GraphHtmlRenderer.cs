using System.Text;

namespace GeoProcessingService.Core.Application.Views;

internal static class GraphHtmlRenderer
{
    public static string Render(Guid jobId, string graphJson)
    {
        var encodedGraph = Convert.ToBase64String(Encoding.UTF8.GetBytes(graphJson));
        return Template
            .Replace("__JOB_ID__", jobId.ToString(), StringComparison.Ordinal)
            .Replace("__GRAPH_DATA__", encodedGraph, StringComparison.Ordinal);
    }

    private const string Template = """
<!doctype html>
<html lang="ru">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <title>Дорожный граф __JOB_ID__</title>
  <style>
    :root { color-scheme: dark; font-family: Inter, Segoe UI, sans-serif; }
    * { box-sizing: border-box; }
    body { margin: 0; overflow: hidden; background: #07111f; color: #e8eef8; }
    #map { position: fixed; inset: 0 330px 0 0; width: calc(100vw - 330px); height: 100vh; cursor: grab; }
    #map.dragging { cursor: grabbing; }
    aside { position: fixed; right: 0; top: 0; width: 330px; height: 100vh; padding: 20px; overflow: auto;
      background: #0d1b2d; border-left: 1px solid #233653; box-shadow: -12px 0 32px #0005; }
    h1 { margin: 0 0 6px; font-size: 19px; }
    .muted { color: #94a6bf; font-size: 12px; word-break: break-all; }
    .stats { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; margin: 18px 0; }
    .stat { padding: 10px; border-radius: 10px; background: #14243a; }
    .stat b { display: block; font-size: 20px; color: #fff; }
    button, .button { border: 1px solid #38506f; background: #172b45; color: white; border-radius: 8px;
      padding: 7px 11px; cursor: pointer; text-decoration: none; font: inherit; }
    button:hover, .button:hover { background: #213b5e; }
    .controls { display: flex; gap: 7px; margin-bottom: 18px; }
    h2 { margin: 18px 0 9px; font-size: 14px; color: #bed0e9; }
    #legend label { display: flex; align-items: center; gap: 8px; padding: 4px 0; font-size: 13px; }
    .swatch { width: 12px; height: 4px; border-radius: 4px; display: inline-block; }
    #details { min-height: 90px; padding: 12px; border-radius: 10px; background: #14243a; font-size: 13px; line-height: 1.65; }
    #details strong { color: #fff; }
    .hint { position: fixed; left: 14px; bottom: 14px; padding: 8px 11px; border-radius: 8px;
      color: #b9c9dc; background: #07111fcc; border: 1px solid #29405f; font-size: 12px; pointer-events: none; }
    @media (max-width: 800px) { #map { inset: 0 0 260px 0; width: 100vw; height: calc(100vh - 260px); }
      aside { top: auto; bottom: 0; width: 100%; height: 260px; } }
  </style>
</head>
<body>
  <canvas id="map"></canvas>
  <div class="hint">Колесо — масштаб · перетаскивание — перемещение · клик — свойства дороги</div>
  <aside>
    <h1>Дорожный граф</h1>
    <div class="muted">Задание __JOB_ID__</div>
    <div class="stats">
      <div class="stat"><b id="nodeCount">—</b><span>вершин</span></div>
      <div class="stat"><b id="edgeCount">—</b><span>рёбер</span></div>
    </div>
    <div class="controls">
      <button id="zoomIn">＋</button><button id="zoomOut">－</button><button id="reset">По размеру</button>
    </div>
    <div class="controls">
      <a class="button" href="download">Скачать JSON</a>
    </div>
    <h2>Выбранная дорога</h2>
    <div id="details">Кликните по ребру графа.</div>
    <h2>Типы дорог</h2>
    <div id="legend"></div>
  </aside>
  <script>
    const encoded = '__GRAPH_DATA__';
    const bytes = Uint8Array.from(atob(encoded), c => c.charCodeAt(0));
    const graph = JSON.parse(new TextDecoder().decode(bytes));
    const canvas = document.getElementById('map');
    const ctx = canvas.getContext('2d');
    const nodes = new Map(graph.nodes.map(n => [n.id, n]));
    const edges = graph.edges;
    const xs = graph.nodes.map(n => n.x), ys = graph.nodes.map(n => n.y);
    const bounds = { minX: Math.min(...xs), maxX: Math.max(...xs), minY: Math.min(...ys), maxY: Math.max(...ys) };
    const center = { x: (bounds.minX + bounds.maxX) / 2, y: (bounds.minY + bounds.maxY) / 2 };
    const enabledTypes = new Set(edges.map(e => e.roadType || 'без типа'));
    let zoom = 1, panX = 0, panY = 0, baseScale = 1, dragging = false, lastX = 0, lastY = 0;

    document.getElementById('nodeCount').textContent = graph.nodeCount.toLocaleString('ru-RU');
    document.getElementById('edgeCount').textContent = graph.edgeCount.toLocaleString('ru-RU');

    function colorFor(type) {
      let hash = 0; for (const ch of type) hash = ((hash << 5) - hash + ch.charCodeAt(0)) | 0;
      return `hsl(${Math.abs(hash) % 360} 72% 62%)`;
    }
    function resize() {
      const ratio = window.devicePixelRatio || 1;
      const rect = canvas.getBoundingClientRect();
      canvas.width = Math.round(rect.width * ratio); canvas.height = Math.round(rect.height * ratio);
      ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
      const dx = Math.max(bounds.maxX - bounds.minX, 1e-12), dy = Math.max(bounds.maxY - bounds.minY, 1e-12);
      baseScale = Math.min((rect.width - 40) / dx, (rect.height - 40) / dy);
      draw();
    }
    function screen(node) {
      const rect = canvas.getBoundingClientRect();
      return { x: rect.width / 2 + (node.x - center.x) * baseScale * zoom + panX,
               y: rect.height / 2 - (node.y - center.y) * baseScale * zoom + panY };
    }
    function draw() {
      const rect = canvas.getBoundingClientRect();
      ctx.clearRect(0, 0, rect.width, rect.height);
      ctx.lineCap = 'round'; ctx.globalAlpha = 0.88;
      for (const edge of edges) {
        const type = edge.roadType || 'без типа'; if (!enabledTypes.has(type)) continue;
        const a = screen(nodes.get(edge.source)), b = screen(nodes.get(edge.target));
        ctx.beginPath(); ctx.moveTo(a.x, a.y); ctx.lineTo(b.x, b.y);
        ctx.strokeStyle = colorFor(type); ctx.lineWidth = edge.maxSpeed ? 2.4 : 1.35; ctx.stroke();
      }
      ctx.globalAlpha = 1;
    }
    function reset() { zoom = 1; panX = 0; panY = 0; draw(); }
    function applyZoom(factor, x = canvas.clientWidth / 2, y = canvas.clientHeight / 2) {
      const rect = canvas.getBoundingClientRect();
      factor = Math.max(0.1, Math.min(10, factor));
      panX = x - rect.width / 2 - (x - rect.width / 2 - panX) * factor;
      panY = y - rect.height / 2 - (y - rect.height / 2 - panY) * factor;
      zoom = Math.max(0.15, Math.min(80, zoom * factor)); draw();
    }
    function distanceToSegment(p, a, b) {
      const dx = b.x-a.x, dy = b.y-a.y, l2 = dx*dx+dy*dy;
      if (!l2) return Math.hypot(p.x-a.x, p.y-a.y);
      const t = Math.max(0, Math.min(1, ((p.x-a.x)*dx+(p.y-a.y)*dy)/l2));
      return Math.hypot(p.x-(a.x+t*dx), p.y-(a.y+t*dy));
    }
    function selectEdge(x, y) {
      let best = null, bestDistance = 10;
      for (const edge of edges) {
        const type = edge.roadType || 'без типа'; if (!enabledTypes.has(type)) continue;
        const distance = distanceToSegment({x,y}, screen(nodes.get(edge.source)), screen(nodes.get(edge.target)));
        if (distance < bestDistance) { bestDistance = distance; best = edge; }
      }
      document.getElementById('details').innerHTML = best
        ? `<strong>Ребро #${best.id}</strong><br>Тип: ${escapeHtml(best.roadType || 'не указан')}<br>` +
          `Макс. скорость: ${escapeHtml(best.maxSpeed || 'не указана')}<br>` +
          `Длина: ${Number(best.length).toPrecision(7)} ед. координат<br>Вершины: ${best.source} → ${best.target}`
        : 'Рядом с точкой клика ребро не найдено.';
    }
    function escapeHtml(value) { const el = document.createElement('span'); el.textContent = value; return el.innerHTML; }

    const typeCounts = edges.reduce((m,e) => m.set(e.roadType||'без типа',(m.get(e.roadType||'без типа')||0)+1), new Map());
    [...typeCounts.entries()].sort((a,b)=>b[1]-a[1]).forEach(([type,count]) => {
      const label = document.createElement('label');
      label.innerHTML = `<input type="checkbox" checked><span class="swatch" style="background:${colorFor(type)}"></span><span>${escapeHtml(type)} (${count})</span>`;
      label.querySelector('input').addEventListener('change', e => { e.target.checked ? enabledTypes.add(type) : enabledTypes.delete(type); draw(); });
      document.getElementById('legend').appendChild(label);
    });
    canvas.addEventListener('wheel', e => { e.preventDefault(); const r=canvas.getBoundingClientRect(); applyZoom(Math.exp(-e.deltaY*.001),e.clientX-r.left,e.clientY-r.top); }, {passive:false});
    canvas.addEventListener('pointerdown', e => { dragging=true; lastX=e.clientX; lastY=e.clientY; canvas.classList.add('dragging'); canvas.setPointerCapture(e.pointerId); });
    canvas.addEventListener('pointermove', e => { if(!dragging)return; panX+=e.clientX-lastX; panY+=e.clientY-lastY; lastX=e.clientX; lastY=e.clientY; draw(); });
    canvas.addEventListener('pointerup', e => { dragging=false; canvas.classList.remove('dragging'); canvas.releasePointerCapture(e.pointerId); });
    canvas.addEventListener('click', e => { if(Math.abs(e.clientX-lastX)<4 && Math.abs(e.clientY-lastY)<4){const r=canvas.getBoundingClientRect();selectEdge(e.clientX-r.left,e.clientY-r.top);} });
    document.getElementById('zoomIn').onclick=()=>applyZoom(1.4); document.getElementById('zoomOut').onclick=()=>applyZoom(1/1.4); document.getElementById('reset').onclick=reset;
    window.addEventListener('resize', resize); resize();
  </script>
</body>
</html>
""";
}
