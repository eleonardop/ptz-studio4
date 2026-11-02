using System.Windows;
using System.Windows.Controls;
using PtzStudio.App.Engine;
using PtzStudio.App.Ui;
using PtzStudio.Core.Models;

namespace PtzStudio.App.Tabs;

/// <summary>
/// "Movimento": todos os ajustes numéricos de Pan, Tilt e Zoom (curva, zona morta, velocidades, suavidade) e os níveis de velocidade.
/// Reúne o que antes ficava espalhado entre as abas Sensibilidade e Zoom.
/// </summary>
internal sealed class MotionTab : TabBase
{
    public MotionTab(AppEngine e) : base(e) { Init(); }

    protected override void Build()
    {
        var m = E.Profile.Motion;
        var levels = Ux.V();
        for (int i = 0; i < m.SpeedLevels.Length; i++)
        {
            int idx = i;
            levels.Children.Add(Ux.SliderRow($"Nível {i + 1} (fração da velocidade máxima)", 0.05, 1.0, m.SpeedLevels[idx], v => { m.SpeedLevels[idx] = v; E.Save(); }));
        }
        levels.Children.Add(Ux.Labeled("Nível inicial do perfil",
            Ux.Combo(Enumerable.Range(1, m.SpeedLevels.Length).Select(i => $"Nível {i}").ToList(), m.DefaultSpeedLevel, i => { m.DefaultSpeedLevel = i; E.Save(); }, 160)));
        levels.Children.Add(Ux.Text("O botão “Alternar velocidade” percorre estes níveis. Eles multiplicam as velocidades de Pan, Tilt e Zoom.", 13, false, Ux.Muted));

        var reset = Ux.Btn("Restaurar padrões deste perfil", () =>
        {
            var r = MessageBox.Show(
                $"Voltar os ajustes de movimento do perfil “{E.Profile.Name}” aos valores de fábrica?\n\n" +
                "Pan, Tilt, Zoom e níveis de velocidade serão restaurados. Botões, eixos e presets não mudam.",
                "Restaurar padrões", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            E.RestoreMotionDefaults();
            Rebuild();
        }, 300);

        var how = Ux.Card(Ux.Text(
            "Como a velocidade é calculada: a inclinação do analógico é multiplicada pela sensibilidade, desconta-se a zona morta, aplica-se a curva " +
            "e o resultado vira uma velocidade entre a mínima e a máxima do eixo. Depois entra o nível de velocidade ativo e, por fim, o limite da câmera " +
            "(aba Câmera). Com a curva Progressiva, 10–20% de inclinação gera movimento muito lento e 100% chega à velocidade máxima configurada.", 13, false, Ux.Muted),
            "COMO OS AJUSTES SE COMBINAM");

        Content = Ux.Scroll(Ux.V(
            Ux.Wrap(reset),
            Ux.Group("Pan (horizontal)", new AxisSettingsPanel(m.Pan, 24, E.Save)),
            Ux.Group("Tilt (vertical)", new AxisSettingsPanel(m.Tilt, 20, E.Save)),
            Ux.Group("Zoom progressivo", Ux.V(
                new AxisSettingsPanel(m.Zoom, 8, E.Save),
                Ux.Text("O zoom usa velocidade variável do VISCA (8 níveis). Soltar o controle envia parada imediata, a menos que “Parada suave” esteja marcada.", 13, false, Ux.Muted))),
            Ux.Group("Níveis de velocidade", levels),
            how));
        Ux.WithHeader((ScrollViewer)Content, "Movimento",
            "Curva, zona morta, velocidade e suavidade de Pan, Tilt e Zoom. Os valores valem para o perfil ativo.");
    }
}
