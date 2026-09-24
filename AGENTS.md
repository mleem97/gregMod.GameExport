# AGENTS.md — Notes for AI agents (gregMod.GameExport)

Repo: gregMod.GameExport · License: see `LICENSE` if present, else Apache-2.0 · Version: see `VERSION` (1.0.0).

MelonMod for Data Center (`GameExportMod : MelonMod`). Exports game data
for external tooling. Panel key: **F12**.

## Duties

1. **Read first:** `README.md` and `src/` — only then make changes.
2. **Do not commit secrets** (keys, tokens, `.env`). Use keys only via environment variables.
3. **Preserve history:** no `push --force`, no history rewrite without instruction.
4. **Verify changes:** before reporting done, build the mod (`dotnet build gregMod.GameExport.csproj -c Release` or `./build.sh GameExport` from `ModRepositories/`).
5. **Keep docs in sync:** for new features update `README.md` + `CHANGELOG.md` (Unreleased).
6. **Conventions:** Conventional Commits (`feat:`, `fix:`, `docs:`, `chore:` …), one logical change per commit.
7. **When unsure:** stop and ask instead of guessing — especially for deletes, migrations, CI.

## Build and references

- Target: `net6.0`, x64. Game: Data Center (`MelonGame("Waseku", "Data Center")`).
- `references/` holds absolute symlinks into the Steam Data Center install.
  Never commit `references/*.dll`, `bin/`, or `obj/`.
- After a fresh clone, run `../tools/sync-melon-assemblies.sh`.
- Deploy only with `./build.sh GameExport --deploy`.

## Hard rules

- Export is read-only against game state: never mutate economy, inventory, or
  save data from export paths.
- **Never** touch gregCore types outside a soft-probe/JIT-split bridge — the
  mod must load without `gregCore.dll`.
- Defensive `try/catch` in every per-frame path; no per-frame reflection.
- Reverse-engineering evidence for game internals belongs in `docs/` (create
  `docs/COMPATIBILITY.md` when documenting interop findings).

## Layout

- `src/GameExportMod.cs` — MelonMod entry, prefs, F12 panel.
- `src/Core/` — export logic (format writers, serializers).
