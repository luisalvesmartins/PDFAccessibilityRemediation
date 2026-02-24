// ── PDF Access Remediation — Frontend App ──────────────────────────────
'use strict';

const $ = id => document.getElementById(id);
const el = (tag, cls, html) => { const e = document.createElement(tag); if(cls) e.className=cls; if(html!==undefined) e.innerHTML=html; return e; };
const fmt = n => n != null ? n.toFixed(1) + '%' : '—';
const fmtBytes = b => b < 1024*1024 ? (b/1024).toFixed(0)+'KB' : (b/1024/1024).toFixed(1)+'MB';
const scoreClass = s => s >= 80 ? 'good' : s >= 50 ? 'ok_' : 'bad';
const fillClass  = s => s >= 80 ? 'fill-good' : s >= 50 ? 'fill-ok' : 'fill-bad';

// ── State ──────────────────────────────────────────────────────────────
let queue = []; // [{file, id, state}]

// ── DOM refs ───────────────────────────────────────────────────────────
const dropZone       = $('dropZone');
const fileInput      = $('fileInput');
const browseBtn      = $('browseBtn');
const fileQueue      = $('fileQueue');
const controls       = $('controls');
const queueSummary   = $('queueSummary');
const clearBtn       = $('clearBtn');
const processBtn     = $('processBtn');
const progressArea   = $('progressArea');
const progressMsg    = $('progressMsg');
const progressPct    = $('progressPct');
const progressFill   = $('progressFill');
const logLines       = $('logLines');
const resultsEmpty   = $('resultsEmpty');
const resultsContent = $('resultsContent');
const resultsTitle   = $('resultsTitle');
const cardGrid       = $('cardGrid');
const zipBtn         = $('zipBtn');
const headerStatus   = $('headerStatus');

// ── Drag & Drop ────────────────────────────────────────────────────────
dropZone.addEventListener('dragover', e => { e.preventDefault(); dropZone.classList.add('over'); });
dropZone.addEventListener('dragleave', () => dropZone.classList.remove('over'));
dropZone.addEventListener('drop', e => {
  e.preventDefault();
  dropZone.classList.remove('over');
  addFiles([...e.dataTransfer.files]);
});
dropZone.addEventListener('click', e => { if(e.target !== browseBtn) fileInput.click(); });
dropZone.addEventListener('keydown', e => { if(e.key==='Enter'||e.key===' ') fileInput.click(); });
browseBtn.addEventListener('click', e => { e.stopPropagation(); fileInput.click(); });
fileInput.addEventListener('change', () => { addFiles([...fileInput.files]); fileInput.value=''; });

// ── File management ────────────────────────────────────────────────────
function addFiles(files) {
  const pdfs = files.filter(f => f.name.toLowerCase().endsWith('.pdf') || f.type === 'application/pdf');
  if (!pdfs.length) return;

  pdfs.forEach(f => {
    if (queue.find(q => q.file.name === f.name && q.file.size === f.size)) return;
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
      <div class="file-row-icon">PDF</div>
      <div class="file-row-body">
        <div class="file-row-name">${escHtml(item.file.name)}</div>
        <div class="file-row-meta">${fmtBytes(item.file.size)}</div>
        <div class="file-row-state ${item.state}" id="state_${item.id}">
          ${stateLabel(item.state)}
        </div>
      </div>
      ${item.state === 'pending' ? `<button class="file-row-del" data-id="${item.id}" title="Remover" aria-label="Remover ${escHtml(item.file.name)}">
        <svg width="14" height="14" viewBox="0 0 14 14" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M2 2l10 10M12 2L2 12"/>
        </svg>
      </button>` : ''}
    `;
    fileQueue.appendChild(row);
  });

  // Remove buttons
  fileQueue.querySelectorAll('.file-row-del').forEach(btn => {
    btn.addEventListener('click', () => {
      queue = queue.filter(q => q.id !== btn.dataset.id);
      renderQueue();
    });
  });

  const hasPending = queue.some(q => q.state === 'pending');
  controls.classList.toggle('hidden', queue.length === 0);
  queueSummary.textContent = `${queue.length} ficheiro${queue.length !== 1 ? 's' : ''} selecionado${queue.length !== 1 ? 's' : ''}`;
  processBtn.disabled = !hasPending;
}

function stateLabel(state) {
  return {
    pending: 'aguardar processamento',
    running: '⟳ a processar…',
    done:    '✓ concluído',
    error:   '✗ erro'
  }[state] || state;
}

clearBtn.addEventListener('click', () => {
  queue = [];
  renderQueue();
  progressArea.classList.add('hidden');
});

// ── Process ────────────────────────────────────────────────────────────
processBtn.addEventListener('click', async () => {
  const pending = queue.filter(q => q.state === 'pending');
  if (!pending.length) return;

  // UI: switch to processing state
  processBtn.disabled = true;
  clearBtn.disabled = true;
  progressArea.classList.remove('hidden');
  setStatus('busy', 'A processar…');
  logLines.innerHTML = '';

  const steps = [
    'A carregar ficheiros…',
    'A analisar acessibilidade (EN 301 549 · WCAG 2.1/2.2)…',
    'A construir árvore de tags semânticas…',
    'A aplicar texto alternativo às imagens…',
    'A corrigir metadados e idioma…',
    'A gerar relatórios de conformidade…',
    'A empacotar ficheiros remediados…'
  ];

  // Add log lines
  steps.forEach((s, i) => {
    const line = el('div', 'log-line', s);
    line.id = `log_${i}`;
    logLines.appendChild(line);
  });

  // Mark all as running in queue
  pending.forEach(item => {
    item.state = 'running';
    const stateEl = $(`state_${item.id}`);
    if (stateEl) { stateEl.className = `file-row-state running`; stateEl.textContent = stateLabel('running'); }
  });

  // Animate progress steps while upload is in flight
  let stepIdx = 0;
  const stepInterval = setInterval(() => {
    const prev = $(`log_${stepIdx - 1}`);
    if (prev) { prev.className = 'log-line done'; }
    const cur = $(`log_${stepIdx}`);
    if (cur) { cur.className = 'log-line active'; }
    const pct = Math.min(90, Math.round(((stepIdx + 1) / steps.length) * 90));
    setProgress(pct, steps[stepIdx] || 'A processar…');
    stepIdx++;
    if (stepIdx >= steps.length) clearInterval(stepInterval);
  }, 800);

  try {
    const formData = new FormData();
    pending.forEach(item => formData.append('files', item.file));

    const res = await fetch('/api/pdf/remediar', { method: 'POST', body: formData });
    clearInterval(stepInterval);

    // Mark all log lines done
    steps.forEach((_, i) => { const l = $(`log_${i}`); if(l) l.className='log-line done'; });
    setProgress(100, 'Concluído!');

    if (!res.ok) {
      const err = await res.json().catch(() => ({ erro: 'Erro desconhecido' }));
      throw new Error(err.erro || 'Servidor retornou erro ' + res.status);
    }

    const data = await res.json();

    // Update queue states
    data.ficheiros.forEach((r, idx) => {
      const item = pending[idx];
      if (item) {
        item.state = r.sucesso ? 'done' : 'error';
        const stateEl = $(`state_${item.id}`);
        if (stateEl) {
          stateEl.className = `file-row-state ${item.state}`;
          stateEl.textContent = stateLabel(item.state);
        }
      }
    });

    renderResults(data);
    setStatus('ok', 'Pronto');

  } catch (err) {
    clearInterval(stepInterval);
    pending.forEach(item => {
      item.state = 'error';
      const stateEl = $(`state_${item.id}`);
      if (stateEl) { stateEl.className='file-row-state error'; stateEl.textContent=stateLabel('error'); }
    });
    setStatus('ok', 'Erro');
    addLog(`Erro: ${err.message}`, 'bad');
    console.error(err);
  }

  processBtn.disabled = false;
  clearBtn.disabled = false;
  queue = queue.filter(q => q.state !== 'done');
  renderQueue();
});

// ── Render Results ─────────────────────────────────────────────────────
function renderResults(data) {
  resultsEmpty.classList.add('hidden');
  resultsContent.classList.remove('hidden');

  const ok = data.sucesso, total = data.total;
  resultsTitle.textContent = `${ok} de ${total} ficheiro${total!==1?'s':''} remediado${total!==1?'s':''}`;

  if (data.urlZip) {
    zipBtn.href = data.urlZip;
    zipBtn.classList.remove('hidden');
  }

  data.ficheiros.forEach(f => cardGrid.insertBefore(buildCard(f), cardGrid.firstChild));
}

function buildCard(f) {
  const cls = f.sucesso ? (f.conformeDepois ? 'ok' : 'warn') : 'err';

  const card = el('div', `r-card ${cls}`);

  // Header
  const delta = f.sucesso ? (f.pontuacaoDepois - f.pontuacaoAntes) : 0;
  const deltaStr = delta >= 0 ? `+${delta.toFixed(1)}%` : `${delta.toFixed(1)}%`;
  card.innerHTML = `
    <div class="r-card-top">
      <div class="r-card-indicator"></div>
      <div class="r-card-info">
        <div class="r-card-name">${escHtml(f.nomeOriginal)}</div>
        <div class="r-card-tag">${f.sucesso
          ? `${f.violacoesDepois} violações restantes · ${deltaStr} melhoria`
          : `Falha: ${escHtml((f.erros||['Erro desconhecido'])[0])}`
        }</div>
      </div>
    </div>
  `;

  // Scores
  if (f.sucesso) {
    const scoresEl = el('div', 'r-scores');
    const norms = [
      { label: 'Global',     before: f.pontuacaoAntes,  after: f.pontuacaoDepois },
      { label: 'EN 301 549', before: f.eN301549Antes,   after: f.eN301549Depois  },
      { label: 'WCAG 2.1',   before: f.wcaG21Antes,     after: f.wcaG21Depois    },
      { label: 'WCAG 2.2',   before: f.wcaG22Antes,     after: f.wcaG22Depois    },
    ];
    console.log(norms)
      console.log(f)
    norms.forEach(n => {
      const d = (n.after - n.before);
      const ds = d > 0.05 ? `+${d.toFixed(1)}%` : d < -0.05 ? `${d.toFixed(1)}%` : '—';
      const sc = el('div', 'r-score');
      sc.innerHTML = `
        <div class="r-score-label">${n.label}</div>
        <div class="r-score-vals">
          <span class="r-score-after ${scoreClass(n.after)}">${fmt(n.after)}</span>
          <span class="r-score-before">${fmt(n.before)}</span>
          <span class="r-score-delta ${d>0?'good':d<0?'bad':''}">${ds}</span>
        </div>
        <div class="r-score-bar">
          <div class="r-score-fill ${fillClass(n.after)}" style="width:${n.after}%"></div>
        </div>
      `;
      scoresEl.appendChild(sc);
    });
    card.appendChild(scoresEl);

    // Stats strip
    if (f.estatisticas) {
      const stats = el('div', 'r-stats');
      const s = f.estatisticas;
      stats.innerHTML = `
        <div>${s.paginas} <span>pág.</span></div>
        <div>${s.imagens} <span>imgs</span></div>
        <div>${s.imagensComAlt} <span>c/alt</span></div>
        <div>${s.links} <span>links</span></div>
        <div>${s.tagged ? '<span style="color:var(--c-green)">tagged ✓</span>' : '<span style="color:var(--c-amber)">não tagged</span>'}</div>
      `;
      card.appendChild(stats);
    }
  }

  // Actions
  const actionsEl = el('div', 'r-actions');
  const detailId = `detail_${Math.random().toString(36).slice(2)}`;

  if (f.sucesso && f.urlDownloadPdf) {
    const dlPdf = el('a', 'r-dl r-dl-pdf');
    dlPdf.href = f.urlDownloadPdf;
    dlPdf.download = '';
    dlPdf.innerHTML = `<svg viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="2.2"><path d="M10 2v8m0 0l-3-3m3 3l3-3"/><path d="M3 14v2a1 1 0 001 1h12a1 1 0 001-1v-2"/></svg> PDF remediado`;
    actionsEl.appendChild(dlPdf);
  }

  if (f.urlDownloadRelatorio) {
    const dlRep = el('a', 'r-dl r-dl-report');
    dlRep.href = f.urlDownloadRelatorio;
    dlRep.download = '';
    dlRep.innerHTML = `<svg viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="2.2"><rect x="4" y="2" width="12" height="16" rx="1.5"/><path d="M7 7h6M7 10h6M7 13h4"/></svg> Relatório`;
    actionsEl.appendChild(dlRep);
  }

  const hasDetails = (f.acoesRealizadas?.length > 0) || (f.acoesNaoRealizadas?.length > 0);
  if (hasDetails) {
    const toggle = el('button', 'r-toggle');
    toggle.textContent = 'ver detalhes ›';
    toggle.addEventListener('click', () => {
      const det = $(detailId);
      const open = det.classList.toggle('open');
      toggle.textContent = open ? 'ocultar ‹' : 'ver detalhes ›';
    });
    actionsEl.appendChild(toggle);
  }
  card.appendChild(actionsEl);

  // Detail drawer
  if (hasDetails) {
    const det = el('div', 'r-detail');
    det.id = detailId;
    const cols = el('div', 'r-detail-cols');

    if (f.acoesRealizadas?.length) {
      const sec = el('div', 'r-detail-sec');
      sec.innerHTML = `<div class="sec-title">✓ Ações realizadas (${f.acoesRealizadas.length})</div>`;
      const ul = el('ul', 'r-detail-list done-list');
      f.acoesRealizadas.forEach(a => { const li = el('li'); li.textContent = a; ul.appendChild(li); });
      sec.appendChild(ul);
      cols.appendChild(sec);
    }

    if (f.acoesNaoRealizadas?.length) {
      const sec = el('div', 'r-detail-sec');
      sec.innerHTML = `<div class="sec-title">⚠ Revisão manual necessária (${f.acoesNaoRealizadas.length})</div>`;
      const ul = el('ul', 'r-detail-list warn-list');
      f.acoesNaoRealizadas.forEach(a => { const li = el('li'); li.textContent = a; ul.appendChild(li); });
      sec.appendChild(ul);
      cols.appendChild(sec);
    }

    det.appendChild(cols);
    card.appendChild(det);
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
  const dot  = headerStatus.querySelector('.status-dot');
  const span = headerStatus.querySelector('.status-text');
  dot.className  = 'status-dot' + (type === 'busy' ? ' busy' : '');
  span.textContent = text;
}

function addLog(msg, cls) {
  const line = el('div', `log-line ${cls||''}`, escHtml(msg));
  logLines.appendChild(line);
}

function escHtml(str) {
  return String(str).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}
