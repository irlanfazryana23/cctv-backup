namespace CctvBackup.App;

public static class WebUiHtml
{
    public const string Page = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>CCTV Backup — Linux Edition</title>
<style>
  :root {
    --bg: #f3f5f8;
    --panel: #ffffff;
    --border: #d0d7de;
    --text: #1f2328;
    --muted: #59636e;
    --primary: #0969da;
    --primary-hover: #0550ae;
    --success: #1a7f37;
    --warn: #9a6700;
    --danger: #cf222e;
    --guide-bg: #ddf4ff;
    --guide-border: #54aeff;
  }
  * { box-sizing: border-box; }
  body {
    margin: 0;
    font-family: system-ui, -apple-system, "Segoe UI", Roboto, Ubuntu, Cantarell, sans-serif;
    font-size: 13px;
    color: var(--text);
    background: var(--bg);
    height: 100vh;
    display: flex;
    flex-direction: column;
    overflow: hidden;
  }
  header {
    background: #1b2430;
    color: #fff;
    padding: 8px 14px;
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    flex-shrink: 0;
  }
  .brand {
    display: flex;
    align-items: center;
    gap: 10px;
    font-weight: 600;
    font-size: 15px;
  }
  .badge {
    font-size: 11px;
    padding: 2px 8px;
    border-radius: 12px;
    background: #2ea043;
    color: #fff;
    font-weight: 500;
  }
  .badge.warn { background: #d29922; }
  .header-controls {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .guide-banner {
    background: var(--guide-bg);
    border-bottom: 1px solid var(--guide-border);
    padding: 8px 14px;
    font-weight: 500;
    color: #0550ae;
    flex-shrink: 0;
  }
  .main-grid {
    flex: 1;
    display: grid;
    grid-template-columns: 340px 1fr;
    gap: 10px;
    padding: 10px;
    min-height: 0;
  }
  .panel {
    background: var(--panel);
    border: 1px solid var(--border);
    border-radius: 6px;
    display: flex;
    flex-direction: column;
    min-height: 0;
  }
  .panel-header {
    padding: 8px 10px;
    font-weight: 600;
    border-bottom: 1px solid var(--border);
    background: #f6f8fa;
    border-radius: 6px 6px 0 0;
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 6px;
  }
  .panel-body {
    padding: 8px 10px;
    flex: 1;
    overflow: auto;
    min-height: 0;
  }
  .right-col {
    display: flex;
    flex-direction: column;
    gap: 10px;
    min-height: 0;
  }
  .top-settings {
    display: grid;
    grid-template-columns: 1fr 1.2fr;
    gap: 10px;
    flex-shrink: 0;
  }
  button, select, input {
    font: inherit;
  }
  button {
    padding: 5px 10px;
    border: 1px solid var(--border);
    background: #f6f8fa;
    border-radius: 4px;
    cursor: pointer;
    color: var(--text);
  }
  button:hover:not(:disabled) { background: #eaeef2; }
  button:disabled { opacity: 0.5; cursor: not-allowed; }
  button.primary {
    background: var(--primary);
    color: #fff;
    border-color: var(--primary);
    font-weight: 600;
  }
  button.primary:hover:not(:disabled) { background: var(--primary-hover); }
  button.success {
    background: var(--success);
    color: #fff;
    border-color: var(--success);
    font-weight: 600;
  }
  button.danger {
    color: var(--danger);
  }
  input[type="text"], input[type="password"], input[type="number"], input[type="datetime-local"], select {
    padding: 5px 8px;
    border: 1px solid var(--border);
    border-radius: 4px;
    background: #fff;
  }
  .branch-list {
    list-style: none;
    margin: 0;
    padding: 0;
  }
  .branch-item {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 6px 8px;
    border-bottom: 1px solid #eef1f4;
    cursor: pointer;
    border-radius: 4px;
  }
  .branch-item:hover { background: #f6f8fa; }
  .branch-item.active { background: #ddf4ff; }
  .branch-info { flex: 1; min-width: 0; }
  .branch-name { font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .branch-meta { font-size: 11px; color: var(--muted); display: flex; gap: 6px; align-items: center; }
  .tag {
    font-size: 10px;
    padding: 1px 5px;
    border-radius: 4px;
    background: #eaeef2;
    color: #1f2328;
  }
  .tag.cloud { background: #ddf4ff; color: #0550ae; }
  .tag.ip { background: #dafbe1; color: #116329; }
  .tag.nopw { background: #ffebe9; color: #cf222e; }
  .cam-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(92px, 1fr));
    gap: 4px;
    max-height: 115px;
    overflow-y: auto;
  }
  .cam-item {
    display: flex;
    align-items: center;
    gap: 5px;
    padding: 3px 5px;
    background: #f6f8fa;
    border: 1px solid #eaeef2;
    border-radius: 4px;
    font-size: 12px;
  }
  .toolbar {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
    padding: 6px 8px;
    border-bottom: 1px solid var(--border);
    background: #fcfcfd;
    align-items: center;
  }
  table {
    width: 100%;
    border-collapse: collapse;
    font-size: 12px;
  }
  th, td {
    text-align: left;
    padding: 5px 8px;
    border-bottom: 1px solid #eef1f4;
    white-space: nowrap;
  }
  th {
    position: sticky;
    top: 0;
    background: #f6f8fa;
    z-index: 1;
    border-bottom: 1px solid var(--border);
  }
  .progress-bar {
    width: 70px;
    height: 12px;
    background: #eaeef2;
    border-radius: 6px;
    overflow: hidden;
    display: inline-block;
    vertical-align: middle;
  }
  .progress-fill {
    height: 100%;
    background: var(--primary);
  }
  .status-Done { color: var(--success); font-weight: 600; }
  .status-Failed { color: var(--danger); font-weight: 600; }
  .status-Downloading { color: var(--primary); font-weight: 600; }
  .status-Skipped { color: var(--muted); }
  .log-box {
    font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
    font-size: 11.5px;
    background: #0d1117;
    color: #c9d1d9;
    padding: 8px;
    height: 125px;
    overflow-y: auto;
    white-space: pre-wrap;
    border-radius: 0 0 6px 6px;
  }
  .modal-backdrop {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.45);
    display: none;
    align-items: center;
    justify-content: center;
    z-index: 100;
  }
  .modal-backdrop.open { display: flex; }
  .modal {
    background: #fff;
    border-radius: 8px;
    width: 480px;
    max-width: 95vw;
    max-height: 90vh;
    display: flex;
    flex-direction: column;
    box-shadow: 0 10px 30px rgba(0,0,0,0.25);
  }
  .modal-header {
    padding: 10px 14px;
    font-weight: 600;
    font-size: 14px;
    border-bottom: 1px solid var(--border);
    display: flex;
    justify-content: space-between;
    align-items: center;
  }
  .modal-body {
    padding: 14px;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 10px;
  }
  .modal-footer {
    padding: 10px 14px;
    border-top: 1px solid var(--border);
    display: flex;
    justify-content: flex-end;
    gap: 8px;
    background: #f6f8fa;
    border-radius: 0 0 8px 8px;
  }
  .form-row {
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  .form-row label { font-weight: 600; font-size: 12px; }
  .hint { font-size: 11px; color: var(--muted); }
  .error-box {
    background: #ffebe9;
    border: 1px solid #ff818266;
    color: var(--danger);
    padding: 8px;
    border-radius: 4px;
    font-size: 12px;
  }
</style>
</head>
<body>

<header>
  <div class="brand">
    <span>CCTV Backup (Linux)</span>
    <span id="backendBadge" class="badge">Loading...</span>
  </div>
  <div class="header-controls">
    <button onclick="openSdkModal()" title="Configure DVR SDK folder (NetSdk.dll / libnetsdk.so)">SDK Folder...</button>
    <button onclick="resetDriver()" title="Reset DVR SDK driver">Reset Driver</button>
    <select id="langSelect" onchange="changeLanguage(this.value)">
      <option value="en">English</option>
      <option value="id">Bahasa Indonesia</option>
    </select>
  </div>
</header>

<div id="guideBanner" class="guide-banner">Loading...</div>

<div class="main-grid">
  <!-- LEFT: Branches -->
  <div class="panel">
    <div class="panel-header">
      <span id="lblBranchesHeader">1. Branches (tick the ones to back up)</span>
    </div>
    <div class="toolbar">
      <button onclick="openBranchModal(null)" id="btnAddBranch" class="primary">+ Add branch...</button>
      <button onclick="editSelectedBranch()" id="btnEditBranch">Edit branch...</button>
      <button onclick="removeSelectedBranch()" id="btnRemoveBranch" class="danger">Remove</button>
      <button onclick="testSelectedBranch()" id="btnTestConn">Test connection</button>
      <button onclick="openCmsModal()" id="btnImportCms">Import from CMS...</button>
    </div>
    <div class="toolbar" style="justify-content: space-between;">
      <input type="text" id="branchFilter" placeholder="Filter branches..." oninput="renderBranches()" style="flex:1;" />
      <button onclick="tickAllBranches(true)" id="btnBranchAll">All</button>
      <button onclick="tickAllBranches(false)" id="btnBranchNone">None</button>
    </div>
    <div class="panel-body" style="padding: 4px;">
      <ul id="branchList" class="branch-list"></ul>
    </div>
  </div>

  <!-- RIGHT: Cameras, Period, Destination, Recordings, Log -->
  <div class="right-col">
    <div class="top-settings">
      <!-- 2. Cameras -->
      <div class="panel">
        <div class="panel-header">
          <span id="lblCamerasHeader">2. Cameras</span>
          <div>
            <button onclick="tickAllCameras(true)" style="padding:2px 6px;font-size:11px;">All</button>
            <button onclick="tickAllCameras(false)" style="padding:2px 6px;font-size:11px;">None</button>
          </div>
        </div>
        <div class="panel-body">
          <div id="cameraContainer" class="cam-grid"></div>
        </div>
      </div>

      <!-- 3. Period & 4. Save to -->
      <div class="panel">
        <div class="panel-header">
          <span id="lblPeriodHeader">3. Period (the DVR's own clock) &amp; 4. Save to</span>
        </div>
        <div class="panel-body" style="display:flex;flex-direction:column;gap:8px;">
          <div style="display:flex;gap:8px;align-items:center;flex-wrap:wrap;">
            <label><span id="lblFrom">From</span>: <input type="datetime-local" id="fromInput" onchange="updatePeriod()" /></label>
            <label><span id="lblTo">To</span>: <input type="datetime-local" id="toInput" onchange="updatePeriod()" /></label>
          </div>
          <div style="display:flex;gap:6px;flex-wrap:wrap;">
            <button onclick="setPreset('yesterday')" id="btnYesterday" style="padding:2px 8px;font-size:11.5px;">Yesterday</button>
            <button onclick="setPreset('today')" id="btnToday" style="padding:2px 8px;font-size:11.5px;">Today so far</button>
            <button onclick="setPreset('week')" id="btnWeek" style="padding:2px 8px;font-size:11.5px;">Last 7 days</button>
          </div>
          <div style="border-top:1px solid #eef1f4;padding-top:6px;display:flex;gap:6px;align-items:center;">
            <strong id="lblSaveTo">4. Save to:</strong>
            <input type="text" id="destInput" placeholder="/media/usb/CCTV-Backup" style="flex:1;" onchange="saveDestination(this.value)" />
            <button onclick="browseDestination()" id="btnBrowse">Browse...</button>
            <button onclick="openDestinationFolder()" id="btnOpenFolder">Open folder</button>
          </div>
          <div id="diskStatus" class="hint"></div>
        </div>
      </div>
    </div>

    <!-- 5 & 6: Recordings -->
    <div class="panel" style="flex:1;">
      <div class="panel-header">
        <div style="display:flex;gap:8px;align-items:center;">
          <button onclick="startFind()" id="btnFind" class="primary">5. Find recordings</button>
          <button onclick="startDownload()" id="btnDownload" class="success">6. Download selected</button>
          <button onclick="cancelOp()" id="btnCancel" class="danger" disabled>Cancel</button>
        </div>
        <div style="display:flex;gap:8px;align-items:center;font-weight:normal;font-size:12px;">
          <span id="jobSummaryText"></span>
          <button onclick="tickAllJobs(true)" id="btnTickAllJobs">Tick all</button>
          <button onclick="tickAllJobs(false)" id="btnUntickAllJobs">Untick all</button>
        </div>
      </div>
      <div class="panel-body" style="padding:0;">
        <table>
          <thead>
            <tr>
              <th style="width:36px;" id="thSave">Save</th>
              <th id="thBranch">Branch</th>
              <th id="thCamera">Camera</th>
              <th id="thRecorded">Recorded</th>
              <th id="thSize">Size</th>
              <th id="thStatus">Status</th>
              <th>%</th>
              <th id="thSpeed">Speed</th>
              <th id="thLeft">Left</th>
              <th id="thProblem">Problem</th>
            </tr>
          </thead>
          <tbody id="jobsBody"></tbody>
        </table>
      </div>
    </div>

    <!-- Activity Log -->
    <div class="panel" style="flex-shrink:0;">
      <div class="panel-header" style="padding:5px 10px;">
        <span id="lblActivity">Activity (what the program is doing)</span>
      </div>
      <div id="logBox" class="log-box"></div>
    </div>
  </div>
</div>

<!-- Add / Edit Branch Modal -->
<div id="branchModal" class="modal-backdrop">
  <div class="modal">
    <div class="modal-header">
      <span id="branchModalTitle">Add branch</span>
      <button onclick="closeModal('branchModal')">&times;</button>
    </div>
    <div class="modal-body">
      <div id="branchModalError" class="error-box" style="display:none;"></div>
      <div class="form-row">
        <label id="lblModalBranchName">Branch name</label>
        <input type="text" id="bmName" placeholder="e.g. Branch-01" />
      </div>
      <div class="form-row">
        <label>Area (optional)</label>
        <input type="text" id="bmArea" placeholder="e.g. AREA 1" />
      </div>
      <div class="form-row">
        <label id="lblModalLoginType">Login type</label>
        <div style="display:flex;gap:16px;">
          <label><input type="radio" name="bmMode" value="Cloud" checked onchange="toggleBranchMode()" /> <span id="lblModalCloud">Cloud (Serial ID)</span></label>
          <label><input type="radio" name="bmMode" value="Ip" onchange="toggleBranchMode()" /> <span id="lblModalIp">IP address</span></label>
        </div>
      </div>
      <div id="bmCloudGroup" class="form-row">
        <label id="lblModalSerial">Serial ID</label>
        <input type="text" id="bmSerial" placeholder="e.g. 0123456789abcdef" />
        <span class="hint" id="lblSerialHint">The cloud ID shown in the DVR (the "Serial ID" in the CMS).</span>
      </div>
      <div id="bmIpGroup" class="form-row" style="display:none;">
        <div style="display:grid;grid-template-columns:1fr 110px;gap:8px;">
          <div class="form-row">
            <label id="lblModalAddress">IP address or domain</label>
            <input type="text" id="bmAddress" placeholder="192.168.1.20" />
          </div>
          <div class="form-row">
            <label id="lblModalPort">Port</label>
            <input type="number" id="bmPort" value="34567" />
          </div>
        </div>
        <span class="hint" id="lblIpHint">The DVR's local address and Media Port (usually 34567).</span>
      </div>
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:8px;">
        <div class="form-row">
          <label id="lblModalUser">User name</label>
          <input type="text" id="bmUser" value="admin" />
        </div>
        <div class="form-row">
          <label id="lblModalPass">Password</label>
          <input type="password" id="bmPass" />
        </div>
      </div>
      <div style="display:flex;justify-content:space-between;align-items:center;">
        <label style="font-size:12px;"><input type="checkbox" onchange="document.getElementById('bmPass').type = this.checked ? 'text' : 'password'" /> <span id="lblShowPass">Show password</span></label>
        <span id="bmPassHint" class="hint"></span>
      </div>
      <div class="form-row">
        <label>Cameras (0 = detect automatically via Test connection)</label>
        <input type="number" id="bmCameras" value="0" min="0" max="64" />
      </div>
    </div>
    <div class="modal-footer">
      <button onclick="closeModal('branchModal')">Cancel</button>
      <button onclick="saveBranchForm()" class="primary">Save</button>
    </div>
  </div>
</div>

<!-- Folder Browser Modal -->
<div id="folderModal" class="modal-backdrop">
  <div class="modal" style="width:560px;">
    <div class="modal-header">
      <span>Choose Backup Destination Folder</span>
      <button onclick="closeModal('folderModal')">&times;</button>
    </div>
    <div class="modal-body">
      <div id="folderRoots" style="display:flex;gap:6px;flex-wrap:wrap;"></div>
      <div style="display:flex;gap:6px;">
        <input type="text" id="fmCurrentPath" style="flex:1;" onkeydown="if(event.key==='Enter')loadDir(this.value)" />
        <button onclick="loadDir(document.getElementById('fmCurrentPath').value)">Go</button>
        <button onclick="createSubfolder()">+ New Folder</button>
      </div>
      <div id="fmList" style="border:1px solid var(--border);border-radius:4px;height:240px;overflow-y:auto;padding:4px;"></div>
    </div>
    <div class="modal-footer">
      <button onclick="closeModal('folderModal')">Cancel</button>
      <button onclick="selectModalFolder()" class="primary">Use This Folder</button>
    </div>
  </div>
</div>

<!-- Import from CMS Modal -->
<div id="cmsModal" class="modal-backdrop">
  <div class="modal" style="width:580px;">
    <div class="modal-header">
      <span>Import branches from the CMS (Data.xml)</span>
      <button onclick="closeModal('cmsModal')">&times;</button>
    </div>
    <div class="modal-body">
      <div id="cmsError" class="error-box" style="display:none;"></div>
      <div class="form-row">
        <label>Select CMS Data.xml file</label>
        <input type="file" id="cmsFileInput" accept=".xml" onchange="previewCmsFile(this)" />
      </div>
      <div class="form-row">
        <label>Password for the imported branches (optional)</label>
        <input type="password" id="cmsSharedPass" placeholder="Shared DVR password (or leave empty to set per branch)" />
      </div>
      <div id="cmsPreviewBox" style="border:1px solid var(--border);border-radius:4px;max-height:220px;overflow-y:auto;padding:8px;display:none;"></div>
    </div>
    <div class="modal-footer">
      <button onclick="closeModal('cmsModal')">Cancel</button>
      <button onclick="confirmCmsImport()" id="btnConfirmCms" class="primary" disabled>Import</button>
    </div>
  </div>
</div>

<!-- SDK Folder Modal -->
<div id="sdkModal" class="modal-backdrop">
  <div class="modal">
    <div class="modal-header">
      <span>DVR SDK Folder (NetSdk.dll / libnetsdk.so)</span>
      <button onclick="closeModal('sdkModal')">&times;</button>
    </div>
    <div class="modal-body">
      <div class="form-row">
        <label>SDK Directory Path</label>
        <input type="text" id="sdkPathInput" />
        <span class="hint">Cloud Serial ID &amp; IP work natively out-of-the-box in pure C#. Optionally point to a folder with native Linux libnetsdk.so.</span>
      </div>
    </div>
    <div class="modal-footer">
      <button onclick="closeModal('sdkModal')">Cancel</button>
      <button onclick="saveSdkFolder()" class="primary">Save</button>
    </div>
  </div>
</div>

<script>
let state = null;
let activeBranchName = null;
let editingBranchOrigName = null;
let loadedCmsXml = "";

async function api(path, body = null) {
  const opts = body !== null
    ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }
    : { method: 'GET' };
  const res = await fetch(path, opts);
  return await res.json();
}

function T(key) {
  if (state && state.strings && state.strings[key]) return state.strings[key];
  return key.replace('&', '');
}

async function refresh() {
  try {
    state = await api('/api/state');
    applyState();
  } catch (e) {
    console.error(e);
  }
}

function applyState() {
  if (!state) return;
  document.getElementById('langSelect').value = state.language;
  const badge = document.getElementById('backendBadge');
  badge.textContent = state.backend;
  badge.className = state.cloudReady ? 'badge' : 'badge warn';

  document.getElementById('guideBanner').textContent = state.guideText;

  // Translate static headers/buttons
  document.getElementById('lblBranchesHeader').textContent = T('1. Branches (tick the ones to back up)');
  document.getElementById('btnAddBranch').textContent = '+ ' + T('&Add branch...');
  document.getElementById('btnEditBranch').textContent = T('&Edit branch...');
  document.getElementById('btnRemoveBranch').textContent = T('Re&move branch');
  document.getElementById('btnTestConn').textContent = T('&Test connection');
  document.getElementById('btnImportCms').textContent = T('&Import from CMS...');
  document.getElementById('btnBranchAll').textContent = T('All');
  document.getElementById('btnBranchNone').textContent = T('None');
  document.getElementById('btnYesterday').textContent = T('Yesterday');
  document.getElementById('btnToday').textContent = T('Today so far');
  document.getElementById('btnWeek').textContent = T('Last 7 days');
  document.getElementById('btnBrowse').textContent = T('&Browse...');
  document.getElementById('btnOpenFolder').textContent = T('&Open folder');
  document.getElementById('btnFind').textContent = T('5. &Find recordings');
  document.getElementById('btnDownload').textContent = T('6. &Download selected');
  document.getElementById('btnCancel').textContent = T('&Cancel');
  document.getElementById('btnTickAllJobs').textContent = T('Tick all');
  document.getElementById('btnUntickAllJobs').textContent = T('Untick all');
  document.getElementById('lblActivity').textContent = T('Activity (what the program is doing)');

  if (document.activeElement !== document.getElementById('fromInput'))
    document.getElementById('fromInput').value = state.fromTime;
  if (document.activeElement !== document.getElementById('toInput'))
    document.getElementById('toInput').value = state.toTime;
  if (document.activeElement !== document.getElementById('destInput'))
    document.getElementById('destInput').value = state.destination;

  const diskEl = document.getElementById('diskStatus');
  diskEl.textContent = state.diskMessage || '';
  diskEl.style.color = state.diskOk ? 'var(--muted)' : 'var(--danger)';

  const busy = state.searching || state.downloading;
  document.getElementById('btnFind').disabled = busy;
  document.getElementById('btnDownload').disabled = busy || state.selectedCount === 0;
  document.getElementById('btnCancel').disabled = !busy;

  let summary = `${state.selectedCount} selected (${state.selectedSizeText})`;
  if (state.overallSpeed) summary += ` • ${state.overallSpeed}`;
  if (state.overallLeft) summary += ` • ${state.overallLeft} left`;
  document.getElementById('jobSummaryText').textContent = summary;

  if (!activeBranchName && state.branches.length > 0) {
    activeBranchName = state.branches[0].name;
  } else if (activeBranchName && !state.branches.some(b => b.name === activeBranchName)) {
    activeBranchName = state.branches.length > 0 ? state.branches[0].name : null;
  }

  renderBranches();
  renderCameras();
  renderJobs();

  const logBox = document.getElementById('logBox');
  const nextLog = (state.logs || []).join('\n');
  if (logBox.textContent !== nextLog) {
    logBox.textContent = nextLog;
    logBox.scrollTop = logBox.scrollHeight;
  }
}

function renderBranches() {
  if (!state) return;
  const filter = (document.getElementById('branchFilter').value || '').toLowerCase();
  const ul = document.getElementById('branchList');
  ul.innerHTML = '';

  for (const b of state.branches) {
    if (filter && !b.name.toLowerCase().includes(filter) && !b.connectionText.toLowerCase().includes(filter))
      continue;

    const li = document.createElement('li');
    li.className = 'branch-item' + (b.name === activeBranchName ? ' active' : '');
    li.onclick = (e) => {
      if (e.target.tagName === 'INPUT') return;
      activeBranchName = b.name;
      renderBranches();
      renderCameras();
    };

    const cb = document.createElement('input');
    cb.type = 'checkbox';
    cb.checked = b.ticked;
    cb.onchange = () => toggleBranchTick(b.name, cb.checked);

    const info = document.createElement('div');
    info.className = 'branch-info';

    const nameDiv = document.createElement('div');
    nameDiv.className = 'branch-name';
    nameDiv.textContent = b.name;

    const metaDiv = document.createElement('div');
    metaDiv.className = 'branch-meta';

    const modeTag = document.createElement('span');
    modeTag.className = 'tag ' + (b.mode === 'Ip' ? 'ip' : 'cloud');
    modeTag.textContent = b.mode === 'Ip' ? `IP: ${b.address}:${b.port}` : `Serial: ${b.cloudId}`;
    metaDiv.appendChild(modeTag);

    const camTag = document.createElement('span');
    camTag.className = 'tag';
    camTag.textContent = b.camerasDetected ? `${b.cameras.length} cam` : 'No cam info';
    metaDiv.appendChild(camTag);

    if (!b.hasPassword) {
      const pwTag = document.createElement('span');
      pwTag.className = 'tag nopw';
      pwTag.textContent = 'No password';
      metaDiv.appendChild(pwTag);
    }

    info.appendChild(nameDiv);
    info.appendChild(metaDiv);
    li.appendChild(cb);
    li.appendChild(info);
    ul.appendChild(li);
  }
}

function renderCameras() {
  const container = document.getElementById('cameraContainer');
  container.innerHTML = '';
  if (!state || !activeBranchName) {
    document.getElementById('lblCamerasHeader').textContent = T('2. Cameras (click a branch)');
    return;
  }
  const branch = state.branches.find(b => b.name === activeBranchName);
  if (!branch) return;

  document.getElementById('lblCamerasHeader').textContent = `2. ${T('Cameras')} — ${branch.name}`;

  if (!branch.camerasDetected) {
    const msg = document.createElement('div');
    msg.className = 'hint';
    msg.style.gridColumn = '1 / -1';
    msg.textContent = T('Cameras not detected yet - press Test connection');
    container.appendChild(msg);
    return;
  }

  for (const c of branch.cameras) {
    const lbl = document.createElement('label');
    lbl.className = 'cam-item';
    const cb = document.createElement('input');
    cb.type = 'checkbox';
    cb.checked = c.ticked;
    cb.onchange = () => toggleCameraTick(branch.name, c.channel, cb.checked);
    const span = document.createElement('span');
    span.textContent = c.folderName;
    lbl.appendChild(cb);
    lbl.appendChild(span);
    container.appendChild(lbl);
  }
}

function renderJobs() {
  const tbody = document.getElementById('jobsBody');
  tbody.innerHTML = '';
  if (!state || !state.jobs || state.jobs.length === 0) {
    const tr = document.createElement('tr');
    const td = document.createElement('td');
    td.colSpan = 10;
    td.style.textAlign = 'center';
    td.style.padding = '24px';
    td.style.color = 'var(--muted)';
    td.textContent = T('The recordings found will be listed here.\nTick the ones you want, then press "Download selected".');
    tr.appendChild(td);
    tbody.appendChild(tr);
    return;
  }

  for (const j of state.jobs) {
    const tr = document.createElement('tr');

    const tdCb = document.createElement('td');
    const cb = document.createElement('input');
    cb.type = 'checkbox';
    cb.checked = j.selected;
    cb.disabled = !j.canChange;
    cb.onchange = () => api('/api/jobs/select', { index: j.index, selected: cb.checked }).then(refresh);
    tdCb.appendChild(cb);

    const tdBranch = document.createElement('td');
    tdBranch.textContent = j.branch;

    const tdCam = document.createElement('td');
    tdCam.textContent = j.camera;

    const tdPeriod = document.createElement('td');
    tdPeriod.textContent = j.period;

    const tdSize = document.createElement('td');
    tdSize.textContent = j.sizeText;

    const tdStatus = document.createElement('td');
    tdStatus.className = 'status-' + j.status;
    tdStatus.textContent = j.statusText;

    const tdPct = document.createElement('td');
    tdPct.innerHTML = `<div class="progress-bar"><div class="progress-fill" style="width:${j.percent}%"></div></div> ${j.percent}%`;

    const tdSpeed = document.createElement('td');
    tdSpeed.textContent = j.speed;

    const tdLeft = document.createElement('td');
    tdLeft.textContent = j.left;

    const tdErr = document.createElement('td');
    tdErr.style.color = 'var(--danger)';
    tdErr.textContent = j.error;

    tr.append(tdCb, tdBranch, tdCam, tdPeriod, tdSize, tdStatus, tdPct, tdSpeed, tdLeft, tdErr);
    tbody.appendChild(tr);
  }
}

async function toggleBranchTick(name, checked) {
  const ticked = state.branches.filter(b => b.name === name ? checked : b.ticked).map(b => b.name);
  await api('/api/branches/select', { names: ticked });
  await refresh();
}

async function tickAllBranches(all) {
  const ticked = all ? state.branches.map(b => b.name) : [];
  await api('/api/branches/select', { names: ticked });
  await refresh();
}

async function toggleCameraTick(branchName, channel, checked) {
  const b = state.branches.find(x => x.name === branchName);
  if (!b) return;
  const channels = b.cameras.filter(c => c.channel === channel ? checked : c.ticked).map(c => c.channel);
  await api('/api/cameras/select', { branch: branchName, channels });
  await refresh();
}

async function tickAllCameras(all) {
  if (!activeBranchName) return;
  const b = state.branches.find(x => x.name === activeBranchName);
  if (!b) return;
  const channels = all ? b.cameras.map(c => c.channel) : [];
  await api('/api/cameras/select', { branch: activeBranchName, channels });
  await refresh();
}

async function updatePeriod() {
  const from = document.getElementById('fromInput').value;
  const to = document.getElementById('toInput').value;
  if (from && to) {
    await api('/api/period', { from, to });
    await refresh();
  }
}

async function setPreset(kind) {
  await api('/api/period/preset', { preset: kind });
  await refresh();
}

async function saveDestination(path) {
  await api('/api/destination', { path });
  await refresh();
}

async function browseDestination() {
  const res = await api('/api/destination/browse', {});
  if (res.picked) {
    await refresh();
  } else if (res.fallbackModal) {
    openFolderModal(state.destination || res.defaultDir);
  }
}

async function openDestinationFolder() {
  await api('/api/destination/open', {});
}

async function changeLanguage(lang) {
  await api('/api/language', { language: lang });
  await refresh();
}

async function resetDriver() {
  await api('/api/driver/reset', {});
  await refresh();
}

function openBranchModal(branch) {
  editingBranchOrigName = branch ? branch.name : null;
  document.getElementById('branchModalError').style.display = 'none';
  document.getElementById('branchModalTitle').textContent = branch ? T('Edit branch') : T('Add branch');
  document.getElementById('bmName').value = branch ? branch.name : '';
  document.getElementById('bmArea').value = branch ? branch.area : '';
  const mode = branch ? branch.mode : 'Cloud';
  document.querySelectorAll('input[name="bmMode"]').forEach(r => r.checked = (r.value === mode));
  document.getElementById('bmSerial').value = branch ? branch.cloudId : '';
  document.getElementById('bmAddress').value = branch ? branch.address : '';
  document.getElementById('bmPort').value = branch ? branch.port : 34567;
  document.getElementById('bmUser').value = branch ? branch.username : 'admin';
  document.getElementById('bmPass').value = '';
  document.getElementById('bmCameras').value = branch && branch.camerasDetected ? branch.cameras.length : 0;
  document.getElementById('bmPassHint').textContent = branch && branch.hasPassword
    ? T('Leave the password empty to keep the saved one.')
    : T('No password is saved for this branch. Enter it.');
  toggleBranchMode();
  document.getElementById('branchModal').classList.add('open');
}

function toggleBranchMode() {
  const mode = document.querySelector('input[name="bmMode"]:checked').value;
  document.getElementById('bmCloudGroup').style.display = mode === 'Cloud' ? 'flex' : 'none';
  document.getElementById('bmIpGroup').style.display = mode === 'Ip' ? 'flex' : 'none';
}

function editSelectedBranch() {
  if (!activeBranchName) return alert('Select a branch first.');
  const b = state.branches.find(x => x.name === activeBranchName);
  if (b) openBranchModal(b);
}

async function saveBranchForm() {
  const mode = document.querySelector('input[name="bmMode"]:checked').value;
  const payload = {
    editingName: editingBranchOrigName,
    name: document.getElementById('bmName').value,
    area: document.getElementById('bmArea').value,
    mode,
    serialId: document.getElementById('bmSerial').value,
    address: document.getElementById('bmAddress').value,
    port: parseInt(document.getElementById('bmPort').value || '34567', 10),
    username: document.getElementById('bmUser').value,
    password: document.getElementById('bmPass').value,
    cameraCount: parseInt(document.getElementById('bmCameras').value || '0', 10)
  };
  const res = await api('/api/branches/save', payload);
  if (!res.ok) {
    const errEl = document.getElementById('branchModalError');
    errEl.textContent = (res.errors || []).join('\n');
    errEl.style.display = 'block';
    return;
  }
  activeBranchName = payload.name.trim();
  closeModal('branchModal');
  await refresh();
}

async function removeSelectedBranch() {
  if (!activeBranchName) return;
  if (!confirm(`Remove "${activeBranchName}"?`)) return;
  await api('/api/branches/delete', { name: activeBranchName });
  activeBranchName = null;
  await refresh();
}

async function testSelectedBranch() {
  if (!activeBranchName) return alert('Select a branch first.');
  const btn = document.getElementById('btnTestConn');
  btn.disabled = true;
  try {
    const res = await api('/api/branches/test', { name: activeBranchName });
    await refresh();
    alert(res.message);
  } finally {
    btn.disabled = false;
  }
}

async function startFind() {
  const res = await api('/api/find', {});
  if (!res.ok) alert((res.errors || []).join('\n'));
  await refresh();
}

async function startDownload() {
  const res = await api('/api/download', {});
  if (!res.ok) alert((res.errors || []).join('\n'));
  await refresh();
}

async function cancelOp() {
  await api('/api/cancel', {});
  await refresh();
}

async function tickAllJobs(selected) {
  await api('/api/jobs/select-all', { selected });
  await refresh();
}

function closeModal(id) {
  document.getElementById(id).classList.remove('open');
}

// Folder browser modal
async function openFolderModal(initialPath) {
  document.getElementById('folderModal').classList.add('open');
  await loadDir(initialPath || '');
}

async function loadDir(path) {
  const res = await api('/api/fs/list', { path });
  document.getElementById('fmCurrentPath').value = res.currentPath || '';
  const rootsDiv = document.getElementById('folderRoots');
  rootsDiv.innerHTML = '';
  for (const r of (res.roots || [])) {
    const b = document.createElement('button');
    b.style.fontSize = '11px';
    b.style.padding = '2px 6px';
    b.textContent = r.label;
    b.onclick = () => loadDir(r.path);
    rootsDiv.appendChild(b);
  }

  const listDiv = document.getElementById('fmList');
  listDiv.innerHTML = '';
  if (res.parentPath) {
    const up = document.createElement('div');
    up.className = 'branch-item';
    up.textContent = '📁 .. (Parent directory)';
    up.onclick = () => loadDir(res.parentPath);
    listDiv.appendChild(up);
  }
  for (const d of (res.directories || [])) {
    const item = document.createElement('div');
    item.className = 'branch-item';
    item.textContent = '📁 ' + d.name;
    item.onclick = () => loadDir(d.fullPath);
    listDiv.appendChild(item);
  }
}

async function createSubfolder() {
  const name = prompt('New folder name:');
  if (!name) return;
  const parent = document.getElementById('fmCurrentPath').value;
  const res = await api('/api/fs/mkdir', { parent, name });
  if (res.ok) await loadDir(res.path);
  else alert(res.error || 'Could not create folder');
}

async function selectModalFolder() {
  const path = document.getElementById('fmCurrentPath').value;
  closeModal('folderModal');
  await saveDestination(path);
}

// CMS XML import modal
function openCmsModal() {
  loadedCmsXml = "";
  document.getElementById('cmsFileInput').value = '';
  document.getElementById('cmsSharedPass').value = '';
  document.getElementById('cmsPreviewBox').style.display = 'none';
  document.getElementById('cmsError').style.display = 'none';
  document.getElementById('btnConfirmCms').disabled = true;
  document.getElementById('cmsModal').classList.add('open');
}

async function previewCmsFile(input) {
  if (!input.files || input.files.length === 0) return;
  loadedCmsXml = await input.files[0].text();
  const res = await api('/api/cms/preview', { xml: loadedCmsXml });
  const errEl = document.getElementById('cmsError');
  const box = document.getElementById('cmsPreviewBox');
  if (!res.ok) {
    errEl.textContent = res.error;
    errEl.style.display = 'block';
    box.style.display = 'none';
    document.getElementById('btnConfirmCms').disabled = true;
    return;
  }
  errEl.style.display = 'none';
  box.style.display = 'block';
  box.innerHTML = '';

  for (const c of res.candidates) {
    const row = document.createElement('label');
    row.style.display = 'flex';
    row.style.gap = '8px';
    row.style.padding = '4px 0';
    row.innerHTML = `<input type="checkbox" class="cms-cand" value="${c.name}" ${c.defaultChecked ? 'checked' : ''} />
      <span><strong>${c.name}</strong> (${c.connection}) ${c.note ? ' — <em>' + c.note + '</em>' : ''}</span>`;
    box.appendChild(row);
  }
  document.getElementById('btnConfirmCms').disabled = res.candidates.length === 0;
}

async function confirmCmsImport() {
  const selected = Array.from(document.querySelectorAll('.cms-cand:checked')).map(cb => cb.value);
  const password = document.getElementById('cmsSharedPass').value;
  const res = await api('/api/cms/import', { xml: loadedCmsXml, selectedNames: selected, password });
  if (res.ok) {
    closeModal('cmsModal');
    await refresh();
  } else {
    alert(res.error || 'Import failed');
  }
}

function openSdkModal() {
  document.getElementById('sdkPathInput').value = state ? state.sdkDirectory : '';
  document.getElementById('sdkModal').classList.add('open');
}

async function saveSdkFolder() {
  const path = document.getElementById('sdkPathInput').value;
  await api('/api/sdk-folder', { path });
  closeModal('sdkModal');
  await refresh();
}

setInterval(refresh, 1000);
refresh();
</script>
</body>
</html>
""";
}
