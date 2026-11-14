#!/bin/sh
set -eu

worker_id="${WEBSITE_POD_NAME:-${CONTAINER_NAME:-}}"
if [ -z "$worker_id" ]; then
    echo "WEBSITE_POD_NAME or CONTAINER_NAME is required." >&2
    exit 64
fi

worker_path="/azure-functions-worker/${FUNCTIONS_WORKER_RUNTIME_VERSION}/LINUX/X64/worker.py"
if [ ! -f "$worker_path" ]; then
    echo "Python worker was not found at $worker_path." >&2
    exit 1
fi

request_id="${FUNCTIONS_WORKER_REQUEST_ID:-$(cat /proc/sys/kernel/random/uuid)}"
exec python -X no_debug_ranges "$worker_path" \
    --functions-uri "${FUNCTIONS_WORKER_PROXY_URI:-http://127.0.0.1:50054}" \
    --functions-worker-id "$worker_id" \
    --functions-request-id "$request_id" \
    --functions-grpc-max-message-length "${FUNCTIONS_GRPC_MAX_MESSAGE_LENGTH:-134217728}"
