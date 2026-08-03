import argparse
import base64
import json
import uuid
from datetime import datetime, timezone
from pathlib import Path

from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey


def canonical_payload_bytes(payload):
    return json.dumps(payload, ensure_ascii=True, sort_keys=True, separators=(",", ":")).encode("utf-8")


def main():
    parser = argparse.ArgumentParser(description="Generate signed offline license for MonKine")
    parser.add_argument("--private-key", required=True, help="PEM private key path")
    parser.add_argument("--machine", required=True, help="Machine fingerprint from activation dialog")
    parser.add_argument("--code", required=True, help="Unique client activation code")
    parser.add_argument("--client", required=True, help="Client display name")
    parser.add_argument("--expires-at", default="", help="Optional expiry ISO date, e.g. 2027-12-31T23:59:59Z")
    parser.add_argument("--out", required=True, help="Output license json path")
    args = parser.parse_args()

    private_key_bytes = Path(args.private_key).read_bytes()
    private_key = serialization.load_pem_private_key(private_key_bytes, password=None)
    if not isinstance(private_key, Ed25519PrivateKey):
        raise ValueError("Private key must be Ed25519")

    payload = {
        "version": 1,
        "license_id": str(uuid.uuid4()),
        "client_name": args.client,
        "activation_code": args.code,
        "machine_fingerprint": args.machine.lower(),
        "issued_at": datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z"),
    }
    if args.expires_at:
        payload["expires_at"] = args.expires_at

    signature = private_key.sign(canonical_payload_bytes(payload))
    blob = {
        "payload": payload,
        "signature": base64.b64encode(signature).decode("ascii"),
    }

    out_path = Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(blob, ensure_ascii=True, indent=2), encoding="utf-8")

    print(f"License file created: {out_path.resolve()}")


if __name__ == "__main__":
    main()
