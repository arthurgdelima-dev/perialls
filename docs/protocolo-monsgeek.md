# Protocolo HID do Akko/MonsGeek FUN60 PRO

Como o driver oficial conversa com o teclado, para usarmos no Perialls.

**De onde veio:** capturas do driver web oficial (https://app.monsgeek.com) feitas com
[tools/hid-espiao.js](../tools/hid-espiao.js), conferidas com o código JavaScript do próprio site
(arquivos `index.js`, `97aa4548.js` e `e11937a0.js`, baixados em 28/10/2026). Só usamos o que o site
oficial manda para este modelo.

## Aparelho

| | |
|---|---|
| VID / PID | `0x3151` / `0x502D` |
| Interface de configuração | `MI_02`, usage page `0xFFFF`, usage `0x02` |
| Chip | RY5088 (a família de comandos muda conforme o chip, então esta tabela vale só para ele) |
| ID do modelo | `2304`, nome interno `ry5088_akko_fun60pro_1m_8k`, "FUN60 PRO" |
| Firmware (USB) do teclado testado | `0x0309` |

Vários teclados usam o mesmo PID `0x502D` (X65HE, K68, Beat65, FUN60 Ultra...). Por isso o modelo é
confirmado pelo **ID** lido com o comando `0x8F` (veja abaixo), e não só pelo VID/PID.

## Formato dos pacotes

- *Feature reports* HID de **64 bytes**, **report ID 0**. Com o HidSharp: buffer de 65 bytes, sendo `[0] = 0`.
- Byte 0 é o **comando**. Os comandos de leitura geralmente são o comando de escrita com o bit `0x80` ligado.
- **Para ler:** manda o pedido com `SetFeature`, espera ~10 ms e lê a resposta com `GetFeature`.
  A resposta repete o byte do comando no byte 0.
- **Depois de uma escrita**, o teclado manda um input report `05 0f 01` e logo depois `05 0f 00`.
  O site espera **1 s** depois de cada escrita antes da próxima.

### Checksum

`checksum = 0xFF - (soma dos bytes anteriores & 0xFF)`

| Modo | Posição do checksum | Soma os bytes | Usado em |
|---|---|---|---|
| Bit7 | byte 7 | 0 a 6 | quase tudo (leituras, Hz) |
| Bit8 | byte 8 | 0 a 7 | iluminação (`0x07`) |

Exemplo: `8f 00 00 00 00 00 00 70`, porque 0xFF − 0x8F = 0x70.

## Comandos que usamos

| Cmd | Nome no site | Para que serve |
|---|---|---|
| `0x8F` | GET_USB_VERSION | ID do modelo e versão do firmware |
| `0x87` | GET_LEDPARAM | ler a iluminação |
| `0x07` | SET_LEDPARAM | mudar a iluminação |
| `0x83` | GET_REPORT | ler o Hz |
| `0x03` | SET_REPORT | mudar o Hz (**veja o aviso abaixo**) |

### `0x8F`: identificação

```
envia:   8f 00 00 00 00 00 00 70
recebe:  8f 00 09 00 00 00 00 09 03
              └─┬─┘           └─┬─┘
       ID = 0x0900 = 2304   firmware = 0x0309
```
- ID do modelo = `resp[2] << 8 | resp[1]`
- Versão USB = `resp[8] << 8 | resp[7]`

### `0x07` / `0x87`: iluminação

| Byte | Conteúdo |
|---|---|
| 0 | `0x07` (escrita) / `0x87` (leitura) |
| 1 | efeito (tabela abaixo) |
| 2 | **4 − velocidade** (velocidade de 0 a 4; o valor fica invertido no pacote) |
| 3 | brilho, de 0 a 4 |
| 4 | `opção << 4 \| modo de cor`: modo `8` = cor única (usa o RGB), `7` = multicor. O código do site chama o 8 de "multicor", mas no teclado é o contrário (testado: com `07` a cor escolhida é ignorada) |
| 5, 6, 7 | R, G, B |
| 8 | checksum Bit8 |

O site troca o branco puro `FFFFFF` por `FAFFFA` antes de enviar, e faz a troca contrária ao ler.

Exemplo da captura, com estático, brilho 4 e vermelho:
```
07 01 04 04 08 ff 00 00 e8
```
Exemplo de brilho (o brilho cai de 4 para 0 e o checksum sobe junto):
```
07 01 04 04 08 ff 00 00 e8
07 01 04 03 08 ff 00 00 e9
...
07 01 04 00 08 ff 00 00 ec
```
Estado original do teclado testado (resposta do `0x87`):
```
87 0f 02 04 07 b4 b4 b4  →  efeito 15 (onda circular), velocidade 2, brilho 4, multicolor
```

**Efeitos que o site oferece para este modelo** (os números vêm da `LightList` do site):

| Nº | Nome no site | Opções (byte 4, parte de cima) |
|---|---|---|
| 1 | LightAlwaysOn (estático) | não tem velocidade |
| 2 | LightBreath (respiração) | |
| 3 | LightNeon | não usa cor |
| 4 | LightWave (onda) | só 2 = para baixo, 3 = para cima |
| 5 | LightRipple | |
| 6 | LightRaindrop | |
| 7 | LightSnake | 0 = zigue-zague, 1 = espiral |
| 8 | LightPressAction | |
| 9 | LightConverage | |
| 10 | LightSineWave | |
| 11 | LightKaleidoscope | 0 = para fora, 1 = para dentro |
| 12 | LightLineWave | |
| 14 | LightLaser | |
| 15 | LightCircleWave | 0 = anti-horário, 1 = horário |
| 16 | LightDazzing | |
| 17 | LightRainDown | |
| 18 | LightMeteor | |
| 19 | LightPressActionOff | |
| 23 | LightTrain | |
| 24 | LightFireWorks | 0 = direita, 1 = esquerda |

Não usamos por enquanto: 13 (desenho por tecla, que usa os comandos `0x0C`/`0x8C`), 20/22 (segue a
música) e 21 (cor da tela).

### `0x03` / `0x83`: taxa de atualização (Hz)

```
envia:   83 00 00 00 00 00 00 7c
recebe:  83 00 01 ...            → resp[2] = 1 → 4000 Hz
```

| Valor (byte 2) | 0 | 1 | 2 | 3 | 4 | 5 | 6 |
|---|---|---|---|---|---|---|---|
| Hz | 8000 | 4000 | 2000 | 1000 | 500 | 250 | 125 |

A escrita seria `03 00 <valor> 00 00 00 00 <checksum Bit7>`.

> **Aviso:** o site só mostra a opção de Hz quando o firmware USB é **≥ 0x0400 (1024)**. O teclado
> testado está em `0x0309`, então o site **nunca mandou** o `0x03` para ele. A leitura (`0x83`)
> funciona. A escrita ainda não foi testada e não deve ser usada sem decidir isso antes.

## Comandos proibidos

Estes existem no protocolo e **nunca** devem ser enviados pelo Perialls:

| Cmd | Nome no site | Motivo |
|---|---|---|
| `0x01` | SET_RESERT | volta para as configurações de fábrica |
| `0xAC` | SET_FLASHCHIPERASSE | apaga a memória flash |
| `0x1C`, `0x1E` | SET_MAGNETISM_CAL... | calibração dos switches magnéticos |
| `0x0A`, `0x10`, `0x0B` | KEYMATRIX / FN / MACRO | mudam o que cada tecla faz |

Atualização de firmware e bootloader não passam por esta tabela, e também não serão usados.
