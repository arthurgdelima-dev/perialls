<p align="center"><img src="Assets/logo.png" width="128" alt="Logo do Perialls"></p>

# Perialls

App leve para Windows e Linux que mostra seus periféricos num lugar só: o que está conectado, a bateria,
a cor do RGB e o DPI do mouse.

Não substitui os apps das marcas (G Hub, Synapse etc.). É só uma tela simples para o dia a dia.

- **Leve:** uma tela só. Não abre com o sistema e não deixa nada rodando em segundo plano.
- **Genérico:** reconhece os aparelhos de qualquer pessoa, usando o que o próprio sistema informa.
  Aparelhos desconhecidos aparecem também, só com nome e status.

## O que ele faz

| Recurso | Funciona com |
|---|---|
| Lista de aparelhos e status | Qualquer aparelho USB, HID, Bluetooth ou de áudio |
| Bateria | Aparelhos Bluetooth que informam bateria, controles (XInput no Windows; os que o kernel conhece no Linux) e Logitech (HID++) |
| Cor RGB | Tudo que o [OpenRGB](https://openrgb.org) reconhece (placa-mãe, fans, fitas, periféricos) |
| DPI | Mouses Logitech com HID++ |
| Efeito, cor, brilho, velocidade e Hz | Teclado Akko/MonsGeek FUN60 PRO |

## Como usar

1. Baixe na página de [Releases](../../releases) e abra. Não precisa instalar nada.
   - **Windows:** `Perialls.exe`.
   - **Linux:** `Perialls`. Dê permissão de execução (`chmod +x Perialls`) e abra.

   > **"O Windows protegeu o computador"?** Esse aviso aparece porque o `.exe` não tem assinatura
   > digital (o certificado é pago). Clique em **Mais informações → Executar assim mesmo**. O código
   > está todo aqui no repositório, se você quiser conferir ou compilar você mesmo.

2. **Linux, para Logitech e Akko/MonsGeek:** o Linux só deixa o root falar direto com esses aparelhos.
   Instale a regra do udev uma vez (ela libera só para quem está usando o PC):
   ```
   sudo curl -L -o /etc/udev/rules.d/70-perialls.rules https://raw.githubusercontent.com/arthurgdelima-dev/perialls/main/linux/70-perialls.rules
   sudo udevadm control --reload && sudo udevadm trigger
   ```
   (Ou, com o repositório baixado: `sudo cp linux/70-perialls.rules /etc/udev/rules.d/`.)
   Depois tire e ligue o aparelho de novo. Sem a regra, eles aparecem na lista, mas sem bateria, DPI e
   iluminação (o rodapé do app avisa).
3. **Para controlar RGB:** instale o [OpenRGB](https://openrgb.org) e deixe ele aberto com o
   **SDK Server** ligado (aba *SDK Server* → *Start Server*). Se o OpenRGB estiver instalado, o
   Perialls mostra um botão na barra lateral para abrir ele já com o servidor ligado.
4. Clique em **Atualizar** sempre que conectar ou desconectar um aparelho. O app não fica
   verificando sozinho.

Dê **dois cliques no nome** de um aparelho para renomear (Enter salva, Esc cancela, deixar vazio volta
ao nome original). Os nomes ficam em `%AppData%\Perialls\nomes.txt` (no Linux, `~/.config/Perialls/nomes.txt`).

Passe o mouse num cartão para **ocultar** um aparelho que você não quer ver. O botão do olho na barra
lateral mostra os ocultos de novo. A lista de ocultos fica em `%AppData%\Perialls\ocultos.txt` (no Linux, `~/.config/Perialls/ocultos.txt`).

### Teclado Akko/MonsGeek FUN60 PRO
- Feche o software oficial (MonsGeek Driver e o `iot_driver` que roda em segundo plano) e o driver
  web antes de usar, para os dois não disputarem o teclado.
- No Linux, precisa da regra do udev (passo 2 acima).
- O app confere o modelo pelo ID que o próprio teclado informa. Se não for um modelo conhecido, os
  controles não aparecem.
- **Hz:** o software oficial só libera essa opção em firmwares mais novos. O Perialls libera em
  qualquer firmware, então use por sua conta. Se o teclado parar de responder, tire o cabo e ligue
  de novo.
- Como o protocolo funciona: [docs/protocolo-monsgeek.md](docs/protocolo-monsgeek.md).

## Compilar

Precisa do [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet run --project Perialls.csproj
```

Para gerar o executável único (cerca de 45 MB, porque o .NET vai junto). Dá para gerar os dois
de qualquer um dos sistemas:

```
dotnet publish Perialls.csproj -p:PublishProfile=win-x64     # sai em publish\Perialls.exe
dotnet publish Perialls.csproj -p:PublishProfile=linux-x64   # sai em publish/linux/Perialls
```

## Adicionar a foto de um aparelho

1. Coloque um PNG em `Assets/Devices/`.
2. Adicione uma linha em `Assets/Devices/devices.json` ligando o código USB do aparelho ao arquivo:
   ```json
   "VID_046D&PID_4093": "logitech-pro-x-superlight.png"
   ```
3. Compile de novo. Aparelhos sem foto mostram o ícone do tipo no lugar.

O código USB (VID/PID) aparece no Gerenciador de Dispositivos: *Propriedades* → *Detalhes* →
*IDs de hardware*. No Linux, no comando `lsusb` (ex.: `ID 046d:4093` vira `VID_046D&PID_4093`).

## Bibliotecas

- [Avalonia](https://avaloniaui.net): a tela, a mesma no Windows e no Linux.
- [HidSharp](https://www.zer7.com/software/hidsharp): conversa com aparelhos HID (Logitech e Akko).
- [OpenRGB.NET](https://github.com/diogotr7/OpenRGB.NET): cliente do SDK do OpenRGB pela rede.
  O OpenRGB em si é um programa separado (GPL) e não vem junto com o Perialls.
- System.Management: lista os aparelhos pelo Windows (WMI). No Linux a lista vem de `/sys` e o
  Bluetooth do BlueZ (pelo `busctl`, que vem com o systemd).

## Licença

[MIT](LICENSE): pode usar, copiar e modificar à vontade, desde que mantenha o aviso de copyright.
