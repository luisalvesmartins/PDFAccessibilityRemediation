'use strict';

// ── Utils ──────────────────────────────────────────────────────────────
const $ = id => document.getElementById(id);
const el = (tag, cls, html) => { const e = document.createElement(tag); if (cls) e.className = cls; if (html !== undefined) e.innerHTML = html; return e; };
const fmt = n => (n != null ? n.toFixed(1) + '%' : '—');
const fmtBytes = b => b < 1048576 ? (b / 1024).toFixed(0) + ' KB' : (b / 1048576).toFixed(1) + ' MB';
const sc = v => v >= 80 ? 'c-good' : v >= 50 ? 'c-ok' : 'c-bad';
const sf = v => v >= 80 ? 'f-good' : v >= 50 ? 'f-ok' : 'f-bad';
const esc = s => String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');

// ── State ──────────────────────────────────────────────────────────────
let queue = []; // { file, id, state }

// ── DOM ────────────────────────────────────────────────────────────────
const dropZone    = $('dropZone');
const fileInput   = $('fileInput');
const browseBtn   = $('browseBtn');
const fileQueue   = $('fileQueue');
const fileCount   = $('fileCount');
const actionArea  = $('actionArea');
const actionSummary = $('actionSummary');
const clearBtn    = $('clearBtn');
const processBtn  = $('processBtn');
const progressArea = $('progressArea');
const progressMsg  = $('progressMsg');
const progressPct  = $('progressPct');
const progressFill = $('progressFill');
const logLines     = $('logLines');
const resultsEmpty   = $('resultsEmpty');
const resultsContent = $('resultsContent');
const resultsMeta    = $('resultsMeta');
const cardGrid       = $('cardGrid');
const zipBtn         = $('zipBtn');
const headerStatus   = $('headerStatus');
const toggleAll      = $('toggleAll');

// ── Feature checkboxes ─────────────────────────────────────────────────
const featureKeys = ['adicionarTags','altTextImagens','corrigirTitulo','corrigirIdioma','corrigirViewerPreferences','corrigirMetadadasXMP'];

function getFeatureState() {
  return Object.fromEntries(featureKeys.map(k => [k, $(`ft_${k}`).checked]));
}

// Toggle-all button
toggleAll.addEventListener('click', () => {
  const anyUnchecked = featureKeys.some(k => !$(`ft_${k}`).checked);
  featureKeys.forEach(k => { $(`ft_${k}`).checked = anyUnchecked; });
  updateActionSummary();
});

featureKeys.forEach(k => {
  $(`ft_${k}`).addEventListener('change', updateActionSummary);
});

function updateActionSummary() {
  const fs = getFeatureState();
  const checked = Object.values(fs).filter(Boolean).length;
  const allChecked = checked === featureKeys.length;
  toggleAll.textContent = allChecked ? 'Nenhuma' : 'Todas';

  const pending = queue.filter(q => q.state === 'pending').length;
  actionSummary.textContent = `${pending} ficheiro${pending !== 1 ? 's' : ''} · ${checked} correç${checked !== 1 ? 'ões' : 'ão'}`;
}

// ── Drag & Drop ────────────────────────────────────────────────────────
dropZone.addEventListener('dragover', e => { e.preventDefault(); dropZone.classList.add('over'); });
dropZone.addEventListener('dragleave', () => dropZone.classList.remove('over'));
dropZone.addEventListener('drop', e => { e.preventDefault(); dropZone.classList.remove('over'); addFiles([...e.dataTransfer.files]); });
dropZone.addEventListener('click', e => { if (e.target !== browseBtn) fileInput.click(); });
dropZone.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') fileInput.click(); });
browseBtn.addEventListener('click', e => { e.stopPropagation(); fileInput.click(); });
fileInput.addEventListener('change', () => { addFiles([...fileInput.files]); fileInput.value = ''; });

// ── File Management ────────────────────────────────────────────────────
function addFiles(files) {
  files.filter(f => f.name.toLowerCase().endsWith('.pdf') || f.type === 'application/pdf')
    .forEach(f => {
      if (!queue.find(q => q.file.name === f.name && q.file.size === f.size))
        queue.push({ file: f, id: Math.random().toString(36).slice(2), state: 'pending' });
    });
  renderQueue();
}

function renderQueue() {
  fileQueue.innerHTML = '';
  queue.forEach(item => {
    const row = el('div', 'file-row');
    row.dataset.id = item.id;
    row.innerHTML = `
      <div class="fr-icon">PDF</div>
      <div class="fr-body">
        <div class="fr-name" title="${esc(item.file.name)}">${esc(item.file.name)}</div>
        <div class="fr-meta">${fmtBytes(item.file.size)}</div>
        <div class="fr-state ${item.state}" id="st_${item.id}">${stateLabel(item.state)}</div>
      </div>
      ${item.state === 'pending'
        ? `<button class="fr-del" data-id="${item.id}" aria-label="Remover ${esc(item.file.name)}">
             <svg width="12" height="12" viewBox="0 0 12 12" fill="none" stroke="currentColor" stroke-width="2"><path d="M1 1l10 10M11 1L1 11"/></svg>
           </button>` : ''}
    `;
    fileQueue.appendChild(row);
  });

  fileQueue.querySelectorAll('.fr-del').forEach(b => b.addEventListener('click', () => {
    queue = queue.filter(q => q.id !== b.dataset.id);
    renderQueue();
  }));

  const pendingCount = queue.filter(q => q.state === 'pending').length;
  fileCount.textContent = queue.length;
  actionArea.classList.toggle('hidden', queue.length === 0);
  processBtn.disabled = pendingCount === 0;
  updateActionSummary();
}

function stateLabel(s) {
  return { pending: 'aguardando', running: '⟳ a processar…', done: '✓ concluído', error: '✗ erro' }[s] || s;
}

clearBtn.addEventListener('click', () => { queue = []; renderQueue(); progressArea.classList.add('hidden'); });

// ── Process ────────────────────────────────────────────────────────────
processBtn.addEventListener('click', async () => {
  const pending = queue.filter(q => q.state === 'pending');
  if (!pending.length) return;

  processBtn.disabled = true;
  clearBtn.disabled   = true;
  progressArea.classList.remove('hidden');
  logLines.innerHTML  = '';
  setStatus('busy', 'A processar…');

  const steps = [
    'Enviar ficheiros…',
    'Analisar conformidade (EN 301 549 · WCAG 2.1/2.2)…',
    'Construir árvore de tags semânticas…',
    'Aplicar texto alternativo às imagens…',
    'Corrigir metadados e idioma…',
    'Gerar relatórios de conformidade…',
    'Finalizar e empacotar…',
  ];

  steps.forEach((s, i) => { const l = el('div', 'log-line', s); l.id = `lg_${i}`; logLines.appendChild(l); });

  pending.forEach(item => { item.state = 'running'; updateRowState(item.id, 'running'); });

  let si = 0;
  const tick = setInterval(() => {
    const prev = $(`lg_${si - 1}`); if (prev) prev.className = 'log-line done';
    const cur  = $(`lg_${si}`);     if (cur)  cur.className  = 'log-line active';
    setProgress(Math.min(90, Math.round(((si + 1) / steps.length) * 88)), steps[si] || '…');
    si++;
    if (si >= steps.length) clearInterval(tick);
  }, 850);

  try {
    const fs = getFeatureState();
    const fd = new FormData();
    pending.forEach(item => fd.append('files', item.file));

    // Append feature flags — ASP.NET Core [FromForm] will bind bool fields
    Object.entries(fs).forEach(([k, v]) => fd.append(k, v));

    const res = await fetch('/api/pdf/remediar', { method: 'POST', body: fd });
    clearInterval(tick);

    steps.forEach((_, i) => { const l = $(`lg_${i}`); if (l) l.className = 'log-line done'; });
    setProgress(100, 'Concluído!');

    if (!res.ok) {
      const err = await res.json().catch(() => ({}));
      throw new Error(err.erro || `Erro ${res.status}`);
    }

    const data = await res.json();

    // Update queue states
    data.ficheiros?.forEach((r, idx) => {
      const item = pending[idx];
      if (item) { item.state = r.sucesso ? 'done' : 'error'; updateRowState(item.id, item.state); }
    });

    // Show results
    renderResults(data, fs);
    setStatus('ok', 'Pronto');

  } catch (err) {
    clearInterval(tick);
    pending.forEach(item => { item.state = 'error'; updateRowState(item.id, 'error'); });
    const el_ = el('div', 'log-line c-bad', `Erro: ${esc(err.message)}`);
    logLines.appendChild(el_);
    setStatus('ok', 'Erro');
    console.error(err);
  }

  processBtn.disabled = false;
  clearBtn.disabled   = false;
  // Remove completed items from queue
  queue = queue.filter(q => q.state !== 'done');
  renderQueue();
});

function updateRowState(id, state) {
  const el_ = $(`st_${id}`);
  if (el_) { el_.className = `fr-state ${state}`; el_.textContent = stateLabel(state); }
}

// ── Results ────────────────────────────────────────────────────────────
function renderResults(data, fs) {
  resultsEmpty.classList.add('hidden');
  resultsContent.classList.remove('hidden');

  const ok = data.sucesso, total = data.total;
  resultsMeta.textContent = `${ok} de ${total} ficheiro${total !== 1 ? 's' : ''} remediado${total !== 1 ? 's' : ''}`;

  if (data.urlZip) { zipBtn.href = data.urlZip; zipBtn.classList.remove('hidden'); }

  data.ficheiros?.forEach(f => cardGrid.insertBefore(buildCard(f, fs), cardGrid.firstChild));
}

function buildCard(f, fs) {
  const cls = f.sucesso ? (f.conformeDepois ? 'ok' : 'warn') : 'err';
  const card = el('div', `r-card ${cls}`);

  const delta = f.sucesso ? f.pontuacaoDepois - f.pontuacaoAntes : 0;
  const dStr  = delta > 0.05 ? `+${delta.toFixed(1)}%` : delta < -0.05 ? `${delta.toFixed(1)}%` : 'sem alteração';

  // ── Header ─────────────────────────────────────────────────────────
  const top = el('div', 'r-top');
  top.innerHTML = `
    <div class="r-dot"></div>
    <div class="r-info">
      <div class="r-name" title="${esc(f.nomeOriginal)}">${esc(f.nomeOriginal)}</div>
      <div class="r-tag">${f.sucesso
        ? `${f.violacoesDepois} violações · ${dStr}`
        : `Falha: ${esc((f.erros || ['?'])[0])}`}</div>
    </div>`;
  card.appendChild(top);

  if (!f.sucesso) return card;

  // ── Applied corrections strip ──────────────────────────────────────
  const applied = el('div', 'r-applied');
  applied.innerHTML = `<span class="r-applied-label">Aplicado:</span>`;

  const chips = [
    { key: 'adicionarTags',             label: 'Tags',         cls: 'chip-auto' },
    { key: 'altTextImagens',            label: 'Alt text',     cls: 'chip-auto' },
    { key: 'corrigirTitulo',            label: 'Título',       cls: 'chip-meta' },
    { key: 'corrigirIdioma',            label: 'Idioma',       cls: 'chip-meta' },
    { key: 'corrigirViewerPreferences', label: 'Viewer Prefs', cls: 'chip-pdfua' },
    { key: 'corrigirMetadadasXMP',      label: 'XMP',          cls: 'chip-pdfua' },
  ];
  chips.forEach(c => {
    const chip = el('span', `r-chip ${fs[c.key] ? c.cls : 'chip-skip'}`, c.label);
    applied.appendChild(chip);
  });
  card.appendChild(applied);

  // ── Scores ─────────────────────────────────────────────────────────
  const scores = el('div', 'r-scores');
  [
    { label: 'Global',     b: f.pontuacaoAntes, a: f.pontuacaoDepois  },
    { label: 'EN 301 549', b: f.eN301549Antes,  a: f.eN301549Depois   },
    { label: 'WCAG 2.1',   b: f.wcaG21Antes,    a: f.wcaG21Depois     },
    { label: 'WCAG 2.2',   b: f.wcaG22Antes,    a: f.wcaG22Depois     },
  ].forEach(n => {
    const d  = n.a - n.b;
    const ds = d > 0.05 ? `+${d.toFixed(1)}%` : d < -0.05 ? `${d.toFixed(1)}%` : '';
    const sc_ = el('div', 'r-score');
    sc_.innerHTML = `
      <div class="r-slabel">${n.label}</div>
      <div class="r-svals">
        <span class="r-safter ${sc(n.a)}">${fmt(n.a)}</span>
        <span class="r-sbefore">${fmt(n.b)}</span>
        ${ds ? `<span class="r-sdelta ${d>0?'c-good':'c-bad'}">${ds}</span>` : ''}
      </div>
      <div class="r-sbar"><div class="r-sfill ${sf(n.a)}" style="width:${n.a}%"></div></div>`;
    scores.appendChild(sc_);
  });
  card.appendChild(scores);

  // ── Stats strip ────────────────────────────────────────────────────
  if (f.estatisticas) {
    const s = f.estatisticas;
    const stats = el('div', 'r-stats');
    stats.innerHTML = `
      <span>${s.paginas} <span>pág.</span></span>
      <span>${s.imagens} <span>imgs</span> · ${s.imagensComAlt} c/alt</span>
      <span>${s.links} <span>links</span></span>
      <span>${s.formularios} <span>campos</span></span>
      <span>${s.tagged
        ? '<span class="tagged-yes">✓ tagged</span>'
        : '<span class="tagged-no">não tagged</span>'}</span>`;
    card.appendChild(stats);
  }

  // ── Actions ────────────────────────────────────────────────────────
  const acts = el('div', 'r-actions');
  const detId = `dt_${Math.random().toString(36).slice(2)}`;

  if (f.urlDownloadPdf) {
    const a = el('a', 'r-dl r-dl-pdf');
    a.href = f.urlDownloadPdf; a.download = '';
    a.innerHTML = `<svg viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="2.2"><path d="M10 2v8m0 0l-3-3m3 3l3-3"/><path d="M3 14v2a1 1 0 001 1h12a1 1 0 001-1v-2"/></svg> PDF remediado`;
    acts.appendChild(a);
  }
  if (f.urlDownloadRelatorio) {
    const a = el('a', 'r-dl r-dl-report');
    a.href = f.urlDownloadRelatorio; a.download = '';
    a.innerHTML = `<svg viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="2.2"><rect x="4" y="2" width="12" height="16" rx="1.5"/><path d="M7 7h6M7 10h6M7 13h4"/></svg> Relatório`;
    acts.appendChild(a);
  }

  const hasDetail = f.acoesRealizadas?.length || f.acoesNaoRealizadas?.length;
  if (hasDetail) {
    const tog = el('button', 'r-toggle', 'detalhes ›');
    tog.addEventListener('click', () => {
      const open = $( detId).classList.toggle('open');
      tog.textContent = open ? 'ocultar ‹' : 'detalhes ›';
    });
    acts.appendChild(tog);
  }
  card.appendChild(acts);

  // ── Detail drawer ──────────────────────────────────────────────────
  if (hasDetail) {
    const det = el('div', 'r-detail'); det.id = detId;
    const cols = el('div', 'r-detail-cols');

    if (f.acoesRealizadas?.length) {
      const sec = el('div', '');
      sec.innerHTML = `<div class="detail-sec-title">✓ Ações realizadas (${f.acoesRealizadas.length})</div>`;
      const ul = el('ul', 'detail-list done-list');
      f.acoesRealizadas.forEach(a => { const li = el('li'); li.textContent = a; ul.appendChild(li); });
      sec.appendChild(ul); cols.appendChild(sec);
    }

    if (f.acoesNaoRealizadas?.length) {
      const sec = el('div', '');
      sec.innerHTML = `<div class="detail-sec-title">⚠ Revisão manual necessária (${f.acoesNaoRealizadas.length})</div>`;
      const ul = el('ul', 'detail-list warn-list');
      f.acoesNaoRealizadas.forEach(a => { const li = el('li'); li.textContent = a; ul.appendChild(li); });
      sec.appendChild(ul); cols.appendChild(sec);
    }

    det.appendChild(cols); card.appendChild(det);
  }

  return card;
}

// ── Helpers ────────────────────────────────────────────────────────────
function setProgress(pct, msg) {
  progressFill.style.width = pct + '%';
  progressPct.textContent  = pct + '%';
  if (msg) progressMsg.textContent = msg;
}

function setStatus(type, text) {
  headerStatus.querySelector('.status-dot').className = 'status-dot' + (type === 'busy' ? ' busy' : '');
  headerStatus.querySelector('.status-text').textContent = text;
}
