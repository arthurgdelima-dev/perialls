# Perialls

App leve para Windows que mostra seus periféricos num lugar só: o que está conectado, a bateria,
a cor do RGB e o DPI do mouse.

Não substitui os apps das marcas (G Hub, Synapse etc.). É só uma tela simples para o dia a dia.

- **Leve:** uma tela só. Não abre com o Windows e não deixa nada rodando em segundo plano.
- **Genérico:** reconhece os aparelhos de qualquer pessoa, usando o que o próprio Windows informa.
  Aparelhos desconhecidos aparecem também, só com nome e status.

## O que ele faz

| Recurso | Funciona com |
|---|---|
| Lista de aparelhos e status | Qualquer aparelho USB, HID, Bluetooth ou de áudio |
| Bateria | Aparelhos Bluetooth que informam bateria, controles XInput e Logitech (HID++) |
| Cor RGB | Tudo que o [OpenRGB](https://openrgb.org) reconhece (placa-mãe, fans, fitas, periféricos) |
| DPI | Mouses Logitech com HID++ |
| Efeito, cor, brilho, velocidade e Hz | Teclado Akko/MonsGeek FUN60 PRO |

## Como usar

1. Baixe o `Perialls.exe` na página de [Releases](../../releases) e abra. Não precisa instalar nada.
2. **Para controlar RGB:** instale o [OpenRGB](https://openrgb.org) e deixe ele aberto com o
   **SDK Server** ligado (aba *SDK Server* → *Start Server*). Se o OpenRGB estiver instalado, o
   Perialls mostra um botão na barra lateral para abrir ele já com o servidor ligado.
3. Clique em **Atualizar** sempre que conectar ou desconectar um aparelho. O app não fica
   verificando sozinho.

Passe o mouse num cartão para **ocultar** um aparelho que você não quer ver. O botão do olho na barra
lateral mostra os ocultos de novo. A lista de ocultos fica em `%AppData%\Perialls\ocultos.txt`.

### Teclado Akko/MonsGeek FUN60 PRO
- Feche o software oficial (MonsGeek Driver e o `iot_driver` que roda em segundo plano) e o driver
  web antes de usar, para os dois não disputarem o teclado.
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

Para gerar o `.exe` único (cerca de 70 MB, porque o .NET vai junto):

```
dotnet publish Perialls.csproj -p:PublishProfile=win-x64
```

O arquivo sai em `publish\Perialls.exe`.

## Adicionar a foto de um aparelho

1. Coloque um PNG em `Assets/Devices/`.
2. Adicione uma linha em `Assets/Devices/devices.json` ligando o código USB do aparelho ao arquivo:
   ```json
   "VID_046D&PID_4093": "logitech-pro-x-superlight.png"
   ```
3. Compile de novo. Aparelhos sem foto mostram o ícone do tipo no lugar.

O código USB (VID/PID) aparece no Gerenciador de Dispositivos: *Propriedades* → *Detalhes* →
*IDs de hardware*.

## Bibliotecas

- [HidSharp](https://www.zer7.com/software/hidsharp): conversa com aparelhos HID (Logitech e Akko).
- [OpenRGB.NET](https://github.com/diogotr7/OpenRGB.NET): cliente do SDK do OpenRGB pela rede.
  O OpenRGB em si é um programa separado (GPL) e não vem junto com o Perialls.
- System.Management: lista os aparelhos pelo Windows (WMI).
