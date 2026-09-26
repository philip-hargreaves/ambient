"""Downloads a translation candidate and exports it to OpenVINO.

    python export.py <id> [--weights int8|int4]

Everything lands under MT_ROOT (default D:\\clinicavt-mt): the checkpoint in hf/, the export in
models/<id>-<weights>, with provenance.json beside it. A finished export is skipped.
"""

import json
import os
import socket
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(os.environ.get("MT_ROOT", r"D:\clinicavt-mt"))
CANDIDATES = json.load(open(Path(__file__).with_name("candidates.json"), encoding="utf-8"))

# IPv6 drops on this network
_lookup = socket.getaddrinfo
socket.getaddrinfo = lambda host, port, family=0, *rest, **more: _lookup(
    host, port, socket.AF_INET, *rest, **more)


def main():
    name = sys.argv[1]
    weights = sys.argv[sys.argv.index("--weights") + 1] if "--weights" in sys.argv else "int8"
    entry = CANDIDATES[name]
    out = ROOT / "models" / f"{name}-{weights}"
    if (out / "provenance.json").exists():
        print(name, "already exported")
        return
    os.environ["HF_HOME"] = str(ROOT / "hf")
    from huggingface_hub import HfApi, snapshot_download

    started = time.time()
    info = HfApi().model_info(entry["hf"])
    revision = info.sha
    # Older checkpoints carry PyTorch weights only
    safetensors = any(f.rfilename.endswith(".safetensors") for f in info.siblings)
    weights_kind = "*.safetensors" if safetensors else "*.bin"
    # A plain folder, since exFAT has no symlinks for the cache
    source = snapshot_download(entry["hf"], revision=revision, local_dir=ROOT / "hf" / name,
                               allow_patterns=["*.json", weights_kind, "*.model", "*.py", "*.txt"])
    print(name, "downloaded in", round(time.time() - started), "s, revision", revision, flush=True)

    cli = Path(sys.executable).with_name("optimum-cli.exe")
    command = [str(cli), "export", "openvino", "--model", source,
               "--task", "text2text-generation-with-past", "--weight-format", weights, str(out)]
    started = time.time()
    subprocess.run(command, check=True)
    size = sum(f.stat().st_size for f in out.glob("*.bin"))
    json.dump({"id": name, "hf": entry["hf"], "revision": revision, "licence": entry["licence"],
               "weights": weights, "command": " ".join(command),
               "export_seconds": round(time.time() - started), "bin_bytes": size,
               "exported_at": time.strftime("%Y-%m-%dT%H:%M:%S")},
              open(out / "provenance.json", "w", encoding="utf-8"), indent=2)
    print(name, "exported,", round(size / 2 ** 20), "MB of weights", flush=True)


if __name__ == "__main__":
    main()
