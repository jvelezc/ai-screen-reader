import hashlib
import io
import json
import os
from pathlib import Path
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "ops" / "worker"))
import release_bundle as bundle

COMMIT = "a" * 40
IMAGE = "sha256:" + "b" * 64


class ReleaseBundleTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="hsr-release-proof-")
        self.root = Path(self.temporary.name)
        self.stage = self.root / "incoming"
        self.stage.mkdir()
        self.sentinel = self.root / "sentinel"
        self.sentinel.write_bytes(b"unrelated operator bytes")
        self.files = {
            "fixture.tar": b"fixed image archive",
            "worker/HeadlessScreenReader.Worker.dll": b"worker payload",
            "worker/HeadlessScreenReader.Worker.runtimeconfig.json": b"{}",
            "worker/HeadlessScreenReader.Worker.deps.json": b"{}",
        }

    def tearDown(self):
        self.assertEqual(b"unrelated operator bytes", self.sentinel.read_bytes())
        self.temporary.cleanup()

    def manifest(self, files=None):
        return {"schema": "hsr-worker-release-v1", "commit": COMMIT, "image": IMAGE,
                "files": {name: hashlib.sha256(data).hexdigest()
                          for name, data in (files or self.files).items()}}

    def archive(self, entries=None, manifest=None):
        entries = list((entries if entries is not None else self.files).items())
        if manifest is None:
            manifest = json.dumps(self.manifest()).encode()
        stream = io.BytesIO()
        with tarfile.open(fileobj=stream, mode="w", format=tarfile.PAX_FORMAT) as archive:
            for name, data in entries + [("manifest.json", manifest)]:
                member = tarfile.TarInfo(name)
                member.size = len(data)
                member.mode = 0o777  # Input permissions never authorize executable output.
                archive.addfile(member, io.BytesIO(data))
        stream.seek(0)
        return stream

    def reject(self, stream, code, commit=COMMIT):
        with self.assertRaises(bundle.ReleaseError) as caught:
            bundle.extract_verified(stream, self.stage, commit)
        self.assertEqual(code, str(caught.exception))

    def test_exact_release_hashes_and_bytes_are_verified_without_input_permissions(self):
        receipt = bundle.extract_verified(self.archive(), self.stage, COMMIT)
        self.assertEqual("hsr-worker-release-v1", receipt["schema"])
        self.assertEqual(COMMIT, receipt["commit"])
        self.assertEqual(IMAGE, receipt["image"])
        self.assertEqual(set(self.files), set(receipt["files"]))
        for name, expected in self.files.items():
            self.assertEqual(expected, (self.stage / name).read_bytes())
            self.assertEqual(0, os.stat(self.stage / name).st_mode & 0o111)

    def test_paths_cannot_escape_or_rename_operator_resources(self):
        cases = {"absolute": "/sentinel", "parent": "../sentinel", "embedded_parent": "worker/../sentinel",
                 "dot": "worker/./payload", "empty_part": "worker//payload", "backslash": r"worker\payload",
                 "drive": "C:/sentinel", "unicode": "worker/\u2603", "long": "worker/" + "x" * 240}
        for label, name in cases.items():
            with self.subTest(case=label):
                self.reject(self.archive({name: b"must not write"}), "RELEASE_PATH_INVALID")
                self.assertEqual([], list(self.stage.iterdir()))

    def test_unknown_top_level_file_is_rejected(self):
        self.reject(self.archive({"config.env": b"not a release file"}), "RELEASE_FILE_UNEXPECTED")

    def test_path_length_accepts_exact_boundary_and_rejects_next_character(self):
        name = "worker/" + "a" * 233
        self.assertEqual(name, str(bundle.safe_name(name)))
        with self.assertRaises(bundle.ReleaseError) as caught:
            bundle.safe_name(name + "a")
        self.assertEqual("RELEASE_PATH_INVALID", str(caught.exception))

    def test_links_directories_devices_and_fifo_never_authorize_extraction(self):
        for label, kind in {"symlink": tarfile.SYMTYPE, "hardlink": tarfile.LNKTYPE,
                            "directory": tarfile.DIRTYPE, "device": tarfile.CHRTYPE,
                            "fifo": tarfile.FIFOTYPE}.items():
            with self.subTest(case=label):
                member = tarfile.TarInfo("worker/unsafe")
                member.type = kind
                member.linkname = "../sentinel"
                stream = io.BytesIO(member.tobuf(format=tarfile.USTAR_FORMAT) + bytes(1024))
                self.reject(stream, "RELEASE_TYPE_INVALID")
                self.assertEqual([], list(self.stage.iterdir()))

    def test_duplicate_member_cannot_overwrite_first_bytes(self):
        stream = io.BytesIO()
        with tarfile.open(fileobj=stream, mode="w") as archive:
            for data in (b"original", b"replacement"):
                member = tarfile.TarInfo("fixture.tar")
                member.size = len(data)
                archive.addfile(member, io.BytesIO(data))
        stream.seek(0)
        self.reject(stream, "RELEASE_DUPLICATE_OR_COUNT")
        self.assertEqual(b"original", (self.stage / "fixture.tar").read_bytes())

    def test_empty_and_oversized_members_fail_before_content_is_read(self):
        for label, size in {"empty": 0, "over_limit": bundle.MAX_FILE + 1}.items():
            with self.subTest(case=label):
                member = tarfile.TarInfo("fixture.tar")
                member.size = size
                self.reject(io.BytesIO(member.tobuf() + bytes(1024)), "RELEASE_SIZE_INVALID")
                self.assertEqual([], list(self.stage.iterdir()))

    def test_total_budget_accepts_boundary_and_rejects_one_byte_over(self):
        total = sum(map(len, self.files.values())) + len(json.dumps(self.manifest()).encode())
        with patch.object(bundle, "MAX_TOTAL", total - 1):
            self.reject(self.archive(), "RELEASE_SIZE_INVALID")
        # A separate empty operator stage is required after any failed partial extraction.
        exact = self.root / "exact"
        exact.mkdir()
        with patch.object(bundle, "MAX_TOTAL", total):
            self.assertEqual(COMMIT, bundle.extract_verified(self.archive(), exact, COMMIT)["commit"])

    def test_file_count_budget_rejects_additional_member(self):
        with patch.object(bundle, "MAX_FILES", len(self.files)):
            self.reject(self.archive(), "RELEASE_DUPLICATE_OR_COUNT")

    def test_stage_must_be_empty_and_existing_bytes_are_preserved(self):
        (self.stage / "existing").write_bytes(b"retain")
        self.reject(self.archive(), "RELEASE_STAGE_NOT_EMPTY")
        self.assertEqual(b"retain", (self.stage / "existing").read_bytes())

    def test_manifest_and_requested_commit_must_match(self):
        self.reject(self.archive(), "RELEASE_IDENTITY_MISMATCH", "c" * 40)

    def test_invalid_requested_commit_is_rejected_before_writing(self):
        for label, value in {"short": "a" * 39, "long": "a" * 41, "uppercase": "A" * 40,
                             "shell": "$(id)"}.items():
            with self.subTest(case=label):
                self.reject(self.archive(), "RELEASE_COMMIT_INVALID", value)
                self.assertEqual([], list(self.stage.iterdir()))

    def test_changed_bytes_cannot_match_the_retained_manifest(self):
        changed = self.files | {"fixture.tar": b"changed image archive"}
        self.reject(self.archive(changed), "RELEASE_DIGEST_MISMATCH")

    def test_missing_runtime_is_rejected_even_with_matching_file_hashes(self):
        files = self.files.copy()
        del files["worker/HeadlessScreenReader.Worker.deps.json"]
        self.reject(self.archive(files, json.dumps(self.manifest(files)).encode()), "RELEASE_RUNTIME_MISSING")

    def test_extra_payload_without_manifest_binding_is_rejected(self):
        self.reject(self.archive(self.files | {"worker/extra": b"extra"}), "RELEASE_FILES_MISMATCH")

    def test_manifest_unknown_field_is_rejected(self):
        manifest = self.manifest() | {"command": "must never execute"}
        self.reject(self.archive(manifest=json.dumps(manifest).encode()), "RELEASE_MANIFEST_INVALID")

    def test_duplicate_manifest_key_is_rejected(self):
        manifest = json.dumps(self.manifest()).replace('"schema":', '"schema":"duplicate","schema":', 1)
        self.reject(self.archive(manifest=manifest.encode()), "RELEASE_JSON_DUPLICATE")

    def test_mutable_image_reference_is_rejected(self):
        manifest = self.manifest() | {"image": "latest"}
        self.reject(self.archive(manifest=json.dumps(manifest).encode()), "RELEASE_IMAGE_INVALID")

    def test_invalid_manifest_root_cannot_become_a_release(self):
        self.reject(self.archive(manifest=b"[]"), "RELEASE_MANIFEST_INVALID")


if __name__ == "__main__":
    unittest.main()
