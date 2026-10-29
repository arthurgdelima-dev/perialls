using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Perialls;

public partial class MainWindow : Window
{
    private List<DeviceInfo> _devices = [];
    private readonly HashSet<string> _hidden = HiddenDevices.Load();
    private readonly Dictionary<string, string> _names = DeviceNames.Load();

    // Se fomos nós que abrimos o OpenRGB, fechamos ele junto com o app (nada fica rodando).
    private Process? _openRgbWeStarted;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) => StopOpenRgbIfWeStartedIt();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    // Lê os aparelhos uma vez. Não há nada rodando sozinho: só atualiza ao abrir ou ao clicar.
    private async Task RefreshAsync()
    {
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Procurando aparelhos...";
        try
        {
            // A busca roda fora da tela para a janela não "congelar" enquanto o Windows responde.
            var result = await Task.Run(DeviceScanner.Scan);
            _devices = result.Devices;
            foreach (var d in _devices)
            {
                d.IsHidden = _hidden.Contains(d.Key);
                d.Photo = DevicePhotos.Find(d);
                d.OriginalName = d.Name;
                if (_names.TryGetValue(d.Key, out var custom))
                    d.Name = custom;
            }
            ShowDevices();

            var hiddenCount = _devices.Count(d => d.IsHidden);
            StatusText.Text = $"{_devices.Count - hiddenCount} aparelhos"
                + (hiddenCount > 0 ? $" ({hiddenCount} ocultos)" : "")
                + $" · atualizado às {DateTime.Now:HH:mm:ss}";

            if (!result.OpenRgbRunning)
            {
                StatusText.Text += "\nRGB indisponível: abra o OpenRGB com o SDK Server ligado.";
                StartOpenRgbButton.Visibility = OpenRgb.FindExe() != null ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                StartOpenRgbButton.Visibility = Visibility.Collapsed;
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

    private void ShowHidden_Changed(object sender, RoutedEventArgs e) => ShowDevices();

    private void ToggleHidden_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DeviceInfo device)
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
    private void DeviceName_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || (sender as FrameworkElement)?.DataContext is not DeviceInfo device)
            return;

        e.Handled = true; // não deixa o clique virar "arrastar a janela"
        device.IsRenaming = true;
        ShowDevices();
    }

    private void RenameBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: DeviceInfo { IsRenaming: true } } box)
        {
            box.Focus();
            box.SelectAll();
        }
    }

    // Enter salva, Esc cancela. Nome vazio volta para o nome original.
    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: DeviceInfo device } box || e.Key is not (Key.Enter or Key.Escape))
            return;

        if (e.Key == Key.Enter)
        {
            var name = box.Text.Trim();
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

        device.IsRenaming = false;
        ShowDevices();
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: DeviceInfo { IsRenaming: true } device })
        {
            device.IsRenaming = false;
            ShowDevices();
        }
    }

    private async void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DeviceInfo device, DataContext: string hex })
            await ApplyColorAsync(device, hex);
    }

    private async void CustomColor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox { DataContext: DeviceInfo device } box)
            await ApplyColorAsync(device, box.Text);
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

    private async void DpiPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DeviceInfo device, DataContext: int dpi })
            await ApplyDpiAsync(device, dpi);
    }

    private async void DpiCustom_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox { DataContext: DeviceInfo device } box)
            return;

        if (int.TryParse(box.Text.Trim(), out var dpi) && dpi >= 50 && dpi <= 50000)
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

    private async void KeyboardEffect_Changed(object sender, SelectionChangedEventArgs e)
    {
        // Sem item removido = é só a tela mostrando o efeito atual, não um clique.
        if (e.RemovedItems.Count == 0 || sender is not ComboBox { DataContext: DeviceInfo device, SelectedItem: MonsGeekKeyboard.Effect effect })
            return;
        if (device.KeyboardLighting is { } current && current.Effect != effect.Id)
            await ApplyKeyboardLightingAsync(device, current with { Effect = effect.Id, Option = effect.DefaultOption },
                $"Efeito {effect.Name} aplicado");
    }

    private async void KeyboardBrightness_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DeviceInfo { KeyboardLighting: { } current } device, DataContext: int level })
            await ApplyKeyboardLightingAsync(device, current with { Brightness = (byte)level }, $"Brilho {level} aplicado");
    }

    private async void KeyboardSpeed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DeviceInfo { KeyboardLighting: { } current } device, DataContext: int level })
            await ApplyKeyboardLightingAsync(device, current with { Speed = (byte)level }, $"Velocidade {level} aplicada");
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

    private async void ReportRate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DeviceInfo device, DataContext: int hz })
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
    private void Cards_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = CardsScroll.ActualWidth - SystemParameters.VerticalScrollBarWidth;
        var columns = Math.Max(1, (int)(width / 240));
        DeviceList.Tag = Math.Floor(width / columns);
    }

    // Janela sem a barra do Windows: arrastar por qualquer área vazia, e botões próprios.
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void StartOpenRgb_Click(object sender, RoutedEventArgs e)
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
