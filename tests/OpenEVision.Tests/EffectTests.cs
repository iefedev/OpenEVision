using OpenEVision.Device;
using OpenEVision.Effects;
using OpenEVision.Effects.Custom;
using Xunit;

namespace OpenEVision.Tests;

public class EffectTests
{
    [Fact]
    public void Every_builtin_effect_renders_values_in_range()
    {
        foreach (var info in EffectEngine.All.Where(e => e.Name is not ("audio" or "typing" or "system")))
        {
            using var fx = info.Create(new Ctx(), EffectEngine.Defaults(info));
            var rgb = new float[Ctx.N * 3];
            for (int f = 0; f < 10; f++) fx.Render(f * 0.04, 0.04, rgb);
            Assert.All(rgb, v => Assert.InRange(v, 0f, 1f));
        }
    }

    [Fact]
    public void Presets_only_use_known_layer_kinds()
    {
        foreach (var d in CustomPresets.Create())
            Assert.All(d.Layers, l => Assert.True(LayerKinds.ByName.ContainsKey(l.Kind), l.Kind));
    }

    /// <summary>White base + "key press" layer with Erase: the pressed key goes dark, then comes back after the duration.</summary>
    [Fact]
    public void Erase_layer_darkens_only_the_pressed_key_for_its_duration()
    {
        var def = new CustomEffectDef
        {
            Layers =
            [
                new() { Kind = "solid", Params = new() { ["color"] = LayerKinds.ToJson("ffffff") } },
                new() { Kind = "keypress", Blend = Blend.Erase, Params = new() { ["duration"] = LayerKinds.ToJson(0.5) } },
            ],
        };
        using var fx = new CustomEffect(new Ctx(), def);
        var rgb = new float[Ctx.N * 3];
        int q = KeyLayout.CustomLed["KeyQ"], w = KeyLayout.CustomLed["KeyW"];
        fx.Render(0, 0.04, rgb);
        Assert.True(rgb[q * 3] > 0.99);
        KeyEvents.Simulate("KeyQ");
        fx.Render(0.04, 0.04, rgb);
        Assert.True(rgb[q * 3] < 0.2);
        Assert.True(rgb[w * 3] > 0.99);
        Thread.Sleep(600);
        fx.Render(0.7, 0.04, rgb);
        Assert.True(rgb[q * 3] > 0.99);
    }
}
