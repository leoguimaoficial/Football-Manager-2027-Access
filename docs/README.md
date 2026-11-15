# Documentation

Project documentation uses English. Official game labels and translations remain
in the selected game language. Legal notices remain verbatim.

The [complete master audit request](requests/FM27_MASTER_PROMPT.pt-BR.txt) is
archived verbatim in the user's original Portuguese. It defines the standing
scope; [session progress](FM27_SESSION_PROGRESS.md) records its current execution.

- [Architecture](architecture.md): the original five-project layout and execution path.
- [FM26 component reference](FM26_ACCESS_REFERENCE.md): actual upstream HTML, keyboard, focus and integration entry points to consult before changing a component.
- [FM27 migration notes](fm27-migration.md): native differences, safety constraints and reuse contracts.
- [Testing](TESTING.md): meaningful checks and evidence boundaries.
- [Screen coverage](fm27-screen-coverage.md): observed squad/career menus, exercised actions and remaining walkthroughs.
- [Entity presentation audit](FM27_ENTITY_PRESENTATION_AUDIT.md): redundant native crest/name, flag/name, portrait/name and readonly caption cases, with distinct destinations preserved.
- [FM26 message contracts](FM26_MESSAGE_PARITY.md): message-list empty state, filters, metadata, body/close/focus rules and required FM27 native adaptations.
- [Tactics audit](FM27_TACTICS_AUDIT.md): native selection/role/instruction actions, substitute pickers, original FM26 Panoramic and separately scoped Both pitch grids.
- [Destination matrix](FM27_IMPLEMENTATION_MATRIX.md): each retained native UID/context and explicit evidence limits; full fields in the accompanying JSON.
- [Known issues](KNOWN_ISSUES.md): remaining validation and compatibility limits.
- [Production release qualification](PRODUCTION_RELEASE.md): player packaging, runtime/logging changes, current evidence and publication gates.
- [Repository preparation](REPOSITORY_PREPARATION.md): source exclusions, generated-file cleanup, retained dependencies and the blocked save-deletion request.
- [Native compatibility](NATIVE_COMPATIBILITY.md): required APIs, compatible Steam/Xbox variants, local interop freshness and remaining live qualification.
- [Transport stability](TRANSPORT_STABILITY.md): persistent HTTP framing, POST/no-replay policy, measured connection churn and timeout evidence.
- [Session progress](FM27_SESSION_PROGRESS.md): current audit state, evidence and exact continuation steps.
- [UI modification guide](UI%20modification%20guide.md): original FM26 profiles and HTML feature protocol.
- [Hot reload](hot-reload-plan.md): original permanent Host/reloadable core design.
- [Live game verification](claude-in-game-verification.md): original developer-driver reference.
- [Companion reference](reference/companion-app.md): client, keyboard, DOM updates and focus.
- [UI types](reference/UI%20elements%20and%20containers%20list.md): original widget classification.
- [Failure handling](reference/failure-handling.md): contextual failures and recovery.
- [Default keybinds](reference/default-keybinds.md): inherited keyboard contracts.

Guides marked as upstream references describe FM26. Their game-specific identifiers,
old build paths and coverage claims must be checked against the current FM27 source
and migration notes. They are not evidence of FM27 screen coverage.
