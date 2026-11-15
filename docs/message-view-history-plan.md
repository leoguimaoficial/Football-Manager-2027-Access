# Message history and native replacement

FM26 source: `.upstream/fm26/docs/message-view-history-plan.md`,
`FM26Access.Navigation/MessageViewHistory.cs` and the original Plugin
`MessageViewSurface`/`ServerHandles`/`app.js` implementation. Classification:
**EXTEND FM26 PATTERN**, with no new HTML component or shortcut.

The original normal sequence remains Inbox at native index N, virtual message
above N, and a native attachment at N+1. `MessageViewHistory` still owns that
behavior and its prefix/stale-message guards.

FM27's actual Cerci loan offer80448 instead replaces `PortalMessagesTool` with
`TransferOfferTool` at the same native index/count. Ordinary native Back then
skips both the Inbox and message. `ReplacedMessageHistory` retains the actual
Inbox and destination `PanelHistoryNode` values and adds three virtual stages
at that native index: Inbox, verified message, actual destination. Native
Back/Forward outside those stages still use the real game buttons.

The native action can also find an already existing offer before the source
Inbox: actual source2→offer1 was observed after opening/editing/undoing that
offer and reentering Inbox. Preserve the actual source Inbox snapshot, compare
exact preceding PanelID/Identifier visits and the reused destination PanelID, and anchor the
composite at the actual destination index. A smaller index or matching panel
name alone is insufficient. The native list/index remain read only.

Actual Debug inspection found the destination Identifier renewed16→20 while
preceding Portal10 remained unchanged, source Inbox19 disappeared and native
count3→2/index2→1 changed. The old destination Identifier must not be required:
capture its NEW result Identifier and guard that afterward. Changing any earlier
visit or the destination panel kind remains a refusal. The pure prefix policy
tests this actual renewal and the negative cases directly.

Only a validated activation inside the current native reading-pane content arms
this capture. Message opening alone, ordinary history or unrelated navigation
must not turn a previous screen into its attachment. Explicit navigation clears
pending capture; reactive native modal transitions retain its actual action
context. Capture the temporary Inbox of an existing composite correctly as well.
The live earlier-slot case is a separate gate (--require-native-reuse); passing
the original same-slot case does not establish it.

The native stack is read only. Entry identity is the native history slot,
manager Pointer, official PanelID/Identifier and preceding shape prefix. The
current foreground additionally retains its actual Pointer and binding key.
Boxed native value-type Pointers are not node identity. Reopening uses the
actual native `OpenPanel` arguments; no offer/business action is replayed.

Native `ForceIgnorePanelForHistory` was rejected after an actual probe changed
the stack. A synchronous `m_ignoreEvents` scope passed retained-panel probes but
failed when the offer was recreated asynchronously. The adapter now holds the
native recording guard until its own rooted transition-completion callback,
with bounded expiry and shutdown/load cleanup. Each callback retains its own
operation, so a late callback cannot release another operation's guard.
Uncompleted callback roots are bounded; exhaustion requires restarting the game.

The temporary Inbox must close through its actual native owner before leaving
the composite entry for a native history move. Otherwise native Back closes the
recorded offer while leaving that unrecorded Inbox covering its parent. Native
landing waits for both the actual interactive content and the native replay
guard to finish. The highest navigation sort entry alone is insufficient on
Portal because it can be OverviewTitleBar. A reopened runtime panel can also
have another Identifier than its retained native history node.

Message restoration retains the existing actual NewsItem ID/subject check and
native row fallback. Explicit Escape retires the message. Explicitly selecting
another message truncates the old virtual destination branch. A normal native
push from a composite message must return to that same verified message.

The frontend recognizes same-index owner transitions. It consumes a return
only after its own command completes, because a temporary Inbox can arrive
before the message restore. Existing history focus memory then restores the
actual control. Later authoritative bodies repair a lost exact control until
new user input cancels the handoff. Ordinary text fields/arrows/shortcuts remain
unchanged; Alt+[ and Alt+] retain their original meanings.

Earlier-slot reuse also truncates the original cursor keys. The coherent body
header carries cursor provenance only while its actual native composite/owner
is verified. Retain the original source index/token and NewsItem ID; before
pruning a replaced branch, remap its two previously observed positions (message
action and Inbox row) to the actual new index. Require the stored message ID to
match. Binding renewal alone cannot identify another message, and a generic
panel-name fallback is insufficient. Explicit reselection captures a new origin;
native restoration retains the old one. Loading a career clears old cursors.

Verification is separate from this design. Pure state/coordination tests are
linked to production code. The real regression is
`tests/companion/live-message-replaced-history.cjs`, scoped to the explicitly
owned February checkpoint and its actual pending offer. It verifies native
stack preservation, exact message identity, focus, chrome/keyboard history,
native parent traversal and Escape. Current pass/failure artifacts and limits
are recorded in session progress, Testing and matrix F119. Neither this document
nor a successful build establishes whole-career or Steam qualification.
