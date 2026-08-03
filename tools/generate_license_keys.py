import argparse
from pathlib import Path

from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey


def main():
    parser = argparse.ArgumentParser(description="Generate offline Ed25519 key pair for MonKine licensing")
    parser.add_argument("--private-out", default="private_license_key.pem", help="Output private key PEM path")
    parser.add_argument("--public-out", default="assets/license_public_key.pem", help="Output public key PEM path")
    args = parser.parse_args()

    private_key = Ed25519PrivateKey.generate()
    public_key = private_key.public_key()

    private_bytes = private_key.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.PKCS8,
        encryption_algorithm=serialization.NoEncryption(),
    )
    public_bytes = public_key.public_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PublicFormat.SubjectPublicKeyInfo,
    )

    private_path = Path(args.private_out)
    public_path = Path(args.public_out)
    private_path.parent.mkdir(parents=True, exist_ok=True)
    public_path.parent.mkdir(parents=True, exist_ok=True)

    private_path.write_bytes(private_bytes)
    public_path.write_bytes(public_bytes)

    print(f"Private key saved: {private_path.resolve()}")
    print(f"Public key saved:  {public_path.resolve()}")


if __name__ == "__main__":
    main()
