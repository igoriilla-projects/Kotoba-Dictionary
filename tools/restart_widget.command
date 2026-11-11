#!/bin/bash
# Restart only Kotoba's widget worker after replacing a local development build.
set -euo pipefail
cd "$(dirname "$0")/.."
task_refresh_host=false
case "${1:-}" in
    '') ;;
    --refresh-host) task_refresh_host=true ;;
    *) printf 'Usage: %s [--refresh-host]\n' "$0" >&2; exit 2 ;;
esac
mkdir -p build/diagnostics
task_report="$PWD/build/diagnostics/widget-restart-latest.log"
exec > >(/usr/bin/tee "$task_report") 2>&1
trap 'task_status=$?; if [[ "$task_status" -ne 0 ]]; then printf "Recovery stopped with exit code %s.\n" "$task_status"; fi; printf "Restart report: %s\n" "$task_report"' EXIT

# Match only this user's processes and verify the selected PIDs actually exit.
stop_processes() {
    local task_label="$1" task_pattern="$2" task_pids task_pid task_remaining task_attempt
    task_pids="$(/bin/ps -U "$(/usr/bin/id -u)" -o pid=,comm= | /usr/bin/awk -v pattern="$task_pattern" '{ pid = $1; sub(/^[[:space:]]*[0-9]+[[:space:]]+/, ""); if ($0 ~ pattern) print pid }')"
    if [[ -z "$task_pids" ]]; then
        printf 'No running %s was found.\n' "$task_label"
        return
    fi
    while IFS= read -r task_pid; do
        printf 'Sending TERM to %s PID %s\n' "$task_label" "$task_pid"
        kill -TERM "$task_pid"
    done <<< "$task_pids"
    for task_attempt in {1..50}; do
        task_remaining=''
        while IFS= read -r task_pid; do
            if kill -0 "$task_pid" 2>/dev/null; then
                task_remaining="$task_remaining $task_pid"
            fi
        done <<< "$task_pids"
        if [[ -z "$task_remaining" ]]; then
            printf '%s stopped.\n' "$task_label"
            return
        fi
        sleep 0.1
    done
    printf '%s did not stop:%s\n' "$task_label" "$task_remaining" >&2
    return 1
}

task_app="$PWD/build/Kotoba.app"
if [[ -d '/Applications/Kotoba.app' ]]; then
    task_app='/Applications/Kotoba.app'
fi
if [[ ! -d "$task_app" ]]; then
    printf 'Kotoba.app was not found. Build the app first.\n' >&2
    exit 1
fi
task_widget="$task_app/Contents/PlugIns/YarxiStudyWidget.appex"
if [[ ! -d "$task_widget" ]]; then
    printf 'The widget extension was not found in %s. Rebuild the app first.\n' "$task_app" >&2
    exit 1
fi
printf 'Stopping the old Kotoba widget worker…\n'
stop_processes 'Old Kotoba widget worker' '/YarxiStudyWidget[.]appex/Contents/MacOS/YarxiStudyWidget$'
# A product rename leaves the previous path/name in host registration. Refresh
# only the chosen app and extension after the old worker has actually exited.
printf 'Registering %s and its widget extension…\n' "$task_app"
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$task_app"
/usr/bin/pluginkit -a "$task_widget"
if [[ "$task_refresh_host" == true ]]; then
    # A renamed extension can keep its old launch job in chronod's PID domain
    # even after PluginKit/LaunchServices point to the new bundle. Terminating
    # the owning user host discards that domain. macOS relaunches these services.
    printf 'Refreshing the desktop/gallery host; widgets will briefly reload…\n'
    stop_processes 'Widget host (chronod)' '^/System/Library/PrivateFrameworks/ChronoCore[.]framework/Support/chronod$'
    stop_processes 'Widget gallery (NotificationCenter)' '^/System/Library/CoreServices/NotificationCenter[.]app/Contents/MacOS/NotificationCenter$'
fi
printf 'Opening %s\n' "$task_app"
/usr/bin/open "$task_app"
printf '\nChoose Settings → Widget setup → Reload widgets in Kotoba.\n'
printf 'The widget host can now launch the current extension.\n'
