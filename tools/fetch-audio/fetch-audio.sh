#!/usr/bin/env bash
# Downloads CC0 sounds from Freesound (needs FREESOUND_API_KEY). See docs/AUDIO.md.
exec python3 "$(dirname "$0")/fetch_audio.py" "$@"
