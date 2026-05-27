(function () {
console.log('[BulkUpload] script carregado');
const DuplicateStrategy={overwrite:'overwrite',rename:'rename',skip:'skip',cancel:'cancel'};
const state={files:[],uploading:false,batchId:null,duplicateStrategy:null};

function initBulkUpload(){
 const dz=document.getElementById('bulkDropzone'),fi=document.getElementById('bulkFileInput');
 if(!dz||!fi) return;
 document.getElementById('btnOpenBulkUpload')?.addEventListener('click',openBulkUploadModal);
 dz.addEventListener('click',()=>fi.click()); fi.addEventListener('change',e=>{addFiles(e.target.files);e.target.value='';});
 dz.addEventListener('dragover',e=>{e.preventDefault();dz.classList.add('drag-over');}); dz.addEventListener('dragleave',()=>dz.classList.remove('drag-over'));
 dz.addEventListener('drop',e=>{e.preventDefault();dz.classList.remove('drag-over');addFiles(e.dataTransfer.files);});
 document.getElementById('btnBulkClear')?.addEventListener('click',clearSelectedFiles);
 document.getElementById('btnBulkRetryFailed')?.addEventListener('click',retryFailedFiles);
 ['btnDupOverwrite','btnDupRename','btnDupSkip','btnDupCancel'].forEach(id=>document.getElementById(id)?.addEventListener('click',()=>applyDuplicateStrategy({btnDupOverwrite:'overwrite',btnDupRename:'rename',btnDupSkip:'skip',btnDupCancel:'cancel'}[id])));
 const btn=document.getElementById('btnBulkUploadSubmit');
 console.log('[BulkUpload] botão enviar encontrado:',!!btn);
}

document.addEventListener('click',async function(e){
 const btn=e.target.closest('#btnBulkUploadSubmit'); if(!btn) return;
 e.preventDefault(); e.stopPropagation();
 console.log('[BulkUpload] clique em Enviar documentos');
 await uploadFiles();
});

function getCurrentFolderId(){
 const modal=document.getElementById('bulkUploadModal');
 const fromModal=modal?.getAttribute('data-folder-id'); if(fromModal) return fromModal;
 const hidden=document.getElementById('bulkFolderId'); if(hidden?.value) return hidden.value;
 const selected=document.querySelector('.js-folder-node.active, .ged-tree-row.active, [data-folder-selected="true"]');
 const id=selected?.closest('[data-folder-id]')?.getAttribute('data-folder-id')||selected?.getAttribute('data-folder-id');
 if(id) return id;
 return new URL(window.location.href).searchParams.get('folderId');
}

const openBulkUploadModal=()=>bootstrap.Modal.getOrCreateInstance('#bulkUploadModal').show();
const closeBulkUploadModal=()=>bootstrap.Modal.getOrCreateInstance('#bulkUploadModal').hide();
function resetBulkUploadState(){state.files=[];state.batchId=null;state.duplicateStrategy=null;const fi=document.getElementById('bulkFileInput');if(fi)fi.value='';renderFileList();updateUploadSummary();hideDuplicateDecision();clearBulkUploadMessage();document.getElementById('btnBulkRetryFailed')?.classList.add('d-none');}
function clearSelectedFiles(){resetBulkUploadState();}
function addFiles(files){for(const f of Array.from(files||[])){if(state.files.some(x=>x.originalName===f.name&&x.size===f.size))continue;state.files.push({id:crypto.randomUUID(),file:f,originalName:f.name,uploadName:f.name,size:f.size,extension:(f.name.split('.').pop()||'').toLowerCase(),status:'waiting',progress:0,errorMessage:null,errorLog:null,duplicateStrategy:null,existingDocumentId:null,serverDocumentId:null,serverVersionId:null});}renderFileList();updateUploadSummary();}
function renderFileList(){const tb=document.getElementById('bulkFileList'); if(!tb)return; tb.innerHTML=state.files.map((x,i)=>`<tr><td>${escapeHtml(x.originalName)}<div class='small text-muted'>${escapeHtml(x.uploadName)}</div>${x.errorLog?`<button class='btn btn-link btn-sm p-0' data-details='${i}'>Ver detalhes</button>`:''}</td><td>${formatFileSize(x.size)}</td><td><span class='badge bg-${statusColor(x.status)}'>${statusLabel(x.status)}</span><div class='small text-muted'>${escapeHtml(x.errorMessage||'')}</div></td><td><div class='progress'><div class='progress-bar' style='width:${x.progress}%'>${x.progress}%</div></div></td><td><button class='btn btn-sm btn-outline-danger' data-i='${i}' ${state.uploading?'disabled':''}>Remover</button></td></tr>`).join('')||'<tr><td colspan="5" class="text-muted">Nenhum arquivo selecionado.</td></tr>';
 tb.querySelectorAll('button[data-i]').forEach(b=>b.onclick=()=>{state.files.splice(Number(b.dataset.i),1);renderFileList();updateUploadSummary();}); tb.querySelectorAll('button[data-details]').forEach(b=>b.onclick=()=>showBulkUploadMessage(`${state.files[Number(b.dataset.details)]?.originalName}: ${state.files[Number(b.dataset.details)]?.errorLog||''}`,'danger'));}
function validateBeforeUpload(){const folderId=getCurrentFolderId();console.log('[BulkUpload] pasta destino:',folderId);console.log('[BulkUpload] arquivos selecionados:',state.files.length);if(!folderId){showBulkUploadMessage('Selecione uma pasta antes de enviar os documentos.','danger');showAppToast('Selecione uma pasta antes de enviar.','warning','Pasta obrigatória');return false;} if(!state.files.length){showBulkUploadMessage('Adicione pelo menos um arquivo para enviar.','danger');showAppToast('Adicione pelo menos um arquivo.','warning','Nenhum arquivo');return false;} return true;}
async function checkDuplicatesBeforeUpload(){const folderId=getCurrentFolderId();const names=state.files.filter(f=>f.status!=='success'&&f.status!=='ignored').map(f=>f.uploadName);if(!names.length)return[];const token=document.querySelector('input[name="__RequestVerificationToken"]')?.value;const r=await fetch('/Ged/Documents/CheckDuplicateNames',{method:'POST',headers:{'Content-Type':'application/json',...(token?{'RequestVerificationToken':token}:{})},body:JSON.stringify({folderId,fileNames:names})});const j=await r.json().catch(()=>({success:false,message:'Erro ao verificar duplicidades'}));if(!r.ok||!j.success)throw new Error(j.message||'Não foi possível verificar duplicidades. Tente novamente.');return j.duplicates||[];}
function showDuplicateDecision(dups){const box=document.getElementById('bulkDuplicateDecision');const list=document.getElementById('bulkDuplicateList');if(!box||!list)return;box.classList.remove('d-none');list.innerHTML=dups.map(d=>`<li><strong>${escapeHtml(d.fileName)}</strong> já existe.</li>`).join('');}
function hideDuplicateDecision(){document.getElementById('bulkDuplicateDecision')?.classList.add('d-none');}
function applyDuplicateStrategy(strategy){state.duplicateStrategy=strategy;hideDuplicateDecision();if(strategy===DuplicateStrategy.cancel){showBulkUploadMessage('Envio cancelado pelo usuário.','warning');showAppToast('Envio cancelado.','warning','Upload em lote');return;}state.files.forEach(f=>{if(f.status==='duplicate')f.duplicateStrategy=strategy;});uploadFiles(true);}

async function uploadFiles(skipDuplicateCheck){try{clearBulkUploadMessage();if(state.uploading||!validateBeforeUpload())return;setBulkUploadLoading(true);state.uploading=true;showBulkUploadMessage('Iniciando envio...','info');console.log('[BulkUpload] iniciando upload');
if(!skipDuplicateCheck){const dups=await checkDuplicatesBeforeUpload();if(dups.length){state.files.forEach(f=>{const hit=dups.find(d=>d.fileName.toLowerCase()===f.uploadName.toLowerCase());if(hit){f.status='duplicate';f.existingDocumentId=hit.existingDocumentId||null;}});renderFileList();updateUploadSummary();showDuplicateDecision(dups);return;}}
let success=0,error=0;for(const fileItem of state.files.filter(x=>x.status!=='success'&&x.status!=='ignored')){if(fileItem.status==='duplicate'&&!fileItem.duplicateStrategy&&!state.duplicateStrategy)continue;const r=await uploadSingleFile(fileItem);if(r==='success'||r==='ignored')success++;else error++;updateUploadSummary();}
if(success>0&&error===0){showAppToast(`${success} documento(s) enviado(s) com sucesso.`,'success','Upload concluído');setTimeout(()=>{closeBulkUploadModal();resetBulkUploadState();window.location.reload();},900);return;}
if(success>0&&error>0){showBulkUploadMessage(`${success} enviado(s), ${error} falharam. Verifique os arquivos com erro.`,'warning');showAppToast('Alguns documentos não foram enviados.','warning','Upload parcial');document.getElementById('btnBulkRetryFailed')?.classList.remove('d-none');return;}
if(success===0&&error>0){showBulkUploadMessage('Nenhum documento foi enviado. Verifique os erros por arquivo.','danger');showAppToast('Nenhum documento foi enviado.','error','Erro no upload');document.getElementById('btnBulkRetryFailed')?.classList.remove('d-none');}
}catch(err){console.error('[BulkUpload] erro geral no upload',err);showBulkUploadMessage('Falha inesperada ao enviar os documentos.','danger');showAppToast('Falha inesperada ao enviar os documentos.','error','Erro');}
finally{state.uploading=false;setBulkUploadLoading(false);}}

function uploadSingleFile(fileItem){return new Promise(resolve=>{const folderId=getCurrentFolderId();console.log('[BulkUpload] enviando arquivo:',fileItem.originalName);const fd=new FormData();fd.append('file',fileItem.file);fd.append('folderId',folderId||'');fd.append('batchId',state.batchId||'');fd.append('runOcr','false');fd.append('generatePreview','false');fd.append('notes','');if(fileItem.duplicateStrategy||state.duplicateStrategy)fd.append('duplicateStrategy',fileItem.duplicateStrategy||state.duplicateStrategy);if(fileItem.existingDocumentId)fd.append('existingDocumentId',fileItem.existingDocumentId);if(fileItem.uploadName)fd.append('uploadName',fileItem.uploadName);
fileItem.status='uploading';fileItem.progress=0;fileItem.errorMessage='Enviando...';renderFileList();updateUploadSummary();
const xhr=new XMLHttpRequest();xhr.open('POST','/Ged/Documents/BulkUploadSingle',true);const token=document.querySelector('input[name="__RequestVerificationToken"]')?.value;if(token)xhr.setRequestHeader('RequestVerificationToken',token);
xhr.upload.onprogress=e=>{if(e.lengthComputable){fileItem.progress=Math.round((e.loaded/e.total)*100);renderFileList();}};
xhr.onload=()=>{let response=null;try{response=JSON.parse(xhr.responseText||'{}');}catch{response=null;}console.log('[BulkUpload] resposta arquivo:',response);if(xhr.status>=200&&xhr.status<300&&response?.success===true){fileItem.status=response.status||'success';fileItem.serverDocumentId=response.data?.documentId||null;fileItem.serverVersionId=response.data?.versionId||null;fileItem.errorMessage=response.message||'Enviado com sucesso.';fileItem.errorLog=null;fileItem.progress=100;renderFileList();resolve(fileItem.status==='ignored'?'ignored':'success');return;}fileItem.status='error';fileItem.errorMessage=response?.message||'Não foi possível enviar o arquivo.';fileItem.errorLog=response?.errorLog||xhr.responseText||null;renderFileList();resolve('error');};
xhr.onerror=()=>{fileItem.status='error';fileItem.errorMessage='Falha de comunicação com o servidor.';fileItem.errorLog='XMLHttpRequest network error';renderFileList();resolve('error');};xhr.send(fd);});}

function retryFailedFiles(){state.files.forEach(f=>{if(f.status==='error'){f.status='waiting';f.progress=0;}});uploadFiles(true);} 
function updateUploadSummary(){const c={total:state.files.length,success:0,error:0,ignored:0,duplicate:0};state.files.forEach(f=>{if(f.status in c)c[f.status]++;});const el=document.getElementById('bulkSummary');if(el)el.textContent=`Total: ${c.total} | Enviados: ${c.success} | Falhas: ${c.error} | Ignorados: ${c.ignored} | Duplicados: ${c.duplicate}`;}
function setBulkUploadLoading(isLoading){const btn=document.getElementById('btnBulkUploadSubmit');if(btn){btn.disabled=isLoading;btn.innerHTML=isLoading?'<span class="spinner-border spinner-border-sm me-1"></span>Enviando...':'Enviar documentos';}document.getElementById('btnBulkClear')?.toggleAttribute('disabled',isLoading);} 
function showBulkUploadMessage(m,t){const el=document.getElementById('bulkUploadMessage');if(!el)return;el.className=`alert alert-${t}`;el.textContent=m;el.classList.remove('d-none');}
function clearBulkUploadMessage(){const el=document.getElementById('bulkUploadMessage');if(!el)return;el.className='d-none alert';el.textContent='';}
function showAppToast(message,type,title){window.showAppToast?.(message,type,title);}const formatFileSize=b=>b<1024?`${b} B`:b<1048576?`${(b/1024).toFixed(1)} KB`:`${(b/1048576).toFixed(1)} MB`;const escapeHtml=v=>(v||'').replace(/[&<>'"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[c]));
const statusLabel=s=>({waiting:'Aguardando',duplicate:'Duplicado',uploading:'Enviando',success:'Enviado',ignored:'Ignorado',error:'Erro'})[s]||s;const statusColor=s=>({success:'success',error:'danger',uploading:'primary',duplicate:'warning',ignored:'secondary',waiting:'light'})[s]||'light';
document.addEventListener('DOMContentLoaded',initBulkUpload);
})();
