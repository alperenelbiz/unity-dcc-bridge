# Releasing

Consumers pin a tag in their Package Manager URL, so a release *is* a git tag. Anyone on an
older tag keeps working until they choose to move.

## Versioning

[Semantic versioning](https://semver.org). For a Unity package that means:

| Change | Bump |
| --- | --- |
| Removing or renaming a public API, command, or a `data/*.json` field | **major** |
| New command, new generator, new optional field | **minor** |
| Bug fix with no API change | **patch** |

Pre-1.0, breaking changes go in a **minor** bump — but say so plainly in the changelog,
because people are already depending on it.

## Steps

1. Update `Packages/com.alperenelbiz.dccbridge/package.json` → `version`.
2. Add a `CHANGELOG.md` section for the version. Write what *changed for the user*, not
   which files moved. Include a **Known limitations** list; it is more useful than silence.
3. Mirror `README.md` and `CHANGELOG.md` to the repo root (the package copies are what the
   Package Manager shows; the root copies are what GitHub shows).
4. Commit, then tag:

   ```bash
   git commit -am "Release 0.2.0"
   git tag -a v0.2.0 -m "0.2.0"
   git push origin main --tags
   ```

5. Verify the tag installs cleanly in a scratch project before announcing it:

   ```
   https://github.com/alperenelbiz/unity-dcc-bridge.git?path=/Packages/com.alperenelbiz.dccbridge#v0.2.0
   ```

## Upgrading a consuming project

Change the tag in `Packages/manifest.json` and let Unity re-resolve:

```json
"com.alperenelbiz.dccbridge": "https://github.com/alperenelbiz/unity-dcc-bridge.git?path=/Packages/com.alperenelbiz.dccbridge#v0.2.0"
```

Unity caches git packages aggressively. If a tag change does not take, delete the package's
folder under `Library/PackageCache` and let it re-fetch.

## Never move a published tag

Re-pointing `v0.1.0` at a different commit gives two people the same version with different
code, and the Package Manager's cache hides the difference. Ship `v0.1.1` instead.
