using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PtzStudio.App.Engine;
using PtzStudio.App.Tabs;
using PtzStudio.App.Ui;
using PtzStudio.Core.Models;
using PtzStudio.Core.Visca;

namespace PtzStudio.App;

/// <summary>Janela principal: cabeçalho fixo (status, perfil, entrada, EMERGÊNCIA), faixa de emergência e navegação lateral.</summary>
public partial class MainWindow : Window
{
    private readonly AppEngine _engine = new();
    private readonly List<TabBase> _tabs = new();
    private readonly TextBlock _cam = new();
    private readonly TextBlock _pad = new();
    private readonly TextBlock _profile = new();
    private readonly TextBlock _source = new();
    private readonly TextBlock _notice = Ux.Text("", 13, false, Ux.Muted);
    private readonly Border _banner = new();
    private readonly TextBlock _bannerMsg = new();
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private string _lastNotice = "";
    private long _noticeTicks;   // quando o aviso apareceu (avisos somem sozinhos depois de alguns segundos)
    private string? _resumeMsg;
    private readonly TrayIcon _tray;
    private bool _exiting;
    private bool _trayTipShown;
    private bool _wasEmergency;
    private ConnectionState _wasCam = ConnectionState.Desconectado;
    private string _lastTip = "";

    public MainWindow()
    {
        InitializeComponent();
        Title = "PTZ Studio";
        Background = Ux.Bg;
        System.Windows.Documents.TextElement.SetForeground(this, Ux.TextBrush);
        var wa = SystemParameters.WorkArea;
        Width = Math.Min(Width, wa.Width - 20);
        Height = Math.Min(Height, wa.Height - 20);

        var tabs = new TabControl { FontSize = 14 };
        void Add(string icon, string header, TabBase tab)
        {
            _tabs.Add(tab);
            var h = new StackPanel { Orientation = Orientation.Horizontal };
            h.Children.Add(Ux.Icon(icon, 20, Ux.AccentLight, 12));
            h.Children.Add(new TextBlock { Text = header, VerticalAlignment = VerticalAlignment.Center });
            tabs.Items.Add(new TabItem { Header = h, Content = tab });
        }
        Add("ptz", "Controle PTZ", new PtzTab(_engine));
        Add("gamepad", "Mapeamento", new MappingTab(_engine));
        Add("stick", "Analógicos", new AnalogTab(_engine));
        Add("tune", "Movimento", new MotionTab(_engine));
        Add("camera", "Câmera", new CameraTab(_engine));
        Add("star", "Presets", new PresetsTab(_engine));
        Add("people", "Perfis", new ProfilesTab(_engine));
        Add("pulse", "Diagnóstico", new DiagnosticsTab(_engine));
        Add("save", "Backup", new BackupTab(_engine));

        tabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, tabs)) return; // ignora eventos vindos de ComboBox/ListBox dentro das abas
            // Reconstrói a aba ao abri-la: os mesmos ajustes aparecem em mais de uma tela e os valores precisam estar atualizados.
            if (tabs.SelectedItem is TabItem { Content: TabBase tb })
            {
                try { tb.Rebuild(); } catch { }
            }
        };

        // ---------- cabeçalho ----------
        var emergency = new Button
        {
            MinHeight = 50, MinWidth = 250, Margin = new Thickness(8, 0, 0, 0), Background = Ux.Danger, BorderBrush = Ux.Danger,
            Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 15,
            Content = Ux.H(Ux.Icon("warning", 22, Brushes.White),
                           new TextBlock { Text = "PARADA DE EMERGÊNCIA", VerticalAlignment = VerticalAlignment.Center })
        };
        emergency.Click += (_, _) => _engine.ActivateEmergency("botão de emergência (tela)");

        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 22, 0) };
        brand.Children.Add(new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Background = Ux.Accent, Margin = new Thickness(0, 0, 12, 0),
            Child = Ux.Icon("camera", 24, Brushes.White, 0)
        });
        var bt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        bt.Children.Add(new TextBlock { Text = "PTZ Studio", FontSize = 19, FontWeight = FontWeights.Bold, Foreground = Ux.TextBrush });
        bt.Children.Add(new TextBlock { Text = "Controle de câmera PTZ", FontSize = 11, Foreground = Ux.Muted });
        brand.Children.Add(bt);

        // WrapPanel: em janelas estreitas os indicadores descem para a linha de baixo em vez de serem cortados
        var chips = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        chips.Children.Add(Ux.Chip("camera", _cam, "CÂMERA"));
        chips.Children.Add(Ux.Chip("gamepad", _pad, "CONTROLE"));
        chips.Children.Add(Ux.Chip("people", _profile, "PERFIL"));
        chips.Children.Add(Ux.Chip("input", _source, "ENTRADA"));

        var header = new DockPanel { Margin = new Thickness(16, 10, 16, 10) };
        DockPanel.SetDock(emergency, Dock.Right);
        header.Children.Add(emergency);
        DockPanel.SetDock(brand, Dock.Left);
        header.Children.Add(brand);
        header.Children.Add(chips);
        var headerBar = new Border { Background = Ux.PanelBg, BorderBrush = Ux.Edge, BorderThickness = new Thickness(0, 0, 0, 1), Child = header };

        // ---------- faixa de emergência (só aparece com a emergência ativa) ----------
        var title = new TextBlock { Text = "EMERGÊNCIA ATIVADA", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
        _bannerMsg.FontSize = 13; _bannerMsg.Foreground = new SolidColorBrush(Color.FromRgb(0xFE, 0xCA, 0xCA)); _bannerMsg.TextWrapping = TextWrapping.Wrap;
        var bannerText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        bannerText.Children.Add(title); bannerText.Children.Add(_bannerMsg);
        var resume = new Button
        {
            Content = "RETOMAR CONTROLE", MinHeight = 52, MinWidth = 250, FontSize = 16, FontWeight = FontWeights.Bold,
            Background = Ux.Ok, BorderBrush = Ux.Ok, Foreground = Brushes.White, Margin = new Thickness(16, 0, 0, 0)
        };
        resume.Click += (_, _) => ResumeControl();
        var bannerRow = new DockPanel { Margin = new Thickness(20, 10, 20, 10) };
        DockPanel.SetDock(resume, Dock.Right);
        bannerRow.Children.Add(resume);
        bannerRow.Children.Add(bannerText);
        _banner.Background = Ux.DangerDim; _banner.BorderBrush = Ux.Danger; _banner.BorderThickness = new Thickness(0, 0, 0, 2);
        _banner.Child = bannerRow; _banner.Visibility = Visibility.Collapsed;

        var statusStrip = new Border
        {
            Background = Ux.PanelBg, BorderBrush = Ux.Edge, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 3, 16, 3), Child = _notice
        };

        var root = new DockPanel();
        var top = new StackPanel();
        top.Children.Add(headerBar); top.Children.Add(_banner);
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(statusStrip, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(statusStrip);
        root.Children.Add(tabs);
        Content = root;

        // ---------- ícone ao lado do relógio ----------
        _tray = new TrayIcon("PTZ Studio", () => _engine.EmergencyActive);
        _tray.OpenRequested += ShowFromTray;
        _tray.EmergencyRequested += () => _engine.ActivateEmergency("botão de emergência (bandeja do Windows)");
        _tray.ResumeRequested += () =>
        {
            ResumeControl();
            if (_resumeMsg != null) ShowFromTray();                       // não liberou: mostra a janela com o motivo
            else _tray.ShowBalloon("PTZ Studio", "Controle retomado.");
        };
        _tray.ExitRequested += () => { _exiting = true; Close(); };
        _tray.Show();
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized && _engine.Settings.MinimizeToTray) HideToTray(false);
        };
        Application.Current.SessionEnding += (_, _) => _exiting = true;    // não impedir o Windows de desligar

        _engine.Notice += m => { _lastNotice = m; Interlocked.Exchange(ref _noticeTicks, DateTime.UtcNow.Ticks); };
        _engine.ProfileChanged += RebuildTabs;
        _engine.SettingsReplaced += RebuildTabs;

        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();

        SourceInitialized += (_, _) => _engine.Start(new WindowInteropHelper(this).Handle);
        Closing += OnClosing;
        UpdateStatus();
    }

    /// <summary>Esconde a janela (sai da barra de tarefas). O controle da câmera, o joystick e os presets seguem funcionando.</summary>
    private void HideToTray(bool fromClose)
    {
        Hide();
        _statusTimer.Interval = TimeSpan.FromSeconds(1);     // escondida: só o ícone precisa de atualização
        if (_trayTipShown) return;
        _trayTipShown = true;
        _tray.ShowBalloon("PTZ Studio continua ativo",
            fromClose ? "O programa segue controlando a câmera ao lado do relógio. Para sair de verdade: botão direito no ícone > Sair."
                      : "Minimizado ao lado do relógio. Clique no ícone para abrir.");
    }

    /// <summary>Traz a janela de volta (clique no ícone, menu ou segunda cópia do programa).</summary>
    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Topmost = true; Topmost = false;                      // garante que apareça na frente
        Activate();
        _statusTimer.Interval = TimeSpan.FromMilliseconds(250);
        UpdateStatus();
    }

    private void ResumeControl()
    {
        if (_engine.TryResumeControl(out string reason)) { _resumeMsg = null; _lastNotice = "Controle retomado."; }
        else _resumeMsg = reason;
        UpdateStatus();
    }

    private void RebuildTabs()
    {
        foreach (var t in _tabs)
        {
            try { t.Rebuild(); } catch { }
        }
    }

    private void UpdateStatus()
    {
        var st = _engine.Client.State;
        (_cam.Text, _cam.Foreground) = st switch
        {
            ConnectionState.Conectado => ("Conectada", Ux.Ok),
            ConnectionState.Conectando => ("Conectando…", Ux.Warn),
            ConnectionState.ErroDeComunicacao => ("Erro de comunicação", Ux.Danger),
            _ => ("Desconectada", Ux.Danger)
        };

        if (_engine.Pad.Connected) { _pad.Text = _engine.Pad.Name ?? "Conectado"; _pad.Foreground = Ux.Ok; }
        else { _pad.Text = "Não detectado"; _pad.Foreground = Ux.Warn; }

        _profile.Text = _engine.Settings.ActiveProfile; _profile.Foreground = Ux.TextBrush;
        _source.Text = _engine.Controller.ActiveSource == InputSource.Fisico ? "Joystick físico" : "Joystick virtual";
        _source.Foreground = Ux.AccentLight;
        if (_lastNotice.Length > 0 && (DateTime.UtcNow.Ticks - Interlocked.Read(ref _noticeTicks)) > TimeSpan.FromSeconds(8).Ticks) _lastNotice = "";
        _notice.Text = _lastNotice;

        bool emergency = _engine.EmergencyActive;
        _banner.Visibility = emergency ? Visibility.Visible : Visibility.Collapsed;
        if (emergency)
        {
            _bannerMsg.Text = _resumeMsg ??
                "Todos os comandos estão BLOQUEADOS (controle, joystick da tela, botões e presets). " +
                "Se a rede estiver fora, a câmera pode não ter recebido a parada. Para liberar: analógicos no centro, nenhum botão pressionado e câmera conectada.";
        }
        else _resumeMsg = null;

        // ícone ao lado do relógio: dica de texto e avisos quando a janela está escondida
        string tip = (emergency ? "EMERGÊNCIA ATIVADA — " : "") + $"PTZ Studio · Câmera: {_cam.Text} · Controle: {_pad.Text}";
        if (tip != _lastTip) { _lastTip = tip; _tray.SetTip(tip); }
        if (!IsVisible)
        {
            if (emergency && !_wasEmergency) _tray.ShowBalloon("EMERGÊNCIA ATIVADA", "Comandos bloqueados. Abra o programa e clique em RETOMAR CONTROLE.");
            if (_wasCam == ConnectionState.Conectado && (st == ConnectionState.Desconectado || st == ConnectionState.ErroDeComunicacao))
                _tray.ShowBalloon("Câmera sem resposta", "A conexão com a câmera foi perdida. O programa tenta reconectar sozinho.");
        }
        _wasEmergency = emergency;
        _wasCam = st;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_exiting && _engine.Settings.CloseToTray)
        {
            e.Cancel = true;                 // o X só esconde: fechar sem querer no meio da transmissão seria pior
            HideToTray(true);
            return;
        }
        _statusTimer.Stop();
        try { _tray.Dispose(); } catch { }
        try { _engine.Dispose(); } catch { }
    }
}
