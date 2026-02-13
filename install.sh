#!/bin/sh
# Install script for package-update-skill native binary
# Usage: curl -fsSL https://raw.githubusercontent.com/seiggy/package-update-skill/master/install.sh | sh

set -e

REPO="seiggy/package-update-skill"
INSTALL_DIR="${INSTALL_DIR:-/usr/local/bin}"
BINARY_NAME="package-update-skill"

# Detect OS and architecture
OS="$(uname -s)"
ARCH="$(uname -m)"

case "$OS" in
    Linux)  PLATFORM="linux" ;;
    Darwin) PLATFORM="osx" ;;
    *)      echo "Error: Unsupported OS: $OS"; exit 1 ;;
esac

case "$ARCH" in
    x86_64|amd64) ARCH="x64" ;;
    aarch64|arm64) ARCH="arm64" ;;
    *)             echo "Error: Unsupported architecture: $ARCH"; exit 1 ;;
esac

ASSET_NAME="package-update-skill-${PLATFORM}-${ARCH}.tar.gz"

# Get latest release tag
if [ -z "$VERSION" ]; then
    VERSION=$(curl -fsSL "https://api.github.com/repos/${REPO}/releases/latest" | grep '"tag_name"' | sed -E 's/.*"([^"]+)".*/\1/')
    if [ -z "$VERSION" ]; then
        echo "Error: Could not determine latest version"
        exit 1
    fi
fi

DOWNLOAD_URL="https://github.com/${REPO}/releases/download/${VERSION}/${ASSET_NAME}"

echo "Installing ${BINARY_NAME} ${VERSION} (${PLATFORM}-${ARCH})..."
echo "  From: ${DOWNLOAD_URL}"
echo "  To:   ${INSTALL_DIR}/${BINARY_NAME}"

# Download and extract
TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT

curl -fsSL "$DOWNLOAD_URL" -o "${TMP_DIR}/${ASSET_NAME}"
tar -xzf "${TMP_DIR}/${ASSET_NAME}" -C "$TMP_DIR"
chmod +x "${TMP_DIR}/${BINARY_NAME}"

# Install (try without sudo first)
if [ -w "$INSTALL_DIR" ]; then
    mv "${TMP_DIR}/${BINARY_NAME}" "${INSTALL_DIR}/${BINARY_NAME}"
else
    echo "  (requires sudo for ${INSTALL_DIR})"
    sudo mv "${TMP_DIR}/${BINARY_NAME}" "${INSTALL_DIR}/${BINARY_NAME}"
fi

echo "Done! Run '${BINARY_NAME} --help' to get started."
