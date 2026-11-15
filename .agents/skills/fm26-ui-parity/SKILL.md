---
name: fm26-ui-parity
description: Investigate, port, fix or audit FM27 Access interfaces and actions while preserving FM26 Access HTML and interaction contracts with real FM27 data and commands.
---

# FM26 to FM27 UI parity

Use for screen, menu, settings, widget, action and accessibility work. FM27 determines
actual content, state and commands; FM26 determines reusable HTML and accessible UX.

1. Read [maintenance instructions](../../../AGENTS.md), the
   [UI guide](../../../docs/UI%20modification%20guide.md), applicable
   [references](../../../docs/README.md) and [migration notes](../../../docs/fm27-migration.md).
   Inspect the pristine .upstream/fm26 baseline at commit
   4d38a0256f29b97376845ef59edfcd64d7f165ed. Preserve GPL and attribution.
2. Open the equivalent profile, walker, renderer, adapter, registration, action,
   JavaScript and tests before editing. Use node scripts/compare-fm26-source.cjs
   when useful; source equality does not establish live screen coverage.
3. Inspect actual FM27 hierarchy, bindings, events and layout. A valid screenshot
   is layout evidence; a failed capture is not. Native calls stay on the Unity
   thread. Record demonstrated differences in the English migration notes.
4. Prefer exact reuse, then a native adapter, an extension of the original pattern,
   and a new component only if required. Preserve semantic HTML, keyboard, focus,
   updates and general tables of 50 rows harvested across frames.
5. Distinguish legitimately native-disabled controls from broken mappings, changed
   APIs, reusable FM26 behavior and removed features. Fix mapping/API failures;
   never conceal them with disabled, placeholders, invented strings or no-ops.
6. Validate DOM → event → transport → dispatcher → native command → observed result
   → DOM. Reuse ButtonAdapter/ServerHandles for ordinary buttons and preserve
   value/destination confirmations. Dispatch acknowledgment alone is not success.
7. Verify native state, value/selection, apply/cancel, reopening, persistence,
   origin focus, keyboard and language as applicable. Run relevant existing tests.
   Distinguish axe from actual NVDA. Do not reset/authenticate/choose privacy to test.
8. Record sources, native differences, verification and pending checks in the
   English migration/testing/known-issues docs. Promote shared fixes for reuse.

The sole runtime is permanent Host → reloadable Plugin. Do not restore retired
bootstrap readers or fixture UI. Compare IL2CPP objects by Pointer. Metadata 39:
never intercept OnPanelLoadedInternal (including its original-call trampoline),
read Layer.Name, receive Layer in a Harmony Postfix or use
TryGetLayerInfoForPanel(..., out LayerInfo). Use the common document/layer catalog.
Observe Panel-only opened events on the following Unity frame; never force
tutorial-manager closure during construction. Use the actual tutorial controls.
Deploy using receipts; permanent assemblies require a restart. Never commit game
data, interop, dumps, saves or secrets.
