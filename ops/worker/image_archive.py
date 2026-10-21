"""Bind a single Docker archive's actual configuration bytes to its image ID."""
import hashlib
import json
from pathlib import PurePosixPath
import tarfile
from release_bundle import IMAGE, require, unique_object


def verify_image_archive(path, image):
    require(IMAGE.fullmatch(image) is not None, "RELEASE_IMAGE_INVALID")
    with tarfile.open(path, mode="r:*") as archive:
        members = {}
        for member in archive:
            require(member.name not in members and len(members) < 4096,
                    "RELEASE_IMAGE_ARCHIVE_INVALID")
            members[member.name] = member
        def read(name):
            member = members.get(name)
            require(member is not None and member.isreg() and not member.issparse()
                    and 0 < member.size <= 1024**2, "RELEASE_IMAGE_ARCHIVE_INVALID")
            return archive.extractfile(member).read(1024**2 + 1)
        manifest = json.loads(read("manifest.json"), object_pairs_hook=unique_object)
        require(isinstance(manifest, list) and len(manifest) == 1
                and isinstance(manifest[0], dict), "RELEASE_IMAGE_ARCHIVE_INVALID")
        config = manifest[0].get("Config")
        require(isinstance(config, str) and not config.startswith("/") and "\\" not in config
                and all(part not in ("", ".", "..") for part in config.split("/")),
                "RELEASE_IMAGE_ARCHIVE_INVALID")
        require(str(PurePosixPath(config)) == config
                and "sha256:" + hashlib.sha256(read(config)).hexdigest() == image,
                "RELEASE_IMAGE_CONTENT_MISMATCH")
