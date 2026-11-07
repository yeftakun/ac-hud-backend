"""AC HUD CSP bridge using a local patch file.

The CSP Python runtime on this installation does not expose the socket module,
so the app writes only the supplemental ABS/TC values to a shared JSON file.
The AC HUD backend watches that file and merges it with normal shared-memory
telemetry.
"""

import ac
import json
import os
import time


APP_NAME = "AC HUD Bridge"
PATCH_FILE = r"C:\Users\yefta\Documents\Assetto Corsa\cfg\ac_hud_csp_patch.json"
PUBLISH_INTERVAL_SECONDS = 0.033

app_window = None
status_label = None
update_accumulator = 0.0
last_write_at = 0.0
last_error = ""


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


def write_patch(patch):
    global last_write_at, last_error

    temp_file = PATCH_FILE + ".tmp"
    handle = None
    try:
        handle = open(temp_file, "w")
        handle.write(json.dumps(patch))
        handle.close()
        handle = None
        os.replace(temp_file, PATCH_FILE)
        last_write_at = time.time()
        last_error = ""
    except Exception as error:
        if handle is not None:
            try:
                handle.close()
            except Exception:
                pass
        try:
            if os.path.exists(temp_file):
                os.remove(temp_file)
        except Exception:
            pass
        last_error = str(error)


def update_status():
    if status_label is None:
        return

    if last_write_at > 0 and time.time() - last_write_at < 2.0:
        status = "CSP bridge: file active"
    elif last_error:
        status = "CSP bridge: file error"
    else:
        status = "CSP bridge: starting"

    ac.setText(status_label, status)


def acMain(ac_version):
    global app_window, status_label

    app_window = ac.newApp(APP_NAME)
    ac.setSize(app_window, 260, 38)
    ac.drawBorder(app_window, 0)
    ac.setBackgroundOpacity(app_window, 0.65)
    status_label = ac.addLabel(app_window, "CSP bridge: starting")
    ac.setPosition(status_label, 8, 10)
    return APP_NAME


def acUpdate(delta_t):
    global update_accumulator

    update_accumulator += delta_t
    if update_accumulator >= PUBLISH_INTERVAL_SECONDS:
        update_accumulator = 0.0
        patch = read_patch()
        if patch:
            write_patch(patch)

    update_status()


def acShutdown():
    pass


def ext_acReload():
    pass
