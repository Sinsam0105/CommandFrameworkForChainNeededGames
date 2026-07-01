# CommandFrameworkForChainNeededGames — Version History

This repository keeps several historical architecture versions for reference.
Only `v1-execute-only` is intended for active Project-C development.

## Active version

### `v1-execute-only`

**Meaning:** Execute pipeline plus numeric preview transactions.

- `Command<T>` exposes `Execute()` only.
- `CommandEvent<T>` owns the execution pipeline and `NumChangeEvent`.
- `NumPreview()` / `NumPreviewAsync()` apply temporary numeric changes, validate, capture values, and remove only newly-added modifiers.
- Project-specific preview presentation must be implemented outside this package.
- For Project-C, card preview should live in a game-side `CardPreviewSystem` / `CardPreviewResult`.

Current role: **active / recommended**.

## Archived versions

### `archive/v0-deepclone-preview`

**Meaning:** DeepClone + full command preview experiment.

Representative commit: `50602555b56a81d00df92fda0fbcaf750b61dad8`

Characteristics:

- Command preview runs close to the real command pipeline.
- Runtime data is cloned for preview.
- `CommandSession` manages nested commands, preview clones, and after-command chains.
- Powerful, but too complex for Project-C's actual preview needs.

Current role: **archive / research reference only**.

### `archive/v0-reset-set-preview`

**Meaning:** Reset/Set-oriented preview experiment.

Representative commit: `aae6366a996ff3eea6d0213b78e44c7f7b5b8a7e`

Characteristics:

- Introduced automatic `ResetContext()` / `SetContext()` traversal through `RuntimeDataReflection`.
- Added automatic `[NullCheck]` support.
- Useful as an intermediate experiment, but not the final preview direction.

Current role: **archive / intermediate experiment**.

### `archive/v1-preview-snapshot`

**Meaning:** PreviewSnapshot / PreviewAware experiment.

Representative commit: `0aa5f36bb5e379160908950627c9021a2dfa7b31`

Characteristics:

- Introduced `PreviewSnapshot` and `PreviewAware` style access.
- Stores real-to-clone mappings during preview.
- Intended to make preview more explicit than raw DeepClone execution.
- Still too broad for Project-C's actual UI preview scope.

Current role: **archive / preview architecture reference**.

## Policy

- Do not build new Project-C gameplay code against archived preview branches.
- Do not add broad gameplay preview features back into the framework unless a concrete game-side need proves it necessary.
- Keep the framework focused on command execution and numeric preview transactions.
- Keep card number, damage, heal, cost, and usability previews in Project-C-specific systems.

## Recommended Project-C dependency

During active development, use:

```json
"com.sinsam.command-framework": "https://github.com/Sinsam0105/CommandFrameworkForChainNeededGames.git#v1-execute-only"
```

After Unity compile/playmode verification, create and pin a stable tag such as:

```json
"com.sinsam.command-framework": "https://github.com/Sinsam0105/CommandFrameworkForChainNeededGames.git#v1.0.0-execute-only"
```

Tag creation should only happen after Project-C compiles against `v1-execute-only`.
