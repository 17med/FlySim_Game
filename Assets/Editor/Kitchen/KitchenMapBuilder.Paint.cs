using UnityEngine;

// The paints every block in the house is coloured with. Each is made once per build, by name, and
// shared by every block that asks for that name.
public partial class KitchenMapBuilder
{
    // A toon material for furniture and fittings. The shader reads smoothness from an ORM map, so each
    // colour gets a 1x1 one; without it its white default means fully rough and fully metallic.
    Material Paint(string name, Color color, float smoothness = 0.2f)
    {
        if (materials.TryGetValue(name, out Material material))
            return material;
        material = new Material(toonShader) { name = "Blockout_" + name };
        material.SetColor("_BaseColor", color);
        material.SetTexture("_ORMMap", SolidTexture(name + "_ORM", new Color(1f, 1f - smoothness, 0f)));
        materials[name] = material;
        return material;
    }

    // Walls, floors, ceiling and trim: big flat surfaces look heavy with an outline, so theirs is off.
    Material PaintSurface(string name, Color color, float smoothness = 0.2f)
    {
        Material material = Paint(name, color, smoothness);
        material.SetFloat("_OutlineWidth", 0f);
        return material;
    }

    // A lamp shade or light fitting that looks switched on. URP Lit, because the toon shader has no emission.
    Material PaintGlow(string name, Color color)
    {
        if (materials.TryGetValue(name, out Material material))
            return material;
        material = new Material(litShader) { name = "Blockout_" + name };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.3f);
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", color * 0.8f);
        materials[name] = material;
        return material;
    }

    // A floor pattern from a tiny pixel grid repeated across the surface: the top face of a box is
    // one UV square, so tiling is how many times the grid fits along the floor.
    Material PaintPattern(string name, Color[] pixels, int width, Vector2 tiling)
    {
        var texture = new Texture2D(width, pixels.Length / width, TextureFormat.RGBA32, false)
        {
            name = "Blockout_" + name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat,
        };
        texture.SetPixels(pixels);
        texture.Apply();
        Material material = PaintSurface(name, Color.white, 0.35f);
        material.SetTexture("_BaseMap", texture);
        material.SetTextureScale("_BaseMap", tiling);
        return material;
    }

    // ORM data is linear, not a colour, so the texture skips sRGB conversion.
    static Texture2D SolidTexture(string name, Color value)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { name = "Blockout_" + name };
        texture.SetPixel(0, 0, value);
        texture.Apply();
        return texture;
    }
}
