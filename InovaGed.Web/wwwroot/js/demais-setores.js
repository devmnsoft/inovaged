(function () {
  const root = document.querySelector("[data-ds-register]");
  if (!root) return;
  const token = document.querySelector("input[name=__RequestVerificationToken]")?.value || "";
  let generation = 0;
  let current = null;
  let files = [];
  const status = root.querySelector("[data-ds-status]");
  const send = root.querySelector("[data-ds-send]");
  const catalog = root.querySelector("[data-ds-catalog]");

  function setStatus(text, kind) {
    status.hidden = false;
    status.dataset.kind = kind || "info";
    status.textContent = text;
  }

  function field(name) {
    return root.querySelector("[name='" + name + "']");
  }

  async function confirmContextChange() {
    if (files.length === 0) return true;
    return window.confirm("Trocar o paciente ou o atendimento descarta os arquivos preparados nesta tela. Eles ainda não foram enviados.");
  }

  root.querySelector("[data-ds-find]").addEventListener("click", async function () {
    if (!(await confirmContextChange())) return;
    files = [];
    renderFiles();
    const myGeneration = ++generation;
    const controller = new AbortController();
    root._finder?.abort();
    root._finder = controller;
    current = null;
    send.disabled = true;
    setStatus("Consultando o paciente na origem informada.", "info");
    const body = new FormData(root.querySelector("[data-ds-patient-form]"));
    try {
      const response = await fetch(root.dataset.findUrl, { method: "POST", body, headers: { RequestVerificationToken: token }, signal: controller.signal });
      const payload = await response.json();
      if (myGeneration !== generation) return;
      if (payload.status !== "Found") {
        setStatus(payload.message || "A consulta não retornou paciente.", "erro");
        root.querySelector("[data-ds-encounters]").replaceChildren();
        return;
      }
      current = { patientId: payload.patient.id, encounterId: null };
      const list = root.querySelector("[data-ds-encounters]");
      list.replaceChildren();
      const empty = document.createElement("option");
      empty.value = "";
      empty.textContent = "Selecione o atendimento";
      list.appendChild(empty);
      (payload.encounters || []).forEach(function (item) {
        const option = document.createElement("option");
        option.value = item.id;
        const kind = item.kind || "tipo externo não mapeado";
        const admitted = item.admitted_at || "entrada não informada";
        const discharged = item.discharged_at || "sem alta";
        option.textContent = item.external_code + " · " + (item.raw_kind || "sem código") + " · " + kind + " · " + admitted + " · " + discharged + " · " + (item.status || "situação não informada");
        list.appendChild(option);
      });
      root.querySelector("[data-ds-context]").textContent = "Paciente " + payload.patient.external_code + " — " + payload.patient.display_name + ". Confirme o atendimento antes de enviar.";
      setStatus(payload.message, "info");
    } catch (error) {
      if (error.name === "AbortError" || myGeneration !== generation) return;
      setStatus("A consulta não foi concluída. Os arquivos preparados permanecem nesta tela.", "erro");
    }
  });

  root.querySelector("[data-ds-encounters]").addEventListener("change", async function (event) {
    if (!(await confirmContextChange())) { event.target.value = current?.encounterId || ""; return; }
    files = [];
    renderFiles();
    current = current ? { patientId: current.patientId, encounterId: event.target.value || null } : null;
    send.disabled = !current?.encounterId;
    const open = root.querySelector("[data-ds-open]");
    if (open && current?.patientId && current.encounterId)
      open.href = root.dataset.openUrl + "?patientId=" + encodeURIComponent(current.patientId) + "&encounterId=" + encodeURIComponent(current.encounterId);
  });

  function addFiles(list) {
    Array.from(list || []).forEach(function (file) {
      files.push({ file: file, catalog: "", sector: "", date: "", notes: "", confirm: false });
    });
    renderFiles();
  }

  root.querySelector("[data-ds-files]").addEventListener("change", function (event) {
    addFiles(event.target.files);
    event.target.value = "";
  });

  const drop = root.querySelector("[data-ds-drop]");
  ["dragover", "dragenter"].forEach(function (name) {
    drop.addEventListener(name, function (event) { event.preventDefault(); drop.classList.add("is-over"); });
  });
  drop.addEventListener("dragleave", function () { drop.classList.remove("is-over"); });
  drop.addEventListener("drop", function (event) {
    event.preventDefault();
    drop.classList.remove("is-over");
    addFiles(event.dataTransfer?.files);
  });

  root.querySelector("[data-ds-apply]").addEventListener("click", function () {
    files.forEach(function (item) {
      item.catalog = catalog.value;
      item.sector = field("sectorCode").value;
      item.date = field("documentDate").value;
      item.notes = field("notes").value;
      item.confirm = field("confirmAnotherOccurrence").checked;
    });
    renderFiles();
    setStatus("Valores comuns aplicados. Ajuste cada arquivo antes de gravar.", "info");
  });

  function renderFiles() {
    const host = root.querySelector("[data-ds-file-list]");
    host.replaceChildren();
    files.forEach(function (item, index) {
      const row = document.createElement("li");
      row.className = "ds-card";
      const title = document.createElement("p");
      title.textContent = (index + 1) + ". " + item.file.name;
      row.appendChild(title);
      if (item.file.type.startsWith("image/")) {
        const img = document.createElement("img");
        img.alt = "Prévia de " + item.file.name;
        img.className = "ds-thumb";
        img.src = URL.createObjectURL(item.file);
        row.appendChild(img);
      } else {
        const note = document.createElement("p");
        note.className = "ds-note";
        note.textContent = "Prévia local indisponível. O PDF abre depois da gravação, no visualizador do atendimento.";
        row.appendChild(note);
      }
      const select = catalog.cloneNode(true);
      select.removeAttribute("data-ds-catalog");
      select.value = item.catalog;
      select.addEventListener("change", function () { item.catalog = select.value; });
      row.appendChild(label("Nome padronizado", select));
      row.appendChild(labeledInput("Setor", item.sector, function (value) { item.sector = value; }));
      row.appendChild(labeledInput("Data", item.date, function (value) { item.date = value; }, "date"));
      row.appendChild(labeledInput("Observação", item.notes, function (value) { item.notes = value; }));
      const check = document.createElement("input");
      check.type = "checkbox";
      check.checked = item.confirm;
      check.addEventListener("change", function () { item.confirm = check.checked; });
      row.appendChild(label("Outra ocorrência, não nova versão", check));
      host.appendChild(row);
    });
  }

  function label(text, control) {
    const wrap = document.createElement("label");
    wrap.append(text, control);
    return wrap;
  }

  function labeledInput(text, value, onChange, type) {
    const input = document.createElement("input");
    input.type = type || "text";
    input.value = value || "";
    input.addEventListener("input", function () { onChange(input.value); });
    return label(text, input);
  }

  function summary() {
    return files.map(function (item, index) {
      const name = item.catalog || catalog.value || "sem nome padronizado";
      return (index + 1) + ". " + item.file.name + " → " + name + " · setor " + (item.sector || field("sectorCode").value || "não informado");
    }).join("\n");
  }

  send.addEventListener("click", async function () {
    if (!current?.patientId || !current.encounterId) {
      setStatus("Confirme o paciente e o atendimento antes do envio.", "erro");
      return;
    }
    if (files.length === 0) {
      setStatus("Inclua ao menos um arquivo.", "erro");
      return;
    }
    const missing = files.some(function (item) { return !(item.catalog || catalog.value); });
    if (missing) { setStatus("Cada arquivo precisa de um nome do catálogo.", "erro"); return; }
    const accepted = window.confirm("Paciente e atendimento confirmados.\nArquivos: " + files.length + "\n" + summary() + "\n\nA gravação não conclui OCR, extração nem conferência.");
    if (!accepted) return;
    send.disabled = true;
    const results = root.querySelector("[data-ds-results]");
    results.replaceChildren();
    const pending = [];
    for (const item of files) {
      const row = document.createElement("li");
      row.textContent = item.file.name + ": preparando a transferência.";
      results.appendChild(row);
      try {
        const stored = await sendInParts(item, row);
        if (!stored) pending.push(item);
      } catch {
        row.textContent = item.file.name + " — a transferência não foi confirmada. O arquivo permanece nesta tela para nova tentativa.";
        pending.push(item);
      }
    }
    files = pending;
    renderFiles();
    send.disabled = !current?.encounterId;
    setStatus("A tentativa de gravação terminou arquivo a arquivo. Pré-visualização, OCR e extração têm situação própria.", "info");
  });

  async function postForm(url, body) {
    const response = await fetch(url, { method: "POST", body, headers: { RequestVerificationToken: token } });
    return response.json();
  }

  async function sendInParts(item, row) {
    const catalogCode = item.catalog || catalog.value;
    if (!item.uploadId) {
      const start = new FormData();
      start.append("__RequestVerificationToken", token);
      start.append("patientId", current.patientId);
      start.append("encounterId", current.encounterId);
      start.append("catalogCode", catalogCode);
      start.append("fileName", item.file.name);
      start.append("contentType", item.file.type || "application/octet-stream");
      start.append("totalBytes", String(item.file.size));
      const started = await postForm(root.dataset.startUrl, start);
      if (!started.uploadId) {
        row.textContent = item.file.name + " — " + (started.stage || "AUTORIZACAO") + ": " + (started.message || "O envio não começou.");
        return false;
      }
      item.uploadId = started.uploadId;
      item.chunkSize = started.chunkSize;
      item.totalChunks = started.totalChunks;
    }
    let missing = [];
    const statusResponse = await fetch(root.dataset.statusUrl + "?uploadId=" + encodeURIComponent(item.uploadId), { headers: { "X-Requested-With": "fetch" } });
    const status = await statusResponse.json();
    missing = status.missing || [];
    if (status.status === "COMPLETED" && status.documentId) {
      return finish(item, row, catalogCode);
    }
    const cancel = document.createElement("button");
    cancel.type = "button";
    cancel.textContent = "Cancelar transferência";
    let cancelled = false;
    cancel.addEventListener("click", async function () {
      cancelled = true;
      const body = new FormData();
      body.append("__RequestVerificationToken", token);
      body.append("uploadId", item.uploadId);
      await postForm(root.dataset.cancelUrl, body);
      row.textContent = item.file.name + " — transferência cancelada.";
    });
    row.appendChild(cancel);
    const indexes = missing.length ? missing : Array.from({ length: item.totalChunks || 1 }, function (_, index) { return index; });
    for (const index of indexes) {
      if (cancelled) return false;
      const size = item.chunkSize || item.file.size;
      const blob = item.file.slice(index * size, Math.min(item.file.size, (index + 1) * size));
      const part = new FormData();
      part.append("__RequestVerificationToken", token);
      part.append("uploadId", item.uploadId);
      part.append("chunkIndex", String(index));
      part.append("parte", blob, item.file.name);
      row.firstChild && (row.childNodes[0].textContent = item.file.name + ": transferência " + (Number(index) + 1) + "/" + (item.totalChunks || indexes.length) + ".");
      const saved = await postForm(root.dataset.partUrl, part);
      if (saved.received === undefined && saved.percent === undefined) {
        row.appendChild(document.createTextNode(" " + (saved.message || "A parte não foi aceita.")));
        return false;
      }
    }
    if (cancelled) return false;
    return finish(item, row, catalogCode);
  }

  async function finish(item, row, catalogCode) {
    const body = new FormData();
    body.append("__RequestVerificationToken", token);
    body.append("patientId", current.patientId);
    body.append("encounterId", current.encounterId);
    body.append("catalogCode", catalogCode);
    body.append("uploadId", item.uploadId);
    body.append("confirmAnotherOccurrence", item.confirm || field("confirmAnotherOccurrence").checked ? "true" : "false");
    const payload = await postForm(root.dataset.completeUrl, body);
    row.textContent = item.file.name + " — " + payload.stage + ": " + payload.message;
    if ((payload.stage === "VINCULO" || payload.stage === "MULTIPLICIDADE" || payload.stage === "DUPLICIDADE") && payload.documentId)
      row.appendChild(retryLink(item, payload.documentId));
    if (payload.stage === "FALHOU_AO_ENFILEIRAR" && payload.linkId) row.appendChild(retryQueue(payload.linkId));
    if (payload.stored) item.uploadId = null;
    return !!payload.stored;
  }

  function retryLink(item, documentId) {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = "Vincular o documento já gravado";
    button.addEventListener("click", async function () {
      const body = new FormData();
      body.append("__RequestVerificationToken", token);
      body.append("patientId", current.patientId);
      body.append("encounterId", current.encounterId);
      body.append("documentId", documentId);
      body.append("catalogCode", item.catalog || catalog.value);
      body.append("confirmAnotherOccurrence", "true");
      const response = await fetch(root.dataset.linkUrl, { method: "POST", body, headers: { RequestVerificationToken: token } });
      const payload = await response.json();
      button.insertAdjacentText("afterend", " " + payload.stage + ": " + payload.message);
    });
    return button;
  }

  function retryQueue(linkId) {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = "Repetir só a fila";
    button.addEventListener("click", async function () {
      const body = new FormData();
      body.append("__RequestVerificationToken", token);
      body.append("linkId", linkId);
      const response = await fetch(root.dataset.retryUrl, { method: "POST", body, headers: { RequestVerificationToken: token } });
      const payload = await response.json();
      button.insertAdjacentText("afterend", " " + payload.stage + ": " + payload.message);
    });
    return button;
  }
})();

(function () {
  const workspace = document.querySelector("[data-ds-workspace]");
  if (!workspace) return;
  const page = document.querySelector("[data-ds-consult]");
  const frame = workspace.querySelector("[data-ds-frame]");
  const viewerStatus = workspace.querySelector("[data-ds-viewer-status]");
  const textHost = workspace.querySelector("[data-ds-text]");
  let request = 0;

  function openDocument(button) {
    const mine = ++request;
    workspace.querySelectorAll("[data-ds-open]").forEach(function (item) { item.setAttribute("aria-current", "false"); });
    button.setAttribute("aria-current", "true");
    frame.removeAttribute("src");
    if (viewerStatus) viewerStatus.textContent = "Carregando o documento. Se a sessão tiver expirado, a mensagem aparece neste quadro, sem JSON.";
    const pageNumber = button.dataset.page;
    const url = button.dataset.url + (pageNumber ? "#page=" + pageNumber : "");
    window.setTimeout(function () {
      if (mine !== request) return;
      frame.src = url;
      if (viewerStatus) viewerStatus.textContent = pageNumber ? "Página indicada pelo texto: " + pageNumber + "." : "Página não informada pelo mecanismo.";
    }, 0);
    if (button.dataset.version && page?.dataset.textUrl) {
      textHost.textContent = "Lendo o texto desta versão.";
      fetch(page.dataset.textUrl + "?versionId=" + encodeURIComponent(button.dataset.version), { headers: { "X-Requested-With": "fetch" } })
        .then(function (response) { return response.text(); })
        .then(function (html) {
          if (mine !== request) return;
          const parsed = new DOMParser().parseFromString(html, "text/html");
          textHost.textContent = parsed.body?.textContent || "O texto não pôde ser lido.";
        })
        .catch(function () { if (mine === request) textHost.textContent = "A leitura do texto não foi concluída."; });
    }
  }

  workspace.querySelectorAll("[data-ds-open]").forEach(function (button) {
    button.addEventListener("click", function () { openDocument(button); });
  });

  workspace.querySelectorAll("[data-ds-tab]").forEach(function (tab) {
    tab.addEventListener("click", function () {
      workspace.querySelectorAll("[data-region]").forEach(function (region) {
        region.classList.toggle("is-active", region.dataset.region === tab.dataset.dsTab);
      });
    });
  });

  workspace.querySelectorAll("[data-ds-panel]").forEach(function (tab) {
    tab.addEventListener("click", function () {
      workspace.querySelectorAll("[data-ds-panel-body]").forEach(function (panel) {
        const active = panel.dataset.dsPanelBody === tab.dataset.dsPanel;
        panel.hidden = !active;
        panel.classList.toggle("is-active", active);
      });
    });
  });

  workspace.querySelectorAll("[data-ds-filter]").forEach(function (input) {
    input.addEventListener("input", applyFilters);
    input.addEventListener("change", applyFilters);
  });

  function applyFilters() {
    const name = (workspace.querySelector("[data-ds-filter='name']")?.value || "").toLocaleLowerCase();
    const origin = workspace.querySelector("[data-ds-filter='origin']")?.value || "";
    const sector = (workspace.querySelector("[data-ds-filter='sector']")?.value || "").toLocaleLowerCase();
    const review = workspace.querySelector("[data-ds-filter='review']")?.value || "";
    workspace.querySelectorAll(".ds-doc").forEach(function (button) {
      const visible = (!name || (button.dataset.name || "").toLocaleLowerCase().includes(name))
        && (!origin || button.dataset.origin === origin)
        && (!sector || (button.dataset.sector || "").toLocaleLowerCase().includes(sector))
        && (!review || button.dataset.review === review);
      button.hidden = !visible;
    });
  }

  const search = workspace.querySelector("[data-ds-search]");
  search?.addEventListener("submit", async function (event) {
    event.preventDefault();
    const token = page.querySelector("input[name=__RequestVerificationToken]")?.value || "";
    const body = new FormData(search);
    body.append("__RequestVerificationToken", token);
    body.append("encounterId", new URLSearchParams(window.location.search).get("encounterId") || "");
    const hits = workspace.querySelector("[data-ds-hits]");
    hits.replaceChildren();
    const waiting = document.createElement("li");
    waiting.textContent = "Buscando no atendimento selecionado.";
    hits.appendChild(waiting);
    try {
      const response = await fetch(page.dataset.searchUrl, { method: "POST", body, headers: { RequestVerificationToken: token } });
      const payload = await response.json();
      hits.replaceChildren();
      const note = document.createElement("li");
      note.textContent = payload.message || "Busca literal concluída.";
      hits.appendChild(note);
      (payload.hits || []).forEach(function (hit) {
        const item = document.createElement("li");
        const button = document.createElement("button");
        button.type = "button";
        button.textContent = hit.name + " · atendimento " + hit.encounterCode + (hit.page ? " · página " + hit.page : " · página não informada");
        button.dataset.dsOpen = "";
        button.dataset.url = page.dataset.fileUrl + "?versionId=" + encodeURIComponent(hit.versionId);
        if (hit.page) button.dataset.page = hit.page;
        button.dataset.version = hit.versionId;
        button.addEventListener("click", function () { openDocument(button); });
        const excerpt = document.createElement("p");
        excerpt.textContent = (hit.excerpt || "Sem trecho localizado.") + " · " + (hit.textOrigin || "");
        item.append(button, excerpt);
        hits.appendChild(item);
      });
      if ((payload.hits || []).length === 0) {
        const empty = document.createElement("li");
        empty.textContent = "Nenhuma ocorrência autorizada neste escopo.";
        hits.appendChild(empty);
      }
    } catch {
      waiting.textContent = "A busca não foi concluída. O atendimento selecionado permanece o mesmo.";
    }
  });

  const auto = workspace.dataset.autoVersion;
  if (auto) {
    const button = workspace.querySelector("[data-version='" + auto + "']");
    if (button) openDocument(button);
  }
  const partner = page?.dataset.partnerOrigin;
  if (page?.dataset.embedded === "1" && partner) {
    const notify = function (payload) {
      if (window.parent && window.parent !== window) window.parent.postMessage(payload, partner);
    };
    notify({ tipo: "ds-carregado", versaoId: auto || null });
    window.addEventListener("message", function (event) {
      if (event.origin !== partner || !event.data || typeof event.data !== "object") return;
      if (event.data.tipo === "ds-altura") return;
      if (event.data.tipo === "ds-atendimento" && event.data.codigoAtendimento) {
        const body = new FormData();
        body.append("encounterCode", event.data.codigoAtendimento);
        fetch(page.dataset.switchUrl, { method: "POST", body, credentials: "same-origin" }).then(function (response) {
          if (response.ok) window.location.reload();
          else notify({ tipo: "ds-expirado" });
        });
      }
    });
  }
})();
