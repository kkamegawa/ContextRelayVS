# ContextRelayVS Marketplace and release flow

This repository now includes the repo-side assets needed to package and publish the VSIX without relying on deprecated tooling.

## Included assets

- `build/Invoke-PackageAudit.ps1` - fails the build when vulnerable or deprecated NuGet packages are detected
- `.github/workflows/release.yml` - builds a Release VSIX on tags or manual dispatch and can publish to the Visual Studio Marketplace
- `vs-publish.json` - Marketplace publish manifest consumed by `VsixPublisher.exe`
- `.github/dependabot.yml` - keeps NuGet packages and GitHub Actions dependencies current

## Prerequisites before the first public release

1. Complete the host validation checklist in [e2e_checklist.md](e2e_checklist.md), including `/rootsuffix Exp`.
2. Review `source.extension.vsixmanifest` metadata and bump the version.
3. Verify `vs-publish.json` matches the intended Marketplace listing.
4. Create a Visual Studio Marketplace publisher and generate a PAT for publishing.

## GitHub Actions usage

### Build a release artifact

- Push a tag like `v0.1.1`, or
- Run **Release** via **workflow_dispatch**

The workflow restores dependencies, runs the package audit, builds the Release configuration, runs Core tests, and uploads the generated VSIX as a workflow artifact.

### Publish to Marketplace

To publish from GitHub Actions:

1. Add the repository secret `VS_MARKETPLACE_PAT`.
2. Run the **Release** workflow manually with `publish_to_marketplace=true`.

The workflow locates `VsixPublisher.exe`, then calls it with:

- the generated VSIX payload
- `vs-publish.json`
- the Marketplace PAT

## Signing guidance

The repository intentionally does **not** use `VSIXSignTool`, because Microsoft documents it as deprecated.

If you require a signed VSIX before publication, use the current Microsoft guidance for **Sign CLI**:

- <https://learn.microsoft.com/visualstudio/extensibility/signing-vsix-packages?view=visualstudio>

Certificate management is external to this repository and should be handled with your organization's approved certificate storage/process.

## Remaining manual or external work

- Experimental Instance validation on supported Visual Studio versions
- Initial Marketplace publisher/PAT provisioning
- Any certificate acquisition and secure-signing setup
- The separate VS Code shared-store migration PR
