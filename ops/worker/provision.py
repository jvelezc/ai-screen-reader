"""One-time operator bootstrap. Input arrives over the existing trusted root SSH channel."""
import base64
import fcntl
import grp
import hashlib
import json
import os
from pathlib import Path
import pwd
import re
import subprocess
import sys
import tempfile

BASE = Path("/srv/headless-screen-reader")
LIB = Path("/usr/local/libexec/headless-screen-reader")
MARKER = "headless-screen-reader:gpnijaxrgtwzwtrqsbcu:v1"
CA_SHA = "700723581420dd1ac98fd7e9ac529f0ef210eadcaf87fc868a3ad7d114c2f3b7"
MODULES = {"activation.py", "deploy.py", "host_runtime.py", "image_archive.py",
           "release_bundle.py", "worker_start.py", "control_runtime.py", "control_start.py"}


class ProvisionError(Exception):
    pass


def require(condition):
    if not condition:
        raise ProvisionError("PROVISION_INPUT_OR_OWNERSHIP_INVALID")


def validate(config, sources):
    required = {"Project", "Commit", "PublicKey", "Password", "Ca"}
    require(isinstance(config, dict) and set(config) in (required, required | {"PortalKey"}))
    if "PortalKey" in config:
        require(public_portal_key(config["PortalKey"]))
    require(config["Project"] == "gpnijaxrgtwzwtrqsbcu"
            and isinstance(config["Commit"], str) and re.fullmatch(r"[a-f0-9]{40}", config["Commit"]) is not None
            and isinstance(config["Password"], str) and re.fullmatch(r"[a-f0-9]{64}", config["Password"]) is not None)
    require(isinstance(config["PublicKey"], str)
            and re.fullmatch(r"ssh-ed25519 [A-Za-z0-9+/]{68}(?: [A-Za-z0-9_.-]{1,64})?", config["PublicKey"]) is not None)
    key = base64.b64decode(config["PublicKey"].split()[1], validate=True)
    require(len(key) == 51 and key[:19] == b"\x00\x00\x00\x0bssh-ed25519\x00\x00\x00\x20")
    require(isinstance(config["Ca"], str))
    certificate = base64.b64decode(config["Ca"], validate=True)
    require(hashlib.sha256(certificate).hexdigest() == CA_SHA)
    require(isinstance(sources, dict) and set(sources) == MODULES)
    require(sum(len(value.encode("utf-8")) for value in sources.values() if isinstance(value, str)) <= 256 * 1024)
    for name, source in sources.items():
        require(isinstance(source, str) and 0 < len(source.encode("utf-8")) <= 128 * 1024)
        compile(source, name, "exec")
    return certificate


def public_portal_key(key):
    # Bootstrap is standalone: the newly supplied modules are not installed yet.
    if not isinstance(key, str) or not 20 <= len(key) <= 4096 or re.fullmatch(r"[A-Za-z0-9_.-]+", key) is None:
        return False
    if key.startswith("sb_publishable_"):
        return True
    try:
        parts = key.split(".")
        if len(parts) != 3:
            return False
        def unique(pairs):
            require(len(pairs) == len(dict(pairs)))
            return dict(pairs)
        value = json.loads(base64.b64decode(parts[1] + "=" * (-len(parts[1]) % 4), altchars=b"-_", validate=True),
                           object_pairs_hook=unique)
        return isinstance(value, dict) and value.get("role") == "anon" and value.get("ref") == "gpnijaxrgtwzwtrqsbcu"
    except (ValueError, UnicodeError, ProvisionError):
        return False


def account_valid(account, name, home, managed):
    require(managed and account.pw_uid > 0 and account.pw_gid > 0 and account.pw_dir == str(home)
            and account.pw_gecos == "HeadlessScreenReader managed " + name)


def command(arguments):
    result = subprocess.run(arguments, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                            stderr=subprocess.DEVNULL, timeout=60)
    require(result.returncode == 0)


def install(path, data, mode):
    descriptor, temporary = tempfile.mkstemp(prefix=".hsr-", dir=path.parent)
    try:
        with os.fdopen(descriptor, "wb") as destination:
            destination.write(data)
            destination.flush()
            os.fsync(destination.fileno())
        os.chmod(temporary, mode)
        os.replace(temporary, path)
        parent = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
        try:
            os.fsync(parent)
        finally:
            os.close(parent)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def own_private_directory(path, uid, gid):
    path.mkdir(mode=0o700, exist_ok=True)
    # State children are app-owned: bind an actual directory before changing ownership,
    # never follow an app-created symlink during a later bootstrap repair.
    descriptor = os.open(path, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        os.fchown(descriptor, uid, gid)
        os.fchmod(descriptor, 0o700)
    finally:
        os.close(descriptor)


def provision(config, sources):
    require(os.geteuid() == 0)
    certificate = validate(config, sources)
    os.umask(0o077)
    managed = BASE.is_dir() and not BASE.is_symlink() and (BASE / ".managed").is_file()
    if managed:
        require((BASE / ".managed").read_text().strip() == MARKER
                and BASE.stat().st_uid == 0 and BASE.stat().st_mode & 0o022 == 0)
    else:
        require(not BASE.exists() and not BASE.is_symlink())
    homes = {"hsr-worker": BASE / "home", "hsr-deploy": BASE / "deploy-home"}
    accounts = {}
    for name, home in homes.items():
        try:
            account = pwd.getpwnam(name)
        except KeyError:
            continue
        account_valid(account, name, home, managed)
        require(grp.getgrgid(account.pw_gid).gr_name == name)
        accounts[name] = account
    protected = [LIB, Path("/usr/local/libexec/hsr-deploy"), Path("/etc/sudoers.d/hsr-deploy"),
                 Path("/etc/systemd/system/hsr-observation.service"), Path("/etc/systemd/system/hsr-control.service")]
    for path in protected:
        if path.exists() or path.is_symlink():
            require(managed and not path.is_symlink() and path.stat().st_uid == 0
                    and path.stat().st_mode & 0o022 == 0)
    env_path = BASE / "config" / "worker.env"
    control_env = BASE / "config" / "control.env"
    evidence_env = BASE / "config" / "evidence.env"
    if evidence_env.exists() or evidence_env.is_symlink():
        require(managed and not evidence_env.is_symlink() and evidence_env.is_file()
                and evidence_env.stat().st_uid == 0 and evidence_env.stat().st_mode & 0o077 == 0)
    if control_env.exists() or control_env.is_symlink():
        require(managed and not control_env.is_symlink() and control_env.is_file()
                and control_env.stat().st_uid == 0 and control_env.stat().st_mode & 0o077 == 0)
    connection = ("Host=db.gpnijaxrgtwzwtrqsbcu.supabase.co;Port=5432;Database=postgres;"
                  "Username=hsr_worker_runtime;Password=" + config["Password"] + ";SSL Mode=VerifyFull;"
                  "Root Certificate=/srv/headless-screen-reader/config/supabase-root.crt;"
                  "Timeout=10;Command Timeout=10;Maximum Pool Size=4;Application Name=HeadlessScreenReader.Worker")
    environment = ("HSR_WORKER_DATABASE=" + json.dumps(connection) + "\n").encode()
    if env_path.exists():
        require(not env_path.is_symlink() and env_path.stat().st_uid == 0
                and env_path.stat().st_mode & 0o077 == 0 and env_path.read_bytes() == environment)
    BASE.mkdir(mode=0o711, exist_ok=True)
    BASE.chmod(0o711)
    lock_descriptor = os.open(BASE / "deployment.lock", os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    # Kept until this one-time bootstrap process exits, including exception paths.
    fcntl.flock(lock_descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
    if not managed:
        install(BASE / ".managed", (MARKER + "\n").encode(), 0o600)
    for child, mode in {"releases": 0o755, "incoming": 0o700, "deployments": 0o700, "config": 0o755}.items():
        path = BASE / child
        require(not path.is_symlink())
        path.mkdir(mode=mode, exist_ok=True)
        require(path.stat().st_uid == 0)
        path.chmod(mode)
    for name, home in homes.items():
        if name not in accounts:
            shell = "/usr/sbin/nologin" if name == "hsr-worker" else "/bin/bash"
            command(["/usr/sbin/useradd", "--user-group", "--no-create-home", "--home-dir", str(home),
                     "--shell", shell, "--comment", "HeadlessScreenReader managed " + name, name])
            accounts[name] = pwd.getpwnam(name)
        home.mkdir(mode=0o755, exist_ok=True)
        require(not home.is_symlink() and home.stat().st_uid == 0)
        home.chmod(0o755)
    worker = accounts["hsr-worker"]
    for suffix in (".local", ".config", ".cache"):
        path = homes["hsr-worker"] / suffix
        require(not path.is_symlink())
        own_private_directory(path, worker.pw_uid, worker.pw_gid)
    for suffix in ("state", "state/evidence", "state/readiness"):
        path = BASE / suffix
        require(not path.is_symlink())
        own_private_directory(path, worker.pw_uid, worker.pw_gid)
    # useradd allocates subordinate IDs; refuse to start a privileged or unmapped fallback.
    for path in (Path("/etc/subuid"), Path("/etc/subgid")):
        require(any(line.startswith("hsr-worker:") and int(line.split(":")[2]) >= 65536
                    for line in path.read_text().splitlines()))
    LIB.mkdir(mode=0o755, exist_ok=True)
    LIB.chmod(0o755)
    for name, source in sources.items():
        install(LIB / name, source.encode("utf-8"), 0o644)
    install(Path("/usr/local/libexec/hsr-deploy"),
            b"#!/bin/sh\nexec /usr/bin/python3 -I -B /usr/local/libexec/headless-screen-reader/deploy.py\n", 0o755)
    ssh = homes["hsr-deploy"] / ".ssh"
    require(not ssh.is_symlink())
    ssh.mkdir(mode=0o755, exist_ok=True)
    require(ssh.stat().st_uid == 0)
    ssh.chmod(0o755)
    key = 'restrict,command="/usr/bin/sudo -n /usr/local/libexec/hsr-deploy" ' + config["PublicKey"] + "\n"
    install(ssh / "authorized_keys", key.encode(), 0o644)
    sudoers = ('Defaults:hsr-deploy env_keep += "SSH_ORIGINAL_COMMAND"\n'
               'hsr-deploy ALL=(root) NOPASSWD: /usr/local/libexec/hsr-deploy ""\n')
    check = BASE / "config" / "sudoers-check"
    install(check, sudoers.encode(), 0o600)
    command(["/usr/sbin/visudo", "-cf", str(check)])
    install(Path("/etc/sudoers.d/hsr-deploy"), sudoers.encode(), 0o440)
    check.unlink()
    install(BASE / "config" / "supabase-root.crt", certificate, 0o644)
    install(env_path, environment, 0o600)
    if "PortalKey" in config:
        install(control_env, ("HSR_CONTROL_PUBLISHABLE_KEY=" + config["PortalKey"] + "\n").encode(), 0o600)
    unit = ("[Unit]\nDescription=Private HeadlessScreenReader observation worker\nAfter=network-online.target\n"
            "Wants=network-online.target\n[Service]\nType=simple\nUser=hsr-worker\nGroup=hsr-worker\n"
            "UMask=0077\nWorkingDirectory=/srv/headless-screen-reader/home\n"
            "EnvironmentFile=/srv/headless-screen-reader/config/worker.env\n"
            "EnvironmentFile=-/srv/headless-screen-reader/config/evidence.env\n"
            "ExecStart=/usr/bin/python3 -I -B /usr/local/libexec/headless-screen-reader/worker_start.py\n"
            "Restart=no\nKillMode=process\nSendSIGKILL=no\nTimeoutStopSec=180\nLimitCORE=0\n"
            "[Install]\nWantedBy=multi-user.target\n")
    install(Path("/etc/systemd/system/hsr-observation.service"), unit.encode(), 0o644)
    control_unit = ("[Unit]\nDescription=Private HeadlessScreenReader workspace portal\n"
        "After=network-online.target hsr-observation.service\nRequires=hsr-observation.service\n"
        "[Service]\nType=simple\nUser=hsr-worker\nGroup=hsr-worker\nUMask=0077\n"
        "WorkingDirectory=/srv/headless-screen-reader/home\n"
        "EnvironmentFile=/srv/headless-screen-reader/config/control.env\n"
        "EnvironmentFile=-/srv/headless-screen-reader/config/evidence.env\n"
        "ExecStart=/usr/bin/python3 -I -B /usr/local/libexec/headless-screen-reader/control_start.py\n"
        "Restart=no\nKillMode=process\nSendSIGKILL=no\nTimeoutStopSec=20\nLimitCORE=0\n"
        "NoNewPrivileges=yes\nPrivateTmp=yes\nProtectSystem=strict\n"
        "[Install]\nWantedBy=multi-user.target\n")
    install(Path("/etc/systemd/system/hsr-control.service"), control_unit.encode(), 0o644)
    command(["/usr/bin/loginctl", "enable-linger", "hsr-worker"])
    command(["/usr/bin/systemctl", "start", "user@" + str(worker.pw_uid) + ".service"])
    command(["/usr/bin/systemctl", "daemon-reload"])
    # No service start or enable until an exact tested release passes native recovery/readiness.
    print(json.dumps({"Schema": "hsr-private-provision-v1", "State": "Provisioned", "Commit": config["Commit"],
                      "WorkerUid": worker.pw_uid, "PublicListenerAdded": False, "CaddyChanged": False}))


if __name__ == "__main__":
    try:
        provision(CONFIG, SOURCES)
    except Exception:
        print("PRIVATE_PROVISION_UNCONFIRMED", file=sys.stderr)
        sys.exit(3)
