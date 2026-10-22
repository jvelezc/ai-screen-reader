"""Linux host operations; browser receives no database or deploy credentials."""
import json
import os
from pathlib import Path
import pwd
import re
import signal
import subprocess
import tempfile
import time
from release_bundle import IMAGE, SHA, ReleaseError, require, unique_object
from image_archive import verify_image_archive

UNIT = "hsr-observation.service"
DOTNET = "/usr/local/bin/dotnet"


def run_checked(arguments, *, env=None, timeout=30):
    # File-backed output bounds memory even if a broken child is noisy. Never log raw output.
    with tempfile.TemporaryFile() as output, tempfile.TemporaryFile() as errors:
        process = subprocess.Popen(arguments, stdin=subprocess.DEVNULL, stdout=output,
                                   stderr=errors, env=env, start_new_session=True)
        try:
            process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            # Only this fresh owned process group; container cleanup still requires its journal.
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            process.wait(timeout=5)
            raise ReleaseError("DEPLOY_COMMAND_TIMEOUT") from None
        require(process.returncode == 0, "DEPLOY_COMMAND_FAILED")
        require(output.tell() <= 1024**2 and errors.tell() <= 1024**2,
                "DEPLOY_COMMAND_OUTPUT_INVALID")
        output.seek(0)
        return output.read().decode("utf-8", errors="strict").strip()


class HostRuntime:
    def __init__(self, base):
        self.base = Path(base)
        self.account = pwd.getpwnam("hsr-worker")
        require(self.account.pw_uid > 0 and self.account.pw_dir == str(self.base / "home"),
                "DEPLOY_WORKER_ACCOUNT_INVALID")

    def environment(self):
        return {"PATH": "/usr/local/bin:/usr/bin:/bin", "HOME": str(self.base / "home"),
                "USER": "hsr-worker", "LOGNAME": "hsr-worker",
                "XDG_RUNTIME_DIR": "/run/user/" + str(self.account.pw_uid),
                "DBUS_SESSION_BUS_ADDRESS": "unix:path=/run/user/" + str(self.account.pw_uid) + "/bus",
                "DOTNET_NOLOGO": "1", "DOTNET_CLI_TELEMETRY_OPTOUT": "1"}

    def manifest(self, commit):
        require(SHA.fullmatch(commit) is not None, "RELEASE_COMMIT_INVALID")
        root = self.base / "releases" / commit
        require(root.is_dir() and not root.is_symlink(), "DEPLOY_RELEASE_INVALID")
        path = root / "manifest.json"
        require(path.stat().st_size <= 1024**2, "RELEASE_MANIFEST_INVALID")
        data = json.loads(path.read_text(), object_pairs_hook=unique_object)
        require(data["commit"] == commit and data["schema"] == "hsr-worker-release-v1"
                and IMAGE.fullmatch(data["image"]) is not None, "RELEASE_IDENTITY_MISMATCH")
        return data

    def prepare(self, commit):
        data = self.manifest(commit)
        verify_image_archive(self.base / "releases" / commit / "fixture.tar", data["image"])
        run_checked(["/usr/sbin/runuser", "-u", "hsr-worker", "--", "/usr/bin/podman", "load",
                     "--input", str(self.base / "releases" / commit / "fixture.tar")],
                    env=self.environment(), timeout=180)
        actual = run_checked(["/usr/sbin/runuser", "-u", "hsr-worker", "--", "/usr/bin/podman",
                              "image", "inspect", "--format", "{{.Id}}", data["image"]],
                             env=self.environment())
        require("sha256:" + actual.removeprefix("sha256:") == data["image"],
                "DEPLOY_IMAGE_UNCONFIRMED")

    def worker(self, commit, scope, mode):
        require(scope in ("evidence", "readiness") and mode in ("--recover", "--check-runtime"),
                "DEPLOY_OPERATION_INVALID")
        env = self.environment() | {"HSR_WORKER_IMAGE": self.manifest(commit)["image"],
                                    "HSR_WORKER_RUNTIME": "podman",
                                    "HSR_WORKER_EVIDENCE": str(self.base / "state" / scope)}
        # No connection string or management credential is supplied to either operator mode.
        text = run_checked(["/usr/sbin/runuser", "-u", "hsr-worker", "--", DOTNET,
                            str(self.base / "releases" / commit / "worker" / "HeadlessScreenReader.Worker.dll"),
                            mode], env=env, timeout=190)
        return json.loads(text, object_pairs_hook=unique_object)

    def recover(self, commit, scope):
        receipt = self.worker(commit, scope, "--recover")
        require(isinstance(receipt, dict) and set(receipt) == {"State", "Recovered"}
                and receipt.get("State") == "Recovered"
                and type(receipt.get("Recovered")) is int and receipt["Recovered"] >= 0,
                "DEPLOY_RECOVERY_UNCONFIRMED")

    def probe(self, commit):
        receipt = self.worker(commit, "readiness", "--check-runtime")
        require(isinstance(receipt, dict) and set(receipt) == {"Schema", "State", "Image", "ObservedNodes",
                "Sha256", "DatabaseAccessed", "ObservationPersisted", "DispatchedInputs", "GoalVerified", "Recovered"}
                and receipt.get("Schema") == "hsr-runtime-readiness-v1"
                and receipt.get("State") == "RuntimeReady"
                and receipt.get("Image") == self.manifest(commit)["image"]
                and type(receipt.get("ObservedNodes")) is int and 1 <= receipt["ObservedNodes"] <= 2048
                and isinstance(receipt.get("Sha256"), str) and re.fullmatch(r"[a-f0-9]{64}", receipt["Sha256"]) is not None
                and receipt.get("DatabaseAccessed") is False and receipt.get("ObservationPersisted") is False
                and type(receipt.get("DispatchedInputs")) is int and receipt["DispatchedInputs"] == 0
                and receipt.get("GoalVerified") is False and type(receipt.get("Recovered")) is int and receipt["Recovered"] >= 0,
                "DEPLOY_RUNTIME_UNCONFIRMED")

    def stop(self):
        run_checked(["/usr/bin/systemctl", "stop", UNIT], timeout=190)
        state = run_checked(["/usr/bin/systemctl", "show", UNIT, "--property=ActiveState", "--value"])
        pid = run_checked(["/usr/bin/systemctl", "show", UNIT, "--property=MainPID", "--value"])
        require(state in ("inactive", "failed") and pid == "0", "DEPLOY_STOP_UNCONFIRMED")

    def enable(self):
        run_checked(["/usr/bin/systemctl", "enable", UNIT])

    def start_ready(self):
        state = run_checked(["/usr/bin/systemctl", "show", UNIT, "--property=ActiveState", "--value"])
        require(state in ("inactive", "failed"), "DEPLOY_START_STATE_UNCONFIRMED")
        # A never-started inactive unit can be unloaded between systemctl calls.
        # reset-failed rejects that valid first-start state; start loads it normally.
        if state == "failed":
            run_checked(["/usr/bin/systemctl", "reset-failed", UNIT])
        run_checked(["/usr/bin/systemctl", "start", UNIT])
        invocation = run_checked(["/usr/bin/systemctl", "show", UNIT, "--property=InvocationID", "--value"])
        require(re.fullmatch(r"[a-f0-9]{32}", invocation) is not None, "DEPLOY_INVOCATION_UNCONFIRMED")
        until = time.monotonic() + 30
        while time.monotonic() < until:
            state = run_checked(["/usr/bin/systemctl", "show", UNIT, "--property=ActiveState", "--value"], timeout=5)
            require(state in ("active", "activating"), "DEPLOY_SERVICE_UNCONFIRMED")
            lines = run_checked(["/usr/bin/journalctl", "--no-pager", "--output=cat", "--lines=32",
                                 "_SYSTEMD_INVOCATION_ID=" + invocation], timeout=5)
            for line in lines.splitlines():
                try:
                    receipt = json.loads(line, object_pairs_hook=unique_object)
                except (ValueError, ReleaseError):
                    continue
                if isinstance(receipt, dict) and receipt.get("State") in ("Empty", "Observed"):
                    status = run_checked(["/usr/bin/systemctl", "show", UNIT, "--property=InvocationID",
                                          "--property=ActiveState", "--property=MainPID"], timeout=5)
                    facts = dict(line.split("=", 1) for line in status.splitlines())
                    require(facts.get("InvocationID") == invocation, "DEPLOY_INVOCATION_CHANGED")
                    require(facts.get("ActiveState") == "active"
                            and re.fullmatch(r"[1-9][0-9]{0,9}", facts.get("MainPID", "")) is not None,
                            "DEPLOY_SERVICE_UNCONFIRMED")
                    return
            time.sleep(0.25)
        raise ReleaseError("DEPLOY_DATABASE_READINESS_UNCONFIRMED")
