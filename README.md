<p align="center"><img src="Assets/logo.png" width="128" alt="Logo do Perialls"></p>

# Perialls

**Português** · [English](README.en.md)

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
| Funções extras por driver | Aparelhos com driver em [`drivers/`](drivers), baixado pelo próprio app |
| Sincronizar RGB | A mesma cor em todos os aparelhos com RGB, com um clique |

## Como usar

1. **[Baixar a última versão](https://github.com/arthurgdelima-dev/perialls/releases/latest)** e abrir.
   Não precisa instalar nada, nem o OpenRGB: ele já vem junto, na pasta `openrgb`.
   - **Windows:** `Perialls-win-x64.zip`. Extraia **tudo** (botão direito → *Extrair tudo*) e abra o
     `Perialls.exe`. Ele precisa da pasta `openrgb` ao lado.
   - **Linux:** `Perialls-linux-x64.tar.gz`. Extraia e abra:
     ```
     tar -xzf Perialls-linux-x64.tar.gz
     cd Perialls-linux-x64
     chmod +x Perialls   # o .tar.gz já guarda a permissão; só precisa se ela se perder
     ./Perialls
     ```
   - `SHA256SUMS.txt` traz o hash de cada arquivo, se você quiser conferir o download.

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
3. **RGB:** funciona sozinho, com o [OpenRGB](https://openrgb.org) que vem junto. Ele só é ligado
   quando você escolhe uma cor ou usa *Sincronizar RGB* (o rodapé mostra "Ligando o RGB..." por alguns
   segundos, enquanto ele procura os aparelhos). Ele roda escondido, só como servidor, sem aplicar
   nenhum perfil, fica aberto enquanto o Perialls estiver aberto e fecha junto com ele. Se você já tiver
   um OpenRGB aberto com o *SDK Server* ligado, o Perialls usa o seu.
   - **Linux:** o OpenRGB precisa de uma regra do udev para abrir os aparelhos USB. Se faltar, o rodapé
     mostra o comando exato. Memórias RAM e algumas placas-mãe usam i2c; para elas, ligue também o
     módulo: `echo i2c-dev | sudo tee /etc/modules-load.d/i2c-dev.conf && sudo modprobe i2c-dev`.
   - **Windows:** memórias RAM e algumas placas-mãe mais antigas (as que usam SMBus) ainda não
     funcionam, porque o OpenRGB precisaria rodar como administrador. Aparelhos USB, como a maioria
     das placas-mãe atuais (ex.: MSI Mystic Light), fans, fitas e periféricos, funcionam normalmente.
4. Clique em **Atualizar** sempre que conectar ou desconectar um aparelho. O app não fica
   verificando sozinho.

**O app não muda nada sozinho.** Ao abrir, ele só lê os aparelhos. Cor, DPI, Hz e o resto só são
enviados quando você muda algo na tela. A última escolha de cada aparelho fica em
`%AppData%\Perialls\settings.json` (no Linux, `~/.config/Perialls/settings.json`), só para mostrar na tela
o que você escolheu quando o aparelho não informa o próprio estado. Ela não é reaplicada ao abrir.
Quando o aparelho tem memória própria e o OpenRGB ou o driver sabem gravar nela, a configuração também
fica gravada no aparelho.

Dê **dois cliques no nome** de um aparelho para renomear (Enter salva, Esc cancela, deixar vazio volta
ao nome original). Os nomes ficam em `%AppData%\Perialls\nomes.txt` (no Linux, `~/.config/Perialls/nomes.txt`).

O botão da **gota** na barra lateral **sincroniza o RGB**: a cor escolhida vai para todos os aparelhos
com RGB de uma vez. Se algum falhar, o rodapé diz qual, e os outros recebem a cor normalmente.

O **idioma** (português ou inglês) fica no pé da barra lateral (`PT`/`EN`). Na primeira vez o app usa o
idioma do sistema.

Passe o mouse num cartão para **ocultar** um aparelho que você não quer ver. O botão do olho na barra
lateral mostra os ocultos de novo. A lista de ocultos fica em `%AppData%\Perialls\ocultos.txt` (no Linux, `~/.config/Perialls/ocultos.txt`).

### Drivers

No canto de cada cartão aparece um ícone pequeno:

| Ícone | Quer dizer |
|---|---|
| ⬇ | Existe um driver para o aparelho. Clique para baixar: as funções aparecem na hora. |
| ⟳ | Há uma versão nova do driver. |
| ✓ | Driver instalado. |
| ➕ | Ainda não há suporte. Clique para abrir um pedido no GitHub, já preenchido com os dados do aparelho. |

Os drivers são só arquivos JSON que descrevem os comandos. O app nunca baixa programas, confere o hash
de cada driver com o índice e bloqueia comandos perigosos (firmware, reset). Ficam em
`%LocalAppData%\Perialls\drivers` (no Linux, `~/.local/share/Perialls/drivers`).

Quer fazer o driver de um aparelho? Veja [como adicionar um aparelho](docs/como-adicionar-um-aparelho.md).

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

## Publicar uma versão

As Releases são feitas sozinhas pelo GitHub Actions ([.github/workflows/release.yml](.github/workflows/release.yml)).
Basta criar uma tag que comece com `v` e enviar:

```
git tag v0.2.0
git push --tags
```

Em alguns minutos aparece a Release com o `.zip` do Windows e o `.tar.gz` do Linux (os dois já com o
OpenRGB, numa versão fixa e com o hash conferido), o código-fonte do OpenRGB, os hashes e as notas
geradas a partir dos commits. Para acompanhar, veja a aba *Actions* do repositório.

## Fotos dos aparelhos

As fotos são automáticas: ninguém precisa adicionar imagem. Quando um aparelho não tem foto salva, o app
procura uma na internet em segundo plano (Bing e, se não der, DuckDuckGo) e guarda só no seu PC, em
`%LocalAppData%\Perialls\imagens` (no Linux, `~/.local/share/Perialls/imagens`). Para isso, **o nome do
aparelho (marca e modelo) é enviado ao buscador**. A busca só aceita imagens com fundo transparente e
formato quadrado; se nada servir ou estiver sem internet, o cartão mostra o ícone do tipo e o app tenta
de novo no próximo Atualizar.

O código USB (VID/PID) aparece no Gerenciador de Dispositivos: *Propriedades* → *Detalhes* →
*IDs de hardware*. No Linux, no comando `lsusb` (ex.: `ID 046d:4093` vira `VID_046D&PID_4093`).

## OpenRGB incluído

A pasta `openrgb` traz o [OpenRGB](https://openrgb.org) 1.1 sem alterações. Ele é um programa separado,
licenciado pela **GNU GPL versão 2**; o Perialls só conversa com ele pela rede local, e o código dos dois
não é misturado. A licença dele está em `openrgb/LICENSE`, e o código-fonte da versão exata vai anexado
em cada Release (`OpenRGB-1.1-source.tar.gz`). Mais detalhes, e a oferta do código-fonte das bibliotecas
que ele usa, em [openrgb/SOURCE.md](openrgb/SOURCE.md).

## Bibliotecas

- [Avalonia](https://avaloniaui.net): a tela, a mesma no Windows e no Linux.
- [HidSharp](https://www.zer7.com/software/hidsharp): conversa com aparelhos HID (Logitech e Akko).
- [OpenRGB.NET](https://github.com/diogotr7/OpenRGB.NET): cliente do SDK do OpenRGB pela rede.
  O OpenRGB em si é um programa separado (GPL), que vem junto na pasta `openrgb` (veja acima).
- System.Management: lista os aparelhos pelo Windows (WMI). No Linux a lista vem de `/sys` e o
  Bluetooth do BlueZ (pelo `busctl`, que vem com o systemd).

## Licença

[MIT](LICENSE): pode usar, copiar e modificar à vontade, desde que mantenha o aviso de copyright.
