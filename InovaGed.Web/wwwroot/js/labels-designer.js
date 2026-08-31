(() => {
  const json = document.querySelector('#FieldsJson');
  if (!json) return;
  let fields;
  try { fields = JSON.parse(json.value); } catch { fields = []; }
  let selected = -1;
  const props = [...document.querySelectorAll('[data-prop]')];
  const nodes = [...document.querySelectorAll('.label-designer-field')];
  const select = i => { selected=i; nodes.forEach((n,x)=>n.classList.toggle('is-selected',x===i)); props.forEach(p=>{const value=fields[i]?.[p.dataset.prop];p.type==='checkbox'?p.checked=!!value:p.value=value??'';}); };
  nodes.forEach((n,i)=>n.addEventListener('click',()=>select(i)));
  props.forEach(p=>p.addEventListener('input',()=>{if(selected<0)return;const key=p.dataset.prop;fields[selected][key]=p.type==='checkbox'?p.checked:p.type==='number'?Number(p.value):p.value;const n=nodes[selected];if(key==='FieldLabel')n.textContent=p.value;if(key==='XMm')n.style.left=`${Number(p.value)*3.2}px`;if(key==='YMm')n.style.top=`${Number(p.value)*3.2}px`;if(key==='WidthMm')n.style.width=`${Number(p.value)*3.2}px`;if(key==='HeightMm')n.style.height=`${Number(p.value)*3.2}px`;if(key==='FontSizePt')n.style.fontSize=`${p.value}pt`;if(key==='Color')n.style.color=p.value;json.value=JSON.stringify(fields);}));
  document.querySelector('#designer-form')?.addEventListener('submit',()=>json.value=JSON.stringify(fields));
})();
