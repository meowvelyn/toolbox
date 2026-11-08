# Avatar Toolbox

Editor tools for configuring VRChat avatars, animators, menus, textures, and screenshots.

## Package source

The Unity/VPM package is in [`Packages/net.lynuwu.toolbox`](Packages/net.lynuwu.toolbox).
See its [documentation](Packages/net.lynuwu.toolbox/README.md) for usage and examples.

For local development in an existing VRChat avatar project, add that package
directory through VCC's local package settings. To copy it manually, copy only
`Packages/net.lynuwu.toolbox` into the avatar project's `Packages` directory.

For published installs, use the repository URL from the
[Cloudflare-hosted VPM listing](https://github.com/meowvelyn/vpm) under
**VCC Settings → Packages → Add Repository**, then add **Avatar Toolbox**.

## Publishing

1. Update `version` and its matching release ZIP `url` in
   `Packages/net.lynuwu.toolbox/package.json`.
2. Commit and push to `main`. The release workflow publishes that version.

You can also run the release workflow manually or push a matching `vX.Y.Z` tag.
Existing releases are kept unchanged. Preserve the author attribution in the
package manifest.

To verify the current package locally:

```powershell
python scripts/package.py --tag v0.5.0
```

The builder includes every file in the package directory and writes the ZIP to
`dist/`, with `package.json` at the ZIP root. Repository tooling stays outside
the package directory. Unity `.meta` files and sample assets retain their GUIDs.
Requires Python 3.12 or newer.

After publishing a release, trigger a fresh build of `meowvelyn-vpm` in
Cloudflare to refresh the listing.
