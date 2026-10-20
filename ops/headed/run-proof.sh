#!/bin/bash
set -eu
umask 077
mkdir /tmp/hsr-headed-proof
cd /tmp/hsr-headed-proof
set +e
xvfb-run --auto-servernum --server-args='-screen 0 1280x720x24 -nolisten tcp' \
  /bin/sh -c 'chmod 600 "$XAUTHORITY"; exec dotnet /app/cli/HeadlessScreenReader.Cli.dll --headed-session' > run.json 2> run.err
result=$?
set -e
clean=false
if [ -f display-number.txt ] && [ -f display-pid.txt ]; then
  read -r number < display-number.txt
  read -r display_pid < display-pid.txt
  case "$number:$display_pid" in
    *[!0-9:]*|:*) clean=false ;;
    *) case "$number" in
    ''|*[!0-9]*) clean=false ;;
    *) for attempt in $(seq 1 50); do
         if [ ! -e "/tmp/.X${number}-lock" ] && [ ! -e "/tmp/.X11-unix/X${number}" ] && ! kill -0 "$display_pid" 2>/dev/null; then clean=true; break; fi
         sleep 0.1
       done ;;
      esac ;;
  esac
fi
printf '{"schema":"hsr-headed-display-cleanup-v1","displayCleanupConfirmed":%s,"commandExitCode":%s}\n' "$clean" "$result" > cleanup.json
if [ "$result" -ne 0 ]; then exit "$result"; fi
if [ "$clean" != true ]; then exit 4; fi
