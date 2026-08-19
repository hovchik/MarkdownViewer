// ============================================================================
// Markdown Viewer — frontend application (vanilla JS, no build step)
// Talks to the ASP.NET Core minimal API in Program.cs.
// ============================================================================
(() => {
  "use strict";

  const API = "/api";

  /** @type {{path: string|null, name: string|null, raw: string, dirty: boolean, isEditing: boolean}} */
  const state = {
    path: null,
    name: null,
    raw: "",
    dirty: false,
    isEditing: false,
  };

  let cmInstance = null;
  let openDirs = new Set();
  let liveRenderTimer = null;
  let treeData = null;

  // ---- DOM refs -------------------------------------------------------
  const $ = (sel) => document.querySelector(sel);
  const els = {
    sidebar: $("#sidebar"),
    sidebarToggle: $("#sidebar-toggle"),
    scrim: $("#scrim"),
    treeContainer: $("#tree-container"),
    searchInput: $("#search-input"),
    searchResults: $("#search-results-container"),
    main: $("#main"),
    topbarPath: $("#topbar-path"),
    topbarMeta: $("#topbar-meta"),
    editToggleBtn: $("#edit-toggle-btn"),
    saveBtn: $("#save-btn"),
    themeToggleBtn: $("#theme-toggle-btn"),
    themePicker: $("#theme-picker"),
    themeMenu: $("#theme-menu"),
    openFileBtn: $("#open-file-btn"),
    newFileBtn: $("#new-file-btn"),
    toast: $("#toast"),
  };

  // ==========================================================================
  // Utilities
  // ==========================================================================

  function debounce(fn, ms) {
    let t;
    return (...args) => {
      clearTimeout(t);
      t = setTimeout(() => fn(...args), ms);
    };
  }

  function escapeHtml(str) {
    return str.replace(/[&<>"']/g, (c) => ({
      "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;",
    }[c]));
  }

  let toastTimer = null;
  function showToast(message, isError = false) {
    els.toast.textContent = message;
    els.toast.style.background = isError ? "var(--destructive)" : "var(--fg)";
    els.toast.classList.add("is-visible");
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => els.toast.classList.remove("is-visible"), 2600);
  }

  async function api(path, options) {
    const res = await fetch(`${API}${path}`, {
      headers: { "Content-Type": "application/json" },
      ...options,
    });
    if (!res.ok) {
      let message = `Request failed (${res.status})`;
      try {
        const body = await res.json();
        if (body?.error) message = body.error;
      } catch { /* ignore */ }
      throw new Error(message);
    }
    return res.status === 204 ? null : res.json();
  }

  function fileIconSvg() {
    return '<svg class="icon" viewBox="0 0 24 24" width="15" height="15"><path d="M13 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"/><path d="M13 2v7h7"/></svg>';
  }
  function folderIconSvg() {
    return '<svg class="icon" viewBox="0 0 24 24" width="15" height="15"><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7z"/></svg>';
  }
  function chevronSvg() {
    return '<svg class="icon chevron" viewBox="0 0 24 24" width="13" height="13"><path d="M9 6l6 6-6 6"/></svg>';
  }

  // ==========================================================================
  // Theme
  // ==========================================================================

  const THEMES = [
    { id: "light", label: "Light", dark: false, swatchBg: "#fdfbf7", swatchFg: "#15803d" },
    { id: "dark", label: "Dark", dark: true, swatchBg: "#0e1522", swatchFg: "#2fd66b" },
    { id: "sepia", label: "Sepia", dark: false, swatchBg: "#f4ecd8", swatchFg: "#15803d" },
    { id: "slate", label: "Slate", dark: true, swatchBg: "#1c2330", swatchFg: "#34d399" },
  ];

  function currentTheme() {
    return document.documentElement.getAttribute("data-theme") || "light";
  }

  function isDarkTheme(themeId) {
    return THEMES.find((t) => t.id === themeId)?.dark ?? false;
  }

  function applyTheme(theme) {
    document.documentElement.setAttribute("data-theme", theme);
    localStorage.setItem("md-viewer-theme", theme);
    const dark = isDarkTheme(theme);

    const lightLink = $("#hljs-theme-light");
    const darkLink = $("#hljs-theme-dark");
    lightLink.disabled = dark;
    darkLink.disabled = !dark;

    if (window.mermaid) {
      mermaid.initialize({ startOnLoad: false, theme: dark ? "dark" : "default", securityLevel: "loose", fontFamily: "IBM Plex Sans" });
      document.querySelectorAll(".prose").forEach(runMermaid);
    }

    if (cmInstance) {
      cmInstance.setOption("theme", dark ? "material-darker" : "default");
    }

    renderThemeMenu();
  }

  function renderThemeMenu() {
    const active = currentTheme();
    els.themeMenu.innerHTML = THEMES.map((t) => `
      <li>
        <button type="button" class="theme-menu__item${t.id === active ? " is-active" : ""}" role="menuitemradio" aria-checked="${t.id === active}" data-theme-id="${t.id}">
          <span class="theme-menu__swatch" style="--swatch-bg:${t.swatchBg};--swatch-fg:${t.swatchFg}"></span>
          ${t.label}
        </button>
      </li>`).join("");
  }

  function openThemeMenu() {
    els.themeMenu.hidden = false;
    els.themeToggleBtn.setAttribute("aria-expanded", "true");
  }
  function closeThemeMenu() {
    els.themeMenu.hidden = true;
    els.themeToggleBtn.setAttribute("aria-expanded", "false");
  }

  function initTheme() {
    const saved = localStorage.getItem("md-viewer-theme");
    const preferred = saved || (window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light");
    applyTheme(THEMES.some((t) => t.id === preferred) ? preferred : "light");
  }

  els.themeToggleBtn.addEventListener("click", (e) => {
    e.stopPropagation();
    els.themeMenu.hidden ? openThemeMenu() : closeThemeMenu();
  });
  els.themeMenu.addEventListener("click", (e) => {
    const btn = e.target.closest(".theme-menu__item");
    if (!btn) return;
    applyTheme(btn.dataset.themeId);
    closeThemeMenu();
  });
  document.addEventListener("click", (e) => {
    if (!els.themePicker.contains(e.target)) closeThemeMenu();
  });
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") closeThemeMenu();
  });

  // ==========================================================================
  // Open file (desktop shell only)
  // ==========================================================================

  const desktopHost = window.chrome && window.chrome.webview;
  if (desktopHost) {
    els.openFileBtn.hidden = false;
    els.openFileBtn.addEventListener("click", () => {
      desktopHost.postMessage(JSON.stringify({ type: "open-file" }));
    });
  }

  // ==========================================================================
  // Rendering helpers (highlight.js / mermaid / KaTeX / heading anchors)
  // ==========================================================================

  function runMermaid(container) {
    if (!window.mermaid) return;
    const nodes = container.querySelectorAll("div.mermaid, div.nomnoml");
    nodes.forEach((el) => {
      if (!el.dataset.src) el.dataset.src = el.textContent;
      el.removeAttribute("data-processed");
      el.innerHTML = el.dataset.src;
    });
    if (nodes.length) {
      try { mermaid.run({ nodes }); } catch (e) { console.warn("mermaid render failed", e); }
    }
  }

  function addHeadingAnchors(container) {
    container.querySelectorAll("h1[id], h2[id], h3[id], h4[id], h5[id], h6[id]").forEach((h) => {
      if (h.querySelector(".heading-anchor")) return;
      const a = document.createElement("a");
      a.href = `#${h.id}`;
      a.className = "heading-anchor";
      a.textContent = "#";
      a.setAttribute("aria-hidden", "true");
      h.appendChild(a);
    });
  }

  function decorateProse(container) {
    container.querySelectorAll("pre code").forEach((block) => {
      try { hljs.highlightElement(block); } catch { /* ignore unrecognized languages */ }
    });
    runMermaid(container);
    if (window.renderMathInElement) {
      renderMathInElement(container, {
        delimiters: [
          { left: "\\(", right: "\\)", display: false },
          { left: "\\[", right: "\\]", display: true },
        ],
        throwOnError: false,
      });
    }
    addHeadingAnchors(container);
  }

  // ==========================================================================
  // File tree
  // ==========================================================================

  async function loadTree() {
    treeData = await api("/tree");
    openDirs.add(""); // root always open
    renderTree();
  }

  function renderTree() {
    els.treeContainer.innerHTML = "";
    if (!treeData || !treeData.children || treeData.children.length === 0) {
      els.treeContainer.innerHTML = `<div class="empty-state" style="padding:var(--space-4)"><p>No markdown files yet. Use "New file" below to create one.</p></div>`;
      return;
    }
    els.treeContainer.appendChild(renderNodeChildren(treeData));
  }

  function renderNodeChildren(node) {
    const ul = document.createElement("ul");
    ul.className = "tree";
    for (const child of node.children) {
      ul.appendChild(child.type === "dir" ? renderDirNode(child) : renderFileNode(child));
    }
    return ul;
  }

  function renderDirNode(node) {
    const li = document.createElement("li");
    const isOpen = openDirs.has(node.path);

    const row = document.createElement("div");
    row.className = "tree-row" + (isOpen ? " is-open" : "");
    row.setAttribute("role", "button");
    row.setAttribute("tabindex", "0");
    row.innerHTML = `${chevronSvg()}${folderIconSvg()}<span class="name">${escapeHtml(node.name)}</span>`;
    row.addEventListener("click", () => {
      if (openDirs.has(node.path)) openDirs.delete(node.path);
      else openDirs.add(node.path);
      renderTree();
    });
    row.addEventListener("keydown", (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); row.click(); } });

    li.appendChild(row);
    if (isOpen) li.appendChild(renderNodeChildren(node));
    return li;
  }

  function renderFileNode(node) {
    const li = document.createElement("li");
    const row = document.createElement("div");
    row.className = "tree-row" + (node.path === state.path ? " is-active" : "");
    row.setAttribute("role", "button");
    row.setAttribute("tabindex", "0");
    row.dataset.path = node.path;
    row.innerHTML = `<span style="width:13px;display:inline-block"></span>${fileIconSvg()}<span class="name">${escapeHtml(node.name)}</span>`;
    row.addEventListener("click", () => openFile(node.path));
    row.addEventListener("keydown", (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); openFile(node.path); } });
    li.appendChild(row);
    return li;
  }

  function markActiveTreeRow() {
    els.treeContainer.querySelectorAll(".tree-row[data-path]").forEach((row) => {
      row.classList.toggle("is-active", row.dataset.path === state.path);
    });
  }

  // ==========================================================================
  // Search
  // ==========================================================================

  const runSearch = debounce(async (query) => {
    if (!query.trim()) {
      els.searchResults.hidden = true;
      els.treeContainer.hidden = false;
      return;
    }
    els.treeContainer.hidden = true;
    els.searchResults.hidden = false;

    try {
      const hits = await api(`/search?q=${encodeURIComponent(query)}`);
      renderSearchResults(hits, query);
    } catch (e) {
      els.searchResults.innerHTML = `<div class="empty-state" style="padding:var(--space-4)"><p>${escapeHtml(e.message)}</p></div>`;
    }
  }, 220);

  function highlightTerm(text, term) {
    if (!text) return "";
    const escaped = escapeHtml(text);
    const safeTerm = term.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    return escaped.replace(new RegExp(`(${safeTerm})`, "ig"), "<mark>$1</mark>");
  }

  function renderSearchResults(hits, query) {
    if (!hits.length) {
      els.searchResults.innerHTML = `<div class="sidebar__section-label">No results</div>`;
      return;
    }
    const byFile = new Map();
    for (const hit of hits) {
      if (!byFile.has(hit.path)) byFile.set(hit.path, []);
      byFile.get(hit.path).push(hit);
    }

    const frag = document.createElement("ul");
    frag.className = "search-results";
    for (const [path, fileHits] of byFile) {
      const li = document.createElement("li");
      li.className = "search-hit";
      const snippet = fileHits.find((h) => h.snippet)?.snippet || "";
      li.innerHTML = `
        <div class="search-hit__title">${highlightTerm(fileHits[0].name, query)}</div>
        ${snippet ? `<div class="search-hit__snippet">${highlightTerm(snippet, query)}</div>` : ""}
      `;
      li.addEventListener("click", () => {
        els.searchInput.value = "";
        els.searchResults.hidden = true;
        els.treeContainer.hidden = false;
        openFile(path);
      });
      frag.appendChild(li);
    }
    els.searchResults.innerHTML = "";
    els.searchResults.appendChild(frag);
  }

  els.searchInput.addEventListener("input", (e) => runSearch(e.target.value));

  // ==========================================================================
  // Main pane shells
  // ==========================================================================

  function renderEmptyShell() {
    els.main.innerHTML = `
      <div class="reader">
        <div class="empty-state">
          <svg class="icon" viewBox="0 0 24 24"><path d="M4 4h16v16H4z"/><path d="M8 9h8M8 13h5"/></svg>
          <h2>No file open</h2>
          <p>Pick a markdown file from the sidebar, or press <kbd>⌘K</kbd> to search across your workspace.</p>
        </div>
      </div>`;
  }

  function renderViewShell() {
    els.main.innerHTML = `
      <div class="reader" id="reader">
        <div class="reader__inner"><div class="prose" id="prose"></div></div>
      </div>
      <aside class="toc" id="toc" hidden>
        <div class="toc__label">On this page</div>
        <ul class="toc__list" id="toc-list"></ul>
      </aside>`;
  }

  function renderEditShell() {
    els.main.innerHTML = `
      <div class="editor-pane">
        <div class="editor-pane__source">
          <div class="pane-label">Markdown source</div>
          <textarea id="cm-host"></textarea>
        </div>
        <div class="editor-pane__preview">
          <div class="pane-label">Live preview</div>
          <div class="reader__inner"><div class="prose" id="prose-edit"></div></div>
        </div>
      </div>`;
  }

  // ==========================================================================
  // Table of contents
  // ==========================================================================

  let tocObserver = null;

  function buildToc(headings) {
    const toc = $("#toc");
    const list = $("#toc-list");
    if (!toc || !list) return;

    const usable = headings.filter((h) => h.level <= 4 && h.id);
    if (!usable.length) {
      toc.hidden = true;
      return;
    }
    toc.hidden = false;
    list.innerHTML = usable
      .map((h) => `<li><a href="#${h.id}" class="lvl-${h.level}" data-id="${h.id}">${escapeHtml(h.text)}</a></li>`)
      .join("");

    if (tocObserver) tocObserver.disconnect();
    const links = list.querySelectorAll("a");
    const reader = $("#reader");
    tocObserver = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          const link = list.querySelector(`a[data-id="${entry.target.id}"]`);
          if (!link) return;
          if (entry.isIntersecting) {
            links.forEach((l) => l.classList.remove("is-active"));
            link.classList.add("is-active");
          }
        });
      },
      { root: reader, rootMargin: "0px 0px -70% 0px", threshold: 0 }
    );
    usable.forEach((h) => {
      const el = document.getElementById(h.id);
      if (el) tocObserver.observe(el);
    });

    list.querySelectorAll("a").forEach((a) => {
      a.addEventListener("click", (e) => {
        e.preventDefault();
        const target = document.getElementById(a.dataset.id);
        target?.scrollIntoView({ behavior: "smooth", block: "start" });
      });
    });
  }

  // ==========================================================================
  // Opening / viewing a file
  // ==========================================================================

  async function openFile(path) {
    if (state.dirty && !confirm("You have unsaved changes. Discard them and open a different file?")) {
      return;
    }

    renderViewShell();
    $("#prose").innerHTML = `<div class="skeleton"><div class="bar" style="width:60%"></div><div class="bar" style="width:90%"></div><div class="bar" style="width:80%"></div></div>`;
    closeMobileSidebar();

    try {
      const data = await api(`/file?path=${encodeURIComponent(path)}`);
      state.path = data.path;
      state.name = data.name;
      state.raw = data.rawContent;
      state.dirty = false;
      state.isEditing = false;

      renderViewShell();
      const prose = $("#prose");
      prose.innerHTML = data.html;
      decorateProse(prose);
      buildToc(data.headings);

      updateTopbar(data);
      markActiveTreeRow();
      els.editToggleBtn.disabled = false;
      els.editToggleBtn.classList.remove("btn--active");
      els.saveBtn.hidden = true;
    } catch (e) {
      renderEmptyShell();
      showToast(e.message, true);
    }
  }

  function updateTopbar(data) {
    const parts = state.path.split("/");
    const dir = parts.slice(0, -1).join(" / ");
    els.topbarPath.innerHTML = dir
      ? `<span>${escapeHtml(dir)} /</span> <strong>${escapeHtml(parts.at(-1))}</strong>`
      : `<strong>${escapeHtml(parts.at(-1))}</strong>`;
    const words = data.wordCount ?? 0;
    const readMins = Math.max(1, Math.round(words / 200));
    els.topbarMeta.textContent = `${words.toLocaleString()} words · ${readMins} min read`;
  }

  // ==========================================================================
  // Edit mode
  // ==========================================================================

  function enterEditMode() {
    if (!state.path) return;
    state.isEditing = true;
    renderEditShell();

    const host = $("#cm-host");
    host.value = state.raw;
    cmInstance = CodeMirror.fromTextArea(host, {
      mode: "markdown",
      theme: isDarkTheme(currentTheme()) ? "material-darker" : "default",
      lineNumbers: true,
      lineWrapping: true,
      viewportMargin: Infinity,
      extraKeys: { Enter: "newlineAndIndentContinueMarkdownList" },
      placeholder: "Start writing…",
    });

    const previewEl = $("#prose-edit");
    renderLivePreview(state.raw, previewEl);

    cmInstance.on("change", () => {
      state.dirty = cmInstance.getValue() !== state.raw;
      els.saveBtn.disabled = !state.dirty;
      clearTimeout(liveRenderTimer);
      liveRenderTimer = setTimeout(() => renderLivePreview(cmInstance.getValue(), previewEl), 350);
    });

    els.editToggleBtn.classList.add("btn--active");
    els.saveBtn.hidden = false;
    els.saveBtn.disabled = true;
    cmInstance.focus();
  }

  async function renderLivePreview(markdown, container) {
    try {
      const data = await api("/render", { method: "POST", body: JSON.stringify({ content: markdown }) });
      container.innerHTML = data.html;
      decorateProse(container);
    } catch (e) {
      container.innerHTML = `<p style="color:var(--destructive)">${escapeHtml(e.message)}</p>`;
    }
  }

  function exitEditMode() {
    state.isEditing = false;
    if (cmInstance) {
      cmInstance.toTextArea();
      cmInstance = null;
    }
    els.editToggleBtn.classList.remove("btn--active");
    els.saveBtn.hidden = true;
    openFile(state.path);
  }

  async function saveCurrentFile() {
    if (!state.path || !cmInstance) return;
    const content = cmInstance.getValue();
    try {
      const data = await api(`/file?path=${encodeURIComponent(state.path)}`, {
        method: "PUT",
        body: JSON.stringify({ content }),
      });
      state.raw = content;
      state.dirty = false;
      els.saveBtn.disabled = true;
      showToast("Saved");
      buildTocSafely(data.headings);
      loadTree(); // sizes/timestamps may have changed
    } catch (e) {
      showToast(e.message, true);
    }
  }

  function buildTocSafely(headings) {
    // TOC only exists in view mode; in edit mode we simply skip it.
    if (!state.isEditing) buildToc(headings);
  }

  els.editToggleBtn.addEventListener("click", () => {
    if (state.isEditing) exitEditMode();
    else enterEditMode();
  });
  els.saveBtn.addEventListener("click", saveCurrentFile);

  // ==========================================================================
  // New file
  // ==========================================================================

  els.newFileBtn.addEventListener("click", async () => {
    const path = prompt("Path for the new file (e.g. notes/idea.md):", "untitled.md");
    if (!path) return;
    try {
      const result = await api("/entries", { method: "POST", body: JSON.stringify({ path, type: "file" }) });
      await loadTree();
      openDirs.add(result.path.split("/").slice(0, -1).join("/"));
      renderTree();
      openFile(result.path).then(() => enterEditMode());
    } catch (e) {
      showToast(e.message, true);
    }
  });

  // ==========================================================================
  // Mobile sidebar
  // ==========================================================================

  function openMobileSidebar() {
    els.sidebar.classList.add("is-open");
    els.scrim.classList.add("is-visible");
  }
  function closeMobileSidebar() {
    els.sidebar.classList.remove("is-open");
    els.scrim.classList.remove("is-visible");
  }
  els.sidebarToggle.addEventListener("click", () => {
    els.sidebar.classList.contains("is-open") ? closeMobileSidebar() : openMobileSidebar();
  });
  els.scrim.addEventListener("click", closeMobileSidebar);

  // ==========================================================================
  // Keyboard shortcuts
  // ==========================================================================

  document.addEventListener("keydown", (e) => {
    const mod = e.metaKey || e.ctrlKey;
    if (mod && e.key.toLowerCase() === "k") {
      e.preventDefault();
      openMobileSidebar();
      els.searchInput.focus();
    } else if (mod && e.key.toLowerCase() === "e") {
      if (state.path) {
        e.preventDefault();
        state.isEditing ? exitEditMode() : enterEditMode();
      }
    } else if (mod && e.key.toLowerCase() === "s") {
      if (state.isEditing) {
        e.preventDefault();
        saveCurrentFile();
      }
    }
  });

  window.addEventListener("beforeunload", (e) => {
    if (state.dirty) {
      e.preventDefault();
      e.returnValue = "";
    }
  });

  // ==========================================================================
  // Boot
  // ==========================================================================

  function openFromHash() {
    // Used by the Windows desktop shell (and shareable links in general): navigating to
    // #open=<relative/path.md> opens that file as soon as the tree has loaded.
    const match = location.hash.match(/^#open=(.+)$/);
    if (match) openFile(decodeURIComponent(match[1]));
  }

  initTheme();
  renderEmptyShell();
  loadTree()
    .then(openFromHash)
    .catch((e) => showToast(`Could not load workspace: ${e.message}`, true));

  window.addEventListener("hashchange", openFromHash);
})();
