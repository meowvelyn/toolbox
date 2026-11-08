# Avatar Toolbox

## Install with VRChat Creator Companion

Use the repository URL from the [VPM listing repository](https://github.com/meowvelyn/vpm)
under **Settings → Packages → Add Repository**, then add **Avatar Toolbox**
to your avatar project. The listing is hosted on Cloudflare Workers with a custom domain.

## Package

This VPM package combines the Avatar template’s 13 editor scripts with their
existing menu commands. The package targets Unity 2022.3.22f1 and declares
the VRChat Avatars SDK dependency in `package.json`. NDMF is optional.
With NDMF installed, the screenshotter can process avatar clones before capture.
Without NDMF, it captures the model directly. The optional integration targets
NDMF 1.14.8 and compatible versions.

For a local install, add this directory as a local package in VCC or place
it in the project's `Packages/net.lynuwu.toolbox` directory.
Import **Avatar Toolbox Examples** from the package's Samples section to
work with editable copies of the sample assets.

When migrating an older Avatar template, replace the old Toolbox package
and remove the old script copies and their `.meta` files from
`Assets/!!! Evelyn/Editor`. Keep the surrounding assets.

Generated outfit clips and submenu assets use unique paths. Regenerating an
Int Layer with its existing expression submenu updates the generated toggle
values to match the current clip order.

The example controller is completely assembled with the provided tools.

**Note:** Ensure that "Write defaults" are enabled; otherwise, the tools will not work.

## Create Int Layer

Animations added in the Create Int Layer are arranged in order. 
Element 0 has an int value of 1, and so on. Please keep this in mind.

## Create Bool Layers

For each animation in the Create Bool Layers, one layer is created. The layer and parameter will have the same name as the animation.

## Subfolder Naming for the Parts Folder

The subfolder naming scheme for the Parts folder should follow the format `#.*`. Please use the same format for your own projects.

### Example of Valid Subfolder Names:
0. default
1. Folder two
2. Folder3
...
255. folder.name

The number in the name of the subfolder is equivalent to the Int value set for the transition to the selected animation(s) in the subfolder.
Index 0 is the default outfit and has no Enable toggle.
**Important:** VRChat does not allow an Int value to exceed 255.

## Publishing a release

The package lives at the root of [meowvelyn/toolbox](https://github.com/meowvelyn/toolbox).
Keep the original author attribution in `package.json`.

1. Update `version` and the matching release ZIP `url` in `package.json`.
2. Commit and push the package changes to `main`.
3. The release workflow publishes the new version automatically when `package.json` changes on `main`.

For the initial release:

```powershell
python scripts/package.py --tag v0.5.0
git tag v0.5.0
git push origin v0.5.0
```

You can also publish by pushing a matching `vX.Y.Z` tag or manually running the
release workflow. Existing releases are kept unchanged.

GitHub Actions verifies the version and publishes a ZIP with `package.json`
at its root, preserving Unity `.meta` files and sample assets. The separate
[VPM repository](https://github.com/meowvelyn/vpm) picks up the release on its
next scheduled run; run its listing workflow manually for an immediate update.

Local builds are written to `dist/` and need Python 3.12 or newer.
