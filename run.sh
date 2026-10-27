#!/usr/bin/env bash
# Agents Hub - Build & Run Pipeline (Cross-Platform)
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "========================================================"
echo " Agents Hub - Build and Launch Pipeline"
echo "========================================================"
echo ""

# ---------------------------------------------------------
# 1. Check and Build Bun Sidecar Engine
# ---------------------------------------------------------
echo "[*] Step 1/3: Checking Bun runtime and building Sidecar Engine..."

BUN_CMD=""
if command -v bun >/dev/null 2>&1; then
    BUN_CMD="bun"
elif [ -x "$HOME/.bun/bin/bun" ]; then
    BUN_CMD="$HOME/.bun/bin/bun"
fi

if [ -z "$BUN_CMD" ]; then
    echo "[!] Bun not found. Installing Bun..."
    curl -fsSL https://bun.sh/install | bash
    export PATH="$HOME/.bun/bin:$PATH"
    BUN_CMD="$HOME/.bun/bin/bun"
fi

# Detect architecture
OS_TYPE="$(uname -s | tr '[:upper:]' '[:lower:]')"
ARCH_TYPE="$(uname -m)"
TARGET="linux-x64"

if [ "$OS_TYPE" = "darwin" ]; then
    if [ "$ARCH_TYPE" = "arm64" ]; then
        TARGET="osx-arm64"
    else
        TARGET="osx-x64"
    fi
elif [ "$OS_TYPE" = "linux" ]; then
    if [ "$ARCH_TYPE" = "aarch64" ] || [ "$ARCH_TYPE" = "arm64" ]; then
        TARGET="linux-arm64"
    else
        TARGET="linux-x64"
    fi
fi

echo "[*] Compiling Sidecar Engine ($TARGET)..."
"$BUN_CMD" run "$SCRIPT_DIR/build.ts" --target="$TARGET"
echo "[OK] Sidecar Engine built successfully."
echo ""

# ---------------------------------------------------------
# 2. Check and Build React / Vite Frontend
# ---------------------------------------------------------
echo "[*] Step 2/3: Checking dependencies and building React UI..."
if [ ! -d "ui/node_modules" ]; then
    echo "[*] ui/node_modules not found. Running npm install in ui..."
    (cd ui && npm install)
fi

echo "[*] Building React UI..."
(cd ui && npm run build)
echo "[OK] React frontend built successfully."
echo ""

# ---------------------------------------------------------
# 3. Launch Agents Hub via .NET 10
# ---------------------------------------------------------
echo "[*] Step 3/3: Starting Agents Hub via dotnet run..."
echo "========================================================"
echo ""

if [ "$OS_TYPE" = "darwin" ] || [ "$OS_TYPE" = "linux" ]; then
    # Headless cross-platform host mode
    dotnet run -f net10.0 "$@"
else
    # Windows desktop GUI mode
    dotnet run -f net10.0-windows "$@"
fi
