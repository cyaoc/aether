#!/bin/zsh
# codex.sh <codex exec args>: every image call in the tools goes through here, so the model is set in one place:
# gpt-6.1-sol at its default reasoning effort (medium), whatever ~/.codex/config.toml defaults to.
exec codex exec -m gpt-6.1-sol --skip-git-repo-check -s workspace-write "$@"
