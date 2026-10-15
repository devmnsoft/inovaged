(function () {
  const root = document.querySelector("[data-ds-register]");
  if (!root) return;
  const token = document.querySelector("input[name=__RequestVerificationToken]")?.value || "";
  let generation = 0;
  let current = null;
  let files = [];
  let busy = false;
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

  function formatWhen(value, empty) {
    if (!value) return empty;
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return String(value);
    return date.toLocaleString("pt-BR", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" });
  }

  function encounterLabel(item) {
    return item.external_code + " · " + (item.raw_kind || "sem código") + " · " + (item.kind || "tipo não mapeado")
      + " · " + formatWhen(item.admitted_at, "entrada não informada")
      + " · " + formatWhen(item.discharged_at, "sem alta")
      + " · " + (item.status || "situação não informada");
  }

  function resetEncounter(placeholder) {
    current = null;
    send.disabled = true;
    const list = root.querySelector("[data-ds-encounters]");
    list.replaceChildren();
    const option = document.createElement("option");
    option.value = "";
    option.textContent = placeholder;
    list.appendChild(option);
    const open = root.querySelector("[data-ds-open]");
    if (open) {
      open.removeAttribute("href");
      open.setAttribute("aria-disabled", "true");
    }
  }

  root.querySelector("[data-ds-open]")?.addEventListener("click", function (event) {
    if (event.currentTarget.getAttribute("aria-disabled") === "true") event.preventDefault();
  });

  async function confirmContextChange() {
    if (busy) return false;
    if (files.length === 0) return true;
    return window.confirm("Trocar o paciente ou o atendimento descarta a seleção local. Arquivos já gravados permanecem no GED; envios parciais continuam no contexto original.");
  }

  root.querySelector("[data-ds-find]").addEventListener("click", async function () {
    if (!(await confirmContextChange())) return;
    files = [];
    renderFiles();
    const myGeneration = ++generation;
    const controller = new AbortController();
    root._finder?.abort();
    root._finder = controller;
    resetEncounter("Carregando atendimentos…");
    setStatus("Consultando o paciente na origem informada.", "info");
    const body = new FormData(root.querySelector("[data-ds-patient-form]"));
    try {
      const response = await fetch(root.dataset.findUrl, { method: "POST", body, headers: { RequestVerificationToken: token }, signal: controller.signal });
      const type = response.headers.get("content-type") || "";
      if (!type.includes("json")) throw new Error("not-json");
      const payload = await response.json();
      if (myGeneration !== generation) return;
      if (payload.status !== "Found" || !payload.patient) {
        resetEncounter("Selecione um atendimento");
        root.querySelector("[data-ds-context]").textContent = payload.status === "NotFound"
          ? (payload.message || "Paciente não encontrado.")
          : payload.status === "Unavailable"
            ? (payload.message || "A origem está indisponível.")
            : payload.status === "Failed"
              ? (payload.message || "A consulta falhou.")
              : (payload.message || "A consulta não retornou paciente.");
        setStatus(root.querySelector("[data-ds-context]").textContent, "erro");
        return;
      }
      const encounters = payload.encounters || [];
      current = { patientId: payload.patient.id, encounterId: null };
      const list = root.querySelector("[data-ds-encounters]");
      list.replaceChildren();
      const empty = document.createElement("option");
      empty.value = "";
      empty.textContent = encounters.length ? "Selecione um atendimento" : "Nenhum atendimento disponível";
      list.appendChild(empty);
      encounters.forEach(function (item) {
        const option = document.createElement("option");
        option.value = item.id;
        option.textContent = encounterLabel(item);
        list.appendChild(option);
      });
      list.value = "";
      root.querySelector("[data-ds-context]").textContent = encounters.length
        ? "Paciente " + payload.patient.external_code + " — " + payload.patient.display_name + ". Selecione um atendimento antes de enviar."
        : "Paciente " + payload.patient.external_code + " — " + payload.patient.display_name + ". Não há atendimentos para abrir.";
      setStatus(payload.message || "Paciente localizado.", encounters.length ? "info" : "erro");
    } catch (error) {
      if (error.name === "AbortError" || myGeneration !== generation) return;
      resetEncounter("Selecione um atendimento");
      setStatus("A consulta não foi concluída. A resposta não pôde ser lida e a seleção anterior foi desfeita.", "erro");
    }
  });

  root.querySelector("[data-ds-encounters]").addEventListener("change", async function (event) {
    if (!(await confirmContextChange())) { event.target.value = current?.encounterId || ""; return; }
    files = [];
    renderFiles();
    current = current ? { patientId: current.patientId, encounterId: event.target.value || null } : null;
    send.disabled = !current?.encounterId;
    const open = root.querySelector("[data-ds-open]");
    if (open && current?.patientId && current.encounterId) {
      open.href = root.dataset.openUrl + "?patientId=" + encodeURIComponent(current.patientId) + "&encounterId=" + encodeURIComponent(current.encounterId);
      open.setAttribute("aria-disabled", "false");
    } else if (open) {
      open.removeAttribute("href");
      open.setAttribute("aria-disabled", "true");
    }
  });

  function addFiles(list) {
    if (busy) return;
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
      if (item.uploadId) return;
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
      select.disabled = !!item.uploadId;
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
    if (busy) return;
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
    busy = true;
    const controls = Array.from(root.querySelectorAll("input,select,textarea,button"));
    const disabledBefore = controls.map(function (control) { return control.disabled; });
    controls.forEach(function (control) { control.disabled = true; });
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
        row.textContent = item.file.name + " — não foi possível confirmar esta etapa. Tente novamente: a situação será consultada antes de reenviar qualquer parte.";
        pending.push(item);
      }
    }
    files = pending;
    controls.forEach(function (control,index) { control.disabled = disabledBefore[index]; });
    busy = false;
    renderFiles();
    send.disabled = !current?.encounterId;
    setStatus("A tentativa de gravação terminou arquivo a arquivo. Pré-visualização, OCR e extração têm situação própria.", "info");
  });

  async function postForm(url, body) {
    const response = await fetch(url, { method: "POST", body, headers: { RequestVerificationToken: token } });
    return response.json();
  }

  async function sendInParts(item, row) {
    if (!item.uploadId) item.context = { patientId: current.patientId, encounterId: current.encounterId, catalogCode: item.catalog || catalog.value };
    const catalogCode = item.context.catalogCode;
    if (!item.uploadId) {
      const start = new FormData();
      start.append("__RequestVerificationToken", token);
      start.append("patientId", item.context.patientId);
      start.append("encounterId", item.context.encounterId);
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
      item.catalog = catalogCode;
      item.chunkSize = started.chunkSize;
      item.totalChunks = started.totalChunks;
    }
    let missing = [];
    const statusResponse = await fetch(root.dataset.statusUrl + "?uploadId=" + encodeURIComponent(item.uploadId), { headers: { "X-Requested-With": "fetch" } });
    const status = await statusResponse.json();
    missing = status.missing || [];
    if ((status.status === "COMPLETED" || status.status === "COMPLETING") && status.documentId) {
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
      const cancellation = await postForm(root.dataset.cancelUrl, body);
      if (!cancellation.cancelled) {
        cancelled = false;
        row.appendChild(document.createTextNode(" O cancelamento não foi confirmado. Retome este envio para consultar sua situação."));
        return;
      }
      item.uploadId = null;
      item.context = null;
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
    body.append("patientId", item.context.patientId);
    body.append("encounterId", item.context.encounterId);
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
  if (page?.dataset.publicId && window.location.hash.indexOf("protecao=") >= 0) {
    const token = new URLSearchParams(window.location.hash.replace(/^#/, "")).get("protecao");
    if (token) writeStore("ds-protecao-" + page.dataset.publicId, token);
    history.replaceState(null, "", window.location.pathname + window.location.search);
  }
  function readStore(key) {
    try { return sessionStorage.getItem(key) || ""; } catch { return ""; }
  }
  function writeStore(key, value) {
    try { sessionStorage.setItem(key, value); } catch { /* a página segue sem o token local quando o navegador bloqueia o armazenamento */ }
  }
  function removeStore(key) {
    try { sessionStorage.removeItem(key); } catch { /* sem armazenamento local */ }
  }
  const protection = function () {
    return page?.dataset.publicId ? readStore("ds-protecao-" + page.dataset.publicId) : "";
  };
  page?.querySelectorAll("[data-ds-protection]").forEach(function (input) { input.value = protection(); });
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

  const regionTabs = Array.from((page || workspace).querySelectorAll("[data-ds-tab]"));
  function activateRegion(tab) {
      workspace.querySelectorAll("[data-region]").forEach(function (region) {
        region.classList.toggle("is-active", region.dataset.region === tab.dataset.dsTab);
      });
      regionTabs.forEach(function (item) {
        const selected = item === tab;
        item.setAttribute("aria-selected", selected ? "true" : "false");
        item.tabIndex = selected ? 0 : -1;
      });
  }
  regionTabs.forEach(function (tab, index) {
    tab.id = tab.id || "ds-region-tab-" + tab.dataset.dsTab;
    tab.setAttribute("role", "tab");
    tab.setAttribute("aria-selected", index === 0 ? "true" : "false");
    tab.tabIndex = index === 0 ? 0 : -1;
    const panel = workspace.querySelector("[data-region='" + tab.dataset.dsTab + "']");
    panel?.setAttribute("role", "tabpanel");
    panel?.setAttribute("aria-labelledby", tab.id);
    tab.addEventListener("click", function () { activateRegion(tab); });
    tab.addEventListener("keydown", function (event) {
      const target = event.key === "Home" ? 0 : event.key === "End" ? regionTabs.length - 1
        : event.key === "ArrowRight" ? (index + 1) % regionTabs.length
        : event.key === "ArrowLeft" ? (index + regionTabs.length - 1) % regionTabs.length : -1;
      if (target < 0) return;
      event.preventDefault();
      activateRegion(regionTabs[target]);
      regionTabs[target].focus();
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
  const hits = workspace.querySelector("[data-ds-hits]");
  const pager = workspace.querySelector("[data-ds-pager]");
  let searchGeneration = 0;
  let searchState = { term: "", scope: "" };

  function searchScope() {
    const box = search?.querySelector("[name='includeOtherEncounters']");
    return box && box.checked ? "1" : "0";
  }

  async function runSearch(requestedPage, reset) {
    if (!search || !hits) return;
    const my = ++searchGeneration;
    const token = page.querySelector("input[name=__RequestVerificationToken]")?.value || "";
    const term = (search.querySelector("[name='term']")?.value || "").trim();
    const scope = searchScope();
    const pageNumber = reset || term !== searchState.term || scope !== searchState.scope ? 0 : requestedPage;
    searchState = { term: term, scope: scope };
    const pageInput = search.querySelector("[data-ds-page]");
    if (pageInput) pageInput.value = String(pageNumber);
    const body = new FormData(search);
    body.set("page", String(pageNumber));
    body.append("__RequestVerificationToken", token);
    body.append("protecao", protection());
    if (page.dataset.embedded !== "1")
      body.set("encounterId", page.dataset.encounterId || "");
    hits.replaceChildren();
    const waiting = document.createElement("li");
    waiting.textContent = "Buscando no atendimento selecionado.";
    hits.appendChild(waiting);
    if (pager) pager.hidden = true;
    try {
      const response = await fetch(page.dataset.searchUrl, { method: "POST", body, credentials: "same-origin", headers: { RequestVerificationToken: token, "X-InovaGed-Protecao": protection(), "X-InovaGed-Contexto": page.dataset.publicId || "" } });
      if (my !== searchGeneration) return;
      const type = response.headers.get("content-type") || "";
      if (!response.ok || !type.includes("application/json")) {
        hits.replaceChildren();
        const failed = document.createElement("li");
        failed.textContent = response.status === 401 || response.status === 403
          ? "A sessão expirou ou a origem do comando não é a desta abertura. Abra novamente em uma página própria do InovaGed. Os filtros desta tela permanecem."
          : "A pesquisa não foi concluída. A resposta não veio em formato utilizável. Os filtros permanecem.";
        hits.appendChild(failed);
        return;
      }
      const payload = await response.json();
      if (my !== searchGeneration) return;
      hits.replaceChildren();
      const note = document.createElement("li");
      const total = typeof payload.total === "number" ? payload.total : 0;
      const size = payload.pageSize || 20;
      const current = typeof payload.page === "number" ? payload.page : pageNumber;
      const shown = (payload.hits || []).length;
      const from = shown === 0 ? 0 : current * size + 1;
      const to = shown === 0 ? 0 : current * size + shown;
      note.textContent = (payload.message || "Busca literal concluída.") + " Resultados neste escopo: " + total + ".";
      hits.appendChild(note);
      (payload.hits || []).forEach(function (hit) {
        const item = document.createElement("li");
        const button = document.createElement("button");
        button.type = "button";
        const other = hit.encounterCode && page.dataset.encounterCode && hit.encounterCode !== page.dataset.encounterCode;
        button.textContent = hit.name + " · atendimento " + hit.encounterCode + (hit.page ? " · página " + hit.page : " · página não informada") + (other ? " · outro episódio" : "");
        const excerpt = document.createElement("p");
        excerpt.textContent = (hit.excerpt || "Sem trecho localizado.") + " · " + (hit.textOrigin || "correspondência literal");
        if (!other) {
          button.dataset.url = page.dataset.fileUrl + "?versionId=" + encodeURIComponent(hit.versionId);
          if (hit.page) button.dataset.page = hit.page;
          button.dataset.version = hit.versionId;
          button.addEventListener("click", function () { openDocument(button); });
        } else if (page.dataset.embedded !== "1" && hit.encounterId) {
          button.addEventListener("click", function () {
            const target = "/DemaisSetores/Consultar?patientId=" + encodeURIComponent(page.dataset.patientId || "") + "&encounterId=" + encodeURIComponent(hit.encounterId) + "&versaoId=" + encodeURIComponent(hit.versionId);
            window.location.assign(target);
          });
          excerpt.textContent += " Este resultado abre o atendimento correspondente, sem permanecer no episódio anterior.";
        } else if (page.dataset.embedded === "1" && page.dataset.allowSwitch === "1") {
          button.addEventListener("click", function () {
            if (page.dataset.publicId) writeStore("ds-abrir-" + page.dataset.publicId, hit.versionId);
            const form = new FormData();
            form.append("encounterCode", String(hit.encounterCode).slice(0, 80));
            form.append("protecao", protection());
            const antiforgery = page.querySelector("input[name=__RequestVerificationToken]")?.value || "";
            form.append("__RequestVerificationToken", antiforgery);
            fetch(page.dataset.switchUrl, { method: "POST", body: form, credentials: "same-origin", headers: { "X-InovaGed-Protecao": protection(), "X-InovaGed-Contexto": page.dataset.publicId || "", RequestVerificationToken: antiforgery } }).then(function (switchResponse) {
              if (switchResponse.ok) window.location.assign(window.location.pathname);
              else excerpt.textContent = "A troca de atendimento não foi autorizada. O visualizador permanece no episódio atual.";
            });
          });
          excerpt.textContent += " A abertura permite ir a esse atendimento. O documento só abre depois da troca autorizada.";
        } else {
          button.disabled = true;
          excerpt.textContent += " Este resultado é de outro atendimento. O visualizador permanece no episódio atual.";
        }
        item.append(button, excerpt);
        hits.appendChild(item);
      });
      if (shown === 0) {
        const empty = document.createElement("li");
        empty.textContent = "Nenhuma ocorrência autorizada neste escopo.";
        hits.appendChild(empty);
      }
      if (pager) {
        pager.hidden = total === 0;
        const range = pager.querySelector("[data-ds-range]");
        const limitNote = payload.truncated ? " Há resultados além das " + (payload.reachable || 1020) + " posições navegáveis. O total acima os inclui. Refine o termo ou o escopo para alcançá-los." : "";
        if (range) range.textContent = shown === 0 ? "Nenhum resultado nesta página." : "Mostrando " + from + "–" + to + " de " + total + "." + limitNote;
        const prev = pager.querySelector("[data-ds-prev]");
        const next = pager.querySelector("[data-ds-next]");
        if (prev) prev.disabled = current <= 0;
        if (next) next.disabled = shown === 0 || (current + 1) * size >= total || current >= (payload.pageLimit || 50);
      }
    } catch {
      if (my !== searchGeneration) return;
      hits.replaceChildren();
      const failed = document.createElement("li");
      failed.textContent = "A busca não foi concluída. Os filtros permanecem e o atendimento selecionado não foi trocado.";
      hits.appendChild(failed);
    }
  }

  search?.addEventListener("submit", function (event) {
    event.preventDefault();
    runSearch(0, true);
  });
  pager?.querySelector("[data-ds-prev]")?.addEventListener("click", function () {
    const current = Number(search?.querySelector("[data-ds-page]")?.value || "0");
    runSearch(Math.max(0, current - 1), false);
  });
  pager?.querySelector("[data-ds-next]")?.addEventListener("click", function () {
    const current = Number(search?.querySelector("[data-ds-page]")?.value || "0");
    runSearch(current + 1, false);
  });

  const pendingOpen = page?.dataset.publicId ? readStore("ds-abrir-" + page.dataset.publicId) : "";
  const auto = pendingOpen || workspace.dataset.autoVersion;
  if (pendingOpen && page?.dataset.publicId) removeStore("ds-abrir-" + page.dataset.publicId);
  if (auto) {
    const button = workspace.querySelector("[data-version='" + auto + "']");
    if (button) openDocument(button);
  }
  page?.querySelector("[data-ds-collapse]")?.addEventListener("click", function (event) {
    const hidden = workspace.classList.toggle("is-analysis-hidden");
    event.currentTarget.setAttribute("aria-expanded", hidden ? "false" : "true");
    event.currentTarget.textContent = hidden ? "Mostrar análise" : "Recolher análise";
  });
  const lookup = page?.querySelector("[data-ds-consult-lookup]");
  let lookupGeneration = 0;
  function formatWhen(value, empty) {
    if (!value) return empty;
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return String(value);
    return date.toLocaleString("pt-BR", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" });
  }
  function resetLookup(placeholder, message) {
    const list = lookup?.querySelector("[data-ds-encounters]");
    const open = lookup?.querySelector("[data-ds-context-open]");
    const status = lookup?.querySelector("[data-ds-context]");
    if (list) {
      list.replaceChildren();
      const option = document.createElement("option");
      option.value = "";
      option.textContent = placeholder;
      list.appendChild(option);
      list.value = "";
    }
    if (open) {
      open.removeAttribute("href");
      open.setAttribute("aria-disabled", "true");
    }
    if (status && message) status.textContent = message;
  }
  lookup?.querySelector("[data-ds-context-open]")?.addEventListener("click", function (event) {
    if (event.currentTarget.getAttribute("aria-disabled") === "true") event.preventDefault();
  });
  lookup?.querySelector("[data-ds-find]")?.addEventListener("click", async function () {
    const my = ++lookupGeneration;
    resetLookup("Carregando atendimentos…", "Consultando a origem informada.");
    const token = page.querySelector("input[name=__RequestVerificationToken]")?.value || "";
    const body = new FormData(lookup.querySelector("[data-ds-patient-form]"));
    const status = lookup.querySelector("[data-ds-context]");
    try {
      const response = await fetch(page.dataset.findUrl, { method: "POST", body, headers: { RequestVerificationToken: token } });
      const type = response.headers.get("content-type") || "";
      if (!type.includes("json")) throw new Error("not-json");
      const payload = await response.json();
      if (my !== lookupGeneration) return;
      if (payload.status !== "Found" || !payload.patient) {
        const message = payload.status === "Unavailable"
          ? (payload.message || "A origem está indisponível. Nenhum atendimento foi mantido.")
          : payload.status === "NotFound"
            ? (payload.message || "Paciente não encontrado.")
            : payload.status === "Failed"
              ? (payload.message || "A consulta falhou. Nenhum atendimento foi mantido.")
              : (payload.message || "A consulta não retornou paciente.");
        resetLookup("Selecione um atendimento", message);
        return;
      }
      const encounters = payload.encounters || [];
      const list = lookup.querySelector("[data-ds-encounters]");
      list.replaceChildren();
      const empty = document.createElement("option");
      empty.value = "";
      empty.textContent = encounters.length ? "Selecione um atendimento" : "Nenhum atendimento disponível";
      list.appendChild(empty);
      encounters.forEach(function (item) {
        const option = document.createElement("option");
        option.value = item.id;
        option.dataset.patient = payload.patient.id;
        option.textContent = item.external_code + " · " + (item.raw_kind || "sem código") + " · " + (item.kind || "tipo não mapeado")
          + " · " + formatWhen(item.admitted_at, "entrada não informada")
          + " · " + formatWhen(item.discharged_at, "sem alta")
          + " · " + (item.status || "situação não informada");
        list.appendChild(option);
      });
      list.value = "";
      status.textContent = encounters.length
        ? "Paciente " + payload.patient.external_code + " — " + payload.patient.display_name + ". Selecione um atendimento para habilitar a abertura."
        : "Paciente " + payload.patient.external_code + " — " + payload.patient.display_name + ". Não há atendimentos disponíveis.";
    } catch {
      if (my === lookupGeneration) resetLookup("Selecione um atendimento", "A consulta não foi concluída. A resposta não pôde ser lida e a abertura anterior foi desabilitada.");
    }
  });
  lookup?.querySelector("[data-ds-encounters]")?.addEventListener("change", function (event) {
    const option = event.target.selectedOptions[0];
    const open = lookup.querySelector("[data-ds-context-open]");
    if (!open) return;
    if (!option?.value) {
      open.removeAttribute("href");
      open.setAttribute("aria-disabled", "true");
      return;
    }
    open.href = "/DemaisSetores/Consultar?patientId=" + encodeURIComponent(option.dataset.patient || "") + "&encounterId=" + encodeURIComponent(option.value);
    open.setAttribute("aria-disabled", "false");
  });
  const partner = page?.dataset.partnerOrigin;
  if (page?.dataset.embedded === "1" && partner) {
    let partnerUrl;
    try { partnerUrl = new URL(partner); } catch { partnerUrl = null; }
    const notify = function (payload) {
      if (partnerUrl && window.parent && window.parent !== window) window.parent.postMessage(payload, partnerUrl.origin);
    };
    notify({ tipo: "ds-carregado" });
    window.addEventListener("message", function (event) {
      if (!partnerUrl || event.origin !== partnerUrl.origin) return;
      if (event.source !== window.parent) return;
      if (!event.data || typeof event.data !== "object" || Array.isArray(event.data)) return;
      if (event.data.tipo === "ds-altura") return;
      if (event.data.tipo !== "ds-atendimento" || typeof event.data.codigoAtendimento !== "string") return;
      if (page.dataset.allowSwitch !== "1") { notify({ tipo: "ds-expirado" }); return; }
      const body = new FormData();
      body.append("encounterCode", event.data.codigoAtendimento.slice(0, 80));
      body.append("protecao", protection());
      const antiforgery = page.querySelector("input[name=__RequestVerificationToken]")?.value || "";
      body.append("__RequestVerificationToken", antiforgery);
      fetch(page.dataset.switchUrl, { method: "POST", body, credentials: "same-origin", headers: { "X-InovaGed-Protecao": protection(), "X-InovaGed-Contexto": page.dataset.publicId || "", RequestVerificationToken: antiforgery } }).then(function (response) {
        if (response.ok) window.location.assign(window.location.pathname);
        else notify({ tipo: "ds-expirado" });
      });
    });
  }
})();
