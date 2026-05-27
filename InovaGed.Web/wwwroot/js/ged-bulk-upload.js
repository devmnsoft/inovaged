(function(){
  const state={files:[],uploading:false};
  function initBulkUpload(){
    const modal=document.getElementById('bulkUploadModal'); if(!modal) return;
    document.getElementById('btnOpenBulkUpload')?.addEventListener('click',openBulkUploadModal);
    const dz=document.getElementById('bulkDropzone'); const fi=document.getElementById('bulkFileInput');
    dz?.addEventListener('click',()=>fi?.click()); fi?.addEventListener('change',handleFileSelect);
    dz?.addEventListener('dragover',e=>{e.preventDefault();dz.classList.add('drag-over');});
    dz?.addEventListener('dragleave',()=>dz.classList.remove('drag-over'));
    dz?.addEventListener('drop',handleDrop);
    document.getElementById('btnBulkClear')?.addEventListener('click',clearFiles);
    document.getElementById('btnBulkSend')?.addEventListener('click',()=>uploadFiles().catch(()=>{}));
  }
  function openBulkUploadModal(){ bootstrap.Modal.getOrCreateInstance('#bulkUploadModal').show(); }
  function handleDrop(e){e.preventDefault();e.currentTarget.classList.remove('drag-over');addFiles(e.dataTransfer.files);} 
  function handleFileSelect(e){addFiles(e.target.files);e.target.value='';}
  function addFiles(files){for(const f of Array.from(files||[])){ if(state.files.some(x=>x.name===f.name&&x.size===f.size)){showBulkUploadMessage('Arquivo duplicado na lista.','warning');continue;} state.files.push({file:f,status:'Aguardando',progress:0,error:null});} renderFileList();}
  function renderFileList(){ const tb=document.getElementById('bulkFileList'); if(!tb) return; tb.innerHTML=state.files.map((x,i)=>`<tr><td>${escapeHtml(x.file.name)}</td><td>${formatFileSize(x.file.size)}</td><td>${x.error?'<span class="text-danger">Erro</span>':x.status}</td><td><div class="progress"><div class="progress-bar" style="width:${x.progress}%">${x.progress}%</div></div></td><td><button class="btn btn-sm btn-outline-danger" data-i="${i}">Remover</button></td></tr>`).join('')||'<tr><td colspan="5" class="text-muted">Nenhum arquivo selecionado.</td></tr>'; tb.querySelectorAll('button[data-i]').forEach(b=>b.onclick=()=>removeFile(parseInt(b.dataset.i))); }
  function removeFile(i){state.files.splice(i,1);renderFileList();}
  function clearFiles(){state.files=[];renderFileList();}
  function validateFiles(){if(!document.getElementById('bulkFolderId')?.value){showBulkUploadMessage('Selecione uma pasta destino.','warning');return false;} if(state.files.length===0){showBulkUploadMessage('Adicione pelo menos um arquivo.','warning');return false;} return true;}
  async function uploadFiles(){ if(state.uploading||!validateFiles()) return; state.uploading=true; const folderId=document.getElementById('bulkFolderId').value; const batchId=crypto.randomUUID(); let ok=0,err=0; for(let i=0;i<state.files.length;i++){const r=await uploadSingleFile(state.files[i],folderId,batchId,i); if(r)ok++; else err++;} showAppToast(`${ok} documento(s) enviado(s) com sucesso, ${err} falharam.`, err?'warning':'success', 'Upload em lote'); state.uploading=false; }
  function uploadSingleFile(item,folderId,batchId,index){ return new Promise((resolve)=>{ item.status='Enviando'; renderFileList(); const xhr=new XMLHttpRequest(); xhr.open('POST','/Ged/Documents/BulkUploadSingle'); xhr.upload.onprogress=(e)=>{ if(e.lengthComputable) updateFileProgress(index,Math.round((e.loaded/e.total)*100)); }; xhr.onload=()=>{ try{const j=JSON.parse(xhr.responseText||'{}'); if(xhr.status>=200&&xhr.status<300&&j.success){ item.status='Enviado'; item.progress=100; resolve(true);} else { item.error=j.message||'Falha no envio'; item.status='Erro'; resolve(false);} }catch{ item.status='Erro'; item.error='Resposta inválida'; resolve(false);} renderFileList();}; xhr.onerror=()=>{item.status='Erro';item.error='Erro de rede';renderFileList();resolve(false);}; const fd=new FormData(); fd.append('file',item.file); fd.append('folderId',folderId); fd.append('batchId',batchId); const token=document.querySelector('input[name="__RequestVerificationToken"]')?.value; if(token) fd.append('__RequestVerificationToken',token); xhr.send(fd); }); }
  function updateFileProgress(i,p){state.files[i].progress=p;renderFileList();}
  function showBulkUploadMessage(message,type){const el=document.getElementById('bulkUploadMessage'); if(!el) return; el.className=`alert alert-${type}`; el.textContent=message; el.classList.remove('d-none');}
  function showAppToast(message,type,title){ if(window.showToast){window.showToast(message,type,title);return;} console.log(title||'Aviso',message); }
  function formatFileSize(bytes){ if(bytes<1024) return `${bytes} B`; if(bytes<1048576) return `${(bytes/1024).toFixed(1)} KB`; return `${(bytes/1048576).toFixed(1)} MB`;}
  function escapeHtml(v){return (v||'').replace(/[&<>'"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','\'':'&#39;','"':'&quot;'}[c]));}
  document.addEventListener('DOMContentLoaded',initBulkUpload);
})();
