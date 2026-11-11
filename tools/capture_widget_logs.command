#!/bin/bash
# Read-only host diagnostics. Run in a normal Terminal session, because macOS
# does not permit unified-log or PluginKit access from the coding sandbox.
set -u
cd "$(dirname "$0")/.."
mkdir -p build/diagnostics
task_report="$PWD/build/diagnostics/widget-host-latest.log"
printf 'Collecting Kotoba widget host diagnostics…\n'
{
    printf 'Kotoba widget host report — %s\n\n' "$(date)"
    /usr/bin/sw_vers
    printf '\nAPP AND WIDGET STATUS\n'
    python3 tools/widget_status.py
    printf '\nRUNNING APP AND WIDGET HOST PROCESSES\n'
    /bin/ps -axo pid,lstart,comm | /usr/bin/awk '/Kotoba|Yarxi|chronod|NotificationCenter/ && !/awk/'
    printf '\nPLUGIN REGISTRATION\n'
    /usr/bin/pluginkit -m -A -D -v -i local.yarxi.study.widget
    printf '\nRECENT WIDGET HOST LOG\n'
    /usr/bin/log show --last 15m --style compact --info --debug \
        --predicate 'process == "YarxiStudyWidget" OR process == "chronod" OR eventMessage CONTAINS[c] "yarxi" OR eventMessage CONTAINS[c] "StudyConfiguration" OR eventMessage CONTAINS[c] "WidgetFontSize"'
} > "$task_report" 2>&1
printf '\nSaved report: %s\n' "$task_report"
printf 'Tell Codex the capture is ready; the report is in the shared project.\n'
