'use strict';

// Same-origin, dependency-free portal. Escaped text and validated asset paths
// keep developer-controlled metadata out of executable HTML/URLs.
const app = document.getElementById('app');
const state = {
  page: 'software', authMode: 'login', token: sessionStorage.getItem('gc_token') || '',
  refreshToken: sessionStorage.getItem('gc_refresh') || '',
  email: sessionStorage.getItem('gc_email') || '',
  software: [], selected: null, creating: false, flash: '', error: false, busy: false
};
const nav = [
  ['overview','Overview'], ['software','My software'], ['releases','Releases'],
  ['media','Media library'], ['account','Account']
];
function esc(value) {
  return String(value == null ? '' : value).replace(/[&<>"']/g, c =>
    ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
}
function safeAsset(url) {
  return /^\/api\/assets\/[a-f0-9-]{36}$/i.test(url || '') ? url : '/assets/software.svg';
}
function notice(message, error) {
  state.flash = message;
  state.error = !!error;
  render();
}
function message() {
  return state.flash ? '<div role="status" class="flash '+(state.error?'error':'')+'">'+esc(state.flash)+'</div>' : '';
}
async function api(path, options, retry) {
  options = options || {};
  const headers = new Headers(options.headers || {});
  if (state.token) headers.set('Authorization','Bearer '+state.token);
  if (!(options.body instanceof FormData) && options.body && !headers.has('Content-Type'))
    headers.set('Content-Type','application/json');
  const response = await fetch(path, Object.assign({}, options, {headers, cache:'no-store'}));
  if (response.status === 401 && !retry && state.refreshToken) {
    const refreshed = await fetch('/account/refresh', {
      method:'POST',headers:{'Content-Type':'application/json'},
      body:JSON.stringify({refreshToken:state.refreshToken})
    });
    if (refreshed.ok) {
      storeTokens(await refreshed.json());
      return api(path, options, true);
    }
  }
  if (!response.ok) {
    if (response.status === 401) {
      signOut(false);
      throw Error('Session expired. Sign in again.');
    }
    const text = (await response.text()).slice(0,450);
    throw Error(text || 'Request failed ('+response.status+')');
  }
  return response.status === 204 ? null : response.json();
}
function storeTokens(tokens) {
  state.token = tokens.accessToken || '';
  state.refreshToken = tokens.refreshToken || '';
  sessionStorage.setItem('gc_token',state.token);
  sessionStorage.setItem('gc_refresh',state.refreshToken);
}
function signOut(redraw) {
  state.token = ''; state.refreshToken = ''; state.email = '';
  state.software = []; state.selected = null;
  sessionStorage.removeItem('gc_token');
  sessionStorage.removeItem('gc_refresh');
  sessionStorage.removeItem('gc_email');
  if (redraw !== false) render();
}
async function loadSoftware() {
  state.software = await api('/api/developer/software');
}
async function openSoftware(id) {
  state.selected = await api('/api/developer/software/'+encodeURIComponent(id));
  state.creating = false;
  state.page = 'detail';
  state.flash = '';
  render();
}
function header() {
  return '<header class="topbar"><a href="/" class="brand">GAMECUBE / DEVELOPER PORTAL</a>'
    +'<span class="account-name">'+esc(state.email)+'</span></header>';
}
function shell(content) {
  return header()+'<div class="layout"><aside class="side"><nav class="nav" aria-label="Main navigation">'
    +nav.map(function(n){return '<button data-nav="'+n[0]+'" class="'+
      (state.page===n[0]||(state.page==='detail'&&n[0]==='software')?'active':'')+
      '">'+esc(n[1])+'</button>';}).join('')
    +'</nav></aside><main class="content" id="main-content">'+message()+content+'</main></div>';
}
function titleBar(title, subtitle, button) {
  return '<div class="heading"><div><h1>'+esc(title)+'</h1><p class="subtitle">'+esc(subtitle)+
    '</p></div>'+(button||'')+'</div>';
}
function statusBadge(item) {
  return '<span class="status '+(item.isPublished?'published':'draft')+'">'+
    (item.isPublished?'Published':'Draft')+'</span>';
}
function softwareCard(item) {
  const url = item.coverUrl || '/assets/software.svg';
  return '<article class="software-card"><img class="cover" alt="" src="'+safeAsset(url)+'">'
    +'<div><h2 class="software-title">'+esc(item.title)+'</h2>'
    +'<p class="metadata">Latest release&nbsp; '+esc(item.latestVersion||'None yet')+'</p>'
    +'<div class="rating">'+(item.ratingCount ? Number(item.averageRating).toFixed(1)+' / '+item.ratingCount+' ratings' : '— / no ratings')+'</div></div>'
    +'<div class="card-actions">'+statusBadge(item)+'<button class="secondary" data-manage="'+esc(item.id)+'">Manage releases</button></div></article>';
}
function listPage() {
  return titleBar('My software','Manage releases, uploads, screenshots and ratings.',
    '<button class="primary" data-action="new">+ Add software</button>')
    +(state.software.length ? '<div class="stack">'+state.software.map(softwareCard).join('')+'</div>'
      : '<div class="empty">You have not submitted any software yet. Select Add software to create a draft.</div>');
}
function overviewPage() {
  const published = state.software.filter(x=>x.isPublished).length;
  const releases = state.software.filter(x=>x.latestVersion).length;
  return titleBar('Overview','Your developer account at a glance.')
    +'<section class="overview"><div class="panel"><h3>Software titles</h3><div class="stat">'+state.software.length+'</div><span class="muted">Total titles</span></div>'
    +'<div class="panel"><h3>Published</h3><div class="stat">'+published+'</div><span class="muted">Visible in the GameCube store</span></div>'
    +'<div class="panel"><h3>With releases</h3><div class="stat">'+releases+'</div><span class="muted">Titles with uploaded builds</span></div></section>'
    +'<div class="panel"><h2>Recent software</h2>'+(state.software.length?state.software.slice(0,5)
      .map(x=>'<p><button class="secondary small" data-manage="'+esc(x.id)+'">'+esc(x.title)+'</button> '+esc(x.latestVersion||'Draft')+'</p>').join('')
      : '<p class="muted">No titles submitted yet.</p>')+'</div>';
}
function editForm(item, isNew) {
  return '<form data-form="'+(isNew?'create':'update')+'" class="panel">'
    +'<h2>'+ (isNew?'New software':'Software information')+'</h2>'
    +'<div class="field"><label for="title">Software title</label><input id="title" name="title" maxlength="120" required value="'+esc(item.title||'')+'"></div>'
    +'<div class="grid-two"><div class="field"><label for="category">Category</label><select id="category" name="category">'
    +['Utility','Game','Emulator','Media','Tool','Other'].map(c=>'<option '+(item.category===c?'selected':'')+'>'+c+'</option>').join('')+'</select></div>'
    +'<div class="field"><label for="publish">Visibility</label><input id="publish" disabled value="'+(isNew?'Draft':item.isPublished?'Published':'Draft')+'"></div></div>'
    +'<div class="field"><label for="description">Description</label><textarea id="description" name="description" maxlength="4000" required>'+esc(item.description||'')+'</textarea></div>'
    +'<div class="actions"><button class="primary" type="submit">'+(isNew?'Create draft':'Save changes')+'</button>'
    +'<button class="secondary" type="button" data-nav="software">Cancel</button></div></form>';
}
function releaseTable(releases) {
  return releases.length ? '<div class="table-wrap"><table><thead><tr><th>Version</th><th>Uploaded</th><th>File</th><th></th></tr></thead><tbody>'
    +releases.map(r=>'<tr><td><strong>'+esc(r.version)+'</strong><div class="muted">'+esc(r.releaseNotes||'')+'</div></td>'
      +'<td>'+esc(new Date(r.uploadedAtUtc).toLocaleDateString())+'</td>'
      +'<td><a href="'+safeAsset(r.downloadUrl)+'">Download</a> ('+Math.round(r.sizeBytes/1024)+' KB)</td>'
      +'<td><button class="secondary small danger" data-remove-release="'+esc(r.id)+'">Remove</button></td></tr>').join('')
    +'</tbody></table></div>' : '<p class="muted">No releases uploaded yet.</p>';
}
function detailsPage() {
  if (state.creating) return titleBar('Add software','Create a draft first, then upload versions and screenshots.')
    +editForm({title:'',description:'',category:'Utility'},true);
  const s = state.selected;
  if (!s) return listPage();
  const detail = s.detail || {screenshots:[],releases:[]};
  const shots = s.screenshotFiles || [];
  return titleBar(s.title,'Manage your software and its published metadata.',
    '<button class="secondary" data-nav="software">Back to software</button>')
    +editForm(s,false)
    +'<section class="panel"><h2>Visibility</h2><p class="muted">Only published titles appear in the GameCube catalog.</p>'
    +'<div class="actions">'+statusBadge(s)
    +'<button class="'+(s.isPublished?'secondary':'primary')+'" data-action="'+(s.isPublished?'unpublish':'publish')+'">'
    +(s.isPublished?'Unpublish':'Publish')+'</button></div></section>'
    +'<section class="panel"><h2>Releases</h2>'+releaseTable(detail.releases||[])
    +'<h3>Upload new version</h3>'
    +'<form data-form="release" enctype="multipart/form-data">'
    +'<div class="grid-two"><div class="field"><label for="version">Version</label><input id="version" name="version" placeholder="1.0.0" maxlength="40" required pattern="[a-zA-Z0-9._+\\-]+"></div>'
    +'<div class="field"><label for="binary">Software package (.dol or .zip, max 64 MB)</label><input id="binary" type="file" name="file" accept=".dol,.zip" required></div></div>'
    +'<div class="field"><label for="releaseNotes">Release notes</label><textarea id="releaseNotes" name="releaseNotes" maxlength="2000"></textarea></div>'
    +'<button class="primary" type="submit">Upload release</button></form></section>'
    +'<section class="panel"><h2>Screenshots</h2><p class="muted">Up to 8 PNG screenshots. Each image must be 1024×1024 or smaller and no more than 256 KB.</p>'
    +(shots.length ? '<div class="media-list">'+shots.map(x=>'<div class="media-tile"><img alt="Screenshot" src="'+safeAsset(x.url)+'">'
       +'<button class="secondary small danger" data-remove-shot="'+esc(x.id)+'">Remove screenshot</button></div>').join('')+'</div>'
      : '<p class="muted">No screenshots yet.</p>')
    +'<form data-form="screenshot" enctype="multipart/form-data"><div class="field"><label for="shot">Add PNG screenshot</label>'
    +'<input id="shot" type="file" name="file" accept="image/png,.png" required></div>'
    +'<button class="primary" type="submit">Upload screenshot</button></form></section>'
    +'<p class="footer-note">Last updated: '+esc(new Date(s.lastUpdatedAtUtc).toLocaleString())+'</p>';
}
function summaryPage(type) {
  const isRelease = type==='releases';
  return titleBar(isRelease?'Releases':'Media library',
    isRelease?'Manage all software versions.':'Manage screenshots for your software.')
    +(state.software.length ? '<div class="stack">'+state.software.map(x=>'<section class="panel"><h2>'+esc(x.title)+'</h2>'
      +'<p class="metadata">'+(isRelease?'Latest: '+esc(x.latestVersion||'None'):'Screenshots: '+(x.screenshotCount||0))+'</p>'
      +'<button class="secondary" data-manage="'+esc(x.id)+'">Manage '+(isRelease?'releases':'screenshots')+'</button></section>').join('')+'</div>'
      : '<div class="empty">No software yet.</div>');
}
function accountPage() {
  return titleBar('Account','Signed-in developer details and session control.')
    +'<div class="panel"><h2>'+esc(state.email)+'</h2>'
    +'<p class="muted">Your account manages the titles and releases you submit. Your uploads are private until published.</p>'
    +'<button class="secondary danger" data-action="logout">Sign out</button></div>';
}
function authPage() {
  const register = state.authMode==='register';
  return header()+'<main class="auth-screen"><div class="auth-panel"><h1>'+ (register?'Create developer account':'Developer sign in')+'</h1>'
    +'<p>Manage your software, screenshots, release history and ratings.</p>'
    +message()
    +'<form data-form="auth"><div class="field"><label for="email">Email</label>'
    +'<input type="email" id="email" name="email" autocomplete="username" required></div>'
    +'<div class="field"><label for="password">Password</label>'
    +'<input type="password" id="password" name="password" minlength="12" autocomplete="'+(register?'new-password':'current-password')+'" required></div>'
    +'<button class="primary" type="submit">'+(register?'Create account':'Sign in')+'</button></form>'
    +'<button class="auth-switch" data-action="switch-auth">'+(register?'Already registered? Sign in':'New developer? Create an account')+'</button>'
    +'</div></main>';
}
function render() {
  if (!state.token) { app.innerHTML=authPage(); return; }
  const content = state.page==='overview' ? overviewPage()
    : state.page==='software' ? listPage()
    : state.page==='detail' ? detailsPage()
    : state.page==='releases'||state.page==='media' ? summaryPage(state.page)
    : accountPage();
  app.innerHTML=shell(content);
}
async function refresh() {
  await loadSoftware();
  if (state.selected && !state.creating) {
    const id = state.selected.id;
    state.selected = await api('/api/developer/software/'+encodeURIComponent(id));
  }
  render();
}
function setBusy(b) {
  state.busy=b;
  app.querySelectorAll('button').forEach(x=>x.disabled=b);
}
async function action(name,el) {
  if (state.busy) return;
  if (name==='switch-auth') {state.flash='';state.authMode=state.authMode==='login'?'register':'login';render();return;}
  if (name==='logout') {signOut();return;}
  if (name==='new') {state.creating=true;state.selected=null;state.page='detail';state.flash='';render();return;}
  if (name==='publish'||name==='unpublish') {
    await api('/api/developer/software/'+state.selected.id+'/'+name,{method:'POST'});
    await refresh(); notice(name==='publish'?'Software is now published.':'Software is now a draft.');
  }
}
document.addEventListener('click',async ev=>{
  const el=ev.target.closest('button[data-action],button[data-nav],button[data-manage],button[data-remove-release],button[data-remove-shot]');
  if (!el) return;
  try {
    const dest=el.dataset.nav, manage=el.dataset.manage;
    if (dest) { state.page=dest; state.creating=false;state.selected=null;state.flash='';render();return; }
    if (manage) {await openSoftware(manage);return;}
    if (el.dataset.action) {await action(el.dataset.action,el);return;}
    if (!state.selected || state.busy) return;
    const releaseId=el.dataset.removeRelease,shotId=el.dataset.removeShot;
    if (releaseId || shotId) {
      if (!confirm('Permanently remove this '+(releaseId?'release':'screenshot')+'?')) return;
      setBusy(true);
      await api('/api/developer/software/'+state.selected.id+'/'+(releaseId?'releases/'+releaseId:'screenshots/'+shotId),{method:'DELETE'});
      await refresh();notice('Asset removed.');
    }
  } catch(ex) {notice(ex.message,true);}
  finally {setBusy(false);}
});
document.addEventListener('submit',async ev=>{
  const form=ev.target.closest('form[data-form]');
  if(!form)return;
  ev.preventDefault();
  if(state.busy)return;
  const kind=form.dataset.form;
  const data=new FormData(form);
  setBusy(true);
  try {
    if(kind==='auth'){
      const email=String(data.get('email')||'');
      const password=String(data.get('password')||'');
      if(state.authMode==='register') {
        await api('/account/register',{method:'POST',body:JSON.stringify({email,password})});
      }
      const auth=await api('/account/login?useCookies=false',{method:'POST',body:JSON.stringify({email,password})});
      storeTokens(auth);state.email=email;sessionStorage.setItem('gc_email',email);
      state.authMode='login';state.flash='';state.page='software';
      await refresh();
      return;
    }
    if(kind==='create'||kind==='update') {
      const body=JSON.stringify({title:data.get('title'),description:data.get('description'),category:data.get('category')});
      if(kind==='create'){
        const result=await api('/api/developer/software',{method:'POST',body});
        await loadSoftware();await openSoftware(result.id);
        notice('Software draft created. Upload a release to publish.');
      } else {
        await api('/api/developer/software/'+state.selected.id,{method:'PUT',body});
        await refresh();notice('Software updated.');
      }
      return;
    }
    if(kind==='release'||kind==='screenshot'){
      const file=data.get('file');
      if(!(file instanceof File)||!file.size)throw Error('Select a file.');
      const max=kind==='release'?64*1024*1024:256*1024;
      if(file.size>max)throw Error('File exceeds upload limit.');
      await api('/api/developer/software/'+state.selected.id+'/'+(kind==='release'?'releases':'screenshots'),
        {method:'POST',body:data});
      await refresh();notice(kind==='release'?'Release uploaded.':'Screenshot uploaded.');
    }
  } catch(ex) {notice(ex.message,true);}
  finally {setBusy(false);}
});
(async function start(){
  if(state.token) {
    try {await loadSoftware();}
    catch(e) {state.flash=e.message;state.error=true;}
  }
  render();
})();