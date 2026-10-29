# Como adicionar um aparelho

Este guia é para quem quer que um periférico ganhe funções no Perialls (cor, brilho, efeitos, Hz...)
e topa descobrir como ele funciona. Não precisa saber C#: um driver é só um arquivo JSON.

## Como funciona

- Cada família de aparelhos tem um arquivo em [`drivers/`](../drivers), por exemplo `drivers/minha-marca.json`.
- O arquivo só **descreve** os comandos que o aparelho entende. Quem monta e envia é o app, depois de
  conferir tudo. Não existe código no driver, e o app nunca baixa `.exe`, `.dll` nem scripts.
- O [`drivers/index.json`](../drivers/index.json) liga cada aparelho (VID/PID) ao arquivo dele, com a
  versão e o hash SHA-256. O app baixa o índice do GitHub, e no cartão do aparelho aparece o ícone
  ⬇ **Baixar driver**. Se o arquivo baixado não tiver o mesmo hash do índice, o app recusa.

### Drivers embutidos têm prioridade

Alguns aparelhos têm driver escrito direto no código do app, porque precisam de algo que um JSON não
faz:

| Aparelho | Por que é embutido |
|---|---|
| Akko/MonsGeek (`VID_3151&PID_502D`) | Vários modelos usam o mesmo PID. O app pergunta ao teclado qual modelo ele é (ID `2304` = FUN60 PRO) antes de mostrar os controles. |
| Logitech (fabricante `046D`, todos os aparelhos) | Usa o protocolo HID++, em que os números dos comandos mudam de um aparelho para outro. Por isso não dá para bloquear a atualização de firmware por byte. |

O driver embutido sempre ganha. Drivers JSON para esses VID/PID são **ignorados** (não aparece ícone de
download, nem um driver na pasta `local` é usado) e o app nunca envia comandos JSON para eles. Um
driver JSON da Logitech é recusado na validação. A lista de aparelhos embutidos fica em
`BuiltInDevices`, no arquivo [`JsonDrivers/DriverCatalog.cs`](../JsonDrivers/DriverCatalog.cs).

Quer melhorar um desses? Mande o Pull Request no código C# ([`BuiltIn/MonsGeekKeyboard.cs`](../BuiltIn/MonsGeekKeyboard.cs), [`BuiltIn/LogitechHidpp.cs`](../BuiltIn/LogitechHidpp.cs)),
e não um JSON.

## 1. Descubra o código USB (VID/PID)

O jeito mais fácil: no Perialls, passe o mouse no ícone ➕ do cartão e clique. O pedido de suporte que
abre no GitHub já traz o código.

Também dá para ver no sistema:
- **Windows:** Gerenciador de Dispositivos → o aparelho → *Propriedades* → *Detalhes* → *IDs de hardware*.
  `HID\VID_3151&PID_502D&MI_02` quer dizer VID `0x3151` e PID `0x502D`.
- **Linux:** `lsusb`. `ID 3151:502d` quer dizer VID `0x3151` e PID `0x502D`.

## 2. Descubra os comandos (engenharia reversa)

A ideia é observar o que o software oficial manda para o aparelho quando você muda **uma coisa de cada
vez**, e comparar os pacotes.

- **Software da marca que roda no navegador (WebHID):** use o [`tools/hid-espiao.js`](../tools/hid-espiao.js).
  Ele só observa e anota os pacotes, sem mandar nada sozinho. Instruções no começo do arquivo.
- **Programa instalado no Windows:** [Wireshark](https://www.wireshark.org) com USBPcap.
- **Linux:** Wireshark com o módulo `usbmon` (`sudo modprobe usbmon`).

Anote:
- **A interface:** a *usage page* e o *usage* da interface que recebe os comandos (as de configuração
  costumam ter usage page `0xFF00` ou maior), se os pacotes são *feature reports* ou *output reports*,
  o *report ID* e o tamanho do pacote.
- **Cada comando:** os bytes enviados e o que muda neles quando você mexe num valor.
- **O checksum,** se houver. Teste se o último byte é `0xFF − soma dos anteriores` (o app já sabe fazer
  esse cálculo).
- **Os comandos perigosos:** firmware, bootloader, reset de fábrica, apagar a memória, calibração.
  Liste no Pull Request. Eles entram numa lista de bloqueio no próprio app.

Um exemplo completo dessa investigação: [protocolo-monsgeek.md](protocolo-monsgeek.md).

> **Cuidado:** só use comandos que você viu o software oficial mandar, para aquele modelo. Nunca teste
> um byte "para ver o que acontece". Um comando errado pode travar o aparelho.

## 3. Escreva o driver

Comece copiando o [`drivers/_modelo.json`](../drivers/_modelo.json). Os campos:

| Campo | O que é |
|---|---|
| `name` | Nome que aparece no app (até 60 letras). |
| `devices` | Os aparelhos: `vid`, `pid` (ex.: `"0x3151"`) e `model`. O app só envia para esses. |
| `interface` | `usagePage`, `usage` (opcional), `reportId`, `reportLength` (até 64) e `type` (`feature` ou `output`). |
| `checksum` | `none` ou `ff-minus-sum`. Com `ff-minus-sum`, informe a posição em `checksumAt` (no driver ou em cada controle). |
| `delayMs` | Espera mínima entre dois comandos (0 a 2000 ms). |
| `defaults` | Valores iniciais de variáveis que não são controles. |
| `controls` | Os controles que aparecem no cartão. |
| `save` | Opcional. Comando que grava a configuração na memória do aparelho (`bytes` e, se precisar, `checksumAt`). O app manda depois de cada mudança, para valer mesmo sem o app. Passa pelas mesmas regras dos controles. |

Cada controle tem `bytes`: o pacote, com números hex (`07`) e variáveis entre chaves (`{brightness}`).
O resto do pacote é completado com zeros. **O primeiro byte é o comando e precisa ser fixo.**

Há três tipos de controle:
- **Cor:** o controle se chama `color` e usa `{r}`, `{g}` e `{b}`. No cartão aparecem as bolinhas de cor.
- **Faixa:** `min` e `max` (até 21 valores), e `default` opcional. Vira uma fileira de botões.
- **Opções:** `options` com `"Nome na tela": valor`. Se uma opção precisar mudar mais de um byte, use
  um grupo: `"Onda para cima": { "effect": 4, "direction": 3 }`. Até 7 opções viram botões; mais que
  isso, uma lista.

Uma variável pode aparecer no comando de vários controles. Por exemplo, se o aparelho manda cor, brilho
e efeito sempre no mesmo pacote, todos os controles usam o mesmo `bytes`, e o app preenche cada variável
com o valor atual.

O app nunca manda nada sozinho ao abrir: os comandos só saem quando a pessoa muda algo na tela. Como
um driver JSON não lê o estado do aparelho, a tela mostra a última escolha salva (ou o `default`).

Os nomes `brightness`, `speed`, `effect`, `direction`, `pollingRate` e `dpi` já ganham um título em
português. Para outros, use `label`.

## 4. Teste no seu computador

Coloque o arquivo na pasta de drivers locais:
- **Windows:** `%LocalAppData%\Perialls\drivers\local\`
- **Linux:** `~/.local/share/Perialls/drivers/local/`

Abra o Perialls (ou clique em **Atualizar**). O cartão do aparelho mostra um ✓ com a dica "Driver local
em teste", e os controles aparecem. Se o arquivo tiver algum erro, o rodapé do app explica o que está
errado, e nada é enviado.

Feche o software oficial antes de testar, para os dois não disputarem o aparelho. No Linux, o app
precisa de permissão para abrir o aparelho: copie a regra do udev do README e acrescente uma linha com
o VID do seu aparelho.

## 5. Mande um Pull Request

1. Coloque o arquivo em `drivers/` (nome só com letras minúsculas, números e `-`, ex.: `minha-marca.json`).
2. Calcule o hash SHA-256 do arquivo:
   - **Linux:** `sha256sum drivers/minha-marca.json`
   - **Windows (PowerShell):** `Get-FileHash drivers\minha-marca.json`
3. Acrescente uma linha em `drivers/index.json`:
   ```json
   {
     "name": "Minha Marca",
     "file": "minha-marca.json",
     "version": "1.0.0",
     "sha256": "o hash do passo 2",
     "devices": ["VID_0000&PID_0000"]
   }
   ```
   `devices` usa o formato `VID_xxxx&PID_yyyy`, com letras maiúsculas, e precisa bater com o arquivo.
4. Se for um fabricante novo e você achou comandos perigosos, liste no PR (ou acrescente em
   `BlockedCommands`, no arquivo [`JsonDrivers/DriverFile.cs`](../JsonDrivers/DriverFile.cs)).
5. No PR, conte em qual modelo e sistema você testou e de onde vieram os comandos.

Para mudar um driver que já existe: altere o arquivo, aumente a `version` e atualize o `sha256` no
índice. Quem já tem o driver vê o ícone ⟳ de atualização.
