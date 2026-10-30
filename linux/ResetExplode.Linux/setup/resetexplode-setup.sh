#!/bin/bash
# RESET EXPLODE (Linux) — configuração inicial de permissões. Roda como root via pkexec.
# Libera, só para o grupo "wheel", a leitura do consumo da CPU (RAPL) e a escrita nos ajustes da GPU AMD.
# É regravado a cada boot por systemd-tmpfiles. Para desfazer: sudo rm /etc/tmpfiles.d/resetexplode.conf e reiniciar.
set -e
U="${1:-}"
echo "→ Verificando grupo wheel"
if [ -n "$U" ] && ! id -nG "$U" | tr ' ' '\n' | grep -qx wheel; then
    echo "  adicionando $U ao grupo wheel (precisa sair e entrar na sessão depois)"
    usermod -aG wheel "$U"
fi

echo "→ Gravando /etc/tmpfiles.d/resetexplode.conf"
cat > /etc/tmpfiles.d/resetexplode.conf <<'EOF'
# RESET EXPLODE: consumo da CPU (somente leitura, grupo wheel)
z /sys/devices/virtual/powercap/intel-rapl/intel-rapl:*/energy_uj 0440 root wheel - -
z /sys/devices/virtual/powercap/intel-rapl/intel-rapl:*/intel-rapl:*:*/energy_uj 0440 root wheel - -
# RESET EXPLODE: ajustes da GPU AMD (limite de energia, clocks, nível de desempenho)
z /sys/class/drm/card*/device/power_dpm_force_performance_level 0664 root wheel - -
z /sys/class/drm/card*/device/pp_od_clk_voltage 0664 root wheel - -
z /sys/class/drm/card*/device/hwmon/hwmon*/power1_cap 0664 root wheel - -
EOF

echo "→ Aplicando agora"
systemd-tmpfiles --create /etc/tmpfiles.d/resetexplode.conf || true

echo "→ Ferramentas opcionais"
for t in smartctl dmidecode; do
    if command -v "$t" >/dev/null; then echo "  $t: ok"; else echo "  $t: não encontrado (algumas telas ficam limitadas)"; fi
done
echo "✔ Pronto."
