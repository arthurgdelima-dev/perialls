using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Perialls;

public partial class MainWindow : Window
{
    private List<DeviceInfo> _devices = [];
    private readonly HashSet<string> _hidden = HiddenDevices.Load();
    private readonly Dictionary<string, string> _names = DeviceNames.Load();

    // Se fomos nós que abrimos o OpenRGB, fechamos ele junto com o app (nada fica rodando).
    private Process? _openRgbWeStarted;

    // Largura da faixa nas bordas da janela que serve para mudar o tamanho.
    private const double ResizeBorder = 8;

    // Deixa o ✓ de "driver instalado" mais discreto que os outros ícones.
    public static readonly IValueConverter IsInstalled =
        new FuncValueConverter<DriverAction, bool>(a => a == DriverAction.Installed);

    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) => await RefreshAsync();
        Closed += (_, _) => StopOpenRgbIfWeStartedIt();
    }

    private async void Refresh_Click(object? sender, RoutedEventArgs e) => await RefreshAsync();

    // Lê os aparelhos uma vez. Não há nada rodando sozinho: só atualiza ao abrir ou ao clicar.
    private async Task RefreshAsync()
    {
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Procurando aparelhos...";
        try
        {
            // A busca roda fora da tela para a janela não "congelar" enquanto o sistema responde.
            // O índice de drivers é baixado ao mesmo tempo.
            var catalog = DriverCatalog.UpdateAsync();
            var result = await Task.Run(DeviceScanner.Scan);
            var catalogWarning = await catalog;
            _devices = result.Devices;
            foreach (var d in _devices)
            {
                d.OriginalName = d.Name;
                d.IsHidden = _hidden.Contains(d.Key);
                d.Photo = DevicePhotos.Find(d);
                if (_names.TryGetValue(d.Key, out var custom))
                    d.Name = custom;
                DriverCatalog.Describe(d);
            }
            ShowDevices();

            var hiddenCount = _devices.Count(d => d.IsHidden);
            StatusText.Text = $"{_devices.Count - hiddenCount} aparelhos"
                + (hiddenCount > 0 ? $" ({hiddenCount} ocultos)" : "")
                + $" · atualizado às {DateTime.Now:HH:mm:ss}";

            if (catalogWarning != null)
                StatusText.Text += "\n" + catalogWarning;

            if (result.HidAccessDenied)
                StatusText.Text += "\nSem permissão para falar com Logitech/Akko: instale a regra do udev (veja o README).";

            if (!result.OpenRgbRunning)
            {
                StatusText.Text += "\nRGB indisponível: abra o OpenRGB com o SDK Server ligado.";
                StartOpenRgbButton.IsVisible = OpenRgb.FindExe() != null;
            }
            else
            {
                StartOpenRgbButton.IsVisible = false;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Erro ao ler os aparelhos: " + ex.Message;
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    // Mostra a lista, escondendo os aparelhos ocultos (a não ser que "Mostrar ocultos" esteja marcado).
    private void ShowDevices()
    {
        var showHidden = ShowHiddenBox.IsChecked == true;
        DeviceList.ItemsSource = _devices.Where(d => showHidden || !d.IsHidden).ToList();
    }

    private void ShowHidden_Changed(object? sender, RoutedEventArgs e) => ShowDevices();

    private void ToggleHidden_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not DeviceInfo device)
            return;

        device.IsHidden = !device.IsHidden;
        if (device.IsHidden)
            _hidden.Add(device.Key);
        else
            _hidden.Remove(device.Key);

        HiddenDevices.Save(_hidden);
        ShowDevices();
    }

    // Dois cliques no nome abrem a caixa para renomear.
    private void DeviceName_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not DeviceInfo device)
            return;

        device.IsRenaming = true;
        ShowDevices();
    }

    private void RenameBox_Attached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // Espera a caixa aparecer na tela antes de pôr o cursor nela.
        if (sender is TextBox { DataContext: DeviceInfo { IsRenaming: true } } box)
            Dispatcher.UIThread.Post(() =>
            {
                box.Focus();
                box.SelectAll();
            }, DispatcherPriority.Loaded);
    }

    // Enter salva, Esc cancela. Nome vazio volta para o nome original.
    private void RenameBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: DeviceInfo device } box || e.Key is not (Key.Enter or Key.Escape))
            return;

        if (e.Key == Key.Enter)
        {
            var name = (box.Text ?? "").Trim();
            if (name == "" || name == device.OriginalName)
            {
                _names.Remove(device.Key);
                device.Name = device.OriginalName;
            }
            else
            {
                _names[device.Key] = name;
                device.Name = name;
            }
            DeviceNames.Save(_names);
        }

        e.Handled = true;
        device.IsRenaming = false;
        ShowDevices();
    }

    private void RenameBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: DeviceInfo { IsRenaming: true } device })
        {
            device.IsRenaming = false;
            ShowDevices();
        }
    }

    // Ícone do canto do cartão.
    private async void DriverIcon_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not DeviceInfo device)
            return;

        switch (device.DriverAction)
        {
            case DriverAction.OpenRgb:
                await OpenLinkAsync(DriverCatalog.OpenRgbSite);
                break;
            case DriverAction.RequestSupport:
                await OpenLinkAsync(DriverCatalog.SupportRequestUrl(device));
                StatusText.Text = "Pedido de suporte aberto no navegador. Confira os dados e clique em \"Create\".";
                break;
            case DriverAction.Download or DriverAction.Update:
                StatusText.Text = $"Driver {device.DriverEntry?.Name} {device.DriverEntry?.Version} disponível.";
                break;
            case DriverAction.Installed:
                StatusText.Text = device.DriverTip;
                break;
        }
    }

    private async Task OpenLinkAsync(string url)
    {
        if (!await Launcher.LaunchUriAsync(new Uri(url)))
            StatusText.Text = "Não deu para abrir o navegador. Endereço: " + url;
    }

    private async void Swatch_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ColorChoice choice)
            await ApplyColorAsync(choice.Device, choice.Hex);
    }

    private async void CustomColor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox { DataContext: DeviceInfo device } box)
            await ApplyColorAsync(device, box.Text ?? "");
    }

    private async Task ApplyColorAsync(DeviceInfo device, string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, null, out var rgb))
        {
            StatusText.Text = "Cor inválida. Use o formato #RRGGBB, por exemplo #FF8800.";
            return;
        }

        byte r = (byte)(rgb >> 16), g = (byte)(rgb >> 8), b = (byte)rgb;

        if (device.KeyboardLighting is { } current)
        {
            // Preto apaga (brilho 0). Outra cor: cor única, e volta o brilho se estava apagado.
            // Efeitos que não usam cor (Neon) trocam para o estático.
            var effect = MonsGeekKeyboard.Effects.FirstOrDefault(x => x.Id == current.Effect);
            var lighting = rgb == 0
                ? current with { Brightness = 0 }
                : current with
                {
                    R = r, G = g, B = b,
                    Multicolor = false,
                    Brightness = current.Brightness == 0 ? (byte)4 : current.Brightness,
                    Effect = effect is { UsesColor: false } ? (byte)1 : current.Effect,
                };
            await ApplyKeyboardLightingAsync(device, lighting,
                rgb == 0 ? "LEDs apagados" : $"Cor #{hex.ToUpperInvariant()} aplicada");
            return;
        }

        try
        {
            await Task.Run(() => OpenRgb.SetColor(device.RgbLocation!, r, g, b));
            StatusText.Text = rgb == 0
                ? $"LEDs desligados: {device.Name}"
                : $"Cor #{hex.ToUpperInvariant()} aplicada: {device.Name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Não deu para mudar a cor: " + ex.Message;
        }
    }

    private async void DpiPreset_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is Choice choice)
            await ApplyDpiAsync(choice.Device, choice.Value);
    }

    private async void DpiCustom_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox { DataContext: DeviceInfo device } box)
            return;

        if (int.TryParse((box.Text ?? "").Trim(), out var dpi) && dpi >= 50 && dpi <= 50000)
            await ApplyDpiAsync(device, dpi);
        else
            StatusText.Text = "DPI inválido. Digite só o número, por exemplo 1600.";
    }

    private async Task ApplyDpiAsync(DeviceInfo device, int dpi)
    {
        try
        {
            await Task.Run(() => LogitechHidpp.SetDpi(device.LogitechProductId, device.LogitechIndex, dpi));
            device.Dpi = dpi;
            ShowDevices(); // redesenha os cartões com o DPI novo
            StatusText.Text = $"DPI {dpi} aplicado: {device.Name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Não deu para mudar o DPI: " + ex.Message;
        }
    }

    private async void KeyboardEffect_Changed(object? sender, SelectionChangedEventArgs e)
    {
        // Sem item removido = é só a tela mostrando o efeito atual, não um clique.
        if (e.RemovedItems.Count == 0 || sender is not ComboBox { DataContext: DeviceInfo device, SelectedItem: MonsGeekKeyboard.Effect effect })
            return;
        if (device.KeyboardLighting is { } current && current.Effect != effect.Id)
            await ApplyKeyboardLightingAsync(device, current with { Effect = effect.Id, Option = effect.DefaultOption },
                $"Efeito {effect.Name} aplicado");
    }

    private async void KeyboardBrightness_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is Choice { Device: { KeyboardLighting: { } current } device } choice)
            await ApplyKeyboardLightingAsync(device, current with { Brightness = (byte)choice.Value }, $"Brilho {choice.Value} aplicado");
    }

    private async void KeyboardSpeed_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is Choice { Device: { KeyboardLighting: { } current } device } choice)
            await ApplyKeyboardLightingAsync(device, current with { Speed = (byte)choice.Value }, $"Velocidade {choice.Value} aplicada");
    }

    private async Task ApplyKeyboardLightingAsync(DeviceInfo device, MonsGeekKeyboard.Lighting lighting, string message)
    {
        try
        {
            await Task.Run(() => MonsGeekKeyboard.SetLighting(lighting));
            device.KeyboardLighting = lighting;
            ShowDevices();
            StatusText.Text = $"{message}: {device.Name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Não deu para mudar a iluminação: " + ex.Message;
        }
    }

    private async void ReportRate_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not Choice { Device: var device, Value: var hz })
            return;
        try
        {
            await Task.Run(() => MonsGeekKeyboard.SetReportRate(hz));
            device.ReportRateHz = hz;
            ShowDevices();
            StatusText.Text = $"{hz} Hz aplicado: {device.Name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Não deu para mudar o Hz: " + ex.Message;
        }
    }

    // Quantos cartões cabem por linha: cada um tem pelo menos 240 px de largura.
    private void Cards_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var width = CardsScroll.Bounds.Width - 18; // espaço da barra de rolagem
        var columns = Math.Max(1, (int)(width / 240));
        DeviceList.Tag = Math.Floor(width / columns);
    }

    // Janela sem a barra do sistema: pelas bordas muda o tamanho; por qualquer área vazia, arrasta.
    // (O nome do aparelho fica de fora, para os dois cliques de renomear funcionarem.)
    private void Frame_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (EdgeAt(e.GetPosition(this)) is { } edge)
            BeginResizeDrag(edge, e);
        else if (e.Source is not TextBlock { Classes: var classes } || !classes.Contains("name"))
            BeginMoveDrag(e);
    }

    // Muda a setinha do mouse nas bordas, para mostrar que dá para redimensionar.
    private void Frame_PointerMoved(object? sender, PointerEventArgs e)
    {
        Frame.Cursor = EdgeAt(e.GetPosition(this)) switch
        {
            WindowEdge.West or WindowEdge.East => new Cursor(StandardCursorType.SizeWestEast),
            WindowEdge.North or WindowEdge.South => new Cursor(StandardCursorType.SizeNorthSouth),
            WindowEdge.NorthWest => new Cursor(StandardCursorType.TopLeftCorner),
            WindowEdge.NorthEast => new Cursor(StandardCursorType.TopRightCorner),
            WindowEdge.SouthWest => new Cursor(StandardCursorType.BottomLeftCorner),
            WindowEdge.SouthEast => new Cursor(StandardCursorType.BottomRightCorner),
            _ => Cursor.Default,
        };
    }

    private WindowEdge? EdgeAt(Point p)
    {
        bool left = p.X < ResizeBorder, right = p.X > Bounds.Width - ResizeBorder;
        bool top = p.Y < ResizeBorder, bottom = p.Y > Bounds.Height - ResizeBorder;
        return (top, bottom, left, right) switch
        {
            (true, _, true, _) => WindowEdge.NorthWest,
            (true, _, _, true) => WindowEdge.NorthEast,
            (_, true, true, _) => WindowEdge.SouthWest,
            (_, true, _, true) => WindowEdge.SouthEast,
            (true, _, _, _) => WindowEdge.North,
            (_, true, _, _) => WindowEdge.South,
            (_, _, true, _) => WindowEdge.West,
            (_, _, _, true) => WindowEdge.East,
            _ => null,
        };
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private async void StartOpenRgb_Click(object? sender, RoutedEventArgs e)
    {
        var exe = OpenRgb.FindExe();
        if (exe == null)
            return;

        StartOpenRgbButton.IsEnabled = false;
        StatusText.Text = "Abrindo o OpenRGB e esperando ele achar os aparelhos...";
        try
        {
            _openRgbWeStarted = await OpenRgb.StartAsync(exe);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Não deu para abrir o OpenRGB: " + ex.Message;
            return;
        }
        finally
        {
            StartOpenRgbButton.IsEnabled = true;
        }
        await RefreshAsync();
    }

    private void StopOpenRgbIfWeStartedIt()
    {
        try
        {
            if (_openRgbWeStarted is { HasExited: false })
                _openRgbWeStarted.Kill();
        }
        catch
        {
            // Se não conseguir fechar, não tem problema: o usuário pode fechar pelo próprio OpenRGB.
        }
    }
}
