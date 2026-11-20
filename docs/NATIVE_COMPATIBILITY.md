# Native compatibility contract

## 0.2.0 preparation — F214

The authorized0.2.0 release incorporates the Steam27.1 correction documented
below. The private compiler-input archive is now refreshed and pinned to
03eba84ebe1db8fe2a1d2f7358ae77b6a3e479f6, identity
A69907E2956FEB792E64D5C9940D1099B11AC350E5139E117DD5C106F6635FFC,
for actual Steam26620374 inputs. Earlier0.1.1 assets/tag remain unchanged.
The historical source-only statements below describe their own qualification;
new hosted/native/public verification is recorded in the F214 release evidence.

## Steam 27.1 update: actual member dependencies — F209, 2026-11-19

This is an unreleased source correction; public 0.1.1 assets and the private
compiler-reference pin remain unchanged. Actual Steam build26620374/game
27.1.0.2505117 retains x64/Unity6000.3.18f1/metadata39. Its locally regenerated
interop is fresh, but the installed published contract refuses36 types. Metadata
inspection identifies removed/renumbered generated callbacks and unused game
implementation members among the failures. The complete unchanged production
source compiles against the new interop, so replacing a build-number allowlist
would not address this failure: the old required-member set was too broad.

Capture now reads exact method/field MemberRef signatures from all four compiled
mod modules, including generic TypeSpec parents and inherited uses. Only native
references participate. An identical signature used elsewhere conservatively
retains that member; unused overloads and unrelated generated callbacks are
omitted. Literal reflection names are collected by decoding IL instruction
boundaries, including property/event accessor names. HarmonyPatch attribute
blobs contribute their method names and serialized target types, including
types used only in those attributes. No game assemblies are loaded to do this.

Type identity, base/layout/generic constraints, implemented interfaces, full
interface contracts and enum constants remain required. Used members retain
their exact signature, attributes, constraints, field offset and constant value.
The current payload has458types/5,179members instead of24,535members. Both the
installer and Host retain the same existing JSON contract and managed validation;
loader/proxy, known mixed-pair, metadata-only change and interop-freshness guards
are unchanged. Older full contracts retain their original validation behavior.

The tests prove that unused internals/overloads can disappear while used methods,
generic methods/fields, reflection/patch targets, enum values and interface
methods cannot silently disappear or change. Capture remains conservative about
literal reflection names; arbitrary dynamically constructed reflection targets
need review. A matching API does not prove native semantics or every screen.
Future updates with the same required contracts can start after normal local
BepInEx regeneration without a mod rebuild. Arbitrary Unity/metadata versions or
genuine required API changes are not promised compatible. Current cold/live
qualification is recorded in [testing](TESTING.md) and session progress.

The native pair for this inspection is GameAssembly SHA256
`3F4A26C4869D6429D2E73C276ADC64BDFAF6F3C86CE44FE0DE93D14683A5D7E9`, metadata
`D6D7158370979A67DBC94DB87CCFD7D624BB6D9EA7FBA87B929EBD250AB02069`.

## Historical published baseline

Final published0.1.1 uses source22dfbddd6f1c54433c8e513a709fae2c33c8fbf1/hosted43772243508.
Its exact required API contract validates against both preserved Xbox0.9.2435.0
and actual Steam references. The final259-file receipt installation passes native
preflight and cold Steam startup with version0.1.1/tools=false. Scoped native men's
table regressions also pass; a new live Xbox launch remains pending. Host's plugin
attribute and health now share a compile-time constant generated from central
Version, with independent assembly-metadata parity coverage. See testing/session
progress for the complete final artifact and public-download evidence.

## Xbox refusal corrected for 0.1.1 / installer 1.0.4

The maintainer supplied an actual Xbox0.9.2435.0 log refusing
`UnityEngine.CoreModule::UnityEngine.Application (1)`. Against the preserved real
Xbox reference archive at private commit0b7686c, the installed0.1.0 API contract
reproduces that exact sole error. Steam's generated Application adds public
RequestAdvertisingIdentifierAsync; Xbox omits it. The mod never calls it.

Application is an explicitly audited direct-only static API in this source:
runInBackground and isFocused. Capture their actual binary member references,
including exact signatures/attributes, while preserving the type header/base
and every other type's conservative public/reflection contract. New cases prove
that omitting unused advertising passes, but changing the actually used focus
signature still fails. No version label, native hash, interop-freshness or loader
guard is bypassed. The new458-type/24,535-member contract validates against the
actual Steam and Xbox references; the full production source compiles against
both with zero errors/warnings. Xbox references are compiler inputs, never a
fabricated running Xbox installation or fresh live qualification.

The universal ZIP retains one canonical byte-identical Doorstop `version.dll`.
Manifest/native-profile `by-store` policy resolves it to `winhttp.dll` for actual
Xbox installations and `version.dll` otherwise. Engine1.0.4 maps archive paths
to installed paths, records exact installed ownership and migrates only the
unchanged owned alternative through the existing rollback. Runtime preflight
resolves the same path/hash and refuses a second proxy. Legacy manifests/profiles
retain their existing exact-name behavior. Manual copies must use the selected
store name; receipt-managed Install ZIP is preferred. The reported log already
reached Host, so that specific refusal was the API guard rather than proof of a
Doorstop injection failure.

Local validation:1,648 pure cases,64 installer cases,89 Companion JavaScript
cases, eight distribution/manual contracts and all20 guide browser editions
pass. Hosted exact-artifact preparation, Steam cold qualification and publication
are recorded separately as they complete. A new Xbox live launch is unavailable
after the maintainer uninstalled that game. Keep that boundary and existing
career/match/NVDA limitations; do not claim all future builds are compatible.

## Steam production startup — F206, 2026-11-15

Steam build 26577676 is now installed and is the primary adaptation target.
It uses x64, Unity 6000.3.18f1 and metadata 39, matching the Xbox engine/metadata
family. Its actual native pair differs: GameAssembly SHA-256
`1152DF80DE0AE35BFF4945CB9196C58E5EC5B53DE2138A006C0597522B558303`, metadata
`774DA150861300AA49FFC462F40B7798A82DA1AA664B647BABF32FAAB649D0ED`.
The pinned BepInEx 793 generated 179 local interop assemblies. The published
0.1.0 required-API guard accepts them, and the complete source compiles against
them. The private compiler-reference pin now records this inspected Steam input.

The original WinHTTP proxy path stalls at the logos, even with Doorstop disabled
and no CoreCLR loaded. A noninvasive sample places the main thread at
`game_plugin!WaitForActivation`; that name alone does not establish a licensing
cause. Removing only the receipt-owned proxy lets the unmodified game reach
MainMenu. No native game, platform activation or authentication code was patched.

The same pinned Doorstop bytes loaded as `version.dll` start the original Release
0.1.0 core412b1c64 successfully. Actual MainMenu, What's New, Preferences and Load
dialog open/close actions pass through the sole production-document client with
exact origin focus and four clean WCAG-tagged axe audits. No career/save is loaded.
The [Doorstop documentation](https://github.com/NeighTools/UnityDoorstop/wiki) and
[BepInEx troubleshooting](https://docs.bepinex.dev/master/articles/user_guide/troubleshooting.html)
explicitly support this proxy rename. The observed correction concerns the loader
path, not a different Unity version or missing patcher plugins.

Installer engine1.0.1 migrates only an unchanged, receipt-owned old proxy with
the identical incoming fingerprint. It refuses shared/modified/conflicting proxies,
keeps the existing transaction rollback, and updates the exact native compatibility
profile. The actual installed 0.1.0 repair passes receipt/native-guard validation;
corrected candidate coref5e6cb73 cold-starts on Steam and passes the same native
actions/focus/four-audit suite. The exact hosted/public core `12107ea9` subsequently
repeats that cold startup and suite; all public assets match build43745356921.
The normal sole Companion is restored on MainMenu. Future store
variants still require API checks and scoped live qualification. Earlier runtime/
package statements below are historical, not the current Steam process.

## Archived pre-Steam qualification records

Current runtime is cold Debugfe50e602-762b-4ad6-a4be-92e3adce44a0/game29712 after F175. The previous player071512/78c package was receipt-uninstalled. Current1628pure cases and loader/profile/player fixtures pass without skips, including compatible Steam/build-label acceptance and seven incompatible/mixed-pair refusals. Actual Steam gameplay is still unavailable. This update changes option availability handling and does not replace API-based native compatibility with a build-number allowlist. Older candidate counts/status below are historical.

Current installed candidate 20261114-071512 uses Release Plugin
`78c27acf-9605-4d0d-8b4f-a1808faa122e` and an API manifest with 455 types /
24,588 members. Compilation, production binary/API checks, 1620 pure tests and
loader/deployment/player fixtures pass without skips. Its own normal Xbox cold
launch, production endpoint refusal, protected-career loading and native
53-save browser/paging checks pass. Current shared and whole-navigator regression
is still running. These results apply to the installed Xbox native pair; Steam
remains structurally supported by the managed API checks and fixtures, without
an actual Steam gameplay run. Earlier candidate counts below are historical.

Current source candidate20261114-032608 / Releaseed981e3d is built with the managed-API compatibility preflight and self-contained .NET10.0.15. Binary checks confirm developerTools=false and no compiler/private inspection types.1594pure checks,68JavaScript checks and loader/player/API fixtures pass0skips, including compatible build/store-label acceptance and incompatible/mixed-pair refusal. Cold/live qualification of this exact candidate is pending; prior13ae results are retained historical scope. Steam remains structural fixture/API coverage without an actual Steam game run.

The user's November 12 direction requires compatible game updates and Steam/Xbox
variants to run without a build-number or store allowlist. Actual Steam gameplay
remains unavailable. This policy supersedes older exact-native-hash acceptance
instructions; historical qualified build results remain scoped to their builds.

## Acceptance before mod initialization

The schema-3 player package and Host use the same managed preflight. It reads PE
metadata without loading game assemblies or calling Unity. The receipt-owned
native-build.json schema2 points to required-api.json. The reference native pair
is retained as provenance, not a general version gate.

1. Identify Football Manager 27 from native app.info; discover Xbox/Steam labels
   without fixed installation paths. Require x64, Unity6000.3.x and metadata39.
2. Refuse known mixed native/metadata pairs and verify exact pinned loader files.
3. Verify locally generated interop against BepInEx793's actual cache checksum.
   Its checksum omits metadata; a metadata-only change against the deployment
   reference is refused for explicit native-pair inspection/regeneration.
4. Check the required managed API captured from compiled Host, Plugin, Navigation
   and Speech references, including transitive base types. The current contract
   has451types/24503member requirements in candidate084525. It compares type identity, base/layout,
   generic constraints, interfaces, nonprivate method/field signatures and enum
   constants. Generated assembly versions, MVIDs, tokens and timestamps do not
   determine acceptance. Additional members are allowed; missing/changed required
   members are rejected with a contextual error.

This conservatively captures public/protected contracts of referenced types,
including potential reflection/patch targets. It cannot establish native method
semantics or full career behavior. New builds still need scoped live regression.
The normal BepInEx config-based interop directory is supported; custom command-line
loader path overrides are not qualified. Loader failures before Host entry remain
outside this guard. No platform activation, authentication or licensing is bypassed.

## Evidence

November13 candidate084525 retains451types/24503members. Current1565pure C# tests,
61JavaScript tests and loader/deployment/player contracts pass/no skips. Candidate
binary checks pass with developerTools=false and no evaluator/mcs. This candidate
passes its own initial cold live qualification5groups/2axe on actual Xbox
Releasef18ea741/game4840 (career-current-release-f18-cold-passed.json). Its full
navigator/shared regression is still running/pending; Debugbdfd career/club results
do not automatically qualify another module/native build. Steam remains fixture/API
compatibility coverage only.

November13 candidate043615 captures451types/24503member requirements.1544pure
C# tests and52companion JavaScript tests pass with loader/deployment/player
contracts. Exact cold package5groups/2axe and actual extended offer edits/Undo
5groups/9axe pass on Xbox0.9.2435/Releasee97752c9/game2992. These targeted results
do not qualify actual Steam, a different native build or complete-career behavior.

1519pure C# tests and44JavaScript tests pass without skips in the current source. Twenty compatibility
cases cover independent emitted
managed APIs and synthetic Xbox/Steam installations: changed labels/native bytes,
additive APIs and assembly-version differences pass; required API, enum/layout,
loader, Unity, malformed profiles, invalid PE headers and stale-cache differences fail. These are fixtures, not Steam
runtime evidence. Deployment-profile tests cover3eligible variants/7refusals.
SDK-free package validation passed against the actual current Xbox installation.
Cold startup logged compatibility acceptance before core mounting. Latest live
regression results are recorded in SESSION_PROGRESS and TESTING.

The pinned checksum algorithm was inspected in the installed loader and checked
against its actual current cache. Upstream references:
[BepInEx interop manager](https://github.com/BepInEx/BepInEx/blob/master/Runtimes/Unity/BepInEx.Unity.IL2CPP/Il2CppInteropManager.cs)
and [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop/).

Legacy schema-2 packages retain their historical exact native acceptance rules.
Use a newly generated schema-3 package for this compatibility behavior. Do not
copy generated interop or proprietary game files between installations.
