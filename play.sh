#!/usr/bin/env bash
# One command to play: builds what changed and starts the game fullscreen (decisions #424, #425).
# Quit: Esc opens the game menu, then "Quit game". Add --window for a framed 1920x1080 window (tests only), --lang en|ru.
exec "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/run-globe-spike.sh" "$@"
