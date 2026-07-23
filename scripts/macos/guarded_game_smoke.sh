#!/bin/zsh

set -u
setopt NO_BG_NICE

script_name=${0:t}

usage() {
    print -u2 "Usage: $script_name <Ryujinx.app> <game> [duration-seconds] [max-rss-mib]"
}

if (( $# < 2 )); then
    usage
    exit 64
fi

app_path=$1
game_path=$2
duration_seconds=${3:-15}
max_rss_mib=${4:-8192}
gpu_timeout_limit=${RYUJINX_GUARD_GPU_TIMEOUT_LIMIT:-2}
no_gui=${RYUJINX_NO_GUI:-0}
no_hypervisor=${RYUJINX_NO_HYPERVISOR:-0}
normal_gui_launch=${RYUJINX_NORMAL_GUI_LAUNCH:-1}
screenshot_at=${RYUJINX_GUARD_SCREENSHOT_AT:-0}
screenshot_path=${RYUJINX_GUARD_SCREENSHOT:-}
binary_path="$app_path/Contents/MacOS/Ryujinx"
timestamp=$(date +%Y%m%d_%H%M%S)
log_path=${RYUJINX_GUARD_LOG:-"$PWD/tmp-build/guarded_game_smoke_${timestamp}.log"}
app_log_path=""
screenshot_taken=0

if [[ ! -x "$binary_path" ]]; then
    print -u2 "Ryujinx binary is not executable: $binary_path"
    exit 66
fi

if [[ ! -f "$game_path" ]]; then
    print -u2 "Game file does not exist: $game_path"
    exit 66
fi

if ! [[ "$duration_seconds" == <1-> && "$max_rss_mib" == <1-> && "$gpu_timeout_limit" == <1-> && "$screenshot_at" == <0-> ]]; then
    print -u2 "Duration, RSS limit, GPU timeout limit, and screenshot time must be non-negative integers."
    exit 64
fi

mkdir -p "${log_path:h}"

if (( screenshot_at > 0 )); then
    if [[ -z "$screenshot_path" ]]; then
        screenshot_path="$PWD/tmp-build/guarded_game_smoke_${timestamp}.png"
    fi

    mkdir -p "${screenshot_path:h}"
fi

pid=0
stop_reason="duration_limit"

stop_process() {
    if (( pid == 0 )) || ! kill -0 "$pid" 2>/dev/null; then
        return
    fi

    kill -TERM "$pid" 2>/dev/null || true

    for _ in {1..30}; do
        if ! kill -0 "$pid" 2>/dev/null; then
            return
        fi

        sleep 0.1
    done

    kill -KILL "$pid" 2>/dev/null || true
}

trap 'stop_reason="signal"; stop_process' INT TERM
trap 'stop_process' EXIT

launch_args=("$game_path")
if [[ "$no_gui" == "1" ]]; then
    launch_args=(--no-gui --backend-threading Off "$game_path")

    if [[ "$no_hypervisor" == "1" ]]; then
        launch_args+=(--use-hypervisor=false)
    fi
fi

if [[ "$no_gui" != "1" && "$normal_gui_launch" == "1" ]]; then
    previous_app_logs=("$HOME/Library/Logs/Ryujinx"/Ryujinx_*.log(Nom))
    previous_app_log_path=${previous_app_logs[1]:-}

    open -n "$app_path" --args --backend-threading Off "${launch_args[@]}"

    for _ in {1..150}; do
        pid=$(ps -axo pid=,command= | awk -v app="$app_path" '
            index($0, app "/Contents/MacOS/../Resources/runtime/Ryujinx") ||
            index($0, app "/Contents/Resources/runtime/Ryujinx") ||
            index($0, app "/Contents/MacOS/Ryujinx") { print $1; exit }
        ')

        if [[ -n "$pid" ]]; then
            break
        fi

        sleep 0.1
    done

    if [[ -z "$pid" ]]; then
        print -u2 "Ryujinx GUI process did not start through LaunchServices."
        exit 70
    fi

    for _ in {1..100}; do
        app_logs=("$HOME/Library/Logs/Ryujinx"/Ryujinx_*.log(Nom))
        newest_app_log_path=${app_logs[1]:-}

        if [[ -n "$newest_app_log_path" && "$newest_app_log_path" != "$previous_app_log_path" ]]; then
            app_log_path="$newest_app_log_path"
            break
        fi

        sleep 0.1
    done

    if [[ -z "$app_log_path" ]]; then
        print -u2 "Ryujinx did not create a new application log."
        stop_process
        exit 70
    fi
else
    env \
        RYUJINX_VTG_DIAGNOSTICS=${RYUJINX_VTG_DIAGNOSTICS:-0} \
        RYUJINX_FOLDED_DIAGNOSTICS=${RYUJINX_FOLDED_DIAGNOSTICS:-0} \
        "$binary_path" "${launch_args[@]}" >"$log_path" 2>&1 &
    pid=$!
    app_log_path="$log_path"
fi

print "pid=$pid"
print "log=$app_log_path"

for (( elapsed = 1; elapsed <= duration_seconds; elapsed++ )); do
    sleep 1

    if ! kill -0 "$pid" 2>/dev/null; then
        stop_reason="process_exit"
        break
    fi

    rss_kib=$(ps -o rss= -p "$pid" | tr -d ' ')
    rss_kib=${rss_kib:-0}
    rss_mib=$(( rss_kib / 1024 ))

    print "t=${elapsed}s rss=${rss_mib}MiB"

    if (( screenshot_at > 0 && screenshot_taken == 0 && elapsed >= screenshot_at )); then
        if screencapture -x "$screenshot_path" 2>/dev/null; then
            print "screenshot=$screenshot_path"
        else
            print -u2 "Failed to capture screenshot: $screenshot_path"
        fi

        screenshot_taken=1
    fi

    if (( rss_mib > max_rss_mib )); then
        stop_reason="rss_limit"
        break
    fi

    timeout_count=0
    if [[ -n "$app_log_path" ]]; then
        timeout_count=$(rg -a -c \
            "gpuEvent progress timeout|Device lost|VK_ERROR|WaitOnSyncpoint:.*more than 1000ms|Unsafe Vulkan texture" \
            "$app_log_path" 2>/dev/null || true)
    fi
    timeout_count=${timeout_count:-0}

    if (( timeout_count >= gpu_timeout_limit )); then
        stop_reason="gpu_timeout_limit"
        break
    fi
done

stop_process
wait "$pid" 2>/dev/null || true
trap - EXIT

print "reason=$stop_reason"
print -- "--- key log ---"
if [[ -n "$app_log_path" ]]; then
    rg -a -n -i \
        "Using folded|Using paged|buffer-backed|Folded diagnostic|Indirect global-store|Blocked unsupported|Unsafe MoltenVK texture descriptor rejected|WaitOnSyncpoint|GPU processing thread|gpuEvent progress timeout|Device lost|exception|fatal|out of memory|shader translator" \
        "$app_log_path" || true
fi

if [[ "$stop_reason" == "rss_limit" || "$stop_reason" == "gpu_timeout_limit" ]]; then
    exit 1
fi
