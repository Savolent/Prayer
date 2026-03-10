(function () {
  function apiUrl(path) {
    var p = typeof path === 'string' ? path : '';
    return p.replace(/^\/+/, '');
  }

  function buildNameRegex(names, lineStartOnly) {
    if (!Array.isArray(names) || names.length === 0) return null;
    var escaped = names
      .filter(function (n) { return typeof n === 'string' && n.trim().length > 0; })
      .map(function (n) { return n.trim().replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); })
      .sort(function (a, b) { return b.length - a.length; });
    if (escaped.length === 0) return null;
    var core = '(?:' + escaped.join('|') + ')';
    var pattern = lineStartOnly ? core + '\\b' : '\\b' + core + '\\b';
    return new RegExp(pattern, 'i');
  }

  window.buildNameRegex = buildNameRegex;
  window._galaxyMapStates = window._galaxyMapStates || new WeakMap();
  // driftT lives outside the WeakMap so it survives HTMX DOM replacement of canvas elements.
  window._starDriftT = window._starDriftT || { system: 0, galaxy: 0 };
  // Star parallax drift loop — runs continuously, advances driftT and redraws visible canvases.
  if (!window._starDriftRafRunning) {
    window._starDriftRafRunning = true;
    var _starDriftPrev = null;
    var _starDriftFrame = function (ts) {
      window.requestAnimationFrame(_starDriftFrame);
      var dt = _starDriftPrev === null ? 0 : Math.min((ts - _starDriftPrev) / 1000, 0.1);
      _starDriftPrev = ts;
      if (dt === 0) return;
      window._starDriftT.system += dt;
      window._starDriftT.galaxy += dt;
      document.querySelectorAll('.galaxy-map-canvas').forEach(function (canvas) {
        if (canvas.offsetParent === null) return; // hidden
        var state = window._galaxyMapStates.get(canvas);
        if (!state || !state.layout || !state.layout.stars || state.layout.stars.length === 0) return;
        state.driftT = window._starDriftT[state.mode] || 0;
        window.drawGalaxyMapCanvas(canvas);
      });
    };
    window.requestAnimationFrame(_starDriftFrame);
  }
  window._scriptCommandRegex = null;
  window._scriptKeywordRegex = buildNameRegex(['repeat', 'until', 'if', 'halt'], true);
  window._scriptSystemRegex = null;
  window._scriptPoiRegex = null;
  window._scriptSymbolRegex = null;
  window._haltHighlightPending = false;
  window._haltHighlightPendingUntil = 0;
  window._statePaneUiState = window._statePaneUiState || {};
  window._mapSubtabSelection = window._mapSubtabSelection || 'system';
  window._galaxyMapViewState = window._galaxyMapViewState || { panX: 0, panY: 0, hasUserPan: false, zoom: 4.5, hasUserZoom: false };
  window._tickBarState = window._tickBarState || { tick: null, observedAtMs: 0, lastPostUtcMs: null, renderPct: null, lastFrameMs: 0 };
