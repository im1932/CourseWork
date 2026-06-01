using UnityEngine;

public readonly struct PatternPoint
{
    public readonly Vector2 position;
    public readonly float scale;
    public readonly float opacity;

    public PatternPoint(float x, float y, float scale, float opacity)
    {
        position = new Vector2(x, y);
        this.scale = scale;
        this.opacity = opacity;
    }
}

public static class DefaultPattern
{
    public static readonly PatternPoint[] Points =
    {
        new PatternPoint(0.5f, 1f - 0.066f, 0.7f / 1.5f, 0.2f),
        new PatternPoint(0.177f, 1f - 0.168f, 0.7f / 1.5f, 0.2f),
        new PatternPoint(0.822f, 1f - 0.168f, 0.7f / 1.5f, 0.2f),
        new PatternPoint(0.37f, 1f - 0.168f, 0.85f / 1.5f, 0.3f),
        new PatternPoint(0.63f, 1f - 0.168f, 0.85f / 1.5f, 0.3f),
        new PatternPoint(0.277f, 1f - 0.308f, 0.7f / 1.5f, 0.3f),
        new PatternPoint(0.723f, 1f - 0.308f, 0.7f / 1.5f, 0.3f),
        new PatternPoint(0.13f, 1f - 0.42f, 0.7f / 1.5f, 0.2f),
        new PatternPoint(0.87f, 1f - 0.42f, 0.7f / 1.5f, 0.2f),
        new PatternPoint(0.27f, 1f - 0.533f, 0.85f / 1.5f, 0.3f),
        new PatternPoint(0.73f, 1f - 0.533f, 0.85f / 1.5f, 0.3f),
        new PatternPoint(0.2f, 1f - 0.73f, 0.7f / 1.5f, 0.2f),
        new PatternPoint(0.8f, 1f - 0.73f, 0.7f / 1.5f, 0.2f),
        new PatternPoint(0.302f, 1f - 0.825f, 0.85f / 1.5f, 0.3f),
        new PatternPoint(0.698f, 1f - 0.825f, 0.85f / 1.5f, 0.3f),
        new PatternPoint(0.5f, 1f - 0.876f, 0.85f / 1.5f, 0.2f),
        new PatternPoint(0.144f, 1f - 0.936f, 0.7f / 1.5f, 0.2f),
        new PatternPoint(0.856f, 1f - 0.936f, 0.7f / 1.5f, 0.2f)
    };
}

public static class InventoryPattern
{
    public static readonly PatternPoint[] Points =
    {
        new PatternPoint(0.495f, 1f - 0.210f, 0.39f, 0.20f),
        new PatternPoint(0.648f, 1f - 0.276f, 0.50f, 0.20f),
        new PatternPoint(0.746f, 1f - 0.386f, 0.39f, 0.20f),
        new PatternPoint(0.794f, 1f - 0.541f, 0.50f, 0.20f),
        new PatternPoint(0.672f, 1f - 0.631f, 0.46f, 0.20f),
        new PatternPoint(0.500f, 1f - 0.678f, 0.42f, 0.20f),
        new PatternPoint(0.339f, 1f - 0.631f, 0.44f, 0.20f),
        new PatternPoint(0.206f, 1f - 0.541f, 0.50f, 0.20f),
        new PatternPoint(0.253f, 1f - 0.388f, 0.39f, 0.20f),
        new PatternPoint(0.348f, 1f - 0.276f, 0.50f, 0.20f),
        new PatternPoint(0.742f, 1f - 0.778f, 0.46f, 0.20f),
        new PatternPoint(0.269f, 1f - 0.767f, 0.46f, 0.20f)
    };
}
