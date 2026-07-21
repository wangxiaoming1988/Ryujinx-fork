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
binary_path="$app_path/Contents/MacOS/Ryujinx"
timestamp=$(date +%Y%m%d_%H%M%S)
log_path=${RYUJINX_GUARD_LOG:-"$PWD/tmp-build/guarded_game_smoke_${timestamp}.log"}

if [[ ! -x "$binary_path" ]]; then
    print -u2 "Ryujinx binary is not executable: $binary_path"
    exit 66
fi

if [[ ! -f "$game_path" ]]; then
    print -u2 "Game file does not exist: $game_path"
    exit 66
fi

if ! [[ "$duration_seconds" == <1-> && "$max_rss_mib" == <1-> && "$gpu_timeout_limit" == <1-> ]]; then
    print -u2 "Duration, RSS limit, and GPU timeout limit must be positive integers."
    exit 64
fi

mkdir -p "${log_path:h}"

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

env \
    RYUJINX_VTG_DIAGNOSTICS=${RYUJINX_VTG_DIAGNOSTICS:-0} \
    "$binary_path" "${launch_args[@]}" >"$log_path" 2>&1 &
pid=$!

print "pid=$pid"
print "log=$log_path"

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

    if (( rss_mib > max_rss_mib )); then
        stop_reason="rss_limit"
        break
    fi

    timeout_count=$(rg -a -c \
        "gpuEvent progress timeout|Device lost|VK_ERROR|WaitOnSyncpoint:.*more than 1000ms|Unsafe Vulkan texture" \
        "$log_path" 2>/dev/null || true)
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
rg -a -n -i \
    "Using paged|buffer-backed|Unsafe MoltenVK texture descriptor rejected|WaitOnSyncpoint|GPU processing thread|gpuEvent progress timeout|Device lost|exception|fatal|out of memory|shader translator" \
    "$log_path" || true

if [[ "$stop_reason" == "rss_limit" || "$stop_reason" == "gpu_timeout_limit" ]]; then
    exit 1
fi
