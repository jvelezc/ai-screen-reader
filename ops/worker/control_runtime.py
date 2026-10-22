"""Private portal lifecycle composed with the existing fenced worker runtime."""
import base64
import http.client
import json
import os
from pathlib import Path
import re
import stat
import time
from activation import current_commit
from host_runtime import HostRuntime, DOTNET, run_checked
from release_bundle import SHA, ReleaseError, require, unique_object

CONTROL_UNIT = "hsr-control.service"


def valid_public_key(key):
    if not isinstance(key, str) or not 20 <= len(key) <= 4096 or re.fullmatch(r"[A-Za-z0-9_.-]+", key) is None:
        return False
    if key.startswith("sb_publishable_"):
        return True
    try:
        parts = key.split(".")
        if len(parts) != 3:
            return False
        payload = base64.b64decode(parts[1] + "=" * (-len(parts[1]) % 4), altchars=b"-_", validate=True)
        value = json.loads(payload, object_pairs_hook=unique_object)
        return isinstance(value, dict) and value.get("role") == "anon" and value.get("ref") == "gpnijaxrgtwzwtrqsbcu"
    except (ValueError, UnicodeError, ReleaseError):
        return False


def control_environment(base, commit, runtime_environment, key):
    require(SHA.fullmatch(commit) is not None and valid_public_key(key), "CONTROL_CONFIGURATION_INVALID")
    # Copy only the existing closed runtime environment, never the caller's environment.
    allowed = {"PATH", "HOME", "USER", "LOGNAME", "XDG_RUNTIME_DIR", "DBUS_SESSION_BUS_ADDRESS",
               "DOTNET_NOLOGO", "DOTNET_CLI_TELEMETRY_OPTOUT"}
    require(set(runtime_environment) == allowed, "CONTROL_ENVIRONMENT_INVALID")
    return runtime_environment | {"HSR_CONTROL_PUBLISHABLE_KEY": key, "HSR_RELEASE_COMMIT": commit,
        "HSR_CONTROL_EVIDENCE": str(Path(base) / "state" / "evidence"),
        "HSR_CONTROL_PORT": "8086", "HSR_CONTROL_ORIGIN": "http://localhost:6086"}


class ControlService:
    def __init__(self, runtime):
        self.runtime = runtime
        self.base = runtime.base

    def public_key(self):
        descriptor = os.open(self.base / "config" / "control.env", os.O_RDONLY | os.O_NOFOLLOW)
        with os.fdopen(descriptor, "r", encoding="utf-8") as source:
            facts = os.fstat(source.fileno())
            require(stat.S_ISREG(facts.st_mode) and facts.st_uid == 0 and facts.st_mode & 0o077 == 0
                    and facts.st_size <= 4200, "CONTROL_CONFIGURATION_INVALID")
            text = source.read(4201)
        match = re.fullmatch(r"HSR_CONTROL_PUBLISHABLE_KEY=([A-Za-z0-9_.-]{20,4096})\n", text)
        require(match is not None and valid_public_key(match[1]), "CONTROL_CONFIGURATION_INVALID")
        return match[1]

    def probe(self, commit):
        env = control_environment(self.base, commit, self.runtime.environment(), self.public_key())
        text = run_checked(["/usr/sbin/runuser", "-u", "hsr-worker", "--", DOTNET,
            str(self.base / "releases" / commit / "control" / "HeadlessScreenReader.Cli.dll"), "--check-control"],
            env=env, timeout=30)
        receipt = json.loads(text, object_pairs_hook=unique_object)
        expected = {"Schema": "hsr-control-runtime-proof-v1", "State": "ControlRuntimeReady", "ReleaseCommit": commit,
                    "PublicConfigurationRead": True, "UnauthenticatedSessionDenied": True,
                    "ServerCloseConfirmed": True, "AppJwtValidated": False, "PublicMcpEnabled": False}
        require(receipt == expected and all(type(receipt.get(key)) is bool for key in expected if type(expected[key]) is bool),
                "CONTROL_RUNTIME_UNCONFIRMED")

    def stop(self):
        run_checked(["/usr/bin/systemctl", "stop", CONTROL_UNIT], timeout=30)
        state = run_checked(["/usr/bin/systemctl", "show", CONTROL_UNIT, "--property=ActiveState", "--value"])
        pid = run_checked(["/usr/bin/systemctl", "show", CONTROL_UNIT, "--property=MainPID", "--value"])
        require(state in ("inactive", "failed") and pid == "0", "CONTROL_STOP_UNCONFIRMED")

    def enable(self):
        run_checked(["/usr/bin/systemctl", "enable", CONTROL_UNIT])

    def disable(self):
        run_checked(["/usr/bin/systemctl", "disable", CONTROL_UNIT])

    def http_ready(self, commit):
        connection = http.client.HTTPConnection("127.0.0.1", 8086, timeout=3)
        try:
            connection.request("GET", "/internal/ready")
            response = connection.getresponse()
            body = response.read(4097)
            require(response.status == 200 and len(body) <= 4096, "CONTROL_HTTP_UNCONFIRMED")
            result = json.loads(body, object_pairs_hook=unique_object)
        except (ValueError, OSError, http.client.HTTPException, ReleaseError):
            raise ReleaseError("CONTROL_HTTP_UNCONFIRMED") from None
        finally:
            connection.close()
        expected = {"schema": "hsr-control-readiness-v1", "state": "Listening", "releaseCommit": commit,
                    "appJwtValidated": False, "publicMcpEnabled": False}
        require(result == expected and result.get("appJwtValidated") is False and result.get("publicMcpEnabled") is False,
                "CONTROL_HTTP_UNCONFIRMED")

    def start_ready(self, commit):
        state = run_checked(["/usr/bin/systemctl", "show", CONTROL_UNIT, "--property=ActiveState", "--value"])
        require(state in ("inactive", "failed"), "CONTROL_START_STATE_UNCONFIRMED")
        if state == "failed":
            run_checked(["/usr/bin/systemctl", "reset-failed", CONTROL_UNIT])
        run_checked(["/usr/bin/systemctl", "start", CONTROL_UNIT])
        invocation = run_checked(["/usr/bin/systemctl", "show", CONTROL_UNIT, "--property=InvocationID", "--value"])
        require(re.fullmatch(r"[a-f0-9]{32}", invocation) is not None, "CONTROL_INVOCATION_UNCONFIRMED")
        deadline = time.monotonic() + 30
        expected = {"Schema": "hsr-control-readiness-v1", "State": "Listening", "ReleaseCommit": commit,
                    "AppJwtValidated": False, "PublicMcpEnabled": False}
        while time.monotonic() < deadline:
            state = run_checked(["/usr/bin/systemctl", "show", CONTROL_UNIT, "--property=ActiveState", "--value"], timeout=5)
            require(state in ("active", "activating"), "CONTROL_SERVICE_UNCONFIRMED")
            lines = run_checked(["/usr/bin/journalctl", "--no-pager", "--output=cat", "--lines=32",
                                 "_SYSTEMD_INVOCATION_ID=" + invocation], timeout=5)
            for line in lines.splitlines():
                try:
                    receipt = json.loads(line, object_pairs_hook=unique_object)
                except (ValueError, ReleaseError):
                    continue
                if receipt == expected and receipt.get("AppJwtValidated") is False and receipt.get("PublicMcpEnabled") is False:
                    self.http_ready(commit)
                    status = run_checked(["/usr/bin/systemctl", "show", CONTROL_UNIT, "--property=InvocationID",
                                          "--property=ActiveState", "--property=MainPID"], timeout=5)
                    facts = dict(line.split("=", 1) for line in status.splitlines())
                    require(facts.get("InvocationID") == invocation, "CONTROL_INVOCATION_CHANGED")
                    require(facts.get("ActiveState") == "active"
                            and re.fullmatch(r"[1-9][0-9]{0,9}", facts.get("MainPID", "")) is not None,
                            "CONTROL_SERVICE_UNCONFIRMED")
                    return
            time.sleep(0.25)
        raise ReleaseError("CONTROL_READINESS_UNCONFIRMED")


class DeploymentRuntime(HostRuntime):
    """Portal quiescence precedes worker recovery; v1 releases remain valid rollback targets."""
    def __init__(self, base):
        super().__init__(base)
        self.control = ControlService(self)

    def has_control(self, commit):
        return self.manifest(commit)["schema"] == "hsr-worker-control-release-v2"

    def stop(self):
        self.control.stop()
        super().stop()

    def probe(self, commit):
        super().probe(commit)
        if self.has_control(commit):
            self.control.probe(commit)

    def start_ready(self):
        super().start_ready()
        commit = current_commit(self.base)
        require(commit is not None, "CONTROL_RELEASE_MISSING")
        if self.has_control(commit):
            self.control.start_ready(commit)
        else:
            self.control.disable()

    def enable(self):
        super().enable()
        commit = current_commit(self.base)
        require(commit is not None, "CONTROL_RELEASE_MISSING")
        if self.has_control(commit):
            self.control.enable()
        else:
            self.control.disable()
