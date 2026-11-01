#!/usr/bin/env bash
# Usage: ./run.sh [profile] [--keep-db]
#   profile    launch profile from launchSettings.json (default: http), e.g. failing-email
#   --keep-db  keep the existing quizapp.db (default: delete it so the schema and seed are recreated)
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/src/QuizApp.Api"
PROFILE="http"
KEEP_DB=false

for arg in "$@"; do
  case "$arg" in
    --keep-db) KEEP_DB=true ;;
    -h|--help) sed -n '2,4p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    -*) echo "Unknown option: $arg" >&2; exit 1 ;;
    *) PROFILE="$arg" ;;
  esac
done

if ! grep -q "\"$PROFILE\": {" "$PROJECT_DIR/Properties/launchSettings.json"; then
  echo "Unknown launch profile '$PROFILE'. Available:" >&2
  grep -oE '^    "[^"]+": \{' "$PROJECT_DIR/Properties/launchSettings.json" | cut -d'"' -f2 | sed 's/^/  /' >&2
  exit 1
fi

# The connection string is relative ("Data Source=quizapp.db"), so always run from the project directory.
cd "$PROJECT_DIR"

if [ "$KEEP_DB" = false ]; then
  rm -f quizapp.db quizapp.db-shm quizapp.db-wal quizapp.db-journal
  echo "Deleted quizapp.db (EnsureCreated + seed will recreate it). Use --keep-db to keep data."
fi

echo "Starting QuizApp with profile '$PROFILE' on http://localhost:5003 ..."
exec dotnet run --launch-profile "$PROFILE"
