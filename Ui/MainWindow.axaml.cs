using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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

    // Busca de fotos em andamento. Um novo Atualizar cancela a anterior.
    private CancellationTokenSource? _photoSearch;

    // Largura da faixa nas bordas da janela que serve para mudar o tamanho.
    private const double ResizeBorder = 8;

    // Deixa o ✓ de "driver instalado" mais discreto que os outros ícones.
    public static readonly IValueConverter IsInstalled =
        new FuncValueConverter<DriverAction, bool>(a => a == DriverAction.Installed);

    public MainWindow()
    {
        InitializeComponent();
        LanguageButton.Content = Lang.Current == "pt-BR" ? "PT" : "EN";
        Opened += async (_, _) => await RefreshAsync();
        Closed += (_, _) => StopOpenRgbIfWeStartedIt();
    }

    private async void Refresh_Click(object? sender, RoutedEventArgs e) => await RefreshAsync();

    // Lê os aparelhos uma vez. Não há nada rodando sozinho: só atualiza ao abrir ou ao clicar.
    private async Task RefreshAsync()
    {
        RefreshButton.IsEnabled = false;
        StatusText.Text = Lang.T("status.searching");
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
                d.Photo = DeviceImages.FromCache(d);
                d.SavedColor = UserSettings.Current.Find(d)?.Color;
                if (_names.TryGetValue(d.Key, out var custom))
                    d.Name = custom;
                DriverCatalog.Describe(d);
            }
            ShowDevices();

            _photoSearch?.Cancel();
            _photoSearch = new CancellationTokenSource();
            _ = SearchMissingPhotosAsync(_devices, _photoSearch.Token);

            var hiddenCount = _devices.Count(d => d.IsHidden);
            StatusText.Text = Lang.T("status.count", _devices.Count - hiddenCount)
                + (hiddenCount > 0 ? Lang.T("status.hiddenCount", hiddenCount) : "")
                + Lang.T("status.updatedAt", DateTime.Now.ToString("HH:mm:ss"));

            if (catalogWarning != null)
                StatusText.Text += "\n" + catalogWarning;
            foreach (var problem in DriverCatalog.Problems)
                StatusText.Text += "\n" + problem;

            if (result.HidAccessDenied)
                StatusText.Text += "\n" + Lang.T("status.hidDenied");

            if (!result.OpenRgbRunning)
            {
                StatusText.Text += "\n" + Lang.T("status.rgbUnavailable");
                StartOpenRgbButton.IsVisible = OpenRgb.FindExe() != null;
            }
            else
            {
                StartOpenRgbButton.IsVisible = false;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = Lang.T("status.scanError", ex.Message);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    // Fotos que não estão no cache: uma busca por aparelho, uma de cada vez, sem travar a tela.
    // Enquanto isso o cartão mostra o ícone; quando a foto chega, só aquele cartão muda.
    // Se não achar, fica o ícone (sem aviso) e tenta de novo no próximo Atualizar.
    private static async Task SearchMissingPhotosAsync(List<DeviceInfo> devices, CancellationToken cancel)
    {
        foreach (var device in devices.Where(d => d.Photo == null && !d.IsHidden).ToList())
        {
            if (cancel.IsCancellationRequested)
                return;
            var photo = await Task.Run(() => DeviceImages.SearchAsync(device, cancel));
            if (photo != null && !cancel.IsCancellationRequested)
                device.Photo = photo;
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
                StatusText.Text = Lang.T("status.supportOpened");
                break;
            case DriverAction.Download or DriverAction.Update when device.DriverEntry is { } entry:
                StatusText.Text = Lang.T("status.downloadingDriver", entry.Name);
                var (ok, message) = await DriverCatalog.InstallAsync(entry);
                StatusText.Text = message;
                if (ok)
                {
                    // Ativa na hora: os cartões dos aparelhos desse driver ganham os controles.
                    foreach (var d in _devices)
                        DriverCatalog.Describe(d);
                    ShowDevices();
                }
                break;
            case DriverAction.Installed:
                StatusText.Text = device.DriverTip;
                break;
        }
    }

    private async Task OpenLinkAsync(string url)
    {
        if (!await Launcher.LaunchUriAsync(new Uri(url)))
            StatusText.Text = Lang.T("status.browserFailed", url);
    }

    private async void Swatch_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ColorChoice choice)
            return;
        if (choice.Device == null)
        {
            SyncButton.Flyout?.Hide();
            await SyncColorAsync(choice.Hex);
        }
        else
        {
            await ApplyColorAsync(choice.Device, choice.Hex);
        }
    }

    private async void CustomColor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox { DataContext: DeviceInfo device } box)
            await ApplyColorAsync(device, box.Text ?? "");
    }

    private async Task ApplyColorAsync(DeviceInfo device, string hex)
    {
        if (ParseColor(hex) is not { } color)
            return;
        var (r, g, b) = color;

        var error = await SetColorAsync(device, r, g, b);
        ShowDevices();
        StatusText.Text = error != null ? Lang.T("status.colorFailed", device.Name, error)
            : Lang.T("status.forDevice", (r, g, b) == (0, 0, 0) ? Lang.T("status.ledsOff") : Lang.T("status.colorApplied", ColorText(r, g, b)), device.Name);
    }

    // Sincronizar RGB: a mesma cor em todos os aparelhos com RGB (OpenRGB, drivers JSON e o teclado Akko).
    // Todos recebem ao mesmo tempo; se um falhar, os outros seguem e o rodapé diz qual falhou.
    private async void SyncCustomColor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            SyncButton.Flyout?.Hide();
            await SyncColorAsync(box.Text ?? "");
        }
    }

    private async Task SyncColorAsync(string hex)
    {
        if (ParseColor(hex) is not { } color)
            return;
        var (r, g, b) = color;

        var targets = _devices.Where(d => d.HasRgb).ToList();
        if (targets.Count == 0)
        {
            StatusText.Text = Lang.T("status.syncNone");
            return;
        }

        SyncButton.IsEnabled = false;
        StatusText.Text = Lang.T("status.syncApplying", ColorText(r, g, b), targets.Count);
        var errors = await Task.WhenAll(targets.Select(d => SetColorAsync(d, r, g, b)));
        SyncButton.IsEnabled = true;
        ShowDevices();

        var failed = targets.Zip(errors).Where(x => x.Second != null).Select(x => $"{x.First.Name} ({x.Second})").ToList();
        StatusText.Text = failed.Count == 0
            ? Lang.T("status.syncDone", ColorText(r, g, b), targets.Count)
            : Lang.T("status.syncPartial", ColorText(r, g, b), targets.Count - failed.Count, targets.Count, string.Join(", ", failed));
    }

    // Pinta um aparelho pelo caminho que ele tiver. Devolve null se deu certo, ou o motivo da falha.
    private static async Task<string?> SetColorAsync(DeviceInfo device, byte r, byte g, byte b)
    {
        try
        {
            if (device.KeyboardLighting is { } current)
            {
                // Preto apaga (brilho 0). Outra cor: cor única, e volta o brilho se estava apagado.
                // Efeitos que não usam cor (Neon) trocam para o estático.
                var effect = MonsGeekKeyboard.Effects.FirstOrDefault(x => x.Id == current.Effect);
                var lighting = (r, g, b) == (0, 0, 0)
                    ? current with { Brightness = 0 }
                    : current with
                    {
                        R = r, G = g, B = b,
                        Multicolor = false,
                        Brightness = current.Brightness == 0 ? (byte)4 : current.Brightness,
                        Effect = effect is { UsesColor: false } ? (byte)1 : current.Effect,
                    };
                await Task.Run(() => MonsGeekKeyboard.SetLighting(lighting));
                device.KeyboardLighting = lighting;
                RememberLighting(device, lighting);
            }
            else if (device.Driver?.Controls.FirstOrDefault(c => c.Kind == DriverControlKind.Color) is { } colorControl)
            {
                await SendDriverAsync(device, colorControl, new Dictionary<string, int> { ["r"] = r, ["g"] = g, ["b"] = b });
            }
            else if (device.RgbLocation != null)
            {
                await Task.Run(() => OpenRgb.SetColor(device.RgbLocation, r, g, b));
                device.ReportedColor = ColorText(r, g, b);
            }
            else
            {
                return Lang.T("error.noRgb");
            }

            device.SavedColor = ColorText(r, g, b);
            UserSettings.Current.Remember(device, s => s.Color = device.SavedColor);
            return null;
        }
        catch (DriverException ex)
        {
            return Lang.T("error.nothingSent", ex.Message);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // "#FF8800" → (255, 136, 0). Se o texto não for uma cor, avisa no rodapé e devolve null.
    private (byte R, byte G, byte B)? ParseColor(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, null, out var rgb))
            return ((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

        StatusText.Text = Lang.T("status.invalidColor");
        return null;
    }

    private static string ColorText(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    private async void DriverChoice_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is DriverChoice choice)
            await ApplyDriverAsync(choice.Device, choice.Control, choice.Sets, Lang.T("status.valueSet", choice.Control.Label, choice.Label));
    }

    private async void DriverList_Changed(object? sender, SelectionChangedEventArgs e)
    {
        // Sem item removido = é só a tela mostrando a opção atual, não um clique.
        if (e.RemovedItems.Count > 0 && (sender as ComboBox)?.SelectedItem is DriverChoice { IsCurrent: false } choice)
            await ApplyDriverAsync(choice.Device, choice.Control, choice.Sets, Lang.T("status.valueSet", choice.Control.Label, choice.Label));
    }

    // Envia um comando do driver JSON e mostra o resultado no rodapé.
    private async Task ApplyDriverAsync(DeviceInfo device, DriverControl control, IReadOnlyDictionary<string, int> sets, string message)
    {
        try
        {
            await SendDriverAsync(device, control, sets);
            ShowDevices();
            StatusText.Text = Lang.T("status.forDevice", message, device.Name);
        }
        catch (DriverException ex)
        {
            StatusText.Text = Lang.T("status.nothingSent", ex.Message);
        }
        catch (Exception ex)
        {
            StatusText.Text = Lang.T("status.deviceFailed", ex.Message);
        }
    }

    // Os valores só mudam na tela se o envio deu certo.
    private static async Task SendDriverAsync(DeviceInfo device, DriverControl control, IReadOnlyDictionary<string, int> sets)
    {
        if (device.Driver is not { } driver)
            throw new DriverException(Lang.T("error.noDriver"));

        var values = new Dictionary<string, int>(device.DriverValues);
        foreach (var (key, value) in sets)
            values[key] = value;

        await Task.Run(() => DriverRunner.Send(driver, control, values, device));
        foreach (var (key, value) in sets)
            device.DriverValues[key] = value;
        UserSettings.Current.Remember(device, s =>
        {
            foreach (var (key, value) in sets)
                s.Values[key] = value;
        });
    }

    // Anota a iluminação do teclado Akko escolhida (o próprio teclado também guarda).
    private static void RememberLighting(DeviceInfo device, MonsGeekKeyboard.Lighting lighting) =>
        UserSettings.Current.Remember(device, s =>
        {
            s.Values["effect"] = lighting.Effect;
            s.Values["brightness"] = lighting.Brightness;
            s.Values["speed"] = lighting.Speed;
        });

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
            StatusText.Text = Lang.T("status.invalidDpi");
    }

    private async Task ApplyDpiAsync(DeviceInfo device, int dpi)
    {
        try
        {
            await Task.Run(() => LogitechHidpp.SetDpi(device.LogitechProductId, device.LogitechIndex, dpi));
            device.Dpi = dpi;
            UserSettings.Current.Remember(device, s => s.Dpi = dpi);
            ShowDevices(); // redesenha os cartões com o DPI novo
            StatusText.Text = Lang.T("status.forDevice", Lang.T("status.valueSet", "DPI", dpi), device.Name);
        }
        catch (Exception ex)
        {
            StatusText.Text = Lang.T("status.dpiFailed", ex.Message);
        }
    }

    private async void KeyboardEffect_Changed(object? sender, SelectionChangedEventArgs e)
    {
        // Sem item removido = é só a tela mostrando o efeito atual, não um clique.
        if (e.RemovedItems.Count == 0 || sender is not ComboBox { DataContext: DeviceInfo device, SelectedItem: MonsGeekKeyboard.Effect effect })
            return;
        if (device.KeyboardLighting is { } current && current.Effect != effect.Id)
            await ApplyKeyboardLightingAsync(device, current with { Effect = effect.Id, Option = effect.DefaultOption },
                Lang.T("status.valueSet", Lang.T("control.effect"), effect.Name));
    }

    private async void KeyboardBrightness_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is Choice { Device: { KeyboardLighting: { } current } device } choice)
            await ApplyKeyboardLightingAsync(device, current with { Brightness = (byte)choice.Value }, Lang.T("status.valueSet", Lang.T("control.brightness"), choice.Value));
    }

    private async void KeyboardSpeed_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is Choice { Device: { KeyboardLighting: { } current } device } choice)
            await ApplyKeyboardLightingAsync(device, current with { Speed = (byte)choice.Value }, Lang.T("status.valueSet", Lang.T("control.speed"), choice.Value));
    }

    private async Task ApplyKeyboardLightingAsync(DeviceInfo device, MonsGeekKeyboard.Lighting lighting, string message)
    {
        try
        {
            await Task.Run(() => MonsGeekKeyboard.SetLighting(lighting));
            device.KeyboardLighting = lighting;
            RememberLighting(device, lighting);
            ShowDevices();
            StatusText.Text = Lang.T("status.forDevice", message, device.Name);
        }
        catch (Exception ex)
        {
            StatusText.Text = Lang.T("status.lightingFailed", ex.Message);
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
            UserSettings.Current.Remember(device, s => s.ReportRateHz = hz);
            ShowDevices();
            StatusText.Text = Lang.T("status.forDevice", Lang.T("status.valueSet", Lang.T("control.pollingRate"), $"{hz} Hz"), device.Name);
        }
        catch (Exception ex)
        {
            StatusText.Text = Lang.T("status.rateFailed", ex.Message);
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

    // Troca entre português e inglês. Os textos da tela são lidos quando a janela é criada,
    // então abrimos uma janela nova no idioma novo, no mesmo lugar, e fechamos esta.
    private void Language_Click(object? sender, RoutedEventArgs e)
    {
        var next = Lang.Current == "pt-BR" ? "en" : "pt-BR";
        UserSettings.Current.Language = next;
        UserSettings.Current.Save();
        Lang.Load(next);

        var window = new MainWindow
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = Position,
            Width = Width,
            Height = Height,
            _openRgbWeStarted = _openRgbWeStarted,
        };
        _openRgbWeStarted = null; // o OpenRGB continua aberto: agora é a janela nova que cuida dele
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = window;
        window.Show();
        Close();
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private async void StartOpenRgb_Click(object? sender, RoutedEventArgs e)
    {
        var exe = OpenRgb.FindExe();
        if (exe == null)
            return;

        StartOpenRgbButton.IsEnabled = false;
        StatusText.Text = Lang.T("status.openingOpenRgb");
        try
        {
            _openRgbWeStarted = await OpenRgb.StartAsync(exe);
        }
        catch (Exception ex)
        {
            StatusText.Text = Lang.T("status.openRgbFailed", ex.Message);
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
