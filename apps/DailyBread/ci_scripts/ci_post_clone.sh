#!/bin/sh
#
# Xcode Cloud — generate the Xcode project before anything tries to build it.
#
# DailyBread.xcodeproj is NOT in git: it is generated from project.yml by
# xcodegen (see apps/DailyBread/.gitignore). Xcode Cloud clones the repo, looks
# for the project named in the workflow, finds nothing, and fails instantly with
# a single error — which is why every Cloud build since the xcodegen switch has
# failed and emailed a notification.
#
# This runs after the clone and before dependency resolution, so by the time
# Xcode Cloud resolves packages and builds, the project exists.
#
# Location matters: Xcode Cloud only runs ci_scripts that sit in the SAME
# directory as the .xcodeproj — here, apps/DailyBread/. Moving this file to the
# repository root silently disables it.

set -e

# Homebrew lives here on Apple silicon runners and isn't always on PATH already.
export PATH="/opt/homebrew/bin:$PATH"

# Xcode Cloud sets this to the cloned repository root. Fall back to walking up
# from this script so the file also works when run by hand.
REPO_ROOT="${CI_PRIMARY_REPOSITORY_PATH:-$(cd "$(dirname "$0")/../../.." && pwd)}"
PROJECT_DIR="$REPO_ROOT/apps/DailyBread"

cd "$PROJECT_DIR"

if ! command -v xcodegen >/dev/null 2>&1; then
    # Skipping the auto-update keeps this to seconds rather than minutes.
    export HOMEBREW_NO_AUTO_UPDATE=1
    export HOMEBREW_NO_INSTALL_CLEANUP=1
    brew install xcodegen
fi

echo "Generating DailyBread.xcodeproj from project.yml…"
xcodegen generate

# Fail loudly here rather than letting xcodebuild report a confusing missing
# scheme two steps later.
if [ ! -d "DailyBread.xcodeproj" ]; then
    echo "error: xcodegen ran but DailyBread.xcodeproj is missing" >&2
    exit 1
fi

echo "Project generated:"
ls -d "$PROJECT_DIR/DailyBread.xcodeproj"
