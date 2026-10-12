#!/usr/bin/env sh

set -eu

if [ -n "${MKDOCS_PATH:-}" ]; then
  RESOLVED_MKDOCS_PATH="$MKDOCS_PATH"
elif [ -x "./.venv/bin/mkdocs" ]; then
  RESOLVED_MKDOCS_PATH="./.venv/bin/mkdocs"
elif [ -x "./.venv/Scripts/mkdocs.exe" ]; then
  RESOLVED_MKDOCS_PATH="./.venv/Scripts/mkdocs.exe"
else
  RESOLVED_MKDOCS_PATH="./.venv/bin/mkdocs"
fi

OUTPUT_DIR="${OUTPUT_DIR:-./artifacts}"
ARCHIVE_NAME="${ARCHIVE_NAME:-hobiary-logpipe-docs.tar.gz}"

if [ ! -x "$RESOLVED_MKDOCS_PATH" ]; then
  echo "MkDocs executable not found at '$RESOLVED_MKDOCS_PATH'." >&2
  exit 1
fi

"$RESOLVED_MKDOCS_PATH" build --strict

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

ARCHIVE_PATH="$OUTPUT_DIR/$ARCHIVE_NAME"

tar -czf "$ARCHIVE_PATH" -C site .

echo "Created docs archive: $ARCHIVE_PATH"