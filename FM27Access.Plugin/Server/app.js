// Companion-app client script for the FM26 accessibility HTML surface.
//
// Shipped as an embedded resource in FM27Access.Plugin.dll and emitted verbatim inside
// the page's second <script> tag. The first <script> declares the only dynamic value,
// FMTOKEN (the current screen token), which this script then owns and mutates.
//
// The page is loaded once per session: a persistent shell around a swappable #fmroot
// container. Updates fetch /body and patch the container in place, restoring focus to
// the previously focused element by stable id (the DOM port of the mod's cursor
// restoration). No location.reload() anywhere.
//
// Organisation (mirrors the C# Server/Features split):
//   FM.core     - refresh/patch cycle, generic actions, shared session state, key dispatch, SSE
//   FM.dropdown - native-select dropdown machinery
//   FM.player   - in-game player-card inline disclosure
//   FM.nav      - ribbon navigation chords
//   FM.bookmarks - ribbon bookmark chords (Ctrl+1..9)
//   FM.describe - on-demand secondary text (Alt+I)
//   FM.pitch    - tactics-pitch disclosure toggle
//   FM.date     - calendar disclosure -> server-validated apply on the game's date selector
// FM.core owns the shared interaction state (dd/ddpend/player) so there is exactly one owner
// of "an interaction is in flight, defer screen refreshes". The handful of global functions at
// the bottom are the public API the server-rendered HTML calls via inline onclick/onchange;
// they delegate into the modules. FMTOKEN stays a global because the server emits it separately.

var FM = {};

// --- FM.core: infrastructure + the single owner of shared session state ---
FM.core = {
  // Patch cycle bookkeeping.
  refreshing: false,
  bodyRequest: Promise.resolve(),
  // FM26 0.2.17: an older body must not undo an optimistic checkbox press.
  // Retire the guard only after a body fetched after the activation reply lands.
  fetchSeq: 0,
  toggles: {},
  answerToggle: function (id, expected) {
    var guard = FM.core.toggles[id];
    if (!guard || expected && guard !== expected) { return; }
    guard.pending = Math.max(0, guard.pending - 1);
    guard.seq = FM.core.fetchSeq;
  },
  toggleGuarded: function (id, seq, token) {
    var guard = id && FM.core.toggles[id];
    return !!guard && guard.owner === FM.core.screenIdentity(FMTOKEN)
      && guard.owner === FM.core.screenIdentity(token || FMTOKEN)
      && (guard.pending > 0 || seq <= guard.seq);
  },
  retireToggles: function (seq) {
    Object.keys(FM.core.toggles).forEach(function (id) {
      var guard = FM.core.toggles[id];
      if (guard.owner !== FM.core.screenIdentity(FMTOKEN) || guard.pending === 0 && seq > guard.seq) {
        delete FM.core.toggles[id];
      }
    });
  },
  // A refresh requested while one is in flight is coalesced into a single follow-up
  // refresh, never dropped. pendingHard keeps a coalesced hard refresh hard: the flows that
  // need a full replace (pitch move, date pick) need it precisely because a morph leaves the
  // focused element's text stale, so the follow-up must not downgrade to a morph.
  pending: false,
  pendingHard: false,
  // pendingNeutral keeps a coalesced refresh neutral ONLY if every coalesced request was: a
  // user-initiated request folded into a background one must still restore position.
  pendingNeutral: false,
  // The pending coalesced re-read for a burst of match-surface 'restructure' events (see that
  // handler). Cancelled on a screen change so a burst cannot land on the screen after it.
  restructureTimer: null,
  // Per-screen cursor memory (the JS port of CommitCursorPosition/TryRestoreCursor):
  // before each patch the focused id is committed under the outgoing screen's token; after
  // the patch, focus restores to the same id if it survived (same screen), else to the
  // incoming screen's remembered position.
  pos: {},
  // Resolved names change page content, not its native owner or bindings.
  // Keep the complete token for requests; normalize only lifecycle comparisons.
  screenIdentity: function (token) { return (token || '').replace(/\x1Fnames:\d+$/, ''); },
  historyIndex: null,
  historyReady: false,
  historyWaiters: [],
  historyPos: {},
  historyMessagePos: {},
  historyMove: null,
  historyFocus: null,
  nativeReturn: null,
  confirmNativeArrival: function (index) {
    var origin = FM.core.nativeReturn;
    if (!origin || Date.now() > origin.until || index <= origin.from) { return null; }
    // A reused native template can rebind before its observable history index
    // advances. Intermediate bodies must not replace the departing action's
    // cursor with a surviving navigation button under that old slot.
    if (origin.id && origin.token) {
      FM.core.rememberHistoryFocus(origin.from, origin.token, origin.id, origin.message, origin.returnState);
    }
    return origin;
  },
  historyKey: function (index, token) {
    // Bindings are rebuilt on Back, while the native history slot and panel
    // destinations still identify the same page. Never use this key for actions.
    return index + '|' + (token || '').split('\x1F').filter(function (p) { return p && !/^names:/.test(p); })
      .map(function (p) { return /^\d+/.test(p) ? p.match(/^\d+/)[0] : p; }).join('/');
  },
  rememberHistoryFocus: function (index, token, id, messageId, returnState) {
    var key = FM.core.historyKey(index, token);
    FM.core.historyPos[key] = id;
    if (messageId) {
      var previous = FM.core.historyMessagePos[key];
      FM.core.historyMessagePos[key] = { message: messageId, id: id,
        returnState: returnState && returnState.message === messageId ? returnState
          : previous && previous.message === messageId ? previous.returnState : null };
    }
    else { delete FM.core.historyMessagePos[key]; }
  },
  historyTarget: function (index, token, html) {
    var key = FM.core.historyKey(index, token);
    var message = /data-fm-message-view-id="(\d+)"/.exec(html || '');
    var current = FM.core.historyMessagePos[key];
    return message && current && current.message !== message[1] ? null : FM.core.historyPos[key];
  },
  historyOriginPositions: function (origin, index) {
    // Preserve both reading-pane and Inbox-row cursors BEFORE the native reuse
    // truncates its old branch. Only native-verified provenance plus the exact
    // previously observed NewsItem can transfer these two known positions.
    if (!origin || !Number.isInteger(origin.index) || !Number.isInteger(index)
        || typeof origin.token !== 'string' || origin.token.indexOf('\x1Fmsgview:open') < 0) { return null; }
    var oldKey = FM.core.historyKey(origin.index, origin.token);
    var saved = FM.core.historyMessagePos[oldKey];
    if (!saved || saved.message !== origin.message) { return null; }
    var oldInbox = oldKey.replace(/\/msgview:open$/, '');
    return { messageKey: FM.core.historyKey(index, origin.token), message: saved,
      inboxKey: index + oldInbox.slice(oldInbox.indexOf('|')), inbox: FM.core.historyPos[oldInbox] };
  },
  restoreHistoryOrigin: function (origin) {
    if (!origin) { return; }
    FM.core.historyPos[origin.messageKey] = origin.message.id;
    FM.core.historyMessagePos[origin.messageKey] = origin.message;
    if (origin.inbox) { FM.core.historyPos[origin.inboxKey] = origin.inbox; }
  },
  beginHistory: function (direction) {
    // Native history can take 5s, followed by the existing 20s body deadline.
    // New user input cancels this handoff; it must never steal a later choice.
    FM.core.historyMove = { from: FM.core.historyIndex,
      fromKey: FM.core.historyKey(FM.core.historyIndex, typeof FMTOKEN === 'undefined' ? '' : FMTOKEN),
      direction: direction, until: Date.now() + 25000 };
    return FM.core.historyMove;
  },
  historyArrived: function (move, index, token) {
    if (!move || move.waiting || move.inFlight || index === null) { return false; }
    if (move.direction === 'back' ? index < move.from : index > move.from) { return true; }
    // FM27 can replace a native history entry in place. Its virtual Inbox,
    // message and destination share an index but retain different page owners.
    return index === move.from && !!move.fromKey && FM.core.historyKey(index, token) !== move.fromKey;
  },
  restoreHistoryFocus: function () {
    var saved = FM.core.historyFocus;
    if (!saved || saved.key !== FM.core.historyKey(FM.core.historyIndex, FMTOKEN)) { return; }
    var active = document.activeElement;
    // The exact return is a one-shot handoff. Once it has landed, a later
    // neutral morph must not refocus this old control after a reader blurs it.
    if (active && active.id === saved.id) { FM.core.historyFocus = null; return; }
    if (active !== document.body && !(active && active.hasAttribute('data-fm-history'))) { return; }
    var target = document.getElementById(saved.id);
    if (target && !target.disabled) {
      target.focus();
      if (document.activeElement === target) { FM.core.historyFocus = null; }
    }
  },
  waitForHistoryBody: function (until) {
    if (FM.core.historyReady && !FM.core.refreshing) { return Promise.resolve(); }
    // A transitional body deliberately has no history header when its native
    // owner changed during composition. Keep the old cursor memory, but do not
    // use that old index as the origin of another native history command.
    return new Promise(function (resolve, reject) {
      var unavailable = new Error('The current native history position is not ready.');
      var finish = function () {
        clearTimeout(timer);
        // A network microtask can run before an overdue timer task. Readiness
        // arriving after the deadline must not dispatch a delayed game command.
        if (Date.now() >= until) { reject(unavailable); } else { resolve(); }
      };
      var timer = setTimeout(function () {
        FM.core.historyWaiters = FM.core.historyWaiters.filter(function (f) { return f !== finish; });
        reject(unavailable);
      }, Math.max(0, until - Date.now()));
      FM.core.historyWaiters.push(finish);
      if (!FM.core.refreshing) { FM.core.refresh(); }
    });
  },
  blur: false,
  // One-shot focus override for the next refresh: an action that MOVES the thing the user is on
  // (a pitch move relocating a player) sets this to the id focus should land on after the morph,
  // taking precedence over the survived-id/remembered-position rules. Cleared when consumed.
  focusAfter: null,
  actionFocus: null,
  provisionalFocus: null,
  actionRow: null,
  revealedInput: null,
  editableInputs: function () {
    var root = document.getElementById('fmroot');
    return root ? Array.prototype.filter.call(root.querySelectorAll('input[type=text][id]:not([data-nav]),input[type=search][id],textarea[id]'), FM.core.canFocus) : [];
  },
  sectionHeading: function (el) {
    var root = document.getElementById('fmroot'), node = el;
    while (node && node.parentElement !== root) { node = node.parentElement; }
    while (node) {
      if (node.matches('[id^="fmsec"][id$="-h"]')) { return node; }
      node = node.previousElementSibling;
    }
    return null;
  },
  rememberRow: function (control) {
    var row = control && control.closest && control.closest('tr');
    if (!row) { return; }
    var controls = Array.prototype.filter.call(row.querySelectorAll('button[id],input[id],select[id],textarea[id],a[id][href],[id][tabindex]'), function (el) { return el.getClientRects().length > 0; });
    FM.core.actionRow = { owner: FM.core.screenIdentity(FMTOKEN), ids: controls.map(function (el) { return el.id; }) };
  },
  rememberActionFocus: function (targetId) {
    if (FM.msgview && FM.msgview.restoreSearchDisclosure) { FM.msgview.restoreSearchDisclosure(); }
    var active = document.activeElement, root = document.getElementById('fmroot');
    if (!active || active.id !== targetId || !root || !root.contains(active)) { FM.core.actionFocus = null; return; }
    var controls = Array.prototype.filter.call(root.querySelectorAll('button[id],input[id],select[id],textarea[id],a[id][href],[id][tabindex]'), function (el) {
      return el.getClientRects().length > 0;
    });
    var index = controls.indexOf(active), row = active.closest('tr'), heading = FM.core.sectionHeading(active);
    if (index < 0) { FM.core.actionFocus = null; return; }
    var owner = FM.core.screenIdentity(FMTOKEN);
    if (!row && active.matches('input[type=radio],[role=tab]')) { FM.core.actionRow = null; }
    var peers = row ? controls.filter(function (el) { return el !== active && row.contains(el); }) : [];
    var priorRow = !row && FM.core.actionRow && FM.core.actionRow.owner === owner ? FM.core.actionRow.ids : [];
    if (row) { FM.core.rememberRow(active); }
    var candidates = peers.concat(controls.slice(index + 1)).map(function (el) { return el.id; })
      .concat(priorRow, controls.slice(0, index).reverse().map(function (el) { return el.id; }));
    // A partial native body may temporarily omit this row. Global navigation
    // belongs to another panel and must not replace its meaningful origin.
    if (heading) { candidates = candidates.filter(function (id) { return FM.core.sectionHeading(document.getElementById(id)) === heading; }); }
    var inputs = active.tagName === 'BUTTON' && !root.querySelector('[data-fm-native-overlay]')
      ? FM.core.editableInputs().map(function (el) { return el.id; }) : null;
    if (inputs) {
      var revealed = FM.core.revealedInput;
      // Retain the opener even when Tab cancelled the initial focus handoff.
      // The following native Close can resolve the newly arrived field before
      // removing it; no delayed focus intent survives that user navigation.
      if (revealed && revealed.owner === owner && !revealed.field) {
        var added = inputs.filter(function (id) { return revealed.inputs.indexOf(id) < 0; });
        if (added.length === 1) { revealed.field = added[0]; }
      }
      if (!revealed || revealed.owner !== owner || revealed.field && !document.getElementById(revealed.field)) {
        FM.core.revealedInput = { owner: owner, field: null, opener: active.id, inputs: inputs };
      } else if (!revealed.field) {
        FM.core.revealedInput = { owner: owner, field: null, opener: active.id, inputs: inputs };
      }
    }
    var rowMove = null, rowAction = active.getAttribute('data-fm-row-action');
    if (row && rowAction) {
      var table = row.closest('table'), tableRows = table && table.tBodies[0] ? Array.from(table.tBodies[0].rows) : [];
      var rowIndex = tableRows.indexOf(row), rowHeading = row.querySelector('th[scope="row"]');
      if (table && table.id && rowIndex >= 0 && rowHeading && (rowAction === 'up' || rowAction === 'down')) {
        // Like the inherited taker handoff, names only corroborate focus.
        // Native reference guards, never this caption, authorize commands.
        rowMove = { table: table.id, index: rowIndex + (rowAction === 'up' ? -1 : 1),
          count: tableRows.length, label: rowHeading.textContent, context: row.getAttribute('data-fm-row-context'), action: rowAction };
      } else if (table && rowAction === 'delete') {
        var safe = tableRows.slice(rowIndex + 1).concat(tableRows.slice(0, rowIndex).reverse()).map(function (other) {
          return Array.from(other.querySelectorAll('input[id],select[id]')).filter(FM.core.canFocus)[0];
        }).filter(Boolean).map(function (el) { return el.id; });
        var preceding = controls.slice(0, index).filter(function (el) { return !table.contains(el); });
        if (preceding.length) { safe.push(preceding[preceding.length - 1].id); }
        candidates = safe.concat(candidates);
      }
    }
    FM.core.actionFocus = { id: active.id, owner: owner, candidates: candidates, heading: heading && heading.id, inputs: inputs, inputAttempts: 0, rowMove: rowMove,
      rowRemoval: row && rowAction === 'delete' ? { table: row.closest('table').id, index: row.sectionRowIndex, count: row.closest('tbody').rows.length } : null };
  },
  preserveCheckedValue: function (previous, next, seq, token) {
    if (!previous || previous.id !== next.id || previous.tagName !== next.tagName) { return; }
    var select = previous.tagName === 'SELECT';
    if (select) {
      // Only native state-tracked selects participate. Swept/custom table
      // values have different read lifetimes and retain their own contracts.
      if (!next.hasAttribute('data-fm-value-at') || previous.hasAttribute('data-loaded')
          || next.hasAttribute('data-loaded')) { return; }
    } else if (previous.tagName !== 'INPUT'
        || (previous.type !== 'checkbox' && previous.type !== 'radio') || previous.type !== next.type) { return; }
    if (!select && previous.type === 'checkbox' && seq != null && FM.core.toggleGuarded(previous.id, seq, token)) {
      next.checked = previous.checked;
      next.toggleAttribute('checked', previous.checked);
      return;
    }
    var observed = Number(previous.getAttribute('data-fm-value-at')) || 0;
    if (observed <= (Number(next.getAttribute('data-fm-value-at')) || 0)) { return; }
    if (select) {
      next.replaceChildren.apply(next, Array.from(previous.options).map(function (option) { return option.cloneNode(true); }));
      next.selectedIndex = previous.selectedIndex;
    } else {
      next.checked = previous.checked;
      next.toggleAttribute('checked', previous.checked);
    }
    next.setAttribute('data-fm-value-at', observed);
  },
  replaceBody: function (html, seq, token) {
    var fragment = document.createElement('template');
    fragment.innerHTML = html;
    fragment.content.querySelectorAll('input[id],select[data-fm-value-at]').forEach(function (next) {
      FM.core.preserveCheckedValue(document.getElementById(next.id), next, seq, token);
    });
    document.getElementById('fmroot').replaceChildren(fragment.content);
  },
  canFocus: function (el) {
    return el && !el.disabled && el.getAttribute('aria-disabled') !== 'true' && el.tabIndex >= 0 && el.getClientRects().length > 0;
  },
  restoreActionFocus: function (previous) {
    var saved = FM.core.actionFocus;
    if (!saved) { return; }
    if (saved.owner !== FM.core.screenIdentity(FMTOKEN)) { FM.core.actionFocus = null; return; }
    var revealed = FM.core.revealedInput;
    if (revealed && revealed.owner !== saved.owner) { FM.core.revealedInput = revealed = null; }
    // A same-screen disclosure can retain its button while revealing a search
    // field. Observe the actual new editable field, including a later native
    // binding refresh; never steal focus after the user moves away. Native
    // dialogs retain their own title/initial-focus contract.
    if (saved.inputs && saved.inputAttempts++ < 10
        && document.activeElement.id === saved.id
        && !document.querySelector('#fmroot [data-fm-native-overlay]')) {
      var added = FM.core.editableInputs().filter(function (el) { return saved.inputs.indexOf(el.id) < 0; });
      if (added.length === 1) {
        FM.core.revealedInput = { owner: saved.owner, field: added[0].id, opener: saved.id };
        FM.core.actionFocus = null;
        added[0].focus();
        return;
      }
    }
    if (previous !== saved.id || (!saved.rowMove && !saved.rowRemoval && FM.core.canFocus(document.getElementById(previous)))) { return; }
    // Restore only focus lost by this exact action's morph, never a later user
    // choice or a neutral refresh which started with no focused control.
    var active = document.activeElement;
    if (active !== document.body && active.id !== previous) { return; }
    if (saved.rowRemoval) {
      var removal = saved.rowRemoval, remainingTable = document.getElementById(removal.table);
      var remaining = remainingTable && remainingTable.tBodies[0] ? Array.from(remainingTable.tBodies[0].rows) : [];
      if (remaining.length === removal.count - 1 && remaining.length) {
        var nextRow = remaining[Math.min(removal.index, remaining.length - 1)];
        var nextInput = Array.from(nextRow.querySelectorAll('input[id],select[id]')).filter(FM.core.canFocus)[0];
        if (nextInput) { FM.core.actionFocus = null; nextInput.focus(); return; }
      }
    }
    if (saved.rowMove) {
      var move = saved.rowMove, table = document.getElementById(move.table);
      var rows = table && table.tBodies[0] ? Array.from(table.tBodies[0].rows) : [];
      var matching = rows.filter(function (row) {
        if (move.context) { return row.getAttribute('data-fm-row-context') === move.context; }
        var heading = row.querySelector('th[scope="row"]'); return heading && heading.textContent === move.label;
      });
      // Observe the moved native row in its requested position. Do not land on
      // a neighboring rule or Clear All while its reference is being replaced.
      if (rows.length === move.count && matching.length === 1 && rows[move.index] === matching[0]) {
        var buttons = Array.from(matching[0].querySelectorAll('button[data-fm-row-action]'));
        var target = buttons.filter(function (el) { return el.getAttribute('data-fm-row-action') === move.action && FM.core.canFocus(el); })[0]
          || Array.from(matching[0].querySelectorAll('input[id],select[id]')).filter(FM.core.canFocus)[0];
        if (target) { FM.core.actionFocus = null; target.focus(); return; }
      }
      if (table) { FM.core.actionFocus = null; table.tabIndex = -1; table.focus(); return; }
    }
    FM.core.actionFocus = null;
    if (revealed && revealed.field && !document.getElementById(revealed.field)) {
      FM.core.revealedInput = null;
      var opener = document.getElementById(revealed.opener);
      if (FM.core.canFocus(opener)) { opener.focus(); return; }
    }
    for (var i = 0; i < saved.candidates.length; i++) {
      var candidate = document.getElementById(saved.candidates[i]);
      if (FM.core.canFocus(candidate)) {
        FM.core.provisionalFocus = { owner: saved.owner, origin: saved.id, target: candidate.id };
        candidate.focus(); return;
      }
    }
    var heading = saved.heading && document.getElementById(saved.heading);
    if (heading) {
      FM.core.provisionalFocus = { owner: saved.owner, origin: saved.id, target: heading.id };
      heading.focus();
    }
  },
  // Dropdown/session state: id of the dropdown button (or player card) whose option list is
  // open, plus a deferred-refresh flag - a screen update must never wipe an open option list
  // mid-interaction (the deferred refresh fires on close).
  dd: null,
  ddpend: false,
  // In-game player cards: id of the card whose options panel is currently expanded, or null.
  // Only one card is ever expanded at a time; while open, dd is also set (to the card id).
  player: null,
  // Refresh self-healing. refreshFails counts consecutive failed /body fetches (reset on
  // success); each failure schedules one bounded retry so a transient error or a hung fetch
  // does not leave the page stale until the player refreshes by hand. staleSeen holds the ids
  // the values stream referenced that were missing from this DOM on the last values event: the
  // value map is rebuilt from each composed body, so an id missing on two consecutive events
  // means this page is behind the server's last compose (a restructure was suppressed, its
  // fetch failed, or a morph dropped content) and a refresh brings it current. healAt/
  // healToken/healCount rate-limit that self-heal so a server-side id bug can never turn it
  // into a refresh loop.
  refreshFails: 0,
  staleSeen: {},
  healAt: 0,
  healToken: '',
  healCount: 0,
  // Cascading context menu (the squad Actions menu) stash. The menu renders modally: /body returns
  // only the menu entries, so opening one replaces the whole page (the table leaves the DOM) and
  // closing forces a full re-render of the screen beneath - on a streamed table that is a multi-
  // second read. Instead, snapshot the page beneath the moment a menu opens and restore it instantly
  // on close, then refresh in the background so handles re-register and any value that changed under
  // the menu is morphed in. { base: screen token with the menu suffix stripped, html: #fmroot } or
  // null when no menu is open.
  menuStash: null,
  // The modal Message view's stash, same shape and lifecycle as menuStash: the list page beneath
  // an open message, restored instantly when the view closes. Owned by FM.msgview and the SSE
  // screen handler.
  msgStash: null,

  // A real screen identity change invalidates any interaction that belongs to the outgoing
  // screen. Keeping dd set here would defer the screen event forever when a match reaches half
  // time or full time with a player card/dropdown still open: there is no old-screen control left
  // for the player to close, so ddpend can never drain. Cancel an in-flight dropdown response as
  // well, otherwise it could arrive after the new body and try to populate a detached control.
  // Same-screen restructure events still use the normal ddpend path below and never call this.
  abandonInteractionForScreenChange: function () {
    FM.core.actionFocus = null;
    FM.core.actionRow = null;
    FM.core.revealedInput = null;
    if (FM.date) { FM.date.teardown(); }
    if (FM.search) { FM.search.abandon(); }
    if (FM.core.restructureTimer) {
      clearTimeout(FM.core.restructureTimer);
      FM.core.restructureTimer = null;
    }
    var p = FM.dropdown && FM.dropdown.pending;
    if (p) {
      p.cancelled = true;
      clearTimeout(p.timer);
      FM.dropdown.pending = null;
      FM.dropdown.gen++;
    }
    FM.core.dd = null;
    FM.core.ddpend = false;
    FM.core.player = null;
  },

  // Re-read the screen, unless a dropdown is open on it: wiping an open list out from under the
  // player mid-browse loses their place in it, so the re-read is parked until that resolves. How
  // every server-pushed screen change lands.
  refreshOrDefer: function (neutral) {
    if (FM.core.dd) { FM.core.ddpend = true; } else { FM.core.refresh(false, neutral); }
  },

  // Patch cycle: fetch the new body, remember which element had focus, swap the container
  // content, refocus the same stable id if it still exists.
  // hard: replace #fmroot's content outright instead of morphing - the in-page equivalent of the
  // player's F5. Used by flows where a morph has been observed to leave the focused region stale
  // for the screen reader (the pitch move: the focused grid cell's text changes under focus). A
  // replace rebuilds the reader's buffer and the follow-up focus lands on a FRESH element, so the
  // announcement is the new state. Reading position elsewhere is lost, which is why this is not
  // the default - the caller must restore position via focusAfter.
  // neutral: a background re-read (settle restructure, table-commit follow-up, staleness
  // self-heal) on a screen the player is browsing. With nothing focused, restoring "the
  // remembered position" here does not restore anything - it yanks the reader onto the last
  // control they touched (the dropdown they just committed), out of nowhere. Neutral refreshes
  // therefore never INITIATE focus: the morph alone leaves the reading position alone. A
  // genuinely focused element still keeps focus (the morph preserves it), and user-initiated
  // refreshes and screen changes keep the full restore behavior. Coalescing keeps the stronger
  // request: one non-neutral caller makes the replayed refresh non-neutral.
  refresh: function (hard, neutral) {
    // A same-screen refresh must not dismantle a live native dropdown. SSE already
    // defers its refresh requests; direct/action callers need the same contract.
    // Screen changes abandon the old interaction before reaching this method.
    if (FM.core.dd || (FM.dropdown && FM.dropdown.pending)) {
      FM.core.ddpend = true; return;
    }
    // The assistant rebinds its two priority tables over several game frames.
    // Keep action-triggered settle updates fresh while that rebind completes.
    if (FM.takers && FM.takers.settleUntil > Date.now() && document.querySelector('table[data-takers]')) { hard = true; }
    if (FM.core.refreshing || (FM.msgview && FM.msgview.opening > 0)) {
      FM.core.pendingNeutral = FM.core.pending ? (FM.core.pendingNeutral && !!neutral) : !!neutral;
      FM.core.pending = true; FM.core.pendingHard = FM.core.pendingHard || !!hard; return;
    }
    FM.core.refreshing = true;
    FM.core.dd = null; FM.core.ddpend = false; FM.core.player = null;
    // c=1 marks this as the companion's own refresh: the server notes the served body's shape so
    // the settle window can skip the restructure that would just re-deliver what this fetch shows.
    // A fetch that hangs (a wedged main-thread dispatch on the server) would otherwise leave
    // refreshing stuck true forever - every later restructure just sets pending and the page is
    // stale until a manual F5. Abort after 20s (comfortably above the route's main-thread
    // timeouts plus a full streamed-table read) so the catch below gets to retry instead.
    var ac = new AbortController();
    var abortTimer = setTimeout(function () { ac.abort(); }, 20000);
    var seq = ++FM.core.fetchSeq;
    FM.core.bodyRequest = fetch('/body?c=1', { signal: ac.signal }).then(function (r) {
      if (!r.ok) { throw new Error('HTTP ' + r.status); }
      var t = r.headers.get('X-FM-Token');
      if (t !== null) { t = decodeURIComponent(t.replace(/\+/g, ' ')); }
      var title = r.headers.get('X-FM-Title');
      var language = r.headers.get('X-FM-Language');
      var history = r.headers.get('X-FM-History-Index');
      var origin = r.headers.get('X-FM-History-Origin');
      if (origin !== null) { origin = JSON.parse(decodeURIComponent(origin.replace(/\+/g, ' '))); }
      return r.text().then(function (h) { return { h: h, t: t, title: title, language: language, origin: origin, history: history === null ? null : parseInt(history, 10) }; });
    }).then(function (b) {
      clearTimeout(abortTimer);
      // A dropdown may open while /body is in flight. Idiomorph preserves the
      // focused select's options (ignoreActiveValue), but removes its data-loaded
      // and owner attributes from the older body, leaving an uncommittable list.
      // Defer that body and re-read after commit/cancel instead of morphing it.
      if (FM.core.screenIdentity(b.t) === FM.core.screenIdentity(FMTOKEN)
          && (FM.core.dd || (FM.dropdown && FM.dropdown.pending))) {
        FM.core.ddpend = true;
        FM.core.refreshing = false;
        FM.core.pending = false;
        return;
      }
      if (b.title !== null) { document.title = decodeURIComponent(b.title.replace(/\+/g, ' ')); }
      if (b.language !== null) { document.documentElement.lang = b.language; }
      var f = document.activeElement ? document.activeElement.id : '';
      var provisional = FM.core.provisionalFocus;
      if (provisional && (provisional.owner !== FM.core.screenIdentity(FMTOKEN) || provisional.target !== f)) {
        FM.core.provisionalFocus = provisional = null;
      }
      // Automatic fallback during a partial outgoing body is not a new user
      // choice. Commit the actual action origin until navigation arrives or
      // user input chooses another position.
      var rememberedFocus = provisional ? provisional.origin : f;
      if (FM.navigator) { FM.navigator.refreshFocus = document.activeElement && document.activeElement.getAttribute('role') === 'treeitem' ? f : null; }
      // A history destination can arrive provisionally before its remembered
      // control. The surviving Back button belongs to that transition, not a
      // new cursor choice: do not overwrite the destination while it is pending.
      var pendingMove = FM.core.historyMove;
      var arriving = FM.core.historyArrived(pendingMove, FM.core.historyIndex, FMTOKEN)
        || !!(pendingMove && pendingMove.inFlight && pendingMove.fromKey
          && FM.core.historyKey(FM.core.historyIndex, FMTOKEN) !== pendingMove.fromKey);
      var historyControl = pendingMove && document.activeElement && document.activeElement.hasAttribute('data-fm-history');
      if (rememberedFocus && !arriving && !historyControl) { FM.core.pos[FM.core.screenIdentity(FMTOKEN)] = rememberedFocus; }
      if (f && !arriving && !historyControl && FM.core.historyIndex !== null) {
        var messageOwner = document.querySelector('[data-fm-message-view-id]');
        FM.core.rememberHistoryFocus(FM.core.historyIndex, FMTOKEN, rememberedFocus,
          messageOwner && messageOwner.getAttribute('data-fm-message-view-id'));
      }
      var historyTarget = null, historyReturn = false, nativeArrival = null;
      // A career load is a new history, not an implicit return from its file
      // dialog to a remembered parent in the previous career.
      if (b.h.indexOf('data-fm-native-loading') >= 0) {
        FM.core.nativeReturn = null;
        FM.core.historyPos = {};
        FM.core.historyMessagePos = {};
        if (FM.msgview) { FM.msgview.searchReturn = null; }
      }
      FM.core.historyReady = b.history !== null && !isNaN(b.history);
      if (FM.core.historyReady) {
        var originPositions = FM.core.historyOriginPositions(b.origin, b.history);
        FM.core.restoreHistoryOrigin(originPositions);
        var move = FM.core.historyMove;
        if (move && !move.waiting && Date.now() > move.until) { FM.core.historyMove = move = null; }
        var nativeReturn = FM.core.nativeReturn;
        // Native Exit/Cancel actions can pop their full-screen tool's history
        // entry without using the companion's Back button. Reuse the existing
        // exact parent handoff only for this still-current user action and a
        // previously observed destination; a provisional category shell may
        // arrive before that parent's actual panel. New input cancels both.
        if (nativeReturn && Date.now() > nativeReturn.until) { FM.core.nativeReturn = nativeReturn = null; }
        if (!move && nativeReturn && b.history > nativeReturn.from) { nativeArrival = FM.core.confirmNativeArrival(b.history); }
        if (!move && nativeReturn && b.history < nativeReturn.from
            && Object.keys(FM.core.historyPos).some(function (key) { return key.indexOf(b.history + '|') === 0; })) {
          FM.core.historyMove = move = { from: nativeReturn.from, direction: 'back', until: nativeReturn.until };
        }
        if (nativeReturn && b.history !== nativeReturn.from) { FM.core.nativeReturn = null; }
        historyReturn = FM.core.historyArrived(move, b.history, b.t || FMTOKEN);
        if (historyReturn) {
          historyTarget = FM.core.historyTarget(b.history, b.t || FMTOKEN, b.h);
          // FM27 can first expose its category shell at the new history slot.
          // Wait for the remembered panel instead of consuming the return there.
          if (!historyTarget && !Object.keys(FM.core.historyPos).some(function (key) { return key.indexOf(b.history + '|') === 0; })) {
            FM.core.historyMove = null;
          }
        } else if (!move && FM.core.historyIndex !== b.history) {
          // A newly opened history branch must not inherit another entity's
          // old cursor merely because its slot and panel type are reused.
          Object.keys(FM.core.historyPos).forEach(function (key) {
            if (parseInt(key, 10) >= b.history) {
              delete FM.core.historyPos[key];
              delete FM.core.historyMessagePos[key];
            }
          });
          FM.core.historyMove = null;
        }
        FM.core.restoreHistoryOrigin(originPositions);
        FM.core.historyIndex = b.history;
      }
      // An ordinary native Actions button can deliver the menu through its
      // action refresh before SSE announces the identity change. Observe the
      // real DOM transition too, so that arrival still captures the origin and
      // uses the same FM26 menu focus contract exactly once.
      if (FM.contextmenu && !FM.contextmenu.menu() && b.h.indexOf(' data-fm-menu') >= 0) {
        FM.contextmenu.wantFirst = true;
        if (!FM.core.menuStash) {
          FM.core.menuStash = { base: stripMenu(FMTOKEN), html: document.getElementById('fmroot').innerHTML };
        }
      }
      FM.dialog.beforeRefresh(b.h, f, b.t || FMTOKEN);
      // Morph rather than replace: Idiomorph diffs b.h against the live #fmroot and changes only
      // what differs, so the screen reader's reading position over unchanged content is left
      // alone and a focused control that survives keeps focus (and caret). ignoreActiveValue
      // stops a refresh from clobbering a value the user is mid-editing. The focus bookkeeping
      // below stays as the cross-screen fallback (morph preserves a surviving element; it does
      // not know our "element gone after navigation, go to this screen's remembered spot" rule).
      // A morph failure must never strand a half-updated page: log it and fall back to the
      // full replace, which cannot half-fail.
      if (hard) {
        FM.core.replaceBody(b.h, seq, b.t || FMTOKEN);
      } else {
        try {
          // Idiomorph also skips the active node's children for this option.
          // Protect an in-progress form edit, but let a focused command receive
          // its new caption (for example the native Play/Pause action).
          var activeEdit = document.activeElement;
          // Checked controls are native state, not an unfinished text edit.
          // Skipping their property sync can leave a focused radio unchecked
          // even though the incoming native body correctly contains checked.
          var preserveEdit = !!(activeEdit && (activeEdit.matches('input:not([type=radio]):not([type=checkbox]), textarea, select') || activeEdit.isContentEditable));
          Idiomorph.morph(document.getElementById('fmroot'), b.h, { morphStyle: 'innerHTML', ignoreActiveValue: preserveEdit,
            callbacks: { beforeNodeMorphed: function (previous, next) { FM.core.preserveCheckedValue(previous, next, seq, b.t || FMTOKEN); } } });
        } catch (err) {
          FM.core.clog('morph failed, falling back to replace: ' + (err && err.message ? err.message : err));
          FM.core.replaceBody(b.h, seq, b.t || FMTOKEN);
        }
      }
      // The commentary live region belongs to the persistent page shell, outside #fmroot. Once
      // the refreshed body is no longer a match surface, do not leave the final commentary line
      // visible on the post-match screen.
      if (!document.getElementById('fmscore')) {
        var live = document.getElementById('fmlive');
        if (live) { live.textContent = ''; }
      }
      if (b.t && FM.core.screenIdentity(b.t) !== FM.core.screenIdentity(FMTOKEN)) {
        // A late speech response belongs to the screen it was requested on.
        FM.core.speechGeneration++;
        FM.core.speechNotice(false);
      }
      if (b.t) { FMTOKEN = b.t; }
      FM.core.retireToggles(seq);
      if (FM.core.provisionalFocus && FM.core.provisionalFocus.owner !== FM.core.screenIdentity(FMTOKEN)) { FM.core.provisionalFocus = null; }
      FM.core.refreshFails = 0;
      // This DOM now reflects the server's latest compose; prior missing-id evidence is void.
      FM.core.staleSeen = {};
      // A one-shot focusAfter (set by actions that relocate the focused thing, e.g. a pitch
      // move) wins when its target exists; focusCell also fixes the grid's roving tabindex.
      // Otherwise: the survived focused id, else the screen's remembered position.
      // Consume a remembered history handoff only when the actual replacement
      // DOM contains its target. A later body can then finish a provisional
      // arrival; ordinary user input still cancels the handoff as before.
      if (historyReturn && historyTarget && document.getElementById(historyTarget)) {
        FM.core.historyFocus = { key: FM.core.historyKey(b.history, FMTOKEN), id: historyTarget };
        FM.core.historyMove = null;
      }
      var fa = FM.core.focusAfter;
      FM.core.focusAfter = null;
      // A neutral refresh with nothing focused initiates no focus of its own.
      // Completing an explicit pending Back/Forward handoff is still a restore:
      // its provisional body may have already lost the transitional focus.
      if (!(fa && FM.pitch.focusCell(fa)) && (historyReturn && historyTarget || f || !neutral)) {
        var target = historyReturn ? historyTarget : (f && document.getElementById(f)) ? f : FM.core.pos[FM.core.screenIdentity(FMTOKEN)];
        if (target) {
          var el = document.getElementById(target);
          if (el) {
            // A remembered native-menu child starts hidden in the fresh body.
            // Reuse the navigator's existing repair after its query/expansion
            // has been restored; focusing a hidden leaf here does nothing.
            if (FM.navigator && el.getAttribute('role') === 'treeitem') { FM.navigator.refreshFocus = target; }
            el.focus(); if (FM.core.blur) { el.blur(); }
          } else if (historyReturn && historyTarget
              && (document.activeElement === document.body
                || document.activeElement.hasAttribute('data-fm-history'))) {
            // The native parent can recreate a different display view without
            // the old row (or deliver that row in a later body). Identify the
            // actual matched destination rather than leaving focus on global
            // Back. Keep the pending exact target: a later native body may
            // restore it, and ordinary user input still cancels the handoff.
            var heads = document.querySelectorAll('#fmroot [id^="fmsec"][id$="-h"]');
            if (heads.length) { heads[heads.length - 1].focus(); }
          }
        }
      }
      FM.core.blur = false;
      if (!fa) { FM.core.restoreActionFocus(f); }
      // A native command can close a floating picker before its new full-screen
      // tool arrives. The intermediate parent return consumed ordinary action
      // focus, but the explicit native history intent still identifies this
      // arrival. Input already cancels that intent in the shared key handler.
      if (nativeArrival && !FM.dialog.current() && !document.querySelector('[data-fm-native-loading]')) {
        var contentHeads = Array.prototype.filter.call(document.querySelectorAll('#fmroot [id^="fmsec"][id$="-h"]'), function (heading) {
          return !document.getElementById('fmnav-tree') || heading.id !== 'fmsec0-h';
        });
        if (!contentHeads.length) { FM.core.nativeReturn = nativeArrival; }
        else if (!fa && document.activeElement === document.body) { contentHeads[contentHeads.length - 1].focus(); }
      }
      if (historyReturn && FM.msgview && FM.msgview.active()) {
        var restoredMessage = FM.core.historyMessagePos[FM.core.historyKey(b.history, FMTOKEN)];
        FM.msgview.restoreHistoryReturn(restoredMessage);
      }
      FM.core.runAfterRefresh();
      FM.core.refreshing = false;
      if (FM.core.historyReady) {
        var historyWaiters = FM.core.historyWaiters;
        FM.core.historyWaiters = [];
        historyWaiters.forEach(function (finish) { finish(); });
      }
      if (FM.core.pending) { FM.core.pending = false; var ph = FM.core.pendingHard; var pn = FM.core.pendingNeutral; FM.core.pendingHard = false; FM.core.refresh(ph, pn); }
    }).catch(function (err) {
      // A fetch failure is routine during a core reload; anything else is a real client bug that
      // was previously swallowed here with the page left stale and no trace. Report both - the
      // server log is the only place a companion-side failure can be seen.
      clearTimeout(abortTimer);
      FM.core.clog('refresh failed: ' + (err && err.message ? err.message : err));
      FM.core.refreshing = false;
      FM.core.refreshFails++;
      if (FM.core.pending) { FM.core.pending = false; var ph = FM.core.pendingHard; var pn = FM.core.pendingNeutral; FM.core.pendingHard = false; FM.core.refresh(ph, pn); }
      // No coalesced refresh to re-run: retry this one, bounded, so a transient failure heals
      // itself instead of leaving the page stale. Three strikes stops a dead server (a core
      // reload in progress) from looping - the SSE reconnect re-reads on its own once the
      // server is back.
      else if (FM.core.refreshFails <= 3) { setTimeout(function () { FM.core.refresh(hard, neutral); }, 1500); }
    });
  },

  // Report a client-side failure to the server log (fire-and-forget; the report itself must
  // never throw into the caller).
  clog: function (msg) {
    try { FM.core.requestText('/clientlog', { method: 'POST', body: msg }, 5000).catch(function () {}); } catch (_) {}
  },

  // Bound the complete response, including a body stalled after its headers.
  // This never retries a command: a lost reply cannot prove it did not execute.
  requestText: function (u, options, timeout) {
    // Rule references can be recycled while an older DOM remains. Keep the
    // native state capability from that DOM; never silently adopt a later one.
    if (/^\/(activate|settext|setvalue|step|dropdown\/open|date\/(?:cal|set))(\?|$)/.test(u) && !/[?&]context=/.test(u)) {
      var match = /[?&]id=([^&]*)/.exec(u), control = match && document.getElementById(decodeURIComponent(match[1]));
      var context = control && control.getAttribute('data-fm-command-context');
      if (context) { u += '&context=' + encodeURIComponent(context); }
    }
    // A body already harvesting native rows can scroll away this popup's
    // anchor, then BeginBody closes its session. Finish that actual compose
    // before opening. Existing pending/dd state defers further same-screen
    // refreshes; no polling delay or native command replay is introduced.
    if (FM.core.refreshing && /^\/(?:dropdown\/open|searchdropdown\/open|player\/panel)(\?|$)/.test(u)) {
      return FM.core.bodyRequest.then(function () { return FM.core.requestText(u, options, timeout); });
    }
    var ac = new AbortController();
    var timer = setTimeout(function () { ac.abort(); }, timeout || 20000);
    // Actions are non-idempotent. POST prevents automatic GET replay when a
    // persistent socket disappears after the native command was dispatched.
    return fetch(u, Object.assign({ method: 'POST' }, options || {}, { signal: ac.signal })).then(function (r) {
      if (!r.ok) { throw new Error('HTTP ' + r.status); }
      return r.text();
    }).finally(function () { clearTimeout(timer); });
  },

  command: function (u, result, failed) {
    // A local calendar must not keep deferring the result of another command.
    // Its native popup was already closed after reading the controller.
    if (FM.date && FM.date.openId && !/^\/date\/(cal|set)(\?|$)/.test(u)) { FM.date.teardown(); }
    var owner = FM.core.screenIdentity(FMTOKEN);
    var target = /[?&]id=([^&]*)/.exec(u), targetId = target ? decodeURIComponent(target[1]) : null;
    if (/^\/activate(\?|$)/.test(u)) {
      FM.core.rememberActionFocus(targetId);
    }
    if (targetId) { FM.core.rememberRow(document.getElementById(targetId)); }
    FM.dialog.rememberOrigin();
    return FM.core.requestText(u).then(function (t) { result(t); return t; }).catch(function (err) {
      if (failed) { failed(err, owner === FM.core.screenIdentity(FMTOKEN)); }
      // Query strings can contain personal form values; diagnostics need only
      // the route. A late failure must not speak over a different native screen.
      FM.core.clog(u.split('?')[0] + ' response failed: ' + (err && err.message ? err.message : err));
      if (owner === FM.core.screenIdentity(FMTOKEN)) {
        FM.core.say('The action could not be confirmed. Check the current game state before trying again.');
        // Re-read optimistic checkbox/radio state instead of leaving a rejected
        // browser choice checked. Ordinary text edits retain the existing morph.
        var active = document.activeElement;
        FM.core.refresh(!!(active && active.matches('input[type=radio],input[type=checkbox]')));
      }
    });
  },

  // A generic action that only resyncs the screen - the reply is not feedback (ribbon nav:
  // the screen change speaks for itself).
  post: function (u) {
    return FM.core.command(u, function () { FM.core.refresh(); });
  },

  // An action whose reply is feedback ONLY when it failed: an empty reply means the screen change
  // says everything (history back/forward), a non-empty one is the reason it could not (the start
  // of history, a message the mod could not bring back). Silence on failure would be
  // indistinguishable from success, which is the one thing a blind player cannot check.
  historyCommand: function (direction, send) {
    var move = FM.core.beginHistory(direction);
    move.waiting = true;
    // The native title can precede a confirmed body history index. Both native
    // buttons and inherited chords wait for that observation, then send once.
    // Later user input cancels a still-undispatched request and its handoff.
    return FM.core.waitForHistoryBody(move.until).then(function () {
      if (FM.core.historyMove !== move) { return; }
      if (Date.now() >= move.until) { throw new Error('The current native history position is not ready.'); }
      move.from = FM.core.historyIndex;
      move.fromKey = FM.core.historyKey(FM.core.historyIndex, typeof FMTOKEN === 'undefined' ? '' : FMTOKEN);
      move.waiting = false;
      move.inFlight = true;
      var sent = send();
      if (!sent || typeof sent.then !== 'function') { move.inFlight = false; return sent; }
      return sent.finally(function () {
        if (FM.core.historyMove !== move) { return; }
        // A native same-index return exposes an intermediate Inbox before the
        // verified message opens. Consume focus only AFTER that own command's
        // observed completion, then read one final authoritative body.
        move.inFlight = false;
        FM.core.refresh();
      });
    }).catch(function (error) {
      if (FM.core.historyMove !== move) { return; }
      FM.core.historyMove = null;
      FM.core.clog('history position unavailable: ' + error.message);
      FM.core.say('Navigation is still loading. Try again when the current page is ready.');
    });
  },
  failPost: function (u) {
    var history = /^\/history\?dir=(back|forward)$/.exec(u);
    var send = function () {
      return FM.core.command(u, function (t) {
        if (t) { FM.core.say(t); }
        FM.core.refresh();
      });
    };
    return history ? FM.core.historyCommand(history[1], send) : send();
  },

  // An action whose reply IS the feedback (a stepper's new value, a slider's clamped value,
  // "Set to X", or an error). The reply is always spoken, then the screen resyncs.
  sayPost: function (u) {
    return FM.core.command(u, function (t) {
      FM.core.say(t);
      FM.core.refresh();
    });
  },

  rememberNativeAction: function (id, dropdownOwner) {
    var control = document.getElementById(id), active = document.activeElement;
    var chained = dropdownOwner && control && FM.dropdown && FM.dropdown.own() === encodeURIComponent(id)
      && active && active.tagName === 'SELECT' && active.hasAttribute('data-dd')
      && active.hasAttribute('data-chain') && active.closest('#fmroot');
    var originMessage = document.querySelector('[data-fm-message-view-id]');
    var originMessageId = originMessage && originMessage.getAttribute('data-fm-message-view-id');
    FM.core.nativeReturn = control && control.hasAttribute('data-fm-history') || FM.core.historyIndex === null ? null
      : { from: FM.core.historyIndex, token: FMTOKEN,
        id: active && (active.id === id || chained) ? id : null,
        message: originMessageId,
        returnState: originMessageId && FM.msgview.returnMessageId === originMessageId
          ? FM.msgview.historyReturnState() : null,
        until: Date.now() + 25000 };
    var origin = FM.core.nativeReturn;
    if (origin && origin.id) {
      FM.core.rememberHistoryFocus(origin.from, origin.token, origin.id, origin.message, origin.returnState);
    }
  },

  act: function (id) {
    var history = document.getElementById(id);
    if (history && history.hasAttribute('data-fm-message-view-id')) { FM.msgview.beginClose(); }
    if (FM.conversation) { FM.conversation.begin(history); }
    if (document.querySelector('table[data-takers]')) { return FM.takers.panelAct(id); }
    var send = function () {
      FM.core.rememberNativeAction(id);
      var checkbox = history && history.type === 'checkbox';
      if (checkbox) {
        var guard = FM.core.toggles[id];
        if (!guard || guard.owner !== FM.core.screenIdentity(FMTOKEN)) {
          guard = FM.core.toggles[id] = { pending: 0, seq: 0, owner: FM.core.screenIdentity(FMTOKEN) };
        }
        guard.pending++;
      }
      return FM.core.command('/activate?id=' + encodeURIComponent(id) + '&screen=' + encodeURIComponent(FMTOKEN), function (t) {
        if (checkbox) { FM.core.answerToggle(id, guard); }
        if (FM.core.actResult(t)) {
          if (history && history.hasAttribute('data-fm-message-view-id')) { FM.msgview.searchReturn = null; }
          if (FM.conversation) { FM.conversation.waiting = null; }
          if (FM.core.actionFocus && FM.core.actionFocus.id === id) { FM.core.actionFocus.rowMove = null; FM.core.actionFocus.rowRemoval = null; }
        }
        FM.core.refresh(); return t;
      }, function () { if (checkbox) { FM.core.answerToggle(id, guard); } });
    };
    return history && history.hasAttribute('data-fm-history')
      ? FM.core.historyCommand(history.getAttribute('data-fm-history'), send) : send();
  },

  // A successful activation ("Activated X") is redundant - the screen reader already announces the
  // control and its new state - so only an error (any other reply) is spoken. Returns whether the
  // reply was an error, for callers that resync only on failure.
  actResult: function (t) {
    if (t && t.indexOf('Activated ') !== 0) { FM.core.say(t); return true; }
    return false;
  },

  // Select a radio button: the browser already shows the new choice (and deselects its siblings)
  // and the screen reader announces it, so a successful select speaks nothing and forces no
  // refresh - that refresh/refocus was what made radios feel stuck. If the choice changes the
  // screen, the SSE screen event refreshes on its own. Only an error (the element vanished) is
  // surfaced and resynced, so a checked radio the game rejected can never be left showing.
  actradio: function (id) {
    return FM.core.command('/activate?id=' + encodeURIComponent(id) + '&screen=' + encodeURIComponent(FMTOKEN), function (t) {
      if (FM.core.actResult(t)) { FM.core.refresh(); }
    });
  },
  settext: function (id, v) { FM.core.sayPost('/settext?id=' + encodeURIComponent(id) + '&value=' + encodeURIComponent(v) + '&screen=' + encodeURIComponent(FMTOKEN)); },
  setvalue: function (id, v) { FM.core.sayPost('/setvalue?id=' + encodeURIComponent(id) + '&value=' + encodeURIComponent(v) + '&screen=' + encodeURIComponent(FMTOKEN)); },
  step: function (id, d) {
    var active = document.activeElement;
    if (active && (active.id === id + '_up' || active.id === id + '_dn')) {
      FM.core.rememberActionFocus(active.id);
      if (FM.core.actionFocus) { FM.core.actionFocus.candidates.unshift(id); }
    }
    FM.core.sayPost('/step?id=' + encodeURIComponent(id) + '&dir=' + d + '&screen=' + encodeURIComponent(FMTOKEN));
  },

  // Keep the original Prism announcement channel. Only a failed speech request
  // creates a short error notice, following the existing persistence-notice
  // paragraph pattern. Never copy the requested description/value into the DOM,
  // replay speech or move focus; a lost response does not prove it was unspoken.
  speechGeneration: 0,
  speechNotice: function (show) {
    var note = document.getElementById('fm_speech_notice');
    if (!show) { if (note) { note.remove(); } return; }
    if (note) { return; }   // one announcement until recovery or a screen change
    var root = document.getElementById('fmroot');
    if (!root || !root.parentNode) { return; }
    note = document.createElement('p');
    note.id = 'fm_speech_notice'; note.setAttribute('role', 'alert');
    note.textContent = 'Speech could not be confirmed.';
    root.parentNode.insertBefore(note, root);
  },
  say: function (t) {
    if (!t) { return; }
    var owner = FM.core.screenIdentity(FMTOKEN), generation = ++FM.core.speechGeneration;
    FM.core.requestText('/speak?text=' + encodeURIComponent(t)).then(function () {
      if (generation === FM.core.speechGeneration && owner === FM.core.screenIdentity(FMTOKEN)) { FM.core.speechNotice(false); }
    }).catch(function (err) {
      FM.core.clog('Speech response failed: ' + (err && err.message ? err.message : String(err)));
      if (generation === FM.core.speechGeneration && owner === FM.core.screenIdentity(FMTOKEN)) { FM.core.speechNotice(true); }
    });
  },

  // Key dispatch: registered handlers run in order; the first to return true has handled the
  // event (and called preventDefault), reproducing the old single-listener early-return.
  keyHandlers: [],
  onKey: function (fn) { FM.core.keyHandlers.push(fn); },

  // Post-refresh hooks: run after each patch cycle completes (morph + focus restore), so a feature
  // can adjust focus once the new body is in the DOM. FM.messages uses this to land focus on an
  // opened email's body, which can arrive a refresh later than the activation (the settle window).
  afterRefresh: [],
  onRefresh: function (fn) { FM.core.afterRefresh.push(fn); },
  runAfterRefresh: function () {
    for (var ri = 0; ri < FM.core.afterRefresh.length; ri++) {
      try { FM.core.afterRefresh[ri](); }
      catch (error) {
        // Isolate one broken component so later focus/value hooks still run,
        // but retain the failure instead of silently hiding a mapping defect.
        FM.core.clog('afterRefresh hook ' + ri + ' failed: ' + (error && error.message ? error.message : String(error)));
      }
    }
  },
};

// Native SpeakTo questions/answers keep FM26's ordinary rich text and buttons.
// FM27 recycles answer rows between questions: focus the new question after an
// answer instead of retaining a different command at the same native path.
FM.conversation = {
  waiting: null,
  begin: function (button) {
    if (!button || !button.hasAttribute('data-fm-conversation-answer')) { return; }
    var questions = document.querySelectorAll('[data-fm-conversation-question]');
    var question = questions.length ? questions[questions.length - 1] : null;
    if (question) { FM.conversation.waiting = { id: question.id, text: question.textContent.trim() }; }
  },
  restore: function () {
    var pending = FM.conversation.waiting;
    if (!pending) { return; }
    var questions = document.querySelectorAll('[data-fm-conversation-question]');
    var question = questions.length ? questions[questions.length - 1] : null;
    // Native start/rebinding can temporarily remove both the bubble and the
    // answers. Keep the handoff through that phase; later user input cancels it.
    if (!question || !question.textContent.trim()) { return; }
    if (question.textContent.trim() !== pending.text) {
      FM.core.blur = false;
      // Keep the delivered target until the next user input. A following native
      // bind can replace a provisional plain paragraph with its rich-link one;
      // repair only that detached target, without pulling focus from a connected
      // control the user/AT has moved to. An id change alone is not a new question.
      if (!pending.target || !pending.target.isConnected) {
        question.focus();
        pending.target = question;
      }
    }
  }
};
FM.core.onRefresh(FM.conversation.restore);
// User navigation cancels a delayed handoff. The activation's own keydown is
// delivered before begin(), so it cannot cancel the command it is starting.
document.addEventListener('keydown', function () { FM.conversation.waiting = null; }, true);
document.addEventListener('pointerdown', function () { FM.conversation.waiting = null; }, true);

// --- FM.dropdown: native-select dropdown widget ---
// Closed dropdowns are real selects holding only the current value; the first interaction
// populates the live options. Browsing is completely local: nothing reaches the game until the
// user presses ENTER on an option - leaf = commit, data-sub = chain a new select for the submenu
// level, data-more = continue a capped harvest. Leaving the select any other way (Escape, Tab,
// quick-nav) cancels: the display reverts to what the game is showing, and nothing is committed.
// Server responses starting with '<' are fragments; anything else is status text.
//
// The open round trip takes ~300ms, so a load window exists between the priming keypress and
// the options landing. Exactly one open is ever in flight (pending): arrows pressed during the
// window buffer and replay onto the populated list, Escape abandons the open, and a superseded
// or no-longer-wanted response never touches the page (re-firing the open used to tear the
// focused select out of the DOM, dropping focus to <body> and silencing the screen reader).
FM.dropdown = {
  is: function (el) { return el && el.tagName === 'SELECT' && el.hasAttribute('data-dd'); },

  // Owner tag appended to every session action: the server refuses an id-carrying action when
  // its live session was opened for a different control, so a fragment built by a superseded
  // session can never act (or commit!) on the wrong dropdown. Empty only if dd is somehow
  // unset, which the server treats as "act on whatever is open" (the dev-driver contract).
  own: function () { return encodeURIComponent(FM.core.dd || ''); },
  uncertainOwner: null,

  // A lost response does not establish whether a native command ran. Keep the
  // current session cancellable and report uncertainty; never repeat a commit.
  failed: function (owner, action, err, commit) {
    FM.core.clog(action + ' response failed: ' + (err && err.message ? err.message : err));
    if (owner === FM.dropdown.own()) {
      if (commit) { FM.dropdown.uncertainOwner = FM.core.dd; }
      FM.core.say(action + ' could not be confirmed.');
    }
  },

  // The one open/expand request in flight: { id, el, gen, keys, cancelled, timer }, or null.
  // gen stamps each request so a superseded response is discarded instead of clobbering the
  // page; el backs id for player sub-selects, which carry no id.
  pending: null,
  gen: 0,

  // Start tracking an open/expand request for el. The slow cue speaks only if this request is
  // still the live one after 400ms, so normal opens stay silent.
  begin: function (el, keys) {
    var p = { id: el.id || '', el: el, gen: ++FM.dropdown.gen, keys: keys || [], cancelled: false, timer: 0 };
    p.timer = setTimeout(function () {
      if (FM.dropdown.pending === p && !p.cancelled) { FM.core.say('Loading'); }
    }, 400);
    FM.dropdown.pending = p;
    return p;
  },

  // Response gate. False = this response was superseded (a newer open owns the page) and must
  // not touch anything. True = this is the live response; pending clears and the caller
  // proceeds, including honouring p.cancelled.
  adopt: function (p) {
    clearTimeout(p.timer);
    if (FM.dropdown.pending !== p) { return false; }
    FM.dropdown.pending = null;
    return true;
  },

  // A key pressed while the options are loading: arrows/Home/End queue for replay, Escape
  // cancels the open. Enter and Space drop - committing an option the user has never heard
  // is never safe.
  buffer: function (code) {
    var p = FM.dropdown.pending;
    if (!p) { return; }
    if (code === 'Escape') { p.cancelled = true; return; }
    if ((code === 'ArrowDown' || code === 'ArrowUp' || code === 'Home' || code === 'End')
      && p.keys.length < 20) { p.keys.push(code); }
  },

  // Apply the keys buffered during the load to the freshly populated select, then confirm the
  // landed option through Prism - swapping options in under focus is silent for the screen
  // reader, so this is how the user learns the list is open and where their arrows put them.
  // No buffered keys (Enter/Space prime) confirms the current value.
  replay: function (live, keys) {
    var i = live.selectedIndex < 0 ? 0 : live.selectedIndex;
    var last = live.options.length - 1;
    for (var k = 0; k < keys.length; k++) {
      var c = keys[k];
      if (c === 'ArrowDown') { i = Math.min(i + 1, last); }
      else if (c === 'ArrowUp') { i = Math.max(i - 1, 0); }
      else if (c === 'Home') { i = 0; }
      else if (c === 'End') { i = last; }
    }
    live.selectedIndex = i;
    var o = live.options[i];
    if (o) { FM.core.say(o.text); }
  },

  // Revert a populated select to its closed state showing the unchanged game value, and return the
  // element to focus. The select is rebuilt as a FRESH node rather than having its options mutated
  // in place: after browsing, a screen reader caches the last-browsed option as the combo's value,
  // so swapping options under the same node left it announcing that stale option beside the real one
  // (the "shows two values after Escape" bug). A brand-new node forces a clean re-read of the true
  // value. A select that was never opened (no data-orig) is returned untouched.
  restore: function (s) {
    var o = s.getAttribute('data-orig');
    if (o === null) { s.removeAttribute('data-loaded'); return s; }
    var fresh = document.createElement('select');
    fresh.id = s.id;
    fresh.setAttribute('data-dd', '1');
    if (s.hasAttribute('data-fm-command-context')) { fresh.setAttribute('data-fm-command-context', s.getAttribute('data-fm-command-context')); }
    if (s.hasAttribute('data-fm-value-at')) { fresh.setAttribute('data-fm-value-at', s.getAttribute('data-fm-value-at')); }
    if (s.hasAttribute('data-card-options')) { fresh.setAttribute('data-card-options', '1'); }
    var al = s.getAttribute('aria-label');
    if (al !== null) { fresh.setAttribute('aria-label', al); }
    var op = document.createElement('option');
    op.text = o; op.selected = true; fresh.add(op);
    s.parentNode.replaceChild(fresh, s);
    return fresh;
  },

  // A live-value update for a closed dropdown's <select>: the game's selection changed under us
  // (a committed value that propagated a beat after the post-commit refresh, or a change made in
  // the game window), so swap the shown value to match. Skipped while the user is browsing the
  // open option list (data-loaded) so an interaction in flight is never disturbed, and a no-op
  // when the value already matches. Rebuilt as a FRESH node, like restore(), so the screen reader
  // re-reads the true value cleanly instead of caching the old one beside it. Preserves the id,
  // aria-label, and the data-dd/disabled nature so the rebuilt select behaves identically.
  syncValue: function (s, text, valueAt) {
    if (s.getAttribute('data-loaded')) { return; }
    var observed = Number(s.getAttribute('data-fm-value-at')) || 0;
    if (valueAt && valueAt < observed) { return; }
    if (valueAt) { s.setAttribute('data-fm-value-at', valueAt); }
    var cur = s.options.length ? s.options[0].text : '';
    if (cur === text) { return; }
    var wasActive = (document.activeElement === s);
    var fresh = document.createElement('select');
    fresh.id = s.id;
    if (s.hasAttribute('data-fm-value-at')) { fresh.setAttribute('data-fm-value-at', s.getAttribute('data-fm-value-at')); }
    if (s.hasAttribute('data-fm-command-context')) { fresh.setAttribute('data-fm-command-context', s.getAttribute('data-fm-command-context')); }
    if (s.hasAttribute('data-dd')) { fresh.setAttribute('data-dd', '1'); }
    if (s.hasAttribute('data-card-options')) { fresh.setAttribute('data-card-options', '1'); }
    if (s.hasAttribute('disabled')) { fresh.setAttribute('disabled', ''); }
    var al = s.getAttribute('aria-label');
    if (al !== null) { fresh.setAttribute('aria-label', al); }
    var op = document.createElement('option');
    op.text = text; op.selected = true; fresh.add(op);
    s.parentNode.replaceChild(fresh, s);
    if (wasActive) { fresh.focus(); }
  },

  teardown: function () {
    if (FM.date) { FM.date.teardown(); }
    var ch = document.querySelectorAll('select[data-chain]');
    for (var k = 0; k < ch.length; k++) { var p = ch[k].parentNode; p.parentNode.removeChild(p); }
    var all = document.querySelectorAll('select[data-dd][data-loaded]');
    for (var k2 = 0; k2 < all.length; k2++) {
      var s = all[k2];
      // Player sub-selects live inside a card panel that FM.player empties wholesale below.
      if (s.getAttribute('data-player-sub')) { continue; }
      FM.dropdown.restore(s);
    }
    // The player module owns its panels' DOM; let it collapse them.
    FM.player.teardownPanels();
  },

  // keepFocus: Escape cancels in place and focus stays on the (rebuilt) combobox; a focusout
  // cancel (Tab/quick-nav away) leaves focus wherever it already moved.
  cancel: function (keepFocus) {
    if (!FM.core.dd) { return; }
    var id = FM.core.dd; FM.core.dd = null;
    var uncertain = FM.dropdown.uncertainOwner === id;
    if (uncertain) { FM.dropdown.uncertainOwner = null; }
    var pid = FM.core.player;
    // Rebuild the owning combobox to the unchanged game value NOW, before the async close, so the
    // screen reader never lingers on the browsed option during the round trip. Player panels own
    // their own DOM (handled by teardown below), so they are left alone here.
    if (!pid) {
      var owner = document.getElementById(id);
      if (owner && FM.dropdown.is(owner) && owner.getAttribute('data-loaded')) {
        var fresh = FM.dropdown.restore(owner);
        if (keepFocus && fresh) { fresh.focus(); }
      }
    }
    FM.core.command('/dropdown/close?id=' + encodeURIComponent(id), function () {
      FM.dropdown.teardown();
      // A cancelled player panel removes its select, so focus would be lost; return it to
      // the card button.
      if (pid) { var b = document.getElementById(pid); if (b) { b.focus(); } }
      // A lost selection reply can follow a successful native commit. Re-read
      // its real value instead of leaving the pre-request data-orig on screen.
      // Replace only for this exceptional recovery, so a focused select cannot
      // pin that old value through ignoreActiveValue. Ordinary Escape is unchanged.
      if (uncertain) { FM.core.ddpend = false; FM.core.refresh(true); }
      else if (FM.core.ddpend) { FM.core.ddpend = false; FM.core.refresh(); }
    });
    return id;
  },

  // Remove chained selects that sit after s in document order - the server pops those levels
  // when s acts again, so their DOM must go too.
  prune: function (s) {
    var ch = document.querySelectorAll('select[data-chain]');
    for (var k = 0; k < ch.length; k++) {
      var c = ch[k];
      if (c === s) { continue; }
      if (s.compareDocumentPosition(c) & Node.DOCUMENT_POSITION_FOLLOWING) {
        var p = c.parentNode; p.parentNode.removeChild(p);
      }
    }
  },

  resolve: function (s) {
    var o = s.options[s.selectedIndex];
    if (!o) { return; }
    var owner = FM.dropdown.own();
    if (o.getAttribute('data-more')) {
      FM.core.requestText('/dropdown/more?id=' + FM.dropdown.own()).then(function (h) {
        if (owner !== FM.dropdown.own()) { return; }
        if (h.charAt(0) !== '<') { FM.core.say(h); return; }
        var first = s.options.length - 1;
        s.remove(first);
        var tmp = document.createElement('select');
        tmp.innerHTML = h;
        while (tmp.options.length) { var op = tmp.options[0]; tmp.remove(0); s.add(op); }
        s.selectedIndex = first;
        s.focus();
      }).catch(function (err) { FM.dropdown.failed(owner, 'The dropdown options', err); });
      return;
    }
    var path = s.getAttribute('data-path') || '';
    var i = o.value;
    if (o.getAttribute('data-sub')) {
      FM.core.requestText('/dropdown/expand?path=' + path + '&index=' + i + '&id=' + FM.dropdown.own()).then(function (h) {
        if (owner !== FM.dropdown.own()) { return; }
        if (h.charAt(0) !== '<') { FM.core.say(h); return; }
        FM.dropdown.prune(s);
        var cp = path ? path + '.' + i : '' + i;
        var c = document.createElement('select');
        c.setAttribute('data-dd', '1');
        c.setAttribute('data-loaded', '1');
        c.setAttribute('data-chain', '1');
        c.setAttribute('data-path', cp);
        c.setAttribute('aria-label', 'submenu');
        c.innerHTML = h;
        var np = document.createElement('p');
        np.appendChild(c);
        var host = s.parentNode;
        host.parentNode.insertBefore(np, host.nextSibling);
        c.focus();
      }).catch(function (err) { FM.dropdown.failed(owner, 'The dropdown options', err); });
      return;
    }
    FM.dialog.rememberOrigin(FM.core.dd);
    FM.core.rememberRow(document.getElementById(FM.core.dd));
    // A native dropdown leaf can navigate just like a button. Its focused
    // chained select has no stable id; retain the verified owning root before
    // teardown/transport, using the same native history handoff as buttons.
    FM.core.rememberNativeAction(FM.core.dd, true);
    FM.core.requestText('/dropdown/select?path=' + path + '&index=' + i + '&id=' + FM.dropdown.own()).then(function (t) {
      if (owner !== FM.dropdown.own()) { return; }
      FM.dropdown.uncertainOwner = null;
      // Confirm the choice through Prism ("<option> selected"), the spoken counterpart to the
      // screen-reader-silent commit. An error reply ("That option is no longer available") speaks
      // the same way - it is the feedback for a failed commit.
      FM.core.say(t);
      // A player sub-select commit ends the root session, so re-open the panel in place with
      // fresh values rather than refreshing (and collapsing) the whole screen.
      if (FM.core.player) {
        var card = FM.core.player;
        FM.core.dd = null; FM.core.ddpend = false;
        FM.player.panelRefresh(card);
        return;
      }
      var id = FM.core.dd; FM.core.dd = null; FM.core.ddpend = false;
      var rb = id ? document.getElementById(id) : null;
      if (FM.takers.addCommit(rb)) { return; }
      // A native card swap replaces the content around this same control.
      // Reuse the normal dropdown teardown and position memory, but retain
      // focus so the new card has a meaningful keyboard origin after refresh.
      if (rb && rb.hasAttribute('data-card-options')) {
        FM.dropdown.teardown();
        FM.core.pos[FM.core.screenIdentity(FMTOKEN)] = id;
        FM.core.blur = false;
        FM.core.refresh();
        return;
      }
      // A dropdown inside a table can rebind its whole ROW, not just its own displayed value:
      // match-day squad selection turns "Choose" into a bench slot and swaps the two player names,
      // taking roles, stars, status and the numeric columns with them. That makes the commit a
      // different shape of update from an ordinary one, handled below and again after it settles.
      // Both stay MORPHS: rebuilding #fmroot would drop a screen reader into forms mode on the last
      // combobox instead of leaving its browse position alone.
      var tableCommit = !!(rb && rb.closest && rb.closest('tr'));
      var inMatchTable = tableCommit && !!document.getElementById('fmscore');
      if (rb && (rb !== s || tableCommit)) {
        // Two commits that must NOT be holding focus when the morph runs, for the same reason.
        //
        // Submenu (hierarchical): the committed leaf lives in a chained CHILD select, while the
        // owning ROOT select (rb) was left parked on the category that was drilled into, with the
        // chained selects still in the DOM. The morph replaces a select's options but cannot undo
        // that browsed state if rb is the focused active element, so the root would keep showing the
        // category until a later refresh (changing another control on the page — the user-seen
        // workaround).
        //
        // In a table: the morph preserves a focused select, and preserving it pins the row it sits
        // in, so a row the game has already rewritten stays on the page reading the old player.
        //
        // Either way, tear the browsing DOM down and DON'T focus rb before the refresh, so the morph
        // reconciles it from the game's committed value; restore focus afterwards via the remembered
        // position.
        FM.dropdown.teardown();
        FM.core.pos[FM.core.screenIdentity(FMTOKEN)] = id; FM.core.blur = true;
      } else if (rb) {
        // Flat dropdown outside a table: the owning select IS the one just browsed to the committed
        // value and nothing around it changes, so focus it before the refresh (the morph keeps a
        // focused select's value) to restore position beside it, then blur after restore so the user
        // isn't left inside the combo box.
        rb.focus(); FM.core.blur = true;
      }
      FM.core.refresh();
      if (tableCommit && !inMatchTable) {
        // Team-selection bindings finish rebinding well after the dropdown event returns (the
        // goalkeeper row has been observed taking nearly two seconds). The immediate morph may
        // therefore still read the previous player. Do one bounded final morph after that window;
        // blur a table control first so Idiomorph is free to update its value and surrounding row.
        // The token gate prevents a late timer from disturbing a screen the player already left.
        var tableCommitToken = FMTOKEN;
        setTimeout(function () {
          if (FMTOKEN !== tableCommitToken) { return; }
          // Only the COMMITTED dropdown needs freeing for the morph (ignoreActiveValue pins the
          // focused element); blurring whatever else the player has since moved to - or flashing
          // focus back onto the combobox via the restore - was itself the jarring focus loss this
          // follow-up used to cause. Neutral: with nothing focused, the morph updates the row and
          // the reading position stays wherever the player is browsing.
          var active = document.activeElement;
          // This follow-up belongs to the preceding commit. A newly loading
          // or browsed dropdown owns its focus, even when it reuses this id.
          if (active && active.id === id && !FM.dropdown.pending && !FM.core.dd) { active.blur(); }
          FM.core.refresh(false, true);
        }, 2000);
      }
    }).catch(function (err) { FM.dropdown.failed(owner, 'The dropdown selection', err, true); });
  },

  // Left arrow: dig out one submenu level. Focus the parent BEFORE removing the chained select
  // so the focusout handler sees a dropdown-to-dropdown move and doesn't resolve or cancel.
  back: function (s) {
    if (!s.getAttribute('data-chain')) { return; }
    var owner = FM.dropdown.own();
    var path = s.getAttribute('data-path') || '';
    var pp = path.indexOf('.') >= 0 ? path.substring(0, path.lastIndexOf('.')) : '';
    FM.core.requestText('/dropdown/collapse?path=' + pp + '&id=' + FM.dropdown.own()).then(function (t) {
      if (owner !== FM.dropdown.own()) { return; }
      var all = document.querySelectorAll('select[data-dd][data-loaded]');
      var parent = null;
      for (var k = 0; k < all.length; k++) {
        if (all[k] !== s && (all[k].getAttribute('data-path') || '') === pp) { parent = all[k]; }
      }
      if (parent) { parent.focus(); }
      var p = s.parentNode; p.parentNode.removeChild(p);
    }).catch(function (err) { FM.dropdown.failed(owner, 'The dropdown level', err); });
  },

  // Populate on first interaction, NOT on focus: focus alone (C quick-nav passing through, or
  // the post-refresh focus restore) must not open game popups. The priming keypress is
  // consumed into the replay buffer; keys pressed during the load buffer too.
  load: function (s, keys) {
    var p = FM.dropdown.begin(s, keys);
    FM.core.requestText('/dropdown/open?id=' + encodeURIComponent(p.id) + '&screen=' + encodeURIComponent(FMTOKEN)).then(function (h) {
      if (!FM.dropdown.adopt(p)) { return; }
      if (h.charAt(0) !== '<') { FM.core.say(h); return; }
      // The server opened a session, but the page may have moved on: the user escaped, focus
      // left, or a refresh replaced the select (a fresh render mints new ids - hence the
      // re-resolve by id rather than the closure node). Close the orphaned game popup instead
      // of populating a control the user is no longer on.
      var live = document.getElementById(p.id);
      if (p.cancelled || !live || !FM.dropdown.is(live) || document.activeElement !== live) {
        FM.core.command('/dropdown/close?id=' + encodeURIComponent(p.id), function () {});
        return;
      }
      // The server closed any previous session when this one opened; mirror that in the DOM
      // before adopting the new session.
      FM.dropdown.teardown();
      FM.dropdown.uncertainOwner = null;
      FM.core.dd = live.id;
      var cur = live.options[live.selectedIndex] ? live.options[live.selectedIndex].text : '';
      live.setAttribute('data-orig', cur);
      live.innerHTML = h;
      live.setAttribute('data-loaded', '1');
      live.setAttribute('data-path', '');
      FM.dropdown.replay(live, p.keys);
    }).catch(function () {
      if (FM.dropdown.adopt(p)) {
        // A failed reply can follow a successful native popup open. Close
        // only this owner, so an unseen menu cannot block the next action.
        FM.core.command('/dropdown/close?id=' + encodeURIComponent(p.id), function () {});
        FM.core.say('The dropdown could not be confirmed.');
      }
    });
  },

  // Priming an unpopulated select: a player sub-select expands its submenu; any other is a
  // standalone dropdown that opens its own session. While a load is already in flight for
  // this select, further presses buffer instead of re-firing the open.
  prime: function (s, code) {
    var p = FM.dropdown.pending;
    if (p && (p.el === s || (p.id && p.id === s.id))) { FM.dropdown.buffer(code); return; }
    var keys = (code === 'ArrowDown' || code === 'ArrowUp' || code === 'Home' || code === 'End') ? [code] : [];
    if (s.getAttribute('data-player-sub')) { FM.player.subload(s, keys); } else { FM.dropdown.load(s, keys); }
  },

  // Key handler (registered with FM.core.onKey). Returns true once it has handled the event.
  key: function (e) {
    // Global chords must reach their handlers while focus remains on a select
    // after committing or saving (Alt+Enter continues the game).
    if (e.altKey || e.ctrlKey || e.metaKey) { return false; }
    // First keypress on an unpopulated dropdown select primes it (consumed into the replay
    // buffer); presses while the options load buffer via prime, and Escape abandons the open.
    if (FM.dropdown.is(e.target) && !e.target.getAttribute('data-loaded')) {
      if (e.code === 'ArrowDown' || e.code === 'ArrowUp' || e.code === 'Home' || e.code === 'End'
        || e.code === 'Enter' || e.code === 'Space') { e.preventDefault(); FM.dropdown.prime(e.target, e.code); return true; }
      var lp = FM.dropdown.pending;
      if (e.code === 'Escape' && lp && (lp.el === e.target || (lp.id && lp.id === e.target.id))) {
        e.preventDefault(); FM.dropdown.buffer('Escape'); return true;
      }
    }
    // Enter on a populated dropdown select is the only commit: it resolves the current option
    // (leaf commit, submenu chain, or load-more). Escape cancels the whole session without
    // committing, reverting the display to what the game is showing.
    if (FM.dropdown.is(e.target) && e.target.getAttribute('data-loaded')) {
      if (e.code === 'Enter') { e.preventDefault(); FM.dropdown.resolve(e.target); return true; }
      if (e.code === 'Escape') { e.preventDefault(); FM.dropdown.cancel(true); return true; }
      // Right digs into a submenu option, Left digs out one level. Both are suppressed
      // otherwise - natively they'd move the selection, identical to Up/Down, and an
      // accidental sideways selection change is just noise.
      if (e.code === 'ArrowRight' && !e.altKey) {
        e.preventDefault();
        var so = e.target.options[e.target.selectedIndex];
        if (so && so.getAttribute('data-sub')) { FM.dropdown.resolve(e.target); }
        return true;
      }
      if (e.code === 'ArrowLeft' && !e.altKey) { e.preventDefault(); FM.dropdown.back(e.target); return true; }
    }
    return false;
  },
};

// --- FM.search: searchable dropdown as an ARIA editable combobox ---
// One search box (role=combobox) that filters the game's huge list into the listbox below it.
// Typing (debounced) opens the game's search popup once, then re-filters per query via
// /searchdropdown/filter, rebuilding the listbox in place. Focus never leaves the box: Down/Up move
// a highlight through the options via aria-activedescendant (so the screen reader announces each),
// and ENTER commits the highlighted option. Escape or leaving the box cancels the session and
// closes the game popup. Reuses FM.core.dd as the single in-flight-session flag, so SSE refreshes
// defer the same way. The listbox items are not focusable - the box owns all the keys.
FM.search = {
  is: function (el) { return el && el.tagName === 'INPUT' && el.hasAttribute('data-sdd'); },
  listbox: function (input) { return document.getElementById(input.id + '-lb'); },
  options: function (input) { var lb = FM.search.listbox(input); return lb ? lb.querySelectorAll('[role="option"]') : []; },
  timer: null,
  tail: Promise.resolve(),
  intent: 0,
  session: null,
  enqueue: function (work) {
    var next = FM.search.tail.then(work);
    FM.search.tail = next.catch(function (err) { FM.core.clog('Search interaction failed: ' + err.message); });
    return FM.search.tail;
  },
  job: function (input) { return { input: input, owner: FM.core.screenIdentity(FMTOKEN), token: FMTOKEN, intent: ++FM.search.intent }; },
  current: function (job) {
    return job.intent === FM.search.intent && job.owner === FM.core.screenIdentity(FMTOKEN)
      && job.input.isConnected && document.getElementById(job.input.id) === job.input;
  },
  request: async function (url) {
    var text, error;
    await FM.core.command(url, function (t) { text = t; }, function (err) { error = err; });
    if (error) { throw error; }
    return text;
  },
  // A failed reply may follow a successful selection. Close only that native
  // session, then observe the real value; never repeat a selection automatically.
  recover: async function (job) {
    if (FM.search.session && FM.search.session.input === job.input) { FM.search.session = null; }
    await FM.core.command('/searchdropdown/close?id=' + encodeURIComponent(job.input.id), function () {});
    if (!FM.search.current(job)) { return; }
    FM.search.clearList(job.input); FM.core.dd = null; FM.core.ddpend = false;
    FM.core.refresh(true);
  },
  abandon: function () {
    clearTimeout(FM.search.timer); FM.search.timer = null;
    FM.search.intent++; FM.search.session = null;
  },

  // Debounced input handler (oninput). Each settled keystroke runs a filter; the first opens the
  // session. Capturing the value at schedule time keeps the request honest if more keys arrive.
  onInput: function (input) {
    if (FM.search.timer) { clearTimeout(FM.search.timer); }
    FM.search.intent++;
    FM.search.clearList(input); // old native indices must not survive a new query
    var q = input.value;
    FM.search.timer = setTimeout(function () { FM.search.run(input, q, false); }, 300);
  },

  // Open the session if needed, then filter. focusFirst highlights the first result once loaded
  // (used by the Down arrow opening an empty box, so the list appears already navigable).
  run: function (input, q, focusFirst) {
    clearTimeout(FM.search.timer); FM.search.timer = null;
    var job = FM.search.job(input);
    FM.core.dd = input.id;
    return FM.search.enqueue(async function () {
      if (!FM.search.current(job)) { return; }
      try {
        var session = FM.search.session;
        if (!session || session.input !== input || session.owner !== job.owner) {
          var t = await FM.search.request('/searchdropdown/open?id=' + encodeURIComponent(input.id) + '&screen=' + encodeURIComponent(job.token));
          if (t) { if (FM.search.current(job)) { FM.core.say(t); } await FM.search.recover(job); return; }
          FM.search.session = { input: input, owner: job.owner };
          if (FM.search.current(job)) { FM.dropdown.teardown(); FM.core.dd = input.id; }
        }
        if (!FM.search.current(job)) { return; }
        await FM.search.filter(input, q, focusFirst, job);
      } catch (err) { await FM.search.recover(job); }
    });
  },

  filter: async function (input, q, focusFirst, job) {
      var h = await FM.search.request('/searchdropdown/filter?q=' + encodeURIComponent(q) + '&id=' + encodeURIComponent(input.id));
      if (!FM.search.current(job) || input.value !== q) { return; }
      var lb = FM.search.listbox(input);
      if (!lb) { return; }
      input.setAttribute('data-search-query', q);
      if (h.charAt(0) !== '<') { FM.search.clearList(input); FM.core.say(h); return; }
      lb.innerHTML = h;
      var opts = lb.querySelectorAll('[role="option"]');
      for (var i = 0; i < opts.length; i++) { opts[i].id = input.id + '-lb-o' + i; }
      lb.hidden = false;
      input.setAttribute('aria-expanded', 'true');
      // A new result set invalidates any prior highlight; start fresh.
      FM.search.setActive(input, focusFirst && opts.length ? 0 : -1);
      if (!focusFirst) { FM.core.say(opts.length + ' results'); }
  },

  // Move the highlight to option i (-1 = none) by repointing aria-activedescendant, the only thing
  // the screen reader follows while focus stays in the box.
  setActive: function (input, i) {
    var opts = FM.search.options(input);
    for (var k = 0; k < opts.length; k++) { opts[k].classList.remove('fm-active'); }
    if (i < 0 || i >= opts.length) { input.removeAttribute('aria-activedescendant'); return; }
    var o = opts[i];
    o.classList.add('fm-active');
    input.setAttribute('aria-activedescendant', o.id);
    if (o.scrollIntoView) { o.scrollIntoView({ block: 'nearest' }); }
  },

  activeIndex: function (input) {
    var id = input.getAttribute('aria-activedescendant');
    if (!id) { return -1; }
    var o = document.getElementById(id);
    return o ? parseInt(o.getAttribute('data-index'), 10) : -1;
  },

  move: function (input, delta) {
    var opts = FM.search.options(input);
    if (!opts.length) {
      // Nothing loaded yet: Down opens the list (filtering on whatever is typed, even empty) and
      // lands on the first result.
      if (delta > 0) { FM.search.run(input, input.value, true); }
      return;
    }
    var cur = FM.search.activeIndex(input);
    var next = cur < 0 ? (delta > 0 ? 0 : opts.length - 1) : cur + delta;
    if (next < 0) { next = 0; }
    if (next >= opts.length) { next = opts.length - 1; }
    FM.search.setActive(input, next);
  },

  commit: function (input) {
    clearTimeout(FM.search.timer); FM.search.timer = null;
    if (input.getAttribute('data-search-query') !== input.value) { FM.search.run(input, input.value, true); return; }
    var i = FM.search.activeIndex(input);
    if (i < 0) { return; } // nothing highlighted
    var job = FM.search.job(input), query = input.value;
    return FM.search.enqueue(async function () {
      if (!FM.search.current(job) || input.value !== query) { return; }
      try {
      var t = await FM.search.request('/searchdropdown/select?index=' + i + '&id=' + encodeURIComponent(input.id));
      FM.search.session = null;
      if (!FM.search.current(job)) { return; }
      FM.core.say(t); // confirm the choice through Prism ("<option> selected")
      FM.core.dd = null; FM.core.ddpend = false;
      // Show the committed choice in the box NOW. The refresh below morphs with
      // ignoreActiveValue while this box holds focus, so the server-rendered value can never
      // reach a focused input - the box stayed visibly empty until a manual F5. The label is
      // taken from the server's own "<label> selected" confirmation, never guessed; any other
      // reply is an error and the box is left untouched.
      if (t && t.length > 9 && t.lastIndexOf(' selected') === t.length - 9) {
        input.value = t.slice(0, -9);
        input.setAttribute('value', input.value);
        FM.search.clearList(input);
      }
      // Focus the box (stable id) so the refresh restores position there, then blur after restore
      // so the user isn't left inside the widget.
      input.focus(); FM.core.blur = true;
      FM.core.refresh();
      } catch (err) { await FM.search.recover(job); }
    });
  },

  clearList: function (input) {
    var lb = FM.search.listbox(input);
    if (lb) { lb.innerHTML = ''; lb.hidden = true; }
    input.setAttribute('aria-expanded', 'false');
    input.removeAttribute('aria-activedescendant');
  },

  cancel: function (input) {
    if (input) { FM.search.clearList(input); }
    var owner = input || (FM.core.dd && document.getElementById(FM.core.dd));
    if (!owner || !FM.search.is(owner)) { return; } // not a search session
    if (!FM.search.timer && FM.core.dd !== owner.id && (!FM.search.session || FM.search.session.input !== owner)) { return; }
    clearTimeout(FM.search.timer); FM.search.timer = null;
    var job = FM.search.job(owner);
    // As in FM26, closing the list does not replace the native search text with
    // a previous choice. FM27 keeps that query independently of selection.
    return FM.search.enqueue(async function () {
      try { await FM.search.request('/searchdropdown/close?id=' + encodeURIComponent(owner.id)); }
      catch (err) { await FM.search.recover(job); return; }
      if (FM.search.session && FM.search.session.input === owner) { FM.search.session = null; }
      if (!FM.search.current(job)) { return; }
      FM.core.dd = null;
      FM.core.ddpend = false; FM.core.refresh();
    });
  },

  // Key handler (registered with FM.core.onKey). All keys are handled on the combobox input, where
  // focus stays the whole time.
  key: function (e) {
    if (!FM.search.is(e.target)) { return false; }
    if (e.altKey || e.ctrlKey || e.metaKey) { return false; } // leave nav chords to FM.nav
    if (e.code === 'ArrowDown') { e.preventDefault(); FM.search.move(e.target, 1); return true; }
    if (e.code === 'ArrowUp') { e.preventDefault(); FM.search.move(e.target, -1); return true; }
    if (e.code === 'Enter') { e.preventDefault(); FM.search.commit(e.target); return true; }
    if (e.code === 'Escape') { e.preventDefault(); FM.search.cancel(e.target); return true; }
    return false;
  },
};

// --- FM.player: in-game player cards (inline disclosure) ---
// The card is a button; activating it opens the card's child dropdown session and injects the
// whole popup inline into the card's panel. Each root row is one of: a read-only status
// indicator (body language, condition) as a text line; an actionable leaf (the player's name,
// Player Instructions) as a button that commits the root selection; or a selectable sub-menu
// (Substitute, In/Out-of-possession role, ...) as its own lazy select. A sub-select opens its
// submenu via /dropdown/expand on first interaction, then runs the normal data-dd machinery.
// Only one card's panel is open at a time (single server session; the client tears down any
// other open panel first). Committing a sub-menu value refreshes the panel in place (re-reads
// fresh values); committing an actionable leaf navigates, so the screen refreshes instead.
FM.player = {
  toggle: function (id) {
    if (FM.core.player === id) { FM.player.collapse(id); return; }
    if (!FM.core.dd) { FM.player.open(id); return; }
    // Some other session is open (another card, or a stray dropdown): close it, then open this.
    FM.core.dd = null; FM.core.player = null;
    FM.core.command('/dropdown/close', function () {
      FM.dropdown.teardown();
      FM.player.open(id);
    });
  },

  open: function (id) {
    FM.core.requestText('/player/panel?id=' + encodeURIComponent(id)).then(function (h) {
      if (h.charAt(0) !== '<') { FM.core.say(h); return; }
      var panel = document.getElementById(id + '-panel');
      if (!panel) { return; }
      panel.innerHTML = h;
      FM.core.player = id;
      FM.core.dd = id;
      var btn = document.getElementById(id);
      if (btn) { btn.setAttribute('aria-expanded', 'true'); btn.focus(); }
    }).catch(function (err) { FM.core.clog('Player menu response failed: ' + err.message); FM.core.say('The player menu could not be confirmed.'); });
  },

  // Re-read the open panel in place after a commit: fresh values, card stays expanded, focus
  // returns to the card button.
  panelRefresh: function (id) {
    FM.core.requestText('/player/panel?id=' + encodeURIComponent(id)).then(function (h) {
      var panel = document.getElementById(id + '-panel');
      if (!panel) { FM.core.player = null; FM.core.dd = null; return; }
      if (h.charAt(0) !== '<') { panel.innerHTML = ''; FM.core.player = null; FM.core.dd = null; FM.core.say(h); return; }
      panel.innerHTML = h;
      FM.core.player = id;
      FM.core.dd = id;
      var b = document.getElementById(id);
      if (b) { b.focus(); }
    }).catch(function (err) { FM.dropdown.failed(encodeURIComponent(id), 'The player menu', err); });
  },

  collapse: function (id) {
    FM.core.dd = null; FM.core.player = null;
    FM.core.command('/dropdown/close?id=' + encodeURIComponent(id), function () {
      var panel = document.getElementById(id + '-panel');
      if (panel) { panel.innerHTML = ''; }
      var btn = document.getElementById(id);
      if (btn) { btn.setAttribute('aria-expanded', 'false'); btn.focus(); }
      if (FM.core.ddpend) { FM.core.ddpend = false; FM.core.refresh(); }
    });
  },

  key: function (e) {
    if (e.code !== 'Escape' || e.altKey || e.ctrlKey || e.metaKey || !FM.core.player) { return false; }
    var id = FM.core.player;
    var panel = document.getElementById(id + '-panel');
    // Loaded sub-selects retain their existing Escape handling first. The
    // inline root also needs an exit after a commit returns focus to its card.
    if (!e.target || (e.target.id !== id && !(panel && panel.contains(e.target)))) { return false; }
    e.preventDefault(); FM.player.collapse(id); return true;
  },

  // An actionable leaf (the player's name, Player Instructions) commits the root-level
  // selection: the game fires its selection event (opening the player report / instructions
  // page) and ends the session, so clear the session state and let the resulting navigation
  // drive a full screen refresh. No path: these are root choices.
  select: function (idx) {
    if (!FM.core.dd) { return; }
    // Inline leaves have no stable id after the panel is torn down. Commit
    // the card as the outgoing history/focus anchor before native navigation.
    var card = document.getElementById(FM.core.player || FM.core.dd);
    if (card) { card.focus(); FM.core.rememberNativeAction(card.id); }
    var own = encodeURIComponent(FM.core.dd);
    FM.core.dd = null; FM.core.player = null; FM.core.ddpend = false;
    FM.core.command('/dropdown/select?path=&index=' + idx + '&id=' + own, function (t) {
      FM.core.say(t);
      FM.core.refresh();
    });
  },

  // A player sub-select opens its own submenu (a child of the already-open root session) on
  // first interaction, rather than starting a new session like a standalone dropdown. Shares
  // the dropdown pending machinery (single flight, buffered keys, replay), but a discarded
  // expand needs no close: the root session stays open and the abandoned child level is
  // popped by the next interaction (ResolveAndPop).
  subload: function (s, keys) {
    var p = FM.dropdown.begin(s, keys);
    // One sub-select open at a time: collapse any other loaded sub-select and remove any
    // chained submenu selects left from a previous one.
    var ch = document.querySelectorAll('select[data-chain]');
    for (var k = 0; k < ch.length; k++) { var pn = ch[k].parentNode; pn.parentNode.removeChild(pn); }
    var loaded = document.querySelectorAll('select[data-player-sub][data-loaded]');
    for (var k2 = 0; k2 < loaded.length; k2++) { if (loaded[k2] !== s) { FM.dropdown.restore(loaded[k2]); } }
    var idx = s.getAttribute('data-index');
    FM.core.requestText('/dropdown/expand?path=&index=' + idx + '&id=' + FM.dropdown.own()).then(function (h) {
      if (!FM.dropdown.adopt(p)) { return; }
      if (h.charAt(0) !== '<') { FM.core.say(h); return; }
      if (p.cancelled || !s.isConnected) { return; }
      var cur = s.options[s.selectedIndex] ? s.options[s.selectedIndex].text : '';
      s.setAttribute('data-orig', cur);
      s.innerHTML = h;
      s.setAttribute('data-loaded', '1');
      s.setAttribute('data-path', idx);
      FM.dropdown.replay(s, p.keys);
    }).catch(function () {
      if (FM.dropdown.adopt(p)) { FM.core.say('The submenu did not open.'); }
    });
  },

  // Collapse every open player panel and reset the shared player flag. Called by
  // FM.dropdown.teardown (the panels are torn down together with the dropdown session).
  teardownPanels: function () {
    var panels = document.querySelectorAll('.fm-player-panel');
    for (var pj = 0; pj < panels.length; pj++) {
      if (panels[pj].innerHTML === '') { continue; }
      panels[pj].innerHTML = '';
      var cid = panels[pj].id.replace(/-panel$/, '');
      var b = document.getElementById(cid);
      if (b) { b.setAttribute('aria-expanded', 'false'); }
    }
    FM.core.player = null;
  },
};

// --- FM.dialog: actual native answer dialogs, original section HTML ---
// Observe the DOM transition, including an action/body arriving before SSE.
// Neutral refreshes must not repeat the arrival or move an edited field.
FM.dialog = {
  stack: [],
  state: null,
  pending: null,
  returning: null,
  actionOrigin: null,
  current: function () { return document.querySelector('[data-fm-native-dialog],[data-fm-native-overlay]'); },
  identity: function (surface) { return surface && (surface.getAttribute('data-fm-native-dialog') || surface.getAttribute('data-fm-native-overlay')); },
  rememberOrigin: function (id) {
    id = id || (document.activeElement && document.activeElement.id);
    FM.dialog.actionOrigin = id ? { origin: id, base: FM.core.screenIdentity(FMTOKEN) } : null;
  },
  beforeRefresh: function (html, focused, nextToken) {
    var current = FM.dialog.current(), next = null;
    if (html.indexOf(' data-fm-native-dialog=') >= 0 || html.indexOf(' data-fm-native-overlay=') >= 0) {
      var template = document.createElement('template'); template.innerHTML = html;
      next = template.content.querySelector('[data-fm-native-dialog],[data-fm-native-overlay]');
    }
    var key = FM.dialog.identity(next);
    var loading = html.indexOf(' data-fm-native-loading') >= 0;
    if (loading) { return; }
    var stack = FM.dialog.stack, known = -1;
    for (var i = stack.length - 1; i >= 0; i--) { if (stack[i].key === key) { known = i; break; } }
    if (key && known >= 0 && known < stack.length - 1) {
      // Closing a confirmation over a card returns to the card's own action,
      // rather than skipping all the way to the page that opened that card.
      var leaving = stack[known + 1];
      FM.dialog.returning = { origin: leaving.origin, base: leaving.base };
      stack.length = known + 1; FM.dialog.state = stack[known]; FM.dialog.pending = null;
    } else if (key && known < 0 && (!current || key !== FM.dialog.identity(current))) {
      // Native validation can replace the form with a genuine loading panel
      // before its answer dialog appears. Retain the command's actual origin,
      // rather than remembering the intervening progress screen/body.
      var prior = FM.dialog.actionOrigin;
      FM.dialog.state = { key: key, origin: prior ? prior.origin : focused || FM.core.pos[FM.core.screenIdentity(FMTOKEN)],
        base: prior ? prior.base : FM.core.screenIdentity(FMTOKEN) };
      FM.dialog.pending = key; FM.dialog.returning = null;
      stack.push(FM.dialog.state);
    } else if (!key && stack.length) {
      var parent = FM.core.screenIdentity(nextToken), source = stack[stack.length - 1];
      for (var j = stack.length - 1; j >= 0; j--) { if (stack[j].base === parent) { source = stack[j]; break; } }
      FM.dialog.returning = { origin: source.origin, base: source.base };
      stack.length = 0;
      FM.dialog.state = null; FM.dialog.pending = null;
    }
    if (!key && !loading && FM.dialog.actionOrigin && FM.dialog.actionOrigin.base !== FM.core.screenIdentity(nextToken)) {
      FM.dialog.actionOrigin = null;
    }
  },
  afterRefresh: function () {
    var dialog = FM.dialog.current();
    if (dialog && (!FM.dialog.state || FM.dialog.pending === FM.dialog.identity(dialog))) {
      if(!FM.dialog.state){FM.dialog.state={key:FM.dialog.identity(dialog)};FM.dialog.stack.push(FM.dialog.state);}
      FM.dialog.pending = null;
      var title = document.getElementById(dialog.getAttribute('aria-labelledby') || dialog.getAttribute('data-fm-overlay-title')) || dialog;
      title.setAttribute('tabindex', '-1'); title.focus(); return;
    }
    if (document.querySelector('[data-fm-native-loading]')) { return; }
    var returning = FM.dialog.returning; FM.dialog.returning = null;
    if (returning) {
      var origin = returning.base === FM.core.screenIdentity(FMTOKEN) && returning.origin && document.getElementById(returning.origin);
      if (origin && origin.getAttribute('role') === 'treeitem') {
        // The authoritative tree arrives collapsed. Its original filter/shape
        // must be restored before this intentional modal return can focus it.
        FM.navigator.returnFocus = origin.id; return;
      }
      if (origin && !origin.disabled && origin.getClientRects().length) { origin.focus(); return; }
      // A native answer may close both its dialog and the editor underneath.
      // Normal screen restoration has already returned to that destination's
      // remembered opener; do not replace it with a heading merely because the
      // dialog's immediate origin belonged to the editor that also disappeared.
      var destination = FM.core.screenIdentity(FMTOKEN), active = document.activeElement;
      if (returning.base !== destination && active && active.id === FM.core.pos[destination]
          && FM.core.canFocus(active)) { return; }
      var headings = document.querySelectorAll('#fmroot [id^="fmsec"][id$="-h"]');
      if (headings.length) { var heading = headings[headings.length - 1]; heading.setAttribute('tabindex', '-1'); heading.focus(); }
    }
  },
  key: function (e) {
    if (e.code !== 'Tab' || e.altKey || e.ctrlKey || e.metaKey) { return false; }
    var dialog = FM.dialog.current(); if (!dialog || dialog.getAttribute('role') !== 'dialog') { return false; }
    var stops = Array.prototype.filter.call(dialog.querySelectorAll('button,input,select,textarea,a[href],[tabindex]'), function (el) {
      return el.tabIndex >= 0 && !el.disabled && el.getAttribute('aria-disabled') !== 'true' && el.getClientRects().length > 0;
    });
    if (!stops.length) { e.preventDefault(); dialog.setAttribute('tabindex', '-1'); dialog.focus(); return true; }
    var index = stops.indexOf(document.activeElement);
    if (index < 0 || (!e.shiftKey && index === stops.length - 1) || (e.shiftKey && index === 0)) {
      e.preventDefault(); stops[e.shiftKey ? stops.length - 1 : 0].focus(); return true;
    }
    return false;
  },
};
FM.core.onRefresh(FM.dialog.afterRefresh);

// --- FM.tabs: keyboard focus within the original TabBarHtml ---
// Keep native activation on Enter/Space: loading a game tab can require a streamed
// table read, so arrows must not issue commands merely while browsing the choices.
// Only this tablist owns these keys; forms, tables, NVDA chords and other widgets
// retain their existing behavior. Tab/Shift+Tab retain the FM26 button sequence.
FM.tabs = {
  key: function (e) {
    if (e.altKey || e.ctrlKey || e.metaKey || e.shiftKey || e.defaultPrevented) { return false; }
    var tab = document.activeElement;
    if (!tab || tab.getAttribute('role') !== 'tab') { return false; }
    var list = tab.closest('[role="tablist"]');
    if (!list) { return false; }
    var vertical = list.getAttribute('aria-orientation') === 'vertical';
    var previous = vertical ? 'ArrowUp' : 'ArrowLeft', next = vertical ? 'ArrowDown' : 'ArrowRight';
    if (e.code !== previous && e.code !== next && e.code !== 'Home' && e.code !== 'End') { return false; }
    var tabs = Array.prototype.filter.call(list.querySelectorAll('[role="tab"]'), function (candidate) {
      return candidate.closest('[role="tablist"]') === list && !candidate.disabled
        && candidate.getAttribute('aria-disabled') !== 'true' && candidate.getClientRects().length > 0;
    });
    var index = tabs.indexOf(tab);
    if (index < 0 || !tabs.length) { return false; }
    if (e.code === 'Home') { index = 0; }
    else if (e.code === 'End') { index = tabs.length - 1; }
    else { index = (index + (e.code === next ? 1 : -1) + tabs.length) % tabs.length; }
    e.preventDefault(); tabs[index].focus(); return true;
  },
};

// --- FM.nav: ribbon navigation ---
// The six digits (Alt+1..6, the fixed mod destinations) and Alt+P/Alt+M are owned by
// FM.matchchord, whose /matchchord route performs the out-of-match navigation server-side
// whenever no match is live (Alt+P still opens the FM menu there). Alt+C opens the calendar; Alt+H
// opens the Go on vacation panel (the game's own Shift+Space handler, server-side); Alt+[ / Alt+] step
// screen history. Alt+Left/Right are swallowed inert: HTML panel cycling is retired (every
// interactive panel is on the page; move by heading with H), but the chord is still caught so
// Alt+Left does not trigger the WebView's browser-Back and navigate away from the surface.
// e.code is used (not e.key) so Shift/layout never munge the chord. Chords confirmed by T1 to
// pass through NVDA in browse mode. Registered after FM.dropdown.key so an in-dropdown
// Alt+Arrow (which the dropdown arm ignores, requiring !altKey) falls through to here.
FM.nav = {
  MAP: { KeyC: 'calendar' },

  key: function (e) {
    if (e.altKey && !e.ctrlKey && !e.metaKey) {
      if (FM.nav.MAP[e.code]) { e.preventDefault(); FM.core.post('/nav?target=' + FM.nav.MAP[e.code]); return true; }
      if (e.code === 'KeyH') { e.preventDefault(); FM.core.failPost('/holiday'); return true; }
      if (e.code === 'ArrowLeft' || e.code === 'ArrowRight') { e.preventDefault(); return true; }
      if (e.code === 'BracketLeft') { e.preventDefault(); FM.core.failPost('/history?dir=back'); return true; }
      if (e.code === 'BracketRight') { e.preventDefault(); FM.core.failPost('/history?dir=forward'); return true; }
    }
    return false;
  },
};

// --- FM.tablepager: Previous / Next page on a table too long for one page ---
// The server reply ("Page 3 of 7") is the player's only feedback: the buttons keep their labels
// across the turn, so nothing about the page change announces itself. The refresh that follows
// re-reads the new page live from the game - no rows are held client-side. The pressed button
// normally survives the morph and keeps focus by id; when it is the one that just became disabled
// (Next landing on the last page) focus moves to its partner, because a disabled element cannot
// take focus and the browser would otherwise drop the player to the top of the document.
FM.tablepager = {
  pending: null,

  go: function (id, otherId, tableId) {
    return FM.core.command('/activate?id=' + encodeURIComponent(id) + '&screen=' + encodeURIComponent(FMTOKEN), function (t) {
      FM.core.say(t);
      FM.tablepager.pending = { id: id, other: otherId, table: tableId };
      FM.core.refresh();
    });
  },

  afterRefresh: function () {
    var p = FM.tablepager.pending;
    if (!p) { return; }
    FM.tablepager.pending = null;
    var el = document.getElementById(p.id);
    if (el && !el.disabled) { el.focus(); return; }
    var other = document.getElementById(p.other);
    if (other && !other.disabled) { other.focus(); return; }
    // A freshly realized window can now contain the whole table. If its pager disappears,
    // preserve the reading destination at the table rather than dropping focus to the body.
    var table = p.table && document.getElementById(p.table);
    if (table) { table.setAttribute('tabindex', '-1'); table.focus(); }
  },
};
FM.core.onRefresh(FM.tablepager.afterRefresh);

// --- FM.describe: on-demand secondary text (Alt+I) ---
// Button labels show only their primary text to keep the page clean; the descriptive
// remainder (a long subtitle, effectively a tooltip) is read on demand. Alt+I queries the
// focused element's description from the server and speaks it through the in-game Prism TTS
// (FM.core.say). Each press re-queries and re-speaks, so pressing Alt+I again on the same element
// repeats the description (the old aria-live line stayed silent on unchanged text). The query is
// read-only and never closes an open dropdown session, so registering after FM.dropdown/FM.nav
// (an in-widget Alt-chord wins first) is purely about precedence. e.code is used so layout/Shift
// never munge it.
FM.describe = {
  key: function (e) {
    if (!e.altKey || e.ctrlKey || e.metaKey || e.code !== 'KeyI') { return false; }
    var el = document.activeElement;
    if (!el || !el.id) { return false; }
    e.preventDefault();
    var owner = FM.core.screenIdentity(FMTOKEN);
    FM.core.requestText('/describe?id=' + encodeURIComponent(el.id)).then(function (t) {
      if (owner === FM.core.screenIdentity(FMTOKEN)) { FM.core.say(t); }
    }).catch(function (err) {
      FM.core.clog('Description response failed: ' + err.message);
      if (owner === FM.core.screenIdentity(FMTOKEN)) { FM.core.say('Additional information could not be read.'); }
    });
    return true;
  },
};

// --- FM.matchchord: Alt+`, Alt+1..7, Alt+P, Alt+M — match readouts and playback controls during a match, nav chords otherwise ---
// One server round-trip (/matchchord) decides which. The match gate is the ENGINE on the server,
// not any page element: the scoreboard template varies per competition, so the page is not a
// reliable match signal. During a match the reply is the readout, spoken through Prism:
//   Alt+`  score             (team names, score, clock: the page's top-heading scoreboard line)
//   Alt+1  ratings           (your on-pitch players, best 3: "best rated: Hamilton 7.4, ...")
//          double tap: the lowest 3 instead ("lowest rated: ...")
//   Alt+2  condition         (best 3, as percentages; double tap: the lowest 3)
//   Alt+3  body language     (players per icon band, most populous first: "body language: 6 neutral, ...")
//          double tap: the non-neutral players, each with the game's detailed word
//          ("Vázquez Complacent, Wilson Nervous"; "all players neutral body language" when none)
//   Alt+4  match conditions: attendance, share of capacity, away following, weather
//          (double tap = both crowds' moods). The Player Database shortcut outside a match.
//   Alt+5  possession, then third + flank ("Hamilton, your team. attacking third left"; a
//          penalty/goal box supersedes the third and drops the flank)
//   Alt+6  (unassigned in a match; the Fixture Schedule shortcut outside one)
//   Alt+7  distance          (metres from the attacked goal)
//   Alt+P  play/pause toggle (presses the game's own playback button; speaks "pausing" or
//          "resuming" from the state observed at press time)
//   Alt+M  match preferences (presses the playback strip's settings button; the card's
//          arrival announcement is the feedback)
// A quick second tap of the same chord (within TAP_MS) resends with taps=2, the standard
// screen-reader double-press idiom: the single-tap readout fires immediately on the first press
// (no waiting window), and the detail variant's speech interrupts it (FM.core.say is
// SpeakInterrupt on the server). Chords without a detail variant simply repeat (each Alt+P tap
// is its own toggle press). Key auto-repeat (holding the chord) is swallowed so a hold never
// counts as a double tap.
// Outside a match the server performs the navigation the chords mean — Alt+1..6 the fixed mod
// destinations (Messages / First Team / Tactics / Player Database / Staff / Fixture Schedule,
// each opened by the Navigator's live-resolved subtab dispatch; the six ribbon SECTIONS stay
// reachable through the Menu browser), Alt+P the FM menu — and replies empty; the screen change
// speaks for itself, so refresh; a non-empty reply there is a failure message, spoken honestly
// (Alt+` and Alt+7 have no out-of-match meaning and answer "Not in a match."). Alt+M outside a match answers the 'menubrowser' token: focus the title
// bar's Menu browser tree (M for Menu; see FM.navigator.focusMenuTree). Owns its keys outright (registered before FM.nav, which now maps
// only Alt+C). e.code is used so Shift/layout never munge the chord; the backquote is sent as
// the token "tick" because a literal backtick is not URL-safe.
FM.matchchord = {
  DIGITS: {
    Backquote: 'tick', Digit1: '1', Digit2: '2', Digit3: '3', Digit4: '4', Digit5: '5',
    Digit6: '6', Digit7: '7', KeyP: 'p', KeyM: 'm',
  },
  TAP_MS: 450,
  lastDigit: null,
  lastTime: 0,
  key: function (e) {
    if (!e.altKey || e.ctrlKey || e.metaKey) { return false; }
    var d = FM.matchchord.DIGITS[e.code];
    if (!d) { return false; }
    e.preventDefault();
    if (e.repeat) { return true; }
    if (d === 'm' && typeof FM_CAPABILITIES !== 'undefined' && FM_CAPABILITIES.initialMenuBrowser && document.getElementById('fmnav-tree')) {
      FM.navigator.focusMenuTree(); return true;
    }
    var now = Date.now();
    var taps = (d === FM.matchchord.lastDigit && now - FM.matchchord.lastTime < FM.matchchord.TAP_MS) ? 2 : 1;
    FM.matchchord.lastDigit = taps === 2 ? null : d; // a triple tap starts a fresh single
    FM.matchchord.lastTime = now;
    FM.core.command('/matchchord?digit=' + d + '&taps=' + taps, function (t) {
      // 'menubrowser' is a protocol token, not speech: Alt+M outside a match (the server owns
      // the match gate) focuses the title bar's Menu browser tree.
      if (t === 'menubrowser') { FM.navigator.focusMenuTree(); }
      else if (t.indexOf('dropdown:') === 0) {
        var dropdown = document.getElementById(t.substring(9));
        if (FM.dropdown.is(dropdown)) { dropdown.focus(); FM.dropdown.load(dropdown, []); }
        else { FM.core.refresh(); }
      }
      else if (t) { FM.core.say(t); } else { FM.core.refresh(); }
    });
    return true;
  },
};

// --- FM.continuebtn: the continue button, from anywhere (Alt+\ announce, Alt+Enter press) ---
// The game's single context-sensitive action button ("Continue", the team-talk and match-flow
// steps, ...) lives in the title bar or match HUD, not in the rendered panel, so the page has no
// element for it. Alt+\ speaks its current label plus the game date and time beside it; Alt+Enter
// presses it. Mirrors the speech surface's Tab / Shift+Tab. Both are Alt chords because NVDA's
// browse mode swallows bare printable keys before the page sees them (bare \ and ` were tried and
// reached the page in only one mode). Success feedback for a press is the screen change itself
// (plus the mod's own "Simulating" announcements); only an honest failure is spoken. e.code is
// used so layout/Shift never munge the keys.
FM.continuebtn = {
  key: function (e) {
    if (!e.altKey || e.ctrlKey || e.metaKey) { return false; }
    if (e.code === 'Backslash') {
      e.preventDefault();
      FM.core.requestText('/continue').then(function (t) { FM.core.say(t); }).catch(function (err) {
        FM.core.clog('/continue response failed: ' + err.message); FM.core.say('Continue status could not be read.');
      });
      return true;
    }
    if (e.code === 'Enter') {
      e.preventDefault();
      // Continue closes native popup sessions; release their client deferral as
      // well, including a same-panel advance whose screen identity stays put.
      FM.core.abandonInteractionForScreenChange();
      FM.core.command('/continue/press', function (t) {
        if (t) { FM.core.say(t); }
        FM.core.refresh();
      });
      return true;
    }
    return false;
  },
};

// --- FM.bookmarks: open MOD bookmark N (Ctrl+1..9, Ctrl+0 = 10, Ctrl+minus = 11, Ctrl+equals = 12) ---
// The slots are the mod's own bookmark store (edited via the Edit bookmarks button), navigated
// server-side through the Navigator dispatch — no longer the game's title-bar strip. Ctrl chords
// rather than Shift because NVDA's browse mode consumes shifted printables (Shift+1 is reverse
// quick-nav); Ctrl+digit passes through, and the tabless WebView2 has no tab-switch accelerator
// to fight. preventDefault also keeps Ctrl+minus / Ctrl+equals out of the WebView's zoom
// accelerator. Success feedback is the screen change itself ("Opening X" is suppressed, matching
// /nav); an empty slot or failed resolve is spoken honestly. e.code so keyboard layout never
// munges the keys.
FM.bookmarks = {
  slotFor: function (code) {
    var m = /^Digit([0-9])$/.exec(code);
    if (m) { return m[1] === '0' ? 10 : parseInt(m[1], 10); }
    if (code === 'Minus') { return 11; }
    if (code === 'Equal') { return 12; }
    return 0;
  },

  key: function (e) {
    if (!e.ctrlKey || e.altKey || e.metaKey || e.shiftKey) { return false; }
    var slot = FM.bookmarks.slotFor(e.code);
    if (!slot) { return false; }
    e.preventDefault();
    FM.core.command('/bookmark?slot=' + slot, function (t) {
      if (t && t.indexOf('Opening ') !== 0) { FM.core.say(t); }
      FM.core.refresh();
    });
    return true;
  },
};

// --- FM.save: Alt+S quick save (S for Save) ---
// The server calls the same game handler the native in-game Ctrl+S runs
// (SaveGameHandlerModule), so the flow is identical to saving in-game. Alt rather than Ctrl
// (2026-07-24, per user): Ctrl+digits are the bookmark chords and Ctrl is headed for
// user-defined shortcuts, so Alt is the mod-chord modifier here too. preventDefault keeps the
// chord out of the WebView; Alt chords pass through NVDA browse mode. The spoken reply is
// "Saving" or an honest not-available phrase; the game's own saving progress panel takes it
// from there.
FM.save = {
  key: function (e) {
    if (!e.altKey || e.ctrlKey || e.metaKey || e.shiftKey) { return false; }
    if (e.code !== 'KeyS') { return false; }
    e.preventDefault();
    FM.core.command('/save', function (t) {
      if (t) { FM.core.say(t); }
    });
    return true;
  },
};

// --- FM.sim: deterministic landing after a screen-changing calendar simulation ---
// The server pushes 'simarrive' (with the arrival page's token) only when a sim actually
// changed screens; same-screen hops never move the reading position. Focus lands on the
// last content section heading, so every arrival starts reading from the landed panel's
// title. The token gate means a late event can never yank focus on an unrelated page:
// the focus waits (via the refresh hook) until the page renders the arrival token, and is
// dropped when the player has already navigated elsewhere (token never matches again —
// the next sim overwrites it).
FM.sim = {
  token: null,
  anchor: null,
  arrive: function (tok) { var parts = tok.split('\t'); FM.sim.token = parts[0]; FM.sim.anchor = parts[1] || null; FM.sim.focusAnchor(); },
  focusAnchor: function () {
    if (!FM.sim.token || FMTOKEN !== FM.sim.token) { return; }
    FM.sim.token = null;
    var origin = FM.sim.anchor && document.getElementById(FM.sim.anchor);
    FM.sim.anchor = null;
    if (origin && !origin.disabled) { origin.focus(); return; }
    var heads = document.querySelectorAll('[id^="fmsec"][id$="-h"]');
    if (!heads.length) { return; }
    var h = heads[heads.length - 1];
    h.setAttribute('tabindex', '-1');
    h.focus();
  },
};

// --- FM.contextmenu: open a control's in-game right-click menu ---
// The browser fires a 'contextmenu' event for all three native gestures — Shift+F10, the Applications
// key, and a physical right-click — so one listener covers them. A control the server marked
// aria-haspopup="menu" has an in-game right-click menu; route the gesture to /contextmenu instead of
// the browser's own menu, and the game's menu surfaces on the next screen render (ContextMenuSurface
// renders it as the modal "Actions" page). Controls without the marker are left alone, so the browser's
// own context menu still works everywhere else.
FM.contextmenu = {
  // One-shot: a menu view has just opened and its first stop should take focus. Set on a
  // successful companion-driven open and on the SSE page->menu transition (a menu opened from
  // in-game); consumed by focusFirst on the first refresh that renders the marker.
  wantFirst: !!document.querySelector('ul[data-fm-menu]'),

  open: function (id) {
    // Snapshot the page beneath BEFORE the open resync replaces #fmroot with the modal menu, so the
    // matching close can restore it instantly (FM.core.menuStash). Captured here, not in the SSE
    // 'screen' handler, because this resync advances FMTOKEN to the menu token before that event
    // arrives. Only kept if the menu actually opened.
    var preToken = FMTOKEN, preHtml = document.getElementById('fmroot').innerHTML;
    return FM.core.command('/contextmenu?id=' + encodeURIComponent(id) + '&screen=' + encodeURIComponent(preToken), function (t) {
      // Success ("Opened menu") is redundant — the menu renders and the screen reader reads it — so
      // only an honest gap ("No context menu here.", "did not open", "Refresh") is spoken. Then resync.
      if (t && t.indexOf('Opened menu') === 0) {
        if (!hasMenu(preToken)) { FM.core.menuStash = { base: stripMenu(preToken), html: preHtml }; }
        FM.contextmenu.wantFirst = true;
      } else if (t) {
        FM.core.say(t);
      }
      FM.core.refresh();
    });
  },

  // One-shot: after a drill (Right/Enter on a folder) or a step-out (Left/Back), focus should land
  // on the first real OPTION of the new level rather than the "Close menu" dismiss, so the player
  // hears where they arrived. Set by the arrow controller before it clicks; consumed by focusFirst.
  wantOption: false,
  optionFrom: null,
  path: function () {
    var i = FMTOKEN.indexOf('menu:');
    return i < 0 ? null : FMTOKEN.slice(i + 5).split(':')[0];
  },
  requestOption: function () {
    FM.contextmenu.optionFrom = FM.contextmenu.path();
    FM.contextmenu.wantOption = true;
  },

  // The open menu as a live ARIA menu, or null when no menu view is on screen.
  menu: function () { return document.querySelector('ul[data-fm-menu]'); },
  // menuitemcheckbox is the same entry with the game's tick on it, so it must be arrowable and
  // countable exactly like a plain menuitem.
  items: function () {
    var m = FM.contextmenu.menu();
    return m ? Array.prototype.slice.call(m.querySelectorAll('[role="menuitem"],[role="menuitemcheckbox"]')) : [];
  },
  // The first entry that is a real action/folder — not the "Close menu" dismiss, not "Back".
  firstOption: function () {
    var items = FM.contextmenu.items();
    for (var i = 0; i < items.length; i++) {
      if (!items[i].hasAttribute('data-fm-menu-close') && !items[i].hasAttribute('data-fm-menu-back')) { return items[i]; }
    }
    return items[0] || null;
  },

  // Move roving focus to one menuitem: it becomes the only tabbable entry, and focus lands on it.
  // A blur/refocus when it is already focused mirrors FM.messages.refocus so a re-land still speaks.
  focusItem: function (el) {
    if (!el) { return; }
    var items = FM.contextmenu.items();
    for (var i = 0; i < items.length; i++) { items[i].setAttribute('tabindex', items[i] === el ? '0' : '-1'); }
    if (document.activeElement === el) { el.blur(); setTimeout(function () { el.focus(); }, 0); }
    else { el.focus(); }
  },

  // Post-refresh hook: land focus inside a freshly rendered menu view. A drill/step-out (wantOption)
  // lands on the first real option of the new level; a fresh open (wantFirst) lands on the "Close
  // menu" dismiss, so every open starts reading from the same place. Both flags are one-shot, so a
  // background refresh while the player browses never yanks focus, and both drop if no menu is on
  // screen (the open failed or already closed).
  focusFirst: function () {
    if (!FM.contextmenu.wantOption && !FM.contextmenu.wantFirst) { return; }
    if (!hasMenu(FMTOKEN)) { FM.contextmenu.wantOption = false; FM.contextmenu.optionFrom = null; FM.contextmenu.wantFirst = false; return; }
    // An older root-menu refresh may finish while a drill is in flight. It must
    // not consume the destination focus before the native folder path changes.
    if (FM.contextmenu.wantOption && FM.contextmenu.path() === FM.contextmenu.optionFrom) { return; }
    var el = FM.contextmenu.wantOption ? FM.contextmenu.firstOption() : document.querySelector('[data-fm-menu-first]');
    if (!el) { return; }   // menu view not morphed in yet; consume on a later refresh
    FM.contextmenu.wantOption = false;
    FM.contextmenu.optionFrom = null;
    FM.contextmenu.wantFirst = false;
    FM.contextmenu.focusItem(el);
  },

  // Arrow-key controller for the open menu (registered with FM.core.onKey). Only fires while focus
  // is on a menuitem, so it never shadows the arrow keys anywhere else. Up/Down (and Home/End) move
  // between siblings; Right drills into a folder (data-fm-folder); Left steps out one level via the
  // "Back" entry; Enter/Space activate; Escape dismisses. Drill/step-out reuse the same act() the
  // click uses, so the arrows are a second driver of the server's existing menu logic, not a fork.
  key: function (e) {
    if (e.altKey || e.ctrlKey || e.metaKey) { return false; }
    var cur = document.activeElement;
    var role = cur ? cur.getAttribute('role') : null;
    if ((role !== 'menuitem' && role !== 'menuitemcheckbox') || !FM.contextmenu.menu()) { return false; }
    if (!FM.contextmenu.menu().contains(cur)) { return false; }

    var items = FM.contextmenu.items();
    var idx = items.indexOf(cur);
    var n = items.length;

    switch (e.code) {
      case 'ArrowDown': e.preventDefault(); FM.contextmenu.focusItem(items[(idx + 1) % n]); return true;
      case 'ArrowUp': e.preventDefault(); FM.contextmenu.focusItem(items[(idx - 1 + n) % n]); return true;
      case 'Home': e.preventDefault(); FM.contextmenu.focusItem(items[0]); return true;
      case 'End': e.preventDefault(); FM.contextmenu.focusItem(items[n - 1]); return true;
      case 'ArrowRight':
        e.preventDefault();
        // Only a folder drills; on a leaf, Right does nothing (suppressed so it is not a stray
        // browse-mode move). The server rebuilds the submenu; focus lands on its first option.
        if (cur.hasAttribute('data-fm-folder')) { FM.contextmenu.requestOption(); cur.click(); }
        return true;
      case 'ArrowLeft': {
        e.preventDefault();
        var back = FM.contextmenu.menu().querySelector('[data-fm-menu-back]');
        if (back) { FM.contextmenu.requestOption(); back.click(); }   // at root there is nothing to exit
        return true;
      }
      case 'Enter':
      case 'Space':
        e.preventDefault();
        // A folder or Back re-renders the menu, so land focus on the new level's first option like the
        // arrows do; a leaf invokes its action and the server closes the menu.
        if (cur.hasAttribute('data-fm-folder') || cur.hasAttribute('data-fm-menu-back')) { FM.contextmenu.requestOption(); }
        cur.click();
        return true;
      case 'Escape': {
        e.preventDefault();
        var close = FM.contextmenu.menu().querySelector('[data-fm-menu-close]');
        if (close) { close.click(); }
        return true;
      }
    }
    return false;
  },

  // The menu-bearing element for a gesture: the focused control (Shift+F10 / Applications key), else
  // the nearest ancestor of the event target (a right-click), whichever carries aria-haspopup="menu".
  owner: function (e) {
    var ae = document.activeElement;
    if (ae && ae.id && ae.getAttribute && ae.getAttribute('aria-haspopup') === 'menu') { return ae; }
    var t = e.target;
    while (t && t.getAttribute) {
      if (t.id && t.getAttribute('aria-haspopup') === 'menu') { return t; }
      t = t.parentNode;
    }
    return null;
  },
};

// --- FM.messages: standalone Messages panel reading pane ---
// Opening an email moves focus straight to the message body (the body, not the subject, lands
// under the screen-reader cursor), so the email reads immediately instead of stranding focus on
// the list. The list and reading pane are server-rendered ARIA regions ("message list" / "message"),
// so the screen reader can hop between them with its landmark key. Alt+Right / Alt+Left jump between
// the body and the open email's label in the list (spatially: right into the content, left back to
// your place in the list). The body carries data-fm-msg-body (the focus target); the body populates
// a beat after the activation, so open() pulls it in and only moves focus once it has changed to the
// opened email.
FM.messages = {
  current: null,    // id of the last-opened email button, so Alt+Left can return to it
  wantBody: false,  // a focus-the-body is pending
  staleText: null,  // the body shown before this open, so we can tell when it has updated
  tries: 0,
  pumpTimer: null,

  bodyText: function () {
    var b = document.querySelector('[data-fm-msg-body]');
    return b ? b.textContent : null;
  },

  // Open an email. The game swaps the reading pane a beat after the activate (well under a second),
  // and opening within the same panel does not reliably trigger a refresh on its own, so we pull the
  // new content ourselves (pump) and only move focus once the body has actually changed from the
  // previously shown email - otherwise focus lands on, and the screen reader announces, the old body.
  open: function (id) {
    FM.messages.current = id;
    FM.messages.staleText = FM.messages.bodyText();
    FM.messages.wantBody = true;
    FM.messages.tries = 0;
    if (FM.messages.pumpTimer) { clearTimeout(FM.messages.pumpTimer); FM.messages.pumpTimer = null; }
    FM.core.command('/activate?id=' + encodeURIComponent(id) + '&screen=' + encodeURIComponent(FMTOKEN),
      function (t) { FM.core.actResult(t); FM.messages.pump(); }, function () { FM.messages.pump(); });
  },

  // Schedule a refresh to pull the freshly opened email; focusBody (a post-refresh hook) decides
  // whether the body has updated yet and either focuses it or pumps again. Capped so a missing or
  // unchanged body (e.g. re-opening the same email) gives up quietly instead of looping forever.
  pump: function () {
    if (!FM.messages.wantBody) { return; }
    if (FM.messages.tries++ > 10) { FM.messages.wantBody = false; return; }
    FM.messages.pumpTimer = setTimeout(function () { FM.messages.pumpTimer = null; FM.core.refresh(); }, 120);
  },

  // Post-refresh hook: focus the body once it shows the newly opened email (its text differs from the
  // previously shown one). If focus is already on the body (opening one while another is focused), it
  // is the same persistent node, so a plain refocus says nothing - blur and refocus on a fresh tick so
  // the move re-announces. Not updated yet -> pump again.
  focusBody: function () {
    if (!FM.messages.wantBody) { return; }
    var body = document.querySelector('[data-fm-msg-body]');
    var txt = body ? body.textContent : null;
    if (!body || txt === FM.messages.staleText) { FM.messages.pump(); return; }
    FM.messages.wantBody = false;
    FM.messages.refocus(body);
  },

  // Focus an element, blurring and refocusing on a fresh tick when it is already focused so the
  // screen reader re-reads it (the body is one persistent node, so a plain refocus says nothing).
  refocus: function (el) {
    if (document.activeElement === el) {
      var owner = FM.core.screenIdentity(FMTOKEN);
      el.blur();
      setTimeout(function () {
        if (document.activeElement === document.body && document.contains(el)
            && FM.core.screenIdentity(FMTOKEN) === owner) { el.focus(); }
      }, 0);
    } else {
      el.focus();
    }
  },

  // Alt+Right -> the message body; Alt+Left -> the open email's label in the list. List and body sit
  // left and right, so the chords read spatially: right into the content, left back to your place in
  // the list. Only on the Messages reading surface (a body anchor is present); elsewhere returns false
  // so FM.nav keeps swallowing Alt+Left/Right inert. Ignored while typing in a field (the search box).
  key: function (e) {
    if (!e.altKey || e.ctrlKey || e.metaKey) { return false; }
    if (e.code !== 'ArrowRight' && e.code !== 'ArrowLeft') { return false; }
    var body = document.querySelector('[data-fm-msg-body]');
    if (!body) { return false; }
    var ae = document.activeElement;
    if (ae && (ae.tagName === 'INPUT' || ae.tagName === 'TEXTAREA')) { return false; }
    e.preventDefault();
    if (e.code === 'ArrowRight') { FM.messages.refocus(body); }
    else { FM.messages.goLabel(); }
    return true;
  },

  // Focus the currently displayed email's button (the last one opened). Falls back to the message-list
  // region when none is known yet, so Alt+Left still lands you in the list.
  goLabel: function () {
    var btn = FM.messages.current ? document.getElementById(FM.messages.current) : null;
    if (btn) { btn.focus(); return; }
    var region = document.querySelector('[data-fm-msg-list]');
    if (region) { region.focus(); }
  },
};

// --- FM.msgview: the modal Message view (an opened email as the whole page) ---
// Clicking a Subject cell in the Messages table opens the email modally: /message/open activates
// the row server-side and only opens the view once the reading pane shows that message; the
// msgview: token segment then renders the modal page (server-side, like the context menu's
// Actions page). The list page is stashed before the open and restored instantly on close, focus
// returning to the row via the screen's remembered position. Escape closes from anywhere in the
// modal (the page's "Close message" stop does the same server-side); a context menu open OVER
// the modal keeps Escape for itself.
FM.msgview = {
  opening: 0, // dispatched opens retain their native handles until the replies finish
  pendingOpen: null, // undispatched intent while an actual body is being replaced
  viewObserved: false,
  wantBody: false,   // focus the message body on the next refresh (set on a successful open)
  refreshAfterClose: false, // resolve the destination filter after this message closes
  unreadRowIndex: -1, // opened row's position, restored inside the filtered table after close
  unreadSourceToken: null,
  returnMessageId: null, // actual NewsItem identity, independent of panel/DOM row ids
  restoreUnreadRow: false,
  returnFocus: null,
  searchReturn: null,

  active: function () { return hasMsgView(FMTOKEN); },

  historyReturnState: function () {
    var search = FM.msgview.searchReturn;
    return { message: FM.msgview.returnMessageId, index: FM.msgview.unreadRowIndex,
      source: FM.msgview.unreadSourceToken,
      search: search ? { key: search.key, query: search.query, closing: false } : null };
  },
  restoreHistoryReturn: function (saved) {
    var state = saved && saved.returnState;
    var owner = document.querySelector('[data-fm-message-view-id]');
    if (!state || FM.msgview.opening || !owner
        || owner.getAttribute('data-fm-message-view-id') !== state.message
        || saved.message !== state.message) { return; }
    FM.msgview.returnMessageId = state.message;
    FM.msgview.unreadRowIndex = state.index;
    FM.msgview.unreadSourceToken = state.source;
    FM.msgview.searchReturn = state.search
      ? { key: state.search.key, query: state.search.query, closing: false } : null;
    FM.msgview.refreshAfterClose = true;
    FM.msgview.viewObserved = true;
    FM.msgview.restoreUnreadRow = false;
    FM.msgview.returnFocus = null;
  },

  // Entity-name callbacks update the content revision, not the inbox identity.
  // Keep the native panel/binding/template identity for a deferred focus return.
  returnScreen: function (token) { return FM.core.screenIdentity(stripMsgView(token)); },

  beginClose: function () {
    var origin = FM.msgview.searchReturn;
    if (!origin) { return; }
    if (FM.core.historyKey(0, stripMsgView(FMTOKEN)) === origin.key) { FM.msgview.searchReturn = null; return; }
    origin.closing = true;
    origin.until = Date.now() + 25000;
  },
  restoreSearchDisclosure: function () {
    var field = document.querySelector('[data-fm-message-query]');
    var id = field && field.getAttribute('data-fm-message-query-opener');
    var opener = id && document.getElementById(id);
    if (!field || !opener) { return; }
    // Preserve the existing disclosure's Close -> opener contract after native
    // history restored its field. This records context; it never moves focus.
    FM.core.revealedInput = { owner: FM.core.screenIdentity(FMTOKEN), field: field.id, opener: opener.id };
  },
  cancelSearchReturn: function () {
    if (FM.msgview.searchReturn && FM.msgview.searchReturn.closing) {
      FM.msgview.searchReturn = null;
      FM.msgview.refreshAfterClose = false;
      FM.msgview.restoreUnreadRow = false;
    }
  },
  waitingForSearchReturn: function () {
    var origin = FM.msgview.searchReturn;
    if (!origin || !origin.closing) { return false; }
    if (Date.now() > origin.until) {
      FM.msgview.searchReturn = null;
      FM.core.say('The original search could not be restored.');
      return false;
    }
    var field = document.querySelector('[data-fm-message-query]');
    return FM.core.historyKey(0, stripMsgView(FMTOKEN)) !== origin.key || !field || field.value !== origin.query;
  },

  open: function (id) {
    FM.msgview.pendingOpen = null;
    if (!FM.core.refreshing && !FM.core.pending) { return FM.msgview.openNow(id); }
    // BeginBody replaces native handles before the HTTP body reaches the DOM.
    // An inbox filter can also replace its whole native list. Wait for that
    // existing body, then resolve the exact NewsItem again, never its old row
    // position/caption. This is one delayed dispatch, not a command retry.
    var button = document.getElementById(id), row = button && button.closest('tr[data-fm-message-id]');
    var message = row && row.getAttribute('data-fm-message-id');
    var request = { owner: FM.msgview.returnScreen(FMTOKEN), message: message, until: Date.now() + 25000 };
    FM.msgview.pendingOpen = request;
    return FM.core.waitForHistoryBody(request.until).then(function () {
      if (FM.msgview.pendingOpen !== request) { return; }
      FM.msgview.pendingOpen = null;
      if (request.owner !== FM.msgview.returnScreen(FMTOKEN)) { return; }
      var matches = Array.prototype.filter.call(document.querySelectorAll('table button[onclick^="FM.msgview.open"]'), function (candidate) {
        var current = candidate.closest('tr[data-fm-message-id]');
        return message && current && current.getAttribute('data-fm-message-id') === message;
      });
      if (Date.now() >= request.until || matches.length !== 1 || matches[0].disabled) {
        FM.core.actResult('The message could not be identified. Refresh the inbox before opening it.');
        FM.core.refresh(); return;
      }
      return FM.msgview.openNow(matches[0].id);
    }, function () {
      if (FM.msgview.pendingOpen !== request) { return; }
      FM.msgview.pendingOpen = null;
      if (request.owner !== FM.msgview.returnScreen(FMTOKEN)) { return; }
      FM.core.actResult('The message could not be identified. Refresh the inbox before opening it.');
      FM.core.refresh();
    });
  },

  openNow: function (id) {
    // After simulation, the first email can be opened while Portal Overview is still transitioning
    // into Messages; its filter DOM is not yet a reliable description of the destination. Defer
    // deciding whether this is an Unread flow until close, when the destination inbox has settled.
    FM.msgview.refreshAfterClose = true;
    FM.msgview.viewObserved = false;
    FM.msgview.unreadRowIndex = -1;
    FM.msgview.unreadSourceToken = stripMsgView(FMTOKEN);
    FM.msgview.returnMessageId = null;
    FM.msgview.restoreUnreadRow = false;
    FM.msgview.returnFocus = null;
    var query = document.querySelector('[data-fm-message-query]');
    FM.msgview.searchReturn = query && query.value.trim()
      ? { key: FM.core.historyKey(0, FMTOKEN), query: query.value, closing: false } : null;
    var rows = document.querySelectorAll('table button[onclick^="FM.msgview.open"]');
    for (var rowIndex = 0; rowIndex < rows.length; rowIndex++) {
      if (rows[rowIndex].id === id) {
        FM.msgview.unreadRowIndex = rowIndex;
        var row = rows[rowIndex].closest('tr[data-fm-message-id]');
        FM.msgview.returnMessageId = row && row.getAttribute('data-fm-message-id');
        break;
      }
    }
    // Stash the list page before the modal replaces #fmroot, so close restores it instantly.
    // Never overwrite an existing stash (it would capture a half-rendered modal).
    if (!FM.core.msgStash) {
      FM.core.msgStash = { base: stripMsgView(FMTOKEN), html: document.getElementById('fmroot').innerHTML };
    }
    // A lost reply can follow a real native open; retain this focus intent
    // while the authoritative screen/reading pane establishes the outcome.
    FM.msgview.wantBody = true;
    FM.msgview.opening++;
    FM.core.command('/message/open?id=' + encodeURIComponent(id) + '&screen=' + encodeURIComponent(FMTOKEN), function (t) {
        if (t) { FM.core.msgStash = null; FM.msgview.clearUnreadReturn(); FM.msgview.wantBody = false; FM.core.actResult(t); }
      }).finally(function () {
        FM.msgview.opening--;
        if (FM.msgview.opening > 0) { return; }
        // A late restructure must not clear native handles between the open
        // handler's main-thread steps. Flush its coalesced read only after the
        // actual replies, including uncertain transport outcomes. Never replay.
        var hard = FM.core.pendingHard;
        FM.core.pending = false; FM.core.pendingHard = false; FM.core.pendingNeutral = false;
        FM.core.refresh(hard);
      });
  },

  // Post-refresh hook: land focus on the opened email's body once the modal page is up, so the
  // message reads immediately instead of stranding focus. Reuses FM.messages.refocus (blur +
  // refocus on a fresh tick when the body node is already focused).
  focusBody: function () {
    if (!FM.msgview.wantBody) { return; }
    if (!FM.msgview.active()) {
      // An uncertain reply can mean either a real open or no native dispatch.
      // Only an actual subsequent inbox body establishes the latter. Do not
      // retain its old HTML/modal intent, or mistake a busy/intermediate page
      // for that observation while the opening command is still in flight.
      if (FM.msgview.opening === 0 && document.querySelector('[data-fm-msg-unread-filter]')) {
        FM.core.msgStash = null;
        FM.msgview.clearUnreadReturn();
        FM.msgview.wantBody = false;
      }
      return;
    }
    FM.msgview.viewObserved = true;
    var historyFocus = FM.core.historyFocus;
    if (historyFocus && historyFocus.key === FM.core.historyKey(FM.core.historyIndex, FMTOKEN)) {
      var origin = document.getElementById(historyFocus.id);
      if (origin) { FM.msgview.wantBody = false; origin.focus(); return; }
    }
    var body = document.querySelector('[data-fm-msg-body]');
    if (!body) { return; }   // modal not rendered yet; the next refresh retries
    FM.msgview.wantBody = false;
    FM.messages.refocus(body);
  },

  close: function () {
    // Match the explicit Close button's authoritative post-action refresh.
    // A close can race an already-running body request and its screen event.
    FM.msgview.beginClose();
    FM.core.command('/message/close', function (t) {
      if (t) { FM.msgview.searchReturn = null; FM.core.actResult(t); }
      FM.core.refresh();
    });
  },

  // After the modal closes and the inbox has been rendered again, repeat the native Unread-filter
  // activation once. This is the same action the player currently performs manually to make the
  // newly read row disappear; it does not alter the other message views or invent a second list.
  refreshUnread: function () {
    // Portal -> Messages navigation emits an inbox screen before the message
    // pane has finished opening. That intermediate page is not a close event.
    if (!FM.msgview.refreshAfterClose || !FM.msgview.viewObserved || FM.msgview.active()) { return; }
    if (FM.msgview.waitingForSearchReturn()) { return; }
    var button = document.querySelector('[data-fm-msg-unread-filter]');
    // A live inbox restructure can update the button's rendered label before Idiomorph carries
    // across the companion-only data-fm-selected attribute. The accessibility state already folded
    // into the label remains authoritative during that one stale frame ("..., selected").
    var selected = button && (button.getAttribute('role') === 'tab'
      ? button.getAttribute('aria-selected') === 'true'
      : button.hasAttribute('data-fm-selected') || /(^|,\s*)selected(\s*,|$)/.test(button.textContent || ''));
    FM.msgview.refreshAfterClose = false;
    FM.msgview.viewObserved = false;
    // The action's /body may commit before the SSE screen event, so that
    // handler may no longer see a msgview token to release this old HTML.
    FM.core.msgStash = null;
    if (!selected) {
      // All/other native filters keep the read row. Portal's compact row can
      // navigate to the full inbox, so its old DOM id may not exist there.
      // Prefer its native NewsItem identity when the destination still has it.
      // The Unread path alone deliberately returns to the next remaining row.
      FM.msgview.restoreUnreadRow = true;
      return;
    }
    FM.msgview.returnMessageId = null;
    var id = button.id;
    // FM27's cached Unread tab keeps the selected read message in its native
    // list. The shared table reads its live unread flags and excludes that row
    // for this selected filter. No temporary switch to another filter is needed.
    if (button.getAttribute('role') === 'tab') {
      FM.msgview.restoreUnreadRow = true;
      FM.core.refresh(true);
      return;
    }
    FM.core.command('/activate?id=' + encodeURIComponent(id) + '&screen=' + encodeURIComponent(FMTOKEN), function (t) {
      FM.core.actResult(t);
      // Removing a read row shifts the inbox's native row bindings. A clean replacement prevents
      // Idiomorph from retaining the departed row's label on a reused table node. The post-refresh
      // hook below returns focus to the same table position, never to the filter button.
      setTimeout(function () {
        FM.msgview.restoreUnreadRow = true;
        FM.core.refresh(true);
      }, 400);
    }, function () { FM.msgview.restoreUnreadRow = true; });
  },

  messageRow: function (rows, messageId) {
    if (!messageId) { return null; }
    var found = null;
    for (var i = 0; i < rows.length; i++) {
      var row = rows[i].closest('tr[data-fm-message-id]');
      if (row && row.getAttribute('data-fm-message-id') === messageId) {
        if (found) { return null; } // Never guess between duplicate identities.
        found = rows[i];
      }
    }
    return found;
  },

  restoreUnreadFocus: function () {
    if (!FM.msgview.active() && FM.msgview.waitingForSearchReturn()) { return; }
    var remembered = FM.msgview.returnFocus;
    if (remembered && remembered.token !== FM.msgview.returnScreen(FMTOKEN)) { FM.msgview.returnFocus = null; remembered = null; }
    if (!FM.msgview.restoreUnreadRow) {
      // A final native settle can replace the returned row after its first
      // arrival. Repair only lost body focus, until the next user interaction.
      if (remembered && document.activeElement === document.body && !FM.msgview.active()) {
        var target = remembered.messageId
          ? FM.msgview.messageRow(document.querySelectorAll('table button[onclick^="FM.msgview.open"]'), remembered.messageId)
          : document.getElementById(remembered.id);
        if (target) { target.focus(); remembered.id = target.id; }
      }
      return;
    }
    var rows = document.querySelectorAll('table button[onclick^="FM.msgview.open"]');
    if (!rows.length && !document.querySelector('[data-fm-msg-unread-filter]')) { return; }
    if (rows.length || document.querySelector('[data-fm-msg-unread-filter]')) {
      FM.msgview.restoreUnreadRow = false;
      // Portal and Messages have different native list owners/DOM ids. A retained
      // message follows its actual NewsItem identity; an absent/read-filtered row
      // keeps the inherited same-position/first-inbox fallback. Observe actual
      // refreshes instead of scheduling focus on an arbitrary timer.
      var sameScreen = FM.msgview.returnScreen(FM.msgview.unreadSourceToken) === FM.msgview.returnScreen(FMTOKEN);
      var wantedIndex = sameScreen ? FM.msgview.unreadRowIndex : 0;
      var token = FM.msgview.returnScreen(FMTOKEN);
      if (FM.msgview.returnScreen(FMTOKEN) === token && !FM.msgview.active()) {
        var currentRows = document.querySelectorAll('table button[onclick^="FM.msgview.open"]');
        if (!currentRows.length) {
          // Reading the final unread message removes the origin row as well.
          // Keep focus in this inbox on its actual selected, now-empty filter.
          var filter = document.querySelector('[data-fm-msg-unread-filter]');
          if (filter && (filter.getAttribute('aria-selected') === 'true'
              || filter.hasAttribute('data-fm-selected'))) { filter.focus(); }
          return;
        }
        var index = Math.min(Math.max(wantedIndex, 0), currentRows.length - 1);
        var original = FM.msgview.messageRow(currentRows, FM.msgview.returnMessageId);
        var target = original || currentRows[index];
        target.focus();
        FM.msgview.returnFocus = { token: token, id: target.id, messageId: original ? FM.msgview.returnMessageId : null };
      }
    }
    FM.msgview.unreadRowIndex = -1;
    FM.msgview.unreadSourceToken = null;
    FM.msgview.returnMessageId = null;
    FM.msgview.searchReturn = null;
  },

  clearUnreadReturn: function () {
    FM.msgview.searchReturn = null;
    FM.msgview.refreshAfterClose = false;
    FM.msgview.viewObserved = false;
    FM.msgview.unreadRowIndex = -1;
    FM.msgview.unreadSourceToken = null;
    FM.msgview.returnMessageId = null;
    FM.msgview.restoreUnreadRow = false;
    FM.msgview.returnFocus = null;
  },

  key: function (e) {
    if (e.key !== 'Escape' || e.altKey || e.ctrlKey || e.metaKey || e.shiftKey) { return false; }
    if (!FM.msgview.active() || hasMenu(FMTOKEN)) { return false; }
    e.preventDefault();
    FM.msgview.close();
    return true;
  },
};

// --- FM.pitch: tactics-pitch 2D grid (formation navigation + position changes) ---
// The server renders the pitch as a role="grid" table whose cells carry their precomputed spatial
// neighbours (data-up/down/left/right -> target cell id). Three linked widgets share the area: the
// browse grid (the formation), a small action menu, and a hidden move grid (all 24 slots). Arrow keys
// follow the neighbours via roving tabindex (so the screen reader announces the focused cell); Enter
// over a player opens its actions; "Change pitch position" swaps in the move grid where Enter places
// the player (a game-validated swap or move) and Escape cancels. Mirrors FM.date's day-grid pattern.
FM.pitch = {
  src: null, // slot being relocated while the move grid is open
  areaId: null,
  moveId: null,

  area: function () { return document.getElementById(FM.pitch.areaId || 'fm-pitch-widget'); },
  part: function (selector) { var area = FM.pitch.area(); return area ? area.querySelector(selector) : null; },
  cell: function (mode, slot) { return FM.pitch.part('table[data-pitch="' + mode + '"] [data-slot="' + slot + '"]'); },

  // Client-side show/hide retained for any non-grid disclosure that still calls fmtoggle.
  toggle: function (bid, pid) {
    var b = document.getElementById(bid);
    var d = document.getElementById(pid);
    if (!b || !d) { return; }
    var open = d.hidden;
    d.hidden = !open;
    b.setAttribute('aria-expanded', open ? 'true' : 'false');
  },

  // Roving-tabindex focus move to a cell by id, clearing the rest of its grid first.
  focusCell: function (id) {
    var t = document.getElementById(id);
    if (!t) { return false; }
    var grid = t.closest('table[role="grid"]');
    if (grid) {
      var cs = grid.querySelectorAll('td[role="gridcell"]');
      for (var i = 0; i < cs.length; i++) { cs[i].setAttribute('tabindex', '-1'); }
    }
    t.setAttribute('tabindex', '0');
    t.focus();
    return true;
  },

  gridArrow: function (e, cell) {
    var attr = e.code === 'ArrowUp' ? 'data-up' : e.code === 'ArrowDown' ? 'data-down'
      : e.code === 'ArrowLeft' ? 'data-left' : e.code === 'ArrowRight' ? 'data-right' : null;
    if (!attr) { return false; }
    e.preventDefault();
    var nid = cell.getAttribute(attr);
    if (nid) { FM.pitch.focusCell(nid); } // no neighbour that way = stay put (no wrap)
    return true;
  },

  openActions: function (cell) {
    var area = cell.closest('.fm-pitch');
    if (!area) { return; }
    if (FM.pitch.areaId && FM.pitch.areaId !== area.id) {
      if (FM.pitch.src) { FM.pitch.closeMove(); }
      FM.pitch.closeActions(false);
    }
    FM.pitch.areaId = area.id;
    var menu = FM.pitch.part('[role="menu"]');
    if (!menu) { return; }
    menu.setAttribute('data-role', cell.getAttribute('data-role') || '');
    menu.setAttribute('data-pi', cell.getAttribute('data-pi') || '');
    menu.setAttribute('data-move', cell.getAttribute('data-move') || '');
    menu.setAttribute('data-slot', cell.getAttribute('data-slot') || '');
    menu.setAttribute('data-return', cell.id);
    menu.hidden = false;
    var items = menu.querySelectorAll('[role="menuitem"]');
    for (var i = 0; i < items.length; i++) { items[i].setAttribute('tabindex', i === 0 ? '0' : '-1'); }
    if (items[0]) { items[0].focus(); }
  },

  closeActions: function (focusReturn) {
    var menu = FM.pitch.part('[role="menu"]');
    if (!menu) { return; }
    menu.hidden = true;
    if (focusReturn) { var r = menu.getAttribute('data-return'); if (r) { FM.pitch.focusCell(r); } }
  },

  doAction: function (act) {
    var menu = FM.pitch.part('[role="menu"]');
    if (!menu) { return; }
    FM.pitch.moveId = menu.getAttribute('data-move');
    if (act === 'move') { menu.hidden = true; FM.pitch.openMove(menu.getAttribute('data-slot')); return; }
    var id = menu.getAttribute(act === 'role' ? 'data-role' : 'data-pi');
    // The command capability is stored in data-role/data-pi, not in a DOM
    // control with that id. Restore the actual source cell before dispatch so
    // the inherited modal/history origin capture retains this exact pitch.
    FM.pitch.closeActions(true);
    if (id) { FM.core.act(id); } // opens the game's role dropdown / instructions; the screen takes over
  },

  openMove: function (slot) {
    var move = FM.pitch.part('table[data-pitch="move"]');
    var browse = FM.pitch.part('table[data-pitch="browse"]');
    if (!move) { return; }
    FM.pitch.src = slot;
    if (browse) { browse.hidden = true; }
    move.hidden = false;
    // Per-move announcements, composed from the server's data-pos/data-name ingredients (only
    // this code knows which cell is the source): the selected player's own cell names him and
    // says selected; occupied cells spell out the swap ("swap X with Y"); empty cells the move
    // ("move X here"). Cleared wholesale by closeMove.
    var srcCell = FM.pitch.cell('move', slot);
    var srcName = (srcCell && srcCell.getAttribute('data-name')) || 'player';
    var cells = move.querySelectorAll('td[role="gridcell"]');
    for (var i = 0; i < cells.length; i++) {
      var c = cells[i];
      var pos = c.getAttribute('data-pos') || c.getAttribute('data-slot');
      if (c === srcCell) { c.setAttribute('aria-label', pos + ', ' + srcName + ', selected'); }
      else if (c.getAttribute('data-occ')) { c.setAttribute('aria-label', pos + ', swap ' + srcName + ' with ' + c.getAttribute('data-name')); }
      else { c.setAttribute('aria-label', pos + ', move ' + srcName + ' here'); }
    }
    FM.core.say('Choose a new position. Arrow keys to move, Enter to place, Escape to cancel.');
    if (srcCell) { FM.pitch.focusCell(srcCell.id); }
  },

  // focusSlot: where focus should land in the browse grid — the source by default (Escape/cancel),
  // the destination after a completed move (that's where the player, and the user's attention, is).
  closeMove: function (focusSlot) {
    var move = FM.pitch.part('table[data-pitch="move"]');
    var browse = FM.pitch.part('table[data-pitch="browse"]');
    var src = FM.pitch.src;
    FM.pitch.src = null;
    if (move) {
      move.hidden = true;
      // Clear the per-move labels (source's "current position" and the swap announcements).
      var labelled = move.querySelectorAll('td[aria-label]');
      for (var i = 0; i < labelled.length; i++) { labelled[i].removeAttribute('aria-label'); }
    }
    if (browse) { browse.hidden = false; var bc = FM.pitch.cell('browse', focusSlot || src); if (bc) { FM.pitch.focusCell(bc.id); } }
  },

  doMove: function (cell) {
    var dest = cell.getAttribute('data-slot');
    var src = FM.pitch.src;
    var areaId = FM.pitch.areaId, sourceCell = FM.pitch.cell('browse', src);
    var prefix = sourceCell ? sourceCell.id.slice(0, -src.length) : 'bc-';
    if (dest === src) {
      // Enter on the selected player's own position: keep him where he is. No move to run -
      // just leave move mode (focus returns to his browse cell, same as a cancel).
      FM.core.say('No change.');
      FM.pitch.closeMove(src);
      return;
    }
    FM.core.command('/tactics/move?source=' + encodeURIComponent(src) + '&dest=' + encodeURIComponent(dest) + '&id=' + encodeURIComponent(FM.pitch.moveId || '') + '&screen=' + encodeURIComponent(FMTOKEN), function (t) {
        FM.core.say(t);
        // The server holds the reply until the move is visible in the pitch occupants, so refresh
        // right away; the settle window still corrects anything slower. Focus follows the PLAYER:
        // the destination cell (which may only exist after the refresh — hence focusAfter, not a
        // direct focus; if the move didn't land, focusAfter misses and normal restore applies).
        // hard refresh: a swap changes the text of the very cell that holds focus, and a morph
        // there has been observed to leave the reader's view of the grid stale (only a full
        // replace - the player's F5 - cleared it). The replace plus focusAfter re-announces the
        // destination cell fresh.
        var confirmed = /^(Moved |Swapped )/.test(t), target = confirmed ? dest : src;
        if (FM.pitch.areaId === areaId) { FM.pitch.closeMove(target); }
        FM.core.focusAfter = prefix + target;
        FM.core.refresh(true);
      }, function (err, owningScreen) {
        if(owningScreen&&FM.pitch.src===src&&FM.pitch.areaId===areaId){FM.pitch.closeMove(src);FM.core.focusAfter=prefix+src;}
      });
  },

  menuArrow: function (e, item) {
    if (e.code !== 'ArrowDown' && e.code !== 'ArrowUp') { return false; }
    e.preventDefault();
    var menu = item.closest('[role="menu"]');
    var items = menu.querySelectorAll('[role="menuitem"]');
    var idx = 0;
    for (var i = 0; i < items.length; i++) { if (items[i] === item) { idx = i; } }
    var ni = e.code === 'ArrowDown' ? Math.min(items.length - 1, idx + 1) : Math.max(0, idx - 1);
    for (var j = 0; j < items.length; j++) { items[j].setAttribute('tabindex', j === ni ? '0' : '-1'); }
    items[ni].focus();
    return true;
  },

  // Key handler (registered with FM.core.onKey). Returns true once it has handled the event.
  key: function (e) {
    var t = e.target;
    if (!t || !t.getAttribute) { return false; }

    if (t.getAttribute('role') === 'gridcell' && t.closest && t.closest('.fm-pitch')) {
      var grid = t.closest('table[role="grid"]');
      var mode = grid ? grid.getAttribute('data-pitch') : null;
      if (e.code === 'ArrowUp' || e.code === 'ArrowDown' || e.code === 'ArrowLeft' || e.code === 'ArrowRight') {
        return FM.pitch.gridArrow(e, t);
      }
      if (e.code === 'Enter' || e.code === 'Space') {
        e.preventDefault();
        if (mode === 'browse') { FM.pitch.openActions(t); } else if (mode === 'move') { FM.pitch.doMove(t); }
        return true;
      }
      if (e.code === 'Escape' && mode === 'move') { e.preventDefault(); FM.pitch.closeMove(); return true; }
      return false;
    }

    if (t.getAttribute('role') === 'menuitem' && t.closest && t.closest('.fm-pitch')) {
      if (e.code === 'ArrowDown' || e.code === 'ArrowUp') { return FM.pitch.menuArrow(e, t); }
      if (e.code === 'Enter' || e.code === 'Space') { e.preventDefault(); FM.pitch.doAction(t.getAttribute('data-act')); return true; }
      if (e.code === 'Escape') { e.preventDefault(); FM.pitch.closeActions(true); return true; }
      return false;
    }
    return false;
  },
};

// --- FM.date: calendar disclosure ---
// The field is a button showing the current date; activating it opens an inline calendar panel
// (fetched from /date/cal). The panel has prev/next year and month buttons and an ARIA day grid.
// The year/month buttons re-fetch the grid and announce the new year (year buttons) or month
// (month buttons); the day grid is navigated with the arrow keys (roving tabindex), each cell
// announcing its full date, and arrowing past a month edge flips to the adjacent month. Enter/Space
// (or click) picks a day and applies it via /date/set: on success the page refreshes (collapsing
// the panel); otherwise the reply is spoken and the panel stays open. Disabled days stay navigable
// but inert.
FM.date = {
  openId: null,
  generation: 0,
  picking: false,
  pad: function (n) { return n < 10 ? '0' + n : '' + n; },
  daysInMonth: function (y, m) { return new Date(y, m, 0).getDate(); },

  toggle: function (id) {
    var panel = document.getElementById(id + '-panel');
    if (!panel) { return; }
    if (panel.firstChild) { FM.date.close(id); } else { FM.date.openCal(id, 0, 0); }
  },

  // Fetch the grid for a month and run onLoaded(panel) once it is in the DOM.
  fetchMonth: function (id, y, m, onLoaded) {
    var owner = FM.core.screenIdentity(FMTOKEN), generation = ++FM.date.generation;
    FM.date.openId = id; FM.core.dd = id;
    var url = '/date/cal?id=' + encodeURIComponent(id) + '&screen=' + encodeURIComponent(FMTOKEN) + (y ? ('&year=' + y + '&month=' + m) : '');
    FM.core.command(url, function (h) {
      if (generation !== FM.date.generation || FM.date.openId !== id || owner !== FM.core.screenIdentity(FMTOKEN)) { return; }
      if (h.charAt(0) !== '<') { FM.date.close(id); FM.core.say(h); return; }
      var panel = document.getElementById(id + '-panel');
      if (!panel) { FM.date.teardown(); return; }
      panel.innerHTML = h;
      var grid = panel.querySelector('[data-cal-grid]'), label = panel.querySelector('[data-cal-label]');
      if (grid && label) { label.id = id + '-cal-label'; grid.setAttribute('aria-labelledby', label.id); }
      onLoaded(panel);
    }, function (err, owningScreen) {
      if (owningScreen && generation === FM.date.generation) { FM.date.close(id, true); }
    });
  },

  openCal: function (id, y, m) {
    if (FM.core.dd && FM.core.dd !== id) {
      var previous = document.getElementById(FM.core.dd), oldId = FM.core.dd;
      FM.core.dd = null;
      FM.core.command((FM.search.is(previous) ? '/searchdropdown/close?id=' : '/dropdown/close?id=') + encodeURIComponent(oldId), function () {
        if (previous && FM.search.is(previous)) { FM.search.clearList(previous); }
        FM.dropdown.teardown(); FM.date.openCal(id, y, m);
      });
      return;
    }
    FM.date.fetchMonth(id, y, m, function (panel) {
      var btn = document.getElementById(id);
      if (btn) { btn.setAttribute('aria-expanded', 'true'); }
      // Land on the roving cell (the selected day, else day 1), else the month label.
      var f = panel.querySelector('[data-cal-grid] [tabindex="0"]') || panel.querySelector('[data-cal-label]');
      if (f) { f.focus(); }
    });
  },

  // Year/month nav buttons: re-fetch the grid, announce the new year or month, and keep focus on
  // the pressed button so repeated presses keep working.
  target: function (panel, suffix) {
    var targets = panel.querySelector('[data-cal-nav]'), raw = targets && targets.getAttribute('data-' + suffix);
    if (!raw) { return null; }
    return raw.split('/').map(Number);
  },

  nav: function (id, dy, dm) {
    var panel = document.getElementById(id + '-panel');
    if (!panel) { return; }
    var root = panel.querySelector('[data-cal]');
    if (!root) { return; }
    var oldCy = parseInt(root.getAttribute('data-cy'), 10);
    var oldCm = parseInt(root.getAttribute('data-cm'), 10);
    var suffix = dy < 0 ? 'py' : dy > 0 ? 'ny' : dm < 0 ? 'pm' : 'nm';
    var target = FM.date.target(panel, suffix); if (!target) { return; }
    var ny = target[0], nm = target[1], focusId = id + '-' + suffix;
    FM.date.fetchMonth(id, ny, nm, function (p) {
      var lbl = p.querySelector('[data-cal-label]');
      var text = lbl ? lbl.textContent : (nm + ' ' + ny);
      var parts = text.split(' ');
      // Year button -> the year; month button -> the month (full label when it crossed a year).
      var msg = dy !== 0 ? (nm !== oldCm ? text : (parts[1] || ('' + ny))) : (ny !== oldCy ? text : (parts[0] || text));
      FM.core.say(msg);
      var b = document.getElementById(focusId);
      if (b && !b.disabled) { b.focus(); }
      else { var l = p.querySelector('[data-cal-label]'); if (l) { l.focus(); } }
    });
  },

  // Move the roving tabindex to a day cell in the current grid and focus it.
  moveTo: function (grid, day) {
    var target = grid.querySelector('[data-day="' + day + '"]');
    if (!target) { return false; }
    var cells = grid.querySelectorAll('[data-day]');
    for (var i = 0; i < cells.length; i++) { cells[i].setAttribute('tabindex', '-1'); }
    target.setAttribute('tabindex', '0');
    target.focus();
    return true;
  },

  // Arrow past a month edge: load the adjacent month and focus the wrapped day (so navigation is
  // continuous, like a wall calendar).
  flip: function (id, y, m, focusDay) {
    FM.date.fetchMonth(id, y, m, function (panel) {
      var grid = panel.querySelector('[data-cal-grid]');
      if (grid) { FM.date.moveTo(grid, focusDay); }
    });
  },

  // A day the controller does not permit. Every disabled cell carries this as its click handler,
  // which is what makes a screen reader treat the cell as a control and read its full-date
  // aria-label instead of the bare day number; activating one says why nothing happened.
  unavailable: function () { FM.core.say('That date is not available.'); },

  pick: function (id, value) {
    if (FM.date.picking) { return; }
    var generation = FM.date.generation, owner = FM.core.screenIdentity(FMTOKEN);
    FM.date.picking = true;
    FM.core.command('/date/set?id=' + encodeURIComponent(id) + '&value=' + encodeURIComponent(value) + '&screen=' + encodeURIComponent(FMTOKEN), function (t) {
        if (generation !== FM.date.generation || FM.date.openId !== id || owner !== FM.core.screenIdentity(FMTOKEN)) { return; }
        FM.date.picking = false;
        FM.core.say(t);
        if (t.indexOf('Set to ') === 0) {
          FM.date.teardown();
          // Move focus to the disclosure button (stable id) so the refresh restores focus there,
          // collapsed and showing the new date. Hard refresh, like the pitch move: the focused
          // button's own text is what changes, and a morph under focus leaves the reader's view
          // stale (only the player's F5 cleared it). The full replace rebuilds the buffer and the
          // restore focuses a FRESH button, announcing the new date.
          var b = document.getElementById(id);
          if (b) { b.focus(); }
          FM.core.refresh(true);
        }
        // Otherwise the date was refused: spoken above, panel stays open for another pick.
      }, function (err, owningScreen) {
        if (owningScreen && FM.date.openId === id) { FM.date.close(id, true); }
      });
  },

  // The calendar shares the existing interaction gate with dropdowns. Releasing
  // it also invalidates a late month response, so Escape cannot reopen the grid.
  teardown: function () {
    var id = FM.date.openId;
    FM.date.openId = null; FM.date.generation++; FM.date.picking = false;
    if (!id) { return; }
    var panel = document.getElementById(id + '-panel');
    if (panel) { panel.innerHTML = ''; }
    var btn = document.getElementById(id);
    if (btn) { btn.setAttribute('aria-expanded', 'false'); }
    if (FM.core.dd === id) { FM.core.dd = null; }
  },

  close: function (id, noRefresh) {
    FM.date.teardown();
    var btn = document.getElementById(id);
    if (btn) { btn.focus(); }
    if (!noRefresh && FM.core.ddpend) { FM.core.ddpend = false; FM.core.refresh(); }
  },

  // Day-grid keys (roving-tabindex arrow navigation + Enter/Space to pick); Left/Right = +/-1 day,
  // Up/Down = +/-7 days (same weekday column), Home/End = first/last day. target outside the month
  // flips to the adjacent month if its nav button is enabled.
  gridKey: function (e, cell) {
    var grid = cell.closest('[data-cal-grid]');
    var panel = cell.closest('.fm-date-panel');
    if (!grid || !panel) { return false; }
    var id = panel.id.replace(/-panel$/, '');
    var root = panel.querySelector('[data-cal]');
    var cy = parseInt(root.getAttribute('data-cy'), 10);
    var cm = parseInt(root.getAttribute('data-cm'), 10);
    var day = parseInt(cell.getAttribute('data-day'), 10);
    var n = parseInt(grid.getAttribute('data-days'), 10);

    if (e.code === 'Enter' || e.code === 'Space') {
      e.preventDefault();
      if (cell.getAttribute('data-dis')) { FM.date.unavailable(); return true; }
      FM.date.pick(id, FM.date.pad(day) + '/' + FM.date.pad(cm) + '/' + cy);
      return true;
    }
    if (e.code === 'Home') { e.preventDefault(); FM.date.moveTo(grid, 1); return true; }
    if (e.code === 'End') { e.preventDefault(); FM.date.moveTo(grid, n); return true; }

    var delta = e.code === 'ArrowLeft' ? -1 : e.code === 'ArrowRight' ? 1
      : e.code === 'ArrowUp' ? -7 : e.code === 'ArrowDown' ? 7 : 0;
    if (delta === 0) { return false; }
    e.preventDefault();

    var target = day + delta;
    if (target >= 1 && target <= n) { FM.date.moveTo(grid, target); return true; }

    if (target < 1) {
      var pm = document.getElementById(id + '-pm');
      if (!pm || pm.disabled) { return true; } // at the lower bound: consume, don't move
      var previous = FM.date.target(panel, 'pm'); if (!previous) { return true; }
      var pmY = previous[0], pmM = previous[1];
      FM.date.flip(id, pmY, pmM, FM.date.daysInMonth(pmY, pmM) + target);
    } else {
      var nm = document.getElementById(id + '-nm');
      if (!nm || nm.disabled) { return true; } // at the upper bound
      var next = FM.date.target(panel, 'nm'); if (!next) { return true; }
      var nmY = next[0], nmM = next[1];
      FM.date.flip(id, nmY, nmM, target - n);
    }
    return true;
  },

  key: function (e) {
    // Escape closes an open calendar when focus is inside its panel.
    if (e.code === 'Escape') {
      var p = e.target && e.target.closest ? e.target.closest('.fm-date-panel') : null;
      var pendingButton = FM.date.openId && e.target && e.target.id === FM.date.openId;
      if ((!p || !p.firstChild) && !pendingButton) { return false; }
      e.preventDefault();
      FM.date.close(pendingButton ? FM.date.openId : p.id.replace(/-panel$/, ''));
      return true;
    }
    // Day-grid navigation when focus is on a day cell.
    var cell = e.target;
    if (cell && cell.getAttribute && cell.getAttribute('data-day') !== null
      && cell.closest && cell.closest('[data-cal-grid]')) {
      return FM.date.gridKey(e, cell);
    }
    return false;
  },
};

// --- FM.attrs: client-side sort for the player-report attribute tables ---
// These tables are mod-built (no game sort control behind them), so the Stat header sorts the
// rendered rows in place by the value at the end of each stat cell ("Composure 15" -> 15):
// first press best-first (descending), next press worst-first, toggling. Sort state lives in
// the header's aria-sort only, so a body refresh restores the game's order.
FM.attrs = {
  sort: function (tid) {
    var t = document.getElementById(tid);
    if (!t || !t.tHead || !t.tHead.rows[0] || !t.tBodies[0]) { return; }
    var th = t.tHead.rows[0].cells[0];
    var tb = t.tBodies[0];
    var descending = th.getAttribute('aria-sort') !== 'descending';
    var num = function (r) {
      var m = (r.cells[0] ? r.cells[0].textContent : '').match(/(\d+(\.\d+)?)\s*$/);
      return m ? parseFloat(m[1]) : -Infinity;
    };
    var rows = Array.prototype.slice.call(tb.rows);
    rows.sort(function (a, b) { return descending ? num(b) - num(a) : num(a) - num(b); });
    for (var i = 0; i < rows.length; i++) { tb.appendChild(rows[i]); }
    th.setAttribute('aria-sort', descending ? 'descending' : 'ascending');
  },
};

// --- FM.ti: team-instruction selects (tactics Team Instructions table) ---
// Follows the dropdown widget's interaction contract exactly: the collapsed select holds only
// the current value's SHORT name; the first interaction populates the full option list - each
// option carrying the game's per-option description, as the game's own editor rows read
// ("Much Shorter, Strongly emphasize shorter passes., Retain the ball.") - so browsing hears the
// descriptions. Browsing is completely local, ENTER is the only commit (through
// /teaminstructions/set, which drives the game's own quick-change menu invisibly and verifies
// the result), and leaving any other way (Escape, Tab, quick-nav) reverts to the collapsed
// short value without committing. Unlike data-dd the options are static catalog data embedded
// in data attributes, so populate is synchronous (no load window, no buffering) and cancel has
// no game popup to close. FM.core.dd is set while populated, exactly like an open dropdown
// session, so SSE refreshes defer instead of wiping the browsed list mid-interaction.
FM.ti = {
  is: function (el) { return el && el.tagName === 'SELECT' && el.hasAttribute('data-ti'); },

  names: function (s) { return JSON.parse(s.getAttribute('data-ti-names') || '[]'); },

  // Swap in the full descriptive option list (synchronous - the data is already in the DOM),
  // stash the collapsed short value for restore, and adopt the browse session. A remote row
  // passes its server-read current index and, when the server read option-level locks from the
  // game's editor, the locked indices - those options stay in the list (the game shows them
  // too) but carry the unavailability right after their name, so the player hears it while
  // browsing instead of discovering it on a refused commit.
  populate: function (s, forceIdx, lockedIdx) {
    var longs = JSON.parse(s.getAttribute('data-ti-longs') || '[]');
    if (!longs.length) { return false; }
    var cur = (forceIdx === undefined) ? parseInt(s.getAttribute('data-ti-cur'), 10) : forceIdx;
    s.setAttribute('data-orig', s.options[0] ? s.options[0].text : '');
    while (s.options.length) { s.remove(0); }
    for (var i = 0; i < longs.length; i++) {
      var o = document.createElement('option');
      var txt = longs[i];
      if (lockedIdx && lockedIdx.indexOf(i) >= 0) {
        var comma = txt.indexOf(',');
        var marker = ', unavailable for this position';
        txt = comma >= 0 ? txt.substring(0, comma) + marker + txt.substring(comma) : txt + marker;
      }
      o.text = txt;
      s.add(o);
    }
    s.selectedIndex = (cur >= 0 && cur < longs.length) ? cur : 0;
    s.setAttribute('data-loaded', '1');
    FM.core.dd = s.id;
    return true;
  },

  // A remote row's populate needs a server round trip (/teaminstructions/remotestate drives the
  // game's editor card to read the value the game shows nowhere else), so it follows data-dd's
  // load protocol: one request in flight, arrows pressed during the load buffer and replay onto
  // the populated list, Escape abandons, and a superseded or unwanted response never touches the
  // page. Enter/Space prime without queueing a move.
  pending: null,
  primeRemote: function (s, code) {
    var p = FM.ti.pending;
    if (p && p.id === s.id) {
      if (code === 'Escape') { p.cancelled = true; return; }
      if ((code === 'ArrowDown' || code === 'ArrowUp' || code === 'Home' || code === 'End')
        && p.keys.length < 20) { p.keys.push(code); }
      return;
    }
    if (FM.core.dd && FM.core.dd !== s.id) { return; }   // another popup session owns the screen
    p = { id: s.id, keys: [], cancelled: false, timer: 0 };
    if (code === 'ArrowDown' || code === 'ArrowUp' || code === 'Home' || code === 'End') { p.keys.push(code); }
    p.timer = setTimeout(function () {
      if (FM.ti.pending === p && !p.cancelled) { FM.core.say('Loading'); }
    }, 400);
    FM.ti.pending = p;
    // Hold the refresh deferral for the WHOLE load, not just from populate: the state session
    // drives a game editor open and closed, and the screen events that causes would otherwise
    // morph the page mid-load and wipe the select the response is about to populate.
    FM.core.dd = s.id;
    var release = function () {
      FM.core.dd = null;
      if (FM.core.ddpend) { FM.core.ddpend = false; FM.core.refresh(); }
    };
    var stateRoute = s.getAttribute('data-ti-state') || '/teaminstructions/remotestate';
    FM.core.requestText(stateRoute + '?id=' + encodeURIComponent(s.id))
      .then(function (t) {
        clearTimeout(p.timer);
        if (FM.ti.pending !== p) { return; }
        FM.ti.pending = null;
        var live = document.getElementById(p.id);
        if (p.cancelled || !live || !FM.ti.is(live) || document.activeElement !== live) { release(); return; }
        if (t.indexOf('idx:') !== 0) { release(); FM.core.say(t); return; }
        // "idx:N" or "idx:N;locked:i,j" - locked indices mark unavailable options in the list.
        var parts = t.split(';locked:');
        var lockedIdx = (parts.length > 1 && parts[1]) ? parts[1].split(',').map(Number) : null;
        FM.ti.populate(live, parseInt(parts[0].substring(4), 10), lockedIdx);
        var i = live.selectedIndex;
        var last = live.options.length - 1;
        for (var k = 0; k < p.keys.length; k++) {
          var c = p.keys[k];
          if (c === 'ArrowDown') { i = Math.min(i + 1, last); }
          else if (c === 'ArrowUp') { i = Math.max(i - 1, 0); }
          else if (c === 'Home') { i = 0; }
          else if (c === 'End') { i = last; }
        }
        live.selectedIndex = i;
        var o = live.options[i];
        if (o) { FM.core.say(o.text); }
      })
      .catch(function (err) {
        clearTimeout(p.timer);
        FM.core.clog('ti remotestate failed: ' + (err && err.message ? err.message : err));
        if (FM.ti.pending === p) {
          FM.ti.pending = null;
          release();
          FM.core.say('The setting did not load.');
        }
      });
  },

  // First keypress: populate, apply the priming key's movement, and confirm the landed option
  // through Prism - swapping options under focus is silent for the screen reader (the data-dd
  // replay rule). Enter/Space prime without moving, confirming the current value.
  prime: function (s, code) {
    if (!FM.ti.populate(s)) { return; }
    var i = s.selectedIndex;
    var last = s.options.length - 1;
    if (code === 'ArrowDown') { i = Math.min(i + 1, last); }
    else if (code === 'ArrowUp') { i = Math.max(i - 1, 0); }
    else if (code === 'Home') { i = 0; }
    else if (code === 'End') { i = last; }
    s.selectedIndex = i;
    var o = s.options[i];
    if (o) { FM.core.say(o.text); }
  },

  // Rebuild as a FRESH collapsed node showing the game's short value, like FM.dropdown.restore:
  // after browsing, a screen reader caches the last-browsed option beside the true value on the
  // same node; a new node forces a clean re-read. Re-entrancy guard: replacing the focused node
  // fires focusout synchronously, whose revert listener would restore the same node again - the
  // nested replacement then steals the node and the outer one throws, dropping focus to body.
  restoring: false,
  restore: function (s, focus) {
    if (FM.ti.restoring || !s.parentNode) { return s; }
    FM.ti.restoring = true;
    try {
      if (FM.core.dd === s.id) { FM.core.dd = null; }
      if (!s.getAttribute('data-loaded')) { return s; }   // never opened - nothing to revert
      var fresh = document.createElement('select');
      fresh.id = s.id;
      fresh.setAttribute('data-ti', '1');
      var keep = ['data-ti-cur', 'data-ti-remote', 'data-ti-set', 'data-ti-state', 'data-ti-names', 'data-ti-longs', 'aria-label'];
      for (var k = 0; k < keep.length; k++) {
        var v = s.getAttribute(keep[k]);
        if (v !== null) { fresh.setAttribute(keep[k], v); }
      }
      var op = document.createElement('option');
      op.text = s.getAttribute('data-orig') || '';
      op.selected = true;
      fresh.add(op);
      s.parentNode.replaceChild(fresh, s);
      if (focus) { fresh.focus(); }
      return fresh;
    } finally { FM.ti.restoring = false; }
  },

  // Id of the select whose commit is in flight, so the focusout revert stands down for it (the
  // commit's own resolution rebuilds the DOM); the deferred-refresh flag stays on FM.core.dd.
  committing: null,

  commit: function (s) {
    if (FM.core.dd && FM.core.dd !== s.id) { return; }   // another popup session owns the screen
    var idx = s.selectedIndex;
    var short = FM.ti.names(s)[idx];
    if (short === undefined) { return; }
    FM.ti.committing = s.id;
    var route = s.getAttribute('data-ti-set')
      || (s.hasAttribute('data-ti-remote') ? '/teaminstructions/remoteset' : '/teaminstructions/set');
    FM.core.command(route + '?id=' + encodeURIComponent(s.id) + '&opt=' + idx + '&screen=' + encodeURIComponent(FMTOKEN), function (t) {
        FM.ti.committing = null;
        FM.core.dd = null;
        var live = document.getElementById(s.id);
        if (t === 'ok') {
          // Spoken confirm matching the dropdown commit ("<option> selected", the short name),
          // then the flat dropdown's post-commit flow: keep position beside the combo, blur out
          // of it after the refresh restores focus.
          FM.core.say(short + ' selected');
          if (live) { live.focus(); FM.core.blur = true; }
          FM.core.refresh();
        } else {
          FM.core.say(t);
          if (live && FM.ti.is(live)) { FM.ti.restore(live, true); }
          FM.core.refresh();
        }
      }, function () {
        if(FM.ti.committing===s.id)FM.ti.committing = null;
        if(FM.core.dd===s.id)FM.core.dd = null;
      });
  },

  key: function (e) {
    var s = e.target;
    if (!FM.ti.is(s)) { return false; }
    if (!s.getAttribute('data-loaded')) {
      // First interaction populates; the priming arrow moves, Enter/Space confirm in place.
      // Remote rows load their current value from the server first; Escape abandons that load.
      var remote = s.hasAttribute('data-ti-remote');
      if (e.code === 'ArrowDown' || e.code === 'ArrowUp' || e.code === 'Home' || e.code === 'End'
        || e.code === 'Enter' || e.code === 'Space') {
        e.preventDefault();
        if (remote) { FM.ti.primeRemote(s, e.code); } else { FM.ti.prime(s, e.code); }
        return true;
      }
      if (remote && e.code === 'Escape' && FM.ti.pending && FM.ti.pending.id === s.id) {
        e.preventDefault();
        FM.ti.primeRemote(s, 'Escape');
        return true;
      }
      return false;
    }
    if (e.code === 'Enter') { e.preventDefault(); FM.ti.commit(s); return true; }
    if (e.code === 'Escape') { e.preventDefault(); FM.ti.restore(s, true); return true; }
    // Sideways would move the selection identically to Up/Down - accidental noise, like data-dd.
    if ((e.code === 'ArrowLeft' || e.code === 'ArrowRight') && !e.altKey) { e.preventDefault(); return true; }
    return false;
  },
};

// Keep a priority action on its player when the game's rows reorder. The rendered
// attributes describe this interaction only; the refreshed game remains authoritative.
FM.takers = {
  pending: null,
  panelAct: function (id) {
    var table = document.querySelector('table[data-takers]');
    FM.takers.settleUntil = Date.now() + 2000;
    return FM.core.command('/activate?id=' + encodeURIComponent(id) + '&screen=' + encodeURIComponent(FMTOKEN), function (t) {
      if (!FM.core.actResult(t)) { FM.takers.pending = { action: 'panel', control: id, section: table.id }; }
      FM.core.refresh(true);
      var token = FMTOKEN;
      setTimeout(function () {
        if (FMTOKEN === token && document.getElementById(table.id) && !FM.core.dd) { FM.core.refresh(true, true); }
      }, 750);
    });
  },
  syncEmpty: function () {
    var tables = document.querySelectorAll('table[data-takers]');
    for (var i = 0; i < tables.length; i++) {
      var status = tables[i].nextElementSibling;
      var option = status && status.hasAttribute('data-taker-status') ? status.nextElementSibling : null;
      // Without a first taker the game's default-both-sides label loses its
      // binding and falls back to "Name". There is no player for this action.
      if (option && option.querySelector('input[type="checkbox"]')) {
        option.hidden = !tables[i].querySelector('[data-taker-row]');
      }
    }
  },
  addCommit: function (select) {
    if (!select || !select.hasAttribute('data-taker-add')) { return false; }
    var table = select.parentNode.nextElementSibling;
    if (!table || !table.hasAttribute('data-takers')) { return false; }
    var before = [];
    var rows = table.querySelectorAll('[data-taker-row]');
    for (var i = 0; i < rows.length; i++) { before.push(rows[i].getAttribute('data-taker-row')); }
    FM.takers.pending = { section: table.id, action: 'add', before: before, select: select.id };
    FM.dropdown.teardown();
    FM.core.refresh(true);
    return true;
  },
  act: function (id) {
    var button = document.getElementById(id);
    if (!button || button.disabled) { return; }
    var target = { player: button.getAttribute('data-taker'),
      section: button.getAttribute('data-taker-section'), action: button.getAttribute('data-taker-action'),
      index: button.closest('tr').sectionRowIndex };
    return FM.core.command('/activate?id=' + encodeURIComponent(id) + '&screen=' + encodeURIComponent(FMTOKEN), function (t) {
      if (!FM.core.actResult(t)) { FM.takers.pending = target; }
      // Rebuild the browse buffer: preserving an active button during a morph can
      // retain the previous player's name at the row's old position.
      FM.core.refresh(true);
    });
  },
  afterRefresh: function () {
    FM.takers.syncEmpty();
    var target = FM.takers.pending;
    if (!target) { return; }
    FM.takers.pending = null;
    if (target.action === 'panel') {
      if (!document.getElementById(target.section)) { return; }
      var control = document.getElementById(target.control);
      // Conversation choices are replaced by the game's reply and its next action.
      if (!control) { control = document.querySelector('#fmroot button'); }
      if (control && !control.disabled) { control.focus(); }
      return;
    }
    var rows = document.querySelectorAll('[data-taker-row]');
    if (target.action === 'add') {
      var added = [];
      for (var a = 0; a < rows.length; a++) {
        var name = rows[a].getAttribute('data-taker-row');
        if (rows[a].getAttribute('data-taker-section') === target.section && target.before.indexOf(name) < 0) { added.push(name); }
      }
      if (added.length !== 1) {
        var select = document.getElementById(target.select);
        if (select) { select.focus(); }
        return;
      }
      target.player = added[0];
    }
    var matching = [];
    for (var i = 0; i < rows.length; i++) {
      if (rows[i].getAttribute('data-taker-row') === target.player &&
          rows[i].getAttribute('data-taker-section') === target.section) { matching.push(rows[i]); }
    }
    // A removed row leaves focus on its successor, or the Add Player control
    // when the list is empty. A different screen has no matching table.
    if (matching.length === 0 && target.action === 'remove') {
      var table = document.getElementById(target.section);
      if (!table) { return; }
      var remaining = table.querySelectorAll('[data-taker-row]');
      var next = remaining[Math.min(target.index, remaining.length - 1)];
      var replacement = next ? next.querySelector('th[scope="row"] button')
        : table.previousElementSibling.querySelector('select');
      if (replacement) { replacement.focus(); }
      return;
    }
    // Ambiguous names must not move focus to a different player.
    if (matching.length !== 1) { return; }
    var row = matching[0];
    var buttons = row.querySelectorAll('button[data-taker-action]');
    var focus = null;
    for (var j = 0; j < buttons.length; j++) {
      if (buttons[j].getAttribute('data-taker-action') === target.action && !buttons[j].disabled) { focus = buttons[j]; }
    }
    if (!focus) { focus = row.querySelector('th[scope="row"] button'); }
    if (focus) { focus.focus(); }
    var statuses = document.querySelectorAll('[data-taker-status]');
    var priority = row.querySelector('td');
    var heading = document.getElementById(target.section);
    for (var k = 0; k < statuses.length; k++) {
      if (statuses[k].getAttribute('data-taker-status') === target.section && priority && heading) {
        statuses[k].textContent = target.player + ', ' + priority.firstChild.textContent.trim() + ', ' + heading.getAttribute('aria-label');
      }
    }
  }
};
FM.core.onRefresh(FM.takers.afterRefresh);
FM.takers.syncEmpty();

// Move a set-piece instruction chip to another category (the mod's non-drag path). The select
// follows the surface's contract — arrows browse the destinations locally (the options are
// static and already in the DOM), ENTER is the only commit, and leaving any other way (Escape,
// Tab, quick-nav) reverts to the resting current-category value without moving anything. The
// commit is a registered move handle behind /activate; the server holds the reply until the
// game confirms (or honestly refuses) the move. The chip re-renders inside the destination with
// a new slot code, so focus lands on the destination's header button instead — its updated
// count announces the result of the move.
FM.spmove = {
  pending: null,
  // The select whose commit is in flight (node identity — move selects carry no id), so the
  // focusout revert stands down for it; its own resolution refreshes the page.
  committing: null,
  is: function (el) { return el && el.tagName === 'SELECT' && el.hasAttribute('data-spmove'); },
  reset: function (sel, speak) {
    if (sel.selectedIndex === 0) { return; }
    sel.selectedIndex = 0;
    // Reverting under focus is silent for the screen reader; confirm the resting value.
    if (speak && sel.options[0]) { FM.core.say(sel.options[0].text); }
  },
  key: function (e) {
    var s = e.target;
    if (!FM.spmove.is(s)) { return false; }
    // Enter on the resting value has nothing to commit (commit ignores the empty value).
    if (e.code === 'Enter') { e.preventDefault(); FM.spmove.commit(s); return true; }
    if (e.code === 'Escape') { e.preventDefault(); FM.spmove.reset(s, true); return true; }
    // Sideways would move the selection identically to Up/Down - accidental noise, like data-dd.
    if ((e.code === 'ArrowLeft' || e.code === 'ArrowRight') && !e.altKey) { e.preventDefault(); return true; }
    return false;
  },
  commit: function (sel) {
    var id = sel.value;
    if (!id) { return; }
    var opt = sel.selectedOptions && sel.selectedOptions[0];
    FM.spmove.pending = opt ? opt.getAttribute('data-h') : null;
    FM.spmove.committing = sel;
    return FM.core.command('/activate?id=' + encodeURIComponent(id) + '&screen=' + encodeURIComponent(FMTOKEN), function (t) {
      FM.spmove.committing = null;
      if (t.indexOf('Moved ') !== 0) { FM.spmove.pending = null; sel.selectedIndex = 0; }
      FM.core.say(t);
      FM.core.refresh(true);
    }, function () {
      if(FM.spmove.committing===sel){FM.spmove.committing=null;FM.spmove.pending=null;sel.selectedIndex=0;}
    });
  },
  afterRefresh: function () {
    var target = FM.spmove.pending;
    if (!target) { return; }
    FM.spmove.pending = null;
    // The destination header button, found by its leading category name ("Box Threats, (1/9)").
    var buttons = document.querySelectorAll('#fmroot button');
    for (var i = 0; i < buttons.length; i++) {
      if (buttons[i].textContent.indexOf(target) === 0) { buttons[i].focus(); return; }
    }
  }
};
FM.core.onRefresh(FM.spmove.afterRefresh);
// --- Public API: the global names the server-rendered HTML calls via inline handlers. ---
function refresh() { return FM.core.refresh(); }
function fmtaker(id) { return FM.takers.act(id); }
function act(id) { return FM.core.act(id); }
function actradio(id) { return FM.core.actradio(id); }
FM.keycapture = {
  queue: Promise.resolve(),
  press: function (event, input) {
    if (input.readOnly) { return; }
    if (event.code === 'Tab') { return; }
    event.stopPropagation();
    event.preventDefault();
    if (event.repeat) { return; }
    // Escape belongs to the native rebinding operation while capture is
    // active. Cancel it through the same setter, before any card-close action.
    // Browser modifier keys are logical controls, matching the game's standard
    // <Keyboard>/ctrl, /shift and /alt bindings across either physical side.
    var id = input.id, code = /^(Control|Shift|Alt)$/.test(event.key) ? event.key : event.code;
    this.queue = this.queue.then(function () {
      return FM.core.command('/settext?id=' + encodeURIComponent(id) + '&screen=' + encodeURIComponent(FMTOKEN) + '&value=' + encodeURIComponent(code),
        function (result) { FM.core.actResult(result); FM.core.refresh(); });
    }).catch(function () { FM.core.refresh(); });
  }
};
FM.core.onRefresh(function () {
  document.querySelectorAll('input[data-keycapture]').forEach(function (input) {
    // This input displays a native capture result; it is never free text being
    // typed by the user. Synchronize it despite morph's active-edit protection.
    var value = input.getAttribute('data-key-value') || '';
    if (input.value !== value) { input.value = value; }
  });
});
function settext(id, v) { return FM.core.settext(id, v); }
function setvalue(id, v) { return FM.core.setvalue(id, v); }
function step(id, d) { return FM.core.step(id, d); }
function fmplayer(id) { return FM.player.toggle(id); }
function fmpselect(idx) { return FM.player.select(idx); }
function fmtoggle(bid, pid) { return FM.pitch.toggle(bid, pid); }
function fmdate(id) { return FM.date.toggle(id); }
function fmsearch(el) { return FM.search.onInput(el); }
function fmcalnav(id, dy, dm) { return FM.date.nav(id, dy, dm); }
function fmcalpick(id, value) { return FM.date.pick(id, value); }
function fmcaldis() { return FM.date.unavailable(); }
// Synthetic Back on a covering overlay (card/modal) shown alone: the same history-back as Alt+[.
function fmback() { return FM.core.failPost('/history?dir=back'); }
// Open an email (news-event button) in the Messages reading pane, landing focus on its body.
function fmmsg(id) { return FM.messages.open(id); }
// Toggle the sort of an attribute table's Value column.
function fmattrs(tid) { return FM.attrs.sort(tid); }

// --- Wiring ---

document.addEventListener('mousedown', function (e) {
  var s = e.target;
  if (FM.dropdown.is(s) && !s.getAttribute('data-loaded')) { FM.dropdown.prime(s); }
  // A mouse press on a collapsed team-instruction select populates it synchronously, so the
  // native popup that opens on this same press already shows the full descriptive list. A remote
  // row's load is asynchronous, so its first press only starts the load - the second shows it.
  if (FM.ti.is(s) && !s.getAttribute('data-loaded')) {
    if (s.hasAttribute('data-ti-remote')) { FM.ti.primeRemote(s, ''); } else { FM.ti.populate(s); }
  }
});

// Enter is the only commit (FM.dropdown.key). Leaving a populated select any other way cancels:
// the displayed value reverts to what the game is showing, so browsing without pressing Enter
// never touches the game.
document.addEventListener('focusout', function (e) {
  var s = e.target;
  if (!FM.dropdown.is(s) || !FM.core.dd || !s.getAttribute('data-loaded')) { return; }
  // Player sub-selects commit on Enter only: a cancel on every blur would thrash focus and could
  // fire while moving between sibling boxes. Leaving a sub-select just closes it; the next
  // interaction (or Escape/collapse) cleans it up.
  if (FM.core.player) { return; }
  // Focus moving to another dropdown select (chain child, refocused parent, or a different
  // dropdown whose first interaction takes over) must not cancel.
  if (FM.dropdown.is(e.relatedTarget)) { return; }
  FM.dropdown.cancel();
});

// Team-instruction selects follow the same contract: leaving a populated select without Enter
// reverts it to the collapsed short value, so browsing never commits. Skipped while this
// select's own commit is in flight (its resolution handles the DOM).
document.addEventListener('focusout', function (e) {
  var s = e.target;
  if (!FM.ti.is(s) || FM.ti.committing === s.id) { return; }
  if (s.getAttribute('data-loaded')) { FM.ti.restore(s); }
});

// Set-piece Move selects: leaving without Enter reverts to the resting current-category value,
// so browsing destinations never moves anything. Skipped while the select's own commit is in
// flight (its resolution refreshes the page).
document.addEventListener('focusout', function (e) {
  var s = e.target;
  if (!FM.spmove.is(s) || FM.spmove.committing === s) { return; }
  FM.spmove.reset(s);
});

// Search combobox: the listbox options are not focusable (focus stays in the box via
// aria-activedescendant), so any focusout from the box means leaving the widget - cancel the
// session and close the game popup and listbox.
document.addEventListener('focusout', function (e) {
  if (FM.search.is(e.target)) { FM.search.cancel(e.target); }
});

// Shift+F10, the Applications key, and a physical right-click all fire 'contextmenu'; open the focused
// control's in-game menu and suppress the browser's own only when the control declares one.
document.addEventListener('contextmenu', function (e) {
  var owner = FM.contextmenu.owner(e);
  if (!owner) { return; }
  e.preventDefault();
  FM.contextmenu.open(owner.id);
});

// --- FM.navigator: the Menu browser (search box + destination tree, one widget, two homes) ---
// The combined widget lives on the title bar (data-home="page") and in a modal Navigator page
// (data-home="modal", NavigatorSurface) for screens without the title bar. Alt+M — routed through
// /matchchord, which owns the match gate — is THE key: it focuses the page tree, opens the modal
// where there is none, and closes an open modal (see focusMenuTree). Typing filters the tree from
// either the box or the tree itself (data-filter = label + owning path, lowercased); Enter
// navigates via /navigator/go (the server re-resolves the destination live); Escape clears the
// query, then closes the modal.
FM.navigator = {
  wantFocus: false,
  query: '',
  queryContext: null,
  refreshFocus: null,
  matches: [],   // the current filter's matched treeitems, in tree order
  sayTimer: null,
  input: function () { return document.getElementById('fmnavq'); },

  // Both homes carry the search box now, so "the input exists" no longer identifies the open
  // modal — the served data-home attribute does.
  modalOpen: function () {
    var input = FM.navigator.input();
    return !!(input && input.getAttribute('data-home') === 'modal');
  },


  close: function () {
    FM.core.command('/navigator/close', function (t) {
      if (t) { FM.core.say(t); }
      FM.core.refresh();
    });
  },

  // Post-refresh hook: any refresh while the page is open re-serves the full unfiltered list, so
  // first re-apply the typed filter (a settle-window morph must never silently un-filter under
  // the player); then the one-shot focus request from the open lands focus in the box once the
  // markup is actually in the DOM.
  focusBox: function () {
    // After any refresh the widget is served fresh (full collapsed tree, empty box). Restore
    // the client state: a live query re-filters (its DOM pruning was wiped by the morph);
    // otherwise the remembered browse expansions re-apply. Then the one-shot focus request
    // from an F1 open lands focus in the modal's box once its markup is in the DOM.
    var input = FM.navigator.input();
    var tree = document.getElementById('fmnav-tree');
    if (tree) {
      var context = tree.getAttribute('data-context');
      if (context !== null) {
        if (FM.navigator.queryContext !== context) { FM.navigator.query = ''; FM.navigator.matches = []; }
        FM.navigator.queryContext = context;
        if (input) { input.value = FM.navigator.query; }
      } else if (input && !input.value && FM.navigator.query) { input.value = FM.navigator.query; }
      if (input && input.value) { FM.navigator.filter(input); }
      else {
        for (var key in FM.navigator.expandedKeys) {
          var item = tree.querySelector('[data-key="' + key + '"]');
          if (item) { FM.navigator.setExpanded(item, true); }
        }
      }
      // The server re-serves collapsed groups. A surviving focused child cannot
      // receive focus until its query/expansion is restored. Repair that loss only
      // when the browser fell back to body; an arrival heading/modal always wins.
      var intentionalReturn = FM.navigator.returnFocus;
      var restoreId = intentionalReturn || FM.navigator.refreshFocus;
      var restore = restoreId && document.getElementById(restoreId);
      if (restore && (intentionalReturn || document.activeElement === document.body) && FM.navigator.treeItems().indexOf(restore) >= 0) {
        FM.navigator.focusTreeItem(restore);
      }
    }
    FM.navigator.refreshFocus = null;
    FM.navigator.returnFocus = null;
    // The one-shot focus request from an Alt+M modal open lands focus on the tree itself
    // (not the box — typing works from the tree, so arriving there is "ready to go"). The
    // flag survives refreshes until the modal markup is actually present.
    if (!FM.navigator.wantFocus || !FM.navigator.modalOpen()) { return; }
    FM.navigator.wantFocus = false;
    var first = FM.navigator.treeItems()[0];
    if (first) { FM.navigator.focusTreeItem(first); }
  },


  // The word filter, applied to the TREE itself (there is no separate results list): every
  // whitespace-separated word must appear somewhere in an item's data-filter (label + owning
  // path), so "tra un" finds Units under Squad > Training. A matching item shows its whole
  // subtree; ancestors of matches stay visible and auto-expand so every match is reachable and
  // announced in its place in the hierarchy. Clearing the box restores the full collapsed tree
  // with the player's remembered expansions. Filter-time expansion is written straight to the
  // DOM, never into expandedKeys — the browse shape is not polluted by search reveals.
  filter: function (input) {
    var tree = document.getElementById('fmnav-tree');
    if (!tree) { return; }
    FM.navigator.query = input.value;
    FM.navigator.queryContext = tree.getAttribute('data-context');
    var words = input.value.toLowerCase().split(/\s+/).filter(function (w) { return w.length > 0; });
    var all = tree.querySelectorAll('[role="treeitem"]');
    var i, g;

    if (!words.length) {
      FM.navigator.matches = [];
      for (i = 0; i < all.length; i++) {
        all[i].hidden = false;
        g = FM.navigator.group(all[i]);
        if (g) { g.hidden = true; all[i].setAttribute('aria-expanded', 'false'); }
      }
      for (var key in FM.navigator.expandedKeys) {
        var item = tree.querySelector('[data-key="' + key + '"]');
        if (item) { FM.navigator.setExpanded(item, true); }
      }
      FM.navigator.announceCount(-1);
      return;
    }

    var matched = [];
    for (i = 0; i < all.length; i++) {
      var f = all[i].getAttribute('data-filter') || '';
      var ok = true;
      for (var w = 0; w < words.length; w++) { if (f.indexOf(words[w]) < 0) { ok = false; break; } }
      all[i].fmMatched = ok;
      if (ok) { matched.push(all[i]); }
    }
    for (i = 0; i < all.length; i++) {
      var el = all[i];
      var show = el.fmMatched;
      if (!show) {
        for (var p = FM.navigator.parentItem(el); p; p = FM.navigator.parentItem(p)) {
          if (p.fmMatched) { show = true; break; } // inside a matched subtree
        }
      }
      var descMatch = false;
      var descs = el.querySelectorAll('[role="treeitem"]');
      for (var d = 0; d < descs.length; d++) { if (descs[d].fmMatched) { descMatch = true; break; } }
      if (descMatch) { show = true; } // an ancestor of a match stays as context
      el.hidden = !show;
      g = FM.navigator.group(el);
      if (g) {
        var open = descMatch || (show && el.fmMatched);
        g.hidden = !open;
        el.setAttribute('aria-expanded', open ? 'true' : 'false');
      }
    }
    FM.navigator.matches = matched;
    FM.navigator.announceCount(matched.length);
  },

  // Count feedback, debounced so a burst of keystrokes speaks once, after typing settles.
  // -1 = filter cleared: cancel any pending announcement and say nothing.
  announceCount: function (n) {
    if (FM.navigator.sayTimer) { clearTimeout(FM.navigator.sayTimer); FM.navigator.sayTimer = null; }
    if (n < 0) { return; }
    FM.navigator.sayTimer = setTimeout(function () {
      FM.navigator.sayTimer = null;
      FM.core.say(n === 0 ? 'No matches' : n + (n === 1 ? ' match' : ' matches'));
    }, 400);
  },

  // Navigate to an addressed element (search option or treeitem). A marker (no address) refuses
  // honestly; "Opening X" is redundant - the destination screen announces itself - so only an
  // error ("no longer available", "not found") is spoken.
  goEl: function (o) {
    var uid = o.getAttribute('data-uid');
    if (!uid) { FM.core.say('This destination could not be read.'); return; }
    var u = '/navigator/go?uid=' + encodeURIComponent(uid);
    var obj = o.getAttribute('data-obj');
    if (obj) { u += '&obj=' + encodeURIComponent(obj); }
    FM.core.command(u, function (t) {
      if (t && t.indexOf('Opening ') !== 0) { FM.core.say(t); }
      FM.core.refresh();
    });
  },


  // --- the browse tree (the ARIA tree contract: Down/Up visible items, Right expand/first
  // child, Left collapse/parent, Home/End, Enter navigates). Expansion is remembered by
  // data-key so a refresh (settle morph) or a close/reopen restores the browsed shape. ---
  expandedKeys: {},

  // Alt+M outside a match (routed through /matchchord's 'menubrowser' reply) — THE Menu
  // browser key. Three-way: an open modal closes (toggle); a page with the title bar's tree
  // focuses it — the player's last tree position (the roving-tabindex item) when still
  // visible, else the first item; a page without one opens the modal Navigator instead, so
  // the widget is reachable from anywhere outside a match. The server refuses the modal
  // honestly where the navigation model itself is unavailable (the main menu).
  focusMenuTree: function () {
    if (FM.navigator.modalOpen()) { FM.navigator.close(); return; }
    var items = FM.navigator.treeItems();
    if (items.length) {
      var tree = document.getElementById('fmnav-tree');
      var cur = tree.querySelector('[role="treeitem"][tabindex="0"]');
      FM.navigator.focusTreeItem(cur && items.indexOf(cur) >= 0 ? cur : items[0]);
      return;
    }
    FM.navigator.wantFocus = true;
    FM.core.command('/navigator/open', function (t) {
      if (t) { FM.core.say(t); FM.navigator.wantFocus = false; } // non-empty reply = error
      FM.core.refresh();
    });
  },

  treeItems: function () {
    var tree = document.getElementById('fmnav-tree');
    if (!tree) { return []; }
    var all = tree.querySelectorAll('[role="treeitem"]');
    var out = [];
    for (var i = 0; i < all.length; i++) {
      if (all[i].hidden) { continue; } // filtered out by the search box
      var vis = true, p = all[i].parentElement;
      while (p && p !== tree) { if (p.hidden) { vis = false; break; } p = p.parentElement; }
      if (vis) { out.push(all[i]); }
    }
    return out;
  },

  group: function (item) {
    for (var c = item.firstElementChild; c; c = c.nextElementSibling) {
      if (c.getAttribute && c.getAttribute('role') === 'group') { return c; }
    }
    return null;
  },

  parentItem: function (item) {
    var g = item.parentElement;
    if (!g || g.id === 'fmnav-tree') { return null; }
    var p = g.parentElement;
    return p && p.getAttribute('role') === 'treeitem' ? p : null;
  },

  focusTreeItem: function (item) {
    var tree = document.getElementById('fmnav-tree');
    if (!tree || !item) { return; }
    var all = tree.querySelectorAll('[role="treeitem"]');
    for (var i = 0; i < all.length; i++) { all[i].setAttribute('tabindex', '-1'); }
    item.setAttribute('tabindex', '0');
    item.focus();
  },

  setExpanded: function (item, on) {
    var g = FM.navigator.group(item);
    if (!g) { return false; }
    g.hidden = !on;
    item.setAttribute('aria-expanded', on ? 'true' : 'false');
    var key = item.getAttribute('data-key');
    if (key) {
      if (on) { FM.navigator.expandedKeys[key] = true; }
      else { delete FM.navigator.expandedKeys[key]; }
    }
    return true;
  },

  // Type-to-filter from inside the tree: typed characters route into the search box (the one
  // query model) and the tree prunes on every keystroke — no Enter to "commit", the arrows work
  // on the filtered tree immediately. Focus stays on the player's item while it survives the
  // filter and jumps to the first match the moment it is pruned away. Clearing the query
  // (Backspace to empty, or Escape) restores the browse tree and keeps the player's item in
  // view by re-expanding its ancestor chain.
  applyTreeQuery: function (item, value) {
    var input = FM.navigator.input();
    if (!input) { return; }
    input.value = value;
    FM.navigator.filter(input);
    if (!value) {
      for (var p = FM.navigator.parentItem(item); p; p = FM.navigator.parentItem(p)) {
        FM.navigator.setExpanded(p, true);
      }
      FM.navigator.focusTreeItem(item);
      return;
    }
    var items = FM.navigator.treeItems();
    if (items.indexOf(item) < 0) {
      var target = FM.navigator.matches.length ? FM.navigator.matches[0] : items[0];
      if (target) { FM.navigator.focusTreeItem(target); }
    }
  },

  treeMove: function (item, delta) {
    var items = FM.navigator.treeItems();
    var idx = -1;
    for (var i = 0; i < items.length; i++) { if (items[i] === item) { idx = i; break; } }
    var next = idx + delta;
    if (idx < 0 || next < 0 || next >= items.length) { return; }
    FM.navigator.focusTreeItem(items[next]);
  },

  treeKey: function (e, item) {
    if (e.altKey || e.ctrlKey || e.metaKey) { return false; }
    var items;
    if (e.code === 'ArrowDown') { e.preventDefault(); FM.navigator.treeMove(item, 1); return true; }
    if (e.code === 'ArrowUp') { e.preventDefault(); FM.navigator.treeMove(item, -1); return true; }
    if (e.code === 'ArrowRight') {
      e.preventDefault();
      if (item.getAttribute('aria-expanded') === 'false') { FM.navigator.setExpanded(item, true); }
      else {
        var g = FM.navigator.group(item);
        var kid = g ? g.querySelector('[role="treeitem"]') : null;
        if (kid) { FM.navigator.focusTreeItem(kid); }
      }
      return true;
    }
    if (e.code === 'ArrowLeft') {
      e.preventDefault();
      if (item.getAttribute('aria-expanded') === 'true') { FM.navigator.setExpanded(item, false); }
      else {
        var up = FM.navigator.parentItem(item);
        if (up) { FM.navigator.focusTreeItem(up); }
      }
      return true;
    }
    if (e.code === 'Home') { e.preventDefault(); items = FM.navigator.treeItems(); if (items.length) { FM.navigator.focusTreeItem(items[0]); } return true; }
    if (e.code === 'End') { e.preventDefault(); items = FM.navigator.treeItems(); if (items.length) { FM.navigator.focusTreeItem(items[items.length - 1]); } return true; }
    if (e.code === 'Enter') { e.preventDefault(); FM.navigator.goEl(item); return true; }
    if (e.code === 'Escape') {
      // First Escape clears a live query (restoring the browse tree, focus kept); an empty
      // query closes the modal. On the title bar home an empty-query Escape is consumed.
      e.preventDefault();
      var esInput = FM.navigator.input();
      if (esInput && esInput.value) { FM.navigator.applyTreeQuery(item, ''); return true; }
      if (FM.navigator.modalOpen()) { FM.navigator.close(); }
      return true;
    }
    if (e.code === 'Backspace') {
      e.preventDefault();
      var bsInput = FM.navigator.input();
      var q = bsInput ? bsInput.value : '';
      if (q) { FM.navigator.applyTreeQuery(item, q.slice(0, -1)); }
      return true;
    }
    // Any single printable character types into the filter (the not-quite-first-letter-nav:
    // type to narrow, arrow immediately). Alt/Ctrl/Meta were rejected above; Shift passes so
    // capitals type (the match is case-insensitive anyway).
    if (e.key && e.key.length === 1) {
      e.preventDefault();
      var tInput = FM.navigator.input();
      if (tInput) { FM.navigator.applyTreeQuery(item, tInput.value + e.key); }
      return true;
    }
    return false;
  },

  // Click (bubbles from the label span; stopPropagation keeps a child click off its ancestors):
  // parents toggle, leaves navigate.
  treeClick: function (ev, item) {
    ev.stopPropagation();
    if (FM.navigator.group(item)) {
      FM.navigator.setExpanded(item, item.getAttribute('aria-expanded') === 'false');
      FM.navigator.focusTreeItem(item);
    } else {
      FM.navigator.goEl(item);
    }
  },

  key: function (e) {
    var t = e.target;
    if (t && t.getAttribute && t.getAttribute('role') === 'treeitem') {
      return FM.navigator.treeKey(e, t);
    }
    var input = FM.navigator.input();
    if (!input || e.target !== input) { return false; }
    if (e.altKey || e.ctrlKey || e.metaKey) { return false; } // leave chords to their handlers
    if (e.code === 'ArrowDown') {
      // Hand focus to the tree: the first match while a query is live, else the first item.
      e.preventDefault();
      var target = FM.navigator.matches.length && input.value ? FM.navigator.matches[0]
        : FM.navigator.treeItems()[0];
      if (target) { FM.navigator.focusTreeItem(target); }
      return true;
    }
    if (e.code === 'Enter') {
      // Go to the first match without ever entering the tree; no query, no target.
      e.preventDefault();
      if (input.value && FM.navigator.matches.length) { FM.navigator.goEl(FM.navigator.matches[0]); }
      return true;
    }
    if (e.code === 'Escape') {
      // First Escape clears a live query (restoring the browse tree); an empty box closes the
      // modal. On the title bar home an empty-box Escape is simply consumed.
      e.preventDefault();
      if (input.value) { input.value = ''; FM.navigator.filter(input); return true; }
      if (FM.navigator.modalOpen()) { FM.navigator.close(); }
      return true;
    }
    return false;
  },
};

// Single keydown listener dispatches to registered handlers; dropdown before nav preserves the
// old early-return precedence.
document.addEventListener('keydown', function (e) {
  // Pressing the modifier of a history chord is not a new cursor choice. Keep
  // the preceding handoff intact until the actual key (Tab, bracket, etc.).
  if (e.key === 'Alt' || e.key === 'Control' || e.key === 'Shift' || e.key === 'Meta') { return; }
  FM.core.historyMove = null;
  FM.core.historyFocus = null;
  FM.core.nativeReturn = null;
  FM.core.actionFocus = null;
  FM.core.provisionalFocus = null;
  if (!document.querySelector('[data-fm-native-loading]')) { FM.dialog.actionOrigin = null; }
  FM.msgview.returnFocus = null;
  FM.msgview.pendingOpen = null;
  FM.msgview.cancelSearchReturn();
  var hs = FM.core.keyHandlers;
  for (var i = 0; i < hs.length; i++) { if (hs[i](e)) { return; } }
});
document.addEventListener('pointerdown', function () { FM.core.historyMove = null; FM.core.historyFocus = null; FM.core.nativeReturn = null; FM.core.actionFocus = null; FM.core.provisionalFocus = null; FM.dialog.actionOrigin = null; FM.msgview.returnFocus = null; FM.msgview.pendingOpen = null; FM.msgview.cancelSearchReturn(); }, true);
FM.core.onKey(FM.contextmenu.key);
FM.core.onKey(FM.dropdown.key);
FM.core.onKey(FM.player.key);
FM.core.onKey(FM.ti.key);
FM.core.onKey(FM.spmove.key);
FM.core.onKey(FM.search.key);
FM.core.onKey(FM.date.key);
FM.core.onKey(FM.messages.key);
FM.core.onKey(FM.msgview.key);
FM.core.onKey(FM.matchchord.key);
FM.core.onKey(FM.continuebtn.key);
FM.core.onKey(FM.bookmarks.key);
FM.core.onKey(FM.save.key);
FM.core.onKey(FM.navigator.key);
FM.core.onKey(FM.dialog.key);
FM.core.onKey(FM.tabs.key);
FM.core.onKey(FM.nav.key);
FM.core.onKey(FM.describe.key);
FM.core.onKey(FM.pitch.key);
FM.core.onKey(function (event) {
  if (event.code !== 'Escape' || event.defaultPrevented) { return false; }
  var close = document.querySelector('button[data-dialog-close]');
  if (!close || close.disabled) { return false; }
  event.preventDefault(); close.click(); return true;
});
FM.core.onRefresh(FM.messages.focusBody);
FM.core.onRefresh(FM.msgview.restoreSearchDisclosure);
FM.core.onRefresh(FM.msgview.focusBody);
FM.core.onRefresh(FM.msgview.refreshUnread);
FM.core.onRefresh(FM.msgview.restoreUnreadFocus);
FM.core.onRefresh(FM.contextmenu.focusFirst);
FM.core.onRefresh(FM.sim.focusAnchor);
FM.core.onRefresh(FM.navigator.focusBox);
// Navigator repair can reveal and focus a previously collapsed history leaf.
// Observe that completed landing before retaining an exact-return handoff.
FM.core.onRefresh(FM.core.restoreHistoryFocus);

// --- FM.audio: shared Web Audio context + decoded buffers for all match sounds ---
// One AudioContext and one buffer set feed both possession metronomes (FM.cue for the reference team,
// FM.oppcue for the opposition), the one-shot 'sfx' sounds, and the contested/air clips. Web Audio (not
// <audio>) so overlapping sounds mix instead of cutting each other off and latency is minimal.
//
// Graph: tickBus (the possession metronomes) and eventBus (everything else) -> one shared
// StereoPannerNode -> destination. The panner is slewed by the server's 'pan' channel to the ball's
// position along the pitch length (-1 full left .. +1 full right, your team attacking rightward in a
// first half), so every sound — including an already-playing loop — localizes to the ball. The two
// buses exist so an event sound is never buried under a tick: every event-bus start DUCKS the tick bus
// to ~25% for ~1.2s, then it recovers.
// --- FM.soundsettings: the player's levels and the wing-drone switch ---
// The server owns the file, this owns the audio graph. A change applies here first and persists second:
// a slider must be audible the instant it moves, and a failed write must not stop the sound changing.
//
// Values are SLIDER POSITIONS (0..100), not gains. amp() converts one to amplitude through a square
// taper, so halfway down the slider is about -12 dB rather than the -6 dB a raw multiply would give;
// a linear slider spends most of its travel in a range that barely sounds different. 100 maps to 1.0
// exactly, so leaving everything alone is bit-identical to the mod before any of this existed.
//
// The defaults here MUST match SoundSettings' defaults on the server, wing drones included: the graph
// is built and can start making sound before the fetch returns, so these are what the first cue of a
// session plays at. Defaulting the drones off is the safe direction — the worst case is a moment of
// silence before the fetch turns them on, rather than a bed the player never asked for.
FM.soundsettings = {
  levels: { master: 100, possession: 100, playback: 100, events: 100, drones: 100, pressure: 100 },
  wingdrones: false,
  pressureon: false,
  pending: {},
  versions: {},
  writes: Promise.resolve(),

  amp: function (key) {
    var pct = FM.soundsettings.levels[key];
    if (typeof pct !== 'number' || isNaN(pct)) { pct = 100; }
    pct = Math.max(0, Math.min(100, pct)) / 100;
    return pct * pct;
  },

  // Push every level onto its node. Safe to call before the graph exists (init calls it again after).
  apply: function () {
    var a = FM.audio;
    if (!a || !a.ctx || !a.master) { return; }
    var t = a.ctx.currentTime;
    // Short ramps, never hard sets: a sustained loop is very likely playing through this, and a step
    // in its gain clicks.
    var set = function (node, v) { node.gain.setTargetAtTime(v, t, 0.02); };
    set(a.master, FM.soundsettings.amp('master'));
    set(a.possessionGroup, FM.soundsettings.amp('possession'));
    set(a.playbackBus, FM.soundsettings.amp('playback'));
    set(a.eventBus, FM.soundsettings.amp('events'));
    // The drone bus carries the switch as well as the level: off means a hard zero, so a disabled
    // drone cannot be heard even if a stale level arrives on the wire afterwards.
    set(a.droneBus, FM.soundsettings.wingdrones ? FM.soundsettings.amp('drones') : 0);
    // The pressure bus carries its switch the same way: off is a hard zero, so the three stems cannot be
    // heard even though they keep running underneath (they are never stopped, only faded).
    set(a.pressureBus, FM.soundsettings.pressureon ? FM.soundsettings.amp('pressure') : 0);
  },

  enabled: function () { return FM.soundsettings.wingdrones; },

  pressureEnabled: function () { return FM.soundsettings.pressureon; },

  load: function () {
    var versions = Object.assign({}, FM.soundsettings.versions);
    return FM.core.requestText('/soundsettings').then(JSON.parse).then(function (s) {
      if (!s) { return; }
      ['master', 'possession', 'playback', 'events', 'drones', 'pressure'].forEach(function (k) {
        if (versions[k] === FM.soundsettings.versions[k] && typeof s[k] === 'number') { FM.soundsettings.levels[k] = s[k]; }
      });
      if (versions.wingdrones === FM.soundsettings.versions.wingdrones) { FM.soundsettings.wingdrones = !!s.wingdrones; }
      if (versions.pressureon === FM.soundsettings.versions.pressureon && typeof s.pressureon === 'boolean') { FM.soundsettings.pressureon = s.pressureon; }
      FM.soundsettings.apply();
    }).catch(function () {
      // Unreachable settings leave the defaults in place, which is the authored mix with the drones
      // off. Honest and playable; the next page open re-reads.
      FM.core.clog('audio: sound settings unavailable; using defaults');
    });
  },

  persistenceNotice: function (text) {
    var note = document.getElementById('fmsnd_save_notice');
    if (!text) { if (note) { note.remove(); } return; }
    if (!note) {
      var field = document.getElementById('fmsnd_master');
      var row = field && field.closest('p');
      if (!row) { return; }
      note = document.createElement('p'); note.id = 'fmsnd_save_notice';
      row.parentNode.insertBefore(note, row);
    }
    // Plain readable feedback. The existing speech request announces once;
    // do not add a second live region or disturb the focused slider.
    note.textContent = text;
  },

  // Persist, coalesced per key. A drag fires oninput per pixel and every one of those would otherwise
  // be a request and a file write; the audio has already moved by the time this runs.
  save: function (key, value) {
    if (FM.soundsettings.pending[key]) { clearTimeout(FM.soundsettings.pending[key]); }
    var version = (FM.soundsettings.versions[key] || 0) + 1;
    FM.soundsettings.versions[key] = version;
    FM.soundsettings.pending[key] = setTimeout(function () {
      FM.soundsettings.pending[key] = null;
      FM.soundsettings.writes = FM.soundsettings.writes.then(function () {
        if (version !== FM.soundsettings.versions[key]) { return; }
        return FM.core.requestText('/soundsetting?key=' + encodeURIComponent(key) + '&value=' + encodeURIComponent(value)).then(function (t) {
          if (version === FM.soundsettings.versions[key]) {
            FM.soundsettings.persistenceNotice(t === 'ok' ? null : t);
            if (t !== 'ok') { FM.core.say(t); }
          }
        }).catch(function () {
          FM.core.clog('audio: could not confirm saved sound setting ' + key);
          if (version === FM.soundsettings.versions[key]) {
            var notice = 'The sound setting could not be confirmed as saved. It remains applied in this session.';
            FM.soundsettings.persistenceNotice(notice);
            FM.core.say(notice);
          }
        });
      });
    }, 250);
  },

  setLevel: function (key, el) {
    var v = parseInt(el.value, 10);
    if (isNaN(v)) { return; }
    FM.soundsettings.levels[key] = v;
    // Keep the announced value in step with the thumb: without this the screen reader reads the
    // percentage the page was rendered with, not the one the player just dragged to.
    el.setAttribute('aria-valuetext', v + ' percent');
    FM.soundsettings.apply();
    FM.soundsettings.save(key, v);
  },

  setToggle: function (key, el) {
    if (key === 'pressureon') {
      FM.soundsettings.pressureon = !!el.checked;
      FM.soundsettings.apply();
      if (!FM.soundsettings.pressureon && FM.pressure) { FM.pressure.silence(); }
      FM.soundsettings.save(key, el.checked ? '1' : '0');
      return;
    }
    FM.soundsettings.wingdrones = !!el.checked;
    FM.soundsettings.apply();
    if (!FM.soundsettings.wingdrones && FM.drone) { FM.drone.silence(); }
    FM.soundsettings.save(key, el.checked ? '1' : '0');
  }
};

FM.audio = {
  ctx: null,
  panner: null,       // shared StereoPannerNode; null -> sounds play centred
  tickBus: null,      // GainNode for the possession metronomes (ducked under event sounds)
  eventBus: null,     // GainNode for one-shots + the contested/air clips
  buffers: {},        // name -> decoded AudioBuffer

  init: function () {
    var AC = window.AudioContext || window.webkitAudioContext;
    if (!AC) { return; }                 // no Web Audio -> cues are silently unavailable
    var ctx = FM.audio.ctx = new AC();
    // Master gain, above everything the player can hear from the match. It has to sit here rather than
    // on the cue gate because three paths deliberately bypass the gate (fast forwarding, the replay
    // markers, previews) and a master that missed them would not be a master.
    FM.audio.master = ctx.createGain();
    FM.audio.master.connect(ctx.destination);
    var out = FM.audio.master;
    if (ctx.createStereoPanner) {
      FM.audio.panner = ctx.createStereoPanner();
      FM.audio.panner.connect(out);
      out = FM.audio.panner;
    } else {
      FM.core.clog('audio: StereoPannerNode unavailable; match sounds play centred');
    }
    // Master cue gate: the one switch the fast-forward mute throws. Both buses route through it,
    // so closing it silences every match cue at once — ticks, loops, and any one-shot already in
    // flight — while FM.ff (connected straight to the destination) keeps sounding. One node beats
    // muting channels one by one: nothing can be forgotten and nothing races.
    FM.audio.cueGate = ctx.createGain();
    FM.audio.cueGate.connect(out);
    // The possession group the player sees as one slider, holding two buses. They stay separate because
    // only the ticks are ducked under event sounds — flattening them into one bus would start ducking
    // the contested and in-the-air loops, which today are deliberately left alone.
    FM.audio.possessionGroup = ctx.createGain();
    FM.audio.possessionGroup.connect(FM.audio.cueGate);
    FM.audio.tickBus = ctx.createGain();
    FM.audio.tickBus.connect(FM.audio.possessionGroup);
    FM.audio.sustainBus = ctx.createGain();
    FM.audio.sustainBus.connect(FM.audio.possessionGroup);
    FM.audio.eventBus = ctx.createGain();
    FM.audio.eventBus.connect(FM.audio.cueGate);
    // Fast forwarding and the replay markers: under the master, outside the cue gate. Outside is the
    // whole point of them — fast forwarding has to be audible while the gate is shut muting every other
    // cue, and the replay markers are meant to sound the same whatever the match audio is doing.
    FM.audio.playbackBus = ctx.createGain();
    FM.audio.playbackBus.connect(FM.audio.master);
    // The touchline drones get their own bus, kept clear of the event bus so the ducking that lets a
    // one-shot cut through the ticks never dips the drone bed, and clear of the tick bus so it is not
    // ducked either. It still routes through the cue gate, so fast-forward silences it with everything else.
    FM.audio.droneBus = ctx.createGain();
    FM.audio.droneBus.connect(FM.audio.cueGate);
    // The carrier-pressure stems get their own bus for the same reasons as the drones: clear of the event
    // ducking so a shot or a whistle never dips the bed, clear of the tick bus so it is not ducked either,
    // and its own gain so the player can turn the bed down without touching the metronomes. Still inside
    // the cue gate, so fast-forward silences it with everything else, and still under the shared panner,
    // so the bed localises to the ball.
    FM.audio.pressureBus = ctx.createGain();
    FM.audio.pressureBus.connect(FM.audio.cueGate);
    // medium ticks the reference team's possession metronome, opptick the opposition's; contested loops
    // while the ball is a 50-50 and air plays once per uncontested flight; fastforwarding loops while
    // the game skips ahead between highlights (FM.ff, its own un-panned channel); the rest play once on
    // the 'sfx' channel: the ball out of play named by the restart (throw-in = touchline, goal kick,
    // corner for / against your team), the ball put back into play (putinplay), a goal for / against,
    // a good defensive play by your team (gooddefence) or the opposition (oppgooddefence), and an attempt
    // on goal by either team (shotattempt, or headerattempt when it was headed), which lands on the strike
    // so the goal / save / restart sound that follows is what tells you where it ended up.
    ['medium', 'opptick',
     'touchline', 'goalkick', 'cornerfor', 'corneragainst', 'putinplay',
     'goalfor', 'goalagainst', 'gooddefence', 'oppgooddefence',
     'shotattempt', 'headerattempt', 'contested', 'air', 'dronenear', 'dronefar',
     'fastforwarding', 'replaystart', 'replayend',
     'pressurelow', 'pressuremed', 'pressurehigh'].forEach(function (name) {
      fetch('/sound?name=' + name)
        .then(function (r) {
          if (!r.ok) { throw new Error('HTTP ' + r.status); }
          return r.arrayBuffer();
        })
        .then(function (buf) { return FM.audio.ctx.decodeAudioData(buf); })
        .then(function (audio) { FM.audio.buffers[name] = audio; })
        .catch(function (err) { FM.core.clog('audio: could not load sound ' + name + ': ' + err.message); });
    });
    // Autoplay: Chromium starts the context suspended until a user gesture. The companion sets
    // --autoplay-policy=no-user-gesture-required so resume() succeeds immediately, but keep the
    // gesture listeners as a fallback for any build without the flag.
    var resume = function () { if (FM.audio.ctx && FM.audio.ctx.state === 'suspended') { FM.audio.ctx.resume(); } };
    resume();
    document.addEventListener('keydown', resume, true);
    document.addEventListener('pointerdown', resume, true);
    // Saved levels last, once every node they address exists.
    FM.soundsettings.apply();
    FM.soundsettings.load();
  },

  // Move the stereo field to the ball. A short slew (~50ms time constant) instead of a hard jump so
  // the sustained clips never click and a fast ball glides rather than teleports.
  setPan: function (v) {
    if (!FM.audio.panner || typeof v !== 'number' || isNaN(v)) { return; }
    v = Math.max(-1, Math.min(1, v));
    FM.audio.panner.pan.setTargetAtTime(v, FM.audio.ctx.currentTime, 0.05);
  },

  // How far the ticks dip under an event sound, at full event volume.
  DUCKFLOOR: 0.25,

  // Dip the tick bus under an event sound, recovering after 0.75s (one tick beat).
  // cancelScheduledValues first so rapid successive events extend the duck instead of fighting the
  // previous recovery ramp.
  //
  // The dip is scaled by how loud events actually are. The duck exists to make room for an event, so
  // a player who has turned events down should get a proportionally smaller hole punched in the ticks
  // rather than the full one; at zero there is nothing to make room for and the ticks are left alone.
  // It rides the tick bus and not the possession group, so it stays independent of the player's
  // possession slider — ducking to a fraction of whatever they chose, not to a fixed absolute level.
  duck: function () {
    var g = FM.audio.tickBus.gain, t = FM.audio.ctx.currentTime;
    var depth = 1 - (1 - FM.audio.DUCKFLOOR) * FM.soundsettings.amp('events');
    g.cancelScheduledValues(t);
    g.setTargetAtTime(depth, t, 0.03);
    g.setTargetAtTime(1.0, t + 0.75, 0.2);
  },

  // Play a sound once, centred, on the playback bus: bypasses the panner, the cue buses, the duck and
  // the fast-forward cue gate. For viewing-state markers (replay start/end) that must sound identical
  // whatever the match audio is doing. It goes through the playback bus rather than straight to the
  // destination so the player's "fast forward and replay" slider and the master reach it.
  playDirect: function (name) {
    var ctx = FM.audio.ctx, buf = FM.audio.buffers[name];
    if (!ctx || !buf) { return; }          // no Web Audio / buffer not loaded -> silent
    if (ctx.state === 'suspended') { ctx.resume(); }
    var src = ctx.createBufferSource();
    src.buffer = buf;
    src.connect(FM.audio.playbackBus);
    src.start(0);
  },

  // Per-sound attenuation for the one-shots, in decibels below full volume — the same convention as
  // FM.loop's dbDown, so every level in this file reads the same way. This balances one clip against
  // its neighbours without touching the bus they all share, which is what a master gain would do.
  // A name absent here plays at full volume.
  trim: { shotattempt: 3 },

  // Play a sound once. Ticks (tick=true) go to the tick bus; everything else goes to the event bus
  // and ducks the ticks so it stands clear of the metronome. A trimmed sound routes through its own
  // gain node on the way to the bus.
  play: function (name, tick) {
    var ctx = FM.audio.ctx, buf = FM.audio.buffers[name];
    if (!ctx || !buf) { return; }
    if (ctx.state === 'suspended') { ctx.resume(); }
    var src = ctx.createBufferSource();
    src.buffer = buf;
    var bus = tick ? FM.audio.tickBus : FM.audio.eventBus;
    var db = FM.audio.trim[name];
    if (db) {
      var g = ctx.createGain();
      g.gain.value = Math.pow(10, -db / 20);
      src.connect(g);
      g.connect(bus);
    } else {
      src.connect(bus);
    }
    if (!tick) { FM.audio.duck(); }
    src.start(0);
  }
};

// --- FM.tick: the live possession-tick cadence, accelerated by distance to goal ---
// The server pushes the possessing team's straight-line metres to the goal it attacks (the Alt+7
// measure — distance to the goal itself, not to the goal-line) on the 'dist' channel while a cue is
// active. Beyond RANGE metres the tick holds its base rate; inside it the cadence shortens to MIN at
// the goal mouth. Each metronome reads ms() when it schedules its next beat, so the acceleration is
// heard within one beat of the ball closing in.
//
// RANGE is pinned to the halfway line — it sits 52.5 m from the attacked goal — so the tick begins
// quickening exactly as a move crosses into the attacking half, and a team playing out from the back
// ticks at a steady base rate.
//
// CURVE bends the gradient and is 1 = LINEAR: the interval falls by a constant 11.5 ms per metre the
// whole way in, so the tempo climbs at an even rate rather than being saved up for the box or spent
// early. Below 1 holds the tempo near base through midfield and escalates late; above 1 does the
// reverse. Kept as a knob so the shape can be re-tuned without touching the endpoints.
//
// MIN sets the goal-mouth ceiling, and with BASE and RANGE pinned it is also the only lever left on the
// steepness — lowering it lengthens the span the ramp covers, so the whole gradient tilts with it. The
// floor is the tick sample itself: the clips are ~75 ms long, so intervals below that overlap and smear
// into a buzz rather than reading as separate beats.
//
// 100 ms is DELIBERATELY PAST the ~115 ms that earlier tuning called the practical floor, chosen by ear
// for a faster goal-mouth peak: it leaves only a 25 ms gap between beats, so the last few metres are
// close to a continuous urgent tone rather than a countable pulse. That is the intent, not an oversight
// — but it is the first thing to raise if the run-in ever reads as mush instead of speed.
//
// Rough feel, in BPM by metres from the attacked goal:
//   52+ = 86 | 40 = 107 | 30 = 134 | 20 = 181 | 16 (box edge) = 211 | 10 = 279 | 5 = 380 | 0 = 600
FM.tick = {
  BASE: 700, MIN: 100, RANGE: 52, CURVE: 1,
  dist: NaN,           // last pushed distance; NaN (nothing pushed yet) -> base cadence
  setDist: function (d) { if (typeof d === 'number' && !isNaN(d)) { FM.tick.dist = d; } },
  ms: function () {
    var d = FM.tick.dist;
    if (isNaN(d)) { return FM.tick.BASE; }
    var f = Math.pow(Math.max(0, Math.min(1, d / FM.tick.RANGE)), FM.tick.CURVE);
    return FM.tick.MIN + (FM.tick.BASE - FM.tick.MIN) * f;
  }
};

// --- FM.metronome: a possession tick metronome driven by a zone -> sound map ---
// The server pushes a zone token whenever it changes ('defensive'/'middle'/'attacking'/'box', or 'off');
// set(zone) runs the metronome for that zone. Timing lives here, not on the SSE stream: the ~500ms poll
// is too jittery to be a metronome, so the server only signals possession and the client self-schedules
// each beat at the live FM.tick.ms() cadence. Every non-off zone maps to the same sound — WHO has the
// ball is the tick sound, WHERE it is is the stereo pan (the shared panner above) and the cadence (the
// distance-to-goal acceleration), so the zones only gate on/off here. Two instances run in parallel:
// FM.cue for the reference team and FM.oppcue for the opposition (its own sound). At most one is
// ticking at a time (only one team is on the ball), but they are independent so a handover never
// crosses their state.
FM.metronome = function (map) {
  var m = {
    zone: 'off',
    sound: null,        // sound name the running metronome ticks
    timer: null,
    MAP: map,           // zone -> sound name; a zone absent from the map (including 'off') is silence

    stop: function () {
      if (m.timer) { clearTimeout(m.timer); m.timer = null; }
      m.sound = null;
    },

    // Chain of one-beat timeouts rather than a fixed interval, so every beat re-reads the live
    // cadence and the acceleration glides instead of stepping.
    schedule: function () {
      m.timer = setTimeout(function () {
        FM.audio.play(m.sound, true);
        m.schedule();
      }, FM.tick.ms());
    },

    // Start the metronome ticking `sound`, with an immediate first tick so a possession gain is felt
    // at once rather than up to a full beat later.
    start: function (sound) {
      if (m.timer) { clearTimeout(m.timer); }
      m.sound = sound;
      FM.audio.play(sound, true);
      m.schedule();
    },

    set: function (zone) {
      if (zone === m.zone) { return; }
      m.zone = zone;
      var sound = m.MAP[zone];
      if (!sound) { m.stop(); return; }           // off / unknown -> silence
      if (m.timer) {
        // Already ticking (a zone move): keep the metronome's phase and just swap which sound the
        // next beat plays - no double tick.
        m.sound = sound;
      } else {
        // Possession gained (was off): tick now and start the beat chain.
        m.start(sound);
      }
    }
  };
  return m;
};

// --- FM.loop: a sustained sound for an on/off state (the contested ball, the ball in the air) ---
// Unlike the metronomes, this plays continuously while active rather than ticking: it starts a single
// Web Audio source when the state turns on and stops it when it turns off. With looping=true the sound
// repeats until 'off' (the contested rumble); with looping=false it is one long clip that plays from
// the beginning once per activation and simply ends if the state outlasts it (the in-the-air clip —
// the next 'off'/'on' cycle restarts it from the start). set('on'/'off') is idempotent, so repeated
// 'on' pushes never stack a second source. Plays on the SUSTAIN bus — grouped with the possession ticks
// under the player's "possession, contested and ball in air" slider, since these describe the state of
// the ball rather than an event — and does NOT duck the ticks: ducking
// is for transient one-shots that must cut through the metronome, and a sustained state is the opposite
// case — it is meant to be heard layered over the ticks, for as long as it lasts. Dipping them at its
// onset fights that directly, and the dip is what a 50-50 sounded like before the contested state
// stopped cutting the tick outright.
// dbDown attenuates this sound alone (decibels below full volume) so the sustained states sit under
// the one-shot events without touching the bus everything shares.
//
// The gain node is kept on the object rather than discarded as a local, so a caller can move the level
// WHILE the sound plays (setGain). l.gain is the level any future start() opens at, so a level set
// between flights is already in place when the next one begins.
FM.loop = function (name, looping, dbDown) {
  var l = {
    src: null,
    g: null,
    gain: Math.pow(10, -(dbDown || 0) / 20),
    start: function () {
      if (l.src) { return; }                 // already active
      var ctx = FM.audio.ctx, buf = FM.audio.buffers[name];
      if (!ctx || !buf) { return; }          // no Web Audio / buffer not loaded -> silent
      if (ctx.state === 'suspended') { ctx.resume(); }
      var s = ctx.createBufferSource();
      s.buffer = buf;
      s.loop = looping;
      var g = ctx.createGain();
      g.gain.value = l.gain;
      s.connect(g);
      g.connect(FM.audio.sustainBus);
      s.start(0);
      l.src = s;
      l.g = g;
    },
    stop: function () {
      if (!l.src) { return; }
      try { l.src.stop(0); } catch (e) {}
      try { l.src.disconnect(); } catch (e) {}
      l.src = null;
      l.g = null;
    },
    // Move the level of a sound that may or may not be playing. A glide rather than a step: this is
    // driven by the ball, which moves continuously, and a stepped gain on a sustained clip clicks.
    setGain: function (amp) {
      l.gain = amp;
      if (l.g) { l.g.gain.setTargetAtTime(amp, FM.audio.ctx.currentTime, 0.08); }
    },
    set: function (on) { if (on === 'on') { l.start(); } else { l.stop(); } }
  };
  return l;
};
// --- FM.drone: the touchline drones, the pitch's width axis as a continuous bed ---
// The stereo field is spent on the length of the pitch (pan is goal line to goal line), so the width has
// no home in it. These two loops fill it: one per physical touchline, the level saying how near the ball
// is to that touchline. Silent only down the middle of the pitch, swelling to full at the touchline. The
// server owns where that silent lane ends (BallLocation.ComputeTouchlineDrone) — it is narrower than the
// spoken flank's central third, so the level here is already moving while the readout still says central.
//
// The server sends ONE signed number: negative selects the near touchline, positive the far one, and the
// magnitude (0..1) is the level. It passes cleanly through 0 in the middle lane, so a ball crossing the
// pitch fades one drone away to silence before the other begins rather than crossfading them over
// each other.
//
// Both sources start once and then run for the life of the page at zero gain, and only the gains ever
// move. Starting and stopping a loop to match the ball would click at every lane boundary and lose the
// loop's phase; holding them open makes every transition a pure fade. Gains glide via setTargetAtTime,
// which is what turns the server's stepped updates into a smooth swell.
FM.drone = {
  MAXDB: 7,           // dB below unity at a ball on the touchline; the level below is relative to this.
                      // The bed sits under the ticks and events, but at 13 it sat so far under them that a
                      // ball out on the wing was barely audible: the ramp already spends most of the band
                      // well below this ceiling, so the cut compounded. 7 keeps it a bed, not a cue.
  GLIDE: 0.12,        // seconds; long enough to hide the update steps, short enough to track a cross
  near: null,
  far: null,
  started: false,

  start: function () {
    if (FM.drone.started) { return; }
    if (!FM.soundsettings.enabled()) { return; }   // switched off: never open the sources at all
    var ctx = FM.audio.ctx;
    if (!ctx || !FM.audio.buffers['dronenear'] || !FM.audio.buffers['dronefar']) { return; }  // not loaded yet
    FM.drone.near = FM.drone.open('dronenear');
    FM.drone.far = FM.drone.open('dronefar');
    FM.drone.started = !!(FM.drone.near && FM.drone.far);
  },

  // One looping source at silence, ready to be faded up.
  open: function (name) {
    var ctx = FM.audio.ctx;
    var src = ctx.createBufferSource();
    src.buffer = FM.audio.buffers[name];
    src.loop = true;
    var g = ctx.createGain();
    g.gain.value = 0;
    src.connect(g);
    g.connect(FM.audio.droneBus);
    src.start(0);
    return g;
  },

  // Fade both drones out and leave them there. Used when the player switches the drones off: the bus
  // gain alone would silence them, but the per-source gains would keep whatever level the ball last
  // put them at, and switching back on would then burst at that stale level before the next update
  // corrected it. Fading the sources too means re-enabling always starts from silence.
  silence: function () {
    if (!FM.drone.started) { return; }
    var t = FM.audio.ctx.currentTime;
    FM.drone.near.gain.setTargetAtTime(0, t, FM.drone.GLIDE);
    FM.drone.far.gain.setTargetAtTime(0, t, FM.drone.GLIDE);
  },

  // v in -1..+1: sign picks the touchline, magnitude is how near the ball is to it.
  set: function (v) {
    if (!FM.soundsettings.enabled()) { return; }
    FM.drone.start();
    if (!FM.drone.started) { return; }
    if (typeof v !== 'number' || isNaN(v)) { return; }   // no reading: hold the current level
    var ctx = FM.audio.ctx;
    if (ctx.state === 'suspended') { ctx.resume(); }
    var ceiling = Math.pow(10, -FM.drone.MAXDB / 20);
    var t = ctx.currentTime;
    FM.drone.near.gain.setTargetAtTime(Math.max(0, -v) * ceiling, t, FM.drone.GLIDE);
    FM.drone.far.gain.setTargetAtTime(Math.max(0, v) * ceiling, t, FM.drone.GLIDE);
  }
};

// --- FM.pressure: how heavily the man on the ball is being closed down ---
// Three stems of identical length, unmuted cumulatively: low alone, then low+medium, then all three. The
// server sends ONE 0..1 number and the banding happens here.
//
// All three sources start together at a single scheduled time, loop forever, and are NEVER stopped — only
// their gains move. That is the same discipline FM.drone uses, for the same reason plus one more: because
// the buffers are byte-identical in length they stay sample-locked indefinitely, so the stems are always
// phase-aligned with each other. Starting a stem when its level was reached would drop it in at whatever
// phase the others happened to be at, which is precisely what this design exists to avoid.
//
// Stems SWITCH rather than fade. A stem is either off or at full, changed over a glide short enough to
// read as a switch and long enough not to click. The first design faded the newest stem in proportionally
// across its band, which was meant to make a press audible building; in play it read as a constant
// swelling and ebbing that was harder to interpret than the levels themselves, so the gradient is gone and
// only the four states remain.
//
// Switching needs hysteresis, which fading did not: a carrier sitting exactly on a band boundary would
// otherwise toggle a stem on and off at the sample rate, and with hard steps that chatters audibly where a
// fade merely wobbled. Rising to a level takes MARGIN more than the boundary, dropping out of it MARGIN
// less, so a boundary can be held without oscillating.
//
// ENTER is the level the bed opens at all, deliberately above zero. The value is continuous, so an
// opponent at the very edge of the radius produces a tiny non-zero reading; without a floor the low stem
// would snap to FULL for a man 3 m away and be back to sounding constantly, which is what the radius and
// base-share retune fixed. Set it to 0 to hear the raw banding.
FM.pressure = {
  STEMS: ['pressurelow', 'pressuremed', 'pressurehigh'],
  MAXDB: 7,           // dB below unity for a stem at full; three summing is the reason it is not louder
  GLIDE: 0.25,        // seconds; fading the whole bed out when it is switched off
  STEPGLIDE: 0.02,    // seconds; a level change - the cue gate's anti-click ramp, not a musical fade
  ENTER: 0.10,        // reading below which no stem plays at all
  MARGIN: 0.04,       // hysteresis either side of a band boundary
  gains: null,
  stage: 0,           // how many stems are currently open, 0..3
  started: false,

  start: function () {
    if (FM.pressure.started) { return; }
    if (!FM.soundsettings.pressureEnabled()) { return; }   // switched off: never open the sources at all
    var ctx = FM.audio.ctx;
    if (!ctx) { return; }
    for (var i = 0; i < FM.pressure.STEMS.length; i++) {
      if (!FM.audio.buffers[FM.pressure.STEMS[i]]) { return; }   // not all loaded yet; try again next update
    }
    // One start time for all three, a hair in the future so the scheduling is sample-accurate rather than
    // "whenever each createBufferSource call happened to land".
    var t0 = ctx.currentTime + 0.05;
    var gains = [];
    for (var j = 0; j < FM.pressure.STEMS.length; j++) {
      var src = ctx.createBufferSource();
      src.buffer = FM.audio.buffers[FM.pressure.STEMS[j]];
      src.loop = true;
      var g = ctx.createGain();
      g.gain.value = 0;
      src.connect(g);
      g.connect(FM.audio.pressureBus);
      src.start(t0);
      gains.push(g);
    }
    FM.pressure.gains = gains;
    FM.pressure.started = true;
  },

  // Fade every stem out and leave them there. Used when the player switches the bed off: the bus gain
  // alone would silence it, but the per-stem gains would keep whatever level the last carrier put them
  // at, and switching back on would burst at that stale level before the next update corrected it.
  silence: function () {
    if (!FM.pressure.started) { return; }
    var t = FM.audio.ctx.currentTime;
    for (var i = 0; i < FM.pressure.gains.length; i++) {
      FM.pressure.gains[i].gain.setTargetAtTime(0, t, FM.pressure.GLIDE);
    }
    FM.pressure.stage = 0;
  },

  // The reading at or above which stage k+1 is justified. Stage 1 has its own floor; the rest are the
  // equal thirds the three stems are banded into.
  bandFloor: function (k) {
    return k === 0 ? FM.pressure.ENTER : k / FM.pressure.STEMS.length;
  },

  // v in 0..1: how pressed the man on the ball is. Resolves to a stage (how many stems are open) and
  // switches only when the stage actually changes.
  level: function (v) {
    if (!FM.soundsettings.pressureEnabled()) { return; }
    FM.pressure.start();
    if (!FM.pressure.started) { return; }
    if (typeof v !== 'number' || isNaN(v)) { return; }   // no reading: hold the current level
    var ctx = FM.audio.ctx;
    if (ctx.state === 'suspended') { ctx.resume(); }
    v = Math.max(0, Math.min(1, v));

    var n = FM.pressure.STEMS.length;
    var m = FM.pressure.MARGIN;
    var st = FM.pressure.stage;
    // Climb while the reading clears the next boundary by the margin, then fall while it is the margin
    // below the one holding the current stage. Only one of the two loops can ever run.
    while (st < n && v >= FM.pressure.bandFloor(st) + m) { st++; }
    while (st > 0 && v < FM.pressure.bandFloor(st - 1) - m) { st--; }

    // Unchanged stage: leave the gains alone. Re-scheduling an identical target every sample would
    // restart the ramp 8 times a second and never quite settle.
    if (st === FM.pressure.stage) { return; }
    FM.pressure.stage = st;

    var ceiling = Math.pow(10, -FM.pressure.MAXDB / 20);
    var t = ctx.currentTime;
    for (var i = 0; i < n; i++) {
      FM.pressure.gains[i].gain.setTargetAtTime(i < st ? ceiling : 0, t, FM.pressure.STEPGLIDE);
    }
  }
};

FM.contested = FM.loop('contested', true, 2);

// --- FM.air: the in-the-air cue, whose loudness is the ball's height ---
// Not an on/off state like the contested loop: the server sends a 0..1 ramp position (the server owns
// where the ramp starts and ends — BallLocation.ComputeAirLoudness) and that maps to a level here, so a
// skied clearance is loud and a bobbling head-height ball is barely there.
//
// FLOORDB is the level at the aerial threshold and CEILDB at the ceiling height, both as decibels BELOW
// full, matching every other level in this file. Interpolating in decibels rather than in amplitude is
// what makes the swell sound even: the ear hears decibels, so a straight line in amplitude would spend
// most of its travel in a range that barely changes.
//
// The 24 dB between them is wide on purpose — a narrower range was tried live and read as the cue barely
// responding to height at all. Where the pair SITS is a separate question from how wide it is, and both
// were settled by ear: widened to 24 dB, then slid down 3 dB together when the result sat too loud in the
// mix. The ceiling stays well under contested (-2) and attempt on goal (-3) so a big clearance stands out
// without burying the events that matter more than it does. These two numbers are the tuning knobs for
// this cue and they live here, on the client, so retuning by ear is a /reload; only the HEIGHTS they map
// (BallLocation.AirFloor/AirCeilingMetres) need a restart.
//
// The clip is ~19.5 s, far longer than any real flight, so it never has to loop: one source covers the
// whole flight and only its gain moves. Level is a pure function of CURRENT height, so it falls again as
// the ball drops.
FM.air = FM.loop('air', false, 33);
FM.air.FLOORDB = 33;
FM.air.CEILDB = 9;
FM.air.level = function (f) {
  if (typeof f !== 'number' || isNaN(f)) { return; }   // no reading: hold the current level
  f = Math.max(0, Math.min(1, f));
  var db = FM.air.FLOORDB + (FM.air.CEILDB - FM.air.FLOORDB) * f;
  FM.air.setGain(Math.pow(10, -db / 20));
};

// --- FM.ff: the fast-forwarding loop (the game skipping ahead between highlights) ---
// Its own channel on the playback bus, which sits under the master but OUTSIDE the panner and the cue
// gate: the wav is authored in stereo and must not be collapsed through the shared ball panner, and
// nothing ducks it (every other cue is muted while it plays). The bus is what lets the player's
// "fast forward and replay" slider reach it without putting it back behind the gate it has to survive.
// Unlike FM.loop, 'off' does NOT stop the source — it clears the loop flag so the current
// pass plays to its natural end, ending cleanly as the highlight begins. An 'on' during that tail
// re-arms the loop flag on the still-playing source, so a quick fast-forward resume never stacks a
// second copy.
//
// 'on' also slams the master cue gate shut (and 'off' reopens it): every other match sound dies the
// instant the fast-forward signal arrives, whatever the individual channels were doing — the server
// stops them too, but the gate is one atomic switch on this side of the wire.
FM.ff = {
  src: null,
  active: false,     // mirrored from the 'ff' channel; the commentary live region checks it too
  gate: function (open) {
    var g = FM.audio.cueGate;
    if (!g) { return; }
    var t = FM.audio.ctx.currentTime;
    g.gain.cancelScheduledValues(t);
    // A very short ramp instead of a hard step so the cut never clicks.
    g.gain.setTargetAtTime(open ? 1 : 0, t, 0.02);
  },
  set: function (on) {
    FM.ff.active = on === 'on';
    if (on !== 'on') {
      if (FM.ff.src) { FM.ff.src.loop = false; }
      FM.ff.gate(true);
      return;
    }
    FM.ff.gate(false);
    if (FM.ff.src) { FM.ff.src.loop = true; return; }
    var ctx = FM.audio.ctx, buf = FM.audio.buffers['fastforwarding'];
    if (!ctx || !buf) { return; }          // no Web Audio / buffer not loaded -> silent
    if (ctx.state === 'suspended') { ctx.resume(); }
    var s = ctx.createBufferSource();
    s.buffer = buf;
    s.loop = true;
    s.onended = function () { if (FM.ff.src === s) { FM.ff.src = null; } };
    s.connect(FM.audio.playbackBus);
    s.start(0);
    FM.ff.src = s;
  }
};

// --- FM.soundcues: preview playback for the Sound Cues Settings page ---
// Each preview button plays its sound once through the shared decoded buffers, but connected
// STRAIGHT to the destination — bypassing the panner, every bus, and the master — so previews are
// centred, never panned to a stale ball position, and never duck the (paused) match audio. dbDown keeps
// the cue's real relative volume (the drone/contested/air attenuations) so previews teach the true
// loudness. Starting any preview stops the one before it, so long clips never pile up.
//
// Bypassing the master and the group buses is deliberate: a preview plays at the reference level the
// sound was authored at, NOT at the player's chosen volume. It is a teaching tool, so it has to stay
// audible while someone is in the middle of setting a group to zero — a preview button that did nothing
// would read as broken rather than as obedient.
//
// Previews are capped at MAXSECS. The sustained beds — the wing drones, fast forwarding, contested —
// are authored as long seamless loops, and playing one in full leaves a learner sat through minutes of
// it with no way to tell whether the sound or the page has stalled. Only clips longer than the cap are
// touched: a one-shot like a goal or a tick is left to end on its own so its natural tail survives.
// The cap fades rather than cutting, because stopping a drone at full level clicks, and a click is
// itself a sound the player is here to learn to recognise.
FM.soundcues = {
  MAXSECS: 4,
  FADE: 0.3,
  cur: null,
  play: function (name, dbDown) {
    var ctx = FM.audio.ctx, buf = FM.audio.buffers[name];
    if (!ctx || !buf) { return; }          // no Web Audio / buffer not loaded -> silent
    if (ctx.state === 'suspended') { ctx.resume(); }
    if (FM.soundcues.cur) {
      try { FM.soundcues.cur.stop(0); } catch (e) {}
      try { FM.soundcues.cur.disconnect(); } catch (e) {}
      FM.soundcues.cur = null;
    }
    var s = ctx.createBufferSource();
    s.buffer = buf;
    var g = ctx.createGain();
    var level = Math.pow(10, -(dbDown || 0) / 20);
    g.gain.value = level;
    s.connect(g);
    g.connect(ctx.destination);
    var t = ctx.currentTime;
    s.start(t);
    if (buf.duration > FM.soundcues.MAXSECS) {
      var end = t + FM.soundcues.MAXSECS;
      g.gain.setValueAtTime(level, end - FM.soundcues.FADE);
      g.gain.linearRampToValueAtTime(0, end);
      s.stop(end);
    }
    FM.soundcues.cur = s;
  }
};

// One tick sound in every zone: the sound names the team on the ball, the stereo pan places it, and
// the cadence (FM.tick) accelerates as that team closes on the goal it attacks.
FM.cue = FM.metronome({
  defensive: 'medium',
  middle: 'medium',
  attacking: 'medium',
  box: 'medium'
});
// The opposition's parallel metronome: same cadence rules, its own tick sound.
FM.oppcue = FM.metronome({
  defensive: 'opptick',
  middle: 'opptick',
  attacking: 'opptick',
  box: 'opptick'
});
if (typeof FM_CAPABILITIES === 'undefined' || FM_CAPABILITIES.matchAudio !== false) { FM.audio.init(); }

// SSE push: the server sends the current token immediately on connect, so only refresh when it
// differs from what this page last rendered. The handler is compare-only: refresh() owns
// FMTOKEN, committing the outgoing screen's focus under the old token before adopting the new
// one from the /body response. EventSource auto-reconnects if the stream drops.
var FMES = new EventSource('/events');
// EventSource auto-reconnects if the stream drops; the server's first tick on a fresh connection
// re-sends 'screen' (its lastToken resets to null), so a reconnect re-reads on its own.
// An open cascading context menu folds a "menu:" segment into the screen token (ServerText
// .ScreenToken). hasMenu detects it; stripMenu returns the token of the screen BENEATH the menu
// (everything before that segment), so an open and its matching close share one base token.
function hasMenu(t) { return !!t && t.indexOf('menu:') >= 0; }
function stripMenu(t) {
  if (!t) { return t; }
  var i = t.indexOf('menu:');
  if (i < 0) { return t; }
  if (i > 0 && t.charCodeAt(i - 1) === 0x1F) { i--; } // drop the unit-separator before the segment
  return t.slice(0, i);
}
// The modal Message view folds a "msgview:" segment into the token the same way (before any
// "menu:" segment, so stripMenu on a menu-over-modal leaves the modal's base intact).
function hasMsgView(t) { return !!t && t.indexOf('msgview:') >= 0; }
function stripMsgView(t) {
  if (!t) { return t; }
  var i = t.indexOf('msgview:');
  if (i < 0) { return t; }
  if (i > 0 && t.charCodeAt(i - 1) === 0x1F) { i--; }
  return t.slice(0, i);
}
FMES.addEventListener('screen', function (e) {
  // A reconnect re-sends the current token and must not tear down an option list the player is
  // still using. A different token, however, means the old control no longer belongs to the live
  // game screen; preserving that session would strand every later refresh behind ddpend.
  var identityChanged = FM.core.screenIdentity(e.data) !== FM.core.screenIdentity(FMTOKEN);
  if (identityChanged) {
    // Also cover instant menu/message stash restores which adopt the token
    // before their background body refresh completes.
    FM.core.speechGeneration++;
    FM.core.speechNotice(false);
  }
  var wasMenu = hasMenu(FMTOKEN);
  var nowMenu = hasMenu(e.data);
  var wasMsg = hasMsgView(FMTOKEN);
  var nowMsg = hasMsgView(e.data);
  // Closing the Message view over the stashed list page: restore it instantly (the menu-close
  // pattern), focus returning to the screen's remembered position — the row that opened the
  // message. A base mismatch (the screen beneath changed, e.g. the view self-closed after an
  // attachment navigated) drops the stash and falls through to a normal refresh.
  if (wasMsg && !nowMsg && !nowMenu) {
    var mstash = FM.core.msgStash;
    FM.core.msgStash = null;
    if (mstash && mstash.base === stripMsgView(e.data) && !FM.core.dd) {
      document.getElementById('fmroot').innerHTML = mstash.html;
      FMTOKEN = e.data;
      var mpos = FM.core.pos[FM.core.screenIdentity(FMTOKEN)];
      if (mpos) { var mel = document.getElementById(mpos); if (mel) { mel.focus(); } }
      FM.core.refresh();
      return;
    }
  }
  // The view opening without a stash (a refresh raced ahead, or the page reloaded mid-open):
  // snapshot the list page before the modal render replaces #fmroot. Never overwrite one.
  if (nowMsg && !wasMsg && !FM.core.msgStash) {
    FM.core.msgStash = { base: stripMsgView(FMTOKEN), html: document.getElementById('fmroot').innerHTML };
  }
  // Any modal appearance lands focus on the message body, not just the one this client opened: a
  // history back/forward reopens the view server-side, and a reload can too. Without this the
  // restored message would render with focus stranded.
  if (nowMsg && !wasMsg) { FM.msgview.wantBody = true; }
  // Closing a menu over a screen we stashed: restore that screen instantly instead of paying a
  // full /body re-render (multi-second on a streamed table). The background refresh that follows
  // re-registers handles and morphs in anything that changed under the menu, behind the already-
  // visible page. A token mismatch (the screen beneath changed) drops the stash and falls through
  // to a normal refresh.
  if (wasMenu && !nowMenu) {
    var stash = FM.core.menuStash;
    FM.core.menuStash = null;
    if (stash && stash.base === stripMenu(e.data) && !FM.core.dd) {
      document.getElementById('fmroot').innerHTML = stash.html;
      FMTOKEN = e.data;
      var pos = FM.core.pos[FM.core.screenIdentity(FMTOKEN)];
      if (pos) { var el = document.getElementById(pos); if (el) { el.focus(); } }
      FM.core.refresh();
      return;
    }
  }
  // Opening a menu over the current screen: snapshot it before the menu render replaces #fmroot.
  // Fallback only — a companion-driven open (FM.contextmenu.open) already stashed before its resync;
  // this covers a menu opened in-game (keyboard/speech cursor), where the SSE event is the only
  // signal. Never overwrite an existing stash (which would capture the half-rendered menu).
  if (nowMenu && !wasMenu && !FM.core.menuStash) {
    FM.core.menuStash = { base: stripMenu(FMTOKEN), html: document.getElementById('fmroot').innerHTML };
  }
  // A menu just opened (companion-driven opens set this too, before their own resync advances
  // FMTOKEN; this covers in-game opens where SSE is the only signal): its first stop takes focus.
  if (nowMenu && !wasMenu) { FM.contextmenu.wantFirst = true; }
  // Always re-read: the server only emits 'screen' on a real identity change, and an optimistic
  // post-action refresh may have already advanced FMTOKEN to a half-built version of the new
  // screen, so deduping on e.data === FMTOKEN would leave the surface stuck on that partial read.
  if (identityChanged) { FM.core.abandonInteractionForScreenChange(); }
  FM.core.refreshOrDefer();
});
// Settle: the server saw the panel's structure change within the short window after a navigation
// (content that populated late). Re-read the screen; morph patches it in gently so the cursor is
// undisturbed. Same in-flight deferral as 'screen' so an open dropdown/calendar is never wiped.
//
// On a match surface one substitution is not one restructure: the tactics table rebinds in several
// consecutive waves, and morphing each of them walks the reader through the game's intermediate
// states before it reaches the one that is true. Coalesce a burst into the single morph of where it
// settled. Trailing edge only — the first wave is the least correct of them, so there is nothing
// worth showing early. A screen change cancels the pending timer via abandonInteractionForScreenChange.
FMES.addEventListener('restructure', function () {
  if (!document.getElementById('fmscore')) { FM.core.refreshOrDefer(true); return; }
  if (FM.core.restructureTimer) { clearTimeout(FM.core.restructureTimer); }
  FM.core.restructureTimer = setTimeout(function () {
    FM.core.restructureTimer = null;
    FM.core.refreshOrDefer(true);
  }, 700);   // long enough for one substitution's waves; a second substitution restarts it
});
// Match stream: commentary announces via its polite live region; the score line updates
// silently in place and is read on demand. While fast-forwarding the live region is gated
// with the audio cues (the server already suppresses these lines; this is the client-side
// backstop, like the cue gate) — the post-fast-forward line arrives on its own event once
// the highlight starts, so nothing needs replaying here.
FMES.addEventListener('commentary', function (e) {
  var n = document.getElementById('fmlive');
  if (!n) { return; }
  // Clear the last play-by-play sentence when the match stream ends. This protocol-only value is
  // handled before the fast-forward gate so stale fast-forward state cannot retain the final line.
  if (e.data === '__fm_commentary_clear__') { n.textContent = ''; return; }
  if (FM.ff.active) { return; }
  n.textContent = e.data;
});
FMES.addEventListener('score', function (e) {
  var n = document.getElementById('fmscore');
  if (n) { n.textContent = e.data; }
});
// Match audio cues: each zone drives its team's possession-tick metronome (silent surface update, no
// live region). 'cue' is the reference team, 'oppcue' the opposition's parallel metronome.
FMES.addEventListener('cue', function (e) { FM.cue.set(e.data); });
FMES.addEventListener('oppcue', function (e) { FM.oppcue.set(e.data); });
// Ball stereo pan: the ball's position along the pitch length (-1 full left .. +1 full right, your
// team attacking rightward in a first half, leftward in a second). Slews the shared panner every
// match sound routes through, so the whole soundscape follows the ball across the stereo field.
FMES.addEventListener('pan', function (e) { FM.audio.setPan(parseFloat(e.data)); });
// Distance to goal for the possessing team (the Alt+7 straight-line measure): drives the possession
// tick's smooth acceleration from the halfway line in.
FMES.addEventListener('dist', function (e) { FM.tick.setDist(parseFloat(e.data)); });
// Touchline drones: the ball's position ACROSS the pitch, which the pan cannot carry because pan is
// spent on the length. Signed: the sign picks which physical touchline, the magnitude how near the ball
// is to it, silent through the middle third. Unlike pan, the touchlines never swap at half time.
FMES.addEventListener('drone', function (e) { FM.drone.set(parseFloat(e.data)); });
// One-shot match sounds: ball out of play named by the restart (touchline / goalkick / cornerfor /
// corneragainst), goals (goalfor/goalagainst), a good defensive play by your team (gooddefence) or the
// opposition (oppgooddefence), and an attempt on goal by either team (shotattempt). These play once,
// independent of the metronomes, so the out-of-play sound
// still sounds although the cue zone has just gone 'off'.
FMES.addEventListener('sfx', function (e) { FM.audio.play(e.data); });
// A screen-changing simulation settled: land focus on the arrival page's content heading
// (immediately when the page already shows that token, else on the refresh that brings it).
FMES.addEventListener('simarrive', function (e) { FM.sim.arrive(e.data); });
// Contested ball: a sustained looping sound while both teams contest the ball. The server pushes 'on'/'off'
// on change; the loop plays continuously until it turns off.
FMES.addEventListener('contested', function (e) { FM.contested.set(e.data); });
// Ball in the air (high and uncontested): one long clip per flight, started from the beginning on 'on'
// and stopped on 'off' (a flight that outlasts the clip just goes quiet until the next one).
// Height first, then the on/off: the server sends them in that order within a tick, so a flight starting
// now opens at the right level instead of at the last flight's.
// Carrier pressure: one 0..1 level driving the three phase-locked stems. Nothing is sent while the
// reading is unavailable, so the bed holds rather than dipping; 0 arrives when there is genuinely nobody
// to press (loose ball, dead ball, between highlights) and fades every stem out.
FMES.addEventListener('pressure', function (e) { FM.pressure.level(parseFloat(e.data)); });
FMES.addEventListener('airlevel', function (e) { FM.air.level(parseFloat(e.data)); });
FMES.addEventListener('air', function (e) { FM.air.set(e.data); });
// Fast-forwarding between highlights: loops the stereo fast-forwarding sound while 'on'; 'off' lets
// the current pass finish so the loop ends cleanly as the highlight begins.
FMES.addEventListener('ff', function (e) { FM.ff.set(e.data); });
// Replay start/end markers: centred one-shots on their own channel, never muted (a replay ending
// straight into a fast-forward still says "exiting replay" alongside the loop).
FMES.addEventListener('replay', function (e) { FM.audio.playDirect(e.data); });
// Live values read from the game are patched in place by id. Controls use their
// own value/checked patchers; only leaf text uses textContent. Keep structure,
// focus and labels intact. No additional live region is introduced.
FMES.addEventListener('values', function (e) {
  var m;
  try { m = JSON.parse(e.data); } catch (_) { return; }
  var valueAt = Number(m.__fm_value_at) || 0;
  delete m.__fm_value_at;
  var missing = {}, heal = false;
  for (var id in m) {
    if (!m.hasOwnProperty(id)) { continue; }
    var el = document.getElementById(id);
    if (!el) {
      // A non-empty value for an id this DOM lacks means the page is behind the server's last
      // compose. One miss can be the race with an in-flight refresh (the morph lands a beat
      // after the compose registers the id); the same id missing on two consecutive events
      // cannot, and triggers the self-heal below. Empty values are excluded: the stream also
      // carries blanks for elements that left the screen, which prove nothing.
      if (m[id]) { missing[id] = true; if (FM.core.staleSeen[id]) { heal = true; } }
      continue;
    }
    // A <select> (a dropdown's value) cannot take textContent without destroying its <option>;
    // patch its shown value through the dropdown machinery instead. An <input> (a searchable
    // dropdown's box) takes the value property - skipped while the player is in the box (focused
    // or mid-session) so a push never clobbers a half-typed filter. Every other tracked id is a
    // leaf-text node whose textContent IS the value.
    if (el.tagName === 'SELECT') { FM.dropdown.syncValue(el, m[id], valueAt); }
      else if (el.getAttribute('role') === 'progressbar') {
        // The native caption and value are semantic attributes as well as
        // visible text. Updating only textContent leaves screen readers on
        // the old aria-valuetext after an in-place native change.
        try {
          var progress = JSON.parse(m[id]);
          if (typeof progress.text !== 'string' || typeof progress.state !== 'string') { continue; }
          el.setAttribute('aria-label', progress.label || 'Progress');
          el.setAttribute('aria-valuetext', progress.state);
          el.textContent = progress.text;
        } catch (error) { FM.core.clog('Progress value update failed: ' + error.message); }
      }
    else if (el.tagName === 'INPUT' && el.type === 'range') {
      // Sliders carry their native numeric range plus the row's meaningful
      // value text. Preserve a focused keyboard edit, while updating its native
      // caption/units; no additional live region or hidden duplicate text.
      try {
        var range = JSON.parse(m[id]);
        if (!Number.isFinite(range.value) || !Number.isFinite(range.min) || !Number.isFinite(range.max)) { continue; }
        el.min = range.min; el.max = range.max; el.step = range.step > 0 ? range.step : 'any';
        if (document.activeElement !== el) { el.value = range.value; }
        if (range.text) { el.setAttribute('aria-valuetext', range.text); } else { el.removeAttribute('aria-valuetext'); }
      } catch (error) { FM.core.clog('Slider value update failed: ' + error.message); }
    }
    else if (el.tagName === 'INPUT' && (el.type === 'checkbox' || el.type === 'radio')) {
      // The native model can restore these after Undo/Redo/Reset. Updating
      // checked does not fire an action, replace the input, or move focus.
      if (valueAt < (Number(el.getAttribute('data-fm-value-at')) || 0)) { continue; }
      el.checked = m[id] === 'checked' || m[id] === 'on' || m[id] === 'selected';
      el.setAttribute('data-fm-value-at', valueAt);
    }
    else if (el.tagName === 'INPUT') {
      if (document.activeElement !== el && FM.core.dd !== el.id) { el.value = m[id]; }
    }
    else { el.textContent = m[id]; }
  }
  FM.core.staleSeen = missing;
  FM.conversation.restore();
  // Self-heal: this page is confirmed behind the server (see above) - re-fetch it, exactly what
  // a manual F5 does, minus losing the reading position (a morph, not a replace). Skipped while
  // a refresh is already in flight (its morph is the catch-up); deferred while an option list is
  // open, like any other refresh. Rate-limited per screen so a server-side id bug degrades to
  // three extra refreshes, never a loop.
  if (heal && !FM.core.refreshing) {
    var now = Date.now();
    if (FM.core.healToken !== FMTOKEN) { FM.core.healToken = FMTOKEN; FM.core.healCount = 0; }
    if (FM.core.healCount < 3 && now - FM.core.healAt > 5000) {
      FM.core.healAt = now;
      FM.core.healCount++;
      FM.core.clog('self-heal: values stream referenced ids missing from the DOM; refreshing');
      FM.core.refreshOrDefer(true);
    }
  }
});

// Dev surface: mirror the LIVE #fmroot DOM back to the server (POST /rendered/report) after every
// change, so an agent can read exactly what this companion renders via GET /rendered — the served
// /body plus all client-side morphs (refreshes, live values, dropdown popups), which can drift from
// a fresh /body. A debounced MutationObserver collapses update bursts into one report. Best-effort
// and silent: a failed report must never disturb the player's surface.
var FMReport = {
  t: null,
  sending: false,
  dirty: false,
  schedule: function () {
    if (document.documentElement.getAttribute('data-fm-development') !== 'true') { return; }
    if (FMReport.sending) { FMReport.dirty = true; return; }
    if (FMReport.t) { return; }
    // 400ms debounce: every report serializes the whole page and POSTs it over loopback, and the
    // focusin/focusout listeners below fire it on every arrow-key move in browse mode — at 150ms
    // that was up to ~6 full-page serializations a second while the player just reads. A dev
    // reading /rendered waits out at most one debounce.
    FMReport.t = setTimeout(function () {
      FMReport.t = null;
      // A dropdown session floods the DOM (options injected, values synced) while /rendered is
      // not being read; defer — not drop — the mirror until the session closes.
      if (FM.core.dd) { FMReport.schedule(); return; }
      var root = document.getElementById('fmroot');
      if (!root) { return; }
      // The focused element id rides along in the query (focus is not part of innerHTML), so an
      // agent can verify where the player's browser focus sits via GET /rendered.
      var f = (document.activeElement && document.activeElement.id) || '';
      FMReport.sending = true;
      FMReport.dirty = false;
      FM.core.requestText('/rendered/report?focus=' + encodeURIComponent(f), { method: 'POST', body: root.innerHTML }, 5000)
        .catch(function () {})
        .finally(function () {
          FMReport.sending = false;
          if (FMReport.dirty) { FMReport.schedule(); }
        });
    }, 400);
  }
};
(function () {
  if (document.documentElement.getAttribute('data-fm-development') !== 'true') { return; }
  var root = document.getElementById('fmroot');
  if (!root || typeof MutationObserver === 'undefined') { return; }
  new MutationObserver(FMReport.schedule).observe(root,
    { childList: true, subtree: true, attributes: true, characterData: true });
  // Focus moves without a DOM mutation (our menu first-stop landing, tabbing) must re-report too.
  document.addEventListener('focusin', FMReport.schedule);
  document.addEventListener('focusout', FMReport.schedule);
  FMReport.schedule(); // report the initial render even if nothing mutates afterward
})();
