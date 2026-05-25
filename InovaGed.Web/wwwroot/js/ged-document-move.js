(function(){
  const table = document.querySelector('table[data-ged-document-list]');
  if(!table) return;
  const selectAll = document.getElementById('selectAllDocuments');
  const bulkBtn = document.getElementById('btnMoveSelected');
  const modalEl = document.getElementById('moveDocumentsModal');
  const modal = modalEl ? new bootstrap.Modal(modalEl) : null;
  const summary = document.getElementById('moveSelectionSummary');
  const folderInput = document.getElementById('destinationFolderSearch');
  const folderIdInput = document.getElementById('destinationFolderId');
  const reasonInput = document.getElementById('moveReason');
  const suggestions = document.getElementById('folderSuggestions');
  const confirmBtn = document.getElementById('btnConfirmMove');
  let selectedIds = [];

  const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
  const picks = () => Array.from(document.querySelectorAll('.js-doc-select:checked')).map(x=>x.value);
  const update = ()=>{ selectedIds = picks(); bulkBtn.disabled = selectedIds.length===0; bulkBtn.textContent = `Mover selecionados (${selectedIds.length})`; };
  table.addEventListener('change', e=>{ if(e.target.classList.contains('js-doc-select')) update(); });
  selectAll?.addEventListener('change', ()=>{ document.querySelectorAll('.js-doc-select').forEach(c=> c.checked = selectAll.checked); update(); });

  function openMove(ids){ selectedIds = ids; summary.textContent = `${ids.length} documento(s) selecionado(s)`; folderInput.value=''; folderIdInput.value=''; reasonInput.value=''; suggestions.innerHTML=''; modal.show(); }
  bulkBtn?.addEventListener('click', ()=> openMove(picks()));
  document.querySelectorAll('.js-move-one').forEach(b=> b.addEventListener('click', ()=> openMove([b.dataset.documentId])));

  folderInput?.addEventListener('input', async ()=>{
    const term = folderInput.value.trim();
    if(term.length < 2){ suggestions.innerHTML=''; return; }
    const r = await fetch(`/Ged/Folders/Search?term=${encodeURIComponent(term)}`);
    const data = await r.json();
    suggestions.innerHTML = data.map(f=> `<button type="button" class="list-group-item list-group-item-action" data-id="${f.id}">${f.fullPath||f.name}</button>`).join('');
  });
  suggestions?.addEventListener('click', e=>{ const btn = e.target.closest('[data-id]'); if(!btn) return; folderIdInput.value = btn.dataset.id; folderInput.value = btn.textContent.trim(); suggestions.innerHTML=''; });

  async function post(url, payload){
    const r = await fetch(url,{method:'POST',headers:{'Content-Type':'application/json','RequestVerificationToken':token},body:JSON.stringify(payload)});
    return await r.json();
  }
  confirmBtn?.addEventListener('click', async ()=>{
    if(!folderIdInput.value || !selectedIds.length){ alert('Selecione documentos e pasta destino.'); return; }
    const reason = reasonInput.value || null;
    if(selectedIds.length===1){
      const res = await post('/Ged/Documents/Move',{documentId:selectedIds[0],destinationFolderId:folderIdInput.value,reason,source:'SINGLE'});
      alert(res?.value?.success===false ? (res?.value?.message||'Falha ao mover.') : 'Documento movido com sucesso.');
    } else {
      const res = await post('/Ged/Documents/MoveBulk',{documentIds:selectedIds,destinationFolderId:folderIdInput.value,reason,source:'BULK'});
      const value = res?.value || res;
      alert(`Movidos: ${value.successCount||0}. Falhas: ${value.failCount||0}.`);
    }
    selectedIds.forEach(id=> document.querySelector(`tr[data-document-id="${id}"]`)?.remove());
    modal.hide();
    update();
  });
})();
