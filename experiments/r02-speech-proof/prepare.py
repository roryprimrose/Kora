"""Stage only the reviewed sherpa artifacts. Network is setup-only, never inference."""

import hashlib
import json
from pathlib import Path
import shutil
import tarfile
import urllib.request

import sentencepiece

ROOT = Path(__file__).resolve().parent
ASSETS = ROOT / "assets"
MODEL_NAME = "sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01"
MODEL_URL = (
    "https://github.com/k2-fsa/sherpa-onnx/releases/download/kws-models/"
    + MODEL_NAME + ".tar.bz2"
)
ARCHIVE_SHA256 = "f170013b4716e41b62b9bfd809687c207cef798ef9bc6534d524e17af9b6561a"
STEM = "-epoch-12-avg-2-chunk-16-left-64"
MODEL_FILES = (
    "bpe.model", "tokens.txt", "README.md",
    "encoder" + STEM + ".int8.onnx",
    "decoder" + STEM + ".onnx",
    "joiner" + STEM + ".int8.onnx",
)


def digest(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main() -> None:
    ASSETS.mkdir(exist_ok=True)
    archive = ASSETS / "sherpa-kws.tar.bz2"
    if not archive.exists():
        temporary = ASSETS / "sherpa-kws.download"
        urllib.request.urlretrieve(MODEL_URL, temporary)
        if digest(temporary) != ARCHIVE_SHA256:
            raise ValueError("Model archive digest mismatch; refusing to install")
        temporary.replace(archive)
    if digest(archive) != ARCHIVE_SHA256:
        raise ValueError("Model archive digest mismatch; refusing to extract")
    model_dir = ASSETS / MODEL_NAME
    model_dir.mkdir(exist_ok=True)
    # Do not extract upstream audio or arbitrary archive paths.
    with tarfile.open(archive, "r:bz2") as bundle:
        for name in MODEL_FILES:
            member = bundle.getmember(MODEL_NAME + "/" + name)
            if not member.isfile():
                raise ValueError(f"Expected a regular model file: {name}")
            with bundle.extractfile(member) as source, (model_dir / name).open("wb") as target:
                shutil.copyfileobj(source, target)
    tokenizer = sentencepiece.SentencePieceProcessor(model_file=str(model_dir / "bpe.model"))
    pieces = tokenizer.encode("KORA", out_type=str)
    vocabulary = {line.split()[0] for line in (model_dir / "tokens.txt").read_text("utf-8").splitlines()}
    if not pieces or any(piece not in vocabulary or piece == "<unk>" for piece in pieces):
        raise ValueError("KORA cannot be encoded with this model vocabulary")
    keyword_file = model_dir / "kora.txt"
    keyword_file.write_text(" ".join(pieces) + " @KORA\n", encoding="utf-8")
    receipt = {
        "url": MODEL_URL,
        "archive_sha256": ARCHIVE_SHA256,
        "keyword": "KORA",
        "bpe_pieces": pieces,
        "files": {name: digest(model_dir / name) for name in (*MODEL_FILES, "kora.txt")},
        "note": "Archive hash recorded on 2026-10-05; upstream release exposes no signed digest",
    }
    (ASSETS / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(receipt, indent=2))


if __name__ == "__main__":
    main()
