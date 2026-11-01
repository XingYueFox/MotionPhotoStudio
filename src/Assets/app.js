/* ============================================================
   动态照片工坊 · Material 3 Expressive 前端
   ============================================================ */
'use strict';

/* ---------------- 色彩：sRGB ↔ OKLab，按色相/彩度/色调生成 M3 角色 ---------------- */

const Color = (() => {
  const clamp01 = v => Math.min(1, Math.max(0, v));
  const srgbToLinear = c => (c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4));
  const linearToSrgb = c => (c <= 0.0031308 ? c * 12.92 : 1.055 * Math.pow(c, 1 / 2.4) - 0.055);

  function hexToRgb(hex) {
    const h = hex.replace('#', '');
    return [parseInt(h.slice(0, 2), 16) / 255, parseInt(h.slice(2, 4), 16) / 255, parseInt(h.slice(4, 6), 16) / 255];
  }

  function linearToOklab(r, g, b) {
    const l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b;
    const m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b;
    const s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b;
    const l_ = Math.cbrt(l), m_ = Math.cbrt(m), s_ = Math.cbrt(s);
    return {
      L: 0.2104542553 * l_ + 0.7936177850 * m_ - 0.0040720468 * s_,
      a: 1.9779984951 * l_ - 2.4285922050 * m_ + 0.4505937099 * s_,
      b: 0.0259040371 * l_ + 0.7827717662 * m_ - 0.8086757660 * s_
    };
  }

  function oklabToLinear(L, a, b) {
    const l_ = L + 0.3963377774 * a + 0.2158037573 * b;
    const m_ = L - 0.1055613458 * a - 0.0638541728 * b;
    const s_ = L - 0.0894841775 * a - 1.2914855480 * b;
    const l = l_ * l_ * l_, m = m_ * m_ * m_, s = s_ * s_ * s_;
    return {
      r: 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
      g: -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
      b: -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s
    };
  }

  const inGamut = rgb => rgb.r >= -0.0005 && rgb.r <= 1.0005 && rgb.g >= -0.0005 && rgb.g <= 1.0005 && rgb.b >= -0.0005 && rgb.b <= 1.0005;

  function toHex(rgb) {
    const f = v => Math.round(clamp01(linearToSrgb(clamp01(v))) * 255).toString(16).padStart(2, '0');
    return '#' + f(rgb.r) + f(rgb.g) + f(rgb.b);
  }

  // 色调 tone(0-100, CIELAB L*) → OKLab 的 L
  function toneToL(tone) {
    const y = tone <= 8 ? tone / 903.3 : Math.pow((tone + 16) / 116, 3);
    return Math.cbrt(Math.max(y, 0));
  }

  function fromHct(hue, chroma, tone) {
    const L = toneToL(tone);
    const rad = (hue * Math.PI) / 180;
    let c = Math.max(0, chroma);
    let rgb = oklabToLinear(L, Math.cos(rad) * c, Math.sin(rad) * c);
    let guard = 0;
    while (!inGamut(rgb) && guard++ < 40) {
      c *= 0.94;
      rgb = oklabToLinear(L, Math.cos(rad) * c, Math.sin(rad) * c);
    }
    return toHex(rgb);
  }

  function analyze(hex) {
    const [r, g, b] = hexToRgb(hex).map(srgbToLinear);
    const lab = linearToOklab(r, g, b);
    const chroma = Math.hypot(lab.a, lab.b);
    let hue = (Math.atan2(lab.b, lab.a) * 180) / Math.PI;
    if (hue < 0) hue += 360;
    return { hue, chroma, lightness: lab.L };
  }

  return { analyze, fromHct };
})();

const SEEDS = [
  { hex: 'system', name: '跟随系统强调色' },
  { hex: '#6750A4', name: '星紫' },
  { hex: '#0B57D0', name: '群青' },
  { hex: '#00696D', name: '深青' },
  { hex: '#3F6B21', name: '苔绿' },
  { hex: '#A33B20', name: '陶土' },
  { hex: '#8C4A74', name: '玫瑰' },
  { hex: '#9A5B00', name: '琥珀' },
  { hex: '#4B5C92', name: '雾蓝' }
];

function buildPalette(seedHex, dark) {
  const { hue, chroma } = Color.analyze(seedHex);
  const c = Math.max(0.055, Math.min(chroma, 0.145));
  const nHue = hue;
  const nC = 0.0075;
  const tHue = (hue + 58) % 360;

  const mk = (h, cc, t) => Color.fromHct(h, cc, t);

  if (!dark) {
    return {
      '--p': mk(hue, c, 40), '--on-p': mk(hue, c, 100),
      '--p-c': mk(hue, c * 0.55, 90), '--on-p-c': mk(hue, c * 0.7, 12),
      '--s': mk(hue, c * 0.38, 40), '--on-s': mk(hue, c * 0.2, 100),
      '--s-c': mk(hue, c * 0.32, 90), '--on-s-c': mk(hue, c * 0.4, 12),
      '--t': mk(tHue, c * 0.85, 40), '--on-t': mk(tHue, c * 0.4, 100),
      '--t-c': mk(tHue, c * 0.6, 90), '--on-t-c': mk(tHue, c * 0.7, 12),
      '--err': '#B3261E', '--err-c': '#F9DEDC', '--on-err-c': '#410E0B',
      '--surf': mk(nHue, nC * 1.6, 98), '--surf-lowest': mk(nHue, nC, 100),
      '--surf-low': mk(nHue, nC * 1.4, 96), '--surf-c': mk(nHue, nC * 1.8, 94),
      '--surf-high': mk(nHue, nC * 2.2, 92), '--surf-highest': mk(nHue, nC * 2.6, 90),
      '--on-surf': mk(nHue, nC * 2, 11), '--on-surf-var': mk(nHue, nC * 3, 30),
      '--outline': mk(nHue, nC * 3, 50), '--outline-var': mk(nHue, nC * 3, 80),
      '--inv-surf': mk(nHue, nC * 3, 20), '--inv-on-surf': mk(nHue, nC * 1.5, 95)
    };
  }
  return {
    '--p': mk(hue, c * 0.9, 80), '--on-p': mk(hue, c * 0.6, 20),
    '--p-c': mk(hue, c * 0.7, 30), '--on-p-c': mk(hue, c * 0.35, 90),
    '--s': mk(hue, c * 0.4, 80), '--on-s': mk(hue, c * 0.3, 20),
    '--s-c': mk(hue, c * 0.4, 30), '--on-s-c': mk(hue, c * 0.25, 90),
    '--t': mk(tHue, c * 0.8, 80), '--on-t': mk(tHue, c * 0.4, 20),
    '--t-c': mk(tHue, c * 0.6, 30), '--on-t-c': mk(tHue, c * 0.4, 90),
    '--err': '#F2B8B5', '--err-c': '#8C1D18', '--on-err-c': '#F9DEDC',
    '--surf': mk(nHue, nC * 1.6, 7), '--surf-lowest': mk(nHue, nC, 4),
    '--surf-low': mk(nHue, nC * 1.6, 10), '--surf-c': mk(nHue, nC * 1.8, 13),
    '--surf-high': mk(nHue, nC * 2.2, 17), '--surf-highest': mk(nHue, nC * 2.6, 22),
    '--on-surf': mk(nHue, nC * 1.5, 92), '--on-surf-var': mk(nHue, nC * 2.2, 80),
    '--outline': mk(nHue, nC * 2.5, 60), '--outline-var': mk(nHue, nC * 2.5, 30),
    '--inv-surf': mk(nHue, nC * 1.5, 90), '--inv-on-surf': mk(nHue, nC * 2, 20)
  };
}

/* ---------------- 状态 ---------------- */

const state = {
  pairs: [],
  summary: null,
  output: '',
  roots: [],
  running: false,
  results: [],
  settings: {
    theme: 'system',
    seed: 'system',
    recursive: true,
    copyAll: false,
    legacy: true,
    preserve: true,
    overwrite: 'rename',
    timestamp: 1500
  },
  app: {},
  update: null
};

const $ = sel => document.querySelector(sel);
const $$ = sel => Array.from(document.querySelectorAll(sel));

const esc = s => String(s == null ? '' : s).replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch]));

function formatSize(bytes) {
  if (!bytes) return '0 B';
  if (bytes < 1024) return bytes + ' B';
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
  if (bytes < 1024 * 1024 * 1024) return (bytes / 1024 / 1024).toFixed(2) + ' MB';
  return (bytes / 1024 / 1024 / 1024).toFixed(2) + ' GB';
}

/* ---------------- 与宿主通信 ---------------- */

const bridge = (() => {
  const pending = new Map();
  let seq = 0;
  const available = !!(window.chrome && window.chrome.webview);

  function send(cmd, args) {
    if (!available) return Promise.reject(new Error('宿主未就绪'));
    return new Promise((resolve, reject) => {
      const id = 'r' + (++seq);
      pending.set(id, { resolve, reject });
      window.chrome.webview.postMessage(JSON.stringify({ id, cmd, args: args || {} }));
      setTimeout(() => {
        if (pending.has(id)) {
          pending.delete(id);
          reject(new Error('操作超时'));
        }
      }, 120000);
    });
  }

  if (available) {
    window.chrome.webview.addEventListener('message', ev => {
      const msg = ev.data;
      if (!msg) return;
      if (msg.event) { handleEvent(msg.event, msg.data); return; }
      const slot = pending.get(msg.id);
      if (!slot) return;
      pending.delete(msg.id);
      if (msg.ok) slot.resolve(msg.data);
      else slot.reject(new Error(msg.error || '操作失败'));
    });
  }

  return { send, available };
})();

/* ---------------- 主题 ---------------- */

function currentDark() {
  if (state.settings.theme === 'dark') return true;
  if (state.settings.theme === 'light') return false;
  return window.matchMedia('(prefers-color-scheme: dark)').matches;
}

function resolvedSeed() {
  if (state.settings.seed === 'system') return state.accent || '#6750A4';
  return state.settings.seed;
}

function applyTheme() {
  const dark = currentDark();
  const palette = buildPalette(resolvedSeed(), dark);
  const root = document.documentElement;
  Object.keys(palette).forEach(k => root.style.setProperty(k, palette[k]));
  document.documentElement.style.colorScheme = dark ? 'dark' : 'light';
  document.body.style.background = palette['--surf'];
  try { bridge.send('ui.theme', { dark, surface: palette['--surf'] }); } catch (e) { }
  $$('.swatch').forEach(el => el.classList.toggle('is-active', el.dataset.seed === state.settings.seed));
}

/* ---------------- 提示条 ---------------- */

let snackTimer = null;
function snack(text, actionLabel, onAction) {
  const bar = $('#snackbar');
  const label = $('#snackbarText');
  const action = $('#snackbarAction');
  label.textContent = text;
  if (actionLabel) {
    action.hidden = false;
    action.textContent = actionLabel;
    action.onclick = () => { hideSnack(); if (onAction) onAction(); };
  } else {
    action.hidden = true;
  }
  bar.classList.add('is-open');
  clearTimeout(snackTimer);
  snackTimer = setTimeout(hideSnack, 4200);
}
function hideSnack() { $('#snackbar').classList.remove('is-open'); }

/* ---------------- 页面切换 ---------------- */

function go(page) {
  $$('.page').forEach(p => p.classList.toggle('is-active', p.dataset.page === page));
  $$('.rail-item').forEach(b => b.classList.toggle('is-active', b.dataset.page === page));
  const scroller = $('#page-' + page + ' .page-scroll');
  if (scroller) {
    // 关闭平滑滚动，避免切换页面时残留上一次的滚动位置
    const prev = scroller.style.scrollBehavior;
    scroller.style.scrollBehavior = 'auto';
    scroller.scrollTop = 0;
    scroller.style.scrollBehavior = prev || '';
    requestAnimationFrame(() => { scroller.scrollTop = 0; });
  }
}

/* ---------------- 素材渲染 ---------------- */

function statusInfo(item) {
  switch (item.status) {
    case 'ready': return { cls: 'ok', text: '可合成' };
    case 'duplicate': return { cls: 'warn', text: '已是动态照片' };
    case 'no-video': return { cls: 'muted', text: '缺少视频' };
    case 'invalid': return { cls: 'bad', text: '视频无法识别' };
    default: return { cls: 'muted', text: '未知' };
  }
}

function renderPairs() {
  const list = $('#pairList');
  const section = $('#pairSection');
  const stats = $('#stats');

  if (!state.pairs.length) {
    section.hidden = true;
    stats.hidden = true;
    updateActionState();
    return;
  }
  section.hidden = false;
  stats.hidden = false;

  list.innerHTML = state.pairs.map((item, index) => {
    const st = statusInfo(item);
    const photoExt = (item.photoName.split('.').pop() || 'jpg').toUpperCase();
    const bits = [];
    if (item.video) {
      bits.push(photoExt + ' ' + formatSize(item.photoSize));
      bits.push(item.videoName.split('.').pop().toUpperCase() + ' ' + formatSize(item.videoSize));
      if (item.codec) bits.push(item.codec);
      if (item.duration) bits.push(item.duration.toFixed(1) + ' s');
      if (item.convert) bits.push('打包前转为 JPEG');
    } else {
      bits.push(photoExt + ' ' + formatSize(item.photoSize));
      bits.push('未找到同名 MOV / MP4');
    }
    return `
      <article class="pair-card ${item.status !== 'ready' ? 'is-dim' : ''}" data-id="${item.id}" style="animation-delay:${Math.min(index * 18, 260)}ms">
        <div class="thumb" data-thumb="${esc(item.photo)}"></div>
        <div class="pair-main">
          <div class="pair-name">${esc(item.photoName)}</div>
          <div class="pair-meta">${esc(bits.join(' · '))}</div>
        </div>
        <span class="chip ${st.cls}">${st.text}</span>
        <button class="icon-btn ghost remove" title="移除" data-remove="${item.id}">
          <svg viewBox="0 0 24 24"><path d="M6 6l12 12M18 6L6 18" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>
        </button>
      </article>`;
  }).join('');

  const ready = state.pairs.filter(p => p.status === 'ready');
  const dup = state.pairs.filter(p => p.status === 'duplicate').length;
  const missing = state.pairs.filter(p => p.status === 'no-video').length;
  $('#statReady').textContent = ready.length;
  $('#statDuplicate').textContent = dup;
  $('#statMissing').textContent = missing;
  const total = ready.reduce((sum, p) => sum + (p.photoSize || 0) + (p.videoSize || 0), 0);
  $('#statSize').textContent = formatSize(total);

  lazyThumbs();
  updateActionState();
}

const thumbCache = new Map();
let thumbQueue = [];
let thumbRunning = false;

function lazyThumbs() {
  $$('.thumb[data-thumb]').forEach(el => {
    const path = el.dataset.thumb;
    if (thumbCache.has(path)) { paintThumb(el, thumbCache.get(path)); return; }
    thumbQueue.push(el);
  });
  pumpThumbs();
}

async function pumpThumbs() {
  if (thumbRunning) return;
  thumbRunning = true;
  while (thumbQueue.length) {
    const el = thumbQueue.shift();
    const path = el.dataset.thumb;
    if (!path) continue;
    if (thumbCache.has(path)) { paintThumb(el, thumbCache.get(path)); continue; }
    try {
      const res = await bridge.send('thumb.get', { path });
      const url = res && res.dataUrl ? res.dataUrl : '';
      thumbCache.set(path, url);
      paintThumb(el, url);
    } catch (e) { thumbCache.set(path, ''); }
  }
  thumbRunning = false;
}

function paintThumb(el, url) {
  if (!el || !url) return;
  el.style.backgroundImage = `url("${url}")`;
  el.innerHTML = '';
}

function paintProgressCards(status) {
  $$('.pair-card').forEach(card => {
    const item = state.pairs.find(p => p.id === card.dataset.id);
    if (!item || item.status !== 'ready') return;
    const chip = card.querySelector('.chip');
    if (status === 'working') { chip.textContent = '合成中'; chip.className = 'chip warn'; }
  });
}

function updateActionState() {
  const ready = state.pairs.filter(p => p.status === 'ready').length;
  const btn = $('#btnStart');
  btn.disabled = state.running || ready === 0 || !state.output;
  $('#actionHint').textContent = state.running
    ? '正在合成…'
    : (!ready ? '选择素材后即可开始' : (!state.output ? '请先选择输出目录' : `准备合成 ${ready} 张照片`));
}

/* ---------------- 扫描 ---------------- */

async function scan(paths, merge) {
  if (!paths || !paths.length) return;
  const res = await bridge.send('scan.paths', { paths, recursive: state.settings.recursive });
  const incoming = res.pairs || [];
  const map = new Map(state.pairs.map(p => [p.photo.toLowerCase(), p]));
  incoming.forEach(p => map.set(p.photo.toLowerCase(), p));
  state.pairs = Array.from(map.values());
  state.summary = res.summary;
  (res.summary.roots || []).forEach(r => { if (!state.roots.includes(r)) state.roots.push(r); });

  if (!state.output && state.summary && state.summary.roots && state.summary.roots.length) {
    state.output = state.summary.roots[0];
    state.settings.outputDir = state.output;
    $('#outputPath').textContent = state.output;
    $('#outputPath').title = state.output;
  }

  renderPairs();
  saveSettings();
  snack(`已载入 ${incoming.length} 个素材，其中 ${incoming.filter(p => p.status === 'ready').length} 组可合成`);
}

/* ---------------- 任务 ---------------- */

async function startJob() {
  if (state.running) return;
  const items = state.pairs.filter(p => p.status === 'ready').map(p => ({ photo: p.photo, video: p.video }));
  if (!items.length) { snack('没有可合成的素材'); return; }

  state.running = true;
  state.results = [];
  $('#resultList').innerHTML = '';
  $('#resultsEmpty').hidden = true;
  $('#resOk').textContent = '0';
  $('#resFail').textContent = '0';
  $('#resSkip').textContent = '0';
  $('#btnStart').classList.add('is-busy');
  $('#btnStart').querySelector('.fab-label').textContent = '合成中';
  $('#btnStart').querySelector('.fab-spinner').hidden = false;
  $('#btnStart').querySelector('.fab-icon').hidden = true;
  $('#progressStrip').hidden = false;
  $('#progressFill').style.width = '0%';
  document.querySelectorAll('.pair-card .chip').forEach(c => { if (c.textContent === '可合成') { c.textContent = '等待'; c.className = 'chip muted'; } });
  $('#actionHint').textContent = '正在合成…';
  $('#btnStart').disabled = true;

  try {
    await bridge.send('job.start', {
      items,
      output: state.output,
      roots: state.roots,
      recursive: state.settings.recursive,
      copyUnmatched: state.settings.copyAll,
      overwrite: state.settings.overwrite,
      legacyTags: state.settings.legacy,
      preserveTime: state.settings.preserve,
      timestampUs: Number(state.settings.timestamp) * 1000
    });
  } catch (err) {
    finishJob({ ok: 0, failed: 0, skipped: 0, canceled: true });
    snack('无法开始：' + err.message);
  }
}

function noteLog(level, text) {
  const panel = $('#logText');
  panel.textContent += (panel.textContent ? '\n' : '') + text;
  const detail = $('#logPanel');
  if (level === 'error') detail.open = true;
}

function appendResult(entry) {
  state.results.push(entry);
  const list = $('#resultList');
  $('#resultsEmpty').hidden = true;
  const row = document.createElement('div');
  row.className = 'result-row' + (entry.skipped ? '' : (entry.ok ? '' : ' bad'));
  row.style.animationDelay = Math.min(state.results.length * 20, 240) + 'ms';
  row.innerHTML = entry.ok
    ? `<span class="dot"></span><span class="name">${esc(entry.name)}</span><span class="meta">${formatSize(entry.size)}${entry.codec ? ' · ' + esc(entry.codec) : ''}</span><button class="text-btn" data-reveal="${esc(entry.output)}">定位</button>`
    : `<span class="dot"></span><span class="name">${esc(entry.name)}</span><span class="meta">${esc(entry.error || (entry.skipped ? '已跳过' : '失败'))}</span>`;
  list.appendChild(row);
}

function finishJob(summary) {
  state.running = false;
  $('#btnStart').classList.remove('is-busy');
  $('#btnStart').querySelector('.fab-label').textContent = '开始合成';
  $('#btnStart').querySelector('.fab-spinner').hidden = true;
  $('#btnStart').querySelector('.fab-icon').hidden = false;
  $('#progressStrip').hidden = true;
  $('#resOk').textContent = summary.ok || 0;
  $('#resFail').textContent = summary.failed || 0;
  $('#resSkip').textContent = (summary.skipped || 0) + (summary.copied ? summary.copied : 0);
  $('#btnOpenOut').disabled = !state.output;
  updateActionState();

  if (summary.canceled) { snack('任务已结束'); return; }
  go('results');
  if ((summary.ok || 0) > 0) {
    snack(`合成完成：${summary.ok} 张`, '打开目录', () => bridge.send('shell.reveal', { path: state.output }));
    state.pairs = state.pairs.filter(p => p.status !== 'ready');
    renderPairs();
  } else {
    snack('没有合成成功，请查看运行日志');
  }
}

/* ---------------- 宿主事件 ---------------- */

function handleEvent(name, data) {
  switch (name) {
    case 'app.info':
      state.app = data || {};
      $('#versionText').textContent = (data && data.version) || '27.0.0';
      if (!state.update) {
        const updateStatus = $('#updateStatus');
        if (updateStatus) updateStatus.innerHTML = `当前版本 <span class="mono">${esc((data && data.version) || '27.0.0')}</span>`;
      }
      $('#webviewText').textContent = (data && data.webview) ? 'WebView2 ' + data.webview : 'WebView2';
      $('#exeDirText').textContent = (data && data.exeDir) || '—';
      $('#exeDirText').title = (data && data.exeDir) || '';
      $('#dataDirText').textContent = (data && data.dataDir) || 'data';
      if (data && data.accent) {
        state.accent = data.accent;
        if (state.settings.seed === 'system') { renderSwatches(); }
      }
      if (data && (data.forceTheme === 'light' || data.forceTheme === 'dark')) {
        state.settings.theme = data.forceTheme;
        applyTheme();
      }
      if (data && data.page) go(data.page);
      if (data && data.demoDir) {
        scan([data.demoDir]).then(async () => {
          state.output = data.demoDir + '\\out';
          state.settings.outputDir = state.output;
          $('#outputPath').textContent = state.output;
          updateActionState();
          await startJob();
        }).catch(() => { });
      }
      break;

    case 'theme.accent':
      if (data && data.hex) {
        state.accent = data.hex;
        if (state.settings.seed === 'system') renderSwatches();
      }
      break;

    case 'update.result':
      applyUpdateResult(data);
      break;

    case 'files.dropped':
      if (data && data.paths) {
        snack('正在读取拖入的素材…');
        scan(data.paths);
      }
      break;

    case 'job.begin':
      $('#progressFill').style.width = '2%';
      break;

    case 'job.progress': {
      const pct = data.total ? Math.round((data.index / data.total) * 100) : 0;
      $('#progressFill').style.width = Math.max(2, pct) + '%';
      $('#actionHint').textContent = `(${data.index + 1}/${data.total}) ${data.name || ''}`;
      break;
    }

    case 'job.item':
      appendResult(data);
      break;

    case 'job.log':
      noteLog(data.level, data.text);
      break;

    case 'job.done':
      $('#progressFill').style.width = '100%';
      finishJob(data || {});
      break;
  }
}

/* ---------------- 设置 ---------------- */

let saveTimer = null;
function saveSettings() {
  clearTimeout(saveTimer);
  saveTimer = setTimeout(() => {
    bridge.send('settings.save', { raw: JSON.stringify(state.settings) }).catch(() => { });
  }, 260);
}

function applySettingsToUI() {
  const s = state.settings;
  $('#optRecursive').checked = !!s.recursive;
  $('#optCopyAll').checked = !!s.copyAll;
  $('#optLegacy').checked = !!s.legacy;
  $('#optPreserve').checked = !!s.preserve;
  $('#optTimestamp').value = s.timestamp;
  $('#timestampValue').textContent = (s.timestamp / 1000).toFixed(1) + 's';
  $$('#segTheme button').forEach(b => b.classList.toggle('is-active', b.dataset.value === s.theme));
  $$('#segOverwrite button').forEach(b => b.classList.toggle('is-active', b.dataset.value === s.overwrite));
  applyTheme();
}

function renderSwatches() {
  const wrap = $('#swatches');
  wrap.innerHTML = SEEDS.map(s => {
    const color = s.hex === 'system' ? (state.accent || '#6750A4') : s.hex;
    const cls = s.hex === 'system' ? 'swatch swatch-system' : 'swatch';
    return `<button class="${cls}" data-seed="${s.hex}" title="${s.name}" style="background:${color}">${s.hex === 'system' ? '<svg viewBox="0 0 24 24"><path d="M12 3.4a8.6 8.6 0 1 0 8.6 8.6c0-1-.9-1.7-1.9-1.7h-1.5a2 2 0 0 1 0-4h1.2A1.7 1.7 0 0 0 20 4.6 8.6 8.6 0 0 0 12 3.4z" fill="none" stroke="currentColor" stroke-width="1.7"/></svg>' : ''}</button>`;
  }).join('');
  $$('.swatch').forEach(btn => {
    btn.addEventListener('click', () => {
      state.settings.seed = btn.dataset.seed;
      applyTheme();
      saveSettings();
    });
  });
  applyTheme();
}

/* ---------------- 初始化 ---------------- */

function bindRipples() {
  document.addEventListener('pointerdown', e => {
    const target = e.target.closest('.btn, .fab, .icon-btn, .rail-item, .text-btn, .segmented button, .swatch');
    if (!target || target.hasAttribute('disabled')) return;
    const rect = target.getBoundingClientRect();
    const size = Math.max(rect.width, rect.height) * 1.2;
    target.style.setProperty('--ripple-x', (e.clientX - rect.left) + 'px');
    target.style.setProperty('--ripple-y', (e.clientY - rect.top) + 'px');
    target.style.setProperty('--ripple-size', size + 'px');
    target.classList.remove('is-ripple');
    void target.offsetWidth;
    target.classList.add('is-ripple');
    setTimeout(() => target.classList.remove('is-ripple'), 600);
  });
}

/* 窗口边缘缩放：WebView2 子窗口会吞掉 WM_NCHITTEST，宿主收不到边缘命中测试。
   由页面判断指针是否贴近边缘，交给宿主走系统原生缩放循环（和拖标题栏同一机制）。 */
const EDGE_PX = 6;
const EDGE_CURSORS = {
  left: 'ew-resize', right: 'ew-resize', top: 'ns-resize', bottom: 'ns-resize',
  topleft: 'nwse-resize', bottomright: 'nwse-resize',
  topright: 'nesw-resize', bottomleft: 'nesw-resize'
};

function edgeAt(x, y) {
  const w = window.innerWidth, h = window.innerHeight;
  const l = x <= EDGE_PX, r = x >= w - EDGE_PX - 1;
  const t = y <= EDGE_PX, b = y >= h - EDGE_PX - 1;
  if (t && l) return 'topleft';
  if (t && r) return 'topright';
  if (b && l) return 'bottomleft';
  if (b && r) return 'bottomright';
  if (l) return 'left';
  if (r) return 'right';
  if (t) return 'top';
  if (b) return 'bottom';
  return null;
}

function bindWindowResize() {
  document.addEventListener('pointermove', e => {
    const edge = edgeAt(e.clientX, e.clientY);
    document.body.classList.toggle('is-edge', !!edge);
    if (edge) document.body.style.setProperty('--edge-cursor', EDGE_CURSORS[edge]);
  }, true);

  document.addEventListener('pointerdown', e => {
    if (e.button !== 0) return;
    const edge = edgeAt(e.clientX, e.clientY);
    if (!edge) return;
    e.preventDefault();
    e.stopPropagation();
    bridge.send('window.resize', { edge });
  }, true);
}

function bindWindowChrome() {
  $$('[data-win]').forEach(btn => {
    btn.addEventListener('click', () => {
      const action = btn.dataset.win;
      if (action === 'min') bridge.send('window.min');
      else if (action === 'max') bridge.send('window.max');
      else bridge.send('window.close');
    });
  });
  const dragZones = [$('#titlebar .titlebar-spacer'), $('#titlebar .brand')];
  dragZones.forEach(el => {
    if (!el) return;
    el.addEventListener('mousedown', e => {
      if (e.button !== 0) return;
      if (e.target.closest('button')) return;
      if (edgeAt(e.clientX, e.clientY)) return;   // 边缘留给缩放
      bridge.send('window.drag');
    });
    el.addEventListener('dblclick', () => bridge.send('window.max'));
  });
  bindWindowResize();
}

function bindDropzone() {
  const dz = $('#dropzone');
  dz.addEventListener('click', () => pickFolder());
  dz.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') pickFolder(); });
  ['dragenter', 'dragover'].forEach(type => {
    dz.addEventListener(type, e => { e.preventDefault(); dz.classList.add('is-over'); });
  });
  ['dragleave', 'drop'].forEach(type => {
    dz.addEventListener(type, e => { e.preventDefault(); dz.classList.remove('is-over'); });
  });
  document.addEventListener('dragover', e => e.preventDefault());
  document.addEventListener('drop', e => {
    e.preventDefault();
    if (e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files.length) {
      snack('请把文件释放到窗口内，或使用「添加文件」按钮');
    }
  });
}

async function pickFolder() {
  if (state.running) return;
  try {
    const res = await bridge.send('dialog.pickFolder', { title: '选择包含照片与视频的文件夹' });
    if (res && res.path) await scan([res.path]);
  } catch (e) { snack(e.message); }
}

async function pickFiles() {
  if (state.running) return;
  try {
    const res = await bridge.send('dialog.pickFiles', {});
    if (res && res.paths && res.paths.length) await scan(res.paths);
  } catch (e) { snack(e.message); }
}

async function pickOutput() {
  if (state.running) return;
  try {
    const res = await bridge.send('dialog.pickFolder', { title: '选择输出目录' });
    if (res && res.path) {
      state.output = res.path;
      state.settings.outputDir = res.path;
      $('#outputPath').textContent = state.output;
      $('#outputPath').title = state.output;
      updateActionState();
      saveSettings();
    }
  } catch (e) { snack(e.message); }
}

async function loadSettings() {
  try {
    const res = await bridge.send('settings.load', {});
    if (res && res.raw) {
      const parsed = JSON.parse(res.raw);
      Object.assign(state.settings, parsed || {});
      if (!state.settings.seed) state.settings.seed = 'system';
    }
  } catch (e) { }
  if (state.settings.outputDir) {
    state.output = state.settings.outputDir;
    $('#outputPath').textContent = state.output;
    $('#outputPath').title = state.output;
  }
}

function wireSettings() {
  $$('#segTheme button').forEach(b => b.addEventListener('click', () => {
    state.settings.theme = b.dataset.value;
    applySettingsToUI();
    saveSettings();
  }));
  $$('#segOverwrite button').forEach(b => b.addEventListener('click', () => {
    state.settings.overwrite = b.dataset.value;
    applySettingsToUI();
    saveSettings();
  }));
  $('#optRecursive').addEventListener('change', e => { state.settings.recursive = e.target.checked; saveSettings(); });
  $('#optCopyAll').addEventListener('change', e => { state.settings.copyAll = e.target.checked; saveSettings(); });
  $('#optLegacy').addEventListener('change', e => { state.settings.legacy = e.target.checked; saveSettings(); });
  $('#optPreserve').addEventListener('change', e => { state.settings.preserve = e.target.checked; saveSettings(); });
  $('#optTimestamp').addEventListener('input', e => {
    state.settings.timestamp = Number(e.target.value);
    $('#timestampValue').textContent = (state.settings.timestamp / 1000).toFixed(1) + 's';
    saveSettings();
  });
  $('#btnOpenData').addEventListener('click', () => bridge.send('shell.openDataDir').catch(() => { }));
}

function wireAbout() {
  const btn = $('#btnCheckUpdate');
  if (btn) {
    btn.addEventListener('click', async () => {
      // 已经查到新版本时，按钮直接变成下载入口
      if (state.update && state.update.hasUpdate) {
        bridge.send('shell.open', { path: state.update.url || state.update.page }).catch(() => { });
        return;
      }
      btn.disabled = true;
      btn.textContent = '检查中…';
      const status = $('#updateStatus');
      if (status) status.textContent = '正在连接 GitHub…';
      try {
        await bridge.send('update.check', {});
      } catch (e) {
        btn.disabled = false;
        btn.textContent = '检查更新';
        if (status) status.textContent = '检查失败：' + (e.message || '未知错误');
      }
    });
  }

  // 外链一律交给系统浏览器，避免把整个应用导航走
  document.addEventListener('click', e => {
    const link = e.target.closest('a[data-external]');
    if (!link) return;
    e.preventDefault();
    bridge.send('shell.open', { path: link.getAttribute('href') }).catch(() => { });
  });
}

function applyUpdateResult(data) {
  const btn = $('#btnCheckUpdate');
  const status = $('#updateStatus');
  if (!btn || !status) return;
  btn.disabled = false;
  const current = (data && data.current) || (state.app && state.app.version) || '27.0.0';
  const currentHtml = `<span class="mono">${esc(current)}</span>`;

  if (!data || !data.ok) {
    state.update = null;
    btn.textContent = '重新检查';
    status.textContent = '检查失败：' + ((data && data.error) || '未知错误');
    return;
  }

  if (data.hasUpdate) {
    state.update = data;
    btn.textContent = '前往下载';
    status.innerHTML = `发现新版本 <span class="mono">${esc(data.latest)}</span>（当前 ${currentHtml}）`;
    snack(`发现新版本 ${data.latest}`, '前往下载', () => bridge.send('shell.open', { path: data.url || data.page }).catch(() => { }));
  } else {
    state.update = null;
    btn.textContent = '重新检查';
    status.innerHTML = `已是最新版本（${currentHtml}）`;
    snack('当前已是最新版本');
  }
}

function wireMain() {
  $$('.rail-item').forEach(btn => btn.addEventListener('click', () => go(btn.dataset.page)));
  $('#btnPickFolder').addEventListener('click', e => { e.stopPropagation(); pickFolder(); });
  $('#btnPickFiles').addEventListener('click', e => { e.stopPropagation(); pickFiles(); });
  $('#btnOutput').addEventListener('click', pickOutput);
  $('#btnStart').addEventListener('click', startJob);
  $('#btnClear').addEventListener('click', () => {
    state.pairs = []; state.roots = []; state.summary = null;
    $('#logText').textContent = '';
    renderPairs();
  });
  $('#btnSelectReady').addEventListener('click', () => {
    const removed = state.pairs.filter(p => p.status !== 'ready').length;
    state.pairs = state.pairs.filter(p => p.status === 'ready');
    renderPairs();
    snack(removed ? `已移除 ${removed} 个不可用素材` : '全部素材都可合成');
  });
  $('#pairList').addEventListener('click', e => {
    const removeBtn = e.target.closest('[data-remove]');
    if (!removeBtn) return;
    state.pairs = state.pairs.filter(p => p.id !== removeBtn.dataset.remove);
    renderPairs();
  });
  $('#btnOpenOut').addEventListener('click', () => {
    if (state.output) bridge.send('shell.reveal', { path: state.output });
  });
  $('#btnBackCompose').addEventListener('click', () => go('compose'));
  $('#resultList').addEventListener('click', e => {
    const btn = e.target.closest('[data-reveal]');
    if (btn) bridge.send('shell.reveal', { path: btn.dataset.reveal, select: true });
  });
  $('#snackbar').addEventListener('click', e => { if (e.target.id !== 'snackbarAction') hideSnack(); });

  window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
    if (state.settings.theme === 'system') applyTheme();
  });
}

async function boot() {
  window.addEventListener('error', ev => {
    try { bridge.send('debug.log', { text: ev.message + ' @' + ev.filename + ':' + ev.lineno + ':' + ev.colno }); } catch (e) { }
  });
  bindWindowChrome();
  bindDropzone();
  bindRipples();
  wireMain();
  wireSettings();
  wireAbout();
  await loadSettings();
  try {
    const accent = await bridge.send('system.accent', {});
    if (accent && accent.hex) state.accent = accent.hex;
  } catch (e) { }
  renderSwatches();
  applySettingsToUI();
  renderPairs();
  if (bridge.available) {
    try { await bridge.send('app.ready'); } catch (e) { }
  }
}

document.addEventListener('DOMContentLoaded', boot);
