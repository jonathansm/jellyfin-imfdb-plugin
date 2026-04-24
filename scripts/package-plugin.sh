#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-0.1.3.0}"
CONFIGURATION="${CONFIGURATION:-Release}"
PROJECT="Jellyfin.Plugin.Imfdb/Jellyfin.Plugin.Imfdb.csproj"
PUBLISH_DIR="Jellyfin.Plugin.Imfdb/bin/${CONFIGURATION}/net9.0/publish"
ARTIFACTS_DIR="artifacts"
ZIP_NAME="jellyfin-plugin-imfdb_${VERSION}.zip"
ROOT_DIR="$(pwd)"

rm -rf "${ARTIFACTS_DIR}"
mkdir -p "${ARTIFACTS_DIR}"

dotnet publish "${PROJECT}" -c "${CONFIGURATION}" -p:Version="${VERSION}" -p:AssemblyVersion="${VERSION}" -p:FileVersion="${VERSION}"

(
  cd "${PUBLISH_DIR}"
  zip -9 "${ROOT_DIR}/${ARTIFACTS_DIR}/${ZIP_NAME}" \
    Jellyfin.Plugin.Imfdb.dll \
    Jellyfin.Plugin.Imfdb.xml \
    Jellyfin.Plugin.Imfdb.deps.json
)

if command -v md5sum >/dev/null 2>&1; then
  md5sum "${ARTIFACTS_DIR}/${ZIP_NAME}" | awk '{print $1}' > "${ARTIFACTS_DIR}/${ZIP_NAME}.md5"
else
  md5 -q "${ARTIFACTS_DIR}/${ZIP_NAME}" > "${ARTIFACTS_DIR}/${ZIP_NAME}.md5"
fi

echo "Created ${ARTIFACTS_DIR}/${ZIP_NAME}"
echo "MD5 $(cat "${ARTIFACTS_DIR}/${ZIP_NAME}.md5")"
