"""AC HUD CSP bridge.

Install this file as an Assetto Corsa Python app.  It reads the player car's
ABS/TC state from CSP and sends a small flat patch to AC HUD's backend.  The
normal shared-memory collector remains the source for the rest of telemetry.

The app intentionally does not read oil/water data.  Those fields are not
important for the current driver HUD and vary more between cars than ABS/TC.
"""

import json
import socket
import threading
import time

import ac

APP_NAME = "AC HUD Bridge"
BACKEND_HOST = "127.0.0.1"
BACKEND_PORT = 3001
BACKEND_PATH = "/telemetry/patch"
PUBLISH_INTERVAL_SECONDS = 0.033
HTTP_TIMEOUT_SECONDS = 0.25

app_window = None
status_label = None
worker_thread = None
stop_event = threading.Event()
patch_event = threading.Event()
patch_lock = threading.Lock()
pending_patch = None
state_lock = threading.Lock()
last_success_at = 0.0
last_error = ""
update_accumulator = 0.0


def read_csp_value(function_name):
    """Call an optional CSP function without breaking older CSP builds."""
    function = getattr(ac, function_name, None)
    if function is None:
        return None
    try:
        return function(0)
    except Exception:
        return None


def read_patch():
    patch = {}

    abs_active = read_csp_value("ext_getStateAbsActive")
    if abs_active is not None:
        patch["abs.active"] = bool(abs_active)

    tc_active = read_csp_value("ext_getStateTcActive")
    if tc_active is not None:
        patch["tc.active"] = bool(tc_active)

    abs_level = read_csp_value("ext_getStateAbs")
    if abs_level is not None:
        patch["abs"] = int(abs_level)

    tc_level = read_csp_value("ext_getStateTc")
    if tc_level is not None:
        patch["tc"] = int(tc_level)

    return patch


def enqueue_latest(patch):
    global pending_patch
    with patch_lock:
        pending_patch = patch
    patch_event.set()


def send_patch(patch):
    body = json.dumps(patch).encode("utf-8")
    headers = (
        "POST " + BACKEND_PATH + " HTTP/1.1\r\n"
        "Host: " + BACKEND_HOST + ":" + str(BACKEND_PORT) + "\r\n"
        "Content-Type: application/json\r\n"
        "Content-Length: " + str(len(body)) + "\r\n"
        "Connection: close\r\n\r\n"
    ).encode("ascii")

    connection = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    connection.settimeout(HTTP_TIMEOUT_SECONDS)
    try:
        connection.connect((BACKEND_HOST, BACKEND_PORT))
        connection.sendall(headers + body)
        connection.recv(256)
    finally:
        connection.close()


def publish_worker():
    global last_success_at, last_error, pending_patch

    while not stop_event.is_set():
        patch_event.wait(0.5)
        if stop_event.is_set():
            break
        with patch_lock:
            patch = pending_patch
            pending_patch = None
            patch_event.clear()
        if patch is None:
            continue

        try:
            send_patch(patch)
            with state_lock:
                last_success_at = time.time()
                last_error = ""
        except Exception as error:
            with state_lock:
                last_error = str(error)


def start_worker():
    global worker_thread, stop_event, patch_event, pending_patch

    stop_event = threading.Event()
    patch_event = threading.Event()
    pending_patch = None
    worker_thread = threading.Thread(target=publish_worker)
    worker_thread.daemon = True
    worker_thread.start()


def stop_worker():
    global worker_thread

    stop_event.set()
    patch_event.set()

    if worker_thread is not None and worker_thread.is_alive():
        worker_thread.join(0.5)
    worker_thread = None


def update_status():
    if status_label is None:
        return

    with state_lock:
        success_at = last_success_at
        error = last_error

    if success_at > 0 and time.time() - success_at < 2.0:
        status = "CSP bridge: connected"
    elif error:
        status = "CSP bridge: retrying"
    else:
        status = "CSP bridge: waiting"

    ac.setText(status_label, status)


def acMain(ac_version):
    global app_window, status_label

    app_window = ac.newApp(APP_NAME)
    ac.setSize(app_window, 260, 38)
    ac.drawBorder(app_window, 0)
    ac.setBackgroundOpacity(app_window, 0.65)
    status_label = ac.addLabel(app_window, "CSP bridge: starting")
    ac.setPosition(status_label, 8, 10)
    start_worker()
    return APP_NAME


def acUpdate(delta_t):
    global update_accumulator

    update_accumulator += delta_t
    if update_accumulator >= PUBLISH_INTERVAL_SECONDS:
        update_accumulator = 0.0
        patch = read_patch()
        if patch:
            enqueue_latest(patch)

    update_status()


def acShutdown():
    stop_worker()


def ext_acReload():
    stop_worker()
