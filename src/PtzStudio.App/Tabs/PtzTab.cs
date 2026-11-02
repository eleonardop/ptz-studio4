using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PtzStudio.App.Engine;
using PtzStudio.App.Ui;
using PtzStudio.Core.Models;

namespace PtzStudio.App.Tabs;

internal sealed class PtzTab : TabBase
{
    private VirtualJoystick _joy = new();
    private readonly TextBlock _info = Ux.Text("", 13, false, Ux.Muted);
    private readonly TextBlock _speeds = Ux.Text("", 15, true);
    private double _zoomIntensity = 0.5;
    private readonly List<Button> _levelBtns = new();
    private readonly List<RadioButton> _srcBtns = new();

    public PtzTab(AppEngine e) : base(e) { Init(); }

    private static Button Hold(string glyph, string text, Action down, Action up)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new TextBlock { Text = glyph, FontSize = 30, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = text, FontSize = 17, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Content = content, MinHeight = 84, MinWidth = 220, Margin = new Thickness(0, 0, 0, 10), HorizontalAlignment = HorizontalAlignment.Stretch };
        b.PreviewMouseLeftButtonDown += (_, _) => { b.CaptureMouse(); down(); };
        b.PreviewMouseLeftButtonUp += (_, _) => { b.ReleaseMouseCapture(); up(); };
        b.LostMouseCapture += (_, _) => up();
        return b;
    }

    protected override void Build()
    {
        _levelBtns.Clear(); _srcBtns.Clear();

        // ---------- Pan / Tilt ----------
        _joy = new VirtualJoystick { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 12) };
        _joy.ValueChanged += (x, y) => { E.VirtualX = x; E.VirtualY = y; };
        var panTilt = Ux.Card(Ux.V(
            _joy,
            Ux.Btn("CENTRALIZAR JOYSTICK", () => { _joy.Release(); E.VirtualX = E.VirtualY = 0; }, 240),
            Ux.Text("Quanto mais longe do centro, mais rápido. Usa a sensibilidade, a curva e a zona morta do perfil ativo.", 12, false, Ux.Muted)),
            "PAN / TILT  ·  JOYSTICK VIRTUAL");

        // ---------- Zoom ----------
        var zoomIn = Hold("+", "Aproximar", () => E.VirtualZ = _zoomIntensity, () => E.VirtualZ = 0);
        var zoomOut = Hold("−", "Afastar", () => E.VirtualZ = -_zoomIntensity, () => E.VirtualZ = 0);
        var zoom = Ux.Card(Ux.V(
            zoomIn, zoomOut,
            Ux.SliderRow("Intensidade", 0.1, 1.0, _zoomIntensity, v => _zoomIntensity = v, "0.00", true),
            Ux.Text("Segure o botão: ao soltar, o zoom para na hora.", 12, false, Ux.Muted)),
            "ZOOM");

        // ---------- Entrada ativa ----------
        var phys = new RadioButton { Content = "Joystick físico (GameSir)", GroupName = "entrada", FontSize = 14 };
        var virt = new RadioButton { Content = "Joystick virtual (mouse)", GroupName = "entrada", FontSize = 14 };
        _srcBtns.Add(phys); _srcBtns.Add(virt);
        phys.IsChecked = E.Controller.ActiveSource == InputSource.Fisico;
        virt.IsChecked = E.Controller.ActiveSource == InputSource.Virtual;
        phys.Checked += (_, _) => { if (E.Controller.ActiveSource != InputSource.Fisico) E.SetSource(InputSource.Fisico); };
        virt.Checked += (_, _) => { if (E.Controller.ActiveSource != InputSource.Virtual) E.SetSource(InputSource.Virtual); };
        var source = Ux.Card(Ux.V(Ux.Wrap(phys, virt),
            Ux.Text("Só uma entrada controla a câmera por vez. Trocar de entrada para o movimento na hora.", 12, false, Ux.Muted)), "ENTRADA ATIVA");

        var leftCol = new StackPanel();
        leftCol.Children.Add(panTilt);
        var midCol = new StackPanel();
        midCol.Children.Add(zoom); midCol.Children.Add(source);

        // ---------- Velocidade ----------
        var levels = E.Profile.Motion.SpeedLevels;
        var speedBtns = new WrapPanel();
        for (int i = 0; i < levels.Length; i++)
        {
            int idx = i;
            var bt = Ux.Btn($"Nível {i + 1}  ({levels[i]:P0})", () => { E.Controller.SpeedLevelIndex = idx; }, 110);
            _levelBtns.Add(bt);
            speedBtns.Children.Add(bt);
        }
        var speed = Ux.Card(Ux.V(speedBtns, _speeds, _info), "VELOCIDADE");

        // ---------- Presets rápidos (mesma lógica de sempre: acionar o slot da câmera) ----------
        var grid = new UniformGrid { Columns = 2 };
        foreach (var p in E.Settings.Presets.Where(x => x.CameraId == E.Camera.Id))
        {
            int slot = p.Slot;
            var face = new StackPanel();
            face.Children.Add(new TextBlock { Text = p.Slot.ToString(), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Ux.AccentLight });
            face.Children.Add(new TextBlock { Text = p.Name, FontSize = 12, Foreground = Ux.Muted, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 120 });
            var btn = new Button { Content = face, MinHeight = 66, Margin = new Thickness(3), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 8, 10, 8) };
            btn.Click += (_, _) => E.Controller.RecallPreset(slot);
            btn.ToolTip = p.ConfirmedOnCamera ? "Gravado na memória da câmera" : "Gravação ainda sem confirmação da câmera";
            grid.Children.Add(btn);
        }
        UIElement presetBody = grid.Children.Count > 0
            ? grid
            : Ux.Text("Nenhum preset cadastrado. Grave posições na aba Presets.", 13, false, Ux.Muted);
        var presets = Ux.Card(Ux.V(presetBody, Ux.Btn("Posição inicial (Home)", () => E.Controller.Home(), 220)), "PRESETS RÁPIDOS");

        var rightCol = new StackPanel();
        rightCol.Children.Add(speed); rightCol.Children.Add(presets);

        // Três colunas de largura fixa dentro de um WrapPanel: em janelas estreitas a última coluna desce para a linha de baixo
        // (antes as colunas tinham largura mínima e a janela cortava o conteúdo).
        leftCol.Width = 340; midCol.Width = 290; rightCol.Width = 360;
        var g = new WrapPanel { Margin = new Thickness(22, 18, 10, 6) };
        g.Children.Add(leftCol); g.Children.Add(midCol); g.Children.Add(rightCol);

        var page = new StackPanel();
        page.Children.Add(Ux.Header("Controle PTZ", "Opere a câmera com o joystick virtual, o zoom e os presets. A parada de emergência fica sempre no topo da janela."));
        page.Children.Add(g);
        Content = Ux.Scroll(page);
    }

    protected override void Refresh()
    {
        var c = E.Controller;
        var lv = E.Profile.Motion.SpeedLevels;
        int li = Math.Clamp(c.SpeedLevelIndex, 0, Math.Max(0, lv.Length - 1));
        for (int i = 0; i < _levelBtns.Count; i++)
        {
            bool on = i == li;
            _levelBtns[i].Background = on ? Ux.Accent : Ux.Raised;
            _levelBtns[i].BorderBrush = on ? Ux.Accent : Ux.BorderStrong;
            _levelBtns[i].Foreground = on ? System.Windows.Media.Brushes.White : Ux.TextBrush;
        }
        _speeds.Text = $"Enviado agora →  Pan {c.SentPan:+0;-0;0}    Tilt {c.SentTilt:+0;-0;0}    Zoom {c.SentZoom:+0;-0;0}";
        _info.Text = $"Perfil: {E.Settings.ActiveProfile}   ·   nível de velocidade {li + 1} de {lv.Length}" +
                     (c.EmergencyActive ? "\nEMERGÊNCIA ATIVADA: comandos bloqueados até RETOMAR CONTROLE." : "") +
                     (!c.EmergencyActive && c.EmergencyLatched ? "\nParada de segurança: leve os controles ao neutro para continuar." : "") +
                     (c.ActiveSource != InputSource.Virtual ? "\nJoystick e zoom da TELA estão desativados: escolha “Joystick virtual (mouse)” em Entrada ativa." : "");
        if (_srcBtns.Count == 2)
        {
            bool virt = c.ActiveSource == InputSource.Virtual;
            if (_srcBtns[0].IsChecked == virt) _srcBtns[0].IsChecked = !virt;
            if (_srcBtns[1].IsChecked != virt) _srcBtns[1].IsChecked = virt;
        }
    }
}
