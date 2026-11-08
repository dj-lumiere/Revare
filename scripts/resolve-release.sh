#!/usr/bin/env bash
# Resolve the packaging version and GitHub-release branding from the pushed tag,
# writing PKG_VERSION / PKG_PREFIX / RELEASE_NAME / RELEASE_NOTES to $GITHUB_ENV for later steps.
#
# Revare's release workflow (.github/workflows/release.yaml) runs for a v* tag in the Revare repository, and the
# Mielia repository calls it for a v* tag of its own. The repository that pushed the tag picks the branding: the
# same builder artifacts, named for that language and the tag version, published with that language's notes and title. Runs from the
# workspace root that holds the projects side by side.
set -euo pipefail

tag="${GITHUB_REF_NAME:?GITHUB_REF_NAME is not set}"
repository="${GITHUB_REPOSITORY:?GITHUB_REPOSITORY is not set}"
pkg_version="${tag#v}"                            # e.g. 0.5.0

if [[ "$repository" == */Mielia ]]; then
  pkg_prefix="mielia"                            # archives are mielia-v<version>-<rid>
  release_name="Mielia ${tag}"                    # e.g. Mielia v0.5.0
  release_notes="Revare/scripts/package-assets/RELEASE_NOTES_MI.md"
else
  pkg_prefix="revare"
  release_name="Revare ${tag}"
  release_notes="Revare/scripts/package-assets/RELEASE_NOTES.md"
fi

{
  echo "PKG_VERSION=${pkg_version}"
  echo "PKG_PREFIX=${pkg_prefix}"
  echo "RELEASE_NAME=${release_name}"
  echo "RELEASE_NOTES=${release_notes}"
} >> "${GITHUB_ENV:?GITHUB_ENV is not set}"

echo "Resolved: repository=${repository} tag=${tag} PKG_VERSION=${pkg_version} PKG_PREFIX=${pkg_prefix} RELEASE_NAME='${release_name}' RELEASE_NOTES=${release_notes}"
