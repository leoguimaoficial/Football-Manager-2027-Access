# Distribution, selected builds and publication

FM27 Access 0.1.0 is the first public release, explicitly authorized by the
maintainer on 2026-11-15. GitHub-hosted Windows runners build and verify the full
distribution. Publication promotes the selected original artifacts after
approval. The existing [known issues](KNOWN_ISSUES.md) and recorded qualification
limits remain in force. Local packaging never creates a release or tag.

## Build a complete candidate

```powershell
./scripts/package-distribution.ps1 -GamePath '<qualified local game root>' `
  -OutputPath artifacts/distribution-candidate
```

The command requires a compatible local game, the exact pinned loader and fresh
locally generated interop. Generated compiler references are also retained in an
explicitly authorized private repository for hosted builds; they never enter
public Git, source archives or player downloads. The five-project solution builds in Release with tools
disabled, and the existing binary inspector verifies production behavior. The
Companion is an embedded-resource C#/WebView2 app; there is no npm build to invent.
Its existing JavaScript tests remain separate from native game qualification.

The new package includes pristine redistributable BepInEx downloaded from its
locked origin/hash, never a copy of a developer's installed game directory.
The only source binary exception remains the exact licensed Prism dependency.
Licenses for the loader, its .NET runtime, the Companion runtime, Prism, WebView2
and other retained dependencies accompany the mod payload.

Assets are:

| Asset | Purpose |
| --- | --- |
| `FM27Access-Installer.exe` | Universal independent installer engine |
| `FM27Access-Installer.json` | Engine version, SHA-256, size and official asset URL |
| `FM27Access-vVERSION.zip` | Full manual payload including pinned loader and console-off defaults |
| `FM27Access-Manifest.json` | Versioned schema, package and per-file integrity/ownership contract |
| `FM27Access-vVERSION-source.zip` | Corresponding source and licensed Prism exception |
| `FM27Access-UserGuide.html` | Standalone offline multilingual guide |
| `build-info.json` | Run ID, source and private-input commits, reference identity, versions and production flags |
| `SHA256SUMS.txt` | Exact list of assets and checksums |

Local dirty builds are explicitly marked and cannot be promoted by the release
verifier. Official runs require a clean checkout and bind artifacts to both run
ID and commit SHA. No game fingerprints, local paths, install receipts, logs,
settings, saves, generated interop or proprietary game files enter source ZIPs.
The deployment profile retains the native hash/API evidence needed by the guard;
it contains no game binary.

## Automatic hosted builds

1. Push the reviewed source/workflows. Hosted Windows CI validates pull requests
   and `main`: game-independent tests, production profile, installer tests,
   Companion tests, multilingual guide/browser/axe checks and self-contained builds.
2. Trusted `main` builds read the exact private commit pinned in
   `packaging/build-references.json`, using the Actions secret
   `FM27_BUILD_REFERENCES_SSH_KEY`. This is a read-only deploy key restricted to
   `leoguimaoficial/FM27Access-Build-References`; no personal GitHub token is
   stored in the workflow. Pull requests and development branches cannot access
   this input through the distribution job.
3. The runner verifies the archive checksum, allowed paths, managed PE metadata,
   every DLL hash and public reference identity. It downloads the official loader
   from `packaging/loader.lock.json`, checks its hash, builds the mod and Companion
   and packages the universal installer. It needs no game installation,
   self-hosted runner or developer computer.
4. Create environment `fm27-release`, add required reviewer(s), disable
   administrator bypass and restrict deployments to `main`. The workflow checks
   required reviewers before it can reach publication. Verify bypass restrictions
   in GitHub settings; the consulted REST environment schema does not expose that
   switch, so the workflow does not pretend to validate it through a missing field.
5. Run the CI workflow on the exact intended `main` commit. Artifacts retain
   their run ID/SHA for 90 days, subject to repository retention limits.
   Preserve the GitHub Actions logs and qualification evidence before approval.

The private input, read-only secret and required-reviewer environment are
configured for this repository. New pushes to `main` automatically build full
distribution artifacts; building alone does not publish a version.
GitHub's [required reviewers documentation](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments)
describes the actual approval mechanism; no dynamic build picker is claimed.

## When Football Manager updates

BepInEx generates interop locally when a player starts the game. Those generated
DLLs are not shipped in the mod ZIP. The compiler also needs reference DLLs to
build the plugin, so GitHub reuses the pinned private copy between mod releases.

The normal developer build (`scripts/build.ps1 -GamePath ...`) checks the real
game fingerprints, current BepInEx generation state and reference hashes against
the cloud baseline. A change stops the build with this refresh command:

```powershell
./scripts/update-build-references.ps1 -GamePath '<updated game root>'
```

After BepInEx has regenerated current interop, the command exports only managed
references, confirms the repository is private, creates the checksum index,
commits and pushes the private bundle, then updates the public pin. It uses the
maintainer's existing Git Credential Manager authentication for this explicit
operation. An unchanged identity needs no upload. No background process or
scheduled task is installed. Test the game adaptation and commit the pin with it;
future hosted releases use that exact input automatically. GitHub cannot detect
an uninstalled game update or generate interop without the game. An API change
can require source adaptations and live regression; replacing compiler input
alone does not establish gameplay compatibility.

## Select and approve a release

Open **Publish selected verified build** on `main` and supply the successful CI
run ID, exact 40-character commit, version already built, channel and English
notes. The verifier checks the workflow identity, successful trusted main run,
nonexpired matching artifact, embedded run/commit/version and all hashes. It
refuses an existing tag or release. A prerelease version cannot be marked stable.

After validation, the publication job waits for `fm27-release` approval. It
downloads and verifies the **same original artifacts** again, creates the tag
at the selected SHA, creates a draft, uploads assets, then publishes. It never
rebuilds current main after approval. Incomplete upload leaves a draft for
inspection; it is not automatically published or silently overwritten on retry.
An expired artifact requires a new validated run at the original commit, as
described by [GitHub's artifact retention guidance](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts).

The first tag is `v0.1.0`, with title `FM27 Access v0.1.0`. Source updates keep
the mod version centralized in `Directory.Build.props` and record the release in
`CHANGELOG.md` when publication is authorized;
engine changes independently bump `installer/InstallerVersion.props`.

## Permanent links, engine updates and independent documentation

The permanent [installer download](https://github.com/leoguimaoficial/Football-Manager-2027-Access/releases/latest/download/FM27Access-Installer.exe)
follows the latest stable release. Every mod release must
attach the universal installer and engine metadata. Use the last validated
engine binary when its version has not changed; do not relabel the mod version
as an engine version.

The guide's maintained source is `docs/user/locales/`. Run
`node docs/user/build-guide.cjs` after editing it. The generated HTML contains
all languages, preserves section anchors during language changes, respects the
installer query/browser preference, and works locally without external assets.
The documentation workflow validates it independently of mod binaries. Merge
reviewed guide changes to `main` to update the canonical raw HTML consumed by
the installer; it downloads a newer copy and retains an offline cache.

This canonical-repository strategy works with both mutable and immutable
releases. Historical release HTML stays attached unchanged. It does not replace
protected assets: [GitHub immutable releases](https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases)
lock published assets and tags, while a separate canonical guide can evolve.

## Qualification before release

Release qualification is separate from the current package's local installation.
Run a clean-machine first installation and first-launch native generation,
update/downgrade/repair/removal/reinstall with real official assets, multiple
installations, game-running and permission cases, interruption/recovery,
WebView2 missing-runtime handling, and a real installer self-update/restart.
Qualify eligible Steam/Epic builds separately from discovery fixtures and Xbox
targeted evidence. Verify keyboard/focus, UI Automation names and state, progress
announcements, error-dialog details, language persistence, RTL, high contrast and
DPI. Manual NVDA testing remains outstanding and is not replaced by axe or unit
tests. Keep unresolved gameplay issues recorded rather than declaring release
readiness from a successful package build.
