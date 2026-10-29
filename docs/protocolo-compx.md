# Protocolo HID dos mouses Compx (ATK, VXE, MAD R)

Como o driver oficial conversa com os mouses de chip Compx, para usarmos no Perialls
([`BuiltIn/CompxMouse.cs`](../BuiltIn/CompxMouse.cs)).

**De onde veio:** leitura do código JavaScript do driver web oficial ATK V HUB (https://hub.atk.pro,
versão 3.2.29, arquivo `filter-BRWy5Cag.js`, baixado em 29/10/2026). **Ainda não foi conferido com
capturas de um mouse de verdade.** Quem tiver um desses mouses pode confirmar com
[tools/hid-espiao.js](../tools/hid-espiao.js) aberto no hub.atk.pro.

## Aparelhos

| | |
|---|---|
| VID | `0x373B` (ATK/VXE) ou `0x3554` (Compx) |
| Interface de configuração | usage page `0xFF04` (em alguns, `0xFF02`), usage `0x02` |
| Modelos | 165 no driver oficial: VXE R1 (todas as versões), ATK X1, F1, A9, U2, Z1, MAD R... |
| VID/PID | Só os 206 que o driver oficial liga a esta família (mouses e receptores), listados em `CompxMouse.HardwareIds`. Os teclados ATK usam o mesmo VID com outros protocolos e ficam de fora |

O mesmo PID pode ser o mouse no cabo ou o receptor. O modelo e o sensor vêm do comando `0x10` (CID/MID)
e de uma tabela copiada do driver oficial. Mouses fora da tabela ficam só com Hz e bateria.

A linha "NK"/Nearlink (VXE R1 NK, ATK A9 3.0 NK...) usa **outro** chip (BITMOUSE, pacotes de 63 bytes,
checksum por soma). Ela não está aqui.

## Formato dos pacotes

- *Output report* com **report ID 8**, 16 bytes. A resposta chega como *input report* com o mesmo
  formato, repetindo o comando no byte 0.
- `[comando, status, endereço (2 bytes, o mais significativo primeiro), tamanho, 10 bytes de dados, checksum]`
- **Checksum:** `0x55 − (8 + soma dos bytes 0 a 14)`, ou seja, o report ID entra na soma.
- O driver oficial tenta até 3 vezes, esperando 500 ms pela resposta.

## Comandos

| Comando | Nome no código oficial | O que faz | Usamos? |
|---|---|---|---|
| `0x01` | `DownLoadData` | Apresentação: manda 4 bytes quaisquer e recebe CID, MID e o tipo de conexão (bytes 9, 10 e 11 da resposta) | Sim |
| `0x03` | `GetWirelessMouseOnline` | No receptor: o byte 5 é `1` se o mouse estiver ligado | Sim |
| `0x04` | `GetBatteryLevel` | Byte 5: bateria em % | Sim |
| `0x07` | `SetEEPROM` | Grava `tamanho` bytes no `endereço` da memória do mouse | Só nos endereços de Hz e DPI |
| `0x08` | `GetEEPROM` | Lê `tamanho` bytes do `endereço` (dados a partir do byte 5) | Sim |
| `0x10` | `GetMouseCIDMID` | Bytes 5 e 6: CID e MID (identificam o modelo) | Sim |
| `0x05`, `0x13` | Parear receptor / sair do pareamento | | **Nunca** |
| `0x09` | `RestoreFactory` | Volta ao padrão de fábrica | **Nunca** |
| `0x0D` | `EnterUSBUpgradeMode` | Entra no modo de atualização de firmware | **Nunca** |

O comando `0x07` grava em qualquer endereço, inclusive no mapa de botões e nas macros. Por isso o app só
o usa a partir de código próprio, nos endereços abaixo, e nunca a partir de um driver JSON.

**Tipo de conexão** (resposta do `0x01`): `0` receptor 1K, `1` receptor 4K, `2` cabo 1K, `3` cabo 8K,
`4` receptor 2K, `5` receptor 8K, `6` cabo 2K, `7` cabo 4K, `8` NearLink. Define até quantos Hz oferecer.

## Memória do mouse

Cada valor é seguido de um byte de conferência: `0x55 − valor` (com mais de um byte, `0x55 − soma`).
Como tudo fica na memória, não existe comando de "salvar": o valor gravado continua depois de desligar.

| Endereço | Tamanho | Conteúdo |
|---|---|---|
| `0x0000` | 10 | Hz, nº de estágios de DPI (1 a 8), estágio atual (começa em 0), bhop, modo dos botões. Cada um com sua conferência |
| `0x000A` | 2 | LOD |
| `0x000C` | 4 × 8 | DPI dos estágios (lidos e gravados de 2 em 2, 8 bytes por vez) |
| `0x00A9` em diante | 10 | Debounce, motion sync, tempo para apagar o LED, angle snapping, ripple control |
| `0x0060` em diante | | Mapa de botões e macros (não mexemos) |
| `0x1B00` | 6 × 8 | DPI dos estágios no sensor PAW3955 |

**Hz:** `1` = 1000, `2` = 500, `4` = 250, `8` = 125, `16` = 2000, `32` = 4000, `64` = 8000.
O app lê o bloco de 10 bytes, troca só o Hz e grava o bloco de volta.

## DPI

Estágio de 4 bytes: `[X, Y, extra, conferência]`. O byte extra guarda os 2 bits altos de X (bits 2-3)
e de Y (bits 6-7), e marcas de escala. A conta depende do sensor:

| Sensor | Gravar | Ler |
|---|---|---|
| PAW3395, 3395 Ultra, 3950, 3950 DM | até 30000: `dpi/50 − 1`; acima: `dpi/100 − 1` com a marca de dobro (`0x11`) | `(valor + 1) × 50`, vezes 2 com a marca |
| PAW3950 Ultra | até 10000: `dpi/10 − 1`; acima: `(dpi − 10050)/50` com a marca `0x22` (e `dpi/2` com `0x11` acima de 30000) | com `0x02`: `valor × 50 + 10050`, senão `(valor + 1) × 10`; vezes 2 com `0x01` |
| CORE26K | `dpi/50 − 1` | `(valor + 1) × 50` |
| PAW3955 | 6 bytes `[X (2), Y (2), dobro, conf.]`: `dpi − 1`; a partir de 40000, `dpi/2 − 1` com `0x11` | `(valor + 1)`, vezes 2 com a marca |
| PAW3395 SE, 3311, 3315, 3320 | Usam tabelas próprias. **Ainda não suportados** (sem DPI no app) | |

As contas do app foram comparadas com as funções do driver oficial (`Ma`, `pa`, `wa`, `pr` e as de
leitura) para vários valores, e deram o mesmo resultado.

O app muda o DPI do **estágio em uso**: lê o estágio atual no bloco `0x0000` e grava só ele.
