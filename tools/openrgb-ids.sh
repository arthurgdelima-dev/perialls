#!/bin/sh
# Gera drivers/openrgb.json: os códigos USB (VID/PID) dos aparelhos que o OpenRGB suporta.
# A lista sai do arquivo de regras do udev que vem com o OpenRGB (60-openrgb.rules).
#
#   sh tools/openrgb-ids.sh /usr/lib/udev/rules.d/60-openrgb.rules "OpenRGB 1.1" > drivers/openrgb.json
#
# Depois, atualize o sha256 de "openrgb" em drivers/index.json (sha256sum drivers/openrgb.json).
rules="$1"
origem="$2"
ids=$(grep -o 'idVendor}=="[0-9a-fA-F]\{4\}", ATTRS{idProduct}=="[0-9a-fA-F]\{4\}"' "$rules" \
  | sed 's/idVendor}=="\(....\)", ATTRS{idProduct}=="\(....\)"/VID_\1\&PID_\2/' \
  | tr 'a-f' 'A-F' | sort -u)
printf '{\n  "source": "%s",\n  "devices": [\n' "$origem"
echo "$ids" | sed 's/.*/    "&"/' | sed '$!s/$/,/'
printf '  ]\n}\n'
