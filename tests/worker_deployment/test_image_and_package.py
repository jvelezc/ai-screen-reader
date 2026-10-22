import hashlib
import io
import json
from pathlib import Path
import sys
import tarfile
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "ops" / "worker"))
from build_release import package
from image_archive import verify_image_archive
from release_bundle import ReleaseError, extract_verified, verify_directory

COMMIT = "a" * 40
CONFIG = b'{"architecture":"amd64","os":"linux"}'
IMAGE = "sha256:" + hashlib.sha256(CONFIG).hexdigest()


def image_archive(path, *, config=CONFIG, manifest=None, entries=None):
    if manifest is None:
        manifest = [{"Config": "config.json", "Layers": [], "RepoTags": []}]
    files = entries if entries is not None else [("config.json", config), ("manifest.json", json.dumps(manifest).encode())]
    with tarfile.open(path, mode="w") as archive:
        for name, data in files:
            item = tarfile.TarInfo(name)
            item.size = len(data)
            archive.addfile(item, io.BytesIO(data))


class ImageAndPackageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="hsr-image-package-proof-")
        self.root = Path(self.temporary.name)
        self.stage = self.root / "content"
        self.worker = self.stage / "worker"
        self.worker.mkdir(parents=True)
        self.tested = self.root / "tested"
        self.tested.mkdir()
        for name, data in {"HeadlessScreenReader.Worker.dll": b"tested worker",
                           "HeadlessScreenReader.Worker.runtimeconfig.json": b"{}",
                           "HeadlessScreenReader.Worker.deps.json": b"{}"}.items():
            (self.worker / name).write_bytes(data)
            (self.tested / name).write_bytes(data)
        self.image = self.stage / "fixture.tar"
        image_archive(self.image)
        self.output = self.root / "release.tar.gz"

    def tearDown(self):
        self.temporary.cleanup()

    def test_actual_image_configuration_hash_matches_declared_immutable_id(self):
        verify_image_archive(self.image, IMAGE)
        with self.assertRaises(ReleaseError) as caught:
            verify_image_archive(self.image, "sha256:" + "b" * 64)
        self.assertEqual("RELEASE_IMAGE_CONTENT_MISMATCH", str(caught.exception))

    def test_different_archive_cannot_reuse_an_image_already_cached_on_the_host(self):
        image_archive(self.image, config=b'{"architecture":"different"}')
        with self.assertRaises(ReleaseError) as caught:
            verify_image_archive(self.image, IMAGE)
        self.assertEqual("RELEASE_IMAGE_CONTENT_MISMATCH", str(caught.exception))

    def test_multiple_images_cannot_silently_choose_one_cached_identity(self):
        image_archive(self.image, manifest=[{"Config": "config.json"}, {"Config": "other.json"}])
        with self.assertRaises(ReleaseError) as caught:
            verify_image_archive(self.image, IMAGE)
        self.assertEqual("RELEASE_IMAGE_ARCHIVE_INVALID", str(caught.exception))

    def test_duplicate_inner_member_cannot_change_the_selected_configuration(self):
        image_archive(self.image, entries=[("config.json", CONFIG), ("config.json", b"different")])
        with self.assertRaises(ReleaseError) as caught:
            verify_image_archive(self.image, IMAGE)
        self.assertEqual("RELEASE_IMAGE_ARCHIVE_INVALID", str(caught.exception))

    def test_foreign_configuration_path_is_rejected(self):
        image_archive(self.image, manifest=[{"Config": "../config.json"}])
        with self.assertRaises(ReleaseError) as caught:
            verify_image_archive(self.image, IMAGE)
        self.assertEqual("RELEASE_IMAGE_ARCHIVE_INVALID", str(caught.exception))

    def test_exact_tested_worker_and_image_roundtrip_through_compressed_bundle(self):
        manifest = package(self.stage, self.output, COMMIT, IMAGE, self.tested)
        destination = self.root / "readback"
        destination.mkdir()
        with self.output.open("rb") as source:
            self.assertEqual(manifest, extract_verified(source, destination, COMMIT))
        self.assertEqual(b"tested worker", (destination / "worker" / "HeadlessScreenReader.Worker.dll").read_bytes())
        self.assertEqual(self.image.read_bytes(), (destination / "fixture.tar").read_bytes())
        self.assertEqual(manifest, verify_directory(destination, COMMIT))
        verify_image_archive(destination / "fixture.tar", IMAGE)

    def add_control(self):
        control = self.stage / "control"
        tested = self.root / "tested-control"
        for root in (control, tested):
            (root / "nested").mkdir(parents=True)
            for name, data in {"HeadlessScreenReader.Cli.dll": b"executed portal",
                "HeadlessScreenReader.Cli.runtimeconfig.json": b"{}", "HeadlessScreenReader.Cli.deps.json": b"{}",
                "nested/required.dll": b"executed dependency"}.items():
                (root / name).write_bytes(data)
        return control, tested

    def test_v2_binds_every_executed_control_byte_and_keeps_v1_worker_compatible(self):
        control, tested = self.add_control()
        receipt = package(self.stage, self.output, COMMIT, IMAGE, self.tested, tested)
        self.assertEqual("hsr-worker-control-release-v2", receipt["schema"])
        target = self.root / "readback"
        target.mkdir()
        with self.output.open("rb") as source:
            self.assertEqual(receipt, extract_verified(source, target, COMMIT))
        for path in control.rglob("*"):
            if path.is_file():
                self.assertEqual(path.read_bytes(), (target / "control" / path.relative_to(control)).read_bytes())
        self.assertEqual(receipt, verify_directory(target, COMMIT))

    def test_control_cannot_enter_a_bundle_without_an_executed_closure(self):
        self.add_control()
        with self.assertRaises(ReleaseError) as caught:
            package(self.stage, self.output, COMMIT, IMAGE, self.tested)
        self.assertEqual("RELEASE_CONTROL_NOT_TESTED", str(caught.exception))
        self.assertFalse(self.output.exists())

    def test_changed_control_dependency_cannot_enter_release(self):
        control, tested = self.add_control()
        (control / "nested" / "required.dll").write_bytes(b"untested change")
        with self.assertRaises(ReleaseError) as caught:
            package(self.stage, self.output, COMMIT, IMAGE, self.tested, tested)
        self.assertEqual("RELEASE_TESTED_CONTROL_MISMATCH", str(caught.exception))
        self.assertFalse(self.output.exists())

    def test_omitted_executed_control_dependency_cannot_create_an_incomplete_release(self):
        control, tested = self.add_control()
        (control / "nested" / "required.dll").unlink()
        with self.assertRaises(ReleaseError) as caught:
            package(self.stage, self.output, COMMIT, IMAGE, self.tested, tested)
        self.assertEqual("RELEASE_TESTED_CONTROL_CLOSURE_MISMATCH", str(caught.exception))
        self.assertFalse(self.output.exists())

    def test_unexecuted_control_dependency_cannot_be_added(self):
        control, tested = self.add_control()
        (control / "unexecuted.dll").write_bytes(b"unexpected dependency")
        with self.assertRaises(ReleaseError) as caught:
            package(self.stage, self.output, COMMIT, IMAGE, self.tested, tested)
        self.assertEqual("RELEASE_TESTED_CONTROL_MISSING", str(caught.exception))
        self.assertFalse(self.output.exists())

    def test_untested_worker_byte_change_cannot_enter_release(self):
        (self.worker / "HeadlessScreenReader.Worker.dll").write_bytes(b"untested replacement")
        with self.assertRaises(ReleaseError) as caught:
            package(self.stage, self.output, COMMIT, IMAGE, self.tested)
        self.assertEqual("RELEASE_TESTED_BINARY_MISMATCH", str(caught.exception))
        self.assertFalse(self.output.exists())

    def test_worker_dependency_missing_from_tested_closure_cannot_enter_release(self):
        (self.worker / "untested.dll").write_bytes(b"extra dependency")
        with self.assertRaises(ReleaseError) as caught:
            package(self.stage, self.output, COMMIT, IMAGE, self.tested)
        self.assertEqual("RELEASE_TESTED_BINARY_MISSING", str(caught.exception))
        self.assertFalse(self.output.exists())

    def test_existing_release_artifact_is_never_overwritten(self):
        self.output.write_bytes(b"retain existing artifact")
        with self.assertRaises(ReleaseError) as caught:
            package(self.stage, self.output, COMMIT, IMAGE, self.tested)
        self.assertEqual("RELEASE_OUTPUT_EXISTS", str(caught.exception))
        self.assertEqual(b"retain existing artifact", self.output.read_bytes())

    def test_omitted_tested_dependency_cannot_create_an_incomplete_release(self):
        (self.worker / "HeadlessScreenReader.Worker.deps.json").unlink()
        with self.assertRaises(ReleaseError) as caught:
            package(self.stage, self.output, COMMIT, IMAGE, self.tested)
        self.assertEqual("RELEASE_TESTED_CLOSURE_MISMATCH", str(caught.exception))
        self.assertFalse(self.output.exists())

    def test_outer_archive_cannot_include_itself_inside_release(self):
        with self.assertRaises(ReleaseError) as caught:
            package(self.stage, self.stage / "release.tar.gz", COMMIT, IMAGE, self.tested)
        self.assertEqual("RELEASE_OUTPUT_INSIDE_STAGE", str(caught.exception))

    def test_altered_existing_release_is_rejected_before_reuse(self):
        package(self.stage, self.output, COMMIT, IMAGE, self.tested)
        (self.worker / "HeadlessScreenReader.Worker.dll").write_bytes(b"changed after verification")
        with self.assertRaises(ReleaseError) as caught:
            verify_directory(self.stage, COMMIT)
        self.assertEqual("RELEASE_DIGEST_MISMATCH", str(caught.exception))

    def test_existing_release_symlink_cannot_follow_an_external_resource(self):
        package(self.stage, self.output, COMMIT, IMAGE, self.tested)
        sentinel = self.root / "unrelated"
        sentinel.write_bytes(b"retain unrelated bytes")
        (self.worker / "unsafe").symlink_to(sentinel)
        with self.assertRaises(ReleaseError) as caught:
            verify_directory(self.stage, COMMIT)
        self.assertEqual("RELEASE_TYPE_INVALID", str(caught.exception))
        self.assertEqual(b"retain unrelated bytes", sentinel.read_bytes())


if __name__ == "__main__":
    unittest.main()
