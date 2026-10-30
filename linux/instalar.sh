#!/bin/bash
# RESET EXPLODE (Linux) — instala para o usuário atual, sem root.
#   ./instalar.sh            instala (ou atualiza)
#   ./instalar.sh --remover  remove o app, o atalho e o ícone
# O binário vem de ./resetexplode (pacote da release) ou de ./ResetExplode.Linux/publish/resetexplode (compilado).
set -e
cd "$(dirname "$0")"

APP="$HOME/.local/share/resetexplode"
BIN="$HOME/.local/bin/resetexplode"
DESK="$HOME/.local/share/applications/resetexplode.desktop"
ICON="$HOME/.local/share/icons/hicolor/256x256/apps/resetexplode.png"

if [ "${1:-}" = "--remover" ]; then
    rm -rf "$APP" "$BIN" "$DESK" "$ICON"
    command -v update-desktop-database >/dev/null && update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true
    echo "Removido. As permissões do sistema continuam; para desfazer: sudo rm /etc/tmpfiles.d/resetexplode.conf e reinicie."
    exit 0
fi

SRC=""
for c in ./resetexplode ./ResetExplode.Linux/publish/resetexplode; do [ -x "$c" ] && SRC="$c" && break; done
[ -n "$SRC" ] || { echo "Não achei o executável. Baixe o pacote da release ou compile (veja README.md)."; exit 1; }

PNG=""
for c in ./resetexplode.png ./ResetExplode.Linux/Assets/logo.png; do [ -f "$c" ] && PNG="$c" && break; done

mkdir -p "$APP" "$(dirname "$BIN")" "$(dirname "$DESK")" "$(dirname "$ICON")"
install -m 755 "$SRC" "$APP/resetexplode"
[ -n "$PNG" ] && install -m 644 "$PNG" "$ICON"
ln -sf "$APP/resetexplode" "$BIN"

cat > "$DESK" <<EOF
[Desktop Entry]
Type=Application
Name=RESET EXPLODE
Comment=Informações, sensores, stress e benchmarks de hardware
Exec=$APP/resetexplode
Icon=resetexplode
Terminal=false
Categories=System;Monitor;
StartupWMClass=resetexplode
EOF
command -v update-desktop-database >/dev/null && update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true

echo "Instalado. Procure por \"RESET EXPLODE\" no menu de aplicativos ou rode: resetexplode"
echo "Na primeira abertura o app pede as permissões (uma vez)."
