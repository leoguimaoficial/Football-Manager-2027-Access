# FM27 Access session progress

## Installer 1.0.2 self-update and beginner manual — 2026-11-15

The user requested startup installer-update offers, one Repair action, readable
player-facing notes, then a fuller accessible beginner manual using FM26's guide.
Source `d7d2e9a65ae9c1570dc8a32fd7ed7387955a1170` is on main and both hosted
CI43751083467 and documentation43751083600 succeed.59 installer cases, five
distribution contracts and all20 guide browser editions pass. Tests cover skip/
contents keyboard focus, real tables, anchors/languages,320px reflow, dark/high
contrast/print, no-JS reading and axe. Dutch long-title overflow was found/fixed.
The expanded beginner chapters are English/pt-BR/pt-PT; all20 core editions
correct Content, store-evidence limits, main navigation and startup-update text.
The official FM27 sources and FM26 manual attribution are in user/SOURCES.md.

Actual local1.0.2 UIA sees Steam0.1.0 installed, exactly one Repair action,
engine title and readable notes. The screenshot showed another app and is
excluded. A later tool reported Escape interruption; app input stopped, with
no assertion about physical user input. No further Computer Use was performed.
No mod/game command, save/career action or manual NVDA test occurred here.

Publication retains release446195775 and mod tag2a6773ce. Only installer EXE,
installer metadata, the additionally requested manual and checksums changed.
All eight resulting assets were independently downloaded and fully verified.
Original mod/source ZIPs, manifest and build-info retain exact hashes and IDs.
Public installer1.0.2 SHA D51D0DD86746D7BDF6A82E95740B3BD9DC574B07978990021B26523FCDE38BB9;
manual SHA8E729F2E7B3530F383749E1B14F9C4202E796B2789A2AD59560D2F54FC892DA6.
Installer metadata identifies its independent source commit/run; the historical
mod source ZIP is intentionally unchanged. Player notes now describe outcomes.

The exact original public1.0.1 Core, built into an isolated qualification host,
finds the actual public1.0.2 and invokes its real downloaded helper. Parent exit,
replacement hash, previous backup, receipt, official new-process title1.0.2 and
helper cleanup all pass. The test's new installer process was stopped afterward.
The user's Downloads EXE remains hash35C7E073.../engine1.0.1 and PID4036 is left
running for their own button/confirmation test. The original GUI was not driven
through update. Explain:1.0.1 requires Update installer;1.0.2 introduces automatic
startup offers. The new Core's public services find no newer engine, validate
the unchanged0.1.0 manifest and fetch the exact updated canonical manual. Actual
Steam receipt and installed Host hashes remain unchanged. No game is running.
Xbox retesting remains deferred because the user uninstalled that game.
Raw receipts, old-source host, backups and publication journal are ignored under
artifacts/installer-1.0.2; see distribution for exact provenance and limits.

## Verified Steam 0.1.0 replacement and final handoff — 2026-11-15

Successful hosted build [43745356921](https://github.com/leoguimaoficial/Football-Manager-2027-Access/actions/runs/43745356921)
at source `2a6773ce2dbb2e1434f5dca3b897ae36720db0c5` supplies all eight refreshed
public assets. The original immutable flag was false. Under the maintainer's
explicit same-version authorization, the existing release ID446195775 and
`v0.1.0` name were retained, with the tag moved to that verified source. Previous
assets were backed up locally, staged replacements were digest-checked, and the
old assets were removed only after independent unauthenticated downloads of all
eight replacements passed exact artifact/source/checksum verification. No new
version, release or replacement automation was created.

The exact hosted payload was receipt-repaired locally before publication; all259
file hashes/native guard pass. Steam game21752 cold-starts core
`12107ea9-b698-470e-b7ed-6b9e1abbf67e`, developerTools=false. The complete four-audit
live-steam-startup.cjs suite passes again on these exact bytes. Sole normal
Companion10724 is restored from the actual Steam executable's installation,
watch-pid21752. Its actual UIA tree contains MainMenu,27.0.1 and the ten normal
buttons plus Mais combobox. A screenshot showing the desktop is excluded as
visual evidence. No auxiliary qualification client remains; no save/career or
preferences commit was performed.

Final public installer-service verification reports online=true, one stable
version0.1.0,260 manifest files, minimum/current engine1.0.1 and online guide.
Player ZIP SHA-256 is
`C3E708F96E458721427904ED41666CBCBA94CC68BE54197BD1660DE4EB28729C`.
Tag and public bytes match the selected build; logging remains console-off /
Fatal-Error-Warning / InstantFlushing=false. Native game files remain unchanged.
The local installation wrapper initially checked an unset native LASTEXITCODE
after a successful .NET API operation, falsely reporting failure; receipt/guard
were independently reverified and installation was not replayed. A subsequent
read-only check needed an explicit Speech assembly load; no game failure occurred.
Raw receipts/backups/reports remain ignored under artifacts/steam-investigation.
Full Steam career, alternate-proxy Xbox, Epic, external clean-machine onboarding,
actual installer self-update UI and manual NVDA remain outside this evidence.
F047/F115 and the historical unresolved issues remain recorded.

## Steam startup correction and authorized 0.1.0 replacement — F206, 2026-11-15

The maintainer explicitly requests working Steam startup/main menu, Steam as the
primary development target, compatible Xbox support where practical, and replacement
of the existing 0.1.0 release without increasing the version. This authorizes that
specific replacement after verification; it does not make the current candidate
stable or qualify a complete career. Work is on codex/steam-startup-compatibility.

Steam build 26577676 uses Unity 6000.3.18f1/metadata39/BepInEx793. Its fresh 179
interop assemblies pass the published required API. Release and explicit
Debug/tools builds compile against Steam with zero errors; inherited test warnings
remain. Current 1,644 pure cases,48 installer cases,83 Companion JavaScript cases,
five distribution contracts and all20 guide locales/browser/axe checks pass.
Loader/deployment/player/production/retention/cleanup fixtures pass. The private
reference bundle and public pin now record the inspected Steam compiler input.

Production core412b1c64/game20608 cold-starts through Steam: nine patches, complete
Host/core initialization, one owned normal Companion24912/watch20608, health200
with screen=Loading/developerTools=false. Native screenshot shows the logo splash;
Companion UIA says Waiting for the game to be ready. A managed stack sample shows
server/socket/thread-pool waits, without identifying a blocked mod call. Startup
does not reach MainMenu in that original configuration. Its startup failure
is retained separately from the corrected runs below.

With Doorstop disabled and no CoreCLR loaded, the WinHTTP proxy still leaves
the game at the logos. A noninvasive native stack places its main thread at
game_plugin!WaitForActivation; this is a named observation, not a licensing
diagnosis or permission to patch native activation. Removing only the owned proxy
lets fully unmodified game21048 reach MainMenu/What's New. The same pinned proxy
bytes as upstream-supported version.dll then start original Release core412b1c64/
game20508 normally. Only Windows' own WinHTTP is loaded alongside it. All nine
mod patches apply; normal Companion10212/watch20508 renders the actual menu.

New live-steam-startup.cjs uses the sole production-document qualification client:
native MainMenu/What's New/Preferences/load-dialog open/close, exact origin focus,
complete labels/descriptions, four clean WCAG-tagged axe audits, no unexpected
browser/request failures and production developer-route404 refusal pass. No career
or save is selected or loaded. The first driver wrongly expected Cancelar in the
load dialog; its native button is Close. That expectation was corrected after
observing the real controls, with the failed report retained independently.

Installer1.0.1 now migrates only the unchanged receipt-owned byte-identical old
proxy, rejects shared/modified/conflicting proxies and rolls back through the
existing journal. The mod remains0.1.0. An actual repair from the original installed
receipt passes all259 installed-file hashes, exact native API guard and restored
production logging. Corrected candidate coref5e6cb73/game27812 cold-starts through
Steam with developerTools=false and owned Companion24196/watch27812. The same
four-audit/native-action regression passes again on that candidate. New public
artifacts and exact hosted-build qualification are still pending publication.

Tool observations: a launch API initially returned no targetable window while
Steam was still preparing the launch. A subsequent direct launch overlapped the
eventual Steam launch; those processes were closed and their port conflict is
excluded from clean-run findings. Later Steam launches stalled before fm.exe at
CheckShaderDepotManifest. Normal Steam shutdown/restart enabled the clean run;
another restart briefly exposed Steam login. Authentication was handed off, then
the client was observed back in its normal session without automating credentials.
No physical user action is inferred. Sky sometimes reports a stale UIA loading
document while its screenshot shows the actual menu; a later activation fails.
Use actual sole-client DOM/native results and distinguish that helper behavior.
Its CANCELAR attempt in Steam returned coordinate input geometry is unavailable;
the later observation showed the native launch had proceeded, so no retry occurred.

All temporary diagnostic changes were hash-checked and restored before the actual
installer repair. BepInEx normalized the diagnostic flags list to equivalent All;
exact text comparison proved that sole setting normalization before restoration.
Current console is off, disk Fatal/Error/Warning, InstantFlushing=false, and no
legacy WinHTTP proxy remains. Original payload/receipts are backed up only in
ignored artifacts/steam-investigation. The currently inspected Documents game
directory contains zero .fm files; do not inherit the historical55-save inventory
as current Steam state. No save deletion/load/write or career action occurred.
F047/F115 and previous unresolved coverage remain; Xbox with the alternative proxy,
Epic, full Steam career and manual NVDA are not newly qualified.

## Authorized 0.1.0 publication and hosted compilation — 2026-11-15

The maintainer explicitly authorized publishing 0.1.0 and updating the principal
GitHub documentation. This supersedes earlier development-only wording. The
installer's initial discovery no longer raises automatic live status speech or
replays focus; user-initiated progress and Tab-accessible messages remain.

The maintainer rejected using their own Windows computer as a build executor
and explicitly authorized private compiler references instead. The temporary
runner process was stopped and its GitHub registration removed; no service was
installed. Automatic approval review rejected deletion of its inert local files
with `blocked by policy`; the deletion was not retried or bypassed. No current
workflow uses a local executor.

The private FM27Access-Build-References repository contains only generated managed
compiler DLLs and checksum/index documentation. The public repository pins its
exact commit and identity. GitHub-hosted Windows jobs use a dedicated read-only
deploy key, validate references and compile the actual mod, Companion and player
package using the pristine locked loader. Private inputs are excluded from public
Git, workflow artifacts and source/player archives. BepInEx still generates each
player's own interop on first launch.

Normal developer builds compare the actual game/loader/reference identity to the
pin. A mismatch identifies the single reference-refresh command, which verifies
the private destination, exports and pushes the input, then updates the source
pin for review and game regression. No local daemon, service or scheduled task
is required. GitHub cannot observe a game update without updated compiler input.
The maintainer requested full automation and enabled repository-local
`fm27.autoUpdateBuildReferences=true`: their normal requested build refreshes
changed input automatically, with no copy or upload for an unchanged identity.
Other checkouts retain validation-only behavior unless their maintainer opts in.
Local validation passes 1642 C# tests (including nine private-reference integrity
and changed-baseline cases), 42 installer tests, 83 Companion JavaScript tests,
four distribution contracts and all 20 guide locales/browser/axe checks. A full
Release package built using only the restored private references and downloaded
official loader passes production-binary and source/player/archive integrity
checks. The reference refresh command recognizes the unchanged baseline without
uploading. These are build/distribution checks, not new live-game qualification.
Hosted main build 43735160905 passes portable and full distribution jobs at
6ed06c8b58c5a515b9d46c4fbb30a8cb3c051faa. Publication 43735594264 passed the
configured required-reviewer gate using the maintainer's explicit authorization
for this exact 0.1.0 release and published v0.1.0 at 2026-11-15T16:29:55Z.
The public tag matches the selected commit. All eight public downloads were
retrieved without authentication, matched against the original hosted artifact
and revalidated for source provenance, production content and integrity. The
actual installer service reports online=true, one stable version 0.1.0, 260
manifest files, online guide and current engine 1.0.0. ZIP SHA-256 is
0B88E2F99182FC899B650249B932AE5CB659BCFF0D03B1DA13E4E5DCFFBD35CB.
Historical game qualification
and unresolved issues below remain unchanged; no new game installation or manual
NVDA qualification is claimed by this distribution work.

Post-publication packaging follow-up: actual PowerShell Invoke-RestMethod returns
a JSON array as one pipeline object. Wrapping it in another array could stop
catalog pagination and unnecessarily rebuild an unchanged installer when several
releases exist. The packaging helper now iterates the returned array directly.
An isolated real-shape fixture covers multiple releases, stable/preview filtering,
a full 100-item first page, exact engine hash reuse and empty-catalog first-engine
fallback. All five distribution contracts pass. This changes future packaging,
not the published installer binary; the 0.1.0 tag/assets remain unchanged.

## Universal installer, distribution and player guide — before publication

The new explicit distribution request authorizes dedicated installer projects,
packaging, tests, GitHub workflows and multilingual player documentation. It
supersedes the earlier restriction against new build/release infrastructure.
The latest clarification says 0.1.0 is installable now and the HTML must be a
player guide, not a technical audit report. GitHub release/tag publication still
required separate explicit approval; this earlier stage did not create one.
The authorized publication and final evidence are recorded above.

The separate native Windows Forms installer follows the maintainer's Reigns
Access reference while retaining FM27's existing native API guard, original
five-project solution, production build defaults and receipt safety. The engine
has its own version, public filename FM27Access-Installer.exe, official GitHub
release discovery and explicit local manifest/ZIP selection. Install, update,
downgrade confirmation, exact-version repair, removal, cancellation, rollback,
durable recovery, separate logging restoration, independent guide updates and
installer self-update are implemented. The complete ZIP includes the pinned
BepInEx 793 IL2CPP x64 runtime and production console-off defaults. Normal disk
logging remains Fatal/Error/Warning. No game/interop/compiler binaries enter
source archives; the Prism redistributable exception is retained.

The user identified the WindowsApps package-registration alias appearing as a
second installation. Actual filesystem inspection confirms a protected reparse
point. Resolve directory aliases before deduplication; inaccessible aliases are
excluded without changing permissions. The later actual UI Automation tree
shows only the usable XboxGames Content destination and recognizes installed
0.1.0 from its assembly. Its older source receipt is not silently adopted. The
inert main-screen Technical details button was removed; actual errors provide
an optional read-only diagnostic view in their own dialog.

The later Tab-access report adds a native multiline read-only Installer messages
field to the main tab sequence. It retains recent operation/error messages
without moving focus on progress updates. Error-dialog explanations are also
read-only multiline Tab stops. Install/update/repair/downgrade and uninstall
completion use a standard Windows information dialog; the same message remains
available in the main message field after dismissal.

The public FM26 release v0.2.16 HTML was downloaded and read, alongside its public
README/INSTALL and the pristine source reference. Its player-oriented structure
informs the replacement guide: setup, screen-reader settings, Enter-to-confirm
choices, searchable menus, column options, bookmarks, career introductions,
delegation, messages, squad, tactics and match readouts. Shortcuts were checked
against current app.js/AccessibilityServer. The generated offline HTML has 20
languages, 14 sections each and two semantic shortcut tables per language.
It contains practical instructions rather than build/API/qualification warnings.
Developer receipt/reload instructions are preserved separately in
DEVELOPER_INSTALLATION.md; workflow/qualification evidence stays in DISTRIBUTION.md.

Validation so far: 1,633 existing C# tests and all existing script fixtures;
83 Companion JavaScript tests; 42 installer tests (including interrupted
preparation recovery and production logging); four distribution/documentation
contracts; the solution and self-contained installer/Companion Release builds;
and real package integrity/production binary inspections. Browser/axe checks
exercise all 20 guide languages, anchors, fallback, narrow viewport and forced
colors. Final regenerated packages are rechecked after source changes.

Evidence limits: UI Automation is actual native installer evidence, not manual
NVDA qualification. Screenshot capture returned an unrelated surface and is
excluded as visual proof. Some input attempts reported `user input was detected`
or `coordinate input geometry is unavailable`; these are tool observations, not
claims of physical user actions or completed keyboard tests. No actual game
installation, reload, launch, save or business action was performed. The game's
existing QA runtime and receipts remain intact. Clean-machine installation,
real engine self-update and live Steam/Epic gameplay are separate remaining
qualification. GitHub's trusted build runner, repository variables and protected
reviewer environment require external setup; local passing tests do not imply
those hosted jobs or a publication approval have run.

## Initial GitHub source publication preparation

The new explicit request authorizes creation of a public
`leoguimaoficial/Football-Manager-2027-Access` repository, an initial source commit
and push to `main`. It does not authorize a software release. The current F205
runtime implementation and existing audit knowledge are preserved; only public
documentation, repository rules and source exclusions are updated.

Fresh verification passes 1,633 C# and 83 JavaScript tests, existing deployment/
profile/retention/cleanup fixtures, and the solution/Companion Release builds
with zero warnings/errors. These are source-publication checks, not new live-game
or manual NVDA evidence. No install, reload, game action, save operation or new
package is part of this task. Existing runtime and qualification limits remain.
See [repository preparation](REPOSITORY_PREPARATION.md) for the publication
baseline; final remote evidence is local in `artifacts/publication/`.

## Automatic arrivals never speak internal panel-name fallback — F205

The observed foreground is now pre-career advanced setup, AddNationsModal,
rather than the earlier men's checkpoint; no agent save/exit/world creation is
part of this change. Before-fix actual Prism history records
DatabaseSelectionCard three times and AddNationsModal once. Native open logs show
empty PanelID titles. Later actual Companion/game hierarchy and screenshot show
Adicionar Nações, while HTML already has that real heading. The first panel-entry
announcement outruns native caption binding and uses GetTitle's internal-name
fallback. The existing on-screen-title extraction itself is correct once bound.

Factor the existing shared resolver into ReadTitle with explicit fallback policy.
GetTitle retains the complete document/bookkeeping contract. GetAnnouncementTitle
uses the same actual native/chrome/title extraction but returns null when no
title is available, instead of PanelID.name or Screen. The existing PanelEntry
scope already handles null as silence; wire all three open/close-return/reload
entry seams to it. Do not queue a later announcement, read the first control,
mute explicit cursor/Alt+I/action feedback, redesign HTML, change history or add
translated-caption/panel allowlists. Real available short titles remain intact.

Receipt-aware Plugin-only reload853533d4-42cb-4962-8103-a0b1e9a69d02/game2676:
three real AddNationsModal open/cancel cycles emit no automatic entry speech,
retain Adicionar Nações in HTML and announce Configuração Avançada on each real
return. All five continent filters'59 actual country choices are observed; the
original selected countries and original continent are restored without Confirm.
Advanced table/form values compare unchanged across the cycles. First-response
HTML headings also contain none of the internal panel IDs. The maintained
live-native-title-arrivals.cjs passes without JS/transport failures.

The actual Número total de jogadores dropdown offers Small/Medium/Large/Custom
database choices using official Portuguese labels. Its Base de Dados Personalizada
option opens GameCreationCustomLeagueSetup with Definição Personalizável in the
document/heading. No internal arrival title is spoken; ordinary Base de Dados
Personalizada selected feedback remains. Native Cancel returns advanced setup
with the entire captured table/input/dropdown state unchanged. The maintained
live-custom-database-title.cjs passes. DatabaseSelectionCard's file-selection
popup is covered by the shared fallback correction and its before-fix evidence;
that separate popup is not reopened by abandoning the user's advanced setup.
No database load, setup commit, country commit, appearance edit, save or world
creation is issued. No manual NVDA or every native-title variant is claimed.

1633 existing C# tests, both script syntax checks and the seven-exact/four-adapted
runtime/source audit pass. Release040249 passes compiled production/companion/
package policy; SHA256
CE0BFB70C18259AD2E19A33D709B638C312F1985E28982B18B2491B55470C3F3.
It is built/inspected, not installed or cold-qualified. Normal owned Companion
is restored at Configuração Avançada; existing audio calibration is unchanged.

Tool observations: a read attempted the first long-running test's report before
its finally block wrote it; this is not a test failure. An optional follow-up
expected the prior AddNationsModal but observed advanced setup before issuing
any action; the intervening close cause is unconfirmed, not attributed to a
physical user action. Another optional follow-up expected an older Avançadas
option label; actual options instead contain Base de Dados Personalizada. Its
pre-selection assertion fails with no value commit. Both harness observations
are retained separately; the corrected maintained actual-option test passes.
Evidence: setup-title-before-*, native-title-arrivals-live.json,
database-title-arrival-check.json and current-verification.json.

## Shared confirmation command readiness — F204

The user's actual men's-career GenericModalDialog for Gravar Antes de Sair e
Regressar ao Ecrã Inicial? is reproduced: the normal Companion DOM has Cancelar,
Não and Sim as paragraphs, while a readonly native probe already sees three
enabled SIButtons with bound commands and ButtonAdapter.Role=button. The actual
game screenshot confirms the three controls. Their callbacks bind after their
captions/tree: the generic adapter correctly treats CanSubmit=false with neither
Events nor custom action as readonly at initial composition, but the existing
shape observer only watches command readiness for NewsItemButton. Later value
updates carry unchanged labels and cannot turn a paragraph into a button.

Extend the existing shared ScreenWalker.CollectShape observation to ordinary
SIButton capability (CanSubmit or actual Events/custom callback), retaining
Pointer identity outside streamed rows and their separate realization/record
contract inside. This reuses the original bounded FM26 settle/restructure/morph
path. Keep the readonly-content distinction, native enabled state, standard
ButtonAdapter/ServerHandles activation and all callback/identity checks intact.
No caption/screen allowlist, synthetic game command, timer/poll-budget change,
native-state cache or dialog-specific replacement HTML is introduced. The
original GenericModalDialog profile and ordinary button markup remain unchanged.

Receipt-aware Plugin-only reload25ec1f2a-b962-4e2d-8087-d6d4f4d8bc97/game2676:
the sole original client opens Sair para o Ecrã Inicial and Deixar Jogo e Sair
three times each. Captured first /body responses contain zero HTML buttons;
normal SSE follow-ups automatically deliver all three actual buttons roughly
0.35–0.74s after those initial responses, with no F5 or explicit refresh call.
Title focus survives settlement. Tab/Shift+Tab remain trapped in the dialog,
background is absent, and Cancelar via Enter/Space plus Escape close through
native callbacks and restore exact FM Menu origin focus. Both prompt variants
have zero axe violations. All compared save file sizes/mtime values are unchanged.
No save, No/Yes response, return-to-start or game-quit command is confirmed.

The original return-to-start prompt is reopened for the user. The normal owned
Companion is restored (PID23408/watch2676); its actual DOM and Windows UIA show
Cancelar, Não and Sim as enabled buttons with the dialog title/content intact.
1633 existing C# tests, node syntax, runtime installed-MVID/hash and seven-exact/
four-adapted source checks pass. Release035336 passes compiled production,
companion and package policy; SHA256
A9355AC622DF83858F76521B591FDC2BC53F91FA0CEEE3AB7360CA6506786CD6.
It is built/inspected, not installed or cold-qualified. No manual NVDA or every
native confirmation variant is claimed; the correction is shared by all ordinary
SIButtons. Existing audio calibration remains untouched.

Tool observation: the first optional probe's typed SIButton access hits the
known mcs metadata-import error. The supported reflected adapter/command read
succeeds without modifying production registries. Evidence is retained in
generic-dialog-before-*, generic-dialog-native-buttons.txt and the maintained
tests/companion/live-generic-dialog-readiness.cjs report
artifacts/generic-dialog-readiness-live.json.

## Headerless interval command ribbon and bounded audio audit — F203

The actual men's AFS0–0FCP half-time screen has ContinuePanel in MenuTitle and
HalfTimeTeamReport in MenuBody. The former has no native heading; ServerText's
synthetic Navigation caption becomes an unnecessary h2 below the score h1.
Native screenshot confirms the untitled command ribbon. Extend FM26's existing
OverviewHeader level-zero rendering to this demonstrated ContinuePanel owner.
All controls, identities and section/report headings remain intact. No translated
caption matching or global heading suppression is introduced. Receipt-aware
Plugin-only reload gives 6dba703f-b32c-46a1-9e12-251913da5f44/game2676.
The normal Companion's actual reported DOM has no Navigation heading, keeps the
score/report headings and all 20 original control tags, labels, IDs and disabled
states at 45:00. Read-revision timestamps legitimately change and are excluded
from that structural comparison. Full-time use of the same native owner is
covered by source structure, not newly replayed as a live full-time test.
1633 existing C# tests and the seven-exact/four-adapted runtime reuse audit pass.
Release032507 passes compiled production/companion/package policy; SHA256
2EED7502F88A1CB8AD7BBE38042377E20A55A026E8EDB32F887E71137D190CA6.
It is built/inspected, not installed or cold-qualified.

The preceding user audio request remains a bounded investigation: all 25 WAV
files equal pristine FM26 byte-for-byte. In the sole original headless client,
all 23 actively loaded sounds decode, Web Audio runs, and master/possession/event/
playback gains are 1. Drone and pressure buses are 0 because both saved switches
are false; no preference is edited. Air has audio in its first 100ms and no
missing/quiet introduction. Offline rendering of the actual decoded air buffer
at the inherited 0.03/0.20/1 ramp positions gives RMS 0.00110/0.00176/0.01605.
Earlier native evidence has nine completed flights, median peak3.1m/max5.6m,
plus actual air-on/off and airlevel0.03–0.20 events. This yields roughly28–32dB
attenuation under the unchanged FM26 2.5–10m /33–9dB ramp. That can explain weak
audibility, but does not prove the device output or justify arbitrary retuning.
No source audio behavior is changed and not every cue has fired in this match.

Evidence limits/tool observations: the first screenshot returned Codex content
despite the selected game window; only the subsequent activated-game screenshot
is native layout evidence. The audio test's later unrelated half-time-heading
assertion fails because it observes Your Squad at45:08 instead of the earlier
45:00 interval. The failure is retained, while audio assertions before it pass;
no combined all-pass claim is made. Its only explicit input is Tab, with no
native match/navigation command. The cause of the intervening screen/playback
change is unconfirmed; do not attribute it to physical user action. The exact
owned Companion is restored in finally (PID30752); a later real DOM observes
playing49:03, with the previously corrected in-match headerless strip intact.
No manual NVDA, physical audio listening/capture, actual WebView audio-output
proof or full cue-event coverage is claimed. Ignored halftime-heading-check.json,
match-sounds-assets.json and match-sounds-client-audit.json retain the evidence.

## Native match preferences with FM26 form contracts — F202

The user reports repeated Melhores Momentos:4 of0 to5 and requests the FM26
settings presentation with FM27's native elements. Current MatchPreferencesCard
is open at AFS0–0FCP, paused9:46. Its profile was still the exact FM26 file, but
actual FM27 ancestry differs. Five HighlightModes radio choices each contain a
decorative SIRadialGauge in inputs-hybrid-task_switch_button-regular; the generic
radio-extra walk exposed their fixed ordinal fills as extra fields. All five
already have their own actual official choice caption. Native gauges have no
tooltip/action. The current Interface/Highlights/Camera/Miscellaneous captions
also no longer match the old profile's section paths, and an old rename replaces
the correctly recovered opposition-replay caption with English.

Adapt the existing profile only: hide the radial glyphs under the actual native
HighlightModes group; preserve other gauges, options, state and native order.
Promote six actual section/subsection captions through structural name/path/
first-child rules (no translated caption matching), retaining original profile
rules for unchanged structures. Remove the obsolete fallback renames so the
common label resolver supplies actual official FM27 labels. Keep the original
FM26 fieldsets, radios, checkboxes, ranges, dropdowns, help and native dispatch.
No new screen, choice list, settings model or framework is introduced.

The native fullscreen button also reports selected through its active segmented
style, while its own transparent background makes the inherited
SegmentedOptionAdapter report unchecked. Reuse the existing ordinary button
active-style resolver from the derived adapter, keeping FM26's solid-fill fallback.
This shared correction retains the existing radio/values/command protocol.
The display mode itself is not changed by testing.

Debug/tools=truef45ba0f4-fa1a-473e-9fd8-534bbb7537a4/game2676:20 snapshots compare
all35 native logical controls against the sole original client's names, checked
properties, availability, selected values and slider bounds/steps/value captions.
Six native section/subsection headings are present, all five decorative numeric
announcements are absent, and Fullscreen correctly shows checked. Graphics
options open/cancel without a commit. Dynamic mode off enables actual highlight
choices; key highlights and the original summary/dynamic state are selected and
restored. Replay2D/3D, both replay toggles, both speed sliders, information-layout
Medium/Large and Tactical/Broadcast camera inverse checks pass. Native dependent
availability is retained. Closing/reopening preserves the complete original
35-control state. No match resume, Instant Result, display-mode or text-scale
change; final surface is the open preferences card with original values.
Native screenshot corroborates the actual controls/artwork and disabled states.
The native drawn clock9:45 and inherited Companion display9:46 retain their
existing clock presentation; no clock adaptation or elapsed-match claim is made.

1633 existing C# tests pass, no skips, and the Debug build has zero warnings/errors.
Runtime/installed hash and seven exact/four adapted central FM26 contracts pass;
the three current presentation/adapter changes are separately reviewed, not
misrepresented as exact-source equality. No manual NVDA or full graphics/display
mode qualification is claimed. Release030109 passes compiled production/companion/
package policy; SHA256411B99D226ACC1B969EB0A049A2C0E7FE86768C51A6C8D9E22ACD3DD64615F02.
Release is not installed or cold-qualified. Source/evidence remain local.

Tool observations: an initial mcs probe could not load typed Unity TextElement;
the supported reflection read succeeded. Another ignored diagnostic enumerated
properties on a field-only match-info struct and returned no metadata; the
corrected field enumeration supplies the actual profile ancestry/child indices.
Neither probe resets production body/command/value registries or changes native
settings. Final evidence: match-settings-native-matches.txt,
match-settings-live-check.json, native control inventory and current-verification.json.

## Paused-match comparison and extra landmark — F201

The user asks whether FM26 provides a side-by-side club comparison while paused,
and reports an extra main announcement before pitch entry. Current native/normal
Companion remains OverviewPrototype, AFS0–0FCP at3:06 paused. Its two summary
tiles show separate club statistics and native Ver estatísticas actions. Actual
opening of either action reaches MatchStatsCard during the ongoing match, with
three-column stat/AFS/FC Porto tables. Key stats show11 rows, including remates1/0,
xG0,03/0,00 and posse30%/70%. The original MatchStatsTableReader and MatchStatsHtml
remain exact apart from project naming; comparison has no full-time-only gate.
Both key and advanced native tabs are available. No dashboard redesign is made.

The FM27 renderer added main around covering non-answer overlays, while FM26's
RenderSection explicitly avoids extra region announcements and renders actual
headings alone. Before-fix captured MatchStatsCard HTML contains a main landmark
beside the score and its meaningful native title; main is not literal native
heading text there. Replace that wrapper with a neutral div, retaining the
existing data-fm-native-overlay/data-fm-overlay-title attributes, section title,
real answer-dialog role/aria-modal, suppressed background and all focus/return
logic. One maintained customization test now recognizes the existing overlay
identity instead of depending on the main tag. No client JavaScript changed.

Explicit Debug/tools=true1a69e696-2300-4c5f-8f3c-763dfdfa2284/game2676: both
native statistics openers, three key/advanced/key observations per opener,
AFS/FC Porto attribution and no main landmark pass. Native title receives
initial focus; underlying Play control stays absent while the card covers it.
Close and Escape return to the exact respective opener, restoring the paused
surface with unchanged score/clock3:06. The match is never resumed.37 existing
C# match-stats/profile tests and83 client unit cases pass; runtime/installed hash
and existing central FM26 source audit pass. Native paused layout and actual card
tree/DOM corroborate the scope. No manual NVDA, exact earlier pre-pitch replay,
new customization-card live pass or whole-match qualification is claimed.
The shared neutral wrapper applies when that earlier overlay appears again;
its reported announcement is not misrepresented as a newly reproduced heading.

Release candidate024139 passes compiled production/companion/package policy;
SHA256EB85DB5FD12FF088C519376EFBB99D82D94F9DB87A68912B317F47196E7FBB68.
Release is not installed or cold-qualified. Ignored match-pause-stats-check.json,
match-pause-landmark-check.json and current-verification.json retain evidence.

## Repair the pre-match reactions table — F200

The user advanced to the current men's TeamTalkPanel and reported an automatic
table update failure. The actual Companion displayed table-not-loaded instead
of25 players. A read-only native probe isolates ReadRowIdentity ->
SetPieceTakers.RowIdentity -> BindingRecords.Fields -> native GetInfo throwing
KeyNotFoundException for context key1414818164. The ready row has an actual
DynamicReference containing Player (MatchPlayerReference), Team (TeamReference),
selection/position/rating/reaction fields and one unregistered Boolean key.
The generic table reader incorrectly applied the specialized taker schema to
every non-reference binding. Repeated refreshes therefore could not heal it.

Preserve the original FM26 walker, 50-row loader, table HTML, selection helpers
and SSE path. Restrict the taker-schema identity to its actual existing block;
other inline rows retain their own direct InteropReference fields and native
numeric property keys through InlineReferenceIdentity. Mutable ratings/reactions/
positions are not identity. No property-name lookup, caption matching, cache,
business-value mutation, retry-budget reduction or availability override is added.
Direct record identity and the specialized type/side/player contract remain.

Explicit Debug/tools=true22db21c9-8acb-479f-8b72-85a6762f5bda/game2676 loads the
normal Companion's25 actual players and reactions. The sole original production
client checks native row count, every name/reaction/checked state,25 unique full
MatchPlayer/Team reference identities, current-record acceptance and rejection of
another actual player's identity. Native Defesas/Médios/Avançados/Individual
selection changes and inherited native range-select/clear update automatically
without F5; final checkboxes are empty and delivered-talk text is unchanged.
These buttons select recipients, not a filtered subset: all25 rows remain.
The final audience selector is Individual; Plantel cannot be restored because
its actual SIEnabled Squad ancestor disables it. No talk response or
Ir para o Campo command is submitted. Native screenshot corroborates the screen.

The user also questioned the selection helpers and Plantel's availability.
Pristine FM26's RenderTable/AppendSelectionButtons, TableSelectionHandle and
TableContext.Many/range-selection path already expose these checkboxes and
Select all/Clear selection. No new helper or screen-specific exception is added.
The native Plantel button has enabledSelf=true, enabledInHierarchy=false under
SIEnabled Squad, matching disabled HTML. That establishes native availability;
no additional business-rule cause is asserted beyond the observed post-talk state.

1633 existing C# tests pass with no skips; Debug build has zero warnings/errors.
Runtime/installed hash and seven exact/four adapted central FM26 contracts pass.
The new TableContext/BindingRecords adaptation is separately inspected, not
included in the audit's existing exact-source count. Native set-piece taker
actions cannot be revisited from this current pre-match screen and are not newly
live-qualified; their specialized identity implementation itself is unchanged.
No half-time/post-match or manual NVDA qualification follows from this check.
Release/source package inspection receipts remain in current-verification.json;
Release is not installed or cold-qualified.

Tool observations: an initial test wrongly tried the native-disabled Plantel
button after four successful group selections; it was corrected without forcing
availability. An mcs diagnostic could not load the typed SIButton and was replaced
by the supported adapter/native-ancestry read. A temporary ignored JS script had
replacement-string syntax errors before execution; final syntax validation and
the completed live report are separate. These are not physical user actions.
Evidence includes teamtalk-native-record-before.txt, teamtalk-native-availability.txt,
teamtalk-live-check.json and the current normal Companion HTML, all ignored.

Final handoff observation: the normal Companion and actual native /gui now show
OverviewPrototype, AFS0–0FCP at3:06 paused. The bounded pre-match check completed
before that observation and executed no new talk/pitch-entry command. A final
receipt assertion expecting one pre-match table encountered eight match tables;
it was stopped and replaced by current-screen observation, not a navigation back.
The Windows tool reported input detected; this is a tool observation, not proof
of a physical user's action. No further input or match control is dispatched.
Do not claim the final handoff remains on TeamTalkPanel or infer match coverage.

## One pre-match team-selection title — F199

The user reports adjacent Escolher equipa / Escolher Equipa. Actual native
TeamSelectionTitleBar owns a readonly SIText/MainTabText, while the content
section provides the real FullScreenTeamSelection heading. Reuse the ordinary
profile hide pattern already used for SIText/MainTabText in PressConferenceCard;
scope it to TeamSelectionTitleBar and preserve the real h2. No translated-text
matching, generic caption deduplication, renderer, native state or client change.
The pristine FM26 header profile has no such hide; this is a bounded presentation
adaptation to the demonstrated native duplicate, as requested by the user.

Debug/tools=true29b88b93-5288-4315-9be7-f0842f429fee/game2676: the sole original
production client finds exactly one case-insensitive Escolher equipa text, as
the retained h2. Heading focus, actual FM Menu options/open/cancel/origin focus,
unchanged table, continued substitutes-strip omission and the untouched
Ir para o Jogo button pass.29 existing shipped-profile tests, build with zero
warnings/errors and runtime/installed hash plus seven exact/four adapted FM26
contracts pass. Normal Companion is restored; no match entry or manual NVDA.
Native screenshot and live ancestry corroborate the readonly header component.
Ignored prematch-title-check.json retains the scoped report.

Release candidate021103 passes compiled production/companion policy and package
inspection; SHA2565F92C3001E64C4DBC96907464C5DC16FC6F737941D807F406102D3884663A126.
It is not installed or cold-qualified. Source/evidence receipts remain local.
F198's timings and wider earlier cohorts keep their original runtime/scope.

## Pre-match duplicate substitutes and scoped latency investigation — F198

The user requests the same table-based substitutes presentation on the current
pre-match screen and asks whether Companion is malfunctioning. Native screenshot
and read-only ancestry confirm the same anonymous TacticalPlannerSubsBlock under
FullScreenTeamSelection. Extend F195's existing ElementBridge InternalChild
adapter to that demonstrated owner. No caption matching, table/control removal,
new renderer, cached game state, harvesting-budget or client change is required.
The original FullScreenTeamSelection profile remains unchanged.

Debug/tools=true1877bcc0-b2f9-40b8-b936-ed07c8d6c289/game2676 passes all four pitch
modes without the duplicate title/count/strip, retaining15 reserve table slots,
both pitches in Both and the original Panoramic. S1/S14 Name menus retain real
candidates and cancel. The table opens the native PlayerSelectionCard and closes
without confirmation; the original11-starter/14-reserve signature and tactic
remain unchanged. Normal Companion is restored visibly at Ir para o Jogo; match
entry is not activated.29 existing shipped-profile tests and the runtime/installed
hash plus seven exact/four adapted central FM26 contracts pass. No manual NVDA.

Actual normal Companion HTTP records show approximately2s composition before
this fix, with no continuing body requests while idle. Three fresh sole original
production-client refreshes after the fix deliver in1598/1838/1966ms and are
observed idle41/40/45ms later. Existing ComposeTiming identifies streamed table
harvesting as the largest portion (33rows,15columns; about1.3–1.5s). No body
refresh occurs during a further12s idle observation. Native tab changes can need
multiple bounded settling reads. No JS/request/CDP failure or stuck refreshing/
pending state was observed. These timings do not prove a WebView2-specific fault
or an engine-only cause, nor qualify all pages. No optimization was implemented;
the user's previous native/FM26 loading contracts remain intact.

An attempted temporary debug-port launch of the normal WebView was rejected
before execution by automatic approval review, with only `blocked by policy` as
its reason. It was not bypassed. Timing evidence therefore uses the original
production app.js in the sole headless Edge client, with the normal Companion
closed and restored in finally. Its own navigation log reports success. An
initial inherited test incorrectly counted native table slot dropdowns as strip
controls; verification now scopes that assertion outside real tables and uses
the actual Nome column. These are tool observations, not physical user actions.

Ignored prematch-subs-check.json and prematch-latency.json retain the scoped
evidence. Final Release/package inspection and local source receipts are recorded
in current-verification.json; Release is not installed or cold-qualified.

## Name the pre-match FM menu correctly — F197

The user's current screen is FullScreenTeamSelection, before AFS v FC Porto on
11 July2026 at16:00, with the actual Ir para o Jogo button. Its TeamSelectionTitleBar
SIDropdown had no own caption and inherited that adjacent match-entry caption.
Readonly ancestry identifies BindingVariables(FMMenuDropdownVariables) inside
TemplateContainer(FMMenuDropdown), owned by TeamSelectionTitleBar. Reuse the exact
existing OverviewTitleBar profile rename: type SIDropdown plus FMMenuDropdown
path → FM Menu. No dropdown extraction, option, state, action or client change.
The original FM26 profile/component architecture applies the name to both HTML
and speech; the genuine Ir para o Jogo button remains unchanged and unactivated.

Receipt-aware Debug/tools=true reload installs3f792462-a578-4209-b2ea-f5e0ea8a64be
on game2676 with zero warnings/errors. All29 existing shipped-profile tests pass;
installed/runtime hash and seven exact/four adapted central FM26 contracts match.
The sole original production client observes exactly one FM Menu combobox, zero
Ir para o Jogo comboboxes and one genuine Ir para o Jogo button. Its15 actual
native menu options open/cancel through keyboard/transport, exact menu focus
returns, the current table selection is unchanged and JS/request/CDP errors are
zero. No menu leaf, match entry, team submission, simulation or manual NVDA test.
The normal owned Companion is restored visibly; final PID/DOM evidence is in
ignored current-verification.json and fm-menu-label-check.json.

An initial harness expected /gui's first panel to be the body. After reload the
actual first panel is TeamSelectionTitleBar while FullScreenTeamSelection remains
the content; that false test assumption was corrected and excluded. It is not
attributed to physical user input or an actual screen-navigation failure.
Release candidate20261115-015244 passes compiled no-diagnostics/companion/package
checks, SHA25602AD26AB97C4E0759558B1AFCA17B91A83F554563296BDFDA099225C79756B64.
It is not installed or cold-qualified. No broader pre-match-table or in-match
qualification follows from this scoped menu-name correction.

## ClubSite transfer headers and news Alt+I observation — F196

The user reported PENÁLTIS in the club transfer-history table and asked whether
news retained FM26's Alt+I behavior. On unchangedb63f6f3a/game2676, actual ClubSite
DOM has that caption in the first column of both15-row arrivals and16-row
departures tables, over player names. Readonly ctxancestry confirms the header's
own native SIText is PENÁLTIS. A successful native game-window screenshot also
shows it in both transfer tables. This is an incoherent native caption, not an
extra penalty action invented by the Companion. ColumnHeaderAdapter.GetLabel
retains FM26's FindBestLabel contract. No caption replacement, sort command,
transfer action, game configuration edit or implementation change was made.

The headline's native description remains behind the original Alt+I contract;
short secondary content is inlined by unchanged FM26 DisplayText (85 characters).
Readonly describe reads525 characters for the current long news tile and
No additional information for its already-inlined small news row. Then the sole
original production client passes an actual keyboard check: focus the headline,
Alt+I twice, two real /describe→/speak acknowledgements with identical525-character
content, unchanged body/focus, followed by the short-row Alt+I acknowledgement.
No JS/request/CDP failures, native game command or manual NVDA claim. Normal
Companion is restored visibly on ClubSite; Confirmar Equipa remains untouched.
Evidence: ignored club-overview-current/native captures, club-transfer-header-probe,
club-news-alt-i-check.json and current-verification. No new whole-game or
all-news-variant qualification follows from this current-screen sample.

The first screenshot tool returned an image of the foreground Codex window while
identifying its target as the game. That image is excluded. After re-observing the
returned game window and activating only that target, the successful screenshot
shows the native ClubSite and its actual transfer captions. This is a tool
observation, not attributed physical user input.

## User-requested progression to the first match, stopped before entry

On unchanged Debug/tools=trueb63f6f3a/game2676, the user explicitly requested
advancing until the match-entry button became available, without activating it.
The men's FC Porto career advanced from6 July09:00 to11 July2026 at16:00,
AFS v FC Porto (friendly, away). The actual native CompetitionFixturesTool and
production Companion show enabled Confirmar Equipa. STOP HERE: that button was
not activated, the team was not submitted and the match was not entered. The
ordinary owned Companion is visibly restored (PID15620/watch2676). The next
match actions belong to the user's requested manual test, not this progression.

Progression accepted the existing club vision and proposed code of conduct,
completed the initial squad meeting, completed the mandatory six-step set-piece
wizard with its existing options, submitted the native automatic training-camp
squad and declined optional training/dynamics/recruitment introductions and the
first press conference. Intervening news was marked read through native controls.
No loan offer was accepted/rejected, and F115 was not investigated. A native
Delegate to Rui Correia confirmation concerned permanent B-team sale duties;
it was cancelled with No. No manual starting lineup/formation/player-instruction
edit or match-entry command was submitted; the wizard did generate native default
set-piece routines. No save command or runtime/source implementation change.

All automation used the original production client as the sole UI client while
the owned ordinary Companion was closed, then restored it. An initial helper
incorrectly waited for /activate after /message/open: the actual opened message
was observed and never replayed. One owned helper's stdin shutdown stalled;
only that exact helper was stopped, allowing its wrapper to restore the Companion.
Transient native simulation busy/compose timeouts were recovered through client
reattachment/reload after processing, without repeating game actions. These are
tool/runtime observations, not physical user actions or new manual NVDA evidence.
Evidence: ignored advance-match-stop.json, action JSONL and current-verification.
This does not qualify in-match behavior or change earlier F194/F195 test cohorts.

## Restore FM26 substitutes presentation — F195

The user demonstrated reserve selection through the table and requested FM26
presentation. Restore its exact two TacticalPlannerTool hide rules for the
duplicate strip/header/count/deep NoPlayer controls. Keep actual table dropdowns,
XIButton, player links, PI and role commands. The earlier empty-slot Name menu
showing "No Choices" did not prove that the table lacked reserve selection.
Before the change, the current S1 João Costa table dropdown exposes five actual
candidate players, All Players, Go to Player Selection and Remove from Position.
The current state has11 starters and14 reserves; it is no longer F194's empty
lineup. Do not overwrite the user's selections to reuse an older checkpoint.

Current explicit Debug/tools=trueb63f6f3a-0c1b-4146-82ac-585931e36a1a/game2676.
The native anonymous TacticalPlannerSubsBlock source also needs its bounded
shared-classifier adapter: the legacy named profile rules alone leave the
header and populated chips. Its exact template is omitted on both surfaces only
inside TacticalPlannerTool. Existing profile expectations are updated. Host/Navigation/
Speech remain untouched. live-tactics-substitutes.cjs checks all four pitch
modes, all15 table reserve slots, occupied first/last reserve dropdown options,
actual player-picker navigation/cancel, unchanged current lineup and original
Panoramic. Results and final normal Companion PID are retained in ignored
current-verification.json. F194's other fixes remain; no business choice,
simulation/save, manual NVDA or match-only qualification is claimed.

Final3 live groups pass onb63f6f3a with zero JS/request/CDP failures. S1/S14
native table menus retain candidate options; S1's actual Go to Player Selection
card opens/closes without confirmation. All four modes omit the duplicate strip
and retain15 table slots; the current11-starter/14-reserve signature and active
tactic remain unchanged. Final screen is original Panoramic, with the normal
sole Companion visibly restored. Pure1633C#/83JS and all deployment/profile/
player/retention/cleanup fixtures pass; installed/runtime hashes and seven exact/
four adapted central sources match. Release candidate20261115-010326 passes
compiled no-diagnostics/package checks, SHA256
55116275DC3384ED9D0865D4EA3C78C7B1D350F0AEDBF19499D4608A4490996B,
without installation/cold qualification. Source preparation remains local.

## Tactics selection, Both pitches and instruction tables — F194

The men's FC Porto TacticalPlannerTool audit restores actual Player Instructions
commands, position/substitute pickers, split role details and real player links.
Both now uses two original editable pitch widgets under their actual phase
headings, with independent native source/phase/tactic guards, menu/move scopes
and exact second-pitch origin focus. Panoramic retains FM26's original behavior
by explicit user steering. Actual readonly meters/footer stay informational.
Captioned role/position and binary preferred-foot artwork keep the official text
without technical duplicate names. Modern native TeamInstructions emits the
original table after complete list-count verification:19 IP rows (17 selects,
2 actual native compound open actions),10 OOP rows. Identity comes from actual
instruction IDs and full career/team tactic+slot context, not translated captions.

The complete9-group audit and5-group extra inventory pass on0e1287a6/game2676,
including50+7 selection rows, PI tabs/editors, role/formation-related controls,
native menus/pickers/cancellation, efficacy/history and real player profile/Back.
Three axe audits are clean; JS/request/CDP failures are zero. The final heading
order/native-disabled refinement passes3 focused groups on51ff64d7. After the
last captioned-position/preferred-foot correction, the current explicit Debug/
tools=true runtime is4f318bff-a94a-4c35-9ab6-d967037fb40e/game2676; its final
focused qualification is recorded separately in tests/web/tactics-final.json.
All4 final focused groups pass with zero JS/request/CDP failures; the normal
sole Companion is restored visibly on Both, with both native phase headings.
The9+5 earlier groups are not relabelled as this final module's full rerun.

Starting/current tactic is slot1,4-3-3 MD Com Extremos Positivo, Controlo da Posse
de Bola, Positiva: Diogo Costa in goal, ten outfield and fifteen substitute slots
empty. No business choice, simulation advance or save was submitted. PI's native
initial IP default also occurs from an OOP source; its own OOP tab works. No
automatic native-tab override was retained. Match-only profiles, committed move/
lineup/role/instruction persistence, unseen conditional states and manual NVDA
remain unqualified. Performance experiments stay reverted; F115 stays deferred.
Full implementation and evidence boundaries: [tactics audit](FM27_TACTICS_AUDIT.md).

Final pure checks pass1633C#/83JS and all deployment/profile/player/retention/
cleanup fixtures. The installed/runtime hash and seven exact/four reviewed
adapted central FM26 contracts pass. Release is built/inspected separately and
is not installed or cold-qualified. Current package receipts and Companion PID
are recorded in ignored current-verification.json; source preparation stays local.

## Global Search close incorrectly reported history timeout — F193

The user reported automatic NVDA reading of "Navigation did not reach another
history entry in time." after opening/closing global search. Reproduced on men's
FC Porto/game2676: actual GlobalSearchCard overlay closed through its generic Back
affordance, but ServerHandles.History waited for a different native history index
and returned that error after5056ms. Search dismissal keeps the original menu
history entry. This was a genuine distinction missed by the FM27 adaptation of
FM26's merged message/native history path, not a reason to mute real failures.

PlanHistoryMove now captures the actual foreground panel only when the existing
interactive set identifies that same Pointer as an overlay. It sends the original
GameMove once, preserving native availability/close policy. A separate bounded
AwaitForegroundClose observes that exact strongly held owner leaving both native
Open and Closing collections. It returns empty success on confirmed dismissal,
without requiring a history-index change, replaying Back, editing native history
or closing any subsequently opened panel. Real page navigation retains the
original AwaitHistoryLanding, message restoration and timeout error. Existing
toggle-owned dismissal and input/focus/history guards remain. No app.js or
permanent Host/Navigation/Speech changes; no performance experiments resumed.

Final explicit Debug/tools=true794af61b-3d5f-4583-9398-5c546cf14b41/game2676.
Direct native/sole-Companion route test returns empty success in488ms. Then the
normal Companion was temporarily closed while the same production app.js ran as
the sole live client in headless Edge. live-global-search-close.cjs passes three
functional groups: four Squad cycles via Back button, Escape, Alt+[ and repeated
Escape; a Portal-origin close; and real First Team/FC Porto page navigation with
native Back. Each search close preserves its native history index, returns an
empty reply and restores exact opener e794d51a1. Four timed client replies are
917/982/503/1033ms. No timeout error speech request, JS error, failed HTTP request
or CDP transport failure. Original normal Companion is restored as7532/watch2676
on Squad overview. Evidence: artifacts/search-close-reproduction.json,
search-close-fixed-http.json and tests/web/global-search-close.json. No manual
NVDA, all unseen overlays, new full11-message or Release cold qualification.

Build/reload has zero warnings/errors; installed/runtime identity and seven exact
central/four reviewed adapted FM26 source contracts pass. Pure1633C#/83JS and all
deployment/profile/player/retention/cleanup fixtures pass. Compiled Release
candidate20261114-224021 passes production/companion no-diagnostics and package
inspection; SHA2569CDE646B81A2D45A7B98C2A8F49219576E37731592DE9CF6034CAC58345E57C3.
It is not installed or cold/gameplay-qualified. Final normal-Companion observation
is Messages on the same794af61b runtime, after the earlier restored Squad page;
this state change is not attributed to physical user input and was not navigated
away to force another test. Current proof is retained in current-verification.json.

## Performance experiments reverted at the user's request

The user requested general loading optimization, then explicitly stopped that
work and requested the previous native/FM26 behavior. Reverted all six trial
implementation files to their exact F192 source in source-20261114-220348.zip:
NativeTreeQuery, ElementBridge, PairedEntityLink, CoreEntry, OriginalProfiles and
ContextMenuOpener. Removed the temporary TraversalTiming profiler and three trial
query/interface/timing verification helpers. F188-F192 corrections, original
Alt+I, native data/command/identity barriers, 50-row harvesting, paging/budgets,
transport and client code remain. No optimization remains in production source.

Receipt-aware reload restores the exact previous Debug module
7eae2261-64fc-45b5-87e9-e29234f9f0ba/game2676. Build has zero warnings/errors;
runtime/installed hash and seven exact central/four adapted FM26 source contracts
pass again. Ignored performance-rollback-source.json records restored file hashes.
The sole normal Companion remains4036. No Host/Speech/permanent install changed,
business leaf was submitted, or game time advanced. Existing Release215956 and
source220348 remain the pre-experiment implementation candidates, not new cold
qualification. No manual NVDA or new full regression is claimed for a byte-identical
rollback of the previously qualified implementation.

The exploratory timings varied substantially and did not establish a consistent
general arrival improvement. They do not prove that all delay is engine-only.
An AtIndex experiment threw Method unstripping failed and temporarily omitted
elements through the existing adapter failure path; it was corrected and excluded.
A source-restore iteration initially retained the previous binary through build
timestamps; its mislabeled run was excluded, then actual MVID/hash were verified.
These are tool/implementation observations, not attributed physical user actions.
All trial runtime changes are now removed. Do not resume performance work without
new user steering. F115 remains deferred.

## Main overview headings and missing fixture content — F192

The user reported that overview pages appeared incomplete, specifically the
senior squad lacking a heading. They explicitly clarified that Dynamics should
keep its original Alt+I description. No ordinary tile/description or speech
redesign was made. Compared the pristine FM26 table/button/calendar patterns,
actual native trees/bindings/screenshots and sole normal Companion reported DOM
across Portal, Squad, Tactics, Recruitment, Training and Club overviews.

Three demonstrated omissions were corrected through shared existing components:

- Native title-link-button frame headers now emit their actual primary-text as a
  live readonly h3, then retain the original ordinary button and full label. This
  covers First Team, Scout Reports, Training Schedule, Club Vision, Finance and
  Club Staff, plus the same Portal header components. No interactive headings.
- The old NavigableLink caption demotion assumed its image command remained.
  F186 paired-entity suppression could remove that image too, losing both. The
  shared LinkHasCommandButton now requires a genuinely bound, retained command
  through the existing hide policy. Named native links/menus and their actual
  record/context/stale checks remain; no caption equality deduplication was added.
- The inherited full-calendar collector gathered SIButtons only. FM27 readonly
  fixture events have opponent/competition/venue content without one. Retain that
  event through the normal walker in the same day/order. Its own team/competition
  badges are redundant only when the actual Event and adjacent caption references
  match; other images/help/menus remain. Training sessions retain their adapters.

A repeat visit also captured a calendar of empty day cells while actual event
rows were still binding. The native observation shows35 list shells with zero
bindings, later28 populated lists (the prior empty week becomes hidden). A
nonempty native event list with zero observed bindings, or an actual pending
binding, now emits the existing provisional marker. This reuses bounded recovery
without rearming retries; native empty days remain empty. The native month label
and recruitment counts/filter captions are already accessible dropdown names,
and long ordinary card descriptions remain available through Alt+I. Raw native
text-versus-innerText inventories are leads, not proof that those labels are lost.

Final explicit Debug/tools=true7eae2261-64fc-45b5-87e9-e29234f9f0ba/game2676,
men's FC Porto July6, normal Companion4036. Receipt reload and unchanged permanent
assembly checks pass. Six saved actual-Companion/native-census groups pass in
tests/companion/verify-overview-content.cjs: live native headings and unchanged
commands, no duplicate IDs/interactive headings/technical Icon fallback, all32
senior rows, retained Dynamics Alt+I and five calendar opponents in their own
dates. Native Agenda uses its existing explicit paging where required. Captures:
artifacts/overview-audit-final-*; report overview-audit-final-checks.json.
Read-only census: tests/companion/native-overview-text-census.cjs. Five live groups
also pass on final7eae: three Training entries deliver their sessions and five
actual calendar opponents (reported complete arrivals at2.79/2.68/2.37s); Agenda
completion; Club Brugge link reaches ClubSite/Club Brugge KV and native Back
returns; First Team header action reaches FC Porto senior squad and native Back
returns; Dynamics Alt+I and final Squad overview are retained. Evidence:
artifacts/overview-live-verification.json and associated actual DOM. These use
HTTP dispatch through actual reported ids; exact input focus/keyboard is not
qualified. The normal Companion remained the sole live UI client throughout.

Pure1633C#/83JS and all deployment/profile/player/retention/cleanup fixtures pass.
Compiled Release candidate20261114-215956 passes production policy and package
inspection (SHA256 E2F91ED6E3B06B9CEE5DEA3BF2060EE57FF789DD7C0C5D18355904BD86F126AE).
It is not installed or cold/gameplay-qualified. Initial capture iterations that
read the preceding/loading document were excluded or retained as partial evidence;
the final driver waits for the actual destination and calendar recovery. One
transient stale paging ID was refused as Unknown element and not replayed. Initial
PowerShell5 handshake/package-argument tool failures were corrected; they are
tool observations, not physical user actions. No new keyboard/manual NVDA, every
nested overview/conditional state, chart completeness or full-career qualification
is claimed. F115 remains deferred; no business edit/time advance was requested.

## Youth Setup table actions audit — F191

User reported possible missing promotion/table actions. On the unchanged explicit
Debug/tools=true d28a5d60/game2676 men's FC Porto career (July6), inspected native
SquadYouthSetupReport, screenshot, bindings and sole normal Companion DOM. FM26
TableContext.BuildCellModel/FindVisibleStops retains each actual visible control;
FM27 uses this ordinary table/adapter contract. The existing command-semantic
NativeButtonCommand fallback names EventFmxActionDevelopmentCentreAdvice as
Apply development advice. No new presentation or production command was needed.

Four actual report captures contain overview10, Sub-23 14, Sub-19 43 and B25 rows.
Overview's10 realized playeradvice records have IsSuggestionActionable=false,
IsSuggestionActioned=false and hidden actionbuttonvisible wrappers. Sub-19 already
renders enabled advice buttons for Abraham Obasi and Juan Camilo Valencia; B for
Mame Niang and Eirik Granaas. Each sits in its own ordinary action cell beside
the actual language-course suggestion. Team-tab flag probes are realized-window
samples (12/15/15 rows), not proof of every harvested record's native flags.

Opened João Afonso's actual name context menu, then Plantel. All7 native squad
entries are exposed, including enabled FC Porto, FC Porto Sub-23s and Sub-19 do
FC Porto. Native root has10 entries; the mod adds its own Close/Back affordances.
The senior move is therefore reachable via Shift+F10 → Plantel → FC Porto. Closed
the menu without executing a business leaf. No promotion/advice/language-course
commit or F115 loan investigation occurred. Saved-capture assertions pass in
artifacts/youth-actions-verification.json; reusable readonly probe is
tests/companion/native-youth-action-state.cjs. No manual NVDA, new keyboard-focus
or business-result qualification is claimed.

One first U23 capture observed SquadScreen and was excluded. A subsequent bounded
sequence observed SquadYouthSetupReport throughout and captured the selected U23
tab. Return-to-overview activation was acknowledged; the later final foreground
was Plantel, so final origin restoration is not qualified. These are observed
screen changes, not attributed physical user actions. No production change,
reload, permanent install, new Release build or package was necessary.

## Current message prose/link steering — F190

The user requested less clickable message text and FM26 source fidelity, then
clarified the exact scope: the introduction paragraph can be wholly readonly,
including person names; useful player/staff links remain in Important People.
The standalone club action extracted from the subject is unwanted. The pristine
FM26 ScreenWalker ordinary-stop branch does not decompose SIText into commands;
its readonly HtmlRenderer paragraph retains MessageBodyText/body focus. Its
ElementBridge.CreateTextAdapter promotes actual table-row links separately.
This is the baseline, with the user's explicit important-person-link requirement.

Final IsNarrativeText reads the existing MessagesToolBlockMessageContent ownership
and excludes actual native tables, StreamedObjectList entity rows and NavigableLink
components. ElementBridge uses the original TextAdapter for narrative links; the
walker retains a whole readonly paragraph; the heading renderer omits extracted
entity actions there. Real list/table links keep the existing SITextLink/ordinary
command adapters and all record/stale/context/menu guards. F188 headings/grouping,
message IDs, source focus and body/close/identity barriers remain. Other screens
and native conversation questions are outside this message ownership boundary.

Final Debug/tools=true Plugind28a5d60-d847-4c1d-91df-55f278c7464b/game2676,
normal Companion4036/watch-pid2676. Receipt reload/permanent hashes and7exact
FM26 central sources/4reviewed adapters pass. The actual final responsibilities
message was captured from the sole Companion: complete prose with correct native
spacing, no paragraph links, no extra subject-club action, plaintext sender,
three menu-capable real important-person links, both response actions and Close
(6buttons total). Six automated DOM checks pass against that actual capture:
artifacts/message-text-contract-after.html and message-text-contract-dom.json.
This is reported-DOM/component evidence, not new manual NVDA or link-destination
qualification. The person-link command implementation/IDs are unchanged.

The updated live-message-content-groups.cjs tests the final split and retains
three person profile/Back checks plus18table club links/actual Benfica return,
all11message bodies/metadata and source restoration. Its current invocation
stopped at the initial message7 precondition before any command: the observed
foreground had changed to the staff introduction popup3/6, then
TeamSquadReport. The final captured message remains valid; do not claim a new
full11message run or attribute that state change to physical user input. Current
gameplay was not navigated away to force the test. The earlier F188/F189 cohorts
retain their own explicit boundaries.

Development steering: an initial implementation made every ordinary message
SIText readonly, also removing the important-person links. The user corrected
that; it was fixed. A temporary club-record-only variant was then superseded by
the explicit request for wholly readonly narrative prose. Neither abandoned
variant remains. An initial reload/open preflight saw the old DOM mirror and
stopped before opening; the actual unique message row was then observed/opened.

Separate squad Actions question: actual ActionsDropdown/ActionsDropdownEnabled
has enabledInHierarchy=false with no checked players. A real Cláudio Ramos
checkbox selection enabled the native control and the companion button; the
inverse restored zero selections and disabled state. Native proof is
artifacts/squad-actions-verification.json (0/false →1/true →0/false). No menu
business option, Save or simulation was submitted, and no force-enable fix is
appropriate. Preserve this prerequisite. The normal Companion remains on the
observed squad screen, with the initial selection restored.

Pure1633C#/83JS and existing deployment/retention fixtures pass. Final Release
candidate20261114-211151 passes compiled no-diagnostics/IL/payload inspection;
ZIP SHA256 A13E01BBD381644EAFF40581BA00ADB0872AFF0DFE03B8869F725B5661231625.
Release remains uninstalled/not cold-qualified. F115 remains deferred; no rejected
save deletion retry, source publication or manual NVDA evidence.

## Current popup transition focus qualification — F189

The user reported "Mensagens, heading level2" after each tutorial advance and
requested no background focus in popups. Initial screenshot/tree/report showed
the actual SquadIntroductionDynamicCard at step4/7, men's FC Porto/game2676.
The final settled DOM was already popup-only; that did not disprove the report.
The real production client reproduced intermediate Navigation/Messages bodies
and actual focus calls to fmsec1-h. Native observation found the incoming Card
in PanelManager.Open with IsLoading=true, visible=false and active=false, while
the outgoing same-schema instance was in Closing, visible/active=true.

InteractivePanels now observes loading covering Card/ModalDialog entries directly
from the current native document/layer collections. Body composition waits across
Unity dispatches within its existing10s budget instead of rendering the blocked
parent. A truly slow opening returns ordinary loading content, not the parent's
controls or a successful no-op. Streamed composition rechecks an actual exclusive
foreground's Pointer set before registering/returning an older body. Dedicated
progress screens and non-covering HUDs retain their own rules. No native state
cache, forced panel, altered business value or replayed command. Client app.js
and its original focus, refresh, history and actual-close behavior are unchanged.

Final Debug/tools=true Plugin252d893e-f516-4704-9879-581be8956dcd/game2676.
Receipt-aware reload verifies unchanged permanent assemblies. Normal sole
Companion is reopened, watches2676 and shows the actual tutorial summary7/7.
The initial expanded driver reached all7native steps through10transitions on
a4ee7d3c: zero background bodies and zero background focus calls. It then stopped
because its assumed Previous action does not exist on native summary7. No
Finalize/Skip was activated; no unsupported inverse was attempted. The corrected
maintained driver avoids entering7when a reversible checkpoint is requested.

Final252d live-popup-transition-focus.cjs passes4groups/15body responses at the
already-open summary: neutral focus stays inside; actual Close releases the
underlying screen; the real holiday popup suppresses its background and Cancel
restores its exact navigator opener without taking a holiday; the squad message
reopens its retained summary7. These cohorts retain their distinct runtime
boundaries. Evidence: artifacts/tutorial-all-seven-observed.json and
artifacts/popup-transition-focus/report.json. Real keyboard/client/native testing
used a sole headless Edge production client while the normal Companion was closed;
there was no overlap or fixture game server. Normal WebView2 reported DOM is
verified separately after reopening. No manual NVDA/real-WebView keyboard claim.
Pure1633C#/83JS and loader/deployment/player/retention/workspace fixtures pass.
Final Release candidate20261114-205355 passes compiled no-diagnostics/IL/payload
inspection; ZIP SHA256
F9100168A129257AFFE6966524EBE416EB2AC9B50033F449BCDD5494A201C46F.
It is uninstalled and not cold-qualified. Normal Companion4036/watch-pid2676
reports only the actual native popup at7/7, focus fmsec0-h, no Messages h2.

Tool/development observations: CUA failed to expose the current window's tree and
returned an occluded unrelated screenshot; neither is popup layout evidence.
Temporary development-only client/native probes were removed from source before
final deployment. A client-only attempt to omit the optimistic action refresh
reduced the failure but still reproduced an in-flight background body, so it was
discarded. The actual native loading gate resolves that race while preserving
the original client. A shell preflight tried to read the cleared /rendered mirror
while no client was running and stopped before dispatch. The summary7 driver
assumption is a test observation, not a missing native command or a user action.
No Save, simulation, tutorial completion, F115 work or rejected save deletion.

## Current message grouping qualification — F188

The user reported confusing subject/person associations in the squad introduction
and requested shared correction. Actual screenshot/tree show independent native
topics and important-person sections, not a one-to-one topic/player association.
The shared native adapter restores live h3 captions and semantic lists, keeping
each person link and its own description in one li. Responsibilities and tactics
introductions reuse the same correction; one actual SIVisible purpose wrapper
is handled. All original text, order, handles, actions, menus and record guards
remain. Partial/unexpected person lists retain original walking/paging. Details:
[message contracts](FM26_MESSAGE_PARITY.md).

Active Debug/tools=true Plugin 3e097334-221b-4ffb-be80-6ac13f702e76, game2676,
normal sole Companion19800/watch-pid2676. Receipt-aware Plugin reload verifies
unchanged permanent assemblies. New live-message-content-groups.cjs passes five
groups: exact original squad text/action inventory; native section/list pairing;
three actual player profiles/native Back; all11message open/close and original
read-list metadata; restored Tasks filter/squad introduction. All three native
introduction variants have headings and grouped rows. No Save, simulation,
business action, auxiliary live client or manual NVDA. Full pure suite passes
1633C#/83JS plus loader/deployment/player/retention/workspace fixtures. Seven
central FM26 sources/four reviewed adapters and installed/runtime hashes pass.
Release compiles with zero errors/warnings. Candidate20261114-202515 passes
compiled no-diagnostics/IL/payload checks, ZIP SHA256
48D0A315C836919D9CEE3B19BB73F9E1728C18E7943416D55E7A3F3BB88537EA.
It is not installed or cold-qualified. No cold Release qualification inherited.

Tool observations: initial compile lacked a namespace import, corrected before
deployment. The new driver initially called ribbon /nav for Back; it received
Unknown navigation target without a native destination, then used the original
/history path. The first full walk caught the responsibilities SIVisible wrapper;
it was fixed immediately and the complete11message regression passed afterward.
An inline shell probe failed parsing before dispatch. Computer Use first returned
no accessibility tree, then refused key injection twice with user-input-detected
notices. A later capture paired stale inbox accessibility text with an unrelated
image; it is not corrected-layout evidence. These are tool observations, not
asserted physical user input. Frontend keyboard/axe are not qualified by this run.
F115 remains deferred; rejected save deletion is not retried.

## Current message parity qualification — F187

Latest user report: FM26 does not recreate the message table after the last unread
item; FM27 does. Follow the full FM26 message interaction rules. The actual sole
Companion reproduced Por Ler(0) with an empty tbody and four column headings.
Native screenshot/tree confirm a cached native list retaining read items; no
manual NVDA evidence. The shared FM26 empty-list rule is now applied after FM27's
render-time native re-read, preserving its provisional marker and late-row recovery.
No blanket table hiding, lost message data or invented filters are introduced.

Final explicit Debug/tools=true Plugin ef524091-4f6c-4fd2-8fcc-f18ed7abfea7,
game2676, men's FC Porto July6,2026. Normal Companion19800 watches2676, with its
reported Por Ler(0), zero tables and native PortalMessagesTool confirmed. Temporary
child-only CDP9222 is closed. Existing active receipt8d5b0c3c remains; only Plugin
changed, with permanent source/hash checks. No Save, simulation, message response,
gameplay command or publication. The original all-read state is restored.

The complete contract review is docs/FM26_MESSAGE_PARITY.md. Six exact source
contracts pass: FM.messages pump/focusBody/key/goLabel, FM.msgview Escape key and
HtmlRenderer.RenderSenderCell. Differences retain actual FM27 POST/screen/native-ID,
history, cached unread-row and newer-input focus guards. Native extraction remains
separate from the original semantic HTML and interaction contract.

Final live-message-fm26-parity.cjs passes7groups/11actual messages/3clean axe audits:
initial empty filter, repeated updates, real native Mark all unread, every actual
message/body, alternating Close/Escape, next-row and final-filter focus, no table
after the last close, later refreshes, all four filters, original All metadata,
a fresh document and the shared Portal inbox. The native all-read state is restored.
The existing inbox regression passes6checks/3clean axe audits: message133 full18rows,
three body-focus/close cycles, Benfica/Sporting native returns, neutral focus,
search and nested menu Back. Measured body focus1176.1–1941.9ms/completion1176.3–1942.6ms
while packaging also ran; this is not an absolute FM26-speed claim.

Pure checks pass1633C#/83JS plus loader/deployment/profile/player/retention/workspace
fixtures. Seven unchanged central FM26 sources/four reviewed native adapters and
active runtime/installed hash pass. Final Release candidate20261114-200020 passes
compiled no-diagnostics/IL/packaging checks; ZIP SHA256
1DE02635B686BAC3D8A3754E9615D2591D7C8B98D72D5861BDDAD809B8267033.
Release cold/gameplay/Steam qualification and the broader recorded limits remain.
F115 remains deferred/unresolved. Do not retry rejected saved-game deletion.

Tool/preflight observations: the first new driver expected an empty native menu
acknowledgment, but received its successful selected-label response. It stopped
before reading any item. The real Mark all read inverse restored the initial state
before the corrected full run; no uncertain native command was replayed. Contract
source comparison confirms only the already documented POST/focus adaptations
inside FM.messages. No tool interruption is attributed to physical user input.

Source is staged/packaged-checked locally and proof is consolidated before cleanup.
Generated build/test/package outputs are removed after verification; source/legal
notices, SDK10, FM26 reference and ownership receipts remain. Historical cohorts
below retain their original runtime/context boundaries.

## Previous entity presentation qualification — F186

Final explicit Debug/tools=true Plugin fb2b5c60-3b8f-4951-a9c1-883c35fa67ad,
game2676, men's FC Porto career July6,2026. Normal Companion21472 watches2676;
its reported Inbox/FC Porto and actual native PortalMessagesTool are confirmed.
Temporary child-only CDP9222 is closed. Active receipt:
artifacts/install/mod-8d5b0c3cb41d4acd8154e1d3aa373083.json. Permanent files were
cold reinstalled after the diagnostic crash; subsequent Plugin reloads passed
permanent-source/hash checks. No Save/simulation/business form submission issued.

Shared corrections, actual native evidence and all146destination observations
are in docs/FM27_ENTITY_PRESENTATION_AUDIT.md and migration notes. Same-record
crests/flags/portraits/competition images consolidate within bounded components;
Club/Team aliases require canonical ownership. Readonly single-record captions
use actual inline-fragment semantics, including late captioned-button variants.
All47clause rows retain truthful separate club/player controls. Finance omits
its generic decorative badge while category/compliance stay. Ghana country/team
controls have distinct proven destinations. Training slots and match results
are independent native entries and remain. No page-wide caption deduplication.

Two complete146destination sweeps were recorded, the later on dcac7fd0 with zero
JavaScript errors. Its remaining compact Portugal pair was proven to open the
same NationSite/NationReference167772322. The local frame differs from the larger
entity component. Final shared bounded flag matching fixes it without a caption
or screen allowlist. Eight affected player/club destinations were re-observed
on fb2b5c60 with zero adjacent/cell/media leads and zero JavaScript errors. This
full-sweep/final-follow-up version boundary is explicit, not flattened.

Final entity regression passes13checks/six clean axe audits: actual Portugal,
Diogo Costa, AFS, separate clause club/player and competition destinations; exact
source focus, neutral refresh, late report/Data Hub captions, Finance, Ghana's
distinct actions and the compact Portugal link/menu/return. Final actual inbox
suite passes six checks/three clean axe audits: NewsItem133 three full18row opens,
body focus913.5–1194.7ms, completion914.0–1194.9ms, exact Escape row return,
Benfica/Sporting profile returns, neutral refresh, native query equivalence,
message search and nested menu Back. No manual NVDA/absolute FM26-speed claim.

Pure checks pass1633C#/83JS and loader/deployment/profile/player/retention/workspace
fixtures. Active Plugin MVID/installed hash and seven exact FM26 central sources /
four reviewed native adapters pass. Final Release candidate20261114-191500 passes
compiled no-diagnostics/IL/packaging checks; ZIP SHA256
601185BFC3D1B1D33508AB681BB62A83953A04B6F55596E4F0D579EF7089BD63.
Release remains uninstalled/not cold-gameplay-qualified. Source remains local;
no GitHub remote/publication/commit is performed. Proof is consolidated in ignored
current-verification.json before generated files are cleaned. Source/legal notices,
SDK10, pristine FM26 reference and ownership receipts remain.

Development/tool observations remain explicit: a broad experimental binding scan
caused a temporary compose timeout and was replaced with bounded named resolution.
A Direct diagnostic crashed game26536. The actual managed dump stack identifies
EntityLinkProbe.Describe → BindingBag.Request.get_Value → Il2CppObjectPool.Get
→ il2cpp_object_get_class. Both Direct/VisualFunction payload reads now require the
actual BindingMethodKind; real Direct probes were repeated after restart. Production
excludes that probe. Initial follow-up caught/fixed the late button caption; the
complete sweep caught/fixed the compact flag. An inbox invocation from Facts failed
its initial screen precondition before any message action; native Alt+1 restored
the required starting screen and the complete final suite passed. These are not
physical user actions, ignored application failures or proof of native-only causes.

No unresolved redundant/decorative lead remains in observed content after triage
and the targeted corrections. Unseen conditional/match screens, every business
action/table page, final cold Release/gameplay, Steam and historical F047 cause
remain unqualified. F115 is deferred/unresolved by user steering. Do not retry the
rejected55save deletion; all55remain, latest saved-file write12:57:43UTC predates
this audit. Prior F182–F185 and other historical evidence below stays separate.

## Previous targeted qualification — F182–F185

Final active runtime: explicit Debug/tools=true Plugin
94cfbf58-1179-40cd-ae0c-95d41dd64d81/game26536, men's FC Porto career,
July6,2026. The production default remains Release/tools=false. Permanent
Host/Navigation/Speech were cold installed using receipt
artifacts/install/mod-19a6bf0786684f1697afdbdaa92b9f1a.json; later Plugin reloads
passed permanent-source/hash checks. No Save, simulation or business edit was
issued by this targeted continuation. Do not infer physical actions from tool
interruptions or unrelated observed career transitions.

Implemented and targeted-live-tested: short actual destination titles without
automatic first-control/image/whole-page reading; four ordinary native-backed
message filter buttons; typed native queries replacing expensive managed tree
walks; consumed exact history-focus handoffs; redundant paired club crest/name
actions consolidated through actual bound Club/Team-to-Club identities. Preserve
all confirmed-body barriers, native record/owner guards, original named text links,
context menus, table rows and separate heading/breadcrumb/biography contexts.
The native activation itself took1ms; measured preflight fell from668ms to52ms
in instrumented observations. Developer timing/probe code is absent from Release.

Final actual Companion regression passes three message133 open/Escape cycles,
all18club rows with one named menu-capable action each, real Benfica and Sporting
destinations/exact Back origins, neutral focus stability after blur, open native
search query equivalence, and an actual nested Tactics menu leaf round trip back
to Squad. Three axe audits are clean. Final run body-focus times719–1142ms and
complete18-row times1063–1745ms; an earlier run was913–1207ms for both. These are
actual current-game observations, not an absolute FM26 speed guarantee.
Native query order/count matches old full walks, including a selected calendar
fixture list and open search. The calendar's changed General tab/exact focus
survives its separate native history round trip (earlier5821 module; same native
query implementation). Final pure checks:1633C#/83JS plus all loader/deployment/
profile/player/retention/workspace fixtures. Test runner now clears an expected
rejected-build exit only after every successful fixture; failures still throw.

Alt+I was pressed in the actual Companion: native /describe→/speak succeeds and
focus stays on its originating bookmark helper. Preferences announces its short
title, without Close/button speech. Its normal explicit selection acknowledgement
remains. Native panel collections show Preferences replaces PortalMessagesTool
in the same card collection; Close exposes the actual underlying Tactics panel.
The companion follows that native lifecycle; no synthetic reopening is added.
Earlier What's New/main-menu complete-silence0b evidence is superseded by the
short-title policy, not inherited as final-policy qualification. No manual NVDA.

The user explicitly resumed Companion tests after the window tool interruption.
The tool still reported a stopped state; testing continued through Playwright
attached to the sole actual WebView2 (temporary child-only loopback CDP flag),
never a fixture or second browser. The same installed Companion was restored
normally as PID28100/watch26536, with the temporary argument removed and the
game untouched; its actual reported Inbox is available again. Original source/
HTML/interaction references remain .upstream/fm26.

Final Release candidate20261114-165704 passes compiled no-diagnostics/companion
policy checks; ZIP SHA256
20FF7C06E5202016F1B12492ABFD30BF55228925B8CCBCA5F9379CB534F0A89B.
It is not installed/cold-qualified in this active career. Preserve existing
F047 historical-cause, F115 user-deferred and broader conditional career/Steam/
cold-release limits. No stable-production or GitHub-publication claim.
Consolidate raw reports into ignored artifacts/current-verification.json before
cleaning generated outputs; retain receipts, SDK10, baseline and source/legal.
All55saves remain: deletion was automatically policy-rejected; do not retry it.

## Earlier continuation snapshot — superseded by the qualification above

Latest user steering: keep short FM26-style destination/title announcements,
including Alt+number; do not read the whole page or automatically read its first
button/image. The earlier broad background mute (Debug0b130c44) is superseded.
Current live Plugin dc09208d-04f7-41f9-ab71-6a72b291e1dd/game26536/companion23324
uses the existing NavigationSpeechMute scope to substitute the shared actual
native title only during automatic panel entry/return. Late automatic rescans/
cursor restoration remain quiet. Explicit native cursor, descriptions and match
speech are preserved. Host/Navigation/Speech are the matching cold Debug profile;
subsequent Plugin reloads used scripts/reload.ps1 and passed permanent hashes.

The user clarified that Messages' four filters (All/New/Tasks/Unread) should be
ordinary buttons. The existing native Unread tutorial/group identity selects
only that bar; AppendButton and native tab handles are reused with aria-pressed.
Other native tab bars keep the original FM26 TabBarHtml unchanged. All four
filters pass actual native selected-style/index versus sole-companion checks;
Unread's existing return marker remains. All six existing Alt-number command
paths pass short-title/no-control speech plus actual companion destination
commit checks. Evidence: artifacts/native-auto-speech/message-filter-buttons.json
and shortcut-title-speech.json. 1,633 pure tests and79JS pass; current production
candidate152610 passes compiled no-developer-tools/companion policy checks.
It is NOT installed in the active men's FC Porto career. No Save/simulation
was issued. Runtime observations changed from initial main menu to this men's
career; do not attribute transitions or tool interruptions to a physical user.

CURRENT WORK: the user next reports slow message opening, requiring FM26-speed
comparison. After fixing latency, audit companion screen-reader focus: unexpected
returns to the navigation menu. Preserve all native record/owner/stale checks,
the confirmed-body barrier, command coalescing and no-replay rules. First timing
on already-read message133: native /message/open reply455.9ms; actual companion
GET /body647.2ms and rendered report roughly1.9s after command start. This was
a protocol/SSE observation, not a physical click timing. Exact client click,
in-flight body, native extraction, body commit and focus timings are still needed.
The original FM26 MessageViewSurface/app.js implementations are local reference.
Additional latest user report: transfer-message tables can repeat the club name
as `BenficaBenfica`; keep the actual named club link and remove only demonstrated
redundant content belonging to that same club/source. Do not deduplicate arbitrary
equal captions, erase another club or remove informative badges/details blindly.
The club page can also announce three Sporting CP links; compare their actual
native source/destination/context before consolidating. The Windows control tool
reported an Escape interruption during window observation. Treat that as a tool
observation, not a confirmed physical user action; do not issue more Computer Use
input in this turn. Readonly native/server/source investigation can continue.

PENDING HOUSEKEEPING after those fixes: rewrite the F182 migration/known-issues/
coverage paragraphs (currently still describing superseded complete silence),
record the final title/filter results and scope; update AGENTS/current production
boundary; consolidate all new proof into artifacts/current-verification.json;
stage source and remove generated artifacts again. Keep all55saves: their earlier
deletion remains policy-rejected. F115 stays user-deferred. No GitHub publication.
No manual NVDA evidence or physical Alt-number keyboard evidence is claimed.

## Latest source override — production configuration

The user's BepInEx clarification requires source prepared for production, no
developer diagnostics in the player build, and loader/compiler binaries outside
Git. Project/solution/build/install defaults are now Release/tools=false. Debug
requires explicit developer opt-in; Release refuses it. Production strips probe,
eval/reload/log/DOM-report routes, startup self-tests, trace recorders, speech/log
history recording and symbols; normal accessibility/health/errors remain.
Companion WebView developer tools are disabled in production. Source/player
installers share console-off/Fatal-Error-Warning logging and separate restoration
receipts. lib/mcs.dll is local-only; Prism's exact licensed runtime stays included.

Current checks pass 1,628 C# cases and loader/deployment/production-profile/player/
retention/workspace fixtures. Release and explicit developer builds compile.
Packaged production metadata/IL checks confirm omitted diagnostics and compiled
companion policy. This source was NOT installed/reloaded into live Debug64fe;
Host/Speech changes require a closed-game install and cold qualification.
Current proof is consolidated in artifacts/current-verification.json before
generated outputs are removed again. No repository remote/publication is created.
F115 remains deferred by the user, not fixed or proven in an unmodded control.
The save-deletion rejection below remains in force.

## Latest user override — cleanup and source preparation

The user defers F115 for now, requests normal companion reopening, all local
saves removed, temporary project outputs cleaned and source prepared for GitHub.
Do not continue loan investigation during this preparation. Normal installed
companion PID11028 is manually reopened with --watch-pid24408; its actual
/rendered document shows Bernardo and controls. Mod/game remain live on64fe.
This supersedes the earlier companion-closed native comparison below.

Generated workspace cleanup completed:155outputs/2.35GiB removed. All1020eligible
source files and33installed files preserved their exact hashes during cleanup.
SDK10.0.403, pristine FM26 reference, Git, source/legal files and active install
receipt remain. All previous raw reports/packages/builds are intentionally removed;
consolidated evidence is artifacts/current-verification.json, with cleanup plan/
result alongside it. Historical raw artifact paths below are unavailable after
this explicitly requested cleanup. C#1628/JS79 passes are retained as prior-runtime
evidence; the cleanup's new own fixtures pass. No runtime source was changed.

IMPORTANT: automatic approval review rejected the requested55save deletion
command with `blocked by policy`. No save was removed;55still exist and config.xml
is unchanged. User authorization was explicit; do not ask routine permission or
retry through another wrapper/tool to bypass the rejection. This portion remains
blocked. See docs/REPOSITORY_PREPARATION.md. No GitHub publication was performed.

The remaining sections preserve the earlier audit state and evidence boundaries.

## Current request and non-negotiable scope

Correct F047 transport, F115 loan state and F181 tactical preview while preserving
FM26 Access 0.2.16 (4d38a0256f29b97376845ef59edfcd64d7f165ed) contracts. ALL further
real-game tests must use a men's career. Do not load the historical women's QA
files or inherit their action/keyboard evidence as current male qualification.
The complete original audit remains docs/requests/FM27_MASTER_PROMPT.pt-BR.txt.
Manual NVDA is waived; graphical appearance/clothing work is excluded. No routine
permission questions. Never hide an unresolved defect to claim stable production.

## Exact current runtime and career

Native game PID 24408, Debug Plugin 64feaca1-6c26-43e4-a927-a6397e48a14a.
Latest user steering requests a native-screen-only loan comparison with the mod
STILL LOADED. Verified native-owned companion PID 20984 was closed as requested;
do not restart or operate it during this comparison. Game PID 24408 remains alive.
Do not use HTTP/native command dispatch as a substitute for the requested direct
FM screen interaction. After the user's 'testa agora', a fresh returned native
window263014 capture showed the actual game and native mouse control succeeded.
The complete editor-before/menu-change/confirmation/editor/menu/inverse sequence
was exercised solely through the FM window with companion closed. It REPRODUCES:
editor remains unavailable while native menu selects available and player shows
Emp. Both the original unavailable menu tick and badge removal were confirmed
after its native inverse. No Save/simulation. All55saves are unchanged. Reports:
artifacts/male-native-screen-loan-comparison.json and
male-native-screen-save-preservation.json. Earlier activation failure is retained
in male-native-only-ui-attempt.json; no physical lock/user cause was inferred.
This excludes companion interaction/rendering and the HTTP command bridge from
the reproduction path; it is NOT an unmodded-game comparison, since mod remains
loaded. Do not declare the native engine solely responsible without that control.
The same full native-screen sequence also reproduces for Bernardo Lima, whose
editor had NOT been opened in the recorded tests. Its first editor open, Cancel,
native menu change/confirmation and reopened stale editor all occurred with
companion already closed. Native menu available/Emp badge and editor unavailable
disagree again. Its original unavailable status/menu tick and badge were restored.
Report: artifacts/male-native-screen-fresh-player-loan.json. This second control
does not depend on Trofim's pre-existing edit session. Mod still remains loaded.
Earlier normal companion ownership was parent/watch-pid 24408.
Actual executable uses the game's WindowsApps alias, not its configured XboxGames
alias. HEAD / only obtains authentication; read /rendered to inspect the real
committed companion document WHEN it is running. While companion remains closed,
/rendered is STALE and must not be treated as current native-screen state.
Never GET / or /body as an auxiliary UI client.
Host/Navigation/Speech remain unchanged. Active receipt:
artifacts/install/mod-488f42863fe640a6ab9c0a234fbfe45f.json (33 owned members).

Current career is FC Porto MEN, July 6, 2026, QA Portal Access. Native screen is
Bernardo Lima's profile, no open menu/modal. The existing
Controlo da Posse de Bola / 4-3-3 IP/OOP tactic is preserved. The
second-tactic regression was canceled, never confirmed. No simulation occurred.
The earlier Cláudio Ramos selection was undone. Trofim Melnichenko's native loan
status inverse restores unavailable in the running career; that inverse is NOT
saved. No menu, dropdown or creation draft remains open.

Two separate owned male files exist:
- FM27Access QA Male FC Porto 20261114.fm: original male initial checkpoint,
  preceding tactic creation and loan commands; preserved exactly.
- FM27Access QA Male Working 20261114.fm: newly created once for F115 persistence;
  contains the tactic and external available-loan branch, then loaded natively.
  It retains the failing menu-available/editor-unavailable case. Do not mistake
  its saved state for the later running-state inverse.

All 54 files preceding the new working checkpoint retain size, FULL timestamp
and SHA256. Total now 55 .fm files. Never overwrite the pre-existing native
'last save overwrite backup' file through ordinary overwriting saves. Use exact
owned checkpoints and preserve other saves. Evidence:
artifacts/male-current-final-preservation.json and male-loan-save-baseline.json.

## F181 correction and actual regression

A concrete FM27 difference was found: selected IP uses an UNNAMED SIText inside
BindingRemapper, whereas its empty branch and OOP keep selected-tactic-name.
The original FoldTacticWizardPreview required that name and left IP formation /
phase as separate paragraphs. It now accepts only the demonstrated unnamed
SIText under the same remapper/direct-caption/native-preview-widget structure.
The original LabelValueItem/LiveValues/phase:formation HTML is reused. No new
component, heading, fictitious position or caption allowlist was introduced.

Final 64fe regression live-male-preview-labels.cjs passes 3 groups / 7 actual
native/sole-production-DOM observations / 7 single native commands: both phases
match their own independent native SIText values, change and inverse retain
stable distinct spans, native Cancel preserves the existing active tactic.
No Confirm, Save or simulation in this suite. It does NOT claim keyboard, axe
or NVDA. Screenshot/native hierarchy were inspected. Static TacticData textures
remain the inherited textual formation representation: no player/Position record
exists in those entities, so never infer named positions from coordinates.
Broader all-formation/keyboard qualification remains partial.

## F047 transport follow-up

New live-transport-probe.cjs navigates only /health, performs HEAD authentication
and verifies current male club through /rendered. It never composes a second
game UI or submits native commands. Production companion remains running.

On preceding Debug3dab (unchanged transport source), all 16,000 Edge requests
pass: 2,000 fresh mitigated connections; 2,000 fresh unmitigated diagnostic
connections; 12,000 persistent requests on 47 connections, 11,954 reused. Zero
JS/request/CDP failures. The last two workloads ran concurrently. Reports:
artifacts/male-transport-fresh.json, male-transport-fresh-unmitigated.json and
male-transport-persistent.json. No Windows networking/security changes or native
command retries. Historical spontaneous pre-HTTP10060 was NOT reproduced and
its underlying cause remains unproved. Keep the current companion-local
TcpPortRandomizationWin mitigation and bounded persistent framing/no-replay.

## F115 remains an actual male failure

Trofim Melnichenko: open native transfer editor/Loan first, observe unavailable,
Cancel. Actual profile Actions > Transfer > Loan > Available, native confirmation
for the SAME player accepted once. Fresh native menu selects available, while
native editor, actual SIText and record remain unavailable (PropertyValue64,
CanUndo/CanRedo false). A separate Save As into the unique working file and
native Load of that exact file still produce menu available/editor unavailable.
The native inverse restores unavailable in memory and its menu flag was checked.

Evidence: artifacts/male-f115-reproduced.json, male-f115-save-reload.json,
male-loan-menu-before.html, male-loan-editor-before.html, male-loan-menu-after.html,
male-loan-editor-after.html, male-loan-native-after.txt,
male-loan-editor-native-events.txt, male-loan-editor-saved-reloaded.html,
male-loan-native-saved-reloaded.txt, male-loan-menu-saved-reloaded.html and
male-loan-menu-inverse-restored.html. This is NOT fixed. No HTML substitution,
provider reset, reordered events, private business value write or command replay.
The native Cancel still carries ClosePanel then EventFmxRevert; metadata/visible
bindings have not established a supported corrective session lifecycle. Initial
July youth rows provide potential feedback, not a loan-advice command: the
advice-after-editor branch still needs a real MEN'S native occurrence.

## Tool observations, not asserted physical input

Automatic approval review rejected launching a diagnostic companion (blocked by
policy); no bypass was attempted. Existing normal companion stayed running.
CUA previously could not activate it. All current results distinguish native
commands/actual reported DOM from frontend keyboard/focus qualification.
Interactive C# evaluator globals retained stale variable scope after reload;
read-only probes failed before Save. Wrapping each probe in one scoped C# block
fixed the diagnostic; only one native Save was submitted. A wizard preflight
failed to trim leading label whitespace before style selection, then succeeded
with the actual trimmed label. Neither observation was a game crash/user action.
Earlier removal of an empty retired FM27Access.Smoke directory was denied; it
contains no installed mod files. Do not retry rejected actions through wrappers.

## Source, verification and publication

Final C# pure suite: 1,628 pass / 0 fail / 0 skip, plus loader, deployment, player
and retention fixtures. JavaScript: 79 pass / 0 fail / 0 skip. Runtime source/hash
reuse audit passes 7 exact FM26 central sources and 4 reviewed adapters on64fe.
Logs: artifacts/male-final-pure-tests.log, male-preview-js-tests.log,
male-final-runtime-reuse.log and male-preview-final-guard-reload.log.

Source .gitignore excludes temporary/generated/game/save/credential files and
keeps the exact required supplied Prism DLL/legal notices. Package retention
keeps one source folder+ZIP and one player folder+ZIP only after replacement
validation. Receipt-aware staging cleanup retains active payload and one recovery
DLL. Earlier 755 staging/backups (13.16 GiB), 22 source and96 player outputs were
removed; subsequent reloads replace only their owned recovery backup.

An earlier artifacts-tree disappearance has no established cause. Its old private
reports are unavailable; historical coverage is not current qualification. The
active receipt was recovered by comparing all33 previously owned installed files
against the regenerated unchanged payload; logs/soundsettings remain independent.
See artifacts/receipt-recovery.json. Do not fabricate old hashes/evidence.

Current player/source candidates are regenerated after the final code/docs.
Consult artifacts/male-final-player-package.log and male-final-source-package.log
for exact retained names; candidate hashes are local, not a stability certificate.
They are NOT cold/public/clean-machine/Steam/full-career qualified. No GitHub
publication, Git remote or commits exist. F047 cause and F115 remain unresolved;
F181's phase-label correction has targeted native validation, not full-screen proof.

## Next actionable investigation

1. Keep the men's career and normal sole companion. Preserve current in-memory
   inverse versus failing saved working branch; never replay the loan command
   without an independently observed baseline and explicit scenario identity.
2. Investigate native transfer edit-session ownership/canonical read lifecycle
   from the actual SetTransferStatusReference and opening/closing callbacks.
   No supported correction is yet demonstrated. Do not repeat already-failed
   provider resets, event permutations or silent HTML overrides.
3. Exercise the advice-after-editor branch only when a genuine men's advice action
   is present; advancing the owned male branch is authorized, other saves protected.
4. Capture a spontaneous10060 if it recurs; passing current stress is not a kernel
   root-cause proof. Do not broaden tests by loading historical women's careers.
5. Complete real frontend keyboard/paging/focus and cold/public/match qualification
   when the supported driver is available. Do not present direct pager POST as a
   frontend paging pass or infer source focus from a direct native command.
6. Publish only after stability requirements are satisfied. Preserve complete
   FM26 labels/recent-save data/Alt+I and informational headings. Do not recreate
   duplicate nested visual CTA commands or infer actions from a tile title.
