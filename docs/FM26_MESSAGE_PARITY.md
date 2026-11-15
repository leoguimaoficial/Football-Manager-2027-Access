# FM26 message contracts — F187–F190

## Readonly narrative with useful entity links — F190

The user's final steering is explicit: ordinary message prose (including person
names in the introduction), subject and metadata should read as FM26 plain text;
important player/staff links remain in their own useful section. Do not apply a
blanket removal to all names, nor keep every word in a narrative as a command.

Upstream sources reviewed: ElementBridge.CreateTextAdapter uses TextAdapter
outside table rows; ScreenWalker ordinary stops do not call SITextLink.ReadParts;
HtmlRenderer's default paragraph is readonly and retains the MessageBodyText
focus marker. The readonly heading branch does not add a separate subject-entity
action. Adapt that contract to the actual FM27 reading pane, retaining native
entity-list and table components for the useful links the user explicitly keeps.

MessageViewSurface.IsNarrativeText identifies actual reading-pane ownership and
excludes native tables, StreamedObjectList and NavigableLink entity components.
The original TextAdapter/paragraph path is reused there, and subject-link
extraction is suppressed. Important People links, native menus/record guards,
table actions, message metadata, whole prose, native spacing, body focus and
F188 grouping remain. Other screens/conversation questions are unaffected.

Actual responsibilities-message DOM on d28a5d60/game2676 has zero prose links,
three important-person links and6buttons total. Six capture-based DOM checks
pass; pure1633C#/83JS and compiled Release checks pass. The updated live driver
requires message7open and stopped before commands when the actual foreground
changed; no new complete11message/destination/manual NVDA pass is claimed.

## Native message content groups — F188

The reported squad introduction flattened its section titles, topic rows and
important-person rows into independent paragraphs. The actual screenshot and
native tree show two independent lists: topics (captains, best XI, candidates,
injuries) and important people. They do not associate each person with a topic.
Never invent that association from list order, player position or reputation.

Extend FM26's existing StopItem/RenderNode heading path and TextListItem list
contract. Native purpose/topic/people captions become live h3 headings; each
topic is an li and each actual person link plus its own description shares one
li. Ordinary linked-text/button renderers keep native IDs, record guards, menus,
actions and Alt+I. The demonstrated responsibilities variant adds one SIVisible
wrapper around purpose. Match these native structural boundaries, without
caption, language, subject, player or screen allowlists. Only consolidate a
fully realised native person list whose complete count matches the visible
BindingRoots; otherwise retain the original walk, expansion and paging.

Debug 3e097334-221b-4ffb-be80-6ac13f702e76/game2676 passes the new
live-message-content-groups.cjs regression in the men's FC Porto career. All 11
messages open/close; three introductions retain Objective/Topics/Important People
headings and lists. Every original squad-message leaf text/ID/action/menu stays
in exactly the original order. All three person links open native PlayerReport
and native Back restores the groups. Original all-read metadata, Tasks filter
and open squad introduction are restored. Evidence is the sole Companion's
reported DOM plus native tree/commands; an offline browser only parses captured
markup and never opens a second live game document. No new frontend-keyboard,
axe or manual NVDA pass is claimed. Unseen message layouts still require native
schema inspection; this does not qualify every future message type.

Reference: pristine FM26 Access0.2.16, commit
4d38a0256f29b97376845ef59edfcd64d7f165ed. FM27 remains the source of actual native
records, data, state, filters and commands. The current review compares the whole
message flow, rather than replacing required native adapters with FM26 API calls.

## Corrected empty-list rule

FM26 ScreenWalker.TryBuildMessagesTable returns without creating a table when
there are no rows. FM27 must retain its inbox item across an unfinished walk:
other streamed tables can load across frames before the inbox binds. Its renderer
therefore re-reads the actual native list immediately before emitting HTML.

That later path had emitted the four-column table even with zero final rows.
The actual men's career reproduced Por Ler(0) with an empty tbody and all four
column headings. RenderMessagesTable now omits that empty table after its native
re-read. It retains the provisional marker/recovery for genuinely pending rows.
This restores FM26's final accessible output without losing late native content.
The rule applies to every empty message filter and the Portal's shared inbox.

## Reviewed contracts

| FM26 source / interaction | FM27 disposition and evidence |
| --- | --- |
| ScreenWalker.TryBuildMessagesTable: no table for zero rows | Restored after the actual render-time native re-read. Empty Unread, refreshes, fresh document and Portal checked live. |
| HtmlRenderer.RenderMessagesTable: Status / Subject or Preview / Sender / Sent | Original semantic table retained. All11actual messages preserve their original subject, status, sender and date/time after the complete read cycle. |
| HtmlRenderer.RenderSenderCell | Exact source reuse; actual sender-owned menu semantics retained. |
| MessageButtonAdapter: subject/preview, sender, time and read/priority/response status | Native FM27 aliases, category chips and actual unread indicator adapt the original metadata contract. No subject-only flattening or invented commands. |
| MessageViewSurface.TryBuildSection / WalkMessageContent | Close, body, attachments, real tables, links and native actions retain the original composition. FM27 stale/native-ID and history guards remain. |
| FM.messages: body focus, reading-key behavior and bounded pump | Original behavior retained; differences are POST/screen-scoped transport and protection against delayed refocus overwriting newer input. |
| FM.msgview.key: Escape and context-menu precedence | Original modal close rule retained; Close and Escape both exercised throughout all11messages. |
| FM.msgview.refreshUnread / restoreUnreadFocus | Selected Unread removes the read row, returns to the next position and ends in the same empty filter. All retains read items. FM27 adds actual NewsItem/owner checks and cached-native-row filtering. |
| Four native message filters | All / New / Tasks / Unread remain ordinary native-backed buttons with exclusive aria-pressed. Original Unread selected marker is retained. No fabricated category/filter rules. |
| Native search, sender/context actions and linked-profile return | Original disclosure/menu behavior retained with actual FM27 opener/NewsItem identity and scoped native capabilities. Existing dedicated search/history regressions retain their recorded scope. |
| Asynchronous row binding, message identity and history | Keep FM27's confirmed-body barriers, native record checks, read-only summary handling and no-command-replay safeguards; these implement the same reading contract against FM27's recycled/cached rows. |

The live parity driver starts with all items read, uses the game's actual Mark
all unread command, reads every message, and restores the original all-read state.
It does not submit gameplay/message response actions, save or advance time.
Raw native/DOM observations remain ignored local evidence. No manual NVDA claim.

The initial driver expected an empty acknowledgment for a native menu action.
The actual successful response was the ordinary selected-label acknowledgment;
the driver stopped before reading any item. The game's actual Mark all read
inverse restored the initial state before the corrected full test. No uncertain
command was replayed. The final driver passes7groups /11messages /3clean axe
audits, including Close/Escape, body focus, next-row focus, final empty state,
later updates, four filters, fresh document and Portal. Pure suites pass1633C#
and83JavaScript cases; build/production checks are recorded in session progress.
