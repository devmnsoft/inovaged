(function(){
  const root=document.getElementById('ged-realtime-root');
  if(!root||!window.React||!window.ReactDOM) return;
  const e=React.createElement;
  function App(){
    const [state,setState]=React.useState({preview:'IDLE',ocr:'IDLE'});
    const backoff=React.useRef(1500);
    React.useEffect(()=>{
      let stopped=false; let conn;
      async function start(){
        if(!window.signalR) return fallback();
        conn=new signalR.HubConnectionBuilder().withUrl('/hubs/ocr-status').withAutomaticReconnect().build();
        conn.on('previewStatus',msg=>setState(s=>({...s,preview:msg.status||s.preview})));
        conn.on('ocrStatus',msg=>setState(s=>({...s,ocr:msg.status||s.ocr})));
        try{await conn.start();await conn.invoke('SubscribeTenant',root.dataset.tenantId);}catch{fallback();}
      }
      async function fallback(){
        while(!stopped){
          try{const r=await fetch(`/Ged/OcrStatus?versionId=${root.dataset.versionId}`);const j=await r.json();setState(s=>({...s,ocr:j.status||s.ocr}));backoff.current=1500;}catch{backoff.current=Math.min(backoff.current*2,20000);} await new Promise(r=>setTimeout(r,backoff.current));
        }
      }
      start(); return ()=>{stopped=true; if(conn) conn.stop();};
    },[]);
    return e('div',null,[e('div',{key:1},`Preview: ${state.preview}`),e('div',{key:2},`OCR: ${state.ocr}`)]);
  }
  ReactDOM.createRoot(root).render(e(App));
})();
