"""Resolve one exact successful main CI run before reading deployment credentials."""
import argparse
import json
from pathlib import Path
import re
import subprocess

REQUIRED = {"Leased observation worker (Linux)", "Durable scan admission and worker leases (Linux)",
            "Isolated public MCP prototype (Linux)", "Build and test (windows-latest)", "Build and test (ubuntu-24.04)"}
REQUIRED |= {"Headed Chromium and Xvfb (" + scenario + ", Linux)"
             for scenario in ("controller", "synthetic-handoff", "private-pixels", "private-client")}


def require(condition):
    if not condition:
        raise ValueError("PRIVATE_RELEASE_CI_UNCONFIRMED")


def select_release(run, workflow, repository, head, jobs, artifacts):
    require(run.get("workflow_id") == workflow.get("id") and workflow.get("path") == ".github/workflows/verify.yml"
            and run.get("name") == "CI" and run.get("status") == "completed" and run.get("conclusion") == "success"
            and run.get("head_branch") == "main" and run.get("event") in ("push", "workflow_dispatch")
            and run.get("head_repository", {}).get("full_name") == repository)
    sha = run.get("head_sha")
    require(isinstance(sha, str) and re.fullmatch(r"[a-f0-9]{40}", sha) is not None)
    if sha != head:
        return {"deploy": "false"}
    require(REQUIRED <= {job.get("name") for job in jobs}
            and all(job.get("status") == "completed" and job.get("conclusion") == "success" for job in jobs))
    candidates = [artifact for artifact in artifacts if artifact.get("name") == "private-worker-release"]
    require(len(candidates) == 1 and candidates[0].get("expired") is False
            and type(candidates[0].get("id")) is int and candidates[0]["id"] > 0)
    return {"deploy": "true", "sha": sha, "artifact": str(candidates[0]["id"])}


def api(path):
    result = subprocess.run(["gh", "api", path], capture_output=True, text=True, timeout=30)
    require(result.returncode == 0 and len(result.stdout) <= 1024**2)
    return json.loads(result.stdout)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repository", required=True)
    parser.add_argument("--run", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    require(re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", args.repository) is not None
            and re.fullmatch(r"[1-9][0-9]{0,19}", args.run) is not None)
    root = "repos/" + args.repository
    run = api(root + "/actions/runs/" + args.run)
    workflow = api(root + "/actions/workflows/verify.yml")
    head = api(root + "/git/ref/heads/main")["object"]["sha"]
    jobs = api(root + "/actions/runs/" + args.run + "/jobs?per_page=100")
    artifacts = api(root + "/actions/runs/" + args.run + "/artifacts?per_page=100")
    require(jobs["total_count"] == len(jobs["jobs"]) and artifacts["total_count"] == len(artifacts["artifacts"]))
    selected = select_release(run, workflow, args.repository, head, jobs["jobs"], artifacts["artifacts"])
    with Path(args.output).open("a", encoding="utf-8") as destination:
        for key, value in selected.items():
            destination.write(key + "=" + value + "\n")


if __name__ == "__main__":
    main()
